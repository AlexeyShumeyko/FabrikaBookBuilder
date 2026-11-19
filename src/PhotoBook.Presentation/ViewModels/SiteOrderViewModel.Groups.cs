using System;
using System.Linq;

namespace PhotoBook.Presentation.ViewModels
{
    /// <summary>How the order page lays its books out into groups, and how they change.</summary>
    /// <remarks>
    /// The page's first split follows the exported page size, which is right for two folders but
    /// wrong for books of different products exported at the same size: those share a group but
    /// not their parameters, and the site prices one set of parameters per project. The customer
    /// therefore decides where a book goes, and this half is the only place that moves it.
    /// </remarks>
    public sealed partial class SiteOrderViewModel
    {
        /// <summary>Writes a line about the format list itself on every group's format card.</summary>
        private void SetFormatNote(string text)
        {
            foreach (var group in Groups)
                group.SetFormatNote(text);
        }

        /// <summary>The group a book's row stands in, or null when it stands in none.</summary>
        public SiteOrderGroup? GroupOf(OrderBookRow row) =>
            Groups.FirstOrDefault(group => group.Books.Contains(row));

        /// <summary>
        /// Moves one book into another group, or into a group of its own.
        /// </summary>
        /// <remarks>
        /// The project's numbering of books does not change, so a moved book keeps the name it
        /// had. A group left without books is removed rather than shown empty, and the remaining
        /// groups are renumbered so the cards read as one order.
        /// </remarks>
        public void MoveBook(OrderBookRow row, SiteOrderGroup? target)
        {
            var groups = Groups.ToList();
            var source = groups.FirstOrDefault(group => group.Books.Contains(row));

            if (source is null || (target is not null && ReferenceEquals(source, target)))
                return;

            var created = target is null;
            if (created)
            {
                target = new SiteOrderGroup(
                    0,
                    Array.Empty<OrderBookRow>(),
                    _catalog,
                    _sessions,
                    _prices,
                    _fileCheck,
                    OnGroupChanged,
                    OnChoiceChanged);

                groups.Add(target);
            }

            source.Remove(row);
            target!.Add(row);

            if (source.Books.Count == 0)
                groups.Remove(source);

            for (var index = 0; index < groups.Count; index++)
                groups[index].Renumber(index + 1);

            Groups = groups.ToList();
            OnPropertyChanged(nameof(HasGroups));

            // A group made by the move still has to read its questions; an existing group
            // already holds them, and a new book takes the answers it already has.
            if (created)
            {
                target.ApplyFormats(_formats);
                _ = target.LoadParametersAsync();
            }

            RefreshReadiness();
        }
    }
}
