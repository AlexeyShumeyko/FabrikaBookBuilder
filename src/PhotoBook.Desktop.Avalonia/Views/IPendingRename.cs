namespace PhotoBook.Shell.Views;

/// <summary>
/// A screen that can have the project's name half-typed into it.
/// </summary>
/// <remarks>
/// The window asks every screen currently on show whether it has a rename open, instead of
/// knowing which screen that is. Two screens can do this and the window should not have to
/// know that there are two.
///
/// A click that lands on a text box or a button belongs to that control - it is either
/// typing or a press, and the rename stays open. Any other click means the person has
/// moved on, and a name left half-typed is a name they believe they saved. That is why this
/// is asked of the whole window rather than only of the box that lost the focus: clicking a
/// label does not take the focus in the first place, so nothing ever loses it.
/// </remarks>
public interface IPendingRename
{
    /// <summary>
    /// Applies the rename if one is open, and does nothing if none is.
    /// </summary>
    void TryCommitPendingRename();
}
