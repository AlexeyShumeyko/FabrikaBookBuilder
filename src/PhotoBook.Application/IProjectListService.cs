using System.Collections.Generic;
using System.Threading.Tasks;
using PhotoBook.Core;

namespace PhotoBookRenamer.Application
{
    public interface IProjectListService
    {
        Task<List<ProjectInfo>> GetProjectsAsync(AppMode mode);
        Task<List<ProjectInfo>> GetAllProjectsAsync();
        Task<ProjectInfo?> CreateProjectAsync(AppMode mode, string name);
        Task<Result> SaveProjectInfoAsync(ProjectInfo projectInfo);
        Task<Result> DeleteProjectAsync(ProjectInfo projectInfo);
        Task<ProjectInfo?> UpdateProjectInfoAsync(Project project, string filePath);
    }
}



