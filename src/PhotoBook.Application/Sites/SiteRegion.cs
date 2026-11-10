using System.Collections.Generic;

namespace PhotoBook.Application.Sites
{
    /// <summary>
    /// One installation of the platform: the site a customer places orders on.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The two sites share the API host and differ by the site prefix only, so the region is
    /// not "which domain we called" - it is the prefix the call goes to. That is why the region
    /// travels with the session and why the API client is built around a single region instead
    /// of switching between them.
    /// </para>
    /// <para>
    /// An order on the wrong site is a real-world mistake, not a cosmetic one, so nothing in
    /// the program is allowed to derive a region by guessing.
    /// </para>
    /// </remarks>
    public sealed record SiteRegion(
        string Code,
        string DisplayName,
        string SiteUrl,
        string ApiOrigin,
        string ShowcaseSlug)
    {
        /// <summary>The prefix every API call of this region starts with.</summary>
        public string ApiRoot => $"{ApiOrigin}/site/{ShowcaseSlug}";

        public static SiteRegion Com { get; } = new(
            "com",
            "fabrika-fotoknigi.com",
            "https://fabrika-fotoknigi.com",
            "https://platform-api.fabrika-fotoknigi.com",
            "p4m3j");

        public static SiteRegion By { get; } = new(
            "by",
            "fabrika-fotoknigi.by",
            "https://fabrika-fotoknigi.by",
            "https://platform-api.fabrika-fotoknigi.com",
            "lzopu");

        /// <summary>Every known site, in the order sign-in tries them.</summary>
        public static IReadOnlyList<SiteRegion> All { get; } = new[] { Com, By };
    }
}
