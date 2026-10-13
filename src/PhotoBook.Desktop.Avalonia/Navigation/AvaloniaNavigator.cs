using System;
using PhotoBook.Core;
using PhotoBook.Presentation.ViewModels;

namespace PhotoBook.Shell.Navigation
{
    /// <summary>
    /// Where this shell is, and how it gets somewhere else.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The project list needs exactly four things from the shell around it: what the current
    /// mode is, how to open a project, how to end a session, and where an opened project
    /// should be handed. Everything else it decides for itself.
    /// </para>
    /// <para>
    /// Until the editors are ported, <see cref="EditorFor"/> has no editor to name and
    /// <see cref="HandProjectToEditor"/> has nowhere to hand anything to. Both say so rather
    /// than guessing: a list that opened a project into nothing would look like the data was
    /// lost, which is the one thing it must never look like.
    /// </para>
    /// </remarks>
    public class AvaloniaNavigator : IShellNavigator
    {
        /// <summary>Raised whenever <see cref="CurrentMode"/> changes, so the shell can repaint.</summary>
        public event EventHandler<AppMode>? ModeChanged;

        private AppMode _currentMode = AppMode.ProjectList;

        public AppMode CurrentMode
        {
            get => _currentMode;
            set
            {
                if (_currentMode == value) return;
                _currentMode = value;
                ModeChanged?.Invoke(this, value);
            }
        }

        /// <summary>The project list is the first screen this shell has, so it opens it.</summary>
        public void OpenProject(AppMode mode)
            => CurrentMode = mode;

        /// <summary>
        /// Not built yet. Creating a project opens the mode-select screen, and that screen is
        /// a later step.
        /// </summary>
        public void CreateProject(AppMode mode)
            => throw new NotSupportedException("The mode-select screen has not been ported yet.");

        public void EndSession() => CurrentMode = AppMode.ProjectList;

        /// <summary>No editor exists in this shell yet, so there is none to name.</summary>
        public IProjectEditor? EditorFor(AppMode mode) => null;

        /// <summary>
        /// Nowhere to hand the project to. Silent rather than an exception, because the open
        /// path runs from a command and an exception there would end the process with no
        /// explanation - which is what a missing editor must never look like.
        /// </summary>
        public void HandProjectToEditor(Project project, ProjectInfo record)
        {
        }
    }
}