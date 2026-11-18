using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PhotoBook.Application.Sites.Books;
using PhotoBook.Application.Sites.Books.Files;
using PhotoBook.Application.Sites.Upload;

namespace PhotoBook.Presentation.ViewModels
{
    /// <summary>The file half of the order page: what is wrong with the files, book by book.</summary>
    /// <remarks>
    /// <para>
    /// The check lives on the page the order is assembled on, next to the button that sends it:
    /// it is the same question, and a second screen between the two would ask the customer to
    /// leave the order to hear the answer.
    /// </para>
    /// <para>
    /// The check runs by itself once the plan exists, because a list of books that says nothing
    /// about their files is a list the customer has to guess from. A run started before the files
    /// were read would upload pages the site is known to refuse, so the button waits for it.
    /// </para>
    /// <para>
    /// A problem that only needs a yes is not hidden: the customer ticks the box, the button
    /// comes on, and the line stays in the list. Nothing here decides on the customer's behalf
    /// that a too-wide file is fine.
    /// </para>
    /// </remarks>
    public sealed partial class SiteOrderViewModel
    {
        private OrderFileCheck? _files;
        private bool _checkingFiles;
        private bool _confirmationsAccepted;
        private string _filesText = string.Empty;
        private CancellationTokenSource? _cancelFiles;
        private Task _filesRun = Task.CompletedTask;

        /// <summary>
        /// The reading the page has started, so a reader can wait for the answer.
        /// </summary>
        public Task FileCheck => _filesRun;

        /// <summary>The files as the check found them; null until it has run.</summary>
        public OrderFileCheck? Files
        {
            get => _files;
            private set
            {
                if (!SetProperty(ref _files, value))
                    return;

                OnPropertyChanged(nameof(HasFiles));
                OnPropertyChanged(nameof(NeedsConfirmation));
                OnPropertyChanged(nameof(CanUpload));
                OnPropertyChanged(nameof(FileLines));
                OnPropertyChanged(nameof(HasFileLines));

                ApplyCheck();
            }
        }

        /// <summary>Whether the files have been read at all.</summary>
        public bool HasFiles => _files is not null;

        /// <summary>The words that belong to the order rather than to one book.</summary>
        public IReadOnlyList<string> FileLines =>
            _files is null
                ? Array.Empty<string>()
                : _files.OrderAlerts.Select(alert => alert.Message).ToList();

        /// <summary>Whether the order itself has something the books do not.</summary>
        public bool HasFileLines => FileLines.Count > 0;

        /// <summary>Whether the customer has to accept something before the button comes on.</summary>
        public bool NeedsConfirmation => _files is { ConfirmationCount: > 0 };

        /// <summary>Whether the customer has said yes to the files the site may trim.</summary>
        public bool ConfirmationsAccepted
        {
            get => _confirmationsAccepted;
            set
            {
                if (!SetProperty(ref _confirmationsAccepted, value))
                    return;

                OnPropertyChanged(nameof(CanUpload));
            }
        }

        /// <summary>Whether the files are being read right now.</summary>
        public bool IsCheckingFiles
        {
            get => _checkingFiles;
            private set
            {
                if (!SetProperty(ref _checkingFiles, value))
                    return;

                OnPropertyChanged(nameof(CanUpload));
                OnPropertyChanged(nameof(CheckingText));

                // A book that was being read turns into «Готова» or a reason it is not.
                ApplyCheck();
            }
        }

        /// <summary>What the page says about the reading itself.</summary>
        public string CheckingText => _checkingFiles ? "Читаем файлы книги." : string.Empty;

        /// <summary>Whether there is a line about the check itself.</summary>
        public bool HasFilesText => _filesText.Length > 0;

        /// <summary>What the page says about the files once they have been read.</summary>
        public string FilesText
        {
            get => _filesText;
            private set
            {
                if (SetProperty(ref _filesText, value ?? string.Empty))
                    OnPropertyChanged(nameof(HasFilesText));
            }
        }

        /// <summary>
        /// Whether the files as they stand may be sent: read, without a refusal, and with the
        /// questions answered.
        /// </summary>
        private bool FilesAllow => !_checkingFiles &&
            _files is { CanSend: true } &&
            (!NeedsConfirmation || _confirmationsAccepted);

        /// <summary>
        /// Reads every page of the plan and says what is wrong with it.
        /// </summary>
        /// <remarks>
        /// A failure to read the files is not an empty check: the order is reported as blocked,
        /// because a button that came on after a failed read would send what nobody looked at.
        /// </remarks>
        private async Task CheckFilesAsync(CancellationToken cancellationToken = default)
        {
            if (_fileCheck is null || _sessions is null || IsCheckingFiles)
                return;

            if (_upload.Offered is not { } plan || plan.Groups.Count == 0)
                return;

            var session = _sessions.Load();
            if (session is null)
            {
                // Nothing to check against: the catalogue's sizes come with the session.
                Message = "Файлы будут проверены после входа на сайт.";
                return;
            }

            using var cancel = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _cancelFiles = cancel;

            // A new reading of the files is a new question, so an old yes no longer answers it.
            ConfirmationsAccepted = false;
            IsCheckingFiles = true;
            FilesText = "Читаем файлы книги.";

            try
            {
                // Reported inline rather than through Progress<T>: that one posts to the
                // dispatcher, so the last "проверяем книгу" line would land after the answer and
                // overwrite it. The awaits here already resume on the window's thread.
                var progress = new InlineProgress<FileCheckProgress>(report =>
                {
                    FilesText = report.BookNumber == 0
                        ? "Читаем файлы книги."
                        : "Проверяем книгу " + report.BookNumber + " из " + report.BooksTotal + ".";
                });

                var check = await _fileCheck
                    .CheckAsync(plan.Groups, progress, cancel.Token)
                    .ConfigureAwait(true);

                Files = check;
                FilesText = Describe(check);
            }
            catch (OperationCanceledException) when (cancel.IsCancellationRequested)
            {
                Files = null;
                FilesText = "Проверка файлов остановлена.";
            }
            catch (Exception error)
            {
                Files = new OrderFileCheck(
                    Array.Empty<CheckedBook>(),
                    new[]
                    {
                        new FileAlert(FileAlertLevel.Blocking, null, "Файлы не проверены: " + error.Message)
                    });

                FilesText = "Проверка файлов не удалась.";
            }
            finally
            {
                _cancelFiles = null;
                IsCheckingFiles = false;
            }
        }

        /// <summary>Stops the reading of the files; nothing was sent by it.</summary>
        public void CancelCheck() => _cancelFiles?.Cancel();

        /// <summary>Gives every book the status the check found for it.</summary>
        private void ApplyCheck()
        {
            var checkedBooks = new Dictionary<int, CheckedBook>();
            foreach (var book in _files?.Books ?? Array.Empty<CheckedBook>())
                checkedBooks[book.Number] = book;

            var groups = _upload.Offered?.Groups ?? Array.Empty<UploadGroup>();
            var number = 0;

            foreach (var group in groups)
            {
                foreach (var book in group.Group.Books)
                {
                    number++;
                    if (number > Books.Count)
                        return;

                    checkedBooks.TryGetValue(number, out var checkedBook);
                    Books[number - 1].ShowCheck(RowOf(number, book, checkedBook));
                }
            }

            // Books the plan left out keep the page's own words rather than a status nobody read.
            for (var index = number; index < Books.Count; index++)
                Books[index].ClearCheck();
        }

        /// <summary>One book with its status and the files it would send.</summary>
        private UploadBookRow RowOf(int number, Book book, CheckedBook? check)
        {
            var status = BookStatus.Of(book, check, IsCheckingFiles);
            var files = new List<UploadFileRow>();

            if (book.CoverFile is { } cover)
                files.Add(FileRowOf("Обложка", null, cover, check));

            var spreads = book.Files ?? (IReadOnlyList<PageFile>)Array.Empty<PageFile>();
            for (var index = 0; index < spreads.Count; index++)
                files.Add(FileRowOf("Разворот " + (index + 1), index + 1, spreads[index], check));

            return new UploadBookRow(number, check?.Name ?? "Книга " + number, status, files);
        }

        /// <summary>One file, with the site's own complaint about it when there is one.</summary>
        private static UploadFileRow FileRowOf(string title, int? position, PageFile file, CheckedBook? check)
        {
            var alert = check?.Alerts.FirstOrDefault(item => item.PageNumber == position);

            return new UploadFileRow(
                title,
                System.IO.Path.GetFileName(file.Path),
                alert?.Message,
                alert?.Level);
        }

        /// <summary>The one line under the books once the files are read.</summary>
        private static string Describe(OrderFileCheck check)
        {
            var books = check.Books.Count == 1 ? "1 книга" : check.Books.Count + " кн.";

            if (check.BlockingCount > 0)
                return "Файлы: " + books + ", " + check.BlockingCount + " без отправки — исправьте отмеченное.";

            if (check.ConfirmationCount > 0)
                return "Файлы: " + books + ", " + check.ConfirmationCount + " вопрос(а) — ответьте ниже.";

            return "Файлы: " + books + ", всё совпадает с тем, что просит сайт.";
        }

        /// <summary>Forgets the previous reading, so the button waits for a new one.</summary>
        private void ForgetFiles()
        {
            Files = null;
            FilesText = string.Empty;
            ConfirmationsAccepted = false;
            ApplyCheck();
        }

        /// <summary>Runs the report where it is made, in the order the work happens.</summary>
        private sealed class InlineProgress<T> : IProgress<T>
        {
            private readonly Action<T> _report;

            public InlineProgress(Action<T> report) => _report = report;

            public void Report(T value) => _report(value);
        }
    }
}
