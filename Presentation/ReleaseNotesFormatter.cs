using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace PhotoBookRenamer.Presentation
{
    /// <summary>
    /// Turns release notes into something worth reading inside the app.
    ///
    /// <para>
    /// The notes are written as markdown so they read well on the release page, and this is
    /// what renders them for the person who sees the update dialog. Before this existed the
    /// dialog showed the raw file: lines beginning with "##", "**bold**" and "- bullets"
    /// exactly as typed. That is not release notes, that is a source file.
    /// </para>
    ///
    /// <para>
    /// It also removes the part of the release body that is not for the user. GitHub can
    /// append an automatically generated "What's Changed" block with commit links, the
    /// author's GitHub account and a compare URL, and the dialog used to show all of it:
    /// every client of the program was invited to look at the developer's repository. So
    /// those sections are dropped here as well - and not only those, because the fix in the
    /// release pipeline could be undone by anyone later: the dialog strips links and
    /// mentions on its own.
    /// </para>
    ///
    /// <para>
    /// A deliberately small subset of markdown: headings, bold, inline code, bullets and
    /// paragraphs. Anything else is shown as plain text. Rendering a full common-mark
    /// engine would be more code than the notes ever need.
    /// </para>
    /// </summary>
    internal static class ReleaseNotesFormatter
    {
        /// <summary>Sections GitHub appends, which have no business in the dialog.</summary>
        private static readonly string[] NoiseHeadings =
        {
            "what's changed", "new contributors", "full changelog", "compare"
        };

        private static readonly Regex Inline = new(
            @"\*\*(?<bold>[^*]+)\*\*|`(?<code>[^`]+)`|https?://\S+",
            RegexOptions.Compiled);

        private static readonly Regex Mention = new(@"@[A-Za-z0-9][\w-]*", RegexOptions.Compiled);

        public static FlowDocument Build(string? markdown)
        {
            var document = new FlowDocument
            {
                PagePadding = new System.Windows.Thickness(0),
                FontFamily = new FontFamily("Inter, Segoe UI"),
                FontSize = 12.5,
                TextAlignment = TextAlignment.Left,
                IsOptimalParagraphEnabled = true,
                LineHeight = 18
            };

            var lines = Clean((markdown ?? string.Empty).Replace("\r\n", "\n").Split('\n'));

            Paragraph? current = null;
            foreach (string line in lines)
            {
                string trimmed = line.Trim();

                if (trimmed.Length == 0)
                {
                    current = null;
                    continue;
                }

                if (trimmed.StartsWith("### ", StringComparison.Ordinal))
                {
                    document.Blocks.Add(Heading(trimmed[4..], 12.5, Brush(0x64748B), 12, 3));
                    current = null;
                    continue;
                }

                if (trimmed.StartsWith("## ", StringComparison.Ordinal))
                {
                    document.Blocks.Add(Heading(trimmed[3..], 14, Brush(0x0F172A), 0, 6));
                    current = null;
                    continue;
                }

                if (trimmed.StartsWith("# ", StringComparison.Ordinal))
                {
                    document.Blocks.Add(Heading(trimmed[2..], 15, Brush(0x0F172A), 0, 8));
                    current = null;
                    continue;
                }

                if (trimmed.StartsWith("- ", StringComparison.Ordinal) ||
                    trimmed.StartsWith("* ", StringComparison.Ordinal))
                {
                    document.Blocks.Add(Bullet(trimmed[2..]));
                    current = null;
                    continue;
                }

                current ??= new Paragraph { Margin = new System.Windows.Thickness(0, 0, 0, 8) };
                AppendInline(current, trimmed, Brush(0x334155));
                document.Blocks.Add(current);
                current = null;
            }

            if (document.Blocks.Count == 0)
            {
                document.Blocks.Add(new Paragraph(
                    Run("Обновления доступны. Ровер.", Brush(0x334155))));
            }

            return document;
        }

        /// <summary>
        /// Drops GitHub's generated tail, links and mentions, and anything that was left
        /// empty by that removal.
        /// </summary>
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

                // "## What's Changed", "**Full Changelog**: https://..." and friends.
                bool isNoiseHeading = NoiseHeadings.Any(n => probe.StartsWith(n, StringComparison.Ordinal));
                if (isNoiseHeading)
                {
                    // Everything from here on belongs to GitHub's block, not to us.
                    break;
                }

                // A generated bullet: "* something by @someone in https://github.com/...".
                if (line.TrimStart().StartsWith("* ", StringComparison.Ordinal) &&
                    line.Contains("@", StringComparison.Ordinal) &&
                    line.Contains("http", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                line = Mention.Replace(line, string.Empty);
                line = Regex.Replace(line, @"https?://\S+", string.Empty).TrimEnd();

                if (line.Trim().Length == 0) continue;
                kept.Add(line);
            }

            // Collapse the blank-line runs the removals leave behind.
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

        private static Paragraph Heading(string text, double size, Brush brush, double before, double after)
        {
            var paragraph = new Paragraph
            {
                Margin = new System.Windows.Thickness(0, before, 0, after),
                KeepWithNext = true
            };
            paragraph.Inlines.Add(Run(text, brush, size, bold: true));
            return paragraph;
        }

        private static Paragraph Bullet(string text)
        {
            var paragraph = new Paragraph
            {
                Margin = new System.Windows.Thickness(14, 0, 0, 4),
                TextIndent = -12
            };
            paragraph.Inlines.Add(Run("•   ", Brush(0x64748B)));
            AppendInline(paragraph, text, Brush(0x334155));
            return paragraph;
        }

        private static void AppendInline(Paragraph paragraph, string text, Brush brush)
        {
            int index = 0;
            foreach (Match match in Inline.Matches(text))
            {
                if (match.Index > index)
                {
                    paragraph.Inlines.Add(Run(text[index..match.Index], brush));
                }

                if (match.Groups["bold"].Success)
                {
                    paragraph.Inlines.Add(Run(match.Groups["bold"].Value, brush, bold: true));
                }
                else if (match.Groups["code"].Success)
                {
                    paragraph.Inlines.Add(Run(match.Groups["code"].Value, Brush(0x0369A1), 12, mono: true));
                }
                else
                {
                    // A URL: the text ends without it. In the dialog there is nowhere to
                    // click and nothing to gain, so the link is simply dropped.
                }

                index = match.Index + match.Length;
            }

            if (index < text.Length)
            {
                paragraph.Inlines.Add(Run(text[index..], brush));
            }
        }

        private static Run Run(string text, Brush brush, double size = 12.5, bool bold = false, bool mono = false)
        {
            var run = new Run(text)
            {
                Foreground = brush,
                FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal
            };

            if (Math.Abs(size - 12.5) > 0.01) run.FontSize = size;
            if (mono) run.FontFamily = new FontFamily("Consolas, Cascadia Mono, Courier New");
            return run;
        }

        /// <summary>#RRGGBB, without depending on a colour helper the view layer owns.</summary>
        private static SolidColorBrush Brush(uint rgb)
        {
            var brush = new SolidColorBrush(Color.FromRgb(
                (byte)((rgb >> 16) & 0xFF),
                (byte)((rgb >> 8) & 0xFF),
                (byte)(rgb & 0xFF)));
            brush.Freeze();
            return brush;
        }
    }
}