using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using PhotoBook.Application;
using PhotoBook.Application.Sites;
using PhotoBook.Application.Sites.Books;
using PhotoBook.Application.Sites.Upload;

namespace PhotoBook.Infrastructure.Sites
{
    /// <summary>Keeps how many pages each project holds in one file on the customer's profile.</summary>
    /// <remarks>
    /// <para>
    /// Not a secret, so a file rather than the Credential Manager, and beside the created
    /// projects for the same reason: the customer may have to look at what the program remembers.
    /// A write goes to a sibling file and is renamed over the target, so a reader sees either the
    /// old count or the new one and never half of each.
    /// </para>
    /// <para>
    /// The count is believed only while the book still has the same number of pages. A record
    /// that outlived its book - a photograph was swapped, the book grew - would otherwise tell
    /// the next upload to skip pages that are no longer there.
    /// </para>
    /// </remarks>
    internal sealed class FileAcceptedFilesStore : IAcceptedFilesStore
    {
        private const string Source = "site-uploads";

        /// <summary>Bumped when the stored shape changes; older files are simply not read.</summary>
        internal const int SchemaVersion = 1;

        private static readonly JsonSerializerOptions Options = new()
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            WriteIndented = true
        };

        private readonly string _path;
        private readonly IJournal _journal;
        private readonly object _gate = new();

        /// <summary>What the file held when it was first read; it is not re-read for every page.</summary>
        private Dictionary<string, Acceptance>? _known;

        public FileAcceptedFilesStore(IJournal journal, string? path = null)
        {
            _journal = journal ?? throw new ArgumentNullException(nameof(journal));
            _path = path ?? CombineDefaultPath();
        }

        private static string CombineDefaultPath() => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PhotoBookRenamer",
            "SiteUploads",
            "accepted.json");

        public int Accepted(SiteRegion region, long projectId, int filesTotal)
        {
            if (region is null || projectId <= 0)
                return 0;

            lock (_gate)
            {
                return Read().TryGetValue(Key(region, projectId), out var stored) &&
                    stored.FilesTotal == filesTotal
                    ? Math.Clamp(stored.FilesUploaded, 0, filesTotal)
                    : 0;
            }
        }

        public Result Remember(UploadSession session)
        {
            if (session is null)
                return Result.Fail("Нечего запоминать: загрузка не начата.");

            if (session.SiteProjectId <= 0)
                return Result.Fail("Проект без номера нельзя запомнить.");

            lock (_gate)
            {
                var stored = Read();
                stored[Key(session.Region, session.SiteProjectId)] =
                    new Acceptance(session.FilesTotal, session.FilesUploaded);

                return Write(stored, "Не удалось сохранить, сколько страниц уже загружено.");
            }
        }

        public Result Forget(SiteRegion region, long projectId)
        {
            if (region is null || projectId <= 0)
                return Result.Ok();

            lock (_gate)
            {
                var stored = Read();
                if (!stored.Remove(Key(region, projectId)))
                    return Result.Ok();

                return Write(stored, "Не удалось удалить запись о законченной загрузке.");
            }
        }

        private static string Key(SiteRegion region, long projectId) =>
            region.Code + ":" + projectId.ToString(CultureInfo.InvariantCulture);

        private Result Write(Dictionary<string, Acceptance> stored, string failure)
        {
            try
            {
                var directory = Path.GetDirectoryName(_path);
                if (!string.IsNullOrEmpty(directory))
                    Directory.CreateDirectory(directory);

                var temporary = _path + ".tmp";
                File.WriteAllText(temporary, JsonSerializer.Serialize(stored, Options));
                File.Move(temporary, _path, overwrite: true);

                _known = stored;

                return Result.Ok();
            }
            catch (Exception error) when (
                error is IOException or UnauthorizedAccessException or NotSupportedException)
            {
                _journal.Write(JournalLevel.Warning, Source, failure, error);

                return Result.Fail(failure + " " + error.Message);
            }
        }

        /// <summary>What the file holds, or nothing when it is absent or unreadable.</summary>
        private Dictionary<string, Acceptance> Read()
        {
            if (_known is not null)
                return new Dictionary<string, Acceptance>(_known, StringComparer.Ordinal);

            if (!File.Exists(_path))
            {
                _known = new Dictionary<string, Acceptance>(StringComparer.Ordinal);
                return new Dictionary<string, Acceptance>(_known, StringComparer.Ordinal);
            }

            try
            {
                var text = File.ReadAllText(_path);
                var stored = JsonSerializer.Deserialize<Dictionary<string, Acceptance>>(text, Options);

                _known = stored is null
                    ? new Dictionary<string, Acceptance>(StringComparer.Ordinal)
                    : new Dictionary<string, Acceptance>(stored, StringComparer.Ordinal);

                return new Dictionary<string, Acceptance>(_known, StringComparer.Ordinal);
            }
            catch (Exception error) when (
                error is IOException or UnauthorizedAccessException or JsonException)
            {
                _journal.Write(
                    JournalLevel.Warning,
                    Source,
                    "Не удалось прочитать, сколько страниц уже загружено; загрузка начнётся заново.",
                    error);

                _known = new Dictionary<string, Acceptance>(StringComparer.Ordinal);

                return new Dictionary<string, Acceptance>(_known, StringComparer.Ordinal);
            }
        }

        /// <summary>How many pages of one book the site holds.</summary>
        private sealed record Acceptance(int FilesTotal, int FilesUploaded);
    }
}
