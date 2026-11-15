using System;
using System.Collections.Generic;
using System.Linq;
using PhotoBook.Application.Sites.Catalog;

namespace PhotoBook.Presentation.ViewModels
{
    /// <summary>One parameter of a chosen format, with what the customer picked.</summary>
    public sealed class OrderParameterRow : ViewModelBase
    {
        private readonly Action _changed;

        private ParameterElement? _selected;

        public OrderParameterRow(
            ProductOption option,
            IReadOnlyList<ParameterElement> elements,
            ParameterElement? selected,
            Action changed)
        {
            Option = option;
            Elements = elements;
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
        public IReadOnlyList<ParameterElement> Elements { get; }

        /// <summary>Whether the site refuses to price the book without this answer.</summary>
        public bool IsRequired => Option.IsRequired;

        /// <summary>The choice, written into every book of this format.</summary>
        public ParameterElement? Selected
        {
            get => _selected;
            set
            {
                if (ReferenceEquals(_selected, value)) return;

                _selected = value;
                OnPropertyChanged(nameof(Selected));
                OnPropertyChanged(nameof(SelectedText));

                if (value is not null)
                    _changed();
            }
        }

        /// <summary>The choice as the row shows it, or a note that none is chosen.</summary>
        public string SelectedText => _selected?.Title ?? "не выбран";
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
        public OrderFormatParameters(FormatChoice choice, IReadOnlyList<OrderParameterRow> parameters)
        {
            Choice = choice;
            Parameters = parameters;
        }

        /// <summary>The format these parameters belong to.</summary>
        public FormatChoice Choice { get; }

        /// <summary>The format's name, as the section header shows it.</summary>
        public string Title => Choice.DisplayText;

        /// <summary>Every parameter the format offers right now.</summary>
        public IReadOnlyList<OrderParameterRow> Parameters { get; private set; }

        /// <summary>Whether every required parameter has an answer.</summary>
        public bool IsAnswered => Parameters.All(row => !row.IsRequired || row.Selected is not null);

        /// <summary>How many required parameters are still open, in words.</summary>
        public string StateText => IsAnswered
            ? "все параметры выбраны"
            : "не выбрано: " + Parameters.Count(row => row.IsRequired && row.Selected is null);

        /// <summary>Replaces the visible parameters after an answer changed what is available.</summary>
        public void Show(IReadOnlyList<OrderParameterRow> parameters)
        {
            Parameters = parameters;
            OnPropertyChanged(nameof(Parameters));
            OnPropertyChanged(nameof(IsAnswered));
            OnPropertyChanged(nameof(StateText));
        }

    }
}
