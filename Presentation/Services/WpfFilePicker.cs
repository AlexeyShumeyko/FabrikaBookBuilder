using System.Threading;
using System.Threading.Tasks;

using Microsoft.Win32;
using PhotoBook.Application;

namespace PhotoBookRenamer.Presentation.Services
{
    /// <summary>
    /// The system file dialog, unchanged: the same window, the same filter, the same
    /// behaviour when it is dismissed. Only its location moved - out of the file layer, where
    /// a class that reads and writes files was opening a window.
    /// </summary>
    public class WpfFilePicker : IPickFiles
    {
        public Task<string[]?> PickImagesAsync(string title = "Выберите JPEG файлы", CancellationToken cancellationToken = default)
        {
            return Task.Run(() =>
            {
                var dialog = new OpenFileDialog
                {
                    Filter = "JPEG файлы|*.jpg;*.jpeg;*.JPG;*.JPEG",
                    Multiselect = true,
                    Title = title
                };

                return dialog.ShowDialog() == true ? dialog.FileNames : null;
            }, cancellationToken);
        }
    }
}
