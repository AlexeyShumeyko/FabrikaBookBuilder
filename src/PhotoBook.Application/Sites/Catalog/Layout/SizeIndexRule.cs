namespace PhotoBook.Application.Sites.Catalog.Layout
{
    /// <summary>
    /// The page size index the site uses for a range of quantities.
    /// </summary>
    /// <remarks>
    /// <para>
    /// On a layflat two to seven spreads share one cover size, eight to ten the next, and so
    /// on; the last range is inclusive, so the check is written as a range rather than a table
    /// of single values.
    /// </para>
    /// <para>
    /// The quantity is not always the quantity of the part being sized: the site keys the
    /// cover's row by the spread count, because a thicker book needs a wider cover, and says so
    /// with the <c>partId</c> of its dependency. That id is kept here, so a reader looks the
    /// quantity up in the part the site names instead of assuming it is the part's own.
    /// </para>
    /// </remarks>
    public sealed record SizeIndexRule(int FromQuantity, int ToQuantity, int Index, long PartId = 0)
    {
        public bool Covers(int quantity) => quantity >= FromQuantity && quantity <= ToQuantity;
    }
}
