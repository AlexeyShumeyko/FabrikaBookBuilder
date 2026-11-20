using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using Avalonia.Styling;
using PhotoBook.Application;
using PhotoBook.Infrastructure;

namespace PhotoBook.Shell.Services;

/// <summary>
/// The theme the person chose, remembered between runs and across updates.
/// </summary>
/// <remarks>
/// <para>
/// It lives beside the journal and the project index, under the user's own profile, because
/// that is the one place an installer does not replace. A setting kept in the program folder
/// is a setting lost at the next update - and a person who chose the dark theme, then found
/// the light one again after upgrading, would choose it once more and stop believing the
/// choice is kept at all.
/// </para>
/// <para>
/// The file is read and written as a JSON object rather than as a serialised class, so that
/// the keys this wave does not own - everything later waves will put in settings.json - are
/// carried through untouched instead of being dropped by a typed model that never knew them.
/// </para>
/// <para>
/// A broken or unreadable file is not an error the person needs to see: the shell opens in
/// the light theme, which is the default, and the journal holds the reason.
/// </para>
/// </remarks>
internal static class ThemePreference
{
    private const string FileName = "settings.json";
    private const string ThemeKey = "theme";

    private const string LightName = "light";
    private const string DarkName = "dark";

    /// <summary>Where the choice is kept. The same folder as the journal.</summary>
    public static string FilePath => Path.Combine(Diagnostics.Directory, FileName);

    /// <summary>
    /// The stored theme. Light when nobody has chosen one, and light when the file cannot
    /// be read - a preference is not worth a failure the person has to deal with.
    /// </summary>
    public static ThemeVariant Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return ThemeVariant.Light;

            if (JsonNode.Parse(File.ReadAllText(FilePath)) is not JsonObject settings) return ThemeVariant.Light;

            if (settings[ThemeKey] is not JsonValue value || !value.TryGetValue<string>(out var name))
                return ThemeVariant.Light;

            return string.Equals(name, DarkName, StringComparison.OrdinalIgnoreCase)
                ? ThemeVariant.Dark
                : ThemeVariant.Light;
        }
        catch (Exception ex)
        {
            Journal.Write(
                JournalLevel.Warning,
                "theme",
                "Настройка темы не прочиталась, открыта светлая",
                ex);
            return ThemeVariant.Light;
        }
    }

    /// <summary>
    /// Remembers the choice. A failure here is written down and not shown: the theme has
    /// already changed on screen, and a warning about remembering it would be a second
    /// answer to a question the person did not ask.
    /// </summary>
    public static void Save(ThemeVariant variant)
    {
        try
        {
            var settings = ReadExisting() ?? new JsonObject();
            settings[ThemeKey] = variant == ThemeVariant.Dark ? DarkName : LightName;

            var directory = Diagnostics.Directory;
            Directory.CreateDirectory(directory);

            // Written to a sibling and moved over the target, so an interrupted write leaves
            // the previous file rather than half of a new one.
            var temporaryPath = FilePath + ".tmp";
            File.WriteAllText(temporaryPath, settings.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporaryPath, FilePath, overwrite: true);
        }
        catch (Exception ex)
        {
            Journal.Write(
                JournalLevel.Warning,
                "theme",
                "Выбор темы не сохранился",
                ex);
        }
    }

    /// <summary>
    /// The file as it stands, or nothing when it is missing or unreadable. Every other key
    /// in it stays where it was.
    /// </summary>
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
            // broken text would mean the theme could never be stored again.
            return null;
        }
    }
}
