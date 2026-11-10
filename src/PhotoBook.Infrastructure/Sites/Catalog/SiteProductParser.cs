using System;
using System.Collections.Generic;
using System.Globalization;
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
                    ReadLong(product, "id"),
                    slug,
                    ReadString(product, "name") ?? slug,
                    ReadString(product, "kind") ?? string.Empty,
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
                    ReadLong(variant, "id"),
                    ReadString(variant, "name") ?? string.Empty,
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
                    ReadLong(part, "id"),
                    ReadString(part, "name") ?? string.Empty,
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
                            ReadLong(element, "id"),
                            ReadString(element, "name") ?? string.Empty));
                    }
                }

                parsed.Add(new ProductOption(
                    ReadLong(option, "id"),
                    ReadString(option, "name") ?? string.Empty,
                    ReadFlag(option, "required"),
                    None(ReadString(option, "dependOptionId")),
                    ReadStringList(option, "dependElementsIds"),
                    None(ReadString(option, "excludedOptionId")),
                    ReadStringList(option, "excludedElementsIds"),
                    elements));
            }

            return parsed;
        }

        private static ProductQuantitySettings ReadQuantity(JsonElement settings)
        {
            var displayName = ReadString(settings, "displayName") ?? string.Empty;
            var rules = new List<QuantityRule>();

            if (settings.TryGetProperty("quantityByOptionsSettings", out var byOption) &&
                byOption.ValueKind == JsonValueKind.Object &&
                byOption.TryGetProperty("rules", out var list) &&
                list.ValueKind == JsonValueKind.Array)
            {
                foreach (var rule in list.EnumerateArray())
                {
                    var optionId = ReadString(rule, "option_id");
                    if (string.IsNullOrWhiteSpace(optionId))
                        continue;

                    var range = ReadRange(rule, "range_from", "range_to", "range_step");
                    if (range is null)
                        continue;

                    rules.Add(new QuantityRule(
                        optionId,
                        ReadStringList(rule, "option_elements_ids"),
                        range));
                }
            }

            return new ProductQuantitySettings(
                displayName,
                UnitOf(displayName),
                ReadRange(settings, "from", "to", "step"),
                ReadFlag(settings, "allowManualInput"),
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
            var start = ReadInt(parent, from);
            var end = ReadInt(parent, to);
            var increment = ReadInt(parent, step);

            return start is { } s && end is { } e && increment is { } i
                ? new QuantityRange(s, e, i)
                : null;
        }

        private static string? ReadString(JsonElement parent, string name) =>
            parent.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;

        private static long ReadLong(JsonElement parent, string name)
        {
            if (!parent.TryGetProperty(name, out var value))
                return 0;

            return value.ValueKind switch
            {
                JsonValueKind.Number when value.TryGetInt64(out var number) => number,
                JsonValueKind.String when long.TryParse(value.GetString(), NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out var parsed) => parsed,
                _ => 0
            };
        }

        private static int? ReadInt(JsonElement parent, string name)
        {
            if (!parent.TryGetProperty(name, out var value))
                return null;

            return value.ValueKind switch
            {
                JsonValueKind.Number when value.TryGetInt32(out var number) => number,
                JsonValueKind.String when int.TryParse(value.GetString(), NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out var parsed) => parsed,
                _ => null
            };
        }

        /// <summary>
        /// The site writes flags as booleans on one page and as "1"/"0" on another, so both are
        /// read rather than one of them silently meaning false.
        /// </summary>
        private static bool ReadFlag(JsonElement parent, string name)
        {
            if (!parent.TryGetProperty(name, out var value))
                return false;

            return value.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.Number when value.TryGetInt32(out var number) => number != 0,
                JsonValueKind.String => string.Equals(value.GetString(), "1", StringComparison.Ordinal) ||
                                        string.Equals(value.GetString(), "true", StringComparison.OrdinalIgnoreCase),
                _ => false
            };
        }

        private static IReadOnlyList<string> ReadStringList(JsonElement parent, string name)
        {
            if (!parent.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Array)
                return Array.Empty<string>();

            var items = new List<string>();
            foreach (var item in value.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String && item.GetString() is { } text && text.Length > 0)
                    items.Add(text);
            }

            return items;
        }

        private static string? None(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
    }
}
