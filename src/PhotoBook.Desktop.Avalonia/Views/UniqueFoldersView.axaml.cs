using Avalonia.Controls;

namespace PhotoBook.Shell.Views
{
    /// <summary>
    /// The folders mode: a project made of one book per folder, each book a container of
    /// spreads.
    /// </summary>
    /// <remarks>
    /// The data context is set in code because the shell builds this screen with a view model
    /// it resolved. There is no DataContext to write in the markup that would not be a lie.
    /// </remarks>
    public partial class UniqueFoldersView : UserControl
    {
        public UniqueFoldersView()
        {
            InitializeComponent();
        }
    }
}
