using System.Collections.Generic;
using System.Threading.Tasks;
using PhotoBook.Core;

namespace PhotoBook.Application
{
    public interface IProjectListService
    {
        Task<List<ProjectInfo>> GetProjectsAsync(AppMode mode);
        Task<List<ProjectInfo>> GetAllProjectsAsync();
        Task<ProjectInfo?> CreateProjectAsync(AppMode mode, string name);
        Task<Result> SaveProjectInfoAsync(ProjectInfo projectInfo);
        Task<Result> DeleteProjectAsync(ProjectInfo projectInfo);
        Task<ProjectInfo?> UpdateProjectInfoAsync(Project project, string filePath);

        /// <summary>
        /// Brings one index entry up to date from the project that was just saved.
        /// </summary>
        /// <remarks>
        /// Called by the repository - the one place a project file is written. That is the whole
        /// point of it: the list needs the counts, the status and the preview strip to be true,
        /// and the honest way to keep them true is for whoever writes the project to update
        /// them, rather than for a dozen call sites to remember, none of which waits for the
        /// answer.
        /// </remarks>
        Task RefreshFromProjectAsync(string projectId, Project project);
    }
}



