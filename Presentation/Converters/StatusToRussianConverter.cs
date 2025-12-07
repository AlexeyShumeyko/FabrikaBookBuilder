using System;
using System.Globalization;
using System.Windows.Data;
using PhotoBookRenamer.Domain;

namespace PhotoBookRenamer.Presentation.Converters
{
    public class StatusToRussianConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is ProjectStatus status)
            {
                return status switch
                {
                    ProjectStatus.NotFilled => "Не заполнен",
                    ProjectStatus.Ready => "Подготовлен",
                    ProjectStatus.SuccessfullyCompleted => "Завершён",
                    _ => value.ToString() ?? string.Empty
                };
            }
            return value?.ToString() ?? string.Empty;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}








