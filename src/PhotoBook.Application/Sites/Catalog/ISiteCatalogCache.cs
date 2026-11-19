using System;

namespace PhotoBook.Application.Sites.Catalog
{
    /// <summary>
    /// A catalog document as it was last stored, with the moment it was fetched.
    /// </summary>
    /// <param name="Content">The document exactly as the site sent it.</param>
    /// <param name="StoredAt">When the program fetched it.</param>
    /// <param name="IsStale">Whether it is older than the cache lifetime.</param>
    /// <remarks>
    /// A stale document is returned rather than withheld: the window has to open without a
    /// network, and the customer can decide whether a day-old option list is good enough while
    /// it says so.
    /// </remarks>
    public sealed record CachedSiteDocument(string Content, DateTimeOffset StoredAt, bool IsStale);

    /// <summary>
    /// Keeps the documents the catalog is read from, so opening the window does not depend on
    /// the network or spend a request on data that has not changed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The platform is shared by two sites and load on it is forbidden, so the catalog is read
    /// once and reused instead of being fetched each time a customer opens the upload window.
    /// </para>
    /// <para>
    /// The cache stores whole documents and knows nothing about products: parsing stays in one
    /// place, and a change in the parser does not leave the cache holding a half-understood
    /// shape - the schema version in the entry name moves instead.
    /// </para>
    /// </remarks>
    public interface ISiteCatalogCache
    {
        /// <summary>The stored document, or null when this document was never stored.</summary>
        CachedSiteDocument? Read(SiteRegion region, string document);

        /// <summary>Stores the document, replacing what was there.</summary>
        void Write(SiteRegion region, string document, string content);

        /// <summary>
        /// Forgets every stored document.
        /// </summary>
        /// <remarks>
        /// Called when the customer signs out: the catalog is public data, but the cache lives
        /// on the account's machine and keeping it after the account changed would let the next
        /// person read options the previous one had loaded. Safe to call when there is nothing
        /// stored.
        /// </remarks>
        void Clear();
    }
}
