using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace PhotoBook.Application
{
    /// <summary>One line of a release note, already classified.</summary>
    /// <param name="Kind">A heading, a bullet, or a plain sentence.</param>
    /// <param name="Level">How deep a heading is: one for a title, two for a section.</param>
    /// <param name="Text">The text, with the markup taken out.</param>
    public readonly record struct ReleaseNoteLine(ReleaseNoteKind Kind, int Level, string Text);

    /// <summary>What a line of a release note is.</summary>
    public enum ReleaseNoteKind
    {
        Heading,
        Bullet,
        Sentence
    }

    /// <summary>
    /// Release notes, with everything that does not belong in front of a person removed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The other shell renders these into a formatted document; this one hands out plain
    /// classified lines and lets the shell draw them. The rules are the point and they are
    /// here rather than in a window: what a release note says is the same whatever draws it.
    /// </para>
    /// <para>
    /// What comes out: no markdown marks, no links, no account names, and none of the
    /// sections the hosting site appends automatically - which are the author's business and
    /// nobody's reason to read them mid-project.
    /// </para>
    /// </remarks>
    public static class ReleaseNotesText
    {
        /// <summary>Sections the hosting site appends, which have no business in a dialog.</summary>
        private static readonly string[] NoiseHeadings =
        {
            "what's changed", "new contributors", "full changelog", "compare"
        };

        private static readonly Regex Mention = new(@"@[A-Za-z0-9][\w-]*", RegexOptions.Compiled);
        private static readonly Regex Link = new(@"https?://\S+", RegexOptions.Compiled);
        private static readonly Regex Bold = new(@"\*\*(?<bold>[^*]+)\*\*", RegexOptions.Compiled);
        private static readonly Regex Code = new(@"`(?<code>[^`]+)`", RegexOptions.Compiled);

        /// <summary>The notes, ready to be drawn.</summary>
        public static IReadOnlyList<ReleaseNoteLine> Parse(string? markdown)
        {
            if (string.IsNullOrWhiteSpace(markdown)) return Array.Empty<ReleaseNoteLine>();

            return Clean(markdown.Replace("\r\n", "\n").Split('\n'))
                .Select(Classify)
                .Where(line => line.Kind != ReleaseNoteKind.Sentence || line.Text.Length > 0)
                .ToArray();
        }

        private static ReleaseNoteLine Classify(string line)
        {
            string trimmed = line.Trim();

            if (trimmed.StartsWith("#", StringComparison.Ordinal))
            {
                int level = 0;
                while (level < trimmed.Length && trimmed[level] == '#') level++;

                return new ReleaseNoteLine(
                    ReleaseNoteKind.Heading,
                    Math.Clamp(level, 1, 3),
                    Strip(trimmed[level..].Trim()));
            }

            if (trimmed.StartsWith("* ", StringComparison.Ordinal) ||
                trimmed.StartsWith("- ", StringComparison.Ordinal))
            {
                return new ReleaseNoteLine(ReleaseNoteKind.Bullet, 0, Strip(trimmed[1..].Trim()));
            }

            return new ReleaseNoteLine(ReleaseNoteKind.Sentence, 0, Strip(trimmed));
        }

        /// <summary>Marks out, keeping the words.</summary>
        private static string Strip(string text)
        {
            string result = Bold.Replace(text, "$bold");
            result = Code.Replace(result, "$code");
            return result.Trim();
        }

        private static List<string> Clean(string[] lines)
        {
            var kept = new List<string>();

            foreach (string raw in lines)
            {
                string line = raw.TrimEnd();
                string probe = line.TrimStart('#', ' ', '*', ' ')
                                      .Replace("**", string.Empty)
                                      .TrimStart()
                                      .ToLowerInvariant();

                // "## What's Changed" and everything under it belongs to the hosting site.
                if (NoiseHeadings.Any(n => probe.StartsWith(n, StringComparison.Ordinal))) break;

                // A generated entry: "* something by @someone in https://...".
                if (line.TrimStart().StartsWith("* ", StringComparison.Ordinal) &&
                    line.Contains("@", StringComparison.Ordinal) &&
                    line.Contains("http", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                line = Mention.Replace(line, string.Empty);
                line = Link.Replace(line, string.Empty).TrimEnd();

                if (line.Trim().Length == 0) continue;
                kept.Add(line);
            }

            // Collapse the blank runs the removals leave behind.
            var result = new List<string>(kept.Count);
            bool previousBlank = true;

            foreach (string line in kept)
            {
                bool blank = line.Trim().Length == 0;
                if (!blank || !previousBlank) result.Add(line);
                previousBlank = blank;
            }

            return result;
        }
    }
}
