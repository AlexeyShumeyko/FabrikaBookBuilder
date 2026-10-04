using System.Collections.Generic;
using System.Threading.Tasks;
using PhotoBook.Core;

namespace PhotoBookRenamer.Application
{
    /// <summary>
    /// Building a project out of folders, and undo and redo while it is being edited.
    /// Storing it is <see cref="IProjectRepository"/>'s job.
    /// </summary>
    public interface IProjectService
    {
        Task<Project> CreateProjectFromFoldersAsync(List<string> folderPaths);
        Project? Undo(Project currentProject);
        Project? Redo(Project currentProject);
        void SaveState(Project project);
        void ClearHistory();
        bool CanUndo { get; }
        bool CanRedo { get; }
    }
}
