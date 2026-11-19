using System.Collections.Generic;

namespace PhotoBook.Application.Sites.Catalog
{
    /// <summary>
    /// An option of a product, with the choices it offers and what it depends on.
    /// </summary>
    /// <remarks>
    /// The dependency fields are kept exactly as the site sends them, including the empty
    /// strings it uses for "none" and the nulls it uses for "no elements". The platform states
    /// the same thing in two ways - "this option is available only when 65075 is 417891" and
    /// "it is excluded when 65075 is 417892" - and both are needed: an interlayer exists for
    /// paper and for cardboard with different ranges, and neither is valid without a choice of
    /// density.
    /// </remarks>
    public sealed record ProductOption(
        long Id,
        string Title,
        bool IsRequired,
        string? DependsOnOptionId,
        IReadOnlyList<string> DependsOnElementIds,
        string? ExcludedOptionId,
        IReadOnlyList<string> ExcludedElementIds,
        IReadOnlyList<OptionElement> Elements);
}
