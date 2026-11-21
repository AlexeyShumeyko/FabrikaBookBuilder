using Avalonia.Controls;
using Avalonia.Interactivity;

namespace PhotoBook.Shell.Views
{
    /// <summary>
    /// The projects screen, with the view model the shell handed it.
    /// </summary>
    /// <remarks>
    /// The data context is set in code rather than declared in the markup, because the shell
    /// builds this screen with a view model it resolved. There is no DataContext to write in
    /// the markup that would not be a lie.
    /// </remarks>
    public partial class ProjectListView : UserControl
    {
        public ProjectListView()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Opens the guide from the first-run panel.
        /// </summary>
        /// <remarks>
        /// The button says "как это работает" and the answer is the guide, so it opens the same
        /// window the header's help button does rather than a window of its own. The owner is
        /// looked up rather than passed in: this view is built by the shell and has no window of
        /// its own to hand.
        /// </remarks>
        private async void OnHowItWorksClick(object? sender, RoutedEventArgs e)
        {
            if (TopLevel.GetTopLevel(this) is not Window owner) return;

            await new HelpWindow().ShowDialog(owner);
        }
    }
}
