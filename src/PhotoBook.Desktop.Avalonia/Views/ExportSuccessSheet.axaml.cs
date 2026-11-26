using System;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Transformation;
using Avalonia.Threading;

namespace PhotoBook.Shell.Views;

/// <summary>
/// Says that the files are written, and where they went.
/// </summary>
/// <remarks>
/// <para>
/// A panel over the page rather than a window, for the reason every other question in this
/// program is one: the page behind it dims and blurs, which is what says the panel is on top
/// of this program's own screen. A frameless window over the same page had no edge to say it
/// at all, and left the screen behind it as legible as it was before.
/// </para>
/// <para>
/// It answers which of the two things the person wants next, and the answer is the whole
/// reason it exists: «Открыть папку» hands the result over to the file manager, «Вернуться к
/// проекту» leaves it where it is. Leaving counts as the second - on a page of one's own
/// nothing but leaving is a request to open anything.
/// </para>
/// <para>
/// The count is read from the folder itself rather than from the export's own bookkeeping: the
/// number that finished the run is a number the sampler trusts without checking, and a folder
/// on disk is the one thing that cannot be wrong about what is in it.
/// </para>
/// </remarks>
public partial class ExportSuccessSheet : OverlaySheet<ConfirmAnswer>
{
    private readonly string _folder;

    public ExportSuccessSheet(string folder)
    {
        InitializeComponent();

        _folder = folder;
        PathText.Text = folder;
        SummaryText.Text = BuildSummary(folder);

        KeyDown += OnKeyDown;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        // The card is drawn from nothing and settles into place, which is the prototype's
        // modalIn. Both halves of that are done here rather than in the markup: a panel that
        // was made to look finished before it arrived had nothing to settle from, and one that
        // is never told to arrive stays at zero opacity over a blurred page.
        //
        // The focus is asked for twice for the same reason it is in the export panel: the host
        // puts the panel in before it shows the layer, so the first call lands on a surface
        // nobody can see, and a panel without focus answers neither Escape nor Enter.
        Focus();

        Dispatcher.UIThread.Post(
            () =>
            {
                Card.RenderTransform = TransformOperations.Parse("translate(0px,0px) scale(1)");
                Card.Opacity = 1;
                Focus();
            },
            DispatcherPriority.Loaded);
    }

    private static string BuildSummary(string folder)
    {
        int files = 0;

        try
        {
            if (Directory.Exists(folder))
                files = Directory.GetFiles(folder, "*.jpg", SearchOption.AllDirectories).Length;
        }
        catch
        {
            // A count is a nicety; never let it break the panel.
        }

        return files > 0
            ? $"Готово, {Files(files)}. Папка готова к загрузке на печать."
            : "Все файлы сформированы и готовы к отправке на печать.";
    }

    /// <summary>"1 файл", "4 файла", "11 файлов" - a count that reads as a sentence.</summary>
    private static string Files(int n)
    {
        string word = (n % 100) is >= 11 and <= 14 ? "файлов"
            : (n % 10) == 1 ? "файл"
            : (n % 10) is >= 2 and <= 4 ? "файла"
            : "файлов";

        return $"{n} {word}";
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            Leave();
        }
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Leave();

    private void OnOpenFolderClick(object? sender, RoutedEventArgs e) => Finish(ConfirmAnswer.Yes);

    /// <summary>Stays where the export left the person: on the project it belongs to.</summary>
    private void OnReturnClick(object? sender, RoutedEventArgs e) => Leave();
}
