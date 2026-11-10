using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PhotoBook.Application;
using PhotoBook.Application.Sites;
using PhotoBook.Application.Sites.Catalog;
using PhotoBook.Infrastructure.Sites.Catalog;

namespace PhotoBook.Application.Tests.Sites.Catalog
{
    /// <summary>
    /// The cache is what lets the upload window open without a network, so it is checked for the
    /// states a file on a customer's machine really takes: fresh, old, damaged, and unwritable.
    /// </summary>
    public class FileSiteCatalogCacheTests : IDisposable
    {
        private static readonly DateTimeOffset Now = new(2026, 11, 10, 12, 0, 0, TimeSpan.Zero);
        private const string Document = "{\"data\":{\"id\":4494,\"title\":\"Лайфлат\"}}";

        private readonly string _directory = Path.Combine(
            Path.GetTempPath(),
            "bookbuilder-cache-tests",
            Guid.NewGuid().ToString("N"));

        public void Dispose()
        {
            if (Directory.Exists(_directory))
                Directory.Delete(_directory, recursive: true);
        }

        private FileSiteCatalogCache CreateCache(
            RecordingJournal? journal = null,
            DateTimeOffset? now = null,
            string? directory = null) =>
            new(journal ?? new RecordingJournal(), directory ?? _directory, () => now ?? Now);

        [Fact]
        public void ReadsBackWhatItStored()
        {
            var cache = CreateCache();

            cache.Write(SiteRegion.Com, SiteCatalogDocuments.Page, Document);
            var stored = cache.Read(SiteRegion.Com, SiteCatalogDocuments.Page);

            Assert.NotNull(stored);
            Assert.Equal(Document, stored!.Content);
            Assert.Equal(Now, stored.StoredAt);
            Assert.False(stored.IsStale);
        }

        [Fact]
        public void AnswersNothingForADocumentItNeverStored()
        {
            var cache = CreateCache();

            Assert.Null(cache.Read(SiteRegion.Com, SiteCatalogDocuments.Page));
        }

        [Fact]
        public void ReturnsAnOldDocumentMarkedStale()
        {
            CreateCache().Write(SiteRegion.Com, SiteCatalogDocuments.Page, Document);

            var later = CreateCache(now: Now.AddHours(7));
            var stored = later.Read(SiteRegion.Com, SiteCatalogDocuments.Page);

            Assert.NotNull(stored);
            Assert.True(stored!.IsStale);
            Assert.Equal(Document, stored.Content);
        }

        [Fact]
        public void KeepsADocumentCurrentJustInsideItsLifetime()
        {
            CreateCache().Write(SiteRegion.Com, SiteCatalogDocuments.Page, Document);

            var stored = CreateCache(now: Now.AddHours(5).AddMinutes(59))
                .Read(SiteRegion.Com, SiteCatalogDocuments.Page);

            Assert.False(stored!.IsStale);
        }

        [Fact]
        public void NamesTheFileWithTheSchemaVersionAndTheSite()
        {
            var path = CreateCache().PathFor(SiteRegion.By, SiteCatalogDocuments.Products);

            Assert.Contains($"catalog-v{FileSiteCatalogCache.SchemaVersion}", Path.GetFileName(path));
            Assert.Contains("-by-", Path.GetFileName(path));
            Assert.EndsWith("products-data.json", Path.GetFileName(path), StringComparison.Ordinal);
        }

        [Fact]
        public void DoesNotMixSitesOrDocuments()
        {
            var cache = CreateCache();
            cache.Write(SiteRegion.Com, SiteCatalogDocuments.Page, "{\"site\":\"com-page\"}");
            cache.Write(SiteRegion.Com, SiteCatalogDocuments.Products, "{\"site\":\"com-products\"}");
            cache.Write(SiteRegion.By, SiteCatalogDocuments.Page, "{\"site\":\"by-page\"}");

            Assert.Contains("com-page", cache.Read(SiteRegion.Com, SiteCatalogDocuments.Page)!.Content);
            Assert.Contains("com-products", cache.Read(SiteRegion.Com, SiteCatalogDocuments.Products)!.Content);
            Assert.Contains("by-page", cache.Read(SiteRegion.By, SiteCatalogDocuments.Page)!.Content);
        }

        [Fact]
        public void LeavesNoTemporaryFileBehind()
        {
            var cache = CreateCache();
            cache.Write(SiteRegion.Com, SiteCatalogDocuments.Page, Document);

            Assert.Empty(Directory.GetFiles(_directory, "*.tmp"));
        }

        [Fact]
        public void DropsADamagedDocumentAndSaysSo()
        {
            var journal = new RecordingJournal();
            var cache = CreateCache(journal);
            var path = cache.PathFor(SiteRegion.Com, SiteCatalogDocuments.Page);
            Directory.CreateDirectory(_directory);
            File.WriteAllText(path, "{ this is not json");

            Assert.Null(cache.Read(SiteRegion.Com, SiteCatalogDocuments.Page));

            Assert.False(File.Exists(path));
            Assert.Contains(journal.Entries, entry => entry.Message.Contains("повреждён"));
        }

        [Fact]
        public void RefusesToStoreSomethingThatIsNotADocument()
        {
            var cache = CreateCache();

            Assert.Throws<ArgumentException>(() =>
                cache.Write(SiteRegion.Com, SiteCatalogDocuments.Page, "<html>not json</html>"));
        }

        [Fact]
        public void ReportsAnUnwritableCacheWithoutFailingTheWindow()
        {
            var journal = new RecordingJournal();

            // A file where the cache folder should be: a real state when a profile is broken.
            Directory.CreateDirectory(Path.GetDirectoryName(_directory)!);
            File.WriteAllText(_directory, "not a folder");

            var cache = CreateCache(journal);

            cache.Write(SiteRegion.Com, SiteCatalogDocuments.Page, Document);
            Assert.Contains(journal.Entries, entry => entry.Message.Contains("Не удалось сохранить кэш"));
            Assert.Null(cache.Read(SiteRegion.Com, SiteCatalogDocuments.Page));

            File.Delete(_directory);
        }

        private sealed class RecordingJournal : IJournal
        {
            public List<(JournalLevel Level, string Source, string Message, Exception? Error)> Entries { get; }
                = new();

            public void Write(JournalLevel level, string source, string message, Exception? error = null)
                => Entries.Add((level, source, message, error));
        }
    }
}
