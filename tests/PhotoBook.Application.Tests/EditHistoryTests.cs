using PhotoBook.Application;
using PhotoBook.Core;

namespace PhotoBook.Application.Tests;

/// <summary>
/// The undo history, which the campaign split out of the old project service without
/// changing a line of its behaviour. It had no tests at all, which is how a two-stack class
/// that every edit in the folders mode goes through went unexamined.
///
/// The contract, stated once so the tests read against it: the editor calls SaveState
/// <em>before</em> it changes anything, so Undo pops the state it saved and Redo puts back
/// the one it moved away from. Nothing here decides which saves are interesting; that is the
/// editor's job, not the stack's.
/// </summary>
public class EditHistoryTests
{
    /// <summary>A project whose single page carries a marker, so states can be told apart.</summary>
    private static Project State(string marker) =>
        TestProject.Folders(TestProject.Book(1, $"/photos/{marker}-cover.jpg", $"/photos/{marker}.jpg"));

    /// <summary>The marker of a state's single page.</summary>
    private static string Marker(Project project) => Path.GetFileName(project.Books[0].Pages[0].SourcePath);

    [Fact]
    public void ANewHistoryHasNothingToUndoOrRedo()
    {
        var history = new EditHistory();

        Assert.False(history.CanUndo);
        Assert.False(history.CanRedo);
    }

    [Fact]
    public void UndoWithNothingSavedReturnsNull()
    {
        var history = new EditHistory();

        Assert.Null(history.Undo(State("first")));
    }

    [Fact]
    public void RedoWithNothingUndoneReturnsNull()
    {
        var history = new EditHistory();

        Assert.Null(history.Redo(State("first")));
    }

    [Fact]
    public void UndoReturnsTheStateThatWasSaved()
    {
        var history = new EditHistory();

        history.SaveState(State("before"));
        var returned = history.Undo(State("after"));

        Assert.NotNull(returned);
        Assert.Equal("before.jpg", Marker(returned!));
    }

    [Fact]
    public void RedoReturnsTheStateThatWasUndone()
    {
        var history = new EditHistory();
        history.SaveState(State("before"));
        var undone = history.Undo(State("after"));

        var redone = history.Redo(undone!);

        Assert.NotNull(redone);
        Assert.Equal("after.jpg", Marker(redone!));
    }

    [Fact]
    public void ANewEditAbandonsTheRedoBranch()
    {
        var history = new EditHistory();
        var first = State("first");
        history.SaveState(first);
        var undone = history.Undo(first);
        Assert.True(history.CanRedo);

        history.SaveState(State("different"));

        // The future the user stepped back from no longer follows from where they are, so
        // Redo must decline rather than jump somewhere they never were.
        Assert.False(history.CanRedo);
        Assert.Null(history.Redo(undone!));
    }

    [Fact]
    public void UndoWalksBackThroughEverySavedStateAndRedoWalksForwardAgain()
    {
        var history = new EditHistory();
        history.SaveState(State("first"));
        history.SaveState(State("second"));
        history.SaveState(State("third"));

        // The stack pops the most recently saved state first, which is the one the editor
        // saved before it made the change the user is now taking back.
        var latest = history.Undo(State("current"));
        var earlier = history.Undo(latest!);
        var forward = history.Redo(earlier!);

        Assert.Equal("third.jpg", Marker(latest!));
        Assert.Equal("second.jpg", Marker(earlier!));
        Assert.Equal("third.jpg", Marker(forward!));

        // Redo pushes the state it handed back onto the undo stack, so the history is still
        // two steps deep rather than emptied. Undoing again therefore returns to where it
        // was, not past it.
        Assert.True(history.CanUndo);
        Assert.Equal("second.jpg", Marker(history.Undo(forward!)!));
    }

    [Fact]
    public void WhatComesBackIsACopySoTheEditorCannotMutateThePast()
    {
        var history = new EditHistory();
        var saved = State("saved");
        history.SaveState(saved);

        // The editor keeps working on the object it passed in. If the history had kept the
        // reference instead of a clone, "the past" would change underneath it.
        var returned = history.Undo(State("working"));
        Assert.NotNull(returned);

        returned!.Books[0].Name = "rewritten after the fact";

        // Same saved state, asked for again: the history was emptied by the Undo above, so
        // save it once more and check the clone was unaffected by what happened to the first.
        history.SaveState(State("again"));
        var secondTime = history.Undo(State("working"));
        Assert.NotNull(secondTime);
        Assert.Equal("Book 1", secondTime!.Books[0].Name);
    }

    [Fact]
    public void ClearHistoryForgetsBothDirections()
    {
        var history = new EditHistory();
        var project = State("first");

        history.SaveState(project);
        var undone = history.Undo(project);
        Assert.True(history.CanRedo);

        history.ClearHistory();

        Assert.False(history.CanUndo);
        Assert.False(history.CanRedo);
        Assert.Null(history.Undo(project));
        Assert.Null(history.Redo(undone!));
    }

    [Fact]
    public void SavingTheSameStateTwiceCountsAsTwoSteps()
    {
        var history = new EditHistory();
        var project = State("same");

        history.SaveState(project);
        history.SaveState(project);

        // A stack, not a set of distinct states. An editor that saves without changing
        // anything can be stepped back through, and the history does not decide for itself
        // which saves are interesting.
        Assert.True(history.CanUndo);
        Assert.NotNull(history.Undo(project));
        Assert.True(history.CanUndo);
        Assert.NotNull(history.Undo(project));
        Assert.False(history.CanUndo);
    }

    [Fact]
    public void UndoAndRedoRefuseWhenThereIsNoProjectToSwapWith()
    {
        var history = new EditHistory();
        history.SaveState(State("saved"));

        // The editor passes whatever it currently holds. A null there means there is nothing
        // to put on the other stack, and silently swapping one stack for the other would
        // lose the state the user is standing on.
        Assert.Null(history.Undo(null!));
        Assert.Null(history.Redo(null!));
        Assert.True(history.CanUndo);
    }
}
