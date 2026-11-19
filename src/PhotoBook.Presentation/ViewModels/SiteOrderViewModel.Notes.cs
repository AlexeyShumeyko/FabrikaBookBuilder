using System;
using System.Linq;

namespace PhotoBook.Presentation.ViewModels
{
    /// <summary>The words of the order page: what is wrong with the project, and what waits.</summary>
    /// <remarks>
    /// <para>
    /// Three kinds of sentence, kept apart. What is true of the project itself - a half-filled
    /// book, photographs the site will not carry - is written once, when the page is opened. What
    /// a reading of the site said about the order is replaced as a whole by the next reading. A
    /// price run that succeeds must not clear the project's own lines: a card that appears on the
    /// way in and vanishes a second later reads as an error that fixed itself.
    /// </para>
    /// <para>
    /// The reason beside the grey button is the second half of the same idea: a disabled button
    /// with no sentence next to it is a puzzle. And when some groups can go while others cannot,
    /// the warning says which ones stay behind - sending part of an order is a decision the
    /// customer has to make knowing what it leaves out.
    /// </para>
    /// </remarks>
    public sealed partial class SiteOrderViewModel
    {
        private string _projectMessage = string.Empty;
        private string _runMessage = string.Empty;
        private string _warning = string.Empty;

        /// <summary>The order's price summed over its groups, and the currency it is written in.</summary>
        private decimal _total;
        private string _currency = string.Empty;

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
        /// The line is empty when there is nothing to explain - either the button is on, or one
        /// group's list already names the book that is not full. When the order was laid out into
        /// several groups, every group that keeps the button off is named: with the groups now
        /// standing apart, "не готова одна из групп" would leave the customer to find which.
        /// </remarks>
        public string BlockingText
        {
            get
            {
                if (IsLoadingFormats)
                    return "Читаем форматы сайта…";

                if (Groups.Count == 0)
                    return string.Empty;

                if (Groups.Any(group => group.CanGo))
                {
                    // A group that is about to answer still holds the button: sending now would
                    // send less than the page has already shown.
                    return Groups.Any(group => group.IsBusy)
                        ? "Дожидаемся ответа сайта по остальным группам…"
                        : string.Empty;
                }

                if (Groups.Count == 1)
                    return Groups[0].ProblemText;

                return string.Join(
                    Environment.NewLine,
                    Groups.Select(group => "Группа " + group.Number + ": " + group.ProblemText));
            }
        }

        /// <summary>Whether there is a reason to show.</summary>
        public bool HasBlockingText => BlockingText.Length > 0;

        /// <summary>
        /// What will not be sent if the button is pressed now, group by group.
        /// </summary>
        /// <remarks>
        /// Shown only when something is going: with every group stuck, the reason beside the
        /// button already says what is missing, and a warning about books staying behind would
        /// describe a send that cannot happen.
        /// </remarks>
        public string WarningText => _warning;

        /// <summary>Whether the order would go without some of its groups.</summary>
        public bool HasWarningText => _warning.Length > 0;

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
