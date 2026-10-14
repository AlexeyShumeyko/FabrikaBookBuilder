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
    /// The action under the caption appears while the card is under the pointer.
    /// </summary>
    /// <remarks>
    /// Driven from here rather than from a style, because there is no way in this engine to
    /// make a child visible because its parent is hovered. The button is found by name among
    /// the card's own descendants, which is stable: it is the only one on the card with that
    /// name, and the card is what the pointer entered.
    /// </remarks>
    private void OnCardPointerEntered(object? sender, PointerEventArgs e)
        => SetCoverAction(sender, visible: true);

    private void OnCardPointerExited(object? sender, PointerEventArgs e)
        => SetCoverAction(sender, visible: false);

    private static void SetCoverAction(object? sender, bool visible)
    {
        if (sender is not Control card) return;

        var action = card.GetVisualDescendants()
            .OfType<Button>()
            .FirstOrDefault(b => b.Name == "AssignCoverAction");

        if (action is not null) action.IsVisible = visible;
    }
}
