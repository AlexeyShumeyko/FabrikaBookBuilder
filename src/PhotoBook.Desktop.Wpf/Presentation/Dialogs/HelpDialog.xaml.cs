using System.Diagnostics;
using System.IO;
using System.Windows;
using PhotoBookRenamer.Infrastructure;

namespace PhotoBookRenamer.Presentation.Dialogs
{
    /// <summary>
    /// The help button's answer for now: what is being prepared, and where the journal is.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The full section - every function, the answers, the licence and what the program does
    /// with the photographs - is planned for after 2.0.0, with the theme and contacts buttons
    /// beside it. This window is the shell that section will grow into, so nothing here has to
    /// be thrown away when it arrives.
    /// </para>
    /// <para>
    /// The journal is here rather than in a dialog elsewhere because a customer who is going
    /// to contact support will look in Help, and without this file that conversation cannot go
    /// further than a description of what they saw.
    /// </para>
    /// </remarks>
    public partial class HelpDialog : Window
    {
        public HelpDialog()
        {
            InitializeComponent();
            JournalPathText.Text = Diagnostics.JournalPath;
        }

        private void OnOpenFolderClick(object sender, RoutedEventArgs e)
        {
            try
            {
                var directory = Path.GetDirectoryName(Diagnostics.JournalPath);
                if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory)) return;

                // Explorer with /select lands on the file itself, which is what a customer is
                // about to attach: opening only the folder makes them go looking for it.
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{Diagnostics.JournalPath}\"")
                {
                    UseShellExecute = true
                });
            }
            catch
            {
                // A folder that will not open is no reason to lose the path: it is on screen.
            }
        }

        private void OnCopyPathClick(object sender, RoutedEventArgs e)
        {
            try
            {
                JournalPathText.SelectAll();
                Clipboard.SetText(JournalPathText.Text);
            }
            catch
            {
                // The clipboard is not always available; the path stays selectable and visible.
            }
        }

        private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
    }
}
