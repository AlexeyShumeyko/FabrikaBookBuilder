using System.Collections.Generic;
using System.Threading.Tasks;
using PhotoBookRenamer.Models;

namespace PhotoBookRenamer.Services
{
    public interface IProjectListService
    {
        Task<List<ProjectInfo>> GetProjectsAsync(AppMode mode);
        Task<List<ProjectInfo>> GetAllProjectsAsync();
        Task<ProjectInfo?> CreateProjectAsync(AppMode mode, string name);
        Task<bool> SaveProjectInfoAsync(ProjectInfo projectInfo);
        Task<bool> DeleteProjectAsync(ProjectInfo projectInfo);
        Task<ProjectInfo?> UpdateProjectInfoAsync(Project project, string filePath);
    }
}



