using Avalonia.Controls;

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
    }
}
