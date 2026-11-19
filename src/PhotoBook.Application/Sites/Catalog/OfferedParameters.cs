using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace PhotoBook.Application.Sites.Catalog
{
    /// <summary>
    /// The parameters of one format as the site offers them under the answers already given.
    /// </summary>
    /// <remarks>
    /// The dependency rule is the site's, so it lives in the application layer and is applied
    /// once; this type only says which parameters that leaves. Nothing here decides what an
    /// option means - the titles and ids are the site's own.
    /// </remarks>
    public static class OfferedParameters
    {
        /// <summary>The parts of a format that ask something of the customer.</summary>
        public static IReadOnlyList<ProductOption> Of(ProductVariant variant) =>
            variant.Parts
                .SelectMany(part => part.Options)
                .ToList();

        /// <summary>
        /// The parameters the site would accept now, in the order the format lists them.
        /// </summary>
        /// <remarks>
        /// A parameter whose dependency is unanswered, or whose exclusion has become true, is
        /// not offered: asking it would let the customer assemble a combination the site prices
        /// at zero. Dependencies arrive as ids written the way the site writes them.
        /// </remarks>
        public static IReadOnlyList<ProductOption> Available(
            IReadOnlyList<ProductOption> options,
            IReadOnlyDictionary<long, long> chosen)
        {
            if (options is null)
                throw new ArgumentNullException(nameof(options));
            if (chosen is null)
                throw new ArgumentNullException(nameof(chosen));

            return options.Where(option => IsOffered(option, chosen)).ToList();
        }

        private static bool IsOffered(ProductOption option, IReadOnlyDictionary<long, long> chosen)
        {
            if (!string.IsNullOrWhiteSpace(option.DependsOnOptionId) &&
                long.TryParse(option.DependsOnOptionId, out var dependsOn))
            {
                if (!chosen.TryGetValue(dependsOn, out var answer) ||
                    !option.DependsOnElementIds.Contains(
                        answer.ToString(System.Globalization.CultureInfo.InvariantCulture)))
                {
                    return false;
                }
            }

            if (!string.IsNullOrWhiteSpace(option.ExcludedOptionId) &&
                long.TryParse(option.ExcludedOptionId, out var excluded) &&
                chosen.TryGetValue(excluded, out var excludedAnswer) &&
                option.ExcludedElementIds.Contains(
                    excludedAnswer.ToString(System.Globalization.CultureInfo.InvariantCulture)))
            {
                return false;
            }

            return true;
        }
    }
}
