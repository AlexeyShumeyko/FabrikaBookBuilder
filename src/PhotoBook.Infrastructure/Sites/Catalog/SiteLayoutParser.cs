using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using PhotoBook.Application;
using PhotoBook.Application.Sites.Catalog;
using PhotoBook.Application.Sites.Catalog.Layout;

namespace PhotoBook.Infrastructure.Sites.Catalog
{
    /// <summary>
    /// Reads the geometry document the site's uploader runs on.
    /// </summary>
    /// <remarks>
    /// The document describes each part twice: the cover by a table of page sizes keyed by an
    /// index, the inner pages by one size for the whole run. It also says how wide the bleed
    /// is under two different names - <c>bleeds</c> on the cover, <c>cuttingLine*</c> on the
    /// pages - and the program keeps the site's shape instead of inventing one, so a mismatch
    /// with the site can still be traced back to the field it came from.
    /// </remarks>
    internal sealed class SiteLayoutParser : ISiteLayoutParser
    {
        public Result<SiteLayout> Parse(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return Result<SiteLayout>.Fail("Сайт вернул пустую геометрию макета.");

            try
            {
                using var document = JsonDocument.Parse(json);
                var root = document.RootElement;

                if (!root.TryGetProperty("integration", out var integration) ||
                    integration.ValueKind != JsonValueKind.Object)
                {
                    return Result<SiteLayout>.Fail("В ответе сайта нет описания загрузчика.");
                }

                var settings = integration.TryGetProperty("settings", out var integrationSettings) &&
                               integrationSettings.ValueKind == JsonValueKind.Object
                    ? integrationSettings
                    : default;

                var resolution = SiteJson.Integer(settings, "outputResolution");
                if (resolution is not { } dpi || dpi <= 0)
                    return Result<SiteLayout>.Fail("Сайт не назвал разрешение печати.");

                var titles = ReadTitles(root);
                var indexes = ReadSizeIndexes(root);
                var parts = ReadParts(integration, titles, indexes);

                if (parts.Count == 0)
                    return Result<SiteLayout>.Fail("Сайт не описал ни одной части макета.");

                return Result<SiteLayout>.Ok(new SiteLayout(
                    SiteJson.Number(root, "productId"),
                    SiteJson.Number(root, "productComponentId"),
                    dpi,
                    parts,
                    SiteJson.Number(integration, "id")));
            }
            catch (JsonException)
            {
                return Result<SiteLayout>.Fail("Геометрия макета пришла в неизвестном формате.");
            }
        }

        private static IReadOnlyList<PartLayout> ReadParts(
            JsonElement integration,
            IReadOnlyDictionary<long, string> titles,
            IReadOnlyDictionary<int, IReadOnlyList<SizeIndexCondition>> indexes)
        {
            var parts = new List<PartLayout>();

            if (!integration.TryGetProperty("elements", out var elements) ||
                elements.ValueKind != JsonValueKind.Array)
            {
                return parts;
            }

            foreach (var element in elements.EnumerateArray())
            {
                if (!element.TryGetProperty("settings", out var settings) ||
                    settings.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                if (SiteJson.Integer(settings, "productPartId") is not { } partId)
                    continue;

                var sizes = ReadSizes(settings);
                if (sizes.Count > 0)
                {
                    var first = sizes[0];

                    parts.Add(new PartLayout(
                        partId,
                        Title(titles, element, partId),
                        sizes.Select(entry => entry.Page).ToArray(),
                        null,
                        first.Page.Bleeds,
                        IndexesFor(settings, indexes),
                        first.Safety,
                        first.ErrorMm));

                    continue;
                }

                if (!string.Equals(SiteJson.Text(settings, "imageSizeWay"), "manually", StringComparison.Ordinal) ||
                    SiteJson.Millimetres(settings, "imageSizeWidth") is not { } width ||
                    SiteJson.Millimetres(settings, "imageSizeHeight") is not { } height)
                {
                    continue;
                }

                parts.Add(new PartLayout(
                    partId,
                    Title(titles, element, partId),
                    Array.Empty<PageSizeRange>(),
                    new PageSize(width, height),
                    new PrintMargins(
                        SiteJson.Millimetres(settings, "cuttingLineLeft") ?? 0,
                        SiteJson.Millimetres(settings, "cuttingLineTop") ?? 0,
                        SiteJson.Millimetres(settings, "cuttingLineRight") ?? 0,
                        SiteJson.Millimetres(settings, "cuttingLineBottom") ?? 0),
                    IndexesFor(settings, indexes),
                    new PrintMargins(
                        SiteJson.Millimetres(settings, "safetyZoneLeft") ?? 0,
                        SiteJson.Millimetres(settings, "safetyZoneTop") ?? 0,
                        SiteJson.Millimetres(settings, "safetyZoneRight") ?? 0,
                        SiteJson.Millimetres(settings, "safetyZoneBottom") ?? 0),
                    SiteJson.Millimetres(settings, "imageSizeError") ?? 0));
            }

            return parts;
        }

        /// <summary>
        /// The cover's table: each row is an index, a page size and the margins around it.
        /// </summary>
        private static IReadOnlyList<SizeEntry> ReadSizes(JsonElement settings)
        {
            var sizes = new List<SizeEntry>();

            if (!SiteJson.Flag(settings, "pageSizeRangesFlag") ||
                !settings.TryGetProperty("pageSizeRanges", out var ranges) ||
                ranges.ValueKind != JsonValueKind.Array)
            {
                return Array.Empty<SizeEntry>();
            }

            foreach (var range in ranges.EnumerateArray())
            {
                if (SiteJson.Integer(range, "rangeMin") is not { } index ||
                    SiteJson.Millimetres(range, "pageSizeWidth") is not { } width ||
                    SiteJson.Millimetres(range, "pageSizeHeight") is not { } height)
                {
                    continue;
                }

                sizes.Add(new SizeEntry(
                    new PageSizeRange(index, new PageSize(width, height), Margins(range, "bleeds")),
                    Margins(range, "safety"),
                    SiteJson.Millimetres(range, "pageSizeError") ?? 0));
            }

            return sizes;
        }

        private static PrintMargins Margins(JsonElement parent, string name) =>
            parent.TryGetProperty(name, out var margins) && margins.ValueKind == JsonValueKind.Object
                ? new PrintMargins(
                    SiteJson.Millimetres(margins, "left") ?? 0,
                    SiteJson.Millimetres(margins, "top") ?? 0,
                    SiteJson.Millimetres(margins, "right") ?? 0,
                    SiteJson.Millimetres(margins, "bottom") ?? 0)
                : new PrintMargins(0, 0, 0, 0);

        /// <summary>
        /// The index tables by the attribute id, with one entry per option element.
        /// </summary>
        /// <remarks>
        /// The same attribute appears several times - once per choice that changes the spine,
        /// so that no interlayer, 1 mm cardboard and 2 mm cardboard each get their own pace.
        /// Keeping them under one id and matching on the option is what keeps them apart.
        /// </remarks>
        private static IReadOnlyDictionary<int, IReadOnlyList<SizeIndexCondition>> ReadSizeIndexes(
            JsonElement root)
        {
            var indexes = new Dictionary<int, List<SizeIndexCondition>>();

            if (!root.TryGetProperty("productAttributes", out var attributes) ||
                attributes.ValueKind != JsonValueKind.Array)
            {
                return new Dictionary<int, IReadOnlyList<SizeIndexCondition>>();
            }

            foreach (var attribute in attributes.EnumerateArray())
            {
                if (SiteJson.Integer(attribute, "id") is not { } id ||
                    !attribute.TryGetProperty("productSettings", out var settings) ||
                    settings.ValueKind != JsonValueKind.Object ||
                    !settings.TryGetProperty("condition", out var condition) ||
                    condition.ValueKind != JsonValueKind.Object ||
                    !SiteJson.Flag(condition, "enabled"))
                {
                    continue;
                }

                var optionId = SiteJson.Text(condition, "optionId");
                var elements = SiteJson.Texts(condition, "optionElementsIds");
                if (string.IsNullOrWhiteSpace(optionId) || elements.Count == 0)
                    continue;

                var rules = ReadRules(settings);
                if (rules.Count == 0)
                    continue;

                if (!indexes.TryGetValue(id, out var list))
                {
                    list = new List<SizeIndexCondition>();
                    indexes[id] = list;
                }

                list.Add(new SizeIndexCondition(optionId, elements[0], rules));
            }

            var result = new Dictionary<int, IReadOnlyList<SizeIndexCondition>>(indexes.Count);
            foreach (var pair in indexes)
                result[pair.Key] = pair.Value;

            return result;
        }

        private static IReadOnlyList<SizeIndexRule> ReadRules(JsonElement settings)
        {
            var rules = new List<SizeIndexRule>();

            if (!settings.TryGetProperty("dependencies", out var dependencies) ||
                dependencies.ValueKind != JsonValueKind.Array)
            {
                return rules;
            }

            foreach (var dependency in dependencies.EnumerateArray())
            {
                if (!string.Equals(SiteJson.Text(dependency, "way"), "part_quantity", StringComparison.Ordinal) ||
                    !string.Equals(SiteJson.Text(dependency, "numberValueMode"), "from-ranges", StringComparison.Ordinal) ||
                    !dependency.TryGetProperty("numberValueRanges", out var ranges) ||
                    ranges.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                foreach (var range in ranges.EnumerateArray())
                {
                    if (SiteJson.Integer(range, "from") is not { } from ||
                        SiteJson.Integer(range, "to") is not { } to ||
                        SiteJson.Integer(range, "value") is not { } value)
                    {
                        continue;
                    }

                    rules.Add(new SizeIndexRule(from, to, value));
                }
            }

            return rules;
        }

        private static IReadOnlyList<SizeIndexCondition> IndexesFor(
            JsonElement settings,
            IReadOnlyDictionary<int, IReadOnlyList<SizeIndexCondition>> indexes)
        {
            return SiteJson.Integer(settings, "pageSizeProductAttributeId") is { } id &&
                   indexes.TryGetValue(id, out var found)
                ? found
                : Array.Empty<SizeIndexCondition>();
        }

        private static string Title(
            IReadOnlyDictionary<long, string> titles,
            JsonElement element,
            long partId) =>
            titles.TryGetValue(partId, out var fromParts)
                ? fromParts
                : SiteJson.Text(element, "title") ?? partId.ToString(CultureInfo.InvariantCulture);

        private static IReadOnlyDictionary<long, string> ReadTitles(JsonElement root)
        {
            var titles = new Dictionary<long, string>();

            if (!root.TryGetProperty("productPartsTitles", out var map) ||
                map.ValueKind != JsonValueKind.Object)
            {
                return titles;
            }

            foreach (var title in map.EnumerateObject())
            {
                if (long.TryParse(title.Name, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) &&
                    title.Value.ValueKind == JsonValueKind.String &&
                    title.Value.GetString() is { Length: > 0 } text)
                {
                    titles[id] = text;
                }
            }

            return titles;
        }

        /// <summary>A table row: the size the site asks for and what the program needs from it.</summary>
        private sealed record SizeEntry(PageSizeRange Page, PrintMargins Safety, double ErrorMm);
    }
}
