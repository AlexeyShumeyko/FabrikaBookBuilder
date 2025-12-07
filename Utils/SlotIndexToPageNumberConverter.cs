using System;
using System.Globalization;
using System.Windows.Data;

namespace PhotoBookRenamer.Utils
{
    public class SlotIndexToPageNumberConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            // КРИТИЧЕСКИ ВАЖНО: Теперь получаем Page напрямую, а не int
            if (value is PhotoBookRenamer.Models.Page page)
            {
                // Обложка без номера
                if (page.IsCover)
                {
                    return string.Empty;
                }
                return page.Index.ToString();
            }
            
            // Fallback для старого формата (int)
            if (value is int slotIndex)
            {
                if (slotIndex == 0)
                {
                    return string.Empty;
                }
                return slotIndex.ToString();
            }
            
            return value?.ToString() ?? string.Empty;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}

