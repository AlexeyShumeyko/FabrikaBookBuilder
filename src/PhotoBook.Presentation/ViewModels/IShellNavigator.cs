using PhotoBook.Core;

namespace PhotoBook.Presentation.ViewModels
{
    /// <summary>
    /// The way out of a screen: where the user goes next, and which editor a project belongs
    /// to.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Implemented by the shell's own navigation hub. It exists because the hub and the
    /// screens it shows refer to each other: the hub holds the editors, and a screen that
    /// wants to go somewhere has to ask the hub. Screens used to reach it by asking the
    /// service container for a concrete class - a lookup at the moment of the click, whose
    /// failure would surface as a button that silently does nothing.
    /// </para>
    /// <para>
    /// Assigning <see cref="CurrentMode"/> is the only way to move between screens, and the
    /// setter is where the rule "a mode needs an open project" is enforced. Everything else
    /// here is a named case of that rule, so that callers do not have to know it.
    /// </para>
    /// </remarks>
    public interface IShellNavigator
    {
        /// <summary>The screen on show. Assigning it moves there.</summary>
        AppMode CurrentMode { get; set; }

        /// <summary>Enters the editor of <paramref name="mode"/> for a project that exists.</summary>
        void OpenProject(AppMode mode);

        /// <summary>Empties the editor of <paramref name="mode"/> and enters it.</summary>
        void CreateProject(AppMode mode);

        /// <summary>The project is done; back to the list, which reloads.</summary>
        void EndSession();

        /// <summary>The editor that owns <paramref name="mode"/>, or null if none does.</summary>
        IProjectEditor? EditorFor(AppMode mode);

        /// <summary>
        /// Hands a freshly opened project to the editor that owns its mode.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Separate from <see cref="OpenProject"/> on purpose, because the two happen at
        /// different moments: OpenProject says where to go, this says what the editor will
        /// find when it arrives. The editor cannot be given its project until it exists and
        /// can receive one, and when that moment is differs per engine - in WPF it is two
        /// dispatches after the view has loaded and its bindings are live.
        /// </para>
        /// <para>
        /// Which is why the list asks rather than decides. A screen that reached for a
        /// dispatcher to work around it had taken on a responsibility it cannot have in
        /// another engine.
        /// </para>
        /// <para>
        /// Two arguments because an editor needs two things and they are not the same: the
        /// mutable project it will work on, and the index record describing it. Collapsing
        /// them would mean either rebuilding the record or reaching back for the project
        /// later, from somewhere with no reason to still have it.
        /// </para>
        /// <para>
        /// May complete after the call returns. The caller has nothing to wait for and must
        /// not assume the editor already holds the project.
        /// </para>
        /// </remarks>
        void HandProjectToEditor(Project project, ProjectInfo record);
    }
}
