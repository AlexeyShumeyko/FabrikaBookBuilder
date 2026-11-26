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

        /// <summary>The group whose card is the chosen one on the page.</summary>
        /// <remarks>
        /// One choice for the whole page rather than a flag per card: the chosen group is what
        /// the page reads from, and two cards both drawn as chosen would say it has two.
        /// </remarks>
        public SiteOrderGroup? SelectedGroup
        {
            get => _selectedGroup;
            private set
            {
                if (ReferenceEquals(_selectedGroup, value))
                    return;

                if (_selectedGroup is not null)
                    _selectedGroup.IsSelected = false;

                _selectedGroup = value;

                if (value is not null)
                    value.IsSelected = true;

                OnPropertyChanged(nameof(SelectedGroup));
            }
        }

        private SiteOrderGroup? _selectedGroup;

        /// <summary>Chooses a group's card, opening it if it was folded away.</summary>
        /// <remarks>
        /// The prototype opens the card it chooses: a chosen group whose books are hidden would
        /// leave the page with nothing to read. The switch and the chevron never come here - the
        /// view leaves them out of the head's own press.
        /// </remarks>
        public void SelectGroup(SiteOrderGroup group)
        {
            if (group is null || !Groups.Contains(group))
                return;

            SelectedGroup = group;

            if (!group.IsExpanded)
                group.ToggleCollapse();
        }

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
                // The row is put in at construction: adding it later would start a parameter
                // reading of its own just before this method starts the one it means to keep.
                target = new SiteOrderGroup(
                    0,
                    new[] { row },
                    _catalog,
                    _sessions,
                    _prices,
                    _fileCheck,
                    OnGroupChanged,
                    OnChoiceChanged,
                    _uploaded);

                groups.Add(target);
            }

            source.Remove(row);

            if (!created)
                target!.Add(row);

            if (source.Books.Count == 0)
                groups.Remove(source);

            for (var index = 0; index < groups.Count; index++)
                groups[index].Renumber(index + 1);

            Groups = groups.ToList();
            OnPropertyChanged(nameof(HasGroups));

            // The chosen card is a card that still exists: if the move took the chosen group
            // away, the first one left becomes the page's choice.
            var chosen = SelectedGroup;
            if (chosen is null || !Groups.Contains(chosen))
                SelectedGroup = Groups.FirstOrDefault();

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
