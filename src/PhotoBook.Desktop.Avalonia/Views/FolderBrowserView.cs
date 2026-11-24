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
using PhotoBook.Shell.Services;

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
/// <item><b>Choosing and opening are two different targets.</b> A card is chosen by pressing
/// it, and opened by the small arrow in its corner. They used to share one row, so choosing
/// moved the list under the pointer that was still travelling, and then opening needed a
/// double click on a row that was still moving. Neither target moves anything now.</item>
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
    /// one folder and 1552 on another, which is taller than the window it sits in. The width
    /// and height here are the prototype's own two numbers - 1180 and 850 less the window's
    /// field - because this panel is now three columns wide and the middle one has to hold a
    /// row of cards rather than a list of lines.
    ///
    /// On a window too small for that, the panel takes what there is rather than running off
    /// the edge. It is never larger.
    /// </remarks>
    private const double FixedWidth = 1112;
    private const double FixedHeight = 740;
    private const double SmallestWidth = 900;
    private const double SmallestHeight = 560;

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

    private readonly WrapPanel _foldersBox = new() { Orientation = Orientation.Horizontal };
    private readonly StackPanel _chosenBox = new() { Spacing = 2 };
    private readonly StackPanel _crumbsBox = new()
    {
        Orientation = Orientation.Horizontal,
        VerticalAlignment = VerticalAlignment.Center
    };
    private readonly TextBlock _chosenCount = new();
    private readonly TextBlock _foundCaption = new();
    private readonly TextBlock _foundSub = new();
    private readonly TextBlock _projectCount = new();
    private readonly TextBlock _summaryPhotos = new();
    private readonly TextBlock _summaryFolders = new();
    private readonly TextBlock _logText = new();
    private readonly TextBlock _okLabel = new();
    private readonly TextBox _filter = new();
    private readonly Button _back = new();
    private readonly Button _ok = new();
    private readonly Button _selectAll = new();
    private readonly Border _logBox = new();
    private Shape _okArrow = null!;

    /// <summary>The cards of the current listing, so the row can be re-measured on a resize.</summary>
    private readonly List<(Border Card, Border Cover)> _cards = new();

    /// <summary>How many folders the project already had when the panel opened.</summary>
    private int _existingCount;

    /// <summary>The width one card is drawn at, decided from the width of the middle column.</summary>
    private double _cardWidth = 172;

    /// <summary>The scrolling viewport the cards are measured against.</summary>
    private ScrollViewer? _folderViewport;

    public FolderBrowserView(IFileService fileService, List<string>? existingFolders = null)
    {
        _files = fileService;

        // Before the panel is built: what the panel says about the project - the count in the
        // side column and the chips on the cards - is read from this list, and a list filled
        // afterwards would leave the first frame saying the project has nothing in it.
        if (existingFolders is not null)
        {
            foreach (var folder in existingFolders.Where(f => !string.IsNullOrWhiteSpace(f)))
            {
                if (_selected.Contains(folder)) continue;
                _selected.Add(folder);
                _chosen.Add(new ChosenFolder(folder, CountJpegFiles(folder)));
                _existingCount++;
            }
        }

        Content = Build();

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
        ConfigureConfirm();

        var root = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto") };

        var header = HeaderBand();
        Grid.SetRow(header, 0);
        root.Children.Add(header);

        var body = new Grid { ColumnDefinitions = new ColumnDefinitions("200,*,270") };

        var side = SidePanel();
        Grid.SetColumn(side, 0);
        body.Children.Add(side);

        var main = MainColumn();
        Grid.SetColumn(main, 1);
        body.Children.Add(main);

        var picked = PickedPanel();
        Grid.SetColumn(picked, 2);
        body.Children.Add(picked);

        Grid.SetRow(body, 1);
        root.Children.Add(body);

        var footer = FooterBand();
        Grid.SetRow(footer, 2);
        root.Children.Add(footer);

        return new Border
        {
            Background = Ink.Modal,
            CornerRadius = new CornerRadius(24),
            BorderBrush = Ink.Subtle,
            BorderThickness = new Thickness(1),
            BoxShadow = ThemeResources.Shadow("ShadowModal"),
            ClipToBounds = true,
            Child = root
        };
    }

    /// <summary>
    /// The panel's own name: what it is, which mode it serves, and one line on what to do.
    /// </summary>
    private Control HeaderBand()
    {
        var badge = new Border
        {
            Width = 46,
            Height = 46,
            CornerRadius = new CornerRadius(14),
            Background = GoldGradient(),
            VerticalAlignment = VerticalAlignment.Center,
            Child = Icon.Stroke(IconGeometry.FolderPlus, 22, Ink.OnPrimary)
        };

        var title = new TextBlock
        {
            Text = "Папки проекта",
            FontSize = 18.5,
            FontWeight = FontWeight.ExtraBold,
            LetterSpacing = -0.5,
            Foreground = Ink.Heading,
            VerticalAlignment = VerticalAlignment.Center
        };

        var copy = new StackPanel
        {
            Spacing = 4,
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 11,
                    VerticalAlignment = VerticalAlignment.Center,
                    Children = { title, ModeChip() }
                },
                new TextBlock
                {
                    Text = "Каждая папка — отдельная книга. Отметьте нужные — остальное программа сделает сама.",
                    FontSize = 12,
                    Foreground = Ink.Text2
                }
            }
        };

        var close = new Button
        {
            Width = 34,
            Height = 34,
            Padding = new Thickness(0),
            CornerRadius = new CornerRadius(10),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            VerticalAlignment = VerticalAlignment.Center,
            Cursor = new Cursor(StandardCursorType.Hand),
            Content = Icon.Stroke(IconGeometry.Close, 14, Ink.Text2)
        };
        AutomationProperties.SetName(close, "Закрыть");
        ToolTip.SetTip(close, "Закрыть");
        close.Click += (_, _) => Leave();
        Hover(close, Ink.Muted, Brushes.Transparent);

        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
            Children = { badge }
        };
        Grid.SetColumn(copy, 1);
        copy.Margin = new Thickness(15, 0, 0, 0);
        grid.Children.Add(copy);
        Grid.SetColumn(close, 2);
        grid.Children.Add(close);

        return new Border
        {
            Padding = new Thickness(20, 18, 24, 18),
            BorderBrush = Ink.Border,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = grid
        };
    }

    /// <summary>A small chip naming the mode the panel is serving.</summary>
    private static Control ModeChip()
    {
        var mark = Icon.Stroke(IconGeometry.Folder, 12, Ink.Accent);

        return new Border
        {
            Height = 24,
            CornerRadius = new CornerRadius(999),
            Padding = new Thickness(10, 0),
            Background = Ink.Muted,
            BorderBrush = Ink.Border,
            BorderThickness = new Thickness(1),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                VerticalAlignment = VerticalAlignment.Center,
                Children =
                {
                    mark,
                    new TextBlock
                    {
                        Text = "Уникальные папки",
                        FontSize = 10.5,
                        FontWeight = FontWeight.Bold,
                        Foreground = Ink.Text,
                        VerticalAlignment = VerticalAlignment.Center
                    }
                }
            }
        };
    }

    /// <summary>
    /// The left column: the places worth opening, the project's own count, and the one rule of
    /// the mode.
    /// </summary>
    private Control SidePanel()
    {
        var top = new StackPanel { Spacing = 2 };
        top.Children.Add(SideLabel("Быстрый доступ"));

        foreach (var place in Places())
            top.Children.Add(PlaceRow(place.Name, place.Path, place.Shape));

        top.Children.Add(SideLabel("В этом проекте", 16));
        top.Children.Add(ProjectBlock());

        var grid = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto") };
        grid.Children.Add(top);

        var tip = TipBlock();
        Grid.SetRow(tip, 2);
        grid.Children.Add(tip);

        return new Border
        {
            Background = Ink.Muted,
            BorderBrush = Ink.Border,
            BorderThickness = new Thickness(0, 0, 1, 0),
            Padding = new Thickness(12, 16, 12, 16),
            Child = grid
        };
    }

    private static Control SideLabel(string text, double top = 0) => new TextBlock
    {
        Text = text.ToUpperInvariant(),
        FontSize = 9.5,
        FontWeight = FontWeight.ExtraBold,
        LetterSpacing = 1.4,
        Foreground = Ink.Faint,
        Margin = new Thickness(10, top, 0, 7)
    };

    private Button PlaceRow(string name, string path, Geometry shape)
    {
        bool exists = Directory.Exists(path);

        var button = new Button
        {
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(10, 8),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            IsEnabled = exists,
            Cursor = new Cursor(StandardCursorType.Hand),
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 10,
                VerticalAlignment = VerticalAlignment.Center,
                Children =
                {
                    Icon.Stroke(shape, 16, exists ? Ink.Faint : Ink.Border),
                    new TextBlock
                    {
                        Text = name,
                        FontSize = 12.5,
                        FontWeight = FontWeight.SemiBold,
                        Foreground = exists ? Ink.Text2 : Ink.Faint,
                        TextTrimming = TextTrimming.CharacterEllipsis,
                        VerticalAlignment = VerticalAlignment.Center
                    }
                }
            }
        };

        if (exists)
        {
            AutomationProperties.SetName(button, name);
            button.Click += (_, _) => Navigate(path);
            Hover(button, Ink.Surface, Brushes.Transparent);
        }

        return button;
    }

    /// <summary>
    /// What the project already holds. The prototype draws a bar here; a bar without a total
    /// is a number pretending to be a share of something, so this block keeps the number and
    /// the sentence that says what will happen to it.
    /// </summary>
    private Control ProjectBlock()
    {
        _projectCount.Text = _existingCount.ToString();
        _projectCount.FontSize = 24;
        _projectCount.FontWeight = FontWeight.ExtraBold;
        _projectCount.Foreground = Ink.Accent;
        _projectCount.LineHeight = 24;
        _projectCount.VerticalAlignment = VerticalAlignment.Center;

        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 9,
            Children =
            {
                _projectCount,
                new TextBlock
                {
                    Text = _existingCount == 0 ? "папок пока нет" : "папок уже\nв проекте",
                    FontSize = 10.5,
                    Foreground = Ink.Text2,
                    LineHeight = 14,
                    VerticalAlignment = VerticalAlignment.Center
                }
            }
        };

        return new Border
        {
            CornerRadius = new CornerRadius(12),
            Background = Ink.AccentTint,
            BorderBrush = Ink.AccentLine,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(12),
            Margin = new Thickness(0, 2, 0, 0),
            Child = new StackPanel
            {
                Spacing = 8,
                Children =
                {
                    row,
                    new TextBlock
                    {
                        Text = _existingCount == 0
                            ? "Отмеченные папки станут книгами проекта."
                            : "Новые добавятся к ним — готовые книги не изменятся.",
                        FontSize = 10.5,
                        LineHeight = 15,
                        TextWrapping = TextWrapping.Wrap,
                        Foreground = Ink.Faint
                    }
                }
            }
        };
    }

    private static Control TipBlock()
    {
        var mark = Icon.Stroke(IconGeometry.Info, 13, Ink.Warn);
        mark.VerticalAlignment = VerticalAlignment.Top;
        mark.Margin = new Thickness(0, 1, 8, 0);

        var text = new TextBlock
        {
            Text = "Во всех папках заказа должно быть одинаковое число снимков.",
            FontSize = 10.5,
            LineHeight = 15,
            TextWrapping = TextWrapping.Wrap,
            Foreground = Ink.Faint
        };

        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*"),
            Children = { mark }
        };
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);

        return new Border
        {
            CornerRadius = new CornerRadius(10),
            Background = Ink.Surface,
            BorderBrush = Ink.Border,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(10, 11),
            Margin = new Thickness(0, 14, 0, 0),
            Child = grid
        };
    }

    /// <summary>
    /// The middle column: the walk along the disk, the search over what is on screen, and the
    /// cards of the folders themselves.
    /// </summary>
    private Control MainColumn()
    {
        ConfigureBack();

        var search = SearchField();

        var toolbar = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
        toolbar.Children.Add(_back);
        Grid.SetColumn(_crumbsBox, 1);
        _crumbsBox.Margin = new Thickness(8, 0, 8, 0);
        toolbar.Children.Add(_crumbsBox);
        Grid.SetColumn(search, 2);
        toolbar.Children.Add(search);

        _foundCaption.FontSize = 11.5;
        _foundCaption.FontWeight = FontWeight.Bold;
        _foundCaption.Foreground = Ink.Text;
        _foundCaption.VerticalAlignment = VerticalAlignment.Center;

        _foundSub.FontSize = 11.5;
        _foundSub.Foreground = Ink.Faint;
        _foundSub.VerticalAlignment = VerticalAlignment.Center;

        var context = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            Margin = new Thickness(2, 8, 2, 0)
        };
        context.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 7,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { _foundCaption, Dot(), _foundSub }
        });

        ConfigureSelectAll();
        Grid.SetColumn(_selectAll, 1);
        context.Children.Add(_selectAll);

        _foldersBox.SizeChanged += (_, e) => LayoutCards(e.NewSize.Width);

        var scroll = new ScrollViewer
        {
            Content = _foldersBox,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Padding = new Thickness(2, 2, 6, 0),
            Margin = new Thickness(0, 6, 0, 0)
        };
        _folderViewport = scroll;
        scroll.SizeChanged += (_, _) => LayoutCards(0);
        Grid.SetRow(scroll, 2);

        var log = MessageStrip();
        Grid.SetRow(log, 3);

        var column = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto"),
            Margin = new Thickness(16, 14, 16, 0)
        };
        column.Children.Add(toolbar);
        Grid.SetRow(context, 1);
        column.Children.Add(context);
        column.Children.Add(scroll);
        column.Children.Add(log);

        return column;
    }

    private void ConfigureBack()
    {
        _back.Width = 32;
        _back.Height = 32;
        _back.Padding = new Thickness(0);
        _back.CornerRadius = new CornerRadius(9);
        _back.Background = Ink.Muted;
        _back.Foreground = Ink.Text;
        _back.BorderThickness = new Thickness(0);
        _back.Cursor = new Cursor(StandardCursorType.Hand);
        _back.Content = Icon.Stroke(IconGeometry.ArrowLeft, 15, Ink.Text2);
        _back.VerticalAlignment = VerticalAlignment.Center;
        AutomationProperties.SetName(_back, "Назад");
        ToolTip.SetTip(_back, "Назад");
        _back.Click += (_, _) => GoBack();
        _back.Opacity = 0.35;
    }

    private Control SearchField()
    {
        _filter.Watermark = "Найти папку";
        _filter.FontSize = 12;
        _filter.Height = 34;
        _filter.Width = 176;
        _filter.VerticalAlignment = VerticalAlignment.Center;
        _filter.CornerRadius = new CornerRadius(9);
        _filter.Background = Ink.Muted;
        _filter.BorderBrush = Ink.Border;
        _filter.BorderThickness = new Thickness(1);
        _filter.Padding = new Thickness(30, 0, 8, 0);
        _filter.TextChanged += (_, _) => BuildFolderRows();

        var mark = Icon.Stroke(IconGeometry.Search, 14, Ink.Faint);
        mark.HorizontalAlignment = HorizontalAlignment.Left;
        mark.VerticalAlignment = VerticalAlignment.Center;
        mark.Margin = new Thickness(10, 0, 0, 0);
        mark.IsHitTestVisible = false;

        var field = new Panel { Children = { _filter, mark } };
        field.VerticalAlignment = VerticalAlignment.Center;
        return field;
    }

    private void ConfigureSelectAll()
    {
        _selectAll.Content = "Выбрать все";
        _selectAll.Padding = new Thickness(8, 4);
        _selectAll.FontSize = 11;
        _selectAll.FontWeight = FontWeight.Bold;
        _selectAll.CornerRadius = new CornerRadius(7);
        _selectAll.Background = Brushes.Transparent;
        _selectAll.Foreground = Ink.Accent;
        _selectAll.BorderThickness = new Thickness(0);
        _selectAll.Cursor = new Cursor(StandardCursorType.Hand);
        _selectAll.VerticalAlignment = VerticalAlignment.Center;
        _selectAll.Click += (_, _) => ToggleEveryVisible();
        Hover(_selectAll, Ink.AccentTint, Brushes.Transparent);
    }

    /// <summary>
    /// The right column: what the project is about to take, and the two totals.
    /// </summary>
    private Control PickedPanel()
    {
        _chosenCount.FontSize = 14.5;
        _chosenCount.FontWeight = FontWeight.ExtraBold;
        _chosenCount.Foreground = Ink.Heading;
        _chosenCount.Margin = new Thickness(0, 2, 0, 0);

        var head = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        head.Children.Add(new StackPanel { Children = { Kicker("В проект"), _chosenCount } });

        var clear = Link("Очистить", ClearChosen);
        clear.VerticalAlignment = VerticalAlignment.Top;
        Grid.SetColumn(clear, 1);
        head.Children.Add(clear);

        var headBorder = new Border
        {
            BorderBrush = Ink.Border,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(2, 0, 2, 11),
            Child = head
        };

        var scroll = new ScrollViewer
        {
            Content = _chosenBox,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Margin = new Thickness(0, 8, 0, 8)
        };
        Grid.SetRow(scroll, 1);

        var sum = SummaryRow();
        Grid.SetRow(sum, 2);

        var column = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*,Auto"),
            Children = { headBorder, scroll, sum }
        };

        return new Border
        {
            Background = Ink.Surface,
            BorderBrush = Ink.Border,
            BorderThickness = new Thickness(1, 0, 0, 0),
            Padding = new Thickness(14, 14, 14, 12),
            Child = column
        };
    }

    private Control SummaryRow()
    {
        var folders = StatBox("Папок", _summaryFolders);
        var photos = StatBox("Фото", _summaryPhotos);
        photos.Margin = new Thickness(8, 0, 0, 0);

        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,*"),
            Children = { folders },
            Margin = new Thickness(0, 11, 0, 0)
        };
        Grid.SetColumn(photos, 1);
        grid.Children.Add(photos);
        return grid;
    }

    private static Border StatBox(string label, TextBlock value)
    {
        value.FontSize = 15;
        value.FontWeight = FontWeight.ExtraBold;
        value.Foreground = Ink.Heading;
        value.Margin = new Thickness(0, 1, 0, 0);

        return new Border
        {
            CornerRadius = new CornerRadius(9),
            Background = Ink.Muted,
            BorderBrush = Ink.Border,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(9, 7),
            Child = new StackPanel { Children = { Kicker(label), value } }
        };
    }

    /// <summary>
    /// The one place the panel says anything about the choice, and it is always this big.
    /// </summary>
    /// <remarks>
    /// It is a fixed height and it is always there. A message that arrives by growing a
    /// banner pushes the cards up under a pointer that is already moving towards one, and the
    /// card under the pointer is then a different one - which is how "I clicked the folder and
    /// it chose another" happens. Here nothing moves, and a message longer than the box
    /// scrolls inside it.
    /// </remarks>
    private Control MessageStrip()
    {
        _logText.FontSize = 11.5;
        _logText.Foreground = Ink.Text2;
        _logText.TextWrapping = TextWrapping.Wrap;
        _logText.LineHeight = 17;
        _logText.VerticalAlignment = VerticalAlignment.Top;

        _logBox.Background = Ink.Muted;
        _logBox.BorderBrush = Ink.Border;
        _logBox.BorderThickness = new Thickness(1);
        _logBox.CornerRadius = new CornerRadius(12);
        _logBox.Height = 58;
        _logBox.Margin = new Thickness(0, 10, 0, 14);
        _logBox.Padding = new Thickness(14, 10);
        _logBox.Child = new ScrollViewer
        {
            Content = _logText,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };

        return _logBox;
    }

    private Control FooterBand()
    {
        var note = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 7,
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                Icon.Stroke(IconGeometry.Lock, 13, Ink.Ok),
                new TextBlock
                {
                    Text = "Исходные файлы не изменяются",
                    FontSize = 11,
                    Foreground = Ink.Faint,
                    VerticalAlignment = VerticalAlignment.Center
                }
            }
        };

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { PlainButton("Отмена", accent: false, onClick: Leave), _ok }
        };

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        grid.Children.Add(note);
        Grid.SetColumn(actions, 1);
        grid.Children.Add(actions);

        return new Border
        {
            Background = Ink.Muted,
            BorderBrush = Ink.Border,
            BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(22, 14),
            Child = grid
        };
    }

    private void ConfigureConfirm()
    {
        _okLabel.FontSize = 13.5;
        _okLabel.FontWeight = FontWeight.Bold;
        _okLabel.Foreground = Ink.OnPrimary;
        _okLabel.VerticalAlignment = VerticalAlignment.Center;

        _okArrow = Icon.Stroke(IconGeometry.ArrowRight, 14, Ink.OnPrimary);

        _ok.Content = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { _okLabel, _okArrow }
        };
        _ok.Height = 42;
        _ok.Padding = new Thickness(20, 0);
        _ok.CornerRadius = new CornerRadius(999);
        _ok.BorderThickness = new Thickness(0);
        _ok.Cursor = new Cursor(StandardCursorType.Hand);
        _ok.Click += async (_, _) => await ConfirmAsync();

        // The filled button of this panel keeps the panel's own gold and only lifts while it
        // can be pressed: on a button that cannot there is nothing for the pointer to answer.
        _ok.PointerEntered += (_, _) =>
        {
            if (_ok.IsEnabled) _ok.Background = Ink.GoldHover;
        };
        _ok.PointerPressed += (_, _) =>
        {
            if (_ok.IsEnabled) _ok.Background = Ink.GoldPressed;
        };
        _ok.PointerReleased += (_, _) =>
        {
            if (_ok.IsEnabled) _ok.Background = Ink.GoldHover;
        };
        _ok.PointerExited += (_, _) => UpdateConfirm();
    }

    /// <summary>
    /// The confirm button says what it will do, and stands down when there is nothing to do.
    /// </summary>
    private void UpdateConfirm()
    {
        int count = _chosen.Count;
        _okLabel.Text = count == 0 ? "Выберите папки" : $"Загрузить {Folders(count)}";

        // The label is drawn from a panel, so the control has no name of its own; without
        // this the button reads as an empty one to anything that is not a pair of eyes.
        AutomationProperties.SetName(_ok, _okLabel.Text);

        bool ready = count > 0 && !_hasError;
        _ok.IsEnabled = ready;
        _ok.Background = ready ? Ink.Gold : Ink.Muted;
        _ok.BorderBrush = ready ? Brushes.Transparent : Ink.Border;
        _ok.BorderThickness = new Thickness(ready ? 0 : 1);
        _okLabel.Foreground = ready ? Ink.OnPrimary : Ink.Faint;
        _okArrow.Stroke = ready ? Ink.OnPrimary : Ink.Faint;
        _ok.Cursor = new Cursor(ready ? StandardCursorType.Hand : StandardCursorType.Arrow);
    }

    // --------------------------------------------------------------- the behaviour --

    /// <summary>The places worth offering first, and the fixed drives behind them.</summary>
    private static IEnumerable<(string Name, string Path, Geometry Shape)> Places()
    {
        yield return ("Рабочий стол", Environment.GetFolderPath(Environment.SpecialFolder.Desktop), IconGeometry.Monitor);
        yield return ("Документы", Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), IconGeometry.Document);
        yield return ("Изображения", Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), IconGeometry.Image);
        yield return ("Загрузки", System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"), IconGeometry.Download);

        foreach (var drive in DriveInfo.GetDrives()
                     .Where(d => d.IsReady && d.DriveType == DriveType.Fixed)
                     .OrderBy(d => d.Name))
        {
            // A disk with no name of its own is just its letter: "C: ()" is what the reference
            // offered, and the empty brackets are noise in a list a person scans.
            string label = drive.VolumeLabel;
            yield return (string.IsNullOrWhiteSpace(label) ? drive.Name : $"{drive.Name} ({label})",
                drive.RootDirectory.FullName, IconGeometry.Drive);
        }
    }

    private async Task NavigateAsync(string path, bool remember)
    {
        if (string.IsNullOrEmpty(path)) return;

        var listing = await Task.Run(() => ReadFolders(path));

        if (listing is null)
        {
            _currentPath = string.Empty;
            _crumbsBox.Children.Clear();
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
        BuildCrumbs(path);
        _back.IsEnabled = _historyAt > 0;
        _back.Opacity = _historyAt > 0 ? 1 : 0.35;
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
        _cards.Clear();

        var needle = _filter.Text?.Trim() ?? string.Empty;
        _visible.Clear();

        foreach (var folder in _folders)
        {
            if (needle.Length > 0 &&
                folder.Name.IndexOf(needle, StringComparison.CurrentCultureIgnoreCase) < 0) continue;

            _visible.Add(folder);
            _foldersBox.Children.Add(FolderCard(folder));
        }

        if (_visible.Count == 0)
            _foldersBox.Children.Add(EmptyListing(needle.Length > 0));

        _foundCaption.Text = Folders(_visible.Count);
        _foundSub.Text = ListingSubtitle(needle.Length > 0);

        // The cards were built before the middle column had its final width, so the row is
        // measured again once the panel has been arranged.
        LayoutCards(_foldersBox.Bounds.Width, force: true);
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            LayoutCards(_foldersBox.Bounds.Width, force: true));
        UpdateSelectAll();
    }

    private static Control EmptyListing(bool filtered)
    {
        var ring = new Border
        {
            Width = 56,
            Height = 56,
            CornerRadius = new CornerRadius(17),
            Background = Ink.AccentTint,
            BorderBrush = Ink.AccentLine,
            BorderThickness = new Thickness(1),
            HorizontalAlignment = HorizontalAlignment.Center,
            Child = Icon.Stroke(IconGeometry.Folder, 25, Ink.Accent)
        };

        return new StackPanel
        {
            Spacing = 9,
            Margin = new Thickness(0, 40, 0, 0),
            Children =
            {
                ring,
                new TextBlock
                {
                    Text = filtered ? "Ничего не найдено" : "В этой папке нет подпапок",
                    FontSize = 13,
                    FontWeight = FontWeight.ExtraBold,
                    Foreground = Ink.Text,
                    HorizontalAlignment = HorizontalAlignment.Center
                },
                new TextBlock
                {
                    Text = filtered
                        ? "Попробуйте другое название или откройте папку выше."
                        : "Загляните в другую папку или начните с быстрого доступа слева.",
                    FontSize = 11.5,
                    LineHeight = 17,
                    TextWrapping = TextWrapping.Wrap,
                    TextAlignment = TextAlignment.Center,
                    MaxWidth = 300,
                    Foreground = Ink.Faint
                }
            }
        };
    }

    /// <summary>What the count beside the folder count says about their contents.</summary>
    private string ListingSubtitle(bool filtered)
    {
        if (_visible.Count == 0) return filtered ? "по этому запросу" : "откройте папку выше";
        if (_visible.Exists(f => f.Files == 0)) return "есть папки без фотографий";

        var sizes = _visible.Select(f => f.Files).Distinct().ToList();
        return sizes.Count == 1 ? $"по {sizes[0]} фото в каждой" : "разное число фотографий";
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
                _chosen.Add(new ChosenFolder(folder.FullPath, folder.Files));
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
    private Control FolderCard(FolderEntry folder)
    {
        bool chosen = _selected.Contains(folder.FullPath);
        int order = _selected.IndexOf(folder.FullPath) + 1;

        var mark = Icon.Stroke(IconGeometry.Folder, 42, chosen ? Ink.GoldDeep : Ink.Accent);
        mark.HorizontalAlignment = HorizontalAlignment.Center;
        mark.Opacity = 0.92;

        var cover = new Border
        {
            Height = 118,
            CornerRadius = new CornerRadius(11),
            Background = Ink.Muted,
            BorderBrush = Ink.Subtle,
            BorderThickness = new Thickness(1),
            ClipToBounds = true
        };

        var tick = new Border
        {
            Width = 21,
            Height = 21,
            CornerRadius = new CornerRadius(7),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 6, 6, 0),
            IsVisible = chosen,
            Background = GoldGradient(),
            Child = Icon.Stroke(IconGeometry.Tick, 12, Ink.OnPrimary)
        };

        var book = new Border
        {
            Height = 19,
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(7, 0),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(6),
            IsVisible = chosen,
            Background = Ink.Chip,
            Child = new TextBlock
            {
                Text = $"Книга {order}",
                FontSize = 9.5,
                FontWeight = FontWeight.ExtraBold,
                Foreground = Ink.ChipInk,
                VerticalAlignment = VerticalAlignment.Center
            }
        };

        // The arrow is the second target: it opens the folder, and it is on screen only while
        // the pointer is on the card, so a card that is merely passing under the hand shows
        // nothing it did not mean to.
        var open = new Button
        {
            Width = 24,
            Height = 24,
            Padding = new Thickness(0),
            CornerRadius = new CornerRadius(8),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 6, 6),
            Opacity = 0,
            Background = Ink.Chip,
            BorderThickness = new Thickness(0),
            Cursor = new Cursor(StandardCursorType.Hand),
            Content = Icon.Stroke(IconGeometry.ChevronRight, 13, Ink.ChipInk)
        };
        AutomationProperties.SetName(open, $"Открыть папку {folder.Name}");
        ToolTip.SetTip(open, folder.FullPath);
        open.Click += (_, _) => Navigate(folder.FullPath);
        open.PointerEntered += (_, _) => open.Background = Ink.Gold;
        open.PointerExited += (_, _) => open.Background = Ink.Chip;

        cover.Child = new Panel { Children = { mark, book, tick, open } };

        var metaMark = Icon.Stroke(IconGeometry.Image, 11, Ink.Faint);
        metaMark.Opacity = 0.8;

        var copy = new StackPanel
        {
            Spacing = 3,
            Margin = new Thickness(3, 9, 3, 0),
            Children =
            {
                new TextBlock
                {
                    Text = folder.Name,
                    FontSize = 12.5,
                    FontWeight = FontWeight.Bold,
                    Foreground = chosen ? Ink.Heading : Ink.Text,
                    TextTrimming = TextTrimming.CharacterEllipsis
                },
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 5,
                    VerticalAlignment = VerticalAlignment.Center,
                    Children =
                    {
                        metaMark,
                        new TextBlock
                        {
                            Text = folder.Files == 0 ? "нет фото" : Photos(folder.Files),
                            FontSize = 10.5,
                            Foreground = folder.Files == 0 ? Ink.DangerMark : Ink.Faint,
                            VerticalAlignment = VerticalAlignment.Center
                        }
                    }
                }
            }
        };

        var card = new Border
        {
            Width = _cardWidth,
            Margin = new Thickness(0, 0, 12, 12),
            CornerRadius = new CornerRadius(15),
            Padding = new Thickness(7, 7, 7, 9),
            Background = chosen ? Ink.AccentTint : Brushes.Transparent,
            BorderBrush = chosen ? Ink.ChosenLine : Ink.Border,
            BorderThickness = new Thickness(1),
            Cursor = new Cursor(StandardCursorType.Hand),
            Focusable = true,
            Child = new StackPanel { Children = { cover, copy } }
        };

        card.PointerEntered += (_, _) =>
        {
            open.Opacity = 1;
            if (!_selected.Contains(folder.FullPath)) card.BorderBrush = Ink.BorderStrong;
            card.RenderTransform = new TranslateTransform(0, -2);
        };

        card.PointerExited += (_, _) =>
        {
            open.Opacity = 0;
            card.BorderBrush = _selected.Contains(folder.FullPath) ? Ink.ChosenLine : Ink.Border;
            card.RenderTransform = null;
        };

        card.PointerPressed += (_, e) =>
        {
            // The arrow is inside the card, so its press travels up here: opening a folder
            // must not also choose it.
            if (IsWithin(e.Source, open)) return;
            Toggle(folder);
        };

        card.KeyDown += (_, e) =>
        {
            if (e.Key is not (Key.Enter or Key.Space)) return;
            Toggle(folder);
            e.Handled = true;
        };

        AutomationProperties.SetName(card, chosen
            ? $"Убрать {folder.Name} из проекта"
            : $"Выбрать папку {folder.Name}");

        if (folder.Files == 0)
            ToolTip.SetTip(card, "В папке нет фотографий — для книги она не подойдёт");

        _cards.Add((card, cover));
        return card;
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
            _chosen.Add(new ChosenFolder(folder.FullPath, folder.Files));
        }

        RefreshChosen();
        BuildFolderRows();
        _ = RevalidateAsync();
    }

    private void RefreshChosen()
    {
        _chosenBox.Children.Clear();
        _chosenCount.Text = Folders(_chosen.Count);

        for (int index = 0; index < _chosen.Count; index++)
            _chosenBox.Children.Add(ChosenRow(_chosen[index], index));

        if (_chosen.Count == 0)
            _chosenBox.Children.Add(EmptyChoice());

        _summaryFolders.Text = _chosen.Count.ToString();
        _summaryPhotos.Text = _chosen.Sum(c => c.Files).ToString();
        UpdateConfirm();
    }

    /// <summary>
    /// One folder in the right-hand column: a mark, its name, and what it will become.
    /// </summary>
    /// <remarks>
    /// The cross belongs to the row it removes and shows itself only where the pointer already
    /// is. A list of twenty folders with twenty crosses in it is a list of crosses, and the
    /// thing a person is looking for in that column is the names.
    /// </remarks>
    private Control ChosenRow(ChosenFolder entry, int index)
    {
        var cover = new Border
        {
            Width = 34,
            Height = 28,
            CornerRadius = new CornerRadius(6),
            Background = Ink.Muted,
            BorderBrush = Ink.Border,
            BorderThickness = new Thickness(1),
            VerticalAlignment = VerticalAlignment.Center,
            Child = Icon.Stroke(IconGeometry.Folder, 15, Ink.Accent)
        };

        var label = new StackPanel
        {
            Spacing = 1,
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                new TextBlock
                {
                    Text = entry.Name,
                    FontSize = 11.5,
                    FontWeight = FontWeight.Bold,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    Foreground = entry.HasError ? Ink.DangerStrong : Ink.Text,
                    VerticalAlignment = VerticalAlignment.Center
                },
                new TextBlock
                {
                    Text = entry.HasError
                        ? "папка не подходит"
                        : $"Книга {index + 1} · {Photos(entry.Files)}",
                    FontSize = 10,
                    Foreground = entry.HasError ? Ink.DangerMark : Ink.Faint
                }
            }
        };

        // The mark and the cross are both here. A folder that cannot be used is exactly the
        // one a person wants to get rid of, and showing its problem in place of the cross
        // left no way to do that at all.
        if (entry.HasError)
        {
            var warning = Icon.Stroke(IconGeometry.Warning, 12, Ink.DangerMark);
            warning.Margin = new Thickness(4, 0, 0, 0);
            label.Children.Add(warning);
        }

        var walk = new Button
        {
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            VerticalContentAlignment = VerticalAlignment.Center,
            Cursor = new Cursor(StandardCursorType.Hand),
            Content = label
        };
        AutomationProperties.SetName(walk, $"Перейти к папке {entry.Name}");
        ToolTip.SetTip(walk, entry.Path);

        // Clicking a chosen folder walks to the directory holding it. It used to remove it,
        // which is what an accidental click costs and what nobody can undo from the keyboard:
        // the folder has to be found again and ticked once more.
        walk.Click += (_, _) => WalkTo(entry.Path);

        var cross = Icon.Stroke(IconGeometry.Close, 12, Ink.Faint);
        var drop = new Button
        {
            Width = 22,
            Height = 22,
            Padding = new Thickness(0),
            CornerRadius = new CornerRadius(6),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            VerticalAlignment = VerticalAlignment.Center,
            Opacity = 0,
            Cursor = new Cursor(StandardCursorType.Hand),
            Content = cross
        };
        AutomationProperties.SetName(drop, $"Убрать {entry.Name} из выбранных");
        ToolTip.SetTip(drop, "Убрать из проекта");
        drop.Click += (_, _) =>
        {
            _selected.RemoveAll(p => string.Equals(p, entry.Path, StringComparison.OrdinalIgnoreCase));
            _chosen.Remove(entry);
            RefreshChosen();
            BuildFolderRows();
            _ = RevalidateAsync();
        };
        drop.PointerEntered += (_, _) =>
        {
            drop.Background = Ink.Danger;
            cross.Stroke = Ink.DangerStrong;
        };
        drop.PointerExited += (_, _) =>
        {
            drop.Background = Brushes.Transparent;
            cross.Stroke = Ink.Faint;
        };

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
        grid.Children.Add(cover);
        Grid.SetColumn(walk, 1);
        walk.Margin = new Thickness(9, 0, 4, 0);
        grid.Children.Add(walk);
        Grid.SetColumn(drop, 2);
        grid.Children.Add(drop);

        var row = new Border
        {
            Background = entry.HasError ? Ink.Danger : Brushes.Transparent,
            CornerRadius = new CornerRadius(9),
            Padding = new Thickness(4, 5),
            Child = grid
        };

        row.PointerEntered += (_, _) =>
        {
            drop.Opacity = 1;
            if (!entry.HasError) row.Background = Ink.Muted;
        };
        row.PointerExited += (_, _) =>
        {
            drop.Opacity = 0;
            row.Background = entry.HasError ? Ink.Danger : Brushes.Transparent;
        };

        return row;
    }

    private static Control EmptyChoice()
    {
        var ring = new Border
        {
            Width = 44,
            Height = 44,
            CornerRadius = new CornerRadius(14),
            Background = Ink.AccentTint,
            BorderBrush = Ink.AccentLine,
            BorderThickness = new Thickness(1),
            HorizontalAlignment = HorizontalAlignment.Center,
            Child = Icon.Stroke(IconGeometry.FolderPlus, 20, Ink.Accent)
        };

        return new StackPanel
        {
            Spacing = 8,
            Margin = new Thickness(8, 34, 8, 0),
            Children =
            {
                ring,
                new TextBlock
                {
                    Text = "Пока пусто",
                    FontSize = 12,
                    FontWeight = FontWeight.ExtraBold,
                    Foreground = Ink.Text,
                    HorizontalAlignment = HorizontalAlignment.Center
                },
                new TextBlock
                {
                    Text = "Отметьте папки слева — каждая станет отдельной книгой проекта.",
                    FontSize = 10.5,
                    LineHeight = 15,
                    TextWrapping = TextWrapping.Wrap,
                    TextAlignment = TextAlignment.Center,
                    MaxWidth = 190,
                    Foreground = Ink.Faint,
                    HorizontalAlignment = HorizontalAlignment.Center
                }
            }
        };
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

        UpdateConfirm();
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

    private static TextBlock Kicker(string text) => new()
    {
        Text = text.ToUpperInvariant(),
        FontSize = 9.5,
        FontWeight = FontWeight.ExtraBold,
        LetterSpacing = 1.4,
        Foreground = Ink.Faint
    };

    private static Control Dot() => new Border
    {
        Width = 3,
        Height = 3,
        CornerRadius = new CornerRadius(2),
        Background = Ink.Faint,
        Opacity = 0.7,
        VerticalAlignment = VerticalAlignment.Center
    };

    /// <summary>A quiet word that acts: "Очистить", and the like.</summary>
    private static Button Link(string text, Action onClick)
    {
        var button = new Button
        {
            Content = text,
            FontSize = 11,
            FontWeight = FontWeight.Bold,
            Foreground = Ink.Text2,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(7, 4),
            CornerRadius = new CornerRadius(6),
            Cursor = new Cursor(StandardCursorType.Hand)
        };

        button.Click += (_, _) => onClick();
        Hover(button, Ink.AccentTint, Brushes.Transparent);
        return button;
    }

    private static Button PlainButton(string text, bool accent, Action onClick)
    {
        var button = new Button
        {
            Content = text,
            Height = 42,
            Padding = new Thickness(20, 0),
            FontSize = 13.5,
            FontWeight = FontWeight.Bold,
            CornerRadius = new CornerRadius(999),
            Background = accent ? Ink.Gold : Brushes.Transparent,
            Foreground = accent ? Ink.OnPrimary : Ink.Text,
            BorderBrush = accent ? Brushes.Transparent : Ink.Border,
            BorderThickness = new Thickness(accent ? 0 : 1),
            Cursor = new Cursor(StandardCursorType.Hand)
        };

        button.Click += (_, _) => onClick();
        if (!accent) Hover(button, Ink.Surface, Brushes.Transparent);
        return button;
    }

    private static void Hover(Button button, IBrush over, IBrush away)
    {
        button.PointerEntered += (_, _) =>
        {
            if (button.IsEnabled) button.Background = over;
        };
        button.PointerExited += (_, _) => button.Background = away;
    }

    /// <summary>
    /// The gold of the filled controls, as the gradient the prototype paints its badge with.
    /// </summary>
    /// <remarks>
    /// Its two ends are read from the theme rather than written here: a gradient stop cannot
    /// take a brush, and a colour spelled out in this file would be the one thing on the panel
    /// that ignored the theme.
    /// </remarks>
    private static IBrush GoldGradient()
    {
        var gold = Ink.Gold;
        var deep = Ink.GoldDeep;

        if (gold is ISolidColorBrush from && deep is ISolidColorBrush to)
        {
            var brush = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative)
            };
            brush.GradientStops.Add(new GradientStop(from.Color, 0));
            brush.GradientStops.Add(new GradientStop(to.Color, 1));
            return brush;
        }

        return gold;
    }

    /// <summary>
    /// The path as steps rather than one long string, so any step can be pressed to walk back
    /// to it. The last step is where the panel is, and is not a button.
    /// </summary>
    private void BuildCrumbs(string path)
    {
        _crumbsBox.Children.Clear();
        if (string.IsNullOrEmpty(path)) return;

        var steps = new List<(string Label, string Path)>();
        var root = System.IO.Path.GetPathRoot(path);

        if (string.IsNullOrEmpty(root))
        {
            steps.Add((path, path));
        }
        else
        {
            var label = root.TrimEnd(
                System.IO.Path.DirectorySeparatorChar,
                System.IO.Path.AltDirectorySeparatorChar);
            steps.Add((string.IsNullOrEmpty(label) ? root : label, root));

            var walked = root;
            foreach (var segment in path[root.Length..].Split(
                         new[]
                         {
                             System.IO.Path.DirectorySeparatorChar,
                             System.IO.Path.AltDirectorySeparatorChar
                         },
                         StringSplitOptions.RemoveEmptyEntries))
            {
                walked = System.IO.Path.Combine(walked, segment);
                steps.Add((segment, walked));
            }
        }

        for (int i = 0; i < steps.Count; i++)
        {
            if (i > 0) _crumbsBox.Children.Add(CrumbSeparator());

            if (i == steps.Count - 1)
            {
                _crumbsBox.Children.Add(new TextBlock
                {
                    Text = steps[i].Label,
                    FontSize = 11,
                    FontWeight = FontWeight.Bold,
                    Foreground = Ink.Text,
                    MaxWidth = 210,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(4, 0, 4, 0)
                });
                continue;
            }

            var step = steps[i];
            var button = new Button
            {
                Content = new TextBlock
                {
                    Text = step.Label,
                    FontSize = 11,
                    FontWeight = FontWeight.SemiBold,
                    MaxWidth = 170,
                    TextTrimming = TextTrimming.CharacterEllipsis
                },
                Tag = step.Path,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(6, 4),
                CornerRadius = new CornerRadius(7),
                Cursor = new Cursor(StandardCursorType.Hand),
                VerticalAlignment = VerticalAlignment.Center
            };
            AutomationProperties.SetName(button, step.Label);
            button.Click += (_, _) => Navigate(step.Path);
            Hover(button, Ink.Muted, Brushes.Transparent);
            _crumbsBox.Children.Add(button);
        }
    }

    private static Control CrumbSeparator() => new TextBlock
    {
        Text = "/",
        FontSize = 10,
        Foreground = Ink.Faint,
        Opacity = 0.5,
        VerticalAlignment = VerticalAlignment.Center
    };

    /// <summary>
    /// How wide a card is drawn: as many across as fit at the prototype's 172, and then the
    /// row divided evenly.
    /// </summary>
    /// <remarks>
    /// The panel is resizable, and a card with a fixed width leaves half a row empty at the
    /// widest window and asks for a horizontal scroll at the narrowest.
    /// </remarks>
    private void LayoutCards(double width, bool force = false)
    {
        if (_cards.Count == 0) return;

        // The viewport is the number that does not depend on these cards: the wrap panel's own
        // width is the width of its widest row, and asking it would make the row decide itself.
        if (_folderViewport is { } viewport && viewport.Bounds.Width > 20)
            width = viewport.Bounds.Width - 10;

        if (width <= 0) return;

        const double gap = 12;
        const double least = 172;

        // Every card carries the gap on its own right edge, the last one included, so the row
        // is columns x (card + gap) and not what is left between the cards.
        int columns = Math.Max(1, (int)(width / (least + gap)));
        double card = Math.Floor(width / columns) - gap - 1;

        while (card < least && columns > 1)
        {
            columns--;
            card = Math.Floor(width / columns) - gap - 1;
        }

        card = Math.Max(least, card);
        if (!force && Math.Abs(card - _cardWidth) < 0.5) return;

        _cardWidth = card;
        foreach (var (border, cover) in _cards)
        {
            border.Width = card;
            cover.Height = Math.Round((card - 14) * 0.72);
        }
    }

    /// <summary>Whether a pointer's source sits inside a control that has its own answer.</summary>
    private static bool IsWithin(object? source, Visual ancestor)
    {
        var visual = source as Visual;
        while (visual is not null)
        {
            if (ReferenceEquals(visual, ancestor)) return true;
            visual = visual.GetVisualParent();
        }

        return false;
    }

    /// <summary>"1 папка", "4 папки", "11 папок".</summary>
    private static string Folders(int n)
    {
        string word = (n % 100) is >= 11 and <= 14 ? "папок"
            : (n % 10) == 1 ? "папка"
            : (n % 10) is >= 2 and <= 4 ? "папки"
            : "папок";

        return $"{n} {word}";
    }

    /// <summary>"9 фото" - the word does not change with the number.</summary>
    private static string Photos(int n) => $"{n} фото";

    /// <summary>Gives every folder back, leaving the panel as it was found.</summary>
    private void ClearChosen()
    {
        _selected.Clear();
        _chosen.Clear();
        RefreshChosen();
        BuildFolderRows();
        _ = RevalidateAsync();
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

        public ChosenFolder(string path, int files)
        {
            Path = path;
            Files = files;
        }

        public string Path { get; }
        public string Name => System.IO.Path.GetFileName(Path);

        /// <summary>How many photographs the folder holds, for the totals in the rail.</summary>
        public int Files { get; }

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

        public static Geometry ArrowRight =>
            Geometry.Parse("M 9.5 5.5 L 16 12 L 9.5 18.5 M 4 12 H 15.6");

        public static Geometry ChevronRight =>
            Geometry.Parse("M 9.5 5.5 L 16 12 L 9.5 18.5");

        public static Geometry FolderPlus =>
            Geometry.Parse("M 3 7.2 A 1.4 1.4 0 0 1 4.4 5.8 H 9.2 L 11.4 8 H 19.6 A 1.4 1.4 0 0 1 21 9.4 V 12.4 M 3 7.2 V 17.6 A 1.4 1.4 0 0 1 4.4 19 H 12 M 18 14.4 V 20.8 M 14.8 17.6 H 21.2");

        public static Geometry Search =>
            Geometry.Parse("M 10.8 4.4 a 6.4 6.4 0 1 0 0 12.8 a 6.4 6.4 0 1 0 0 -12.8 M 15.6 15.6 L 20 20");

        public static Geometry Info =>
            Geometry.Parse("M 12 3.4 a 8.6 8.6 0 1 0 0 17.2 a 8.6 8.6 0 1 0 0 -17.2 M 12 11 V 16.4 M 12 7.9 H 12.01");

        public static Geometry Lock =>
            Geometry.Parse("M 5.5 10.5 H 18.5 V 20 H 5.5 Z M 8.4 10.5 V 7.6 a 3.6 3.6 0 0 1 7.2 0 V 10.5");

        public static Geometry Image =>
            Geometry.Parse("M 4 5.5 H 20 V 18.5 H 4 Z M 4 15 L 9 10.4 L 13 14 L 16.5 11 L 20 14 M 8.4 9.2 a 1 1 0 1 0 0 -2 a 1 1 0 0 0 0 2");

        public static Geometry Monitor =>
            Geometry.Parse("M 3.5 5 H 20.5 V 15.6 H 3.5 Z M 9 19 H 15 M 12 15.6 V 19");

        public static Geometry Document =>
            Geometry.Parse("M 6 3.6 H 14 L 18.4 8 V 20.4 H 6 Z M 14 3.6 V 8 H 18.4");

        public static Geometry Download =>
            Geometry.Parse("M 12 4 V 14.6 M 7.8 10.6 L 12 14.8 L 16.2 10.6 M 4.4 19.4 H 19.6");
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
        public static IBrush Surface => ThemeResources.Brush("Surface");
        public static IBrush Muted => ThemeResources.Brush("SurfaceMuted");
        public static IBrush Border => ThemeResources.Brush("BorderDefault");
        public static IBrush BorderStrong => ThemeResources.Brush("BorderStrong");
        public static IBrush Faint => ThemeResources.Brush("TextFaint");
        public static IBrush Heading => ThemeResources.Brush("TextStrong");
        public static IBrush Text => ThemeResources.Brush("TextDefault");
        public static IBrush Text2 => ThemeResources.Brush("TextMuted");
        public static IBrush Accent => ThemeResources.Brush("Accent");

        public static IBrush Primary => ThemeResources.Brush("Gold");

        public static IBrush OnPrimary => ThemeResources.Brush("OnAccent");
        public static IBrush AccentTint => ThemeResources.Brush("Brand50");
        public static IBrush AccentLine => ThemeResources.Brush("Brand100");
        public static IBrush Warn => ThemeResources.Brush("Amber500");

        public static IBrush Modal => ThemeResources.Brush("SurfaceModal");
        public static IBrush Subtle => ThemeResources.Brush("BorderSubtle");
        public static IBrush Gold => ThemeResources.Brush("Gold");
        public static IBrush GoldDeep => ThemeResources.Brush("GoldDeep");
        public static IBrush GoldHover => ThemeResources.Brush("GoldHover");
        public static IBrush GoldPressed => ThemeResources.Brush("GoldPressed");
        public static IBrush Ok => ThemeResources.Brush("Emerald600");

        /// <summary>The chip that sits on a folder's picture and names its book.</summary>
        public static IBrush Chip => ThemeResources.Brush("OverlayChipFill");

        public static IBrush ChipInk => ThemeResources.Brush("OverlayChipInk");

        /// <summary>The rim of a chosen card: gold, and not just a warmer hairline.</summary>
        public static IBrush ChosenLine => ThemeResources.Brush("BorderAccentHover");

        // Named rather than written where they are used: `Brush` on its own inside a control
        // resolves to the control's own protected property of that name, not to the colour
        // helper, and the mistake reads as a method that cannot be called.
        public static IBrush Danger => ThemeResources.Brush("Red50");
        public static IBrush DangerLine => ThemeResources.Brush("Red200");
        public static IBrush DangerInk => ThemeResources.Brush("Red700");
        public static IBrush DangerStrong => ThemeResources.Brush("Red700");
        public static IBrush DangerMark => ThemeResources.Brush("Red600");
    }
}
