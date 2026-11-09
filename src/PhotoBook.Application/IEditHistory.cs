using System;
using System.Collections.Generic;
using PhotoBook.Core;

namespace PhotoBook.Application
{
    /// <summary>
    /// Remembers what a project looked like a moment ago, so an edit can be taken back.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This used to share a class with building a project out of folders. The two have
    /// nothing in common beyond both mentioning <see cref="Project"/>: one needs the file
    /// system and image sizes, the other needs nothing at all and is two stacks. An undo
    /// history is not a project service, and every call site had to know which half of the
    /// service it was actually using.
    /// </para>
    /// <para>
    /// The history holds clones rather than references, because the editor mutates the
    /// project it is given; storing the live object would make undo return the state it just
    /// left.
    /// </para>
    /// </remarks>
    public interface IEditHistory
    {
        /// <summary>Pushes the current state onto the undo stack and forgets the redo one.</summary>
        void SaveState(Project project);

        /// <summary>The state before the last <see cref="SaveState"/>, or null if there is none.</summary>
        Project? Undo(Project currentProject);

        /// <summary>The state undone most recently, or null if there is none.</summary>
        Project? Redo(Project currentProject);

        /// <summary>Forgets everything. Called when a different project is opened.</summary>
        void ClearHistory();

        bool CanUndo { get; }
        bool CanRedo { get; }
    }

    internal class EditHistory : IEditHistory
    {
        private readonly Stack<Project> _undoStack = new();
        private readonly Stack<Project> _redoStack = new();

        public bool CanUndo => _undoStack.Count > 0;
        public bool CanRedo => _redoStack.Count > 0;

        public void SaveState(Project project)
        {
            if (project == null) return;

            var clone = project.Clone();
            _undoStack.Push(clone);

            // A new edit abandons the redo branch: the future the user had stepped back
            // from no longer follows from where they are.
            _redoStack.Clear();
        }

        public Project? Undo(Project currentProject)
        {
            if (!CanUndo || currentProject == null) return null;

            _redoStack.Push(currentProject.Clone());

            return _undoStack.Pop();
        }

        public Project? Redo(Project currentProject)
        {
            if (!CanRedo || currentProject == null) return null;

            _undoStack.Push(currentProject.Clone());

            return _redoStack.Pop();
        }

        public void ClearHistory()
        {
            _undoStack.Clear();
            _redoStack.Clear();
        }
    }
}
