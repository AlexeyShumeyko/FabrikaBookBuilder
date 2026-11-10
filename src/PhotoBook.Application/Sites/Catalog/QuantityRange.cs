namespace PhotoBook.Application.Sites.Catalog
{
    /// <summary>
    /// The quantities the site accepts, and the step it steps by.
    /// </summary>
    public sealed record QuantityRange(int From, int To, int Step)
    {
        /// <summary>Whether this quantity is one the site will price rather than refuse.</summary>
        public bool Allows(int quantity) =>
            Step > 0 && quantity >= From && quantity <= To && (quantity - From) % Step == 0;
    }
}
