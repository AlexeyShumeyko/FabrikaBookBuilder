using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PhotoBook.Application;
using PhotoBook.Application.Sites;
using PhotoBook.Application.Sites.Catalog;

namespace PhotoBook.Presentation.ViewModels
{
    /// <summary>
    /// The uploader's documents for the formats an order chose, one per format, in order.
    /// </summary>
    /// <remarks>
    /// Both the price and the upload need these documents, and both need the same list: asking
    /// per row would multiply one request by the size of the order, and building the list twice
    /// would let a page price one set of formats and send another.
    /// </remarks>
    internal static class ChosenFormatDocuments
    {
        /// <summary>Reads every chosen format's document, or says which one could not be read.</summary>
        public static async Task<Result<IReadOnlyList<SiteProduct>>> ReadAsync(
            ISiteCatalogService catalog,
            SiteRegion region,
            IEnumerable<FormatChoice?> choices,
            CancellationToken cancellationToken = default)
        {
            if (catalog is null)
                throw new ArgumentNullException(nameof(catalog));
            if (region is null)
                throw new ArgumentNullException(nameof(region));

            var documents = new List<SiteProduct>();

            foreach (var choice in (choices ?? Enumerable.Empty<FormatChoice?>())
                         .Where(choice => choice is not null)
                         .Select(choice => choice!)
                         .DistinctBy(choice => (choice.Product.Id, choice.Format.Id)))
            {
                var document = await catalog
                    .GetFormatAsync(region, choice.Product.Id, choice.Format.Id, cancellationToken)
                    .ConfigureAwait(true);

                if (!document.IsSuccess || document.Value is null)
                    return Result<IReadOnlyList<SiteProduct>>.Fail(
                        document.Error ?? "Описание формата не открылось.");

                documents.Add(document.Value);
            }

            return Result<IReadOnlyList<SiteProduct>>.Ok(documents);
        }
    }
}
