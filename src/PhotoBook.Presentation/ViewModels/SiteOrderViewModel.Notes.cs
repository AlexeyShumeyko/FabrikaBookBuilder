using System;
using System.Linq;

namespace PhotoBook.Presentation.ViewModels
{
    /// <summary>The words of the order page: what is wrong with the project, and what the button waits for.</summary>
    /// <remarks>
    /// <para>
    /// Two kinds of sentence, kept apart. What is true of the project itself - a half-filled book,
    /// photographs the site will not carry - is written once, when the page is opened. What a
    /// reading of the site said about the order is replaced as a whole by the next reading. A
    /// price run that succeeds must not clear the project's own lines: a card that appears on the
    /// way in and vanishes a second later reads as an error that fixed itself.
    /// </para>
    /// <para>
    /// The reason beside the grey button is the second half of the same idea. A disabled button
    /// with no sentence next to it is a puzzle, and the customer should not have to guess that the
    /// format was never picked or that an answer is still missing.
    /// </para>
    /// </remarks>
    public sealed partial class SiteOrderViewModel
    {
        private string _projectMessage = string.Empty;
        private string _runMessage = string.Empty;

        /// <summary>What the customer has to know before going on.</summary>
        public string Message
        {
            get
            {
                if (_projectMessage.Length == 0)
                    return _runMessage;
                if (_runMessage.Length == 0)
                    return _projectMessage;

                return _projectMessage + Environment.NewLine + _runMessage;
            }
        }

        /// <summary>Whether there is anything to say.</summary>
        public bool HasMessage => _projectMessage.Length > 0 || _runMessage.Length > 0;

        /// <summary>
        /// Why the send button is off, in the words of the step that keeps it off.
        /// </summary>
        /// <remarks>
        /// The line is empty when there is nothing to explain - either the button is on, or the
        /// list already names the book that is not full.
        /// </remarks>
        public string BlockingText
        {
            get
            {
                if (IsLoadingFormats)
                    return "Читаем форматы сайта…";

                if (Books.Count == 0 || Books.Any(book => !book.IsReady))
                    return string.Empty;

                if (SelectedFormat is null)
                    return "Выберите формат группы: по нему сайт считает цену.";

                if (!_parametersLoaded)
                    return "Читаем параметры формата…";

                if (!_parameters.IsAnswered)
                    return "Ответьте на параметры формата: без ответов сайт цену не назовёт.";

                if (_isPricing)
                    return "Считаем цену на сайте…";

                if (IsCheckingFiles)
                    return "Проверяем файлы книг…";

                if (NeedsConfirmation && !_confirmationsAccepted)
                    return "Отметьте, что файлы можно отправлять: край сайт может обрезать.";

                if (_files is null)
                    return "Проверяем файлы книг…";

                if (_files is { CanSend: false })
                    return "Файлы не готовы к отправке: исправьте отмеченное в списке.";

                // Nothing above explains it, so the plan itself is not complete. Saying at least
                // that much keeps the button from being grey for no stated reason.
                return CanUpload ? string.Empty : "Заказ не готов к отправке.";
            }
        }

        /// <summary>Whether there is a reason to show.</summary>
        public bool HasBlockingText => BlockingText.Length > 0;

        /// <summary>What is true of the project, written once when the page is opened.</summary>
        private void SetProjectMessage(string? text)
        {
            _projectMessage = text ?? string.Empty;
            _runMessage = string.Empty;
            OnPropertyChanged(nameof(Message));
            OnPropertyChanged(nameof(HasMessage));
        }

        /// <summary>
        /// What the last reading of the site said, replaced as a whole by the next one.
        /// </summary>
        private void Note(string? text)
        {
            var value = text ?? string.Empty;
            if (value == _runMessage)
                return;

            _runMessage = value;
            OnPropertyChanged(nameof(Message));
            OnPropertyChanged(nameof(HasMessage));
        }
    }
}
