using System;
using PhotoBook.Application;
using PhotoBook.Core;
using PhotoBookRenamer.Presentation.ViewModels;

namespace PhotoBookRenamer.Presentation.Views
{
    /// <summary>
    /// The WPF answer to <see cref="IScreenViewFactory"/>: the five screens this shell owns,
    /// each wrapped around the view model the navigator chose.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the only place left in the shell that knows a screen is a
    /// <c>UserControl</c>. The navigator asks for a screen and gets one; it no longer has
    /// to know how to build one, which is what lets the same navigator drive the Avalonia
    /// shell - where this file has a counterpart that knows a screen is something else.
    /// </para>
    /// <para>
    /// Each cast below is the contract being enforced: the navigator pairs a mode with the
    /// view model that mode is for, and a wrong pair is a bug here rather than a null
    /// reference somewhere inside a freshly built view.
    /// </para>
    /// </remarks>
    internal class WpfScreenViewFactory : IScreenViewFactory
    {
        public object CreateScreen(AppMode mode, object viewModel) => mode switch
        {
            AppMode.StartScreen => new StartScreenView((MainViewModel)viewModel),

            // Built per visit rather than kept: the list exists to show the projects as
            // they are at the moment it is opened.
            AppMode.ProjectList => new ProjectListView((ProjectListViewModel)viewModel),

            // One shell, one editor instance per mode: switching away and back finds the
            // editor as it was left, which is why these two pass the singletons through.
            AppMode.UniqueFolders => new UniqueFoldersView((UniqueFoldersViewModel)viewModel),
            AppMode.Combined => new CombinedModeView((CombinedModeViewModel)viewModel),

            _ => throw new ArgumentOutOfRangeException(
                nameof(mode),
                mode,
                "There is no screen for this mode.")
        };
    }
}
