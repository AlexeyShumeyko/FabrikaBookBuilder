using System;
using Avalonia.Controls;
using PhotoBook.Application;
using PhotoBook.Core;
using PhotoBook.Presentation.ViewModels;
using PhotoBook.Shell.Views;

namespace PhotoBook.Shell.Navigation
{
    /// <summary>
    /// This shell's screens: the same shapes as the WPF shell, built out of a different
    /// framework.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This class is where a screen stops being a concept and becomes a window. Everything
    /// above it asks for "the screen for this mode" and never learns what came back.
    /// </para>
    /// <para>
    /// View models are passed in rather than resolved here: the navigator holds them, and a
    /// factory that resolved its own would answer the same question twice, in two places
    /// that would then drift apart. The projects list is the exception, because it is built
    /// per visit - so it arrives through a function.
    /// </para>
    /// </remarks>
    internal class AvaloniaScreenViewFactory : IScreenViewFactory
    {
        private readonly UniqueFoldersViewModel _uniqueFolders;
        private readonly CombinedModeViewModel _combined;
        private readonly Func<ProjectListViewModel> _newProjectsViewModel;

        public AvaloniaScreenViewFactory(
            UniqueFoldersViewModel uniqueFolders,
            CombinedModeViewModel combined,
            Func<ProjectListViewModel> newProjectsViewModel)
        {
            _uniqueFolders = uniqueFolders;
            _combined = combined;
            _newProjectsViewModel = newProjectsViewModel;
        }

        public object CreateScreen(AppMode mode, object viewModel) => mode switch
        {
            AppMode.StartScreen => new StartScreenView(),

            AppMode.ProjectList => ProjectsScreen(viewModel),

            AppMode.UniqueFolders => new UniqueFoldersView
            {
                DataContext = viewModel as UniqueFoldersViewModel ?? _uniqueFolders
            },

            AppMode.Combined => new CombinedModeView
            {
                DataContext = viewModel as CombinedModeViewModel ?? _combined
            },

            _ => throw new ArgumentOutOfRangeException(
                nameof(mode),
                mode,
                "This shell has no screen for this mode yet.")
        };

        /// <summary>
        /// A projects list, wrapped around the view model the navigator was already holding.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The view model is what the navigator had in hand; the view is this factory's job.
        /// Returning the bare view model puts a type name on the window where the screen
        /// should be, which is exactly what it did the first time.
        /// </para>
        /// <para>
        /// The handed view model is used as it comes. Building another one here was measured
        /// as a second full read of the index and a second set of cards on every visit: the
        /// navigator had already loaded the screen's data, and the screen then threw that
        /// away and loaded its own. The factory keeps the function for the case where the
        /// navigator has nothing to give.
        /// </para>
        /// </remarks>
        private Control ProjectsScreen(object viewModel) => new ProjectListView
        {
            DataContext = viewModel as ProjectListViewModel ?? _newProjectsViewModel()
        };
    }
}
