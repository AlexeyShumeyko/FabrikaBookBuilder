using System.Globalization;

namespace PhotoBook.Presentation.ViewModels
{
    /// <summary>Counts that read as Russian sentences: «1 книга», «2 книги», «5 книг».</summary>
    /// <remarks>
    /// Three forms, chosen by the last two digits the way the language chooses them: eleven to
    /// fourteen take the third form whatever their last digit says. Written once here because a
    /// page with groups, books, spreads and files would otherwise write the same five branches
    /// four times and get one of them wrong.
    /// </remarks>
    internal static class RussianPlural
    {
        /// <summary>The form of the word for this number, without the number itself.</summary>
        public static string Of(int count, string one, string few, string many)
        {
            var lastTwo = count % 100;
            if (lastTwo is >= 11 and <= 14)
                return many;

            return (count % 10) switch
            {
                1 => one,
                >= 2 and <= 4 => few,
                _ => many
            };
        }

        /// <summary>The number and its word: «2 книги».</summary>
        public static string With(int count, string one, string few, string many) =>
            count.ToString(CultureInfo.CurrentCulture) + " " + Of(count, one, few, many);
    }
}
