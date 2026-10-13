using System.Collections.Generic;
using System.Threading.Tasks;
using PhotoBook.Core;

namespace PhotoBook.Application
{
    /// <summary>
    /// The windows an editor opens to collect something the user chooses.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Separate from <see cref="IProjectDialogs"/>, which asks questions with known answers.
    /// These collect answers that are not known in advance - where to export, which folders,
    /// which page - and each returns the thing itself rather than a yes.
    /// </para>
    /// <para>
    /// Keeping them apart is not tidiness. A question returns one of a fixed set, so a view
    /// model can carry on afterwards; a choice returns data it has to work with, and the
    /// difference between "the user cancelled" and "the user chose nothing" is one this
    /// contract has to carry. Folding the two together into a single "dialogs" port would
    /// mean every caller inventing its own way to say that, and one of them would get it
    /// wrong: in the folder picker, an empty selection means "remove every folder", and
    /// treating it as a cancel would silently do nothing.
    /// </para>
    /// <para>
    /// Nothing here returns a window, a control, or a handle. A dialog belongs to the engine
    /// that drew it, and a contract that handed one back would bind every caller to it.
    /// </para>
    /// </remarks>
    public interface IEditorDialogs
    {
        /// <summary>
        /// Asks where and how to export, and runs the export if the user proceeds.
        /// </summary>
        /// <returns>
        /// The folder the files landed in, or null if the user stopped before exporting.
        /// </returns>
        /// <remarks>
        /// The window owns the progress bar, so it owns the work: the export runs inside it,
        /// which is what lets a long export show progress at all. What comes back is the
        /// folder, and the caller does the bookkeeping.
        /// </remarks>
        Task<string?> AskToExport(Project project);

        /// <summary>
        /// Says the export finished, and offers to open the folder.
        /// </summary>
        /// <remarks>
        /// Opening a folder is the shell's job - it is a piece of platform, not a decision.
        /// A view model that launched Explorer itself was one more reason it could not run
        /// in a shell where "open a folder" means something else.
        /// </remarks>
        Task ShowExportSucceeded(string outputFolder);

        /// <summary>
        /// Lets the user choose folders.
        /// </summary>
        /// <param name="existingFolders">The folders the project already has, if any.</param>
        /// <returns>
        /// The folders chosen - an empty list when the user removed them all - or null when
        /// the window was closed without a decision.
        /// </returns>
        List<string>? PickFolders(List<string> existingFolders);

        /// <summary>
        /// Asks which page a number should be assigned to.
        /// </summary>
        /// <returns>The page number, or null if the user stopped without choosing.</returns>
        int? AskPageNumber(int maxPageNumber);
    }
}
