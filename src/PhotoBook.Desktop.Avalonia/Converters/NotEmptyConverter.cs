using System;
using System.Collections;
using System.Globalization;
using Avalonia.Data.Converters;

namespace PhotoBook.Shell.Converters
{
    /// <summary>
    /// "Is there anything to show" - a string that is not empty, or a collection that is not
    /// empty.
    /// </summary>
    /// <remarks>
    /// Used for the error banner and for the preview strip. Both are "show me only if I have
    /// something", which in markup is a visibility question and in the view model is a
    /// question about data that does not exist. The alternative - a parallel
    /// <c>HasErrorMessage</c> on every view model - would put a property in the model for the
    /// convenience of a layout.
    /// </remarks>
    public class NotEmptyConverter : IValueConverter
    {
        public static readonly NotEmptyConverter Instance = new();

        public object? Convert(
            object? value,
            Type targetType,
            object? parameter,
            CultureInfo culture)
        {
            // "empty" asks the opposite question, and it has to be a parameter rather than a
            // second converter: "the empty state" and "has projects" are the same fact read
            // two ways, and two converters would be two places for it to be wrong.
            bool inverted = StatusToLabelConverter.Is(parameter, "empty");

            bool has = value switch
            {
                string text => !string.IsNullOrEmpty(text),
                ICollection collection => collection.Count > 0,
                int count => count > 0,
                bool flag => flag,
                _ => value is not null
            };

            return inverted ? !has : has;
        }

        public object? ConvertBack(
            object? value,
            Type targetType,
            object? parameter,
            CultureInfo culture)
            => throw new NotSupportedException();
    }
}