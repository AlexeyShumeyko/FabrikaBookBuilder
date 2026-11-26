using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using PhotoBook.Application;
using PhotoBook.Core;
using PhotoBook.Presentation.ViewModels;

namespace PhotoBook.Application.Tests;

/// <summary>
/// The promise the removed save button used to carry: whatever the photographer changed is
/// on disk a moment later, in both modes.
/// </summary>
public class AutoSaveWiringTests
{
    [Fact]
    public async Task Combined_mode_writes_a_spread_placed_in_a_book()
    {
        var repository = new RecordingProjectRepository();
        var model = CombinedModel(repository);

        var spread = new Page { Index = 1, DisplayIndex = 1 };
        var book = new Book { BookIndex = 1, Cover = new Page { IsCover = true, Index = 0 } };
        book.Pages.Add(spread);
        book.UpdatePageSlots();

        // The structure itself is work: a run of books and spreads with no photographs in
        // it is already worth keeping, so the first write happens here.
        model.Books.Add(book);

        var structure = await repository.nextSaveAsync();
        Assert.Equal(1, structure.BookCount);

        spread.SourcePath = "/photos/one.jpg";

        var placed = await repository.nextSaveAsync();
        Assert.Contains("/photos/one.jpg", placed.SourcePaths);
    }

    [Fact]
    public async Task Combined_mode_writes_a_file_loaded_into_the_panel()
    {
        var repository = new RecordingProjectRepository();
        var model = CombinedModel(repository);

        model.AvailableFiles.Add("/photos/from-panel.jpg");

        var saved = await repository.nextSaveAsync();
        Assert.Contains("/photos/from-panel.jpg", saved.AvailableFiles);
    }

    [Fact]
    public async Task Folders_mode_writes_a_spread_after_the_project_was_opened()
    {
        var repository = new RecordingProjectRepository();
        var model = FoldersModel(repository);

        var book = TestProject.Book(1, "/photos/cover.jpg", "/photos/old.jpg");
        var project = TestProject.Folders(book);
        var info = new ProjectInfo
        {
            Id = Guid.NewGuid().ToString(),
            Name = "Run",
            Mode = AppMode.UniqueFolders
        };

        model.SetProject(project, info);

        // Opening a project is not an edit. A write here would rewrite the file that was
        // just read, with whatever the loading pass had managed to put in place.
        Assert.Equal(0, repository.SaveCount);

        book.Pages[0].SourcePath = "/photos/new.jpg";

        var saved = await repository.nextSaveAsync();
        Assert.Contains("/photos/new.jpg", saved.SourcePaths);
    }

    [Fact]
    public async Task A_project_nobody_named_is_called_by_the_moment_it_started()
    {
        var repository = new RecordingProjectRepository();
        var projectList = new RecordingProjectListService();
        var model = CombinedModel(repository, projectList);

        model.Books.Add(new Book { BookIndex = 1 });
        await repository.nextSaveAsync();

        var name = Assert.Single(projectList.CreatedNames);

        Assert.Matches(@"^\d{4}-\d{2}-\d{2} \d{2}:\d{2}$", name);
        Assert.True(name.Length <= ProjectNameRule.MaxLength);

        // The bar reads the screen's own property: a record that exists and a bar that still
        // says "Новый проект" is the pair this keeps together.
        Assert.Equal(name, model.ProjectName);
    }

    private static CombinedModeViewModel CombinedModel(
        IProjectRepository repository,
        IProjectListService? projectList = null)
        => new(
            fileService: null!,
            files: null!,
            imageService: new NeverMeasuredImages(),
            thumbnails: new NeverBuiltThumbnails(),
            exportService: null!,
            loggingService: new SilentLog(),
            projects: repository,
            projectListService: projectList ?? new RecordingProjectListService(),
            dialogs: null!,
            images: new NullImageCache(),
            ui: new ImmediateUiThread(),
            editorDialogs: null!);

    private static UniqueFoldersViewModel FoldersModel(IProjectRepository repository)
        => new(
            fileService: null!,
            files: null!,
            imageService: new NeverMeasuredImages(),
            thumbnails: new NeverBuiltThumbnails(),
            projectBuilder: null!,
            editHistory: new NoEditHistory(),
            projects: repository,
            exportService: null!,
            loggingService: new SilentLog(),
            projectListService: new RecordingProjectListService(),
            images: new NullImageCache(),
            ui: new ImmediateUiThread(),
            dialogs: null!,
            editorDialogs: null!);

    /// <summary>What one save contained, captured when it was asked for.</summary>
    private sealed record SavedProject(
        int BookCount,
        IReadOnlyList<string?> SourcePaths,
        IReadOnlyList<string> AvailableFiles);

    /// <summary>
    /// Keeps every save as a value, because the editors mutate the same project instance
    /// for the next one. A test that held the instance would only ever see its last state.
    /// </summary>
    private sealed class RecordingProjectRepository : IProjectRepository
    {
        private readonly Channel<SavedProject> _saves = Channel.CreateUnbounded<SavedProject>();

        public int SaveCount { get; private set; }

        public Task SaveAsync(Project project, string filePath)
        {
            SaveCount++;

            var paths = project.Books
                .SelectMany(b => new[] { b.Cover?.SourcePath }.Concat(b.Pages.Select(p => p.SourcePath)))
                .ToList();

            _saves.Writer.TryWrite(new SavedProject(
                project.Books.Count,
                paths,
                project.AvailableFiles.ToList()));

            return Task.CompletedTask;
        }

        public Task<Project?> LoadAsync(string filePath) => Task.FromResult<Project?>(null);

        public Task<SavedProject> nextSaveAsync()
            => _saves.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10));
    }

    private sealed class RecordingProjectListService : IProjectListService
    {
        /// <summary>The names the editor asked a new project to be called, in order.</summary>
        public List<string> CreatedNames { get; } = new();

        public Task<List<ProjectInfo>> GetProjectsAsync(AppMode mode)
            => Task.FromResult(new List<ProjectInfo>());

        public Task<List<ProjectInfo>> GetAllProjectsAsync()
            => Task.FromResult(new List<ProjectInfo>());

        public Task<ProjectInfo?> CreateProjectAsync(AppMode mode, string name)
        {
            CreatedNames.Add(name);

            return Task.FromResult<ProjectInfo?>(new ProjectInfo
            {
                Id = Guid.NewGuid().ToString(),
                Name = name,
                Mode = mode
            });
        }

        public Task<Result> SaveProjectInfoAsync(ProjectInfo projectInfo) => Task.FromResult(Result.Ok());

        public Task<Result> DeleteProjectAsync(ProjectInfo projectInfo) => Task.FromResult(Result.Ok());

        public Task<ProjectInfo?> UpdateProjectInfoAsync(Project project, string filePath)
            => Task.FromResult<ProjectInfo?>(null);

        public Task RefreshFromProjectAsync(string projectId, Project project) => Task.CompletedTask;
    }

    private sealed class NoEditHistory : IEditHistory
    {
        public bool CanUndo => false;
        public bool CanRedo => false;
        public void SaveState(Project project) { }
        public Project? Undo(Project currentProject) => null;
        public Project? Redo(Project currentProject) => null;
        public void ClearHistory() { }
    }

    private sealed class NeverMeasuredImages : IImageService
    {
        public Task<(int Width, int Height)> GetImageDimensionsAsync(string filePath)
            => Task.FromResult((0, 0));

        public Task<string?> DetectCoverAsync(string[] filePaths) => Task.FromResult<string?>(null);
    }

    private sealed class NeverBuiltThumbnails : IThumbnailProvider
    {
        public string? GetExistingPath(string sourcePath) => null;

        public Task<string?> GetOrCreateAsync(string sourcePath, CancellationToken cancellationToken = default)
            => Task.FromResult<string?>(null);

        public Task EnsureAsync(
            IReadOnlyCollection<string> sourcePaths,
            IProgress<double>? progress = null,
            CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private sealed class NullImageCache : IDecodedImageCache
    {
        public void Invalidate(DecodedImageKind kind) { }
        public void InvalidateAll() { }
        public void InvalidateFile(string filePath) { }

        public Task PrewarmAsync(IEnumerable<string>? filePaths, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private sealed class ImmediateUiThread : IUiThread
    {
        public bool IsOnUiThread => true;
        public void Invoke(Action action) => action();

        public Task InvokeAsync(Action action)
        {
            action();
            return Task.CompletedTask;
        }

        public void BeginInvoke(Action action, UiDispatchPriority priority = UiDispatchPriority.Normal)
            => action();
    }

    private sealed class SilentLog : ILoggingService
    {
        public void LogInfo(string message) { }
        public void LogError(string message, Exception? exception = null) { }
        public void LogWarning(string message) { }
    }
}
