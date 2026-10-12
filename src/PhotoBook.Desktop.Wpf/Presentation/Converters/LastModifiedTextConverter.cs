using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using PhotoBook.Core;
using PhotoBookRenamer.Presentation.ViewModels;

namespace PhotoBookRenamer.Presentation.Converters
{
    /// <summary>
    /// "Изменён: 24 мая, 14:20" - relative for today/yesterday, absolute otherwise.
    /// </summary>
    public class LastModifiedTextConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is not DateTime dt) return string.Empty;

            var now = DateTime.Now;
            string body;
            if (dt.Date == now.Date)
                body = dt.ToString("HH:mm", CultureInfo.InvariantCulture);
            else if (dt.Date == now.Date.AddDays(-1))
                body = "вчера, " + dt.ToString("HH:mm", CultureInfo.InvariantCulture);
            else if (dt.Year == now.Year)
                body = dt.ToString("d MMM, HH:mm", new CultureInfo("ru-RU"));
            else
                body = dt.ToString("d MMM yyyy", new CultureInfo("ru-RU"));

            return string.Equals(parameter as string, "prefix", StringComparison.OrdinalIgnoreCase)
                ? "Изменён: " + body
                : body;
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => Binding.DoNothing;
    }
}
