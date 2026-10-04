using System.Threading;
using System.Threading.Tasks;

namespace PhotoBook.Application
{
    /// <summary>
    /// Asks the person using the program to choose photographs from disk.
    ///
    /// <para>
    /// A port, because choosing files is a user interface concern and it was sitting in the
    /// file layer: <c>FileService</c> opened a system dialog from the middle of a class whose
    /// job is reading and writing files. Any engine can ask the question; only the shell can
    /// put it to a person.
    /// </para>
    ///
    /// <para>
    /// The three folder-picking methods that used to sit beside this one in the file layer
    /// had no callers at all and were removed rather than ported.
    /// </para>
    /// </summary>
    public interface IPickFiles
    {
        /// <summary>
        /// The chosen files, or null when the dialog was dismissed. A dismissal is not a
        /// failure: the caller decides whether an empty answer means "do nothing".
        /// </summary>
        Task<string[]?> PickImagesAsync(
            string title = "Выберите JPEG файлы",
            CancellationToken cancellationToken = default);
    }
}
