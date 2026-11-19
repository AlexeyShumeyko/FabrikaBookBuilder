namespace PhotoBook.Application.Sites.Catalog
{
    /// <summary>
    /// What the quantity of a book is counted in.
    /// </summary>
    /// <remarks>
    /// The platform does not send a unit: it names what it counts in the calculator's own
    /// words, "Количество разворотов" for a layflat and "Количество страниц" for a page book,
    /// and leaves the reading to the client. The unit is therefore derived from that text, and
    /// when the words are not ones the program knows it stays <see cref="Unknown"/>: a page
    /// count read as a spread count would halve the files a book expects without any error
    /// anywhere.
    /// </remarks>
    public enum QuantityUnit
    {
        Unknown = 0,
        Spreads,
        Pages,
        Copies
    }
}
