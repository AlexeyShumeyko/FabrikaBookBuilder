using System;
using Avalonia;
using Avalonia.Media.Imaging;

namespace PhotoBook.Shell.Converters;

/// <summary>
/// How many device pixels one layout unit is worth on the monitor the window is on.
/// </summary>
/// <remarks>
/// A converter that turns a path into an image has no visual to ask, and a photograph is
/// decoded in device pixels while a cell is measured in layout units. So the screen writes
/// the number here when it attaches and when the window moves to a monitor with a different
/// scaling, and the converter multiplies by it. Static on purpose: there is one window, and
/// the alternative - threading a visual through every binding - is a conversion that needs
/// the screen to exist before it can be written down.
/// </remarks>
public static class BitmapScale
{
    /// <summary>Pixels per layout unit. Never zero: a window detached is 1, not nothing.</summary>
    public static double Current { get; set; } = 1;

    /// <summary>The width to decode at for a cell drawn this many layout units wide.</summary>
    public static int Pixels(int layoutWidth)
        => layoutWidth <= 0 ? 0 : Math.Max(1, (int)Math.Round(layoutWidth * Clamp(Current)));

    private static double Clamp(double scaling)
        => scaling < 1 ? 1 : scaling > 3 ? 3 : scaling;
}
