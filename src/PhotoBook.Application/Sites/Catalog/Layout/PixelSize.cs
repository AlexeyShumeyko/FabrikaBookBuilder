namespace PhotoBook.Application.Sites.Catalog.Layout
{
    /// <summary>The size a file has to be, in pixels, at the resolution the site prints at.</summary>
    /// <remarks>
    /// Written as <c>4913×3495</c> when it reaches the customer: a size is compared, stored and
    /// shown in the same form, so there is one spelling of it rather than one per message.
    /// </remarks>
    public sealed record PixelSize(int Width, int Height)
    {
        public override string ToString() => $"{Width}×{Height}";
    }
}
