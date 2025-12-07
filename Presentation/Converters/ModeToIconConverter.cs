using System;
using System.Globalization;
using System.Windows.Data;
using PhotoBookRenamer.Domain;

namespace PhotoBookRenamer.Presentation.Converters
{
    public class ModeToIconConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is AppMode mode)
            {
                return mode switch
                {
                    AppMode.UniqueFolders => "📁",
                    AppMode.Combined => "🎨",
                    _ => "📄"
                };
            }
            return "📄";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}









