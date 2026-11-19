using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using PhotoBook.Application;
using PhotoBook.Application.Sites.Projects;

namespace PhotoBook.Infrastructure.Sites
{
    /// <summary>Keeps the projects finished groups went to, in one file on the profile.</summary>
    /// <remarks>
    /// Written beside the created-project record and in the same shape: the key names the group,
    /// the value is the project the site holds. A failure to read is reported as "nothing
    /// uploaded", which only means the page will not mark a group by itself; a failure to write
    /// is journalled, because a silently forgotten upload is what makes a customer send the same
    /// books twice.
    /// </remarks>
    internal sealed class FileUploadedProjectStore : IUploadedProjectStore
    {
        private const string Source = "site-uploads";

        private static readonly JsonSerializerOptions Options = new()
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            WriteIndented = true
        };

        private readonly string _path;
        private readonly IJournal _journal;
        private readonly object _gate = new();

        public FileUploadedProjectStore(IJournal journal, string? path = null)
        {
            _journal = journal ?? throw new ArgumentNullException(nameof(journal));
            _path = path ?? CombineDefaultPath();
        }

        private static string CombineDefaultPath() => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PhotoBookRenamer",
            "SiteProjects",
            "uploaded.json");

        public long? FindUploaded(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
                return null;

            lock (_gate)
            {
                var stored = Read();
                return stored.TryGetValue(key, out var projectId) ? projectId : null;
            }
        }

        public Result RememberUploaded(string key, long projectId)
        {
            if (string.IsNullOrWhiteSpace(key))
                return Result.Fail("Группу нельзя запомнить без имени.");

            if (projectId <= 0)
                return Result.Fail("Проект без номера нельзя запомнить.");

            lock (_gate)
            {
                var stored = Read();
                stored[key] = projectId;

                return Save(stored);
            }
        }

        private Result Save(Dictionary<string, long> stored)
        {
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
                    "Не удалось сохранить список загруженных групп.",
                    error);

                return Result.Fail(
                    "Не удалось сохранить запись о загруженной группе: " + error.Message);
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
                    "Не удалось прочитать список загруженных групп.",
                    error);

                return new Dictionary<string, long>(StringComparer.Ordinal);
            }
        }
    }
}
