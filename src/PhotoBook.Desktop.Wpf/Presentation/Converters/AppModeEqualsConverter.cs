using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using PhotoBook.Core;
using PhotoBookRenamer.Presentation.ViewModels;

namespace PhotoBookRenamer.Presentation.Converters
{
    /// Two-way AppMode &lt;-&gt; bool, so the header tabs can be RadioButtons bound straight
    /// to <c>MainViewModel.CurrentMode</c>.
    ///
    /// ConvertBack deliberately ignores <c>false</c>: when the mode changes, every other tab
    /// flips to unchecked, and returning a mode for that would immediately undo the change.
    /// </summary>
    public class AppModeEqualsConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is not AppMode current) return false;
            if (parameter is not string name) return false;
            return Enum.TryParse(name, ignoreCase: true, out AppMode target) && current == target;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is not bool isChecked || !isChecked) return Binding.DoNothing;
            if (parameter is not string name) return Binding.DoNothing;
            return Enum.TryParse(name, ignoreCase: true, out AppMode target) ? target : (object?)Binding.DoNothing;
        }
    }
}
