using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.VisualTree;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using PhotoBook.Application;

namespace PhotoBook.Shell.Views;

/// <summary>
/// Picks the folders a project's photographs come from. A panel over the page, not a second
/// window.
/// </summary>
/// <remarks>
/// <para>
/// It was a frameless white window over a white page, and there was nothing in that to say
/// which one was on top. Dimming the page and putting the panel on it answers it, and answers
/// the second question as well - whether the panel belongs to this program or to another one
/// that happened to be in front of it.
/// </para>
/// <para>
/// Four decisions that are not the obvious ones, and why they went this way:
/// </para>
/// <list type="bullet">
/// <item><b>The tick is the only way to choose.</b> The rest of the row opens the folder.
/// Ticking used to shift the layout - the message about a folder you cannot use appeared and
/// pushed the list down while the pointer was still travelling - and a double click to open
/// something is not a thing a hand can do when the row is moving. Now opening a folder and
/// choosing a folder are two different targets, and neither moves anything.</item>
/// <item><b>A chosen folder is a name, and clicking it walks to it.</b> It showed the whole
/// path and removed the folder when pressed, which is what an accidental click costs: a walk
/// back down to find it. Only the cross removes it.</item>
/// <item><b>The message area has a fixed height.</b> A message that appears must not move the
/// list under the pointer. It is always the same box, and a long message scrolls inside it.</item>
/// <item><b>Opens next to the project's folders, not inside one.</b> Which of several folders
/// to open inside is a question with no good answer; the directory holding them is the place
/// where all of them can be seen at once.</item>
/// </list>
/// <para>
/// Reading folders happens off the drawing thread.
/// </para>
/// </remarks>
public class FolderBrowserView : OverlaySheet<List<string>>
{
    /// <summary>
    /// The panel is this size and no other, which is the point of the two numbers.
    /// </summary>
    /// <remarks>
    /// Measured, not chosen. Before this it was measured by whatever it contained, and a
    /// folder list grows with the number of folders in it - so the same panel was 714 tall on
    /// one folder and 1552 on another, which is taller than the window it sits in. 1112 is the
    /// width it settled on by itself in every state measured, and 714 is the height with a
    /// full list in it and the message box at its full height: the state that has the most in
    /// it, and therefore the one worth fixing at.
    ///
    /// On a window too small for that, the panel takes what there is rather than running off
    /// the edge. It is never larger.
    /// </remarks>
    private const double FixedWidth = 1112;
    private const double FixedHeight = 714;
    private const double SmallestWidth = 820;
    private const double SmallestHeight = 480;

    /// <summary>The gap the window leaves around the panel.</summary>
    private const double SheetMargin = 28;

    private readonly IFileService _files;

    private readonly List<string> _selected = new();
    private readonly List<ChosenFolder> _chosen = new();
    private readonly List<FolderEntry> _folders = new();

    /// <summary>
    /// The folders the list is actually showing, which is what "choose all" acts on.
    /// </summary>
    private readonly List<FolderEntry> _visible = new();

    /// <summary>
    /// Where the pointer has been, and where in that list it is now.
    /// </summary>
    /// <remarks>
    /// A stack was the wrong shape for it. Going back popped one entry and moved, and then the
    /// next move pushed the place you had just left back on top of it - so back, back, back
    /// walked into a circle between two folders and never reached the disk it started from.
    /// A list with a position in it is what a walk is: going back moves the position, and
    /// going somewhere new forgets everything after it.
    /// </remarks>
    private readonly List<string> _history = new();
    private int _historyAt = -1;

    private string _currentPath = string.Empty;
    private bool _validating;
    private bool _hasError;

    private readonly StackPanel _placesBox = new() { Spacing = 2 };
    private readonly StackPanel _foldersBox = new() { Spacing = 4 };
    private readonly StackPanel _chosenBox = new() { Spacing = 4 };
    private readonly TextBlock _pathText = new();
    private readonly TextBlock _chosenCount = new();
    private readonly TextBlock _foundCaption = new();
    private readonly TextBlock _logText = new();
    private readonly TextBox _filter = new();
    private readonly Button _back = new();
    private readonly Button _ok = new();
    private readonly Button _selectAll = new();
    private readonly Border _logBox = new();

    public FolderBrowserView(IFileService fileService, List<string>? existingFolders = null)
    {
        _files = fileService;

        FontFamily = new FontFamily("Inter");
        Content = Build();

        if (existingFolders is not null)
        {
            foreach (var folder in existingFolders.Where(f => !string.IsNullOrWhiteSpace(f)))
            {
                if (_selected.Contains(folder)) continue;
                _selected.Add(folder);
                _chosen.Add(new ChosenFolder(folder));
            }
        }

        RefreshChosen();

        // Next to the project's folders rather than inside one of them: with several folders
        // there is no right one to open, and the directory holding them is where they can all
        // be seen. With no folders at all, the Desktop.
        var first = _chosen.FirstOrDefault(c => Directory.Exists(c.Path))?.Path;
        var start = first is null
            ? Environment.GetFolderPath(Environment.SpecialFolder.Desktop)
            : System.IO.Path.GetDirectoryName(first) ?? first;

        _ = NavigateAsync(start, remember: false);
        _ = RevalidateAsync();

        KeyDown += OnKeyDown;
    }

    /// <summary>
    /// Keeps the panel at its size, or at the size of the window when the window is smaller.
    /// </summary>
    /// <remarks>
    /// The size is set here rather than written once on the control because it depends on the
    /// window: a fixed width on a narrow window runs off both edges, and a panel that runs off
    /// the edge is worse than a small one. It shrinks only when it has to and never grows.
    /// </remarks>
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        if (TopLevel.GetTopLevel(this) is not { } top) return;

        FitTo(top);
        top.SizeChanged += (_, _) => FitTo(top);
    }

    private void FitTo(TopLevel top)
    {
        Width = Math.Clamp(top.Bounds.Width - SheetMargin * 2, SmallestWidth, FixedWidth);
        Height = Math.Clamp(top.Bounds.Height - SheetMargin * 2, SmallestHeight, FixedHeight);
    }

    // ------------------------------------------------------------------ the sheet --

    private Control Build()
    {
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

        var root = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto,Auto") };

        var header = HeaderBand();
        Grid.SetRow(header, 0);
        root.Children.Add(header);

        var body = new Grid
        {
            Margin = new Thickness(20, 16, 20, 0),
            ColumnDefinitions = new ColumnDefinitions("200,14,*,14,264")
        };

        var places = PlacesPanel();
        Grid.SetColumn(places, 0);
        body.Children.Add(places);

        var folders = FoldersColumn();
        Grid.SetColumn(folders, 2);
        body.Children.Add(folders);

        var chosen = ChosenPanel();
        Grid.SetColumn(chosen, 4);
        body.Children.Add(chosen);

        Grid.SetRow(body, 1);
        root.Children.Add(body);

        var log = LogArea();
        Grid.SetRow(log, 2);
        root.Children.Add(log);

        _foundCaption.FontSize = 12;
        _foundCaption.Foreground = Ink.Text2;
        _foundCaption.VerticalAlignment = VerticalAlignment.Center;

        // Taking everything, and taking it back, sit beside the count of what is on screen -
        // that is the thing the count is about. It is here and not above the list because
        // the list is the one part of this panel with no room to give away.
        _selectAll.Content = "Выбрать все";
        _selectAll.Padding = new Thickness(12, 7);
        _selectAll.FontSize = 12;
        _selectAll.FontWeight = FontWeight.SemiBold;
        _selectAll.CornerRadius = new CornerRadius(10);
        _selectAll.Background = Brushes.Transparent;
        _selectAll.Foreground = Ink.Accent;
        _selectAll.BorderThickness = new Thickness(0);
        _selectAll.Cursor = new Cursor(StandardCursorType.Hand);
        _selectAll.VerticalAlignment = VerticalAlignment.Center;
        _selectAll.Margin = new Thickness(12, 0, 0, 0);
        _selectAll.Click += (_, _) => ToggleEveryVisible();

        var left = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { _foundCaption, _selectAll }
        };

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children =
            {
                PlainButton("Отмена", accent: false, onClick: Leave),
                _ok
            }
        };
        Grid.SetColumn(buttons, 1);

        var footer = new Grid
        {
            Margin = new Thickness(20, 16, 20, 20),
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            Children = { left, buttons }
        };
        Grid.SetRow(footer, 3);
        root.Children.Add(footer);

        return new Border
        {
            Background = Ink.Surface,
            CornerRadius = new CornerRadius(20),
            BorderBrush = Ink.Border,
            BorderThickness = new Thickness(1),
            BoxShadow = new BoxShadows(BoxShadow.Parse("0 24 70 0 #520F172A")),
            Child = root
        };
    }

    private Control HeaderBand()
    {
        var close = new Button
        {
            Content = Glyph((char)0xE711, 14, Ink.Text2),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(8),
            Cursor = new Cursor(StandardCursorType.Hand)
        };
        AutomationProperties.SetName(close, "Закрыть");
        close.Click += (_, _) => Leave();

        var column = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        column.Children.Add(new TextBlock
        {
            Text = "Проводник папок проекта",
            FontSize = 17,
            FontWeight = FontWeight.Bold,
            Foreground = Ink.Heading
        });
        column.Children.Add(new TextBlock
        {
            Text = "Выберите папки с фотографиями. В одной папке — одна книга.",
            FontSize = 12,
            Foreground = Ink.Text2
        });

        var tile = new Border
        {
            Width = 40,
            Height = 40,
            CornerRadius = new CornerRadius(12),
            Background = Ink.AccentTint,
            VerticalAlignment = VerticalAlignment.Center,
            Child = Icon.Stroke(IconGeometry.Folder, 18, Ink.Accent)
        };

        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
            Children = { tile }
        };
        Grid.SetColumn(column, 1);
        column.Margin = new Thickness(14, 0, 0, 0);
        grid.Children.Add(column);
        Grid.SetColumn(close, 2);
        grid.Children.Add(close);

        return new Border
        {
            Background = Ink.Muted,
            CornerRadius = new CornerRadius(20, 20, 0, 0),
            Padding = new Thickness(24, 18),
            Child = grid
        };
    }

    // ------------------------------------------------------------------- the parts --

    private Control PlacesPanel()
    {
        foreach (var (name, path) in Places())
        {
            bool exists = Directory.Exists(path);

            var icon = Icon.Stroke(IconGeometry.Drive, 14, Ink.Warn);
            icon.Opacity = exists ? 1 : 0.35;

            var row = new Button
            {
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(10, 8),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                IsEnabled = exists,
                Cursor = new Cursor(StandardCursorType.Hand),
                Content = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8,
                    VerticalAlignment = VerticalAlignment.Center,
                    Children =
                    {
                        icon,
                        new TextBlock
                        {
                            Text = name,
                            FontSize = 12,
                            Foreground = exists ? Ink.Text : Ink.Faint,
                            TextTrimming = TextTrimming.CharacterEllipsis,
                            VerticalAlignment = VerticalAlignment.Center
                        }
                    }
                }
            };

            if (exists)
            {
                AutomationProperties.SetName(row, name);
                row.Click += (_, _) => Navigate(path);
            }

            _placesBox.Children.Add(row);
        }

        return Box(new StackPanel
        {
            Spacing = 6,
            Children = { Caption("Места", 6), _placesBox }
        }, 10, 12);
    }

    private Control FoldersColumn()
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
        _back.Padding = new Thickness(10, 7);
        _back.FontSize = 12;
        _back.FontWeight = FontWeight.SemiBold;
        _back.CornerRadius = new CornerRadius(12);
        _back.Background = Ink.Surface;
        _back.Foreground = Ink.Text;
        _back.BorderBrush = Ink.Border;
        _back.BorderThickness = new Thickness(1);
        _back.Cursor = new Cursor(StandardCursorType.Hand);
        AutomationProperties.SetName(_back, "Назад");
        _back.Click += (_, _) => GoBack();

        _pathText.FontFamily = new FontFamily("Consolas");
        _pathText.FontSize = 11.5;
        _pathText.Foreground = Ink.Text2;
        _pathText.TextTrimming = TextTrimming.CharacterEllipsis;
        _pathText.VerticalAlignment = VerticalAlignment.Center;
        _pathText.Margin = new Thickness(12, 0, 12, 0);

        _filter.Watermark = "Поиск";
        _filter.FontSize = 12;
        _filter.Height = 30;
        _filter.Width = 190;
        _filter.HorizontalAlignment = HorizontalAlignment.Right;
        _filter.VerticalAlignment = VerticalAlignment.Center;
        _filter.CornerRadius = new CornerRadius(9);
        _filter.Background = Ink.Surface;
        _filter.BorderBrush = Ink.Border;
        _filter.BorderThickness = new Thickness(1);
        _filter.Padding = new Thickness(9, 5);
        _filter.TextChanged += (_, _) => BuildFolderRows();

        // One line: back, the path, and the search. They were three rows, and the two rows
        // above the list are the ones the list could least afford to give away - they are
        // where the eye goes before it goes anywhere else.
        var bar = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
        bar.Children.Add(_back);
        Grid.SetColumn(_pathText, 1);
        bar.Children.Add(_pathText);
        Grid.SetColumn(_filter, 2);
        bar.Children.Add(_filter);

        var scroll = new ScrollViewer
        {
            Content = _foldersBox,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };

        var column = new Grid { RowDefinitions = new RowDefinitions("Auto,*"), Margin = new Thickness(0, 12, 0, 0) };
        column.Children.Add(bar);
        Grid.SetRow(scroll, 1);
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

    /// <summary>
    /// The one place the window says anything about the choice, and it is always this big.
    /// </summary>
    /// <remarks>
    /// It is a fixed height and it is always there. A message that arrives by growing a
    /// banner pushes the list down under a pointer that is already moving towards a row, and
    /// the row under the pointer is then a different one - which is how "I clicked the folder
    /// and it chose another" happens. Here nothing moves, and a message longer than the box
    /// scrolls inside it.
    /// </remarks>
    private Control LogArea()
    {
        _logText.FontSize = 12;
        _logText.Foreground = Ink.Text2;
        _logText.TextWrapping = TextWrapping.Wrap;
        _logText.LineHeight = 18;
        _logText.VerticalAlignment = VerticalAlignment.Top;

        _logBox.Background = Ink.Muted;
        _logBox.BorderBrush = Ink.Border;
        _logBox.BorderThickness = new Thickness(1);
        _logBox.CornerRadius = new CornerRadius(12);
        _logBox.Height = 92;
        _logBox.Margin = new Thickness(20, 16, 20, 0);
        _logBox.Padding = new Thickness(16, 14);
        _logBox.Child = new ScrollViewer
        {
            Content = _logText,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };

        return _logBox;
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

        if (remember && !string.IsNullOrEmpty(_currentPath) && _currentPath != path)
        {
            // Everywhere after the current place is where you had been going before you went
            // back. It is dropped: having come back to change your mind, the old continuation
            // is not a continuation any more.
            if (_historyAt < _history.Count - 1)
                _history.RemoveRange(_historyAt + 1, _history.Count - _historyAt - 1);

            _history.Add(path);
            _historyAt = _history.Count - 1;
        }
        else if (_historyAt < 0)
        {
            // Where the panel opened is not a step in the walk, it is where the walk starts.
            // Recording it means opening somewhere, going up to the Desktop and coming back
            // retraces Desktop - start - Desktop, and three presses of Назад look like a loop
            // between two folders rather than a route that ran out.
            _historyAt = -1;
        }

        _currentPath = path;
        _pathText.Text = path;
        _back.IsEnabled = _historyAt > 0;
        _filter.Text = string.Empty;

        _folders.Clear();
        _folders.AddRange(listing);

        BuildFolderRows();
    }

    private void Navigate(string path) => _ = NavigateAsync(path, remember: true);

    /// <summary>
    /// The subfolders of a path with the number of photographs in each, or null when the path
    /// cannot be read at all.
    /// </summary>
    /// <remarks>
    /// The count comes with the listing rather than on demand: it is the one number that says
    /// whether a folder can be used, and asking for it later means asking for it again after
    /// every change.
    /// </remarks>
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
        if (_historyAt <= 0) return;
        _historyAt--;
        _ = NavigateAsync(_history[_historyAt], remember: false);
    }

    private void BuildFolderRows()
    {
        _foldersBox.Children.Clear();
        _foundCaption.Text = $"Подпапок в этой папке: {_folders.Count}";

        var needle = _filter.Text?.Trim() ?? string.Empty;
        _visible.Clear();
        int shown = 0;

        foreach (var folder in _folders)
        {
            if (needle.Length > 0 &&
                folder.Name.IndexOf(needle, StringComparison.CurrentCultureIgnoreCase) < 0) continue;

            _visible.Add(folder);
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
                Margin = new Thickness(12, 12, 0, 0)
            });
        }

        UpdateSelectAll();
    }

    /// <summary>
    /// Chooses everything on screen, or gives it all back.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Only what the list is showing. With a search typed in, "выбрать все" that reached
    /// behind the search would add folders the person cannot see and cannot undo one at a
    /// time.
    /// </para>
    /// <para>
    /// One press instead of twenty. A client's order is twenty books in twenty folders, and
    /// asking for twenty separate clicks before anything happens is a tax on the ordinary
    /// case rather than on an unusual one.
    /// </para>
    /// <para>
    /// Empty folders are taken along with the rest. They are shown as "нет фото" and the
    /// check refuses them afterwards, and the point of this button is the same as the point of
    /// ticking by hand: what is visible is what was asked for, and the refusal explains
    /// itself.
    /// </para>
    /// </remarks>
    private void ToggleEveryVisible()
    {
        if (_visible.Count == 0) return;

        bool allChosen = _visible.TrueForAll(f => _selected.Contains(f.FullPath));

        foreach (var folder in _visible)
        {
            if (allChosen)
            {
                _selected.Remove(folder.FullPath);
                var entry = _chosen.FirstOrDefault(c =>
                    string.Equals(c.Path, folder.FullPath, StringComparison.OrdinalIgnoreCase));
                if (entry is not null) _chosen.Remove(entry);
            }
            else if (!_selected.Contains(folder.FullPath))
            {
                _selected.Add(folder.FullPath);
                _chosen.Add(new ChosenFolder(folder.FullPath));
            }
        }

        RefreshChosen();
        BuildFolderRows();
        _ = RevalidateAsync();
    }

    /// <summary>
    /// The button reads as what it will do, not as what it is.
    /// </summary>
    /// <remarks>
    /// "Выбрать все" on a list where everything is already chosen would do the opposite of
    /// what it says, which is worse than having no button at all.
    /// </remarks>
    private void UpdateSelectAll()
    {
        bool allChosen = _visible.Count > 0 && _visible.TrueForAll(f => _selected.Contains(f.FullPath));
        _selectAll.Content = allChosen ? "Снять выбор" : "Выбрать все";
        _selectAll.IsEnabled = _visible.Count > 0;
    }

    /// <summary>
    /// One folder: a tick that chooses it, and the rest of the row that opens it.
    /// </summary>
    /// <remarks>
    /// Two controls rather than one row with two meanings. When the whole row chose, ticking a
    /// folder made the list jump - the message about a folder that cannot be used arrived and
    /// moved everything under the pointer - and then opening a folder needed a double click on
    /// a row that was still moving. Nothing here moves, and neither action depends on the other.
    /// </remarks>
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

        var choose = new Button
        {
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0),
            Cursor = new Cursor(StandardCursorType.Hand),
            Margin = new Thickness(0, 0, 12, 0),
            Content = tick
        };
        AutomationProperties.SetName(choose, chosen
            ? $"Убрать {folder.Name} из выбранных"
            : $"Выбрать папку {folder.Name}");
        choose.Click += (_, _) => Toggle(folder);

        var open = new Button
        {
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Cursor = new Cursor(StandardCursorType.Hand),
            Margin = new Thickness(10, 0, 0, 0),
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 10,
                VerticalAlignment = VerticalAlignment.Center,
                Children =
                {
                    Icon.Stroke(IconGeometry.Folder, 15, Ink.Warn),
                    new TextBlock
                    {
                        Text = folder.Name,
                        FontSize = 13,
                        FontWeight = FontWeight.SemiBold,
                        Foreground = Ink.Text,
                        TextTrimming = TextTrimming.CharacterEllipsis,
                        VerticalAlignment = VerticalAlignment.Center
                    },
                    new TextBlock
                    {
                        FontSize = 11,
                        Foreground = folder.Files == 0 ? Ink.DangerMark : Ink.Faint,
                        VerticalAlignment = VerticalAlignment.Center,
                        Text = folder.Files == 0 ? "нет фото" : Files(folder.Files)
                    }
                }
            }
        };
        AutomationProperties.SetName(open, $"Открыть папку {folder.Name}");
        ToolTip.SetTip(open, folder.FullPath);
        open.Click += (_, _) => Navigate(folder.FullPath);

        var row = new Border
        {
            Background = chosen ? Ink.AccentTint : Brushes.Transparent,
            BorderBrush = chosen ? Ink.AccentLine : Brushes.Transparent,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(12, 10),
            Child = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("Auto,*")
            }
        };

        // Both children are placed by hand. A grid does not move the second child along for
        // you - it leaves it in the first column, where it lands on top of the first one and
        // the folder's mark is drawn across the tick.
        ((Grid)row.Child).Children.Add(choose);
        Grid.SetColumn(open, 1);
        ((Grid)row.Child).Children.Add(open);

        if (folder.Files == 0)
            ToolTip.SetTip(row, "В папке нет фотографий — для книги она не подойдёт");

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
            var walk = new Button
            {
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(8, 6),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Cursor = new Cursor(StandardCursorType.Hand),
                Content = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8,
                    VerticalAlignment = VerticalAlignment.Center,
                    Children =
                    {
                        new TextBlock
                        {
                            Text = entry.Name,
                            FontSize = 12,
                            TextTrimming = TextTrimming.CharacterEllipsis,
                            Foreground = entry.HasError ? Ink.DangerStrong : Ink.Text,
                            FontWeight = entry.HasError ? FontWeight.SemiBold : FontWeight.Normal,
                            VerticalAlignment = VerticalAlignment.Center
                        }
                    }
                }
            };

            AutomationProperties.SetName(walk, $"Перейти к папке {entry.Name}");
            ToolTip.SetTip(walk, entry.Path);

            // Clicking a chosen folder walks to the directory holding it. It used to remove
            // it, which is what an accidental click costs and what nobody can undo from the
            // keyboard: the folder has to be found again and ticked once more.
            walk.Click += (_, _) => WalkTo(entry.Path);

            var row = new Border
            {
                Background = entry.HasError ? Ink.Danger : Brushes.Transparent,
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(4),
                Child = new Grid
                {
                    ColumnDefinitions = new ColumnDefinitions("*,Auto"),
                    Children = { walk }
                }
            };

            // The mark and the cross are both here. A folder that cannot be used is exactly
            // the one a person wants to get rid of, and showing its problem in place of the
            // cross left no way to do that at all.
            if (entry.HasError)
            {
                var mark = Icon.Stroke(IconGeometry.Warning, 12, Ink.DangerMark);
                mark.Margin = new Thickness(6, 0, 2, 0);
                ((StackPanel)walk.Content).Children.Add(mark);
            }

            var drop = new Button
            {
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(6),
                Cursor = new Cursor(StandardCursorType.Hand),
                Content = Icon.Stroke(IconGeometry.Close, 12, Ink.Faint)
            };
            drop.Click += (_, _) =>
            {
                _selected.RemoveAll(p => string.Equals(p, entry.Path, StringComparison.OrdinalIgnoreCase));
                _chosen.Remove(entry);
                RefreshChosen();
                BuildFolderRows();
                _ = RevalidateAsync();
            };
            AutomationProperties.SetName(drop, $"Убрать {entry.Name} из выбранных");
            Grid.SetColumn(drop, 1);
            ((Grid)row.Child).Children.Add(drop);

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
                Margin = new Thickness(4, 6, 0, 0)
            });
        }

        _ok.IsEnabled = _chosen.Count > 0 && !_hasError;
    }

    /// <summary>
    /// Shows the directory a chosen folder sits in.
    /// </summary>
    /// <remarks>
    /// The parent, not the folder. The chosen folder is already in the list on the right;
    /// opening its own contents would put the pointer somewhere the choice is not.
    /// </remarks>
    private void WalkTo(string folderPath)
    {
        var parent = System.IO.Path.GetDirectoryName(folderPath);
        if (string.IsNullOrEmpty(parent) || !Directory.Exists(parent)) return;
        Navigate(parent);
    }

    /// <summary>
    /// Checks the chosen folders and reports in the fixed box below.
    /// </summary>
    /// <remarks>
    /// Every change re-checks rather than only at the end, because the answer is what tells
    /// somebody they are finished. Older results are dropped rather than applied: a check that
    /// took time can finish after the list has changed again, and the older answer would be
    /// describing a list that is no longer there.
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
        }

        SetError(result.ErrorMessage);
        RefreshChosen();
    }

    private void SetError(string? message)
    {
        _hasError = message is not null;

        if (message is null)
        {
            _logBox.Background = Ink.Muted;
            _logBox.BorderBrush = Ink.Border;
            _logText.Foreground = Ink.Text2;
            _logText.Text = _chosen.Count == 0
                ? "Выберите папки с фотографиями. Количество фотографий во всех выбранных папках должно быть одинаковым."
                : "Папки готовы к загрузке.";
        }
        else
        {
            _logBox.Background = Ink.Danger;
            _logBox.BorderBrush = Ink.DangerLine;
            _logText.Foreground = Ink.DangerInk;
            _logText.Text = message;
        }

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

        Finish(_selected.ToList());
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Leave();
            e.Handled = true;
            return;
        }

        if (e.Key != Key.Enter) return;

        // Enter belongs to the search box while the search box has it.
        if (ReferenceEquals(e.Source, _filter)) return;

        if (TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() is Button { Tag: string path }
            && Directory.Exists(path))
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

    /// <summary>"1 файл", "4 файла", "11 файлов".</summary>
    /// <remarks>
    /// The count is on every row, and a row that says "1 файлов" in a list of eleven is the
    /// kind of thing a person stops trusting. Teens are their own case: "11 файлов".
    /// </remarks>
    private static string Files(int n)
    {
        string word = (n % 100) is >= 11 and <= 14 ? "файлов"
            : (n % 10) == 1 ? "файл"
            : (n % 10) is >= 2 and <= 4 ? "файла"
            : "файлов";

        return $"{n} {word}";
    }

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
    /// Observable because the tray is rebuilt from it and the answer arrives after the panel
    /// is already on screen.
    /// </remarks>
    private sealed class ChosenFolder : ObservableObject
    {
        private bool _hasError;

        public ChosenFolder(string path) => Path = path;

        public string Path { get; }
        public string Name => System.IO.Path.GetFileName(Path);

        public bool HasError
        {
            get => _hasError;
            set => SetProperty(ref _hasError, value);
        }
    }

    /// <summary>The shapes this panel draws, on a 24 grid.</summary>
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
    /// a glyph and a line of type have different heights and nothing aligns them, and the weight
    /// of the line never quite matches the weight of the words beside it.
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
