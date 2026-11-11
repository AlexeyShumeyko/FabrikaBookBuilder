using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using PhotoBook.Application.Sites.Books;
using PhotoBook.Application.Sites.Books.Files;

namespace PhotoBook.Application.Tests.Sites.Books.Files
{
    /// <summary>
    /// The inspector is the only part that touches the disk, so it is where a missing file, a
    /// damaged one and a readable one are told apart.
    /// </summary>
    public class BookFileInspectorTests
    {
        private static PageFile Page(string path) => new(path, LengthBytes: 4096);

        [Fact]
        public async Task ReportsAPresentFileWithItsSize()
        {
            var files = new StubFiles(@"C:\photos\1.jpg");
            var images = new StubImages(4913, 3495);

            var facts = await new BookFileInspector(files, images)
                .InspectAsync(new[] { Page(@"C:\photos\1.jpg") });

            var fact = Assert.Single(facts);
            Assert.True(fact.Exists);
            Assert.Equal(4913, fact.Size?.Width);
            Assert.Equal(3495, fact.Size?.Height);
        }

        [Fact]
        public async Task DoesNotReadTheSizeOfAFileThatIsNotThere()
        {
            var files = new StubFiles();
            var images = new StubImages(4913, 3495);

            var facts = await new BookFileInspector(files, images)
                .InspectAsync(new[] { Page(@"C:\photos\gone.jpg") });

            var fact = Assert.Single(facts);
            Assert.False(fact.Exists);
            Assert.Null(fact.Size);
            Assert.Empty(images.Asked);
        }

        [Fact]
        public async Task ReportsAFileThatIsThereButSaysNothing()
        {
            var files = new StubFiles(@"C:\photos\broken.jpg");
            var images = new StubImages(0, 0);

            var facts = await new BookFileInspector(files, images)
                .InspectAsync(new[] { Page(@"C:\photos\broken.jpg") });

            var fact = Assert.Single(facts);
            Assert.True(fact.Exists);
            Assert.Null(fact.Size);
        }

        [Fact]
        public async Task KeepsThePagesInOrder()
        {
            var files = new StubFiles(@"C:\photos\3.jpg", @"C:\photos\1.jpg", @"C:\photos\2.jpg");
            var images = new StubImages(4913, 3495);
            var pages = new[]
            {
                Page(@"C:\photos\1.jpg"),
                Page(@"C:\photos\2.jpg"),
                Page(@"C:\photos\3.jpg")
            };

            var facts = await new BookFileInspector(files, images).InspectAsync(pages);

            Assert.Equal(pages.Length, facts.Count);
            for (var index = 0; index < pages.Length; index++)
                Assert.Equal(pages[index].Path, facts[index].Path);
        }

        [Fact]
        public async Task StopsBetweenPagesWhenCancelled()
        {
            var files = new StubFiles(@"C:\photos\1.jpg");
            var images = new StubImages(4913, 3495);
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                new BookFileInspector(files, images)
                    .InspectAsync(new[] { Page(@"C:\photos\1.jpg") }, cancellation.Token));

            Assert.Empty(images.Asked);
        }

        private sealed class StubFiles : IFileService
        {
            private readonly HashSet<string> _present;

            internal StubFiles(params string[] present) =>
                _present = new HashSet<string>(present, StringComparer.OrdinalIgnoreCase);

            public bool FileExists(string path) => _present.Contains(path);

            public Task<List<string>> GetJpegFilesAsync(string folderPath) =>
                throw new NotSupportedException();

            public Task<ValidationResult> ValidateFoldersDetailedAsync(List<string> folderPaths) =>
                throw new NotSupportedException();

            public Task CopyFileAsync(string source, string destination) =>
                throw new NotSupportedException();

            public bool IsJpegFile(string filePath) => throw new NotSupportedException();

            public bool DirectoryExists(string path) => throw new NotSupportedException();

            public void EnsureDirectory(string path) => throw new NotSupportedException();
        }

        private sealed class StubImages : IImageService
        {
            private readonly int _width;
            private readonly int _height;

            internal StubImages(int width, int height)
            {
                _width = width;
                _height = height;
            }

            internal List<string> Asked { get; } = new();

            public Task<(int Width, int Height)> GetImageDimensionsAsync(string filePath)
            {
                Asked.Add(filePath);
                return Task.FromResult((_width, _height));
            }

            public Task<string?> DetectCoverAsync(string[] filePaths) =>
                Task.FromResult<string?>(null);
        }
    }
}
