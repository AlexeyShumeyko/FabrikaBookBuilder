using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using PhotoBook.Application;

namespace PhotoBook.Shell.Views;

/// <summary>
/// Picks the folders a project's photographs come from.
/// </summary>
/// <remarks>
/// <para>
/// The platform's own folder picker was doing this job and is gone. It could pick folders and
/// nothing else: it could not show what was already chosen, it could not say how many
/// photographs a folder holds before committing to it, and it answered a question about a
/// photographer's work with a file list.
/// </para>
/// <para>
/// What changed from the reference's browser, on purpose:
/// </para>
/// <list type="bullet">
/// <item><b>A tick per folder, not a hidden modifier.</b> The reference asks for Ctrl and Shift
/// to choose more than one, and opens a message box when you did not. A row that is either in
/// or out says the same thing without being told.</item>
/// <item><b>Adding is the tick.</b> There is no separate step between choosing and having
/// chosen, and nothing to confirm before the chosen list means something.</item>
/// <item><b>An empty folder is marked before it is refused.</b> A folder with no photographs in
/// it can never be accepted, and the reference only discovered that after you had committed to
/// it. The count is on the row for that reason.</item>
/// </list>
/// <para>
/// Reading folders happens off the drawing thread. The reference did it on that thread, which is
/// why opening it on a folder of scan files stopped answering for a moment.
/// </para>
/// </remarks>
public class FolderBrowserWindow : Window
{
    private readonly IFileService _files;

    private readonly List<string> _selected = new();
    private readonly ObservableCollection<ChosenFolder> _chosen = new();
    private readonly Stack<string> _history = new();
    private readonly List<FolderEntry> _folders = new();

    private string _currentPath = string.Empty;
    private bool _validating;
    private bool _hasError;
    private DateTime _lastRowClick = DateTime.MinValue;
    private string _lastRowPath = string.Empty;

    private readonly StackPanel _placesBox = new() { Spacing = 2 };
    private readonly StackPanel _foldersBox = new() { Spacing = 4 };
    private readonly StackPanel _chosenBox = new() { Spacing = 4 };
    private readonly TextBlock _pathText = new();
    private readonly TextBlock _chosenCount = new();
    private readonly TextBlock _foundCaption = new();
    private readonly TextBlock _errorText = new();
    private readonly TextBox _filter = new();
    private readonly Button _back = new();
    private readonly Button _ok = new();
    private Border _errorBanner = new();

    public FolderBrowserWindow(IFileService fileService, List<string>? existingFolders = null)
    {
        _files = fileService;

        Title = "Проводник папок";
        Width = 1020;
        Height = 660;
        MinWidth = 900;
        MinHeight = 520;
        CanResize = true;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brushes.Transparent;
        SystemDecorations = SystemDecorations.None;
        FontFamily = new FontFamily("Inter");

        Content = Build();

        KeyDown += OnKeyDown;

        if (existingFolders is not null)
        {
            foreach (var folder in existingFolders.Where(f => !string.IsNullOrWhiteSpace(f)))
            {
                _selected.Add(folder);
                _chosen.Add(new ChosenFolder(folder));
            }
        }

        _ok.Content = "Загрузить структуру";
        _ok.Padding = new Thickness(16, 10);
        _ok.FontSize = 13;
        _ok.FontWeight = FontWeight.SemiBold;
        _ok.CornerRadius = new CornerRadius(12);
        _ok.Background = Ink.Accent;
        _ok.Foreground = Brushes.White;
        _ok.BorderThickness = new Thickness(0);
        _ok.Cursor = new Cursor(StandardCursorType.Hand);
        _ok.Click += async (_, _) => await ConfirmAsync();

        RefreshChosen();

        // Opens where the last selection was: a photographer's photographs are not on the
        // Desktop, and the reference always opened on the Desktop whatever was already chosen.
        var start = FirstExisting(_selected) ?? Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        _ = NavigateAsync(start, remember: false);

        if (_chosen.Count > 0) _ = RevalidateAsync();
    }

    // ------------------------------------------------------------------ the window --

    private Control Build()
    {
        _errorBanner = ErrorBanner();

        // The body is the only part that grows. A panel in a stack is measured with infinite
        // height, so a "*" row inside one asks for everything and the window becomes as tall
        // as the file system - which is what the first version of this did, and it is why the
        // header had scrolled off the top.
        var body = new Grid
        {
            Margin = new Thickness(20, 18, 20, 0),
            ColumnDefinitions = new ColumnDefinitions("196,12,*,12,256"),
            RowDefinitions = new RowDefinitions("Auto,*")
        };
        Grid.SetColumnSpan(_errorBanner, 5);
        body.Children.Add(_errorBanner);

        body.Children.Add(PlacesPanel());
        Grid.SetRow(body.Children[1], 1);

        body.Children.Add(FolderColumn());
        Grid.SetColumn(body.Children[2], 2);
        Grid.SetRow(body.Children[2], 1);

        body.Children.Add(ChosenPanel());
        Grid.SetColumn(body.Children[3], 4);
        Grid.SetRow(body.Children[3], 1);

        _foundCaption.FontSize = 12;
        _foundCaption.Foreground = Ink.Text2;
        _foundCaption.VerticalAlignment = VerticalAlignment.Center;

        var footRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        footRow.Children.Add(_foundCaption);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children =
            {
                PlainButton("Отмена", accent: false, onClick: () => Close(null)),
                _ok
            }
        };
        Grid.SetColumn(buttons, 1);
        footRow.Children.Add(buttons);

        // Every row is set by hand. A grid puts anything without a row in the first one, and a
        // header laid out on top of a footer looks like a window that came apart rather than
        // like a mistake in a row number.
        var root = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto") };

        var header = HeaderBand();
        Grid.SetRow(header, 0);
        root.Children.Add(header);

        var topRule = new Border { Height = 1, Background = Ink.Border };
        Grid.SetRow(topRule, 1);
        root.Children.Add(topRule);

        Grid.SetRow(body, 2);
        root.Children.Add(body);

        var footer = new StackPanel
        {
            Children =
            {
                new Border { Height = 1, Background = Ink.Border, Margin = new Thickness(0, 18, 0, 0) },
                new StackPanel { Margin = new Thickness(20, 14, 20, 18), Children = { footRow } }
            }
        };
        Grid.SetRow(footer, 3);
        root.Children.Add(footer);

        return new Border
        {
            Background = Ink.Surface,
            CornerRadius = new CornerRadius(16),
            BorderBrush = Ink.Border,
            BorderThickness = new Thickness(1),
            BoxShadow = new BoxShadows(BoxShadow.Parse("0 12 50 0 #40000000")),
            Child = root
        };
    }

    private Control HeaderBand()
    {
        var title = new TextBlock
        {
            Text = "Проводник папок проекта",
            FontSize = 16,
            FontWeight = FontWeight.Bold,
            Foreground = Ink.Heading,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(12, 0, 0, 0)
        };

        var close = new Button
        {
            Content = Glyph((char)0xE711, 13, Ink.Text2),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(8),
            Cursor = new Cursor(StandardCursorType.Hand)
        };
        close.Click += (_, _) => Close(null);

        var grid = new Grid
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
                    Child = Icon.Stroke(IconGeometry.Folder, 16, Ink.Accent)
                }
            }
        };
        Grid.SetColumn(title, 1);
        grid.Children.Add(title);
        Grid.SetColumn(close, 2);
        grid.Children.Add(close);

        return new Border
        {
            Background = Ink.Muted,
            CornerRadius = new CornerRadius(16, 16, 0, 0),
            Padding = new Thickness(24, 18),
            Child = grid
        };
    }

    // ------------------------------------------------------------------- the parts --

    private Control PlacesPanel()
    {
        _placesBox.Children.Clear();

        foreach (var (name, path) in Places())
        {
            bool exists = Directory.Exists(path);

            var icon = Icon.Stroke(IconGeometry.Drive, 13, Ink.Warn);
            icon.Opacity = exists ? 1 : 0.35;

            var label = new TextBlock
            {
                Text = name,
                FontSize = 12,
                Foreground = exists ? Ink.Text : Ink.Faint,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center
            };

            var row = new Button
            {
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(10, 7),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                IsEnabled = exists,
                Cursor = new Cursor(StandardCursorType.Hand),
                Content = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8,
                    VerticalAlignment = VerticalAlignment.Center,
                    Children = { icon, label }
                }
            };

            if (exists) row.Click += (_, _) => Navigate(path);
            _placesBox.Children.Add(row);
        }

        return Box(new StackPanel
        {
            Spacing = 4,
            Children = { Caption("Места", 6), _placesBox }
        }, 10, 10);
    }

    private Control FolderColumn()
    {
        _back.Content = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 7,
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                Icon.Stroke(IconGeometry.ArrowLeft, 13, Ink.Text),
                new TextBlock
                {
                    Text = "Назад",
                    FontSize = 13,
                    FontWeight = FontWeight.SemiBold,
                    VerticalAlignment = VerticalAlignment.Center
                }
            }
        };
        _back.Padding = new Thickness(12, 9);
        _back.FontSize = 13;
        _back.FontWeight = FontWeight.SemiBold;
        _back.CornerRadius = new CornerRadius(12);
        _back.Background = Ink.Surface;
        _back.Foreground = Ink.Text;
        _back.BorderBrush = Ink.Border;
        _back.BorderThickness = new Thickness(1);
        _back.Cursor = new Cursor(StandardCursorType.Hand);
        _back.Click += (_, _) => GoBack();

        _pathText.FontFamily = new FontFamily("Consolas");
        _pathText.FontSize = 12;
        _pathText.Foreground = Ink.Text2;
        _pathText.TextTrimming = TextTrimming.CharacterEllipsis;
        _pathText.VerticalAlignment = VerticalAlignment.Center;
        _pathText.Margin = new Thickness(16, 0, 16, 0);

        _filter.Watermark = "Поиск по имени";
        _filter.FontSize = 12;
        _filter.Width = 240;
        _filter.HorizontalAlignment = HorizontalAlignment.Left;
        _filter.CornerRadius = new CornerRadius(10);
        _filter.Background = Ink.Surface;
        _filter.BorderBrush = Ink.Border;
        _filter.BorderThickness = new Thickness(1);
        _filter.Padding = new Thickness(10, 7);
        _filter.TextChanged += (_, _) => BuildFolderRows();

        // Two columns only. With a third one holding the search box the path and the button
        // fought over the middle and the path came and went - drawn once in the right place
        // and once, faint, on top of the button, and after opening a folder not drawn at all.
        // The search sits on its own line under them, which is where it is easier to reach
        // anyway.
        var bar = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        bar.Children.Add(_back);
        Grid.SetColumn(_pathText, 1);
        bar.Children.Add(_pathText);

        var scroll = new ScrollViewer
        {
            Content = _foldersBox,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };

        var search = new StackPanel { Margin = new Thickness(0, 10, 0, 0), Children = { _filter } };

        var column = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,Auto,*"),
            Margin = new Thickness(0, 12, 0, 0)
        };
        column.Children.Add(bar);
        Grid.SetRow(search, 1);
        column.Children.Add(search);
        Grid.SetRow(scroll, 2);
        column.Children.Add(scroll);

        return column;
    }

    private Control ChosenPanel()
    {
        _chosenCount.FontSize = 12;
        _chosenCount.FontWeight = FontWeight.Bold;
        _chosenCount.Foreground = Ink.Accent;

        var heading = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        heading.Children.Add(Caption("Выбрано папок", 0));
        Grid.SetColumn(_chosenCount, 1);
        heading.Children.Add(_chosenCount);

        var scroll = new ScrollViewer
        {
            Content = _chosenBox,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Margin = new Thickness(0, 8, 0, 0)
        };
        Grid.SetRow(scroll, 1);

        return Box(new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*"),
            Children = { heading, scroll }
        }, 12, 12);
    }

    private Border ErrorBanner()
    {
        _errorText.FontSize = 12;
        _errorText.Foreground = Ink.DangerInk;
        _errorText.TextWrapping = TextWrapping.Wrap;
        _errorText.LineHeight = 18;
        _errorText.Margin = new Thickness(10, 0, 0, 0);

        return new Border
        {
            Background = Ink.Danger,
            BorderBrush = Ink.DangerLine,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(14, 12),
            Margin = new Thickness(20, 16, 20, 0),
            IsVisible = false,
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Children = { Icon.Stroke(IconGeometry.Warning, 14, Ink.DangerMark), _errorText }
            }
        };
    }

    // --------------------------------------------------------------- the behaviour --

    /// <summary>The places worth offering first, and the fixed drives behind them.</summary>
    private static IEnumerable<(string Name, string Path)> Places()
    {
        yield return ("Рабочий стол", Environment.GetFolderPath(Environment.SpecialFolder.Desktop));
        yield return ("Документы", Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
        yield return ("Изображения", Environment.GetFolderPath(Environment.SpecialFolder.MyPictures));
        yield return ("Загрузки", System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"));

        foreach (var drive in DriveInfo.GetDrives()
                     .Where(d => d.IsReady && d.DriveType == DriveType.Fixed)
                     .OrderBy(d => d.Name))
        {
            // A disk with no name of its own is just its letter: "C: ()" is what the reference
            // offered, and the empty brackets are noise in a list a person scans.
            string label = drive.VolumeLabel;
            yield return (string.IsNullOrWhiteSpace(label) ? drive.Name : $"{drive.Name} ({label})",
                drive.RootDirectory.FullName);
        }
    }

    /// <summary>
    /// Reads a folder's subfolders off the drawing thread.
    /// </summary>
    /// <remarks>
    /// The count of photographs comes with the listing rather than on demand. It is the one
    /// number that decides whether a folder can be used at all, and asking for it later means
    /// asking for it again after every change.
    /// </remarks>
    private async Task NavigateAsync(string path, bool remember)
    {
        if (string.IsNullOrEmpty(path)) return;

        var listing = await Task.Run(() => ReadFolders(path));

        if (listing is null)
        {
            _currentPath = string.Empty;
            _pathText.Text = string.Empty;
            _folders.Clear();
            BuildFolderRows();
            return;
        }

        if (remember && !string.IsNullOrEmpty(_currentPath) && _currentPath != path) _history.Push(_currentPath);

        _currentPath = path;
        _pathText.Text = path;
        _back.IsEnabled = _history.Count > 0;
        _filter.Text = string.Empty;

        _folders.Clear();
        _folders.AddRange(listing);

        BuildFolderRows();
    }

    private void Navigate(string path) => _ = NavigateAsync(path, remember: true);

    /// <summary>The subfolders of a path, or null when it cannot be read at all.</summary>
    private static List<FolderEntry>? ReadFolders(string path)
    {
        if (!Directory.Exists(path)) return null;

        var result = new List<FolderEntry>();

        try
        {
            foreach (var dir in Directory.GetDirectories(path))
            {
                // A folder that cannot be opened is left out rather than shown and then
                // refused: it would be a row that does nothing.
                try
                {
                    Directory.GetDirectories(dir);
                }
                catch
                {
                    continue;
                }

                result.Add(new FolderEntry(dir, CountJpegFiles(dir)));
            }
        }
        catch (UnauthorizedAccessException)
        {
            return result;
        }
        catch
        {
            return result;
        }

        result.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase));
        return result;
    }

    private static int CountJpegFiles(string folder)
    {
        try
        {
            return Directory.GetFiles(folder).Count(f =>
                f.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
                f.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
            return 0;
        }
    }

    private void GoBack()
    {
        if (_history.Count > 0) _ = NavigateAsync(_history.Pop(), remember: false);
    }

    private void BuildFolderRows()
    {
        _foldersBox.Children.Clear();
        _foundCaption.Text = $"Найдено подпапок: {_folders.Count}";

        var needle = _filter.Text?.Trim() ?? string.Empty;
        int shown = 0;

        foreach (var folder in _folders)
        {
            if (needle.Length > 0 &&
                folder.Name.IndexOf(needle, StringComparison.CurrentCultureIgnoreCase) < 0) continue;

            _foldersBox.Children.Add(FolderRow(folder));
            shown++;
        }

        if (shown == 0)
        {
            _foldersBox.Children.Add(new TextBlock
            {
                Text = needle.Length > 0 ? "Ничего не найдено" : "В этой папке нет подпапок",
                FontSize = 12,
                Foreground = Ink.Faint,
                Margin = new Thickness(12, 10, 0, 0)
            });
        }
    }

    private Control FolderRow(FolderEntry folder)
    {
        bool chosen = _selected.Contains(folder.FullPath);

        var tick = new Border
        {
            Width = 18,
            Height = 18,
            CornerRadius = new CornerRadius(5),
            Background = chosen ? Ink.Accent : Brushes.White,
            BorderBrush = chosen ? Ink.Accent : Ink.BorderStrong,
            BorderThickness = new Thickness(1),
            VerticalAlignment = VerticalAlignment.Center,
            Child = chosen ? Icon.Stroke(IconGeometry.Tick, 11, Brushes.White) : null
        };

        var row = new Button
        {
            Tag = folder.FullPath,
            Background = chosen ? Ink.AccentTint : Brushes.Transparent,
            BorderBrush = chosen ? Ink.AccentLine : Brushes.Transparent,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(12, 9),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Cursor = new Cursor(StandardCursorType.Hand),
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 10,
                VerticalAlignment = VerticalAlignment.Center,
                Children =
                {
                    tick,
                    Icon.Stroke(IconGeometry.Folder, 14, Ink.Warn),
                    new TextBlock
                    {
                        Text = folder.Name,
                        FontSize = 12,
                        FontWeight = FontWeight.SemiBold,
                        Foreground = Ink.Text,
                        TextTrimming = TextTrimming.CharacterEllipsis,
                        VerticalAlignment = VerticalAlignment.Center
                    },
                    new TextBlock
                    {
                        FontSize = 10,
                        Foreground = folder.Files == 0 ? Ink.DangerMark : Ink.Faint,
                        VerticalAlignment = VerticalAlignment.Center,
                        Text = folder.Files == 0 ? "нет фото" : Files(folder.Files)
                    }
                }
            }
        };

        if (folder.Files == 0)
            ToolTip.SetTip(row, "В папке нет JPG или JPEG — для книги она не подойдёт");

        row.Click += (_, _) =>
        {
            var now = DateTime.UtcNow;
            bool twice = _lastRowPath == folder.FullPath && (now - _lastRowClick).TotalMilliseconds < 350;
            _lastRowClick = now;
            _lastRowPath = folder.FullPath;

            // One click takes the folder or lets it go; two in a row open it. The reference
            // asked for a modifier key to choose more than one and never opened a folder at
            // all without one, which made both actions cost something nobody could see.
            if (twice) Navigate(folder.FullPath);
            else Toggle(folder);
        };

        return row;
    }

    private void Toggle(FolderEntry folder)
    {
        if (_selected.Remove(folder.FullPath))
        {
            var entry = _chosen.FirstOrDefault(c =>
                string.Equals(c.Path, folder.FullPath, StringComparison.OrdinalIgnoreCase));
            if (entry is not null) _chosen.Remove(entry);
        }
        else
        {
            _selected.Add(folder.FullPath);
            _chosen.Add(new ChosenFolder(folder.FullPath));
        }

        RefreshChosen();
        BuildFolderRows();
        _ = RevalidateAsync();
    }

    private void RefreshChosen()
    {
        _chosenBox.Children.Clear();
        _chosenCount.Text = _chosen.Count.ToString();

        foreach (var entry in _chosen)
        {
            // The path takes the row and gives up first; the mark sits at the end of it and stays.
            // In a panel the two size themselves and the long path pushes the mark off the
            // edge, which is how a row ended up with no way to take anything out of it.
            var line = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };

            var path = new TextBlock
            {
                Text = entry.Path,
                FontSize = 11,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Foreground = entry.HasError ? Ink.DangerStrong : Ink.Text2,
                FontWeight = entry.HasError ? FontWeight.SemiBold : FontWeight.Normal,
                VerticalAlignment = VerticalAlignment.Center
            };
            line.Children.Add(path);

            var mark = entry.HasError
                ? Icon.Stroke(IconGeometry.Warning, 11, Ink.DangerMark)
                : Icon.Stroke(IconGeometry.Close, 11, Ink.Faint);
            mark.Margin = new Thickness(8, 0, 0, 0);
            Grid.SetColumn(mark, 1);
            line.Children.Add(mark);

            var row = new Button
            {
                Background = entry.HasError ? Ink.Danger : Brushes.Transparent,
                BorderThickness = new Thickness(0),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(8, 6),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Cursor = new Cursor(StandardCursorType.Hand),
                Content = line
            };

            ToolTip.SetTip(row, entry.HasError ? entry.ErrorMessage ?? string.Empty : "Убрать из списка");

            row.Click += (_, _) =>
            {
                _selected.RemoveAll(p => string.Equals(p, entry.Path, StringComparison.OrdinalIgnoreCase));
                _chosen.Remove(entry);
                RefreshChosen();
                BuildFolderRows();
                _ = RevalidateAsync();
            };

            _chosenBox.Children.Add(row);
        }

        if (_chosen.Count == 0)
        {
            _chosenBox.Children.Add(new TextBlock
            {
                Text = "Пока ничего не выбрано",
                FontSize = 11,
                Foreground = Ink.Faint,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(2, 4, 0, 0)
            });
        }

        _ok.IsEnabled = _chosen.Count > 0 && !_hasError;
    }

    /// <summary>
    /// Checks the chosen folders, and marks the one that cannot be used.
    /// </summary>
    /// <remarks>
    /// Every change re-checks rather than only at the end, because the answer is what tells
    /// somebody they are finished. A folder that has emptied itself, or that holds something
    /// other than photographs, is no use for a book, and finding that out only on "load" would
    /// make the button the thing that reports errors.
    ///
    /// Older results are dropped rather than applied: a check that took time can finish after
    /// the list has changed again, and the older answer would be describing a list that is no
    /// longer there.
    /// </remarks>
    private async Task RevalidateAsync()
    {
        if (_validating) return;

        if (_chosen.Count == 0)
        {
            SetError(null);
            return;
        }

        _validating = true;
        var snapshot = _chosen.Select(c => c.Path).ToList();
        var result = await _files.ValidateFoldersDetailedAsync(snapshot);

        if (!_chosen.Select(c => c.Path).SequenceEqual(snapshot, StringComparer.OrdinalIgnoreCase))
        {
            _validating = false;
            await RevalidateAsync();
            return;
        }

        _validating = false;

        if (result.IsValid)
        {
            foreach (var entry in _chosen) entry.HasError = false;
            SetError(null);
            return;
        }

        foreach (var entry in _chosen)
        {
            bool isProblem = !string.IsNullOrEmpty(result.ProblemFolder) &&
                             string.Equals(entry.Path, result.ProblemFolder, StringComparison.OrdinalIgnoreCase);

            entry.HasError = isProblem;
            entry.ErrorMessage = isProblem ? result.ErrorMessage : null;
        }

        SetError(result.ErrorMessage);
        RefreshChosen();
    }

    private void SetError(string? message)
    {
        _hasError = message is not null;
        _errorBanner.IsVisible = _hasError;
        if (message is not null) _errorText.Text = message;
        _ok.IsEnabled = _chosen.Count > 0 && !_hasError;
    }

    private async Task ConfirmAsync()
    {
        if (_chosen.Count == 0) return;

        // Checked again on the way out: a folder can empty itself between being chosen and
        // being accepted, and accepting it then is what makes the screen that follows report a
        // broken book.
        var result = await _files.ValidateFoldersDetailedAsync(_selected.ToList());

        if (!result.IsValid)
        {
            await RevalidateAsync();
            return;
        }

        // The answer goes back as the dialog's result, and a window closed without deciding returns
        // nothing at all - which is how "clear the project" stays distinguishable from
        // "never mind".
        Close(_selected.ToList());
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close(null);
            e.Handled = true;
            return;
        }

        if (e.Key != Key.Enter) return;

        // Enter belongs to the search box while the search box has it.
        if (ReferenceEquals(e.Source, _filter)) return;

        if (FocusManager?.GetFocusedElement() is Button { Tag: string path } && Directory.Exists(path))
        {
            Navigate(path);
            e.Handled = true;
            return;
        }

        if (_ok.IsEnabled)
        {
            _ = ConfirmAsync();
            e.Handled = true;
        }
    }

    /// <summary>
    /// "1 файл", "4 файла", "11 файлов".
    /// </summary>
    /// <remarks>
    /// The count is on every row, and a row that says "1 файлов" in a list of eleven is the
    /// kind of thing a person stops trusting. Teens are their own case: "11 файлов", not
    /// "11 файла".
    /// </remarks>
    private static string Files(int n)
    {
        string word = (n % 100) is >= 11 and <= 14 ? "файлов"
            : (n % 10) == 1 ? "файл"
            : (n % 10) is >= 2 and <= 4 ? "файла"
            : "файлов";

        return $"{n} {word}";
    }

    private static string? FirstExisting(IEnumerable<string> paths)
    {
        foreach (var path in paths)
        {
            if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path)) return path;
        }

        return null;
    }

    // ------------------------------------------------------------------ chrome bits --

    private static TextBlock Caption(string text, double bottom) => new()
    {
        Text = text,
        FontSize = 10,
        FontWeight = FontWeight.SemiBold,
        Foreground = Ink.Faint,
        Margin = new Thickness(6, 0, 0, bottom)
    };

    private static Control Box(Control child, double h, double v) => new Border
    {
        Background = Ink.Muted,
        BorderBrush = Ink.Border,
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(12),
        Padding = new Thickness(h, v),
        Child = child
    };

    private static Button PlainButton(string text, bool accent, Action onClick)
    {
        var button = new Button
        {
            Content = text,
            Padding = new Thickness(16, 10),
            FontSize = 13,
            FontWeight = FontWeight.SemiBold,
            CornerRadius = new CornerRadius(12),
            Background = accent ? Ink.Accent : Ink.Surface,
            Foreground = accent ? Brushes.White : Ink.Text,
            BorderBrush = accent ? Brushes.Transparent : Ink.Border,
            BorderThickness = new Thickness(1),
            Cursor = new Cursor(StandardCursorType.Hand)
        };

        button.Click += (_, _) => onClick();
        return button;
    }

    private static TextBlock Glyph(char codePoint, double size, IBrush brush) => new()
    {
        // Written as its number so that no round trip through a file can turn it into a
        // different character.
        Text = codePoint.ToString(),
        FontFamily = new FontFamily("Segoe MDL2 Assets"),
        FontSize = size,
        Foreground = brush
    };

    private sealed class FolderEntry
    {
        public FolderEntry(string fullPath, int files)
        {
            FullPath = fullPath;
            Name = System.IO.Path.GetFileName(fullPath);
            Files = files;
        }

        public string FullPath { get; }
        public string Name { get; }
        public int Files { get; }
    }

    /// <summary>
    /// A folder that has been chosen, and whether it can be used.
    /// </summary>
    /// <remarks>
    /// Observable because the tray is rebuilt from it and the answer arrives after the window
    /// is already on screen.
    /// </remarks>
    private sealed class ChosenFolder : ObservableObject
    {
        private bool _hasError;
        private string? _errorMessage;

        public ChosenFolder(string path) => Path = path;

        public string Path { get; }

        public bool HasError
        {
            get => _hasError;
            set => SetProperty(ref _hasError, value);
        }

        public string? ErrorMessage
        {
            get => _errorMessage;
            set => SetProperty(ref _errorMessage, value);
        }
    }

    /// <summary>The shapes this window draws, on a 24 grid.</summary>
    private static class IconGeometry
    {
        public static Geometry Folder =>
            Geometry.Parse("M 3 7.2 A 1.4 1.4 0 0 1 4.4 5.8 H 9.2 L 11.4 8 H 19.6 A 1.4 1.4 0 0 1 21 9.4 V 17.6 A 1.4 1.4 0 0 1 19.6 19 H 4.4 A 1.4 1.4 0 0 1 3 17.6 Z");

        public static Geometry Drive =>
            Geometry.Parse("M 4 5 H 20 A 1.5 1.5 0 0 1 21.5 6.5 V 17.5 A 1.5 1.5 0 0 1 20 19 H 4 A 1.5 1.5 0 0 1 2.5 17.5 V 6.5 A 1.5 1.5 0 0 1 4 5 Z M 2.5 13 H 21.5");

        public static Geometry Tick =>
            Geometry.Parse("M 5 12.4 L 9.8 17 L 19 7.4");

        public static Geometry Close =>
            Geometry.Parse("M 6.5 6.5 L 17.5 17.5 M 17.5 6.5 L 6.5 17.5");

        public static Geometry Warning =>
            Geometry.Parse("M 12 3.6 L 21.4 20 H 2.6 Z M 12 9.4 V 14.2 M 12 17.1 V 17.2");

        public static Geometry ArrowLeft =>
            Geometry.Parse("M 14.5 5.5 L 8 12 L 14.5 18.5 M 8.4 12 H 20");
    }

    /// <summary>
    /// Draws the shapes above as strokes.
    /// </summary>
    /// <remarks>
    /// Strokes rather than glyphs out of an icon font, for the reason the folders screen gives:
    /// a glyph and a line of type have different heights and nothing aligns them, and the
    /// weight of the line never quite matches the weight of the words beside it.
    /// </remarks>
    private static class Icon
    {
        public static Avalonia.Controls.Shapes.Path Stroke(Geometry data, double size, IBrush brush) => new()
        {
            Data = data,
            Width = size,
            Height = size,
            Stretch = Stretch.Uniform,
            Stroke = brush,
            StrokeThickness = 1.8,
            StrokeLineCap = PenLineCap.Round,
            VerticalAlignment = VerticalAlignment.Center
        };
    }

    /// <summary>The colours this shell's windows use, in one place.</summary>
    private static class Ink
    {
        public static IBrush Surface => ConfirmWindow.Brush("#FFFFFF");
        public static IBrush Muted => ConfirmWindow.Brush("#F8FAFC");
        public static IBrush Border => ConfirmWindow.Brush("#E2E8F0");
        public static IBrush BorderStrong => ConfirmWindow.Brush("#CBD5E1");
        public static IBrush Faint => ConfirmWindow.Brush("#94A3B8");
        public static IBrush Heading => ConfirmWindow.Brush("#0F172A");
        public static IBrush Text => ConfirmWindow.Brush("#334155");
        public static IBrush Text2 => ConfirmWindow.Brush("#475569");
        public static IBrush Accent => ConfirmWindow.Brush("#4F46E5");
        public static IBrush AccentTint => ConfirmWindow.Brush("#EEF2FF");
        public static IBrush AccentLine => ConfirmWindow.Brush("#C7D2FE");
        public static IBrush Warn => ConfirmWindow.Brush("#F59E0B");

        // Named rather than written where they are used: `Brush` on its own inside a control
        // resolves to the control's own protected property of that name, not to the colour
        // helper, and the mistake reads as a method that cannot be called.
        public static IBrush Danger => ConfirmWindow.Brush("#FEF2F2");
        public static IBrush DangerLine => ConfirmWindow.Brush("#FECACA");
        public static IBrush DangerInk => ConfirmWindow.Brush("#991B1B");
        public static IBrush DangerStrong => ConfirmWindow.Brush("#B91C1C");
        public static IBrush DangerMark => ConfirmWindow.Brush("#DC2626");
    }
}
