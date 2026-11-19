using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;

namespace PhotoBook.Infrastructure.Sites.Catalog
{
    /// <summary>
    /// Reads the site's JSON without trusting its types.
    /// </summary>
    /// <remarks>
    /// The platform sends the same value as a number on one endpoint and as a string on
    /// another: a flag is <c>true</c> here and <c>"1"</c> there, a size is <c>428</c> and
    /// <c>"428"</c>. A reader that assumes one shape works until the next product arrives, so
    /// every value goes through this place, which accepts both and treats anything else as
    /// absent rather than as zero.
    /// </remarks>
    internal static class SiteJson
    {
        /// <summary>The string value, or null when the field is missing or of another type.</summary>
        public static string? Text(JsonElement parent, string name) =>
            parent.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;

        /// <summary>A whole number that may arrive as a number or as text; 0 when it does not.</summary>
        public static long Number(JsonElement parent, string name) =>
            Integer(parent, name) ?? 0;

        /// <summary>A whole number that may arrive as a number or as text; null when it does not.</summary>
        public static int? Integer(JsonElement parent, string name)
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
        /// A measurement in millimetres. The site writes decimals with a dot and, in a few
        /// places, with a comma, so both are read.
        /// </summary>
        public static double? Millimetres(JsonElement parent, string name)
        {
            if (!parent.TryGetProperty(name, out var value))
                return null;

            if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number))
                return number;

            if (value.ValueKind != JsonValueKind.String || value.GetString() is not { } text)
                return null;

            var normalized = text.Replace(',', '.').Trim();
            return double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : null;
        }

        /// <summary>
        /// A flag written as a boolean on one page and as "1"/"0" on another, so both are read
        /// rather than one of them silently meaning false.
        /// </summary>
        public static bool Flag(JsonElement parent, string name)
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

        /// <summary>The non-empty strings of an array, in the order sent.</summary>
        public static IReadOnlyList<string> Texts(JsonElement parent, string name)
        {
            if (!parent.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Array)
                return Array.Empty<string>();

            var items = new List<string>();
            foreach (var item in value.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String && item.GetString() is { Length: > 0 } text)
                    items.Add(text);
            }

            return items;
        }
    }
}
