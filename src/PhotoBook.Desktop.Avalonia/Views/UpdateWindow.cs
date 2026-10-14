using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using PhotoBook.Application;
using PhotoBook.Shell.ViewModels;

namespace PhotoBook.Shell.Views;

/// <summary>
/// Says there is a new version, and gets it if the answer is yes.
/// </summary>
/// <remarks>
/// The same chrome as every other window here: a sheet on the program's own background, no
/// system frame. The versions are one line, the current one quiet and the new one the point;
/// the release text sits in a frame of its own and scrolls, because it is longer than the
/// window and shortening it to fit would be deciding what matters for the person reading.
/// </remarks>
public class UpdateWindow : Window
{
    private readonly UpdateViewModel _model;
    private readonly StackPanel _notes = new();
    private readonly StackPanel _progressPanel = new();
    private readonly StackPanel _buttons = new();

    public UpdateWindow(UpdateViewModel model)
    {
        _model = model;
        DataContext = model;

        Title = "Доступно обновление";
        Width = 560;
        Height = 640;
        MaxHeight = 760;
        MinWidth = 480;
        MinHeight = 440;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = Brushes.Transparent;
        FontFamily = new FontFamily("Inter");

        // No system frame. See the note on the help window.
        SystemDecorations = SystemDecorations.None;

        Content = Build();
        FillNotes();

        // The progress and the buttons take turns, and which one is showing is a fact
        // about the download rather than about this window.
        _progressPanel.Bind(IsVisibleProperty, new Binding(nameof(UpdateViewModel.IsDownloading)));
        _buttons.Bind(IsVisibleProperty, new Binding("!IsDownloading"));

        model.PostponeRequested += (_, _) => Close();
        model.Failed += (_, message) => new NotifyWindow("Ок", message, destructive: true)
            .ShowDialog(this);
        model.InstallerStarted += (_, _) => EndSession();

        KeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape) return;
            Close();
            e.Handled = true;
        };
    }

    /// <summary>
    /// Ends the program once the installer has taken over.
    /// </summary>
    /// <remarks>
    /// The installer replaces the files this program is running from, so carrying on would
    /// mean writing into a directory that is being rewritten underneath. Leaving is the
    /// shell's decision - the feed knows where the update came from and nothing about this
    /// window.
    /// </remarks>
    private static void EndSession()
    {
        if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime lifetime)
            lifetime.Shutdown();
    }

    private Control Build()
    {
        _notes.Spacing = 4;

        // Bound with Bind rather than written in the initialiser: a binding is not a value,
        // and "Text = <binding>" is not a sentence this language will accept.
        var progressCaption = new TextBlock
        {
            Text = "Загрузка обновления",
            FontSize = 12,
            Foreground = Ink.Muted2
        };
        progressCaption.Bind(TextBlock.TextProperty, new Binding(nameof(UpdateViewModel.DownloadProgressText)));

        var progressValue = new ProgressBar { Maximum = 100, Minimum = 0 };
        progressValue.Bind(ProgressBar.ValueProperty, new Binding(nameof(UpdateViewModel.DownloadProgress)));

        _progressPanel.Spacing = 6;
        _progressPanel.Children.Add(new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*"),
            Children = { progressCaption }
        });
        _progressPanel.Children.Add(progressValue);

        var postpone = Button("Отложить", accent: false);
        postpone.Click += (_, _) => _model.PostponeCommand.Execute(null);

        var update = Button("Обновить", accent: true);
        update.Click += async (_, _) => await _model.UpdateCommand.ExecuteAsync(null);

        _buttons.Orientation = Orientation.Horizontal;
        _buttons.Spacing = 8;
        _buttons.Children.Add(postpone);
        _buttons.Children.Add(update);

        var body = new Grid { Margin = new Thickness(24, 22, 24, 20) };
        body.RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto");

        AddRow(body, 0, Header());
        AddRow(body, 1, Versions());
        AddRow(body, 2, NotesFrame());
        AddRow(body, 3, Footer());

        return new Border
        {
            Background = Ink.Surface,
            CornerRadius = new CornerRadius(16),
            BorderBrush = Ink.Border,
            BorderThickness = new Thickness(1),
            BoxShadow = new BoxShadows(BoxShadow.Parse("0 12 50 0 #40000000")),
            Child = body
        };
    }

    private static void AddRow(Grid grid, int row, Control child)
    {
        Grid.SetRow(child, row);
        grid.Children.Add(child);
    }

    private static IBinding TextBinding() =>
        new Avalonia.Data.Binding(nameof(UpdateViewModel.DownloadProgressText));

    private static IBinding ValueBinding() =>
        new Avalonia.Data.Binding(nameof(UpdateViewModel.DownloadProgress));

    private Control Header()
    {
        var tile = new Border
        {
            Width = 36,
            Height = 36,
            CornerRadius = new CornerRadius(10),
            Background = Ink.AccentTint,
            VerticalAlignment = VerticalAlignment.Center,
            Child = Glyph(0xE8A7, 15, Ink.Accent)
        };

        var title = new TextBlock
        {
            Text = "Доступно обновление",
            FontSize = 16,
            FontWeight = FontWeight.Bold,
            Foreground = Ink.Heading,
            VerticalAlignment = VerticalAlignment.Center
        };

        Grid.SetColumn(tile, 0);
        Grid.SetColumn(title, 1);

        return new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*"),
            Margin = new Thickness(0, 0, 0, 14),
            Children = { tile, title }
        };
    }

    /// <summary>One line: the current version quiet, the new one the point.</summary>
    private Control Versions()
    {
        var current = new TextBlock
        {
            FontSize = 12,
            Foreground = Ink.Muted2,
            VerticalAlignment = VerticalAlignment.Center
        };
        current.Bind(TextBlock.TextProperty, new Binding(nameof(UpdateViewModel.CurrentVersionText)));

        var arrow = new TextBlock
        {
            Text = "→",
            FontSize = 12,
            Foreground = Ink.Faint,
            VerticalAlignment = VerticalAlignment.Center
        };

        var latest = new TextBlock
        {
            FontSize = 13,
            FontWeight = FontWeight.SemiBold,
            Foreground = Ink.Accent,
            VerticalAlignment = VerticalAlignment.Center
        };
        latest.Bind(TextBlock.TextProperty, new Binding(nameof(UpdateViewModel.LatestVersionText)));

        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            Children = { current, arrow, latest }
        };

        return Frame(row, new Thickness(12, 10), new Thickness(0, 0, 0, 12));
    }

    private Control NotesFrame()
    {
        var scroller = new ScrollViewer
        {
            Content = _notes,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };

        return Frame(scroller, new Thickness(14), new Thickness(0));
    }

    private Control Footer()
    {
        var stack = new StackPanel { Spacing = 12 };
        stack.Children.Add(_progressPanel);
        stack.Children.Add(_buttons);

        return stack;
    }

    /// <summary>
    /// Lays the notes out from the parsed lines.
    /// </summary>
    /// <remarks>
    /// Called once, at construction, because the text never changes while the window is
    /// open. A heading is heavier and darker, a bullet is marked, and everything else is a
    /// sentence - which is the whole of the classification, and the window's only job is to
    /// draw it.
    /// </remarks>
    private void FillNotes()
    {
        foreach (var line in _model.Notes)
        {
            _notes.Children.Add(new TextBlock
            {
                Text = line.Kind == ReleaseNoteKind.Bullet ? "•  " + line.Text : line.Text,
                FontSize = line.Kind == ReleaseNoteKind.Heading ? (line.Level == 1 ? 16 : 13) : 12.5,
                FontWeight = line.Kind == ReleaseNoteKind.Heading ? FontWeight.Bold : FontWeight.Normal,
                Foreground = line.Kind == ReleaseNoteKind.Heading ? Ink.Heading : Ink.Text,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(line.Kind == ReleaseNoteKind.Heading ? 0 : 0, 2, 0, 2)
            });
        }
    }

    private static Control Frame(Control child, Thickness padding, Thickness margin) => new Border
    {
        Background = Ink.Muted,
        BorderBrush = Ink.FaintBorder,
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(12),
        Padding = padding,
        Margin = margin,
        Child = child
    };

    private static Control Glyph(int codePoint, double size, IBrush brush) => new TextBlock
    {
        Text = ((char)codePoint).ToString(),
        FontFamily = new FontFamily("Segoe MDL2 Assets"),
        FontSize = size,
        Foreground = brush,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center
    };

    private static Button Button(string text, bool accent) => new()
    {
        Content = text,
        Padding = new Thickness(16, 9),
        FontSize = 12,
        FontWeight = FontWeight.SemiBold,
        CornerRadius = new CornerRadius(12),
        Background = accent ? Ink.Accent : Ink.Surface,
        Foreground = accent ? Brushes.White : Ink.Text,
        BorderBrush = accent ? Brushes.Transparent : Ink.Border,
        BorderThickness = new Thickness(1),
        Cursor = new Cursor(StandardCursorType.Hand)
    };

    private static class Ink
    {
        public static IBrush Surface => ConfirmWindow.Brush("#FFFFFF");
        public static IBrush Muted => ConfirmWindow.Brush("#F8FAFC");
        public static IBrush Muted2 => ConfirmWindow.Brush("#64748B");
        public static IBrush Border => ConfirmWindow.Brush("#E2E8F0");
        public static IBrush FaintBorder => ConfirmWindow.Brush("#E2E8F0");
        public static IBrush Faint => ConfirmWindow.Brush("#94A3B8");
        public static IBrush Heading => ConfirmWindow.Brush("#0F172A");
        public static IBrush Text => ConfirmWindow.Brush("#334155");
        public static IBrush Accent => ConfirmWindow.Brush("#4F46E5");
        public static IBrush AccentTint => ConfirmWindow.Brush("#EEF2FF");
    }
}
