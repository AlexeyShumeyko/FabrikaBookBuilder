using System.Windows;

namespace PhotoBookRenamer.Presentation.Dialogs
{
    /// <summary>Which button a three-way question was answered with.</summary>
    public enum ConfirmOutcome
    {
        /// <summary>The primary (right-most, coloured) button.</summary>
        Primary,

        /// <summary>The secondary button, offered only by a three-way question.</summary>
        Secondary,

        /// <summary>Cancel, Escape, or the window's X.</summary>
        Cancelled
    }

    /// <summary>
    /// The app's own "are you sure" window.
    ///
    /// Every confirmation used to be <c>System.Windows.MessageBox</c>, which on Windows 10/11
    /// still draws the Win95-era box: a grey slab, a bevelled edge and a system icon. The
    /// owner called it out as the one thing in the program that does not belong to it, so
    /// this replaces it and <see cref="AppDialogs"/> is how the rest of the code asks.
    ///
    /// A destructive question (delete, discard) is red by default; a neutral one passes
    /// destructive: false and gets the brand colour, so "are you sure you want to leave"
    /// does not shout about destruction.
    ///
    /// A question that genuinely has three answers (save / discard / stay) gets a third
    /// button rather than a system box: the only such case is "the project is not saved,
    /// save it before leaving", where collapsing the middle option would throw work away
    /// or trap the user.
    /// </summary>
    public partial class ConfirmDialog : Window
    {
        private ConfirmOutcome _outcome = ConfirmOutcome.Cancelled;

        public ConfirmDialog()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Sets the text. <paramref name="destructive"/> picks red vs brand;
        /// <paramref name="secondaryText"/> adds the third button when given.
        /// </summary>
        public void Configure(
            string title,
            string message,
            string confirmText,
            bool destructive = true,
            string? secondaryText = null)
        {
            TitleText.Text = title;
            MessageText.Text = message;
            ConfirmButton.Content = confirmText;

            // The automation name follows the label. Leaving it at the XAML's
            // "Подтвердить" would make a screen reader - and any test - hear "Подтвердить"
            // on a button that says "Уменьшить" or "Удалить".
            System.Windows.Automation.AutomationProperties.SetName(ConfirmButton, confirmText);

            if (secondaryText != null)
            {
                SecondaryButton.Content = secondaryText;
                System.Windows.Automation.AutomationProperties.SetName(SecondaryButton, secondaryText);
                SecondaryButton.Visibility = Visibility.Visible;
            }

            if (destructive)
            {
                IconHost.Background = (System.Windows.Media.Brush)FindResource("Red50");
                IconGlyph.Foreground = (System.Windows.Media.Brush)FindResource("Red600");
            }
            else
            {
                IconHost.Background = (System.Windows.Media.Brush)FindResource("Brand50");
                IconGlyph.Foreground = (System.Windows.Media.Brush)FindResource("Brand600");
            }
        }

        /// <summary>
        /// One-button mode, for a message to read rather than a question to answer. The
        /// remaining button becomes the default and the only way out besides Escape.
        /// </summary>
        public void ShowSingleButton()
        {
            CancelButton.Visibility = Visibility.Collapsed;
            ConfirmButton.IsDefault = true;
            ConfirmButton.IsCancel = true;
        }

        private void OnConfirmClick(object sender, RoutedEventArgs e)
        {
            _outcome = ConfirmOutcome.Primary;
            DialogResult = true;
        }

        private void OnSecondaryClick(object sender, RoutedEventArgs e)
        {
            _outcome = ConfirmOutcome.Secondary;
            DialogResult = true;
        }

        private void OnCancelClick(object sender, RoutedEventArgs e)
        {
            _outcome = ConfirmOutcome.Cancelled;
            DialogResult = false;
        }

        /// <summary>True only when the user pressed the confirm button.</summary>
        public bool ShowConfirmed() => ShowOutcome() == ConfirmOutcome.Primary;

        /// <summary>
        /// Shows the window and reports which button was pressed. Closing it with Escape or
        /// the X counts as <see cref="ConfirmOutcome.Cancelled"/>, as it must for a
        /// destructive action: an escaped dialog must never delete anything.
        /// </summary>
        public ConfirmOutcome ShowOutcome() => base.ShowDialog() == true ? _outcome : ConfirmOutcome.Cancelled;
    }
}
