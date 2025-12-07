using System.Collections.Generic;
using System.Threading.Tasks;
using PhotoBookRenamer.Models;

namespace PhotoBookRenamer.Services
{
    public interface IProjectService
    {
        Task<Project> CreateProjectFromFoldersAsync(List<string> folderPaths);
        Task SaveProjectAsync(Project project, string filePath);
        Task<Project?> LoadProjectAsync(string filePath);
        Project? Undo(Project currentProject);
        Project? Redo(Project currentProject);
        void SaveState(Project project);
        void ClearHistory();
        bool CanUndo { get; }
        bool CanRedo { get; }
    }
}

