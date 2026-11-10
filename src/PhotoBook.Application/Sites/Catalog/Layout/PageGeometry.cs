using System;

namespace PhotoBook.Application.Sites.Catalog.Layout
{
    /// <summary>
    /// The size a file must be, in pixels, for a page of a given size and its margins.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The site's uploader converts each measurement on its own and then adds the pieces:
    /// <c>Math.round(mm * dpi / 25.4)</c> for the page, and the same for each margin side. It
    /// does not convert the sum, and the difference is measurable: a 428 mm cover with 18 mm
    /// bleeds is 5055 + 213 + 213 = 5481 pixels, while converting 464 mm in one step gives
    /// 5480. A file that is a few pixels off is refused by the site, so the pieces are added
    /// the way the site adds them.
    /// </para>
    /// <para>
    /// Rounded half away from zero, because that is what JavaScript's <c>Math.round</c> does
    /// for positive numbers and what the site's numbers were produced with.
    /// </para>
    /// </remarks>
    public static class PageGeometry
    {
        /// <summary>Millimetres in an inch; the site switches to 1 only for its imperial mode.</summary>
        public const double MillimetresPerInch = 25.4;

        public static PixelSize Required(PageSize page, PrintMargins bleeds, int resolutionDpi)
        {
            if (page is null)
                throw new ArgumentNullException(nameof(page));
            if (bleeds is null)
                throw new ArgumentNullException(nameof(bleeds));
            if (resolutionDpi <= 0)
                throw new ArgumentOutOfRangeException(nameof(resolutionDpi), "Разрешение должно быть положительным.");

            var width = ToPixels(page.WidthMm, resolutionDpi) +
                        ToPixels(bleeds.Left, resolutionDpi) +
                        ToPixels(bleeds.Right, resolutionDpi);

            var height = ToPixels(page.HeightMm, resolutionDpi) +
                         ToPixels(bleeds.Top, resolutionDpi) +
                         ToPixels(bleeds.Bottom, resolutionDpi);

            return new PixelSize(width, height);
        }

        public static int ToPixels(double millimetres, int resolutionDpi) =>
            (int)Math.Round(millimetres * resolutionDpi / MillimetresPerInch, MidpointRounding.AwayFromZero);
    }
}
