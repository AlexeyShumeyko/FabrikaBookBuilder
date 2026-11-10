using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using PhotoBook.Application;
using PhotoBook.Application.Sites;
using PhotoBook.Application.Sites.Catalog;

namespace PhotoBook.Infrastructure.Sites.Catalog
{
    /// <summary>
    /// One small file per document under the customer's profile, replaced atomically.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Files rather than one big file: a catalog of a dozen products is written and read
    /// independently, and a half-written product does not cost the others their copy.
    /// </para>
    /// <para>
    /// The schema version is part of the name, not of the contents. When the stored shape
    /// changes, the new version simply looks for other files and the old ones are never read
    /// again - no migration code, and no chance of parsing yesterday's shape as today's.
    /// </para>
    /// <para>
    /// A write goes to a sibling file and is then renamed over the target, so a reader sees the
    /// old document or the new one and never a truncated JSON. Failing to write the cache is not
    /// a failure of the program: the network copy still exists, so the error is journalled and
    /// the window is opened without the cache.
    /// </para>
    /// </remarks>
    internal sealed class FileSiteCatalogCache : ISiteCatalogCache
    {
        /// <summary>Bumped when the stored shape changes; the name carries it.</summary>
        internal const int SchemaVersion = 1;

        /// <summary>How long a stored document is considered current.</summary>
        internal static readonly TimeSpan Lifetime = TimeSpan.FromHours(6);

        private const string Source = "site-catalog";

        // The catalog is full of Russian titles, and escaping them into Л... makes the
        // stored document unreadable when someone has to look at it. Nothing here is HTML.
        private static readonly JsonSerializerOptions EnvelopeOptions = new()
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        private readonly string _directory;
        private readonly Func<DateTimeOffset> _now;
        private readonly TimeSpan _lifetime;
        private readonly IJournal _journal;

        public FileSiteCatalogCache(
            IJournal journal,
            string? directory = null,
            Func<DateTimeOffset>? now = null,
            TimeSpan? lifetime = null)
        {
            _journal = journal ?? throw new ArgumentNullException(nameof(journal));
            _directory = directory ?? CombineDefaultPath();
            _now = now ?? (() => DateTimeOffset.UtcNow);
            _lifetime = lifetime ?? Lifetime;
        }

        /// <summary>
        /// Under the user's profile rather than beside the executable: the program is installed
        /// once for everyone, and the cache belongs to the person who signed in.
        /// </summary>
        private static string CombineDefaultPath() => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PhotoBookRenamer",
            "SiteCatalog");

        public CachedSiteDocument? Read(SiteRegion region, string document)
        {
            if (region is null)
                throw new ArgumentNullException(nameof(region));

            var path = PathFor(region, document);
            if (!File.Exists(path))
                return null;

            string text;
            try
            {
                text = File.ReadAllText(path);
            }
            catch (FileNotFoundException)
            {
                // It went away between the check and the read; that is a miss, not an error.
                return null;
            }
            catch (DirectoryNotFoundException)
            {
                return null;
            }
            catch (IOException error)
            {
                _journal.Write(
                    JournalLevel.Warning,
                    Source,
                    $"Не удалось прочитать кэш «{document}», будет запрошен заново.",
                    error);
                return null;
            }

            try
            {
                using var envelope = JsonDocument.Parse(text);
                var root = envelope.RootElement;

                if (!root.TryGetProperty("storedAt", out var storedElement) ||
                    !storedElement.TryGetDateTimeOffset(out var storedAt) ||
                    !root.TryGetProperty("document", out var documentElement))
                {
                    throw new JsonException("В записи кэша нет даты или документа.");
                }

                var isStale = _now() - storedAt >= _lifetime;

                return new CachedSiteDocument(documentElement.GetRawText(), storedAt, isStale);
            }
            catch (JsonException error)
            {
                // A damaged entry cannot be repaired and would fail on every start, so it is
                // removed the moment it is found.
                _journal.Write(
                    JournalLevel.Warning,
                    Source,
                    $"Кэш «{document}» повреждён и удалён, документ будет запрошен заново.",
                    error);
                Delete(path);

                return null;
            }
        }

        public void Write(SiteRegion region, string document, string content)
        {
            if (region is null)
                throw new ArgumentNullException(nameof(region));
            if (content is null)
                throw new ArgumentNullException(nameof(content));

            // The document is embedded as JSON, so an empty or broken answer must be refused
            // here rather than stored as a string that no parser can use later.
            using var parsed = ParseDocument(content);

            var path = PathFor(region, document);
            var temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";

            try
            {
                Directory.CreateDirectory(_directory);

                var envelope = new CachedEnvelope(_now(), parsed.RootElement);
                File.WriteAllText(temporaryPath, JsonSerializer.Serialize(envelope, EnvelopeOptions));

                // A rename on one volume: a reader sees the old file or the new one, never a
                // half-written document.
                File.Move(temporaryPath, path, overwrite: true);
            }
            catch (IOException error)
            {
                _journal.Write(
                    JournalLevel.Warning,
                    Source,
                    $"Не удалось сохранить кэш «{document}».",
                    error);
            }
            catch (UnauthorizedAccessException error)
            {
                _journal.Write(
                    JournalLevel.Warning,
                    Source,
                    $"Не удалось сохранить кэш «{document}»: нет доступа к папке кэша.",
                    error);
            }
            finally
            {
                Delete(temporaryPath);
            }
        }

        public void Clear()
        {
            string[] files;
            try
            {
                if (!Directory.Exists(_directory))
                    return;

                files = Directory.GetFiles(
                    _directory,
                    $"catalog-v{SchemaVersion.ToString(CultureInfo.InvariantCulture)}-*.json");
            }
            catch (IOException error)
            {
                _journal.Write(JournalLevel.Warning, Source, "Не удалось прочитать папку кэша.", error);
                return;
            }
            catch (UnauthorizedAccessException error)
            {
                _journal.Write(JournalLevel.Warning, Source, "Нет доступа к папке кэша.", error);
                return;
            }

            foreach (var file in files)
                Delete(file);
        }

        /// <summary>The file a document of this site is stored in.</summary>
        internal string PathFor(SiteRegion region, string document)
        {
            if (string.IsNullOrWhiteSpace(document))
                throw new ArgumentException("Имя документа не задано.", nameof(document));

            var name = string.Join(
                "-",
                document.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries));

            return Path.Combine(
                _directory,
                $"catalog-v{SchemaVersion.ToString(CultureInfo.InvariantCulture)}-{region.Code}-{name}.json");
        }

        private static JsonDocument ParseDocument(string content)
        {
            try
            {
                return JsonDocument.Parse(content);
            }
            catch (JsonException error)
            {
                throw new ArgumentException(
                    "В кэш можно записать только JSON-документ.",
                    nameof(content),
                    error);
            }
        }

        private void Delete(string path)
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch (IOException error)
            {
                _journal.Write(JournalLevel.Warning, Source, "Не удалось удалить файл кэша.", error);
            }
            catch (UnauthorizedAccessException error)
            {
                _journal.Write(JournalLevel.Warning, Source, "Не удалось удалить файл кэша.", error);
            }
        }

        private sealed record CachedEnvelope(
            [property: JsonPropertyName("storedAt")] DateTimeOffset StoredAt,
            [property: JsonPropertyName("document")] JsonElement Document);
    }
}
