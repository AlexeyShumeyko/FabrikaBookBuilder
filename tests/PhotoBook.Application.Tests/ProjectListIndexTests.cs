using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using PhotoBook.Application;
using PhotoBook.Core;
using PhotoBook.Infrastructure;

namespace PhotoBook.Application.Tests
{
    /// <summary>
    /// The project index is what the cards are drawn from. A card's picture strip is written to
    /// the index by the refresh that reads the project, while the editors update single fields
    /// of the same record all the time - a name, a count - without knowing anything about
    /// pictures. An editor's partial update must not be read as "this project has no pictures".
    /// </summary>
    public class ProjectListIndexTests : IDisposable
    {
        private readonly string _root = Path.Combine(
            Path.GetTempPath(), "PhotoBookRenamer.Tests", Guid.NewGuid().ToString("N"));

        private readonly FileService _files = new();

        public ProjectListIndexTests() => Directory.CreateDirectory(_root);

        public void Dispose()
        {
            try { Directory.Delete(_root, recursive: true); } catch { }
        }

        [Fact]
        public async Task AnEditorUpdateWithoutPicturesKeepsTheStripTheIndexAlreadyHas()
        {
            var index = new MemoryIndex
            {
                Json = Json(new ProjectInfo
                {
                    Id = "p1",
                    Name = "Уникальные папки",
                    Mode = AppMode.UniqueFolders,
                    BookCount = 4,
                    PageCount = 8,
                    PreviewPhotos = { "/photos/a.jpg", "/photos/b.jpg", "/photos/c.jpg" },
                    TotalPhotoCount = 23
                })
            };

            var service = new ProjectListService(index, _files, new QuietLogging());

            var result = await service.SaveProjectInfoAsync(new ProjectInfo
            {
                Id = "p1",
                Name = "Уникальные папки",
                Mode = AppMode.UniqueFolders,
                BookCount = 4,
                PageCount = 8
            });

            Assert.True(result.IsSuccess);

            var saved = Assert.Single(Parse(index.Json));
            Assert.Equal(3, saved.PreviewPhotos.Count);
            Assert.Equal(23, saved.TotalPhotoCount);
            Assert.Equal(4, saved.BookCount);
        }

        [Fact]
        public async Task ARefreshThatReadsTheProjectStillClearsPicturesThatAreGone()
        {
            var index = new MemoryIndex
            {
                Json = Json(new ProjectInfo
                {
                    Id = "p2",
                    Name = "Проект",
                    Mode = AppMode.UniqueFolders,
                    PreviewPhotos = { "/photos/gone.jpg" },
                    TotalPhotoCount = 1
                })
            };

            var service = new ProjectListService(index, _files, new QuietLogging());

            await service.RefreshFromProjectAsync("p2", TestProject.Combined(TestProject.Book(1, At("gone.jpg"))));

            var saved = Assert.Single(Parse(index.Json));
            Assert.Empty(saved.PreviewPhotos);
        }

        private string At(string name) => Path.Combine(_root, name);

        private static string Json(params ProjectInfo[] entries) => JsonSerializer.Serialize(
            new List<ProjectInfo>(entries),
            new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });

        private static List<ProjectInfo> Parse(string? json) => JsonSerializer.Deserialize<List<ProjectInfo>>(
            json!,
            new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            })!;

        /// <summary>The index, kept in a string: a real write is a file, and the rule is the same.</summary>
        private sealed class MemoryIndex : IProjectIndexStore
        {
            public string? Json { get; set; }

            public string GetProjectFilePath(string projectId) => "/projects/" + projectId + ".json";

            public Task<string?> ReadAsync(CancellationToken cancellationToken = default) =>
                Task.FromResult(Json);

            public Task WriteAsync(string json, CancellationToken cancellationToken = default)
            {
                Json = json;
                return Task.CompletedTask;
            }

            public bool DeleteProjectFile(string projectId) => false;

            public Task<string?> ReadLegacyAsync(string mode, CancellationToken cancellationToken = default) =>
                Task.FromResult<string?>(null);
        }

        private sealed class QuietLogging : ILoggingService
        {
            public void LogInfo(string message) { }

            public void LogError(string message, Exception? exception = null) { }

            public void LogWarning(string message) { }
        }
    }
}
