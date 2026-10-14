using System;
using Avalonia.Controls;
using PhotoBook.Application;
using PhotoBook.Core;
using PhotoBook.Presentation.ViewModels;

namespace PhotoBook.Shell.Navigation
{
    /// <summary>
    /// Where this shell is, and how it gets somewhere else.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The project list needs four things from the shell around it: the current mode, how to
    /// open a project, how to end a session, and where an opened project should be handed.
    /// Everything else it decides for itself.
    /// </para>
    /// <para>
    /// <b>The navigator drives the window.</b> It is the one that knows which screen a mode
    /// means, and it is the one that makes sure an editor has its project before its screen
    /// exists to show it. The window holds whatever it is given - the same shape as the WPF
    /// shell's navigator, which is the point: one behaviour, written twice.
    /// </para>
    /// </remarks>
    public class AvaloniaNavigator : IShellNavigator
    {
        private readonly IScreenViewFactory _screens;
        private readonly UniqueFoldersViewModel _uniqueFolders;
        private readonly CombinedModeViewModel _combined;

        private ContentControl? _host;
        private AppMode _currentMode = AppMode.ProjectList;
        private AppMode? _sessionMode;
        private (Project Project, ProjectInfo Record)? _pending;

        public AvaloniaNavigator(
            IScreenViewFactory screens,
            UniqueFoldersViewModel uniqueFolders,
            CombinedModeViewModel combined,
            Func<ProjectListViewModel> projectListProvider)
        {
            _screens = screens;
            _uniqueFolders = uniqueFolders;
            _combined = combined;
            ProjectListProvider = projectListProvider;
        }

        /// <summary>Builds the next projects list, once per visit.</summary>
        public Func<ProjectListViewModel> ProjectListProvider { get; }

        /// <summary>Raised whenever the mode changes, so a shell can react.</summary>
        public event EventHandler<AppMode>? ModeChanged;

        /// <summary>
        /// Raised when a project arrives or a session ends.
        /// </summary>
        /// <remarks>
        /// Separate from <see cref="ModeChanged"/> because the two do not move together: a
        /// project can arrive without the screen changing, and the session can end while the
        /// screen stays put. The header needs to hear about both, and folding them into one
        /// event would either miss one or fire on every tab click for nothing.
        /// </remarks>
        public event EventHandler? ProjectChanged;

        /// <summary>
        /// Which mode the current session belongs to, or null when no project is open.
        /// </summary>
        /// <remarks>
        /// This is what keeps an editor tab enabled while its project is open and disables it
        /// again after going back to the list. Reading it off the editor instead would keep
        /// the tab alive forever: an editor never forgets its project, only the session ends.
        /// </remarks>
        public AppMode? SessionMode => _sessionMode;

        public AppMode CurrentMode
        {
            get => _currentMode;
            set
            {
                bool changed = _currentMode != value;
                _currentMode = value;
                if (changed || _host?.Content is null) Show();
                ModeChanged?.Invoke(this, value);
            }
        }

        /// <summary>
        /// Gives the navigator the place it puts screens. The window does not exist when the
        /// container is built, so this comes afterwards - and it is the only thing the
        /// navigator needs from the window.
        /// </summary>
        public void Attach(ContentControl host)
        {
            _host = host;
            Show();
        }

        /// <summary>Puts the current mode's screen on the window.</summary>
        public void Show()
        {
            if (_host is null) return;

            // The projects list is built per visit - it exists to show the projects as they
            // are at the moment it is opened - so it is created here rather than kept. The
            // editors are singletons and come back exactly as they were left.
            var viewModel = _currentMode switch
            {
                AppMode.UniqueFolders => (object)_uniqueFolders,
                AppMode.Combined => _combined,
                _ => ProjectListProvider()
            };

            _host.Content = _screens.CreateScreen(_currentMode, viewModel);

            DeliverPending();
        }

        public void OpenProject(AppMode mode)
        {
            _sessionMode = mode;
            CurrentMode = mode;
        }

        /// <summary>
        /// Not built yet. Creating a project opens the mode-select screen, and that screen is
        /// a later step.
        /// </summary>
        public void CreateProject(AppMode mode)
        {
            _sessionMode = mode;
            throw new NotSupportedException("The mode-select screen has not been ported yet.");
        }

        public void EndSession()
        {
            _sessionMode = null;
            CurrentMode = AppMode.ProjectList;
            ProjectChanged?.Invoke(this, EventArgs.Empty);
        }

        public IProjectEditor? EditorFor(AppMode mode) => mode switch
        {
            AppMode.UniqueFolders => _uniqueFolders,
            AppMode.Combined => _combined,
            _ => null
        };

        /// <summary>
        /// Hands an opened project to the editor that owns its mode.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The hand-over is held rather than performed when the caller asks. The screen for a
        /// mode is created after the mode changes, and an editor asked for a project before
        /// its screen exists has nowhere to show it - which in the WPF shell needed two
        /// dispatches at Loaded priority, and is the reason that wait is written once, here,
        /// rather than at every call site.
        /// </para>
        /// <para>
        /// Delivery happens immediately after the screen is set, which in this engine is
        /// synchronous: the content is in place before the layout pass that binds it. No
        /// waiting, and no chance of a project arriving at an editor that cannot see it yet.
        /// </para>
        /// </remarks>
        public void HandProjectToEditor(Project project, ProjectInfo record)
            => _pending = (project, record);

        private void DeliverPending()
        {
            if (_pending is not { } pending) return;
            _pending = null;

            _sessionMode = pending.Record.Mode;
            EditorFor(pending.Record.Mode)?.SetProject(pending.Project, pending.Record);

            ProjectChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
