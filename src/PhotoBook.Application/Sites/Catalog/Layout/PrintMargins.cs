namespace PhotoBook.Application.Sites.Catalog.Layout
{
    /// <summary>
    /// How far the printable area extends past the page on each side, in millimetres.
    /// </summary>
    /// <remarks>
    /// The site calls these bleeds for a cover and cutting lines for an inner page; they are
    /// the same thing to a file that has to cover the page plus the trim, and keeping one type
    /// is what lets the size calculation be written once.
    /// </remarks>
    public sealed record PrintMargins(double Left, double Top, double Right, double Bottom)
    {
        public static PrintMargins None { get; } = new(0, 0, 0, 0);
    }
}
