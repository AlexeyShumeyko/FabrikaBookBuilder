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
    /// <summary>The preparation half of the upload screen: what is wrong with the files.</summary>
    /// <remarks>
    /// <para>
    /// The check is on this screen rather than its own, because it is the same question the
    /// button asks: the customer is about to send these files, so the answer belongs next to the
    /// button that sends them. The screen waits for it: a run started before the files were read
    /// would upload pages the site is known to refuse.
    /// </para>
    /// <para>
    /// A problem that only needs a yes is not hidden: the customer ticks the box, the button
    /// comes on, and the line stays in the list. Nothing here decides on the customer's behalf
    /// that a too-wide file is fine.
    /// </para>
    /// </remarks>
    public sealed partial class SiteUploadViewModel
    {
        private OrderFileCheck? _files;
        private bool _checkingFiles;
        private bool _confirmationsAccepted;
        private string _filesText = string.Empty;

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

                RefreshBooks();
            }
        }

        /// <summary>Whether the files have been read at all.</summary>
        public bool HasFiles => _files is not null;

        private IReadOnlyList<UploadBookRow> _books = Array.Empty<UploadBookRow>();

        /// <summary>Every book of the plan with its status and its files, in the order sent.</summary>
        /// <remarks>
        /// Rebuilt when the reading of the files starts or ends rather than on every repaint: the
        /// list holds the state of each expansion, and a list rebuilt under an open book would
        /// close it again.
        /// </remarks>
        public IReadOnlyList<UploadBookRow> BookRows
        {
            get => _books;
            private set
            {
                if (SetProperty(ref _books, value))
                    OnPropertyChanged(nameof(HasBookRows));
            }
        }

        /// <summary>Whether there are books to list.</summary>
        public bool HasBookRows => _books.Count > 0;

        private IReadOnlyList<UploadGroupRow> _groupRows = Array.Empty<UploadGroupRow>();

        /// <summary>One row per project, with its books and its parameters.</summary>
        /// <remarks>
        /// The projects are the screen's grid: the books of a project sit on the left, the
        /// format, the spreads and the price the site named for it sit on the right of the same
        /// row, so a number is never read away from the books it belongs to.
        /// </remarks>
        public IReadOnlyList<UploadGroupRow> GroupRows
        {
            get => _groupRows;
            private set
            {
                if (SetProperty(ref _groupRows, value))
                    OnPropertyChanged(nameof(HasGroupRows));
            }
        }

        /// <summary>Whether there are projects to show.</summary>
        public bool HasGroupRows => _groupRows.Count > 0;

        /// <summary>What the files as a whole came to, one line each.</summary>
        public IReadOnlyList<string> OrderAlertLines =>
            _files is null
                ? Array.Empty<string>()
                : _files.OrderAlerts.Select(alert => alert.Message).ToList();

        /// <summary>Whether the order itself has something the books do not.</summary>
        public bool HasOrderAlerts => OrderAlertLines.Count > 0;

        /// <summary>Rebuilds the books and their statuses from the check in hand.</summary>
        private void RefreshBooks()
        {
            var checkedBooks = new Dictionary<int, CheckedBook>();
            foreach (var book in _files?.Books ?? Array.Empty<CheckedBook>())
                checkedBooks[book.Number] = book;

            var groups = new List<UploadGroupRow>();
            var rows = new List<UploadBookRow>();
            var number = 0;

            foreach (var group in _groups)
            {
                var books = new List<UploadBookRow>();
                var spreads = 0;

                foreach (var book in group.Group.Books)
                {
                    number++;
                    checkedBooks.TryGetValue(number, out var checkedBook);

                    var row = Row(number, book, checkedBook);
                    books.Add(row);
                    rows.Add(row);

                    spreads += book.Files?.Count ?? 0;
                }

                groups.Add(new UploadGroupRow(
                    group.Number,
                    FormatOf(group),
                    spreads,
                    group.Price,
                    group.Currency,
                    books));
            }

            GroupRows = groups;
            BookRows = rows;
            OnPropertyChanged(nameof(OrderAlertLines));
            OnPropertyChanged(nameof(HasOrderAlerts));
        }

        /// <summary>The format a project is for, as the site names it.</summary>
        private static string FormatOf(UploadGroup group)
        {
            if (group.Group.Books.Count == 0)
                return "формат не выбран";

            var book = group.Group.Books[0];
            return book.Product.Title + " " + book.Variant.Title;
        }

        /// <summary>One book with its status and the files it would send.</summary>
        private UploadBookRow Row(int number, Book book, CheckedBook? check)
        {
            var status = BookStatus.Of(book, check, IsCheckingFiles);
            var files = new List<UploadFileRow>();

            if (book.CoverFile is { } cover)
                files.Add(FileRow("Обложка", null, cover, check));

            var spreads = book.Files ?? (IReadOnlyList<PageFile>)Array.Empty<PageFile>();
            for (var index = 0; index < spreads.Count; index++)
                files.Add(FileRow("Разворот " + (index + 1), index + 1, spreads[index], check));

            var name = check?.Name ?? "Книга " + number;

            return new UploadBookRow(number, name, status, files);
        }

        /// <summary>One file, with the site's own complaint about it when there is one.</summary>
        private static UploadFileRow FileRow(string title, int? position, PageFile file, CheckedBook? check)
        {
            var problem = check?.Alerts
                .FirstOrDefault(alert => alert.PageNumber == position)?.Message;

            return new UploadFileRow(title, System.IO.Path.GetFileName(file.Path), problem);
        }

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

                OnPropertyChanged(nameof(CanCheckFiles));
                OnPropertyChanged(nameof(CanUpload));
                OnPropertyChanged(nameof(ButtonText));

                // A book that was being read turns into «Готова» or a reason it is not.
                RefreshBooks();
            }
        }

        /// <summary>Whether the files may be read right now.</summary>
        private bool CanCheckFiles => !IsCheckingFiles && !IsBusy && _session is not null && _groups.Count > 0;

        /// <summary>What the screen says while it reads the files.</summary>
        public string FilesText
        {
            get => _filesText;
            private set
            {
                if (SetProperty(ref _filesText, value ?? string.Empty))
                    OnPropertyChanged(nameof(HasFilesText));
            }
        }

        /// <summary>Whether there is a line about the check itself.</summary>
        public bool HasFilesText => _filesText.Length > 0;

        /// <summary>
        /// Reads every page of the plan and says what is wrong with it.
        /// </summary>
        /// <remarks>
        /// A failure to read the files is not an empty check: the order is reported as blocked,
        /// because a button that came on after a failed read would send what nobody looked at.
        /// </remarks>
        public async Task CheckFilesAsync(CancellationToken cancellationToken = default)
        {
            if (!CanCheckFiles || _fileCheck is null)
                return;

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
                    .CheckAsync(_groups, progress, cancel.Token)
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
                        new FileAlert(
                            FileAlertLevel.Blocking,
                            null,
                            "Файлы не проверены: " + error.Message)
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

        /// <summary>The one line under the list once the files are read.</summary>
        private static string Describe(OrderFileCheck check)
        {
            var books = check.Books.Count == 1 ? "1 книга" : check.Books.Count + " кн.";

            if (check.BlockingCount > 0)
                return "Файлы: " + books + ", " + check.BlockingCount + " без отправки — исправьте отмеченное.";

            if (check.ConfirmationCount > 0)
                return "Файлы: " + books + ", " + check.ConfirmationCount + " вопрос(а) — ответьте ниже.";

            return "Файлы: " + books + ", всё совпадает с тем, что просит сайт.";
        }

        /// <summary>Runs the report where it is made, in the order the work happens.</summary>
        private sealed class InlineProgress<T> : IProgress<T>
        {
            private readonly Action<T> _report;

            public InlineProgress(Action<T> report) => _report = report;

            public void Report(T value) => _report(value);
        }

        /// <summary>Forgets the previous reading, so the button waits for a new one.</summary>
        private void ForgetFiles()
        {
            Files = null;
            FilesText = string.Empty;
            ConfirmationsAccepted = false;
            RefreshBooks();
        }
    }
}
