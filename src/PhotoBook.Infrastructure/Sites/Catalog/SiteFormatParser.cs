using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using PhotoBook.Application;
using PhotoBook.Application.Sites.Catalog;

namespace PhotoBook.Infrastructure.Sites.Catalog
{
    /// <summary>
    /// Reads the document the site's uploader loads for one product and one format.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the document that decides where files go: an integration element names the part
    /// its <c>pagesCountPartId</c> points at, so the part holding pages is known by id rather
    /// than by the site's wording. The wording is read too, but only as a label.
    /// </para>
    /// <para>
    /// Options arrive with the part they belong to and with the choices the site offers; a part
    /// whose quantity is sent as a fixed value is kept as a one-off range so the rest of the
    /// program sees one shape.
    /// </para>
    /// </remarks>
    internal sealed class SiteFormatParser : ISiteFormatParser
    {
        public Result<SiteProduct> Parse(string json, long productId, long componentId)
        {
            if (string.IsNullOrWhiteSpace(json))
                return Result<SiteProduct>.Fail("Сайт вернул пустое описание формата.");

            try
            {
                using var document = JsonDocument.Parse(json);
                var root = document.RootElement;

                if (root.ValueKind != JsonValueKind.Object)
                    return Result<SiteProduct>.Fail("Описание формата пришло в неизвестном виде.");

                var titles = ReadTitles(root, "productPartsTitles");
                var pagesParts = ReadPagesPartIds(root);
                var parts = ReadParts(root, titles, pagesParts);
                var variantTitle = ReadTitles(root, "productComponentsTitles")
                    .TryGetValue(componentId, out var name) ? name : string.Empty;

                return Result<SiteProduct>.Ok(new SiteProduct(
                    productId,
                    string.Empty,
                    (SiteJson.Text(root, "productTitle") ?? string.Empty).Trim(),
                    SiteJson.Text(root, "productKind") ?? string.Empty,
                    new[]
                    {
                        new ProductVariant(
                            componentId,
                            variantTitle,
                            Array.Empty<ProductOption>(),
                            parts),
                    },
                    ReadIntegrationId(root)));
            }
            catch (JsonException)
            {
                return Result<SiteProduct>.Fail("Описание формата пришло в неизвестном виде.");
            }
        }

        /// <summary>The uploader's own id for this product, which every new project names.</summary>
        private static long ReadIntegrationId(JsonElement root) =>
            root.TryGetProperty("integration", out var integration) &&
            integration.ValueKind == JsonValueKind.Object
                ? SiteJson.Number(integration, "id")
                : 0;

        /// <summary>The part ids the integration sends pages into, whoever names them.</summary>
        private static HashSet<long> ReadPagesPartIds(JsonElement root)
        {
            var ids = new HashSet<long>();

            if (root.TryGetProperty("integration", out var integration) &&
                integration.ValueKind == JsonValueKind.Object &&
                integration.TryGetProperty("elements", out var elements) &&
                elements.ValueKind == JsonValueKind.Array)
            {
                foreach (var element in elements.EnumerateArray())
                {
                    if (!element.TryGetProperty("settings", out var settings) ||
                        settings.ValueKind != JsonValueKind.Object)
                    {
                        continue;
                    }

                    if (SiteJson.Integer(settings, "pagesCountPartId") is { } id and > 0)
                        ids.Add(id);
                }
            }

            return ids;
        }

        private static List<ProductPart> ReadParts(
            JsonElement root,
            IReadOnlyDictionary<long, string> titles,
            HashSet<long> pagesParts)
        {
            var quantities = new Dictionary<long, ProductQuantitySettings>();
            var options = new Dictionary<long, List<ProductOption>>();
            var order = new List<long>();

            if (root.TryGetProperty("calculatorParts", out var list) && list.ValueKind == JsonValueKind.Array)
            {
                foreach (var entry in list.EnumerateArray())
                {
                    var partId = SiteJson.Integer(entry, "productPartId");
                    if (partId is null || partId <= 0)
                        continue;

                    var id = partId.Value;

                    if (!order.Contains(id))
                        order.Add(id);

                    if (SiteJson.Integer(entry, "optionId") is { } optionId)
                    {
                        if (!options.TryGetValue(id, out var forPart))
                            options[id] = forPart = new List<ProductOption>();

                        forPart.Add(ReadOption(entry, optionId));
                        continue;
                    }

                    quantities[id] = ReadQuantity(entry);
                }
            }

            foreach (var id in titles.Keys.Where(id => !order.Contains(id)))
                order.Add(id);

            return order
                .Select(id => new ProductPart(
                    id,
                    titles.TryGetValue(id, out var title) ? title : string.Empty,
                    options.TryGetValue(id, out var forPart) ? forPart : Array.Empty<ProductOption>(),
                    quantities.TryGetValue(id, out var quantity) ? quantity : Fixed(),
                    pagesParts.Contains(id)))
                .ToList();
        }

        private static ProductOption ReadOption(JsonElement entry, long optionId)
        {
            var elements = new List<OptionElement>();

            if (entry.TryGetProperty("elements", out var list) && list.ValueKind == JsonValueKind.Array)
            {
                foreach (var element in list.EnumerateArray())
                {
                    elements.Add(new OptionElement(
                        SiteJson.Number(element, "value"),
                        (SiteJson.Text(element, "title") ?? string.Empty).Trim()));
                }
            }

            var dependsOn = (string?)null;
            var excluded = (string?)null;
            var dependsOnElements = (IReadOnlyList<string>)Array.Empty<string>();
            var excludedElements = (IReadOnlyList<string>)Array.Empty<string>();

            if (entry.TryGetProperty("dependencies", out var dependencies) &&
                dependencies.ValueKind == JsonValueKind.Object)
            {
                dependsOn = Fan(dependencies, "depend");
                dependsOnElements = SiteJson.Texts(dependencies, "dependElements");
                excluded = Fan(dependencies, "excluded");
                excludedElements = SiteJson.Texts(dependencies, "excludedElements");
            }

            return new ProductOption(
                optionId,
                (SiteJson.Text(entry, "title") ?? string.Empty).Trim(),
                SiteJson.Flag(entry, "required"),
                dependsOn,
                dependsOnElements,
                excluded,
                excludedElements,
                elements);
        }

        /// <summary>
        /// The part's quantity: a typed range when the site offers one, a fixed one otherwise.
        /// </summary>
        private static ProductQuantitySettings ReadQuantity(JsonElement entry)
        {
            var title = (SiteJson.Text(entry, "title") ?? string.Empty).Trim();
            var rules = new List<QuantityRule>();

            if (entry.TryGetProperty("quantityByOptionsSettings", out var byOption) &&
                byOption.ValueKind == JsonValueKind.Object &&
                byOption.TryGetProperty("rules", out var ruleList) &&
                ruleList.ValueKind == JsonValueKind.Array)
            {
                foreach (var rule in ruleList.EnumerateArray())
                {
                    var optionId = SiteJson.Text(rule, "option_id");
                    var ruleRange = ReadRange(rule, "range_from", "range_to", "range_step");

                    if (!string.IsNullOrWhiteSpace(optionId) && ruleRange is not null)
                    {
                        rules.Add(new QuantityRule(
                            optionId,
                            SiteJson.Texts(rule, "option_elements_ids"),
                            ruleRange));
                    }
                }
            }

            var range = entry.TryGetProperty("options", out var options) &&
                        options.ValueKind == JsonValueKind.Object
                ? ReadRange(options, "min", "max", "step")
                : null;

            if (range is null && SiteJson.Integer(entry, "value") is { } value and > 0)
                range = new QuantityRange(value, value, 1);

            return new ProductQuantitySettings(
                title,
                QuantityUnit.Unknown,
                range ?? Fixed().Default,
                false,
                rules);
        }

        private static ProductQuantitySettings Fixed() =>
            new(
                string.Empty,
                QuantityUnit.Copies,
                new QuantityRange(1, 1, 1),
                false,
                Array.Empty<QuantityRule>());

        private static QuantityRange? ReadRange(JsonElement parent, string from, string to, string step)
        {
            var start = SiteJson.Integer(parent, from);
            var end = SiteJson.Integer(parent, to);
            var increment = SiteJson.Integer(parent, step);

            if (start is not { } s || end is not { } e)
                return null;

            return new QuantityRange(s, e, increment is > 0 ? increment.Value : 1);
        }

        private static string? Fan(JsonElement parent, string name)
        {
            var text = SiteJson.Text(parent, name);

            return string.IsNullOrWhiteSpace(text) ? null : text;
        }

        private static IReadOnlyDictionary<long, string> ReadTitles(JsonElement root, string name)
        {
            var titles = new Dictionary<long, string>();

            if (root.TryGetProperty(name, out var map) && map.ValueKind == JsonValueKind.Object)
            {
                foreach (var pair in map.EnumerateObject())
                {
                    if (long.TryParse(pair.Name, out var id) && pair.Value.ValueKind == JsonValueKind.String)
                        titles[id] = (pair.Value.GetString() ?? string.Empty).Trim();
                }
            }

            return titles;
        }
    }
}
