using System;
using System.Collections.Generic;
using PhotoBook.Application.Sites.Catalog.Layout;

namespace PhotoBook.Application.Sites.Books.Files
{
    /// <summary>What one page's file has to be: a pixel size, the slack allowed, the file kinds.</summary>
    /// <remarks>
    /// <para>
    /// Every number here comes from the site: the size from its layout (<see
    /// cref="SiteLayout.RequiredPixels"/>), the slack from the range's own <c>pageSizeError</c>
    /// converted at the site's resolution. A size written into the program would be wrong the
    /// first time a product changed, and nothing would fail to say so.
    /// </para>
    /// <para>
    /// The tolerance is not cosmetic: the site sends the same pixel size it checks against, and
    /// files a few pixels off are the normal result of another program's rounding, so a page a
    /// pixel wider is reported rather than refused.
    /// </para>
    /// </remarks>
    /// <param name="Required">The page plus its margins, in pixels at the site's resolution.</param>
    /// <param name="TolerancePixels">How far from the required size a file may be, in pixels.</param>
    /// <param name="Extensions">The file kinds the site's uploader takes.</param>
    public sealed record PageImageRule(
        PixelSize Required,
        int TolerancePixels,
        IReadOnlyList<string> Extensions)
    {
        /// <summary>
        /// The rule for one page, from the size the site asks for and the error it allows.
        /// </summary>
        public static PageImageRule For(PixelSize required, double toleranceMm, int resolutionDpi)
        {
            if (required is null)
                throw new ArgumentNullException(nameof(required));

            return new PageImageRule(
                required,
                Math.Max(0, PageGeometry.ToPixels(toleranceMm, resolutionDpi)),
                JpegFiles.Extensions);
        }

        /// <summary>Whether the file is of a kind the site takes, judged by its name alone.</summary>
        public bool Accepts(string path) => JpegFiles.Is(path);

        /// <summary>Whether the picture is narrower or shorter than the page needs.</summary>
        public bool IsTooSmall(PixelSize size) =>
            size.Width < Required.Width - TolerancePixels ||
            size.Height < Required.Height - TolerancePixels;

        /// <summary>Whether the picture is wider or taller than the page allows.</summary>
        public bool IsTooLarge(PixelSize size) =>
            size.Width > Required.Width + TolerancePixels ||
            size.Height > Required.Height + TolerancePixels;

        /// <summary>Whether the picture is not exactly the page's size, but close enough.</summary>
        public bool IsWithinTolerance(PixelSize size) =>
            size != Required && !IsTooSmall(size) && !IsTooLarge(size);
    }
}
