using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows.Data;
using PhotoBookRenamer.Models;

namespace PhotoBookRenamer.Utils
{
    public class PageSlotConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is System.Collections.ObjectModel.ObservableCollection<Page> pages)
            {
                var pagesWithoutCover = pages.Where(p => !p.IsCover).ToList();
                var count = pagesWithoutCover.Count;
                
                // Создаем список индексов от 1 до count
                return Enumerable.Range(1, count).ToList();
            }
            
            return new List<int>();
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}












