using System;
using System.Globalization;
using System.Linq;
using System.Windows.Data;
using PhotoBookRenamer.Models;

namespace PhotoBookRenamer.Utils
{
    public class IsSlotFilledConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values == null || values.Length != 3)
                return false;
            
            if (values[0] is Page cover && values[1] is System.Collections.ObjectModel.ObservableCollection<Page> pages && values[2] is int index)
            {
                // Если индекс 0 - проверяем обложку
                if (index == 0)
                {
                    return !string.IsNullOrEmpty(cover.SourcePath);
                }
                else
                {
                    // Иначе проверяем страницу по индексу
                    // КРИТИЧЕСКИ ВАЖНО: Ищем страницу по Index, а не по позиции в списке!
                    // index - это slotIndex (1, 2, 3, 4...), который соответствует Page.Index
                    var page = pages.FirstOrDefault(p => !p.IsCover && p.Index == index);
                    
                    if (page != null)
                    {
                        return !string.IsNullOrEmpty(page.SourcePath);
                    }
                }
            }
            
            return false;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}



