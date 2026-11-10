namespace PhotoBook.Application.Sites.Catalog.Layout
{
    /// <summary>
    /// The page size index the site uses for a range of quantities.
    /// </summary>
    /// <remarks>
    /// On a layflat two to seven spreads share one cover size, eight to ten the next, and so
    /// on; the last range is inclusive, so the check is written as a range rather than a table
    /// of single values.
    /// </remarks>
    public sealed record SizeIndexRule(int FromSpreads, int ToSpreads, int Index)
    {
        public bool Covers(int spreads) => spreads >= FromSpreads && spreads <= ToSpreads;
    }
}
