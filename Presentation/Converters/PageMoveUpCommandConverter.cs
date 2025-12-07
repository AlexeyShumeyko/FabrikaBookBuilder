using System;
using System.Globalization;
using System.Linq;
using System.Windows.Data;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using PhotoBookRenamer.Domain;

namespace PhotoBookRenamer.Presentation.Converters
{
    public class PageMoveUpCommandConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Length != 3) return null;
            
            if (values[0] is ICommand command && 
                values[1] is System.Collections.ObjectModel.ObservableCollection<Page> pages && 
                values[2] is int pageNumber)
            {
                var pagesWithoutCover = pages.Where(p => !p.IsCover).ToList();
                var index = pageNumber - 1;
                
                if (index >= 0 && index < pagesWithoutCover.Count)
                {
                    var page = pagesWithoutCover[index];
                    return new RelayCommand(() => command.Execute(page));
                }
            }
            
            return null;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}

