using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using PhotoBook.Application;
using PhotoBook.Application.Sites.Catalog;

namespace PhotoBook.Infrastructure.Sites.Catalog
{
    /// <summary>
    /// Reads the list the site's own uploader offers: products in display order, each with the
    /// formats it is sold in.
    /// </summary>
    /// <remarks>
    /// The site calls a format a component. Nothing is renamed here - the id is kept as the
    /// format id because that is the value the order has to send - and the order the site sends
    /// is kept rather than sorted by name, so the picker shows what the site shows.
    /// </remarks>
    internal sealed class SiteProductsParser : ISiteProductsParser
    {
        public Result<IReadOnlyList<CatalogProduct>> Parse(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return Result<IReadOnlyList<CatalogProduct>>.Fail("Сайт прислал пустой список продуктов.");

            try
            {
                using var document = JsonDocument.Parse(json);

                if (document.RootElement.ValueKind != JsonValueKind.Array)
                    return Result<IReadOnlyList<CatalogProduct>>.Fail("Список продуктов пришёл в неизвестном формате.");

                var products = new List<CatalogProduct>();

                foreach (var element in document.RootElement.EnumerateArray())
                {
                    var formats = new List<CatalogFormat>();

                    if (element.TryGetProperty("components", out var components) &&
                        components.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var component in components.EnumerateArray())
                        {
                            formats.Add(new CatalogFormat(
                                SiteJson.Number(component, "id"),
                                SiteJson.Text(component, "name") ?? string.Empty));
                        }
                    }

                    products.Add(new CatalogProduct(
                        SiteJson.Number(element, "id"),
                        SiteJson.Text(element, "name")?.Trim() ?? string.Empty,
                        SiteJson.Text(element, "kind") ?? string.Empty,
                        (int)SiteJson.Number(element, "order"),
                        formats));
                }

                return Result<IReadOnlyList<CatalogProduct>>.Ok(
                    products.OrderBy(product => product.Order).ToList());
            }
            catch (JsonException)
            {
                return Result<IReadOnlyList<CatalogProduct>>.Fail("Список продуктов пришёл в неизвестном формате.");
            }
        }
    }
}
