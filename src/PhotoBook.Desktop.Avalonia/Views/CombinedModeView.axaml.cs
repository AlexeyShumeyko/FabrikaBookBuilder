using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using PhotoBook.Core;
using PhotoBook.Presentation.ViewModels;

namespace PhotoBook.Shell.Views;

/// <summary>
/// The combined screen: choose a run, load photographs, lay them into books.
/// </summary>
/// <remarks>
/// <para>
/// Every action here belongs to a model and none of them is implemented in this file. What
/// the file does own is what the other shell's markup expressed as triggers and
/// storyboards: which of two words in a slot's footer is showing, and the small zoom when
/// the pointer is on a photograph. Both are facts about the pointer, and the pointer is not
/// in the model.
/// </para>
/// <para>
/// The actions go through the model by name rather than through a binding, for the same
/// reason the folders screen does: these buttons sit inside rows of books and slots, and
/// the nearest ancestor carrying a view model is a book or a page, not this screen. A
/// binding that asks a book to duplicate itself finds nothing, and the button is dead
/// without looking disabled.
/// </para>
/// </remarks>
public partial class CombinedModeView : UserControl, IPendingRename
{
    public CombinedModeView()
    {
        InitializeComponent();

        // Drag and drop is handled once, here, rather than on each zone.
        //
        // In this engine a control does not declare that it accepts a drop: it is a call on
        // the view, and the events travel up from whatever was hit. Handling them at the root
        // is also the only way that reaches the slot cards at all - those are created per
        // book by a template and have no name this file could ask for.
        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);
    }

    private CombinedModeViewModel? Model => DataContext as CombinedModeViewModel;

    // ---------------------------------------------------------------- renaming --

    private void OnProjectNamePencilClick(object? sender, RoutedEventArgs e) => BeginRename();

    private void OnProjectNameClick(object? sender, PointerPressedEventArgs e) => BeginRename();

    private void OnProjectNameLostFocus(object? sender, RoutedEventArgs e) => CommitRename();

    private void OnProjectNameKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter:
                CommitRename();
                e.Handled = true;
                break;

            case Key.Escape:
                ShowNameAsText();
                e.Handled = true;
                break;
        }
    }

    private void BeginRename()
    {
        if (Model is not { } model) return;

        ProjectNameTextBox.Text = model.ProjectName ?? string.Empty;
        ProjectNameTextBlock.IsVisible = false;
        ProjectNameTextBox.IsVisible = true;
        ProjectNameTextBox.Focus();
        ProjectNameTextBox.SelectAll();
    }

    /// <summary>
    /// Applies the rename if one is open, and does nothing if none is.
    /// </summary>
    /// <remarks>
    /// Posted rather than called straight away, because the click that dismisses the box is
    /// the very event being handled. Taking the box away from under the event that is still
    /// travelling loses the click, and the next click then looks like the first one failed.
    /// </remarks>
    public void TryCommitPendingRename()
    {
        if (!ProjectNameTextBox.IsVisible) return;

        Dispatcher.UIThread.Post(CommitRename, DispatcherPriority.Loaded);
    }

    private void CommitRename()
    {
        if (Model is not { } model) return;

        ShowNameAsText();

        var newName = ProjectNameTextBox.Text?.Trim();
        if (string.IsNullOrEmpty(newName) || newName == model.ProjectName) return;

        // The record first and the property second: the header shows the record's name, and
        // the only thing that makes it re-read is the notification the property raises.
        if (model.CurrentProjectInfo is { } info)
        {
            info.Name = newName;
            info.LastModified = DateTime.Now;
        }

        model.ProjectName = newName;
        _ = model.SaveProjectNameOnlyAsync();
    }

    private void ShowNameAsText()
    {
        ProjectNameTextBox.IsVisible = false;
        ProjectNameTextBlock.IsVisible = true;
    }

    // ------------------------------------------------------------- whole books --

    private void OnDuplicateBookClick(object? sender, RoutedEventArgs e)
        => Execute(nameof(CombinedModeViewModel.DuplicateBookCommand), sender);

    private void OnDeleteBookClick(object? sender, RoutedEventArgs e)
        => Execute(nameof(CombinedModeViewModel.DeleteBookCommand), sender);

    // ------------------------------------------------------------------- slots --

    private void OnClearSlotClick(object? sender, RoutedEventArgs e)
        => Execute(nameof(CombinedModeViewModel.DeletePageCommand), sender);

    private void OnApplyToAllClick(object? sender, PointerPressedEventArgs e)
        => Execute(nameof(CombinedModeViewModel.DuplicateToAllBooksCommand), sender);

    private void OnFileItemClick(object? sender, PointerPressedEventArgs e)
        => Execute(nameof(CombinedModeViewModel.LoadPageFileCommand), sender);

    // ------------------------------------------------------------------ upload --

    private void OnUploadAreaClick(object? sender, PointerPressedEventArgs e)
        => Execute(nameof(CombinedModeViewModel.LoadFilesCommand), sender);

    // ------------------------------------------------------------------- hover --

    /// <summary>
    /// The label and the "to all books" action share one cell and take turns.
    /// </summary>
    /// <remarks>
    /// The action only appears for a filled slot that is not already run-wide - there is
    /// nothing left to apply in that case, and hiding the label there left the footer of
    /// every ordinary slot empty at rest. The zoom is set here rather than by a transform in
    /// the markup because a style cannot scale by one and a bit without a trigger, and
    /// there is no trigger here.
    /// </remarks>
    /// <summary>The slot whose caption was last replaced, so it can be put back.</summary>
    /// <remarks>
    /// One slot at a time, remembered rather than found by walking the tree. The pair of
    /// words in a slot's footer swap places when the pointer is on the slot, and a swap
    /// that is put back only when the pointer leaves the slot can be stranded: the pointer
    /// goes from one slot to another, the second takes over the caption, and the first
    /// never hears about it again. A stranded caption reads as a photograph whose name is
    /// "to all the books", which is a thing to be told rather than a thing to be worked
    /// out.
    ///
    /// Moving to a new slot therefore puts the previous one back on the way in, not only on
    /// the way out.
    /// </remarks>
    private Control? _captionSwapped;

    private void OnSlotPointerEntered(object? sender, PointerEventArgs e)
    {
        if (sender is not Control slot) return;

        // Whoever held the caption last gives it back before anyone else takes it.
        if (_captionSwapped is not null && !ReferenceEquals(_captionSwapped, slot))
        {
            ResetCaption(_captionSwapped);
            _captionSwapped = null;
        }

        var (label, action) = FooterParts(slot);
        var page = slot.DataContext as Page;

        bool applicable = page is { IsShared: false, IsEmpty: false };

        if (label is not null) label.IsVisible = !applicable;
        if (action is not null) action.IsVisible = applicable;

        if (!applicable) return;

        _captionSwapped = slot;

        if (slot.GetVisualDescendants().OfType<Image>().FirstOrDefault() is { } photo)
            photo.RenderTransform = new ScaleTransform(1.04, 1.04);
    }

    private void OnSlotPointerExited(object? sender, PointerEventArgs e)
    {
        if (sender is not Control slot) return;

        ResetCaption(slot);
        if (ReferenceEquals(_captionSwapped, slot)) _captionSwapped = null;
    }

    /// <summary>Puts a slot's footer back to showing what the slot is.</summary>
    private static void ResetCaption(Control slot)
    {
        var (label, action) = FooterParts(slot);

        if (label is not null) label.IsVisible = true;
        if (action is not null) action.IsVisible = false;

        if (slot.GetVisualDescendants().OfType<Image>().FirstOrDefault() is { } photo)
            photo.RenderTransform = null;
    }

    private static (TextBlock? Label, TextBlock? Action) FooterParts(Control slot)
    {
        var texts = slot.GetVisualDescendants().OfType<TextBlock>().ToArray();

        return (
            texts.FirstOrDefault(t => t.Name == "SlotLabelText"),
            texts.FirstOrDefault(t => t.Name == "ApplyToAllText"));
    }

    // ------------------------------------------------------------ drag and drop --

    /// <summary>
    /// Which zone a drop landed on, and the thing it landed on.
    /// </summary>
    /// <remarks>
    /// Both handlers here only say yes and pass the question on. What arrives is a file the
    /// model already knows about, and where it lands is decided by which zone was hit.
    /// Refusing the drag would grey the zone out and take the gesture with it.
    /// </remarks>
    private void OnDragOver(object? sender, DragEventArgs e) => e.DragEffects = DragDropEffects.Copy;

    private void OnDrop(object? sender, DragEventArgs e)
    {
        if (ZoneUnder(e.Source) is not Control zone) return;

        // The upload zone takes a drop the same way it takes a click: the same command.
        Execute(
            zone.Name == "DropZone"
                ? nameof(CombinedModeViewModel.LoadFilesCommand)
                : nameof(CombinedModeViewModel.LoadPageFileCommand),
            zone);
    }

    /// <summary>The nearest zone above whatever was hit: the upload zone, or a slot.</summary>
    private static Control? ZoneUnder(object? source)
    {
        for (object? node = source as Avalonia.Visual; node is not null;)
        {
            if (node is Control { Name: "DropZone" or "SlotCard" } named) return named;
            node = (node as Avalonia.Visual)?.GetVisualParent();
        }

        return null;
    }

    // ------------------------------------------------------------------ shared --

    /// <summary>
    /// Runs one of the model's commands, with whatever the button was pointing at.
    /// </summary>
    /// <remarks>
    /// <paramref name="sender"/> is passed in rather than read from a field because every
    /// handler has it and none of them outlives its own call. The parameter is the button's
    /// own data context - a book, a page or a file - which is the thing the command acts on;
    /// when there is none, the command takes no argument at all rather than an empty one.
    /// </remarks>
    private void Execute(string command, object? sender)
    {
        var model = Model;
        if (model is null) return;

        if (typeof(CombinedModeViewModel).GetProperty(command)?.GetValue(model)
            is not System.Windows.Input.ICommand target)
            return;

        var parameter = (sender as Control)?.DataContext;

        if (parameter is null)
        {
            if (target.CanExecute(null)) target.Execute(null);
            return;
        }

        if (target.CanExecute(parameter)) target.Execute(parameter);
    }
}
