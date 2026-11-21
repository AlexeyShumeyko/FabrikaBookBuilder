using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using PhotoBook.Infrastructure;

namespace PhotoBook.Shell.Views;

/// <summary>
/// The guide's page: the cover, the spine and the seven chapters.
/// </summary>
/// <remarks>
/// <para>
/// The chapters are one page, not a set of tabs: a manual is read in order and found by
/// scrolling back, and a tab per chapter turns one page into seven that each have to be
/// opened separately.
/// </para>
/// <para>
/// Three things are done here rather than in markup, because none of them exists as a
/// property. The spine follows the page down, which is `position: sticky` in the prototype.
/// The current chapter is the last one whose head has passed the top of the reading area.
/// And the chapter numbers in the margin are outlines, which this engine cannot draw with
/// a TextBlock - so they are built from the typeface as geometry.
/// </para>
/// </remarks>
public partial class HelpView : UserControl
{
    /// <summary>Where the spine stops as the page scrolls under it.</summary>
    /// <remarks>
    /// The prototype pins it at ninety-eight from the top of the window: seventy-six of
    /// header and twenty-two of air. The reading area here starts under the header, so the
    /// twenty-two is what is left.
    /// </remarks>
    private const double StickyTop = 20;

    /// <summary>How far down a chapter's head has to be before the spine calls it current.</summary>
    /// <remarks>
    /// The prototype's own threshold is a hundred and thirty-two from the window's top;
    /// minus the header, that is fifty-six below the first pixel of the page.
    /// </remarks>
    private const double ActiveThreshold = 56;

    private const int StepCount = 5;

    private int _step;

    public HelpView()
    {
        InitializeComponent();

        JournalPath.Text = Diagnostics.JournalPath;

        BuildChapterNumerals();
        ShowStep(0);
        SetActiveSpine(0);
    }

    /// <summary>Raised when the reader asks for the support page.</summary>
    public event EventHandler? SupportRequested;

    private void OnSupportClick(object? sender, RoutedEventArgs e)
        => SupportRequested?.Invoke(this, EventArgs.Empty);

    // ------------------------------------------------------------------ the spine

    private void OnSpineClick(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.Tag is not string tag || !int.TryParse(tag, out int number))
            return;

        if (number < 1 || number > Chapters.Children.Count)
            return;

        if (Chapters.Children[number - 1] is not Control chapter)
            return;

        // The chapter is put where the spine's own top sits, so the head it was asked for
        // appears in the same place a scrolled-to chapter does.
        PageScroll.Offset = new Vector(
            0,
            Math.Max(0, PageScroll.Offset.Y + ViewportTop(chapter) - StickyTop));

        SetActiveSpine(number - 1);
    }

    private void OnPageScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        StickSpine();
        SyncActiveChapter();
    }

    /// <summary>
    /// Keeps the spine beside the chapters as they scroll.
    /// </summary>
    /// <remarks>
    /// A render transform moves the spine without taking it out of its column or changing
    /// the page's layout, which is exactly what sticky positioning is. The travel stops at
    /// the foot of the chapters: a spine that carried on past the end of its own column
    /// would slide over the page's margin.
    /// </remarks>
    private void StickSpine()
    {
        if (Spine.RenderTransform is not TranslateTransform transform)
        {
            transform = new TranslateTransform();
            Spine.RenderTransform = transform;
        }

        double travel = Math.Max(0, Chapters.Bounds.Height - Spine.Bounds.Height);

        transform.Y = Math.Clamp(StickyTop - ShellTop, 0, travel);
    }

    private void SyncActiveChapter()
    {
        int current = 0;

        for (int i = 0; i < Chapters.Children.Count; i++)
        {
            if (Chapters.Children[i] is not Control chapter)
                continue;

            if (ViewportTop(chapter) <= ActiveThreshold)
                current = i;
        }

        SetActiveSpine(current);
    }

    /// <summary>
    /// Where a part of the page sits now, measured down from the top of the reading area.
    /// </summary>
    /// <remarks>
    /// The page's own bounds already carry the scroll here - the scroll presenter moves the
    /// content rather than offsetting what is drawn - so adding the offset back would count
    /// it twice. The chapter's bounds are measured from the shell, because the chapters and
    /// the spine start on the same line.
    /// </remarks>
    private double ViewportTop(Control part) => ShellTop + part.Bounds.Y;

    /// <summary>Where the columns of the guide begin, down from the top of the reading area.</summary>
    private double ShellTop => PageContent.Bounds.Y + ManualShell.Bounds.Y;

    private void SetActiveSpine(int index)
    {
        var rows = Spine.Children.OfType<Button>().ToList();

        for (int i = 0; i < rows.Count; i++)
            rows[i].Classes.Set("active", i == index);
    }

    // --------------------------------------------------------------- the training

    private void OnTrainTabClick(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.Tag is string tag && int.TryParse(tag, out int step))
            ShowStep(step);
    }

    private void OnTrainPrev(object? sender, RoutedEventArgs e) => ShowStep(_step - 1);

    private void OnTrainNext(object? sender, RoutedEventArgs e)
        => ShowStep(_step == StepCount - 1 ? 0 : _step + 1);

    /// <summary>
    /// Puts one step of the training on screen.
    /// </summary>
    /// <remarks>
    /// The last step's forward button offers to start again rather than to go on, which is
    /// what the prototype does: a fifth step followed by a dead arrow would be a page that
    /// does not finish.
    /// </remarks>
    private void ShowStep(int step)
    {
        _step = Math.Clamp(step, 0, StepCount - 1);

        for (int i = 0; i < StepCount; i++)
        {
            if (this.FindControl<Button>("TrainTab" + (i + 1)) is { } tab)
            {
                tab.Classes.Set("on", i == _step);
                tab.Classes.Set("done", i < _step);
            }

            if (this.FindControl<StackPanel>("TrainPanel" + (i + 1)) is { } panel)
                panel.IsVisible = i == _step;
        }

        TrainPosition.Text = (_step + 1).ToString(CultureInfo.InvariantCulture);
        TrainPrev.IsEnabled = _step > 0;

        bool last = _step == StepCount - 1;
        TrainNextLabel.Text = last ? "Ещё раз" : "Дальше";
        TrainNextArrow.IsVisible = !last;
        TrainAgainIcon.IsVisible = last;
    }

    // ---------------------------------------------------------------- the numerals

    /// <summary>
    /// Draws the chapter numbers that sit in the margin of the first five chapters.
    /// </summary>
    /// <remarks>
    /// The prototype draws them as one-pixel outlines of a hundred-and-eight-point figure,
    /// at a seventh of the gold. Avalonia has no stroke on text, so the figure is built from
    /// the typeface itself and handed to a Path, which has one. The last two chapters carry
    /// no numeral in the prototype, and none here.
    /// </remarks>
    private void BuildChapterNumerals()
    {
        var typeface = new Typeface(
            new FontFamily("avares://PhotoBookRenamer/Assets/Fonts#Manrope"),
            FontStyle.Normal,
            FontWeight.ExtraBold);

        foreach (var (path, number) in new[]
                 {
                     (Ghost1, "01"),
                     (Ghost2, "02"),
                     (Ghost3, "03"),
                     (Ghost4, "04"),
                     (Ghost5, "05")
                 })
        {
            var text = new FormattedText(
                number,
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                typeface,
                108,
                Brushes.Transparent);

            path.Data = text.BuildGeometry(new Point(0, 0));
        }
    }

    // ----------------------------------------------------------------- the journal

    private void OnOpenJournalFolder(object? sender, RoutedEventArgs e)
    {
        try
        {
            var folder = Path.GetDirectoryName(Diagnostics.JournalPath);
            if (string.IsNullOrEmpty(folder)) return;

            Directory.CreateDirectory(folder);

            Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            // The path is on screen right above the button: nothing to explain that it does not
            // already say, and a dialog about a folder would be louder than the problem.
        }
    }
}
