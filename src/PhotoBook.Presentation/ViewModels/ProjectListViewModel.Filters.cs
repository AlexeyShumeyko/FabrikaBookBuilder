using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PhotoBook.Core;

namespace PhotoBook.Presentation.ViewModels
{
    /// <summary>
    /// Finding one project out of several.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Four controls, and the argument for each of them is the same: somebody with twenty
    /// projects cannot find the one they want. A search by name solves "where is it"; a filter
    /// by mode or status solves "show me only the ones I was working on"; a sort solves
    /// "which of these did I touch last".
    /// </para>
    /// <para>
    /// Nothing here filters the collection that was loaded. <see cref="Projects"/> stays what
    /// is on disk, so clearing a filter brings everything back without touching the disk
    /// again - and so the count shown beside the filters is always the true total, not the
    /// number that survived the last change.
    /// </para>
    /// <para>
    /// The rebuild is a walk over the list, not a re-read: a few hundred cards is nothing,
    /// and re-reading would mean the delay the owner is complaining about arriving again
    /// every time a letter is typed.
    /// </para>
    /// </remarks>
    public partial class ProjectListViewModel
    {
        /// <summary>Which way to narrow the list.</summary>
        public enum ModeChoice
        {
            All,
            UniqueFolders,
            Combined
        }

        /// <summary>Which state of a run to narrow to.</summary>
        public enum StatusChoice
        {
            All,
            Draft,
            Ready,
            Completed
        }

        /// <summary>Which order to put them in.</summary>
        public enum SortChoice
        {
            ByChanged,
            ByName
        }

        /// <summary>
        /// One option as the person reads it, paired with the value it stands for.
        /// </summary>
        /// <remarks>
        /// Paired rather than using the enum's own name because "NotFilled" is what the code
        /// calls a run with an empty slot and "Черновик" is what a photographer calls it. The
        /// name in the code is not an argument for putting it on the screen.
        /// </remarks>
        public sealed record Choice<T>(T Value, string Label) where T : struct, Enum;

        private string _searchText = string.Empty;
        private ModeChoice _modeChoice = ModeChoice.All;
        private StatusChoice _statusChoice = StatusChoice.All;
        private SortChoice _sortChoice = SortChoice.ByChanged;

        /// <summary>The cards actually shown. Rebuilt, not queried.</summary>
        /// <remarks>
        /// A view filter over a collection would avoid the copy, but this list also has to be
        /// sorted, and a sort cannot be expressed as a filter. One collection that holds
        /// exactly what is on screen is easier to reason about than a filtered view over a
        /// second sorted one.
        /// </remarks>
        public ObservableCollection<ProjectInfo> FilteredProjects { get; } = new();

        /// <summary>What the controls offer, in the order they are read.</summary>
        public IReadOnlyList<Choice<ModeChoice>> ModeChoices { get; } = new[]
        {
            new Choice<ModeChoice>(ModeChoice.All, "Все режимы"),
            new Choice<ModeChoice>(ModeChoice.UniqueFolders, "Уникальные папки"),
            new Choice<ModeChoice>(ModeChoice.Combined, "Комбинированный")
        };

        /// <inheritdoc cref="ModeChoices"/>
        public IReadOnlyList<Choice<StatusChoice>> StatusChoices { get; } = new[]
        {
            new Choice<StatusChoice>(StatusChoice.All, "Любой статус"),
            new Choice<StatusChoice>(StatusChoice.Draft, "Черновик"),
            new Choice<StatusChoice>(StatusChoice.Ready, "Готов к печати"),
            new Choice<StatusChoice>(StatusChoice.Completed, "Выгружен")
        };

        /// <inheritdoc cref="ModeChoices"/>
        public IReadOnlyList<Choice<SortChoice>> SortChoices { get; } = new[]
        {
            new Choice<SortChoice>(SortChoice.ByChanged, "По изменению"),
            new Choice<SortChoice>(SortChoice.ByName, "По названию")
        };

        /// <summary>What the person is typing. Empty matches everything.</summary>
        public string SearchText
        {
            get => _searchText;
            set
            {
                if (SetProperty(ref _searchText, value ?? string.Empty)) ApplyFilters();
            }
        }

        /// <inheritdoc cref="SearchText"/>
        public Choice<ModeChoice>? SelectedMode
        {
            get => ModeChoices.FirstOrDefault(c => c.Value == _modeChoice);
            set
            {
                if (value is null || value.Value == _modeChoice) return;
                _modeChoice = value.Value;
                OnPropertyChanged();
                ApplyFilters();
            }
        }

        /// <inheritdoc cref="SearchText"/>
        public Choice<StatusChoice>? SelectedStatus
        {
            get => StatusChoices.FirstOrDefault(c => c.Value == _statusChoice);
            set
            {
                if (value is null || value.Value == _statusChoice) return;
                _statusChoice = value.Value;
                OnPropertyChanged();
                ApplyFilters();
            }
        }

        /// <inheritdoc cref="SearchText"/>
        public Choice<SortChoice>? SelectedSort
        {
            get => SortChoices.FirstOrDefault(c => c.Value == _sortChoice);
            set
            {
                if (value is null || value.Value == _sortChoice) return;
                _sortChoice = value.Value;
                OnPropertyChanged();
                ApplyFilters();
            }
        }

        /// <summary>
        /// Whether the cards on screen are the whole list. Drives the "nothing found" note.
        /// </summary>
        public bool IsNarrowed =>
            !string.IsNullOrWhiteSpace(_searchText) ||
            _modeChoice != ModeChoice.All ||
            _statusChoice != StatusChoice.All;

        /// <summary>"12 из 39" while a filter is on, and just the total when it is not.</summary>
        public string FilterSummary => IsNarrowed
            ? $"{FilteredProjects.Count} из {Projects.Count}"
            : $"Всего: {Projects.Count}";

        /// <summary>Puts everything back. One control, for the moment somebody gives up.</summary>
        [RelayCommand]
        private void ClearFilters()
        {
            _searchText = string.Empty;
            _modeChoice = ModeChoice.All;
            _statusChoice = StatusChoice.All;

            OnPropertyChanged(nameof(SearchText));
            OnPropertyChanged(nameof(SelectedMode));
            OnPropertyChanged(nameof(SelectedStatus));

            ApplyFilters();
        }

        private void ApplyFilters()
        {
            var wanted = Projects.AsEnumerable();

            if (!string.IsNullOrWhiteSpace(_searchText))
            {
                // Ordinal-ignore-case, because somebody typing a name in Cyrillic should not
                // have to match the letter case the file was created with.
                var needle = _searchText.Trim();
                wanted = wanted.Where(p =>
                    p.Name is not null && p.Name.Contains(needle, StringComparison.OrdinalIgnoreCase));
            }

            wanted = _modeChoice switch
            {
                ModeChoice.UniqueFolders => wanted.Where(p => p.Mode == AppMode.UniqueFolders),
                ModeChoice.Combined => wanted.Where(p => p.Mode == AppMode.Combined),
                _ => wanted
            };

            wanted = _statusChoice switch
            {
                StatusChoice.Draft => wanted.Where(p => p.Status == ProjectStatus.NotFilled),
                StatusChoice.Ready => wanted.Where(p => p.Status == ProjectStatus.Ready),
                StatusChoice.Completed => wanted.Where(p => p.Status == ProjectStatus.SuccessfullyCompleted),
                _ => wanted
            };

            var ordered = _sortChoice == SortChoice.ByName
                ? wanted.OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase)
                : wanted.OrderByDescending(p => p.LastModified);

            FilteredProjects.Clear();
            foreach (var project in ordered) FilteredProjects.Add(project);

            OnPropertyChanged(nameof(IsNarrowed));
            OnPropertyChanged(nameof(FilterSummary));
            OnPropertyChanged(nameof(HasNoMatches));
        }

        /// <summary>
        /// True when a filter is on and nothing survived it.
        /// </summary>
        /// <remarks>
        /// An empty grid on its own says "you have no projects", which is a different and much
        /// worse thing to read. This is the difference between the two.
        /// </remarks>
        public bool HasNoMatches => IsNarrowed && FilteredProjects.Count == 0;
    }
}
