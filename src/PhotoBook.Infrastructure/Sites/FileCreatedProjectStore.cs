using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using PhotoBook.Application;
using PhotoBook.Application.Sites.Projects;

namespace PhotoBook.Infrastructure.Sites
{
    /// <summary>Keeps the keys of created projects in one file on the customer's profile.</summary>
    /// <remarks>
    /// <para>
    /// A file rather than the Credential Manager: these are not secrets, and the customer may
    /// reasonably have to look at what the program remembers. It is written beside the other
    /// site data, under the profile rather than beside the executable, because the program is
    /// installed once for everyone while the projects belong to the person who signed in.
    /// </para>
    /// <para>
    /// A write goes to a sibling file and is renamed over the target, so a reader sees either
    /// the old record or the new one. Failing to read it is reported as "nothing remembered"
    /// and journalled: the cost of that is one extra call the site would refuse to recognise
    /// as a repeat anyway, while a failed read that threw would stop an order that is fine.
    /// </para>
    /// </remarks>
    internal sealed class FileCreatedProjectStore : ICreatedProjectStore
    {
        private const string Source = "site-projects";

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

        public FileCreatedProjectStore(IJournal journal, string? path = null)
        {
            _journal = journal ?? throw new ArgumentNullException(nameof(journal));
            _path = path ?? CombineDefaultPath();
        }

        private static string CombineDefaultPath() => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PhotoBookRenamer",
            "SiteProjects",
            "created.json");

        public long? Find(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
                return null;

            lock (_gate)
            {
                var stored = Read();
                return stored.TryGetValue(key, out var projectId) ? projectId : null;
            }
        }

        public Result Remember(string key, long projectId)
        {
            if (string.IsNullOrWhiteSpace(key))
                return Result.Fail("Группу нельзя запомнить без имени.");

            if (projectId <= 0)
                return Result.Fail("Проект без номера нельзя запомнить.");

            lock (_gate)
            {
                var stored = Read();
                stored[key] = projectId;

                try
                {
                    var directory = Path.GetDirectoryName(_path);
                    if (!string.IsNullOrEmpty(directory))
                        Directory.CreateDirectory(directory);

                    var temporary = _path + ".tmp";
                    File.WriteAllText(temporary, JsonSerializer.Serialize(stored, Options));
                    File.Move(temporary, _path, overwrite: true);

                    return Result.Ok();
                }
                catch (Exception error) when (
                    error is IOException or UnauthorizedAccessException or NotSupportedException)
                {
                    _journal.Write(
                        JournalLevel.Warning,
                        Source,
                        "Не удалось сохранить список созданных проектов.",
                        error);

                    return Result.Fail(
                        "Не удалось сохранить запись о созданном проекте: " + error.Message);
                }
            }
        }

        /// <summary>What the file holds, or nothing when it is absent or unreadable.</summary>
        private Dictionary<string, long> Read()
        {
            if (!File.Exists(_path))
                return new Dictionary<string, long>(StringComparer.Ordinal);

            try
            {
                var text = File.ReadAllText(_path);
                var stored = JsonSerializer.Deserialize<Dictionary<string, long>>(text, Options);

                return stored is null
                    ? new Dictionary<string, long>(StringComparer.Ordinal)
                    : new Dictionary<string, long>(stored, StringComparer.Ordinal);
            }
            catch (Exception error) when (
                error is IOException or UnauthorizedAccessException or JsonException)
            {
                _journal.Write(
                    JournalLevel.Warning,
                    Source,
                    "Не удалось прочитать список созданных проектов; он будет создан заново.",
                    error);

                return new Dictionary<string, long>(StringComparer.Ordinal);
            }
        }
    }
}
