using System;
using Avalonia;
using Avalonia.Controls;

namespace PhotoBook.Shell.Views;

/// <summary>
/// Lays children out in three columns, every card in a row as tall as the tallest card in
/// that row.
/// </summary>
/// <remarks>
/// <para>
/// It exists because the two obvious answers are both wrong here. The platform's uniform grid
/// forces every cell in the whole list to one height, so a card whose text runs long is
/// clipped and the rest of the row is padded with nothing. A wrapping panel sizes each card
/// to its own content, so a row reads as a ragged edge and an empty project comes out visibly
/// shorter than its neighbours - which is exactly how it looked.
/// </para>
/// <para>
/// The other shell gets this from a uniform grid with three columns, inside a scroller that
/// offers infinite height: the grid then makes each row as tall as that row's tallest card
/// and no taller. That is the behaviour reproduced here, for the reason that the engine's
/// own uniform grid cannot be given an infinite height to be tall in.
/// </para>
/// </remarks>
public sealed class ThreeColumnPanel : Panel
{
    private const int Columns = 3;

    /// <summary>
    /// The space between two cards, across and down.
    /// </summary>
    /// <remarks>
    /// This number used to live in two places at once - here and in the margin on the card
    /// itself - and the gap between rows came out 48 pixels where the released program
    /// has 25. It also meant the last column stopped short of the right-hand edge by
    /// exactly one margin, while the control panel above the list ran to it, so the two
    /// blocks did not line up.
    ///
    /// One place for it, and the card carries no margin of its own.
    /// </remarks>
    private const double Gutter = 24;

    protected override Size MeasureOverride(Size availableSize)
    {
        if (Children.Count == 0) return default;

        // An infinite width means the caller is measuring without a bound - inside a
        // scroller's measure pass, before the viewport is known. Laying out at zero is
        // useless but not wrong, and the arrange pass below gets a real width and does the
        // real work.
        double width = double.IsInfinity(availableSize.Width) ? 0 : availableSize.Width;
        double columnWidth = Math.Max(0, width / Columns);

        double rowHeight = 0;
        double totalHeight = 0;

        for (int i = 0; i < Children.Count; i++)
        {
            Children[i].Measure(new Size(columnWidth, double.PositiveInfinity));

            if (i % Columns == 0) rowHeight = 0;
            rowHeight = Math.Max(rowHeight, Children[i].DesiredSize.Height);

            bool rowEnds = i % Columns == Columns - 1 || i == Children.Count - 1;
            if (rowEnds)
            {
                totalHeight += rowHeight + Gutter;
                rowHeight = 0;
            }
        }

        return new Size(width, Math.Max(0, totalHeight - Gutter));
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (Children.Count == 0) return finalSize;

        // The same arithmetic as the measure pass, or the cards are laid out into a
        // width that was never asked for.
        double columnWidth = Math.Max(0, (finalSize.Width - Gutter * (Columns - 1)) / Columns);
        double y = 0;
        double rowHeight = 0;

        for (int i = 0; i < Children.Count; i++)
        {
            if (i % Columns == 0) rowHeight = 0;
            rowHeight = Math.Max(rowHeight, Children[i].DesiredSize.Height);

            double x = (i % Columns) * (columnWidth + Gutter);
            Children[i].Arrange(new Rect(x, y, columnWidth, rowHeight));

            bool rowEnds = i % Columns == Columns - 1 || i == Children.Count - 1;
            if (rowEnds)
            {
                y += rowHeight + Gutter;
                rowHeight = 0;
            }
        }

        return finalSize;
    }
}
