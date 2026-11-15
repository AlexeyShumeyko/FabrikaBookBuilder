using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PhotoBook.Application.Sites.Books.Files;

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
                OnPropertyChanged(nameof(FileLines));
                OnPropertyChanged(nameof(FileProblemsText));
                OnPropertyChanged(nameof(HasFileProblems));
                OnPropertyChanged(nameof(NeedsConfirmation));
                OnPropertyChanged(nameof(CanUpload));
            }
        }

        /// <summary>Whether the files have been read at all.</summary>
        public bool HasFiles => _files is not null;

        /// <summary>One line per problem, then one per book that is clean.</summary>
        public IReadOnlyList<string> FileLines
        {
            get
            {
                if (_files is null)
                    return Array.Empty<string>();

                var lines = new List<string>();

                foreach (var book in _files.Books)
                {
                    if (book.Alerts.Count == 0)
                    {
                        lines.Add(book.Name + ": страницы совпадают с тем, что просит сайт.");
                        continue;
                    }

                    lines.Add(book.Name + ":");
                    lines.AddRange(book.Alerts.Select(alert => "  " + alert.Message));
                }

                lines.AddRange(_files.OrderAlerts.Select(alert => alert.Message));

                return lines;
            }
        }

        /// <summary>What keeps the order from going, in one paragraph.</summary>
        public string FileProblemsText => string.Join(Environment.NewLine, FileLines);

        /// <summary>Whether there is anything to read about the files.</summary>
        public bool HasFileProblems => FileLines.Count > 0;

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
            }
        }

        /// <summary>Whether the check may be started again.</summary>
        public bool CanCheckFiles => !IsCheckingFiles && !IsBusy && _session is not null && _groups.Count > 0;

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
        }
    }
}
