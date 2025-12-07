using System.Windows.Input;

namespace PhotoBookRenamer.ViewModels
{
    public static class CommandManager
    {
        public static void InvalidateRequerySuggested()
        {
            System.Windows.Input.CommandManager.InvalidateRequerySuggested();
        }
    }
}





