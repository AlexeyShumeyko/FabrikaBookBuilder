using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using PhotoBook.Application;
using PhotoBook.Infrastructure;
using PhotoBook.Core;

namespace PhotoBook.Shell.Views;

/// <summary>
/// Asks where an export goes, and then makes it.
/// </summary>
/// <remarks>
/// <para>
/// It shows what will be produced before anything is copied, because the folder is always a
/// new one and the name of that folder is the only irreversible thing on this screen.
/// </para>
/// <para>
/// Built in code, like the other windows here. A dialog is a handful of rows that change
/// once or twice, and a code-built one keeps the numbers next to what they are for.
/// </para>
/// </remarks>
public class ExportWindow : Window
{
    private readonly Project _project;
    private readonly IExportService _export;

    private string _targetFolder;
    private bool _running;
    private double _progress;
    private bool _succeeded;
    private string _exportedFolder = string.Empty;

    private readonly TextBlock _folder = new();
    private readonly TextBlock _progressText = new();
    private readonly ProgressBar _bar = new() { Maximum = 100, Minimum = 0 };
    private readonly StackPanel _progressPanel = new();
    private readonly CheckBox _subfolders = new();
    private readonly Button _exportButton;
    private readonly Button _cancelButton;

    /// <summary>The folder the export went to, once it has.</summary>
    public string ExportedFolder => _exportedFolder;

    /// <summary>Whether the export finished. A window that was closed is not a failure.</summary>
    public bool Exported => _succeeded;

    public ExportWindow(Project project, IExportService exportService, string? defaultFolder = null)
    {
        _project = project;
        _export = exportService;

        _targetFolder = defaultFolder
                        ?? ExportFolderNaming.UniqueExportFolder(
                            Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                            ExportFolderNaming.NameFor(project));

        Title = "Экспорт";
        Width = 580;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brushes.Transparent;

        // No system frame. The reference draws these as a sheet on the app's own
        // background, and a title bar with its own minimise and close buttons is a
        // window belonging to something else.
        SystemDecorations = SystemDecorations.None;
        FontFamily = new FontFamily("Inter");

        _exportButton = Button("Выгрузить", accent: true);
        _exportButton.Click += async (_, _) => await RunAsync();

        _cancelButton = Button("Отмена", accent: false);
        _cancelButton.Click += (_, _) => Close();

        Content = Build();

        KeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape) return;
            if (_running) return;
            Close();
            e.Handled = true;
        };
    }

    // ------------------------------------------------------------------ building --

    private Control Build()
    {
        _folder.Text = _targetFolder;
        _folder.FontSize = 13;
        _folder.Foreground = Ink.Text;
        _folder.TextTrimming = TextTrimming.CharacterEllipsis;
        Avalonia.Controls.ToolTip.SetTip(_folder, _targetFolder);

        _progressText.FontSize = 12;
        _progressText.FontWeight = FontWeight.SemiBold;
        _progressText.Foreground = Ink.Accent;
        _progressText.HorizontalAlignment = HorizontalAlignment.Right;

        _progressPanel.Spacing = 8;
        _progressPanel.Children.Add(new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            Children =
            {
                new TextBlock { Text = "Копирование файлов...", FontSize = 13, FontWeight = FontWeight.SemiBold },
                _progressText
            }
        });
        _progressPanel.Children.Add(_bar);
        _progressPanel.IsVisible = false;

        _subfolders.Content = "Создавать отдельную папку для каждого альбома";
        _subfolders.FontSize = 12;
        _subfolders.Margin = new Thickness(0);

        var body = new StackPanel { Margin = new Thickness(24, 20, 24, 0), Spacing = 0 };

        body.Children.Add(Box(Counts(), 16, 14));

        body.Children.Add(Caption("Имена файлов", 18, 8));
        body.Children.Add(Box(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 9,
            Children =
            {
                Glyph(0xE73E, 11, Ink.Success),
                new TextBlock { Text = "001-00.jpg, 001-01.jpg, 001-02.jpg", FontSize = 12, VerticalAlignment = VerticalAlignment.Center },
                new TextBlock
                {
                    Text = "Формат фиксирован — сайт приёма загружает файлы по нему",
                    FontSize = 10,
                    Foreground = Ink.Faint,
                    VerticalAlignment = VerticalAlignment.Center
                }
            }
        }, 14, 11));

        body.Children.Add(Caption("Папка назначения", 18, 8));
        body.Children.Add(new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            Children =
            {
                Box(_folder, 12, 10),
                BrowseButton()
            }
        });
        body.Children.Add(new TextBlock
        {
            Text = "Каждый экспорт получает свою папку — файлы прошлой выгрузки не перезаписываются.",
            FontSize = 10,
            Foreground = Ink.Faint,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(2, 6, 0, 0)
        });

        // Hidden in the combined mode: a shared file cannot live in one book's folder, and
        // that mode's naming is the whole point. Neutral rather than amber - it is an
        // option, not a warning.
        if (_project.Mode != AppMode.Combined)
        {
            var panel = new StackPanel { Spacing = 5 };
            panel.Children.Add(_subfolders);
            panel.Children.Add(new TextBlock
            {
                Text = "По умолчанию выключено: все книги складываются в одну папку. Включайте, только если загрузка на сайт это допускает.",
                FontSize = 10,
                Foreground = Ink.Faint,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(24, 0, 0, 0)
            });

            var subfoldersBox = new Border
            {
                Background = Ink.Muted,
                BorderBrush = Ink.Border,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(14, 12),
                Margin = new Thickness(0, 18, 0, 0),
                Child = panel
            };

            body.Children.Add(subfoldersBox);
        }

        body.Children.Add(_progressPanel);
        _progressPanel.Margin = new Thickness(0, 18, 0, 0);

        return new Border
        {
            Background = Ink.Surface,
            CornerRadius = new CornerRadius(16),
            BorderBrush = Ink.Border,
            BorderThickness = new Thickness(1),
            BoxShadow = new BoxShadows(BoxShadow.Parse("0 12 50 0 #40000000")),
            Child = new StackPanel
            {
                Children =
                {
                    HeaderBand(),
                    new Border { Height = 1, Background = Ink.Border },
                    body,
                    new Border { Height = 1, Background = Ink.Border, Margin = new Thickness(0, 20, 0, 0) },
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Margin = new Thickness(24, 16, 24, 20),
                        Spacing = 8,
                        Children = { _cancelButton, _exportButton }
                    }
                }
            }
        };
    }

    private Control HeaderBand() => new Border
    {
        Background = Ink.Muted,
        CornerRadius = new CornerRadius(16, 16, 0, 0),
        Padding = new Thickness(24, 18),
        Child = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
            Children =
            {
                new Border
                {
                    Width = 36,
                    Height = 36,
                    CornerRadius = new CornerRadius(10),
                    Background = Ink.AccentTint,
                    VerticalAlignment = VerticalAlignment.Center,
                    Child = Glyph(0xE8A7, 15, Ink.Accent)
                },
                new TextBlock
                {
                    Text = "Параметры экспорта",
                    FontSize = 16,
                    FontWeight = FontWeight.Bold,
                    Foreground = Ink.Heading,
                    Margin = new Thickness(12, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center
                },
                CloseButton()
            }
        }
    };

    private Control CloseButton()
    {
        var close = new Button
        {
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(8),
            Cursor = new Cursor(StandardCursorType.Hand)
        };
        close.Content = Glyph(0xE711, 13, Ink.Muted2);
        close.Click += (_, _) => { if (!_running) Close(); };
        return close;
    }

    private Control BrowseButton()
    {
        var browse = Button("Обзор", accent: false);
        browse.Margin = new Thickness(8, 0, 0, 0);
        browse.Padding = new Thickness(14, 9);
        browse.Click += async (_, _) => await BrowseAsync();
        return browse;
    }

    private Control Counts() => new Grid
    {
        ColumnDefinitions = new ColumnDefinitions("*,*"),
        Children =
        {
            Count("Книг", _project.Books?.Count ?? 0),

            // "Слотов", not "файлов": in the combined mode the produced set is smaller than
            // the slot count by design - a shared spread is one file - and two numbers with
            // the same word in them read as a contradiction.
            Count("Слотов", FilesIn(_project))
        }
    };

    private static int FilesIn(Project project) =>
        project.Books?.Sum(b =>
        {
            int n = 0;
            if (b.Cover is { IsEmpty: false }) n++;
            if (b.Pages is not null) n += b.Pages.Count(p => !p.IsCover && !p.IsEmpty);
            return n;
        }) ?? 0;

    private static Control Count(string label, int value) => new StackPanel
    {
        Children =
        {
            new TextBlock { Text = label, FontSize = 10, Foreground = Ink.Faint },
            new TextBlock
            {
                Text = value.ToString(),
                FontSize = 20,
                FontWeight = FontWeight.Bold,
                Foreground = Ink.Heading,
                Margin = new Thickness(0, 2, 0, 0)
            }
        }
    };

    // ------------------------------------------------------------------ actions --

    /// <summary>
    /// Choosing a parent does not choose the folder.
    /// </summary>
    /// <remarks>
    /// The export still gets its own new folder inside whatever was chosen. That is the whole
    /// point of the rule, and it is why this button cannot be a way to write straight over a
    /// previous run.
    /// </remarks>
    private async Task BrowseAsync()
    {
        var folders = await PickFoldersAsync();
        if (folders is not { Count: > 0 }) return;

        _targetFolder = ExportFolderNaming.UniqueExportFolder(folders[0]);
        _folder.Text = _targetFolder;
        Avalonia.Controls.ToolTip.SetTip(_folder, _targetFolder);
    }

    private async Task<List<string>?> PickFoldersAsync()
    {
        var window = Owner as Window ?? MainWindowOf(this);
        var storage = window?.StorageProvider;
        if (storage is null || !storage.CanOpen) return null;

        var picked = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Выберите папку для экспорта",
            AllowMultiple = false
        });

        return picked.Count == 0 ? null : picked.Select(f => f.TryGetLocalPath()).Where(p => p is not null).Select(p => p!).ToList();
    }

    private async Task RunAsync()
    {
        if (_running) return;

        _running = true;
        _progress = 0;
        _bar.Value = 0;
        _progressPanel.IsVisible = true;
        _progressText.Text = string.Empty;
        _exportButton.IsEnabled = false;
        _cancelButton.IsEnabled = false;

        try
        {
            var options = new ExportOptions
            {
                PerBookSubfolders = _subfolders.IsChecked ?? false,
                Progress = new Progress<double>(Report)
            };

            var result = await _export.ExportProjectAsync(_project, _targetFolder, options);

            _succeeded = result.IsSuccess;
            _exportedFolder = result.IsSuccess ? _targetFolder : string.Empty;

            if (result.IsSuccess) Close();
            else _progressText.Text = "Не удалось";
        }
        catch (Exception ex)
        {
            _succeeded = false;
            _progressText.Text = ex.Message;
        }
        finally
        {
            _running = false;
            _exportButton.IsEnabled = true;
            _cancelButton.IsEnabled = true;
        }
    }

    private void Report(double value)
    {
        _progress = value;
        _bar.Value = value;
        _progressText.Text = $"{value:0}%";
    }

    // -------------------------------------------------------------------- chrome --

    private static Window? MainWindowOf(Visual visual) =>
        (Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;

    private static Control Box(Control child, double h, double v) => new Border
    {
        Background = Ink.Muted,
        BorderBrush = Ink.Border,
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(12),
        Padding = new Thickness(h, v),
        Child = child
    };

    private static Control Caption(string text, double top, double bottom) => new TextBlock
    {
        Text = text,
        FontSize = 13,
        FontWeight = FontWeight.SemiBold,
        Foreground = Ink.Heading,
        Margin = new Thickness(0, top, 0, bottom)
    };

    private static Control Glyph(int codePoint, double size, IBrush brush) => new TextBlock
    {
        Text = ((char)codePoint).ToString(),
        FontFamily = new FontFamily("Segoe MDL2 Assets"),
        FontSize = size,
        Foreground = brush
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
        public static IBrush Faint => ConfirmWindow.Brush("#94A3B8");
        public static IBrush Heading => ConfirmWindow.Brush("#0F172A");
        public static IBrush Text => ConfirmWindow.Brush("#334155");
        public static IBrush Accent => ConfirmWindow.Brush("#4F46E5");
        public static IBrush AccentTint => ConfirmWindow.Brush("#EEF2FF");
        public static IBrush Success => ConfirmWindow.Brush("#10B981");
    }
}
