using System;

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
    /// Why a group cannot go is the group's own sentence, written on its card: a disabled button
    /// with no word about it is a puzzle, and a page-level summary of several groups repeated
    /// their numbers without saying where to look. When some groups can go while others cannot,
    /// each card left out says so as a warning - sending part of an order is a decision the
    /// customer has to make knowing what it leaves out.
    /// </para>
    /// </remarks>
    public sealed partial class SiteOrderViewModel
    {
        private string _projectMessage = string.Empty;
        private string _runMessage = string.Empty;

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
