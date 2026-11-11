using System;
using System.Collections.Generic;
using PhotoBook.Application;
using PhotoBook.Application.Sites;
using PhotoBook.Application.Sites.Books;
using PhotoBook.Application.Sites.Upload;
using PhotoBook.Infrastructure.Sites;

namespace PhotoBook.Application.Tests.Sites.Upload
{
    /// <summary>
    /// The roadmap's criterion needs the record to outlive the program, so the tests read it back
    /// through a second instance rather than through the one that wrote it.
    /// </summary>
    public class FileAcceptedFilesStoreTests
    {
        [Fact]
        public void RemembersTheCountAcrossInstances()
        {
            using var folder = new TempFolder();
            var path = folder.PathTo("accepted.json");

            var written = new FileAcceptedFilesStore(new SilentJournal(), path)
                .Remember(new UploadSession(SiteRegion.Com, 527733, 10, 4));

            Assert.True(written.IsSuccess);
            Assert.Equal(4, ReadAt(path).Accepted(SiteRegion.Com, 527733, 10));
        }

        [Fact]
        public void DoesNotBelieveACountForABookWithAnotherNumberOfPages()
        {
            using var folder = new TempFolder();
            var path = folder.PathTo("accepted.json");

            new FileAcceptedFilesStore(new SilentJournal(), path)
                .Remember(new UploadSession(SiteRegion.Com, 527733, 10, 4));

            // The customer swapped a photograph: the book has another length now, and the old
            // count would skip pages that are not in the same places any more.
            Assert.Equal(0, ReadAt(path).Accepted(SiteRegion.Com, 527733, 12));
        }

        [Fact]
        public void KeepsTheTwoSitesApart()
        {
            using var folder = new TempFolder();
            var path = folder.PathTo("accepted.json");

            new FileAcceptedFilesStore(new SilentJournal(), path)
                .Remember(new UploadSession(SiteRegion.Com, 527733, 10, 4));

            Assert.Equal(0, ReadAt(path).Accepted(SiteRegion.By, 527733, 10));
        }

        [Fact]
        public void ForgetsAFinishedBook()
        {
            using var folder = new TempFolder();
            var path = folder.PathTo("accepted.json");
            var store = new FileAcceptedFilesStore(new SilentJournal(), path);

            store.Remember(new UploadSession(SiteRegion.Com, 527733, 10, 4));
            var forgotten = store.Forget(SiteRegion.Com, 527733);

            Assert.True(forgotten.IsSuccess);
            Assert.Equal(0, ReadAt(path).Accepted(SiteRegion.Com, 527733, 10));
        }

        [Fact]
        public void AnUnreadableRecordIsTreatedAsNothingRatherThanAsAFailure()
        {
            using var folder = new TempFolder();
            var path = folder.PathTo("accepted.json");
            System.IO.File.WriteAllText(path, "{ this is not json");

            var journal = new SilentJournal();
            var store = new FileAcceptedFilesStore(journal, path);

            Assert.Equal(0, store.Accepted(SiteRegion.Com, 527733, 10));
            Assert.True(store.Remember(new UploadSession(SiteRegion.Com, 527733, 10, 2)).IsSuccess);
            Assert.Single(journal.Entries);
        }

        [Fact]
        public void SaysSoWhenTheRecordCannotBeWritten()
        {
            using var folder = new TempFolder();

            // A file where the folder should be: the write cannot succeed whatever the disk does.
            var blocker = folder.PathTo("blocker");
            System.IO.File.WriteAllText(blocker, "x");

            var store = new FileAcceptedFilesStore(new SilentJournal(), System.IO.Path.Combine(blocker, "accepted.json"));
            var written = store.Remember(new UploadSession(SiteRegion.Com, 527733, 10, 2));

            Assert.False(written.IsSuccess);
            Assert.False(string.IsNullOrWhiteSpace(written.Error));
        }

        private static FileAcceptedFilesStore ReadAt(string path) => new(new SilentJournal(), path);

        private sealed class SilentJournal : IJournal
        {
            internal List<string> Entries { get; } = new();

            public void Write(JournalLevel level, string source, string message, Exception? error = null) =>
                Entries.Add(level + ": " + source + ": " + message);
        }

        private sealed class TempFolder : IDisposable
        {
            private readonly string _path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "bookbuilder-tests-" + Guid.NewGuid().ToString("N"));

            internal TempFolder() => System.IO.Directory.CreateDirectory(_path);

            internal string PathTo(string name) => System.IO.Path.Combine(_path, name);

            public void Dispose()
            {
                try
                {
                    System.IO.Directory.Delete(_path, recursive: true);
                }
                catch (System.IO.IOException)
                {
                    // The store's own tests do not turn a leftover folder into a failure.
                }
            }
        }
    }
}
