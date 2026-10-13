using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace PhotoBook.Shell.Converters
{
    /// <summary>
    /// "Изменён: 24 мая, 14:20" - relative for today/yesterday, absolute otherwise.
    /// </summary>
    /// <remarks>
    /// Transcribed from the design spec and the WPF converter, including the fixed cultures:
    /// the time and the day are formatted invariantly and Russianly on purpose, so the text
    /// does not change with whatever culture the machine happens to be set to.
    /// </remarks>
    public class LastModifiedTextConverter : IValueConverter
    {
        public static readonly LastModifiedTextConverter Instance = new();

        private static readonly CultureInfo Russian = new("ru-RU");

        public object? Convert(
            object? value,
            Type targetType,
            object? parameter,
            CultureInfo culture)
        {
            if (value is not DateTime dt) return string.Empty;

            var now = DateTime.Now;
            string body;

            if (dt.Date == now.Date)
                body = dt.ToString("HH:mm", CultureInfo.InvariantCulture);
            else if (dt.Date == now.Date.AddDays(-1))
                body = "вчера, " + dt.ToString("HH:mm", CultureInfo.InvariantCulture);
            else if (dt.Year == now.Year)
                body = dt.ToString("d MMM, HH:mm", Russian);
            else
                body = dt.ToString("d MMM yyyy", Russian);

            return StatusToLabelConverter.Is(parameter, "prefix") ? "Изменён: " + body : body;
        }

        public object? ConvertBack(
            object? value,
            Type targetType,
            object? parameter,
            CultureInfo culture)
            => throw new NotSupportedException();
    }
}