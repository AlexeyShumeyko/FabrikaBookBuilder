using System;
using System.Linq;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using PhotoBook.Core;
using PhotoBook.Presentation.ViewModels;
using PhotoBook.Shell.Converters;

namespace PhotoBook.Shell.Views;

/// <summary>
/// The unique-folders screen.
/// </summary>
/// <remarks>
/// It owns no behaviour of its own. It knows three things the models do not: that the arrows
/// move a page, that a slot's action appears under the pointer, and that a photograph is
/// decoded for the monitor this window is on - all three questions about the screen rather
/// than about a book.
/// </remarks>
public partial class UniqueFoldersView : UserControl
{
    private double _renderScaling = 1;

    public UniqueFoldersView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Pixels per layout unit on the monitor this window is on.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Photographs are decoded in device pixels while a cell is measured in layout units,
    /// so without this a photograph decoded for a 100-percent monitor is stretched back up
    /// on a 150- or 200-percent one - which is why the tiles went soft the moment the window
    /// moved to a denser screen. Read from the top level rather than from a setting,
    /// because the setting is per monitor and this is the only thing that knows which one
    /// the window is on.
    /// </para>
    /// <para>
    /// Nothing here names the property being watched. Naming it meant naming its type as
    /// well, and that type is not in the namespace its own name points at; reading the value
    /// and letting every change notify is both simpler and one less thing to get wrong.
    /// </para>
    /// </remarks>
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        if (TopLevel.GetTopLevel(this) is not { } top) return;

        top.PropertyChanged += (_, _) => TrackScaling(top);
        TrackScaling(top);
    }

    private void TrackScaling(TopLevel top)
    {
        double scaling = top.RenderScaling;
        if (Math.Abs(scaling - _renderScaling) < 0.001) return;

        _renderScaling = scaling;
        BitmapScale.Current = scaling;
    }

    /// <summary>
    /// Renaming a project, where its name already is.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A name being renamed is already on screen, and a second window for it is a second
    /// thing to look at. The whole name is the target, not only the pencil.
    /// </para>
    /// <para>
    /// Order matters when saving. The header shows the project record's name, and the only
    /// thing that makes it re-read is the notification raised by the name property. Writing
    /// the record first and the property second is what makes the top bar follow the rename;
    /// the other way round the rename lands in the panel and the header keeps showing the
    /// old one.
    /// </para>
    /// </remarks>
    private void OnProjectNamePencilClick(object? sender, RoutedEventArgs e) => BeginProjectRename();

    private void OnProjectNameClick(object? sender, PointerPressedEventArgs e) => BeginProjectRename();

    private void OnProjectNameLostFocus(object? sender, RoutedEventArgs e) => CommitProjectName();

    private void OnProjectNameKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter:
                CommitProjectName();
                e.Handled = true;
                break;

            case Key.Escape:
                ShowProjectNameAsText();
                e.Handled = true;
                break;
        }
    }

    private void BeginProjectRename()
    {
        if (DataContext is not UniqueFoldersViewModel editor) return;

        ProjectNameTextBox.Text = editor.ProjectName ?? string.Empty;
        ProjectNameTextBlock.IsVisible = false;
        ProjectNameTextBox.IsVisible = true;

        ProjectNameTextBox.Focus();
        ProjectNameTextBox.SelectAll();
    }

    private void CommitProjectName()
    {
        if (DataContext is not UniqueFoldersViewModel editor) return;

        ShowProjectNameAsText();

        var newName = ProjectNameTextBox.Text?.Trim();
        if (string.IsNullOrEmpty(newName) || newName == editor.ProjectName) return;

        if (editor.CurrentProjectInfo is { } info)
        {
            info.Name = newName;
            info.LastModified = DateTime.Now;
        }

        editor.ProjectName = newName;

        _ = editor.SaveProjectNameOnlyAsync();
    }

    private void ShowProjectNameAsText()
    {
        ProjectNameTextBox.IsVisible = false;
        ProjectNameTextBlock.IsVisible = true;
    }

    /// <summary>
    /// The arrows move a page within its book.
    /// </summary>
    /// <remarks>
    /// Done here rather than bound, because the button sits inside the row of slots and the
    /// nearest ancestor carrying a view model is that row - a book, not the screen. A binding
    /// that asks that ancestor to move a page up finds nothing, and the button is simply
    /// dead, which is what it was. The model still decides; this only says which page and
    /// which way.
    /// </remarks>
    private void OnMovePageLeftClick(object? sender, RoutedEventArgs e)
        => Execute(nameof(UniqueFoldersViewModel.MovePageUpCommand), sender);

    private void OnMovePageRightClick(object? sender, RoutedEventArgs e)
        => Execute(nameof(UniqueFoldersViewModel.MovePageDownCommand), sender);

    /// <summary>Promotes a slot to be its book's cover.</summary>
    private void OnAssignCoverClick(object? sender, RoutedEventArgs e)
        => Execute(nameof(UniqueFoldersViewModel.AssignCoverCommand), sender);

    private void Execute(string command, object? sender)
    {
        var editor = DataContext as UniqueFoldersViewModel;
        var page = (sender as Control)?.DataContext as Page;

        if (editor is null || page is null) return;
        if (typeof(UniqueFoldersViewModel).GetProperty(command)?.GetValue(editor) is not ICommand target)
            return;

        if (target.CanExecute(page)) target.Execute(page);
    }

    /// <summary>
    /// The caption makes way for the action while the card is under the pointer.
    /// </summary>
    /// <remarks>
    /// <para>
    /// They occupy the same half of the same row, so one of them has to go. Which one is a
    /// fact about the pointer rather than about the page, and the pointer is not in the
    /// model - which is why this is done here and not by a converter. Bound to the slot
    /// instead, it hid the caption on every spread and left the two words printed on top of
    /// each other.
    /// </para>
    /// <para>
    /// A cover has no action to offer, so its caption stays where it is; that is what the
    /// action's own visibility decides, and the caption only moves if the action appeared.
    /// </para>
    /// </remarks>
    private void OnCardPointerEntered(object? sender, PointerEventArgs e)
        => SwapCaptionForAction(sender, action: true);

    private void OnCardPointerExited(object? sender, PointerEventArgs e)
        => SwapCaptionForAction(sender, action: false);

    private static void SwapCaptionForAction(object? sender, bool action)
    {
        if (sender is not Control card) return;

        var parts = card.GetVisualDescendants().OfType<Control>().ToArray();

        var button = parts.OfType<Button>().FirstOrDefault(b => b.Name == "AssignCoverAction");
        if (button is null) return;

        button.IsVisible = action;

        var caption = parts.OfType<TextBlock>().FirstOrDefault(t => t.Name == "PageCaption");
        if (caption is not null) caption.IsVisible = !action;
    }
}
