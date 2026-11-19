using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using PhotoBook.Application.Sites.Catalog;

namespace PhotoBook.Presentation.ViewModels
{
    /// <summary>One parameter of a chosen format, with what the customer picked.</summary>
    /// <remarks>
    /// The row is updated in place rather than replaced. The window lays panels out while it
    /// measures them, and swapping the whole list under a panel mid-measure is what made the
    /// page crash when an answer changed which questions the site offers.
    /// </remarks>
    public sealed class OrderParameterRow : ViewModelBase
    {
        private readonly Action _changed;

        private IReadOnlyList<ParameterElement> _elements;
        private ParameterElement? _selected;

        public OrderParameterRow(
            ProductOption option,
            IReadOnlyList<ParameterElement> elements,
            ParameterElement? selected,
            Action changed)
        {
            Option = option;
            _elements = elements;
            _selected = selected;
            _changed = changed;
        }

        /// <summary>The site's parameter.</summary>
        public ProductOption Option { get; }

        /// <summary>Its id, which is what the price request carries.</summary>
        public long OptionId => Option.Id;

        /// <summary>The site's name for the parameter.</summary>
        public string Title => Option.Title;

        /// <summary>What the customer may pick.</summary>
        public IReadOnlyList<ParameterElement> Elements => _elements;

        /// <summary>Whether the site refuses to price the book without this answer.</summary>
        public bool IsRequired => Option.IsRequired;

        /// <summary>Whether this answer is still missing, which the panel marks in red.</summary>
        public bool IsMissing => IsRequired && _selected is null;

        /// <summary>The choice, written into every book of this format.</summary>
        public ParameterElement? Selected
        {
            get => _selected;
            set
            {
                // Compared by value, not by reference: the same choice arrives as a fresh
                // instance after the block is rebuilt, and treating it as a new answer made the
                // binding write it back, which rebuilt the block again.
                if (Equals(_selected, value)) return;

                _selected = value;
                OnPropertyChanged(nameof(Selected));
                OnPropertyChanged(nameof(SelectedText));
                OnPropertyChanged(nameof(IsMissing));

                // Taking an answer back is a change like any other: the book loses the choice
                // and the price the page holds was asked for another combination.
                _changed();
            }
        }

        /// <summary>The choice as the row shows it, or a note that none is chosen.</summary>
        public string SelectedText => _selected?.Title ?? "не выбран";

        /// <summary>Shows what the site offers now, keeping the row itself in place.</summary>
        public void Show(IReadOnlyList<ParameterElement> elements, ParameterElement? selected)
        {
            var sameChoice = Equals(_selected, selected);

            // The same choices arrive as fresh instances after a rebuild. Keeping the instance
            // the window already shows keeps its list untouched: a replaced list makes the
            // window drop its own selection and write "no answer" back, which reads as a new
            // answer and rebuilds the block again.
            if (!SameElements(_elements, elements))
            {
                _elements = elements;
                OnPropertyChanged(nameof(Elements));
            }

            // Announcing an unchanged answer would send the window's own write-back through the
            // setter and rebuild this very block; only a real change is announced.
            if (sameChoice)
                return;

            _selected = selected;
            OnPropertyChanged(nameof(Selected));
            OnPropertyChanged(nameof(SelectedText));
            OnPropertyChanged(nameof(IsMissing));
        }

        private static bool SameElements(
            IReadOnlyList<ParameterElement> current,
            IReadOnlyList<ParameterElement> next)
        {
            if (current.Count != next.Count)
                return false;

            for (var index = 0; index < current.Count; index++)
            {
                if (!Equals(current[index], next[index]))
                    return false;
            }

            return true;
        }
    }

    /// <summary>
    /// The parameters of one format, shared by every book ordered in it.
    /// </summary>
    /// <remarks>
    /// Parameters belong to the product and format, not to a copy: ten books of one format are
    /// ten identical orders, and asking the same questions ten times would invite ten different
    /// answers the site would then have to price separately.
    /// </remarks>
    public sealed class OrderFormatParameters : ViewModelBase
    {
        public OrderFormatParameters(FormatChoice choice, IEnumerable<OrderParameterRow> parameters)
        {
            Choice = choice;
            Parameters = new ObservableCollection<OrderParameterRow>(parameters);
        }

        /// <summary>The format these parameters belong to.</summary>
        public FormatChoice Choice { get; }

        /// <summary>The format's name, as the section header shows it.</summary>
        public string Title => Choice.DisplayText;

        /// <summary>Every parameter the format offers right now.</summary>
        public ObservableCollection<OrderParameterRow> Parameters { get; }

        /// <summary>Whether every required parameter has an answer.</summary>
        public bool IsAnswered => Parameters.All(row => !row.IsRequired || row.Selected is not null);

        /// <summary>Whether at least one required parameter is still unanswered.</summary>
        public bool HasMissing => !IsAnswered;

        /// <summary>The unanswered parameters by the site's own names, quoted.</summary>
        public string MissingText => string.Join(", ", Parameters
            .Where(row => row.IsMissing)
            .Select(row => "«" + row.Title + "»"));

        /// <summary>How many required parameters are still open, in words.</summary>
        public string StateText => IsAnswered
            ? "Все параметры выбраны"
            : "Не выбраны: " + MissingText + ".";

        /// <summary>
        /// Brings the block up to date with what the site offers, changing the rows that are
        /// still asked and adding the ones an answer has just unlocked.
        /// </summary>
        public void Sync(IReadOnlyList<OrderParameterRow> offered)
        {
            for (var index = Parameters.Count - 1; index >= 0; index--)
            {
                if (!offered.Any(row => row.OptionId == Parameters[index].OptionId))
                    Parameters.RemoveAt(index);
            }

            foreach (var row in offered)
            {
                var existing = Parameters.FirstOrDefault(item => item.OptionId == row.OptionId);

                if (existing is null)
                    Parameters.Add(row);
                else
                    existing.Show(row.Elements, row.Selected);
            }

            OnPropertyChanged(nameof(IsAnswered));
            OnPropertyChanged(nameof(HasMissing));
            OnPropertyChanged(nameof(MissingText));
            OnPropertyChanged(nameof(StateText));
        }

        /// <summary>Re-reads the answers, which change the section's own line.</summary>
        public void Refresh()
        {
            OnPropertyChanged(nameof(IsAnswered));
            OnPropertyChanged(nameof(HasMissing));
            OnPropertyChanged(nameof(MissingText));
            OnPropertyChanged(nameof(StateText));
        }
    }
}
