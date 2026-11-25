using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using PhotoBook.Application;
using PhotoBook.Infrastructure;

namespace PhotoBook.Shell.Services;

/// <summary>
/// The view the folder panel opens in: the row of cards, or the list.
/// </summary>
/// <remarks>
/// <para>
/// The cards until somebody says otherwise. A cover shows the first photographs of its
/// folder, so an order is judged by eye before a single name is read - which the list cannot
/// do at 58 pixels a row. Once the switch has been touched, the answer is theirs and it is
/// kept: flipping it back on every opening would make the switch look like a control that
/// does nothing.
/// </para>
/// <para>
/// It lives in the same file as the theme, beside the journal, and it is written as a key of
/// a JSON object so the settings this wave does not own survive. A file that cannot be read
/// is not a failure to show: the panel opens as a list.
/// </para>
/// </remarks>
internal static class PickerViewPreference
{
    private const string FileName = "settings.json";
    private const string ViewKey = "foldersView";
    private const string PreviewsKey = "folderPreviews";

    private const string ListName = "list";
    private const string GridName = "grid";

    /// <summary>Where the choice is kept. The same folder as the theme and the journal.</summary>
    private static string FilePath => Path.Combine(Diagnostics.Directory, FileName);

    /// <summary>Whether the panel opens as a list. False unless the list was chosen and kept.</summary>
    public static bool LoadList()
    {
        try
        {
            if (!File.Exists(FilePath)) return false;

            if (JsonNode.Parse(File.ReadAllText(FilePath)) is not JsonObject settings) return false;

            if (settings[ViewKey] is not JsonValue value || !value.TryGetValue<string>(out var name))
                return false;

            return string.Equals(name, ListName, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            Journal.Write(
                JournalLevel.Warning,
                "folders",
                "Настройка вида папок не прочиталась, открыта плитка",
                ex);
            return false;
        }
    }

    /// <summary>
    /// Remembers the view. A failure here is written down and not shown: the list has already
    /// changed on screen, and a warning about remembering it would answer a question nobody
    /// asked.
    /// </summary>
    public static void Save(bool list)
        => Save(settings => settings[ViewKey] = list ? ListName : GridName);

    /// <summary>
    /// Whether covers show the first photographs of their folder. On unless switched off.
    /// </summary>
    /// <remarks>
    /// On by default, because it is what the panel is for. A folder on a network drive makes
    /// every preview a read across the wire, so the switch has to exist and has to be kept: a
    /// client who turned it off once did not mean "for this opening".
    /// </remarks>
    public static bool LoadPreviews()
    {
        try
        {
            if (!File.Exists(FilePath)) return true;

            if (JsonNode.Parse(File.ReadAllText(FilePath)) is not JsonObject settings) return true;

            if (settings[PreviewsKey] is not JsonValue value || !value.TryGetValue<bool>(out var on))
                return true;

            return on;
        }
        catch (Exception ex)
        {
            Journal.Write(
                JournalLevel.Warning,
                "folders",
                "Настройка превью папок не прочиталась, превью включены",
                ex);
            return true;
        }
    }

    public static void SavePreviews(bool on)
        => Save(settings => settings[PreviewsKey] = on);

    /// <summary>Rewrites one key of the shared file, leaving everything else it holds alone.</summary>
    private static void Save(Action<JsonObject> change)
    {
        try
        {
            var settings = ReadExisting() ?? new JsonObject();
            change(settings);

            var directory = Diagnostics.Directory;
            Directory.CreateDirectory(directory);

            // Written beside the target and moved over it, so an interrupted write leaves the
            // previous file rather than half of a new one.
            var temporaryPath = FilePath + ".tmp";
            File.WriteAllText(temporaryPath, settings.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporaryPath, FilePath, overwrite: true);
        }
        catch (Exception ex)
        {
            Journal.Write(JournalLevel.Warning, "folders", "Настройки панели папок не сохранились", ex);
        }
    }

    /// <summary>The file as it stands, or nothing when it is missing or unreadable.</summary>
    private static JsonObject? ReadExisting()
    {
        try
        {
            if (!File.Exists(FilePath)) return null;

            return JsonNode.Parse(File.ReadAllText(FilePath)) as JsonObject;
        }
        catch
        {
            // A file that cannot be parsed is replaced rather than appended to: keeping the
            // broken text would mean the choice could never be stored again.
            return null;
        }
    }
}
