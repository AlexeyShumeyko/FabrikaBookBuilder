using System.Windows.Input;

namespace PhotoBookRenamer.Presentation.ViewModels
{
    public static class CommandManager
    {
        public static void InvalidateRequerySuggested()
        {
            System.Windows.Input.CommandManager.InvalidateRequerySuggested();
        }
    }
}





