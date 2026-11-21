using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Transformation;
using Avalonia.Threading;
using PhotoBook.Core;
using PhotoBook.Shell.Converters;
using PhotoBook.Shell.Services;

namespace PhotoBook.Shell.Views;

/// <summary>
/// The question asked before a saved project is deleted.
/// </summary>
/// <remarks>
/// <para>
/// It says what is about to be lost and what is not: the project's name, the mode it was made
/// in and what is inside it, then the promise that the photographs on disk are not part of the
/// deletion. A question that only says "delete this project?" leaves the person to remember
/// whether their files are at risk, which is the one thing they should not have to remember.
/// </para>
/// <para>
/// Escape and the close button mean no, Enter means yes, and the answer is the only thing the
/// panel completes with. The dimming and blurring of the page behind belongs to the shell.
/// </para>
/// </remarks>
public partial class DeleteProjectSheet : OverlaySheet<ConfirmAnswer>
{
    private const string UniqueIcon =
        "M3.5 8A2.5 2.5 0 0 1 6 5.5h3.6l1.8 2.2H18a2.5 2.5 0 0 1 2.5 2.5v7A2.5 2.5 0 0 1 18 19.7H6A2.5 2.5 0 0 1 3.5 17.2z";

    private const string CombinedIcon =
        "M5.3 3.5h3.4a1.8 1.8 0 0 1 1.8 1.8v3.4a1.8 1.8 0 0 1-1.8 1.8H5.3a1.8 1.8 0 0 1-1.8-1.8V5.3a1.8 1.8 0 0 1 1.8-1.8zM15.3 3.5h3.4a1.8 1.8 0 0 1 1.8 1.8v3.4a1.8 1.8 0 0 1-1.8 1.8h-3.4a1.8 1.8 0 0 1-1.8-1.8V5.3a1.8 1.8 0 0 1 1.8-1.8zM5.3 13.5h3.4a1.8 1.8 0 0 1 1.8 1.8v3.4a1.8 1.8 0 0 1-1.8 1.8H5.3a1.8 1.8 0 0 1-1.8-1.8v-3.4a1.8 1.8 0 0 1 1.8-1.8zM15.3 13.5h3.4a1.8 1.8 0 0 1 1.8 1.8v3.4a1.8 1.8 0 0 1-1.8 1.8h-3.4a1.8 1.8 0 0 1-1.8-1.8v-3.4a1.8 1.8 0 0 1 1.8-1.8z";

    public DeleteProjectSheet(ProjectInfo project)
    {
        InitializeComponent();

        ProjectName = project.Name;
        ProjectMeta =
            $"{ModeLabel(project.Mode)} · Книг: {project.BookCount} шт. · Разворотов: {project.PageCount} стр.";
        ModeIcon = Geometry.Parse(project.Mode == AppMode.UniqueFolders ? UniqueIcon : CombinedIcon);
        ModeInk = ThemeResources.Brush(
            project.Mode == AppMode.UniqueFolders ? "ModeIconUnique" : "Accent");

        // The card is told what to show before it is put up, so nothing arrives after the
        // first frame has been drawn and moves under the pointer.
        DataContext = this;

        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
    }

    public string ProjectName { get; }

    public string ProjectMeta { get; }

    public Geometry ModeIcon { get; }

    public IBrush ModeInk { get; }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        // The card is focusable so that Escape and Enter reach it without the person having to
        // click something first; the rise then runs from the state the markup starts in.
        Focus();

        Dispatcher.UIThread.Post(
            () =>
            {
                Card.RenderTransform = TransformOperations.Parse("translate(0px,0px) scale(1)");
                Card.Opacity = 1;
            },
            DispatcherPriority.Loaded);
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Leave();
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            Finish(ConfirmAnswer.Yes);
            e.Handled = true;
        }
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Leave();

    private void OnDeleteClick(object? sender, RoutedEventArgs e) => Finish(ConfirmAnswer.Yes);

    private static string ModeLabel(AppMode mode) =>
        AppModeToLabelConverter.Instance.Convert(mode, typeof(string), null, CultureInfo.InvariantCulture)
            as string ?? string.Empty;
}
