using System;
using System.Collections.Generic;

namespace PhotoBook.Application.Sites.Catalog
{
    /// <summary>What is wrong with a selection, in a form the caller can name.</summary>
    public enum PartProblemKind
    {
        /// <summary>A required option has no choice.</summary>
        MissingRequired,

        /// <summary>The chosen element is not one the option offers.</summary>
        UnknownElement,

        /// <summary>The choice is not available with the rest of the selection.</summary>
        Unavailable,

        /// <summary>The quantity is outside what the site prices for this combination.</summary>
        QuantityNotAllowed
    }

    /// <summary>One reason a selection cannot be sent to the site.</summary>
    public sealed record PartProblem(PartProblemKind Kind, string Message);

    /// <summary>
    /// The verdict on a selection: every problem found, not only the first.
    /// </summary>
    /// <remarks>
    /// All of them, because a person fixing a form wants to see everything at once; reporting
    /// the first one and then the next after each attempt turns a single correction into three.
    /// </remarks>
    public sealed record PartCheck(bool IsValid, IReadOnlyList<PartProblem> Problems)
    {
        public static PartCheck Ok() => new(true, Array.Empty<PartProblem>());

        public static PartCheck Invalid(IReadOnlyList<PartProblem> problems) => new(false, problems);
    }
}
