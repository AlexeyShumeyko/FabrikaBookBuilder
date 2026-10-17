using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.Input;
using PhotoBook.Core;

namespace PhotoBook.Presentation.ViewModels
{
    /// <summary>
    /// Narrowing a long list of projects down to the ones in hand.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two questions, because there are two questions somebody actually asks with twenty
    /// projects on the machine: <b>which editor am I working in</b>, and <b>which of those
    /// runs are finished</b>.
    /// </para>
    /// <para>
    /// There is deliberately no search box and no sorting control.
    /// </para>
    /// <para>
    /// Search was tried and removed: the list is already ordered by what was touched last,
    /// so the project somebody is looking for is almost always in the first screenful, and a
    /// box they have to focus, clear and re-focus is worse than scrolling once. Sorting by
    /// name was removed for a stronger reason - projects are named after the job and the
    /// useful order is the one that matches the work, which is the order they are already
    /// in.
    /// </para>
    /// <para>
    /// The words in the status filter are the words on the cards, read from
    /// <see cref="ProjectStatusText"/>. They are not written out again here on purpose: when
    /// they were, the filter labelled "готов к печати" selected the runs whose cards said
    /// "в процессе сборки", and there was no way for a person to tell that pressing it had
    /// done anything.
    /// </para>
    /// </remarks>
    public partial class ProjectListViewModel
    {
        /// <summary>Which editor a project belongs to.</summary>
        public enum ModeChoice
        {
            All,
            UniqueFolders,
            Combined
        }

        /// <summary>How far along a run is.</summary>
        public enum StatusChoice
        {
            All,
            Draft,
            InProgress,
            Completed
        }

        /// <summary>
        /// One option as the person reads it, paired with the state it stands for.
        /// </summary>
        /// <remarks>
        /// Paired rather than using the enum's own name for the same reason the card is: the
        /// name in the code is not an argument for putting it on the screen.
        /// </remarks>
        public sealed record Choice<T>(T Value, string Label) where T : struct, Enum;

        private ModeChoice _modeChoice = ModeChoice.All;
        private StatusChoice _statusChoice = StatusChoice.All;

        /// <summary>The cards actually shown.</summary>
        public ObservableCollection<ProjectInfo> FilteredProjects { get; } = new();

        /// <summary>The two editors.</summary>
        public IReadOnlyList<Choice<ModeChoice>> ModeChoices { get; } = new[]
        {
            new Choice<ModeChoice>(ModeChoice.All, "Все режимы"),
            new Choice<ModeChoice>(ModeChoice.UniqueFolders, "Уникальные папки"),
            new Choice<ModeChoice>(ModeChoice.Combined, "Комбинированный")
        };

        /// <summary>The three states a run can be in, in the words its own cards use.</summary>
        public IReadOnlyList<Choice<StatusChoice>> StatusChoices { get; } = new[]
        {
            new Choice<StatusChoice>(StatusChoice.All, "Любой статус"),
            new Choice<StatusChoice>(StatusChoice.Draft, ProjectStatusText.Of(ProjectStatus.NotFilled)),
            new Choice<StatusChoice>(StatusChoice.InProgress, ProjectStatusText.Of(ProjectStatus.Ready)),
            new Choice<StatusChoice>(StatusChoice.Completed, ProjectStatusText.Of(ProjectStatus.SuccessfullyCompleted))
        };

        /// <summary>Which editor to show.</summary>
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

        /// <summary>Which state to show.</summary>
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

        /// <summary>Whether anything is being filtered out.</summary>
        public bool IsNarrowed => _modeChoice != ModeChoice.All || _statusChoice != StatusChoice.All;

        /// <summary>
        /// "12 из 39" while narrowing, and the plain total when not.
        /// </summary>
        /// <remarks>
        /// In words when it is a total. A bare number beside two filters reads as "the filter
        /// returned 39", which is a different and wrong thing to believe.
        /// </remarks>
        public string FilterSummary =>
            IsNarrowed ? $"{FilteredProjects.Count} из {Projects.Count}" : $"Всего: {Projects.Count}";

        /// <summary>Puts both filters back. One control, for the moment somebody gives up.</summary>
        [RelayCommand]
        private void ClearFilters()
        {
            _modeChoice = ModeChoice.All;
            _statusChoice = StatusChoice.All;

            OnPropertyChanged(nameof(SelectedMode));
            OnPropertyChanged(nameof(SelectedStatus));

            ApplyFilters();
        }

        private void ApplyFilters()
        {
            var wanted = Projects.AsEnumerable();

            wanted = _modeChoice switch
            {
                ModeChoice.UniqueFolders => wanted.Where(p => p.Mode == AppMode.UniqueFolders),
                ModeChoice.Combined => wanted.Where(p => p.Mode == AppMode.Combined),
                _ => wanted
            };

            wanted = _statusChoice switch
            {
                StatusChoice.Draft => wanted.Where(p => p.Status == ProjectStatus.NotFilled),
                StatusChoice.InProgress => wanted.Where(p => p.Status == ProjectStatus.Ready),
                StatusChoice.Completed => wanted.Where(p => p.Status == ProjectStatus.SuccessfullyCompleted),
                _ => wanted
            };

            FilteredProjects.Clear();
            foreach (var project in wanted.OrderByDescending(p => p.LastModified))
            {
                FilteredProjects.Add(project);
            }

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
