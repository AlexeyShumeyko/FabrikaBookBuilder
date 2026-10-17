using System.Collections.Generic;
using System.Threading.Tasks;
using PhotoBook.Application;
using PhotoBook.Core;

namespace PhotoBook.Application.Tests
{
    /// <summary>
    /// A list service that keeps its hands off, for tests that are about the project file.
    /// </summary>
    /// <remarks>
    /// The repository updates the index whenever it writes a project, because that is the one
    /// place a project is written and therefore the one place that cannot forget. Tests about
    /// the file itself want a repository that only touches the file - and they should have to
    /// say so, which is what passing this makes them do.
    ///
    /// It records what it was asked, so a test that cares can look; nothing here throws,
    /// because a stub that fails a test about something else is worse than no stub at all.
    /// </remarks>
    internal sealed class QuietProjectListService : IProjectListService
    {
        /// <summary>Every id this was asked to refresh, in order.</summary>
        public List<string> Refreshed { get; } = new();

        public Task<List<ProjectInfo>> GetProjectsAsync(AppMode mode)
            => Task.FromResult(new List<ProjectInfo>());

        public Task<List<ProjectInfo>> GetAllProjectsAsync()
            => Task.FromResult(new List<ProjectInfo>());

        public Task<ProjectInfo?> CreateProjectAsync(AppMode mode, string name)
            => Task.FromResult<ProjectInfo?>(null);

        public Task<Result> SaveProjectInfoAsync(ProjectInfo projectInfo)
            => Task.FromResult(Result.Ok());

        public Task<Result> DeleteProjectAsync(ProjectInfo projectInfo)
            => Task.FromResult(Result.Ok());

        public Task<ProjectInfo?> UpdateProjectInfoAsync(Project project, string filePath)
            => Task.FromResult<ProjectInfo?>(null);

        public Task RefreshFromProjectAsync(string projectId, Project project)
        {
            Refreshed.Add(projectId);
            return Task.CompletedTask;
        }
    }
}
