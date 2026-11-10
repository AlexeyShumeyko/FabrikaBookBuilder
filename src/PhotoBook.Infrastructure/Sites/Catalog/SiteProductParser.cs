using System;
using System.Collections.Generic;
using System.Text.Json;
using PhotoBook.Application;
using PhotoBook.Application.Sites.Catalog;

namespace PhotoBook.Infrastructure.Sites.Catalog
{
    /// <summary>
    /// Reads the product page the site's own calculator reads.
    /// </summary>
    /// <remarks>
    /// The same product is described in more than one document, arranged differently in each:
    /// the catalog list keeps the cover and the spreads as parts, the product page keeps the
    /// cover options at the variant and the spreads under parts. This parser follows the
    /// product page, because it is the document that carries the quantity settings and the
    /// site's own words for what a quantity counts.
    /// </remarks>
    internal sealed class SiteProductParser : ISiteProductParser
    {
        private static readonly ProductQuantitySettings NoQuantity = new(
            string.Empty,
            QuantityUnit.Unknown,
            null,
            false,
            Array.Empty<QuantityRule>());

        public Result<SiteProduct> Parse(string json, string slug)
        {
            if (string.IsNullOrWhiteSpace(json))
                return Result<SiteProduct>.Fail("Сайт вернул пустой каталог.");

            if (string.IsNullOrWhiteSpace(slug))
                throw new ArgumentException("Нужен слаг продукта: по нему продукт и опознаётся.", nameof(slug));

            try
            {
                using var document = JsonDocument.Parse(json);

                if (!document.RootElement.TryGetProperty("product", out var product) ||
                    product.ValueKind != JsonValueKind.Object)
                {
                    return Result<SiteProduct>.Fail("В ответе сайта нет описания продукта.");
                }

                return Result<SiteProduct>.Ok(new SiteProduct(
                    SiteJson.Number(product, "id"),
                    slug,
                    SiteJson.Text(product, "name") ?? slug,
                    SiteJson.Text(product, "kind") ?? string.Empty,
                    ReadVariants(product)));
            }
            catch (JsonException)
            {
                return Result<SiteProduct>.Fail("Каталог пришёл в неизвестном формате.");
            }
        }

        private static IReadOnlyList<ProductVariant> ReadVariants(JsonElement product)
        {
            var variants = new List<ProductVariant>();

            if (!product.TryGetProperty("calculator", out var calculator) ||
                calculator.ValueKind != JsonValueKind.Object ||
                !calculator.TryGetProperty("variants", out var list) ||
                list.ValueKind != JsonValueKind.Array)
            {
                return variants;
            }

            foreach (var variant in list.EnumerateArray())
            {
                var cover = new List<ProductOption>();
                var parts = new List<ProductPart>();

                if (variant.TryGetProperty("calculator", out var inner) &&
                    inner.ValueKind == JsonValueKind.Object)
                {
                    if (inner.TryGetProperty("options", out var coverOptions))
                        cover.AddRange(ReadOptions(coverOptions));

                    parts.AddRange(ReadParts(inner));
                }

                variants.Add(new ProductVariant(
                    SiteJson.Number(variant, "id"),
                    SiteJson.Text(variant, "name") ?? string.Empty,
                    cover,
                    parts));
            }

            return variants;
        }

        private static IReadOnlyList<ProductPart> ReadParts(JsonElement calculator)
        {
            var parts = new List<ProductPart>();

            if (!calculator.TryGetProperty("parts", out var list) || list.ValueKind != JsonValueKind.Array)
                return parts;

            foreach (var part in list.EnumerateArray())
            {
                var options = new List<ProductOption>();
                var quantity = NoQuantity;

                if (part.TryGetProperty("calculator", out var inner) &&
                    inner.ValueKind == JsonValueKind.Object)
                {
                    if (inner.TryGetProperty("options", out var partOptions))
                        options.AddRange(ReadOptions(partOptions));

                    if (inner.TryGetProperty("quantitySettings", out var settings) &&
                        settings.ValueKind == JsonValueKind.Object)
                    {
                        quantity = ReadQuantity(settings);
                    }
                }

                parts.Add(new ProductPart(
                    SiteJson.Number(part, "id"),
                    SiteJson.Text(part, "name") ?? string.Empty,
                    options,
                    quantity));
            }

            return parts;
        }

        private static IReadOnlyList<ProductOption> ReadOptions(JsonElement options)
        {
            var parsed = new List<ProductOption>();

            if (options.ValueKind != JsonValueKind.Array)
                return parsed;

            foreach (var option in options.EnumerateArray())
            {
                var elements = new List<OptionElement>();

                if (option.TryGetProperty("elements", out var list) && list.ValueKind == JsonValueKind.Array)
                {
                    foreach (var element in list.EnumerateArray())
                    {
                        elements.Add(new OptionElement(
                            SiteJson.Number(element, "id"),
                            SiteJson.Text(element, "name") ?? string.Empty));
                    }
                }

                parsed.Add(new ProductOption(
                    SiteJson.Number(option, "id"),
                    SiteJson.Text(option, "name") ?? string.Empty,
                    SiteJson.Flag(option, "required"),
                    None(SiteJson.Text(option, "dependOptionId")),
                    SiteJson.Texts(option, "dependElementsIds"),
                    None(SiteJson.Text(option, "excludedOptionId")),
                    SiteJson.Texts(option, "excludedElementsIds"),
                    elements));
            }

            return parsed;
        }

        private static ProductQuantitySettings ReadQuantity(JsonElement settings)
        {
            var displayName = SiteJson.Text(settings, "displayName") ?? string.Empty;
            var rules = new List<QuantityRule>();

            if (settings.TryGetProperty("quantityByOptionsSettings", out var byOption) &&
                byOption.ValueKind == JsonValueKind.Object &&
                byOption.TryGetProperty("rules", out var list) &&
                list.ValueKind == JsonValueKind.Array)
            {
                foreach (var rule in list.EnumerateArray())
                {
                    var optionId = SiteJson.Text(rule, "option_id");
                    if (string.IsNullOrWhiteSpace(optionId))
                        continue;

                    var range = ReadRange(rule, "range_from", "range_to", "range_step");
                    if (range is null)
                        continue;

                    rules.Add(new QuantityRule(
                        optionId,
                        SiteJson.Texts(rule, "option_elements_ids"),
                        range));
                }
            }

            return new ProductQuantitySettings(
                displayName,
                UnitOf(displayName),
                ReadRange(settings, "from", "to", "step"),
                SiteJson.Flag(settings, "allowManualInput"),
                rules);
        }

        /// <summary>
        /// What the site's label for the quantity means.
        /// </summary>
        /// <remarks>
        /// Anything not recognised stays unknown on purpose. Guessing would turn 24 pages into
        /// 24 spreads, and the mistake would surface as a rejected project rather than as a
        /// wrong number on screen.
        /// </remarks>
        private static QuantityUnit UnitOf(string displayName)
        {
            var text = displayName.ToLowerInvariant();

            if (text.Contains("разворот", StringComparison.Ordinal))
                return QuantityUnit.Spreads;
            if (text.Contains("страниц", StringComparison.Ordinal))
                return QuantityUnit.Pages;
            if (text.Contains("экземпляр", StringComparison.Ordinal) ||
                text.Contains("копи", StringComparison.Ordinal))
            {
                return QuantityUnit.Copies;
            }

            return QuantityUnit.Unknown;
        }

        private static QuantityRange? ReadRange(JsonElement parent, string from, string to, string step)
        {
            var start = SiteJson.Integer(parent, from);
            var end = SiteJson.Integer(parent, to);
            var increment = SiteJson.Integer(parent, step);

            return start is { } s && end is { } e && increment is { } i
                ? new QuantityRange(s, e, i)
                : null;
        }

        private static string? None(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
    }
}
