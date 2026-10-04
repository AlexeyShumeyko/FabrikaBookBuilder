using System.Threading.Tasks;
using System.Windows.Input;
using PhotoBook.Core;

namespace PhotoBookRenamer.Presentation.ViewModels
{
    /// <summary>
    /// The two editors seen as one thing: what the shell's chrome and the project list need
    /// from an open project.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Both editors were reached through the service container from the window that owns
    /// the keyboard shortcuts, which meant the window could not say what it was talking to -
    /// it asked for a concrete class by name, in a switch, on every key. This is what those
    /// callers actually need, and the difference between the two modes is a name: the
    /// folders mode loads folders, the combined mode loads photographs, and both are "choose
    /// what this project is made of".
    /// </para>
    /// <para>
    /// Commands rather than methods, because the caller has always executed commands and a
    /// command knows whether it is allowed to run right now.
    /// </para>
    /// </remarks>
    public interface IProjectEditor
    {
        /// <summary>Which mode this editor is. An editor belongs to exactly one.</summary>
        AppMode Mode { get; }

        /// <summary>Takes over a project loaded from disk or handed over by a new session.</summary>
        void SetProject(Project? project, ProjectInfo projectInfo);

        /// <summary>Saves without asking anything. Answers whether anything was written.</summary>
        Task<bool> QuickSaveAsync();

        /// <summary>Choose the source: folders in one mode, photographs in the other.</summary>
        ICommand LoadSourceCommand { get; }

        ICommand ExportCommand { get; }

        ICommand ExportWithFolderCommand { get; }

        ICommand ResetProjectCommand { get; }

        ICommand UndoCommand { get; }

        ICommand RedoCommand { get; }
    }
}
