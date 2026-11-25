using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.VisualTree;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
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
    /// The panel takes the space the host gives it, and the host gives it the window.
    /// </summary>
    /// <remarks>
    /// It used to be clamped to 1112 by 740, which is one laptop's size and nobody else's. On
    /// a wide screen the panel sat in the middle of the page at the width the prototype drew
    /// for a 1440 screen, and every column inside it was narrower than it had room to be. The
    /// clamp is gone: the sheet's own margin keeps it off the edges, and the columns take the
    /// rest - the folder row simply fits more cards across.
    /// </remarks>

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

    /// <summary>The same folders drawn one to a line, which is the other view of the toolbar.</summary>
    private readonly StackPanel _listBox = new() { Spacing = 5 };

    /// <summary>The middle of the column when there is nothing to show.</summary>
    private readonly ContentControl _emptyHost = new()
    {
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
        IsVisible = false
    };

    private readonly Button _viewGrid = new();
    private readonly Button _viewList = new();
    private Glyph _viewGridGlyph = null!;
    private Glyph _viewListGlyph = null!;
    private Border _viewGridChip = null!;
    private Border _viewListChip = null!;

    /// <summary>
    /// Whether the folders are drawn one to a line rather than as a row of cards.
    /// </summary>
    /// <remarks>
    /// The cards, unless the person chose the list: with the first photographs of a folder on
    /// its cover, the cards say what an order holds before a single name is read. The choice
    /// is kept between runs, so a panel that was switched stays switched.
    /// </remarks>
    private bool _listView;

    private readonly StackPanel _chosenBox = new() { Spacing = 2 };
    private readonly StackPanel _crumbsBox = new()
    {
        Orientation = Orientation.Horizontal,
        VerticalAlignment = VerticalAlignment.Center
    };
    private readonly TextBlock _chosenCount = new();
    private readonly TextBlock _foundCaption = new();
    private readonly TextBlock _selectAllLabel = new();
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
    private Glyph _okArrow = null!;

    /// <summary>The cards of the current listing, so the row can be re-measured on a resize.</summary>
    private readonly List<(Border Card, Border Cover)> _cards = new();

    /// <summary>
    /// Every card and row on screen, by path, so a choice repaints them instead of rebuilding.
    /// </summary>
    /// <remarks>
    /// Rebuilding the listing for one tick was what made the covers blink even with their
    /// photographs remembered: the pictures were the same, the controls were new. A choice
    /// changes a tick, a plate and a colour, and those are cheaper to write than to recreate.
    /// </remarks>
    private readonly Dictionary<string, FolderView> _folderViews =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The covers of the current listing that can hold photographs.</summary>
    private readonly List<PreviewTarget> _previewTargets = new();

    /// <summary>
    /// The previews already decoded for this listing, by folder.
    /// </summary>
    /// <remarks>
    /// Choosing a folder rebuilds the listing, and a rebuilt card used to come back with an
    /// empty cover until its preview arrived again - a blink on every tick. The pictures are
    /// kept here so a card is built with its photographs already on it. The cap matters: the
    /// cache behind this one is bounded, and a view that held every path it ever saw would
    /// keep pictures the cache has already released.
    /// </remarks>
    private readonly Dictionary<string, IReadOnlyList<Bitmap>> _loadedPreviews =
        new(StringComparer.OrdinalIgnoreCase);

    private const int RememberedFolders = 200;

    /// <summary>Cancels the previews of a listing that is no longer on screen.</summary>
    /// <remarks>
    /// Not disposed: a request may still be waiting inside the cache when the listing it
    /// belonged to is replaced, and disposing the source under its token is an error rather
    /// than a cleanup. A cancelled source is collected like any other object.
    /// </remarks>
    private CancellationTokenSource? _previewLoad;

    /// <summary>Whether covers show photographs. On unless the switch in the tray says no.</summary>
    private bool _previewsOn = true;

    private readonly Button _previewButton = new();
    private Glyph _previewGlyph = null!;
    private Border _previewChip = null!;

    /// <summary>The places in the left column, so the one being shown can be marked.</summary>
    private readonly List<PlaceRowView> _placeRows = new();

    /// <summary>The project plate's bar: the share of the result that is being added now.</summary>
    private Grid? _projectTrack;

    private readonly Border _projectFill = new();

    /// <summary>How many folders the project already had when the panel opened.</summary>
    private int _existingCount;

    /// <summary>
    /// The project's own folders, by path, so the plate's bar counts only what this visit adds.
    /// </summary>
    /// <remarks>
    /// The panel opens with those folders already ticked. Counting the ticked list instead
    /// made the bar claim that half of an untouched project was new.
    /// </remarks>
    private readonly HashSet<string> _existing = new(StringComparer.OrdinalIgnoreCase);

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
                _existing.Add(folder);
                _existingCount++;
            }
        }

        _listView = PickerViewPreference.LoadList();
        _previewsOn = PickerViewPreference.LoadPreviews();

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

    // ------------------------------------------------------------------ the sheet --

    private Control Build()
    {
        ConfigureConfirm();

        var root = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto") };

        // A press anywhere but the search box takes the caret out of it. The insertion point
        // stayed in the field after the pointer had moved on to the empty page around the
        // cards, and a box still holding the caret reads as a box still waiting for words.
        AddHandler(PointerPressedEvent, OnSheetPressed, RoutingStrategies.Tunnel);

        var header = HeaderBand();
        Grid.SetRow(header, 0);
        root.Children.Add(header);

        var body = new Grid { ColumnDefinitions = new ColumnDefinitions("214,*,286") };

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
            BorderBrush = Ink.ModalEdge,
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
            Background = Gradient("PickerBadgeFill"),
            BoxShadow = ThemeResources.Shadow("PickerBadgeShadow"),
            VerticalAlignment = VerticalAlignment.Center,
            Child = Icon.Stroke(IconGeometry.FolderPlus, 22, Ink.BadgeInk)
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
                title,
                new TextBlock
                {
                    Text = "Каждая папка — отдельная книга. Отметьте нужные — остальное программа сделает сама.",
                    FontSize = 12,
                    Foreground = Ink.Text2
                }
            }
        };

        // The program's own cross, not one made for this panel: 36 square, a tenth of a round,
        // and no plate at all until the pointer is on it. The ringed circle it used to be was a
        // different button from the one every other panel closes with.
        var closeGlyph = Icon.Stroke(IconGeometry.Close, 18, Ink.Text2);

        var close = new Button
        {
            Classes = { "modalClose" },
            Content = closeGlyph,
            VerticalAlignment = VerticalAlignment.Center
        };
        AutomationProperties.SetName(close, "Закрыть");
        ToolTip.SetTip(close, "Закрыть");
        close.Click += (_, _) => Leave();

        // The wash under the pointer is the shared style's; only the mark follows it here,
        // because a shape's stroke is not a property a button style can reach.
        close.PointerEntered += (_, _) => closeGlyph.Stroke = Ink.Text;
        close.PointerExited += (_, _) => closeGlyph.Stroke = Ink.Text2;

        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
        row.Children.Add(badge);
        Grid.SetColumn(copy, 1);
        copy.Margin = new Thickness(15, 0, 0, 0);
        row.Children.Add(copy);
        Grid.SetColumn(close, 2);
        row.Children.Add(close);

        var content = new Grid { Margin = new Thickness(24, 20) };
        content.Children.Add(row);

        // Two gradients, as the prototype stacks them: a warm wash down the whole band, and
        // the light of a lamp in its top-left corner. One brush per border, so the glow is its
        // own layer behind the words.
        return new Border
        {
            Background = Gradient("PickerHeadWash"),
            BorderBrush = Ink.Border,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = new Grid
            {
                Children =
                {
                    new Border { Background = Gradient("PickerHeadGlow"), IsHitTestVisible = false },
                    content
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
            Background = Ink.SunkenSoft,
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

    private Control PlaceRow(string name, string path, Geometry shape)
    {
        bool exists = Directory.Exists(path);

        var mark = Icon.Stroke(shape, 17, Ink.Faint);
        mark.StrokeThickness = 1.45;

        var label = new TextBlock
        {
            Text = name,
            FontSize = 12.5,
            FontWeight = FontWeight.SemiBold,
            Foreground = exists ? Ink.Text2 : Ink.Faint,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center
        };

        // "ghost" is the program's box-less button: its template paints nothing under the
        // pointer. Without it the platform's own wash arrives on hover, which is the grey
        // rectangle that appeared behind a folder's name in the chosen column.
        var button = new Button
        {
            Classes = { "ghost" },
            Background = Brushes.Transparent,
            BorderBrush = Brushes.Transparent,
            BorderThickness = new Thickness(1),
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
                Children = { mark, label }
            }
        };
        button.Transitions = Fade();
        mark.Transitions = Fade();
        label.Transitions = Fade();

        if (exists)
        {
            AutomationProperties.SetName(button, name);
            button.Click += (_, _) => Navigate(path);

            // The pointer answers on its own row and the row under it is the one being shown:
            // a place that is already current has nothing to say to a passing pointer.
            button.PointerEntered += (_, _) =>
            {
                if (IsCurrentPlace(path)) return;
                button.Background = Ink.SunkenSoft;
                label.Foreground = Ink.Text;
                mark.Stroke = Ink.Text2;
            };
            button.PointerExited += (_, _) => PaintPlaces();
        }

        // The gold mark of the place being shown, drawn where the prototype draws it: a short
        // bar on the row's left edge, shorter than the row and glowing.
        var bar = new Border
        {
            Width = 2.5,
            CornerRadius = new CornerRadius(3),
            Background = Ink.Accent,
            BoxShadow = ThemeResources.Shadow("PickerBarGlow"),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Stretch,
            Margin = new Thickness(0, 9),
            IsVisible = false,
            IsHitTestVisible = false
        };

        _placeRows.Add(new PlaceRowView(path, button, mark, label, bar));

        return new Grid { Children = { button, bar } };
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

        // The bar is not decoration and it is not a share of an invented total: it is how much
        // of the project this visit is adding. With nothing ticked there is nothing to add and
        // the bar is empty; tick four of eight and it shows a third.
        _projectFill.Background = Gradient("PickerBarFill");
        _projectFill.CornerRadius = new CornerRadius(99);

        var track = new Grid
        {
            Height = 3,
            Margin = new Thickness(0, 10, 0, 8),
            Background = Ink.BarTrack,
            ClipToBounds = true,
            ColumnDefinitions = new ColumnDefinitions("0*,1*"),
            Children = { _projectFill }
        };
        _projectTrack = track;

        return new Border
        {
            CornerRadius = new CornerRadius(12),
            Background = Gradient("PickerProjectFill"),
            BorderBrush = Ink.ProjectRing,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(12),
            Margin = new Thickness(0, 2, 0, 0),
            Child = new StackPanel
            {
                Children =
                {
                    row,
                    track,
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
            Background = Ink.CardFill,
            BorderBrush = Ink.SumRing,
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

        var tools = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { ViewsToggle(), search }
        };

        var toolbar = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
        toolbar.Children.Add(_back);
        Grid.SetColumn(_crumbsBox, 1);
        _crumbsBox.Margin = new Thickness(8, 0, 8, 0);
        toolbar.Children.Add(_crumbsBox);
        Grid.SetColumn(tools, 2);
        toolbar.Children.Add(tools);

        _foundCaption.FontSize = 11.5;
        _foundCaption.FontWeight = FontWeight.Bold;
        _foundCaption.Foreground = Ink.Heading;
        _foundCaption.VerticalAlignment = VerticalAlignment.Center;

        // The count, and the one press that takes everything under it. The line that used to
        // stand between them said what the folders hold, which every card says for itself.
        ConfigureSelectAll();

        var context = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Margin = new Thickness(2, 10, 2, 4),
            VerticalAlignment = VerticalAlignment.Center,
            Children = { _foundCaption, Dot(), _selectAll }
        };

        _foldersBox.SizeChanged += (_, e) => LayoutCards(e.NewSize.Width);

        var scroll = new ScrollViewer
        {
            Content = new Panel { Children = { _foldersBox, _listBox } },
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Padding = new Thickness(2, 2, 6, 16)
        };
        _folderViewport = scroll;
        scroll.SizeChanged += (_, _) => LayoutCards(0);
        scroll.ScrollChanged += (_, _) => LoadVisiblePreviews();

        // The listing, over it the empty answer, and over both the report of a broken choice.
        // The empty answer is not a child of the listing: a column that is empty has no
        // content to centre the words in, and that is why they sat off to one side. The
        // report is a layer rather than a row of its own: a banner that arrives by pushing the
        // cards up moves the card under a pointer that is already on its way to it.
        var stage = new Grid { Children = { scroll, _emptyHost, MessageStrip() } };

        var column = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,Auto,*"),
            Margin = new Thickness(18, 14, 18, 0)
        };
        column.Children.Add(toolbar);
        Grid.SetRow(context, 1);
        column.Children.Add(context);
        Grid.SetRow(stage, 2);
        column.Children.Add(stage);

        return column;
    }

    /// <summary>
    /// The prototype's two view buttons: the same folders as cards, or as lines.
    /// </summary>
    private Control ViewsToggle()
    {
        ConfigureView(_viewGrid, "Плиткой", IconGeometry.ViewGrid, list: false);
        ConfigureView(_viewList, "Списком", IconGeometry.ViewList, list: true);
        PaintViews();

        return new Border
        {
            CornerRadius = new CornerRadius(9),
            Background = Ink.Sunken,
            BoxShadow = ThemeResources.Shadow("PickerViewTrayRing"),
            Padding = new Thickness(3),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 2,
                Children =
                {
                    _viewGrid,
                    _viewList,

                    // A hairline, because the third switch is not a third view: it turns the
                    // pictures on the covers on and off.
                    new Border
                    {
                        Width = 1,
                        Height = 16,
                        Margin = new Thickness(2, 0, 2, 0),
                        Background = Ink.Subtle,
                        VerticalAlignment = VerticalAlignment.Center
                    },
                    PreviewsToggle()
                }
            }
        };
    }

    /// <summary>
    /// One of the two view buttons: a chip that is lit while its view is the one on screen.
    /// </summary>
    /// <remarks>
    /// The chip is a border of its own inside the button, because the design draws the chosen
    /// view as a small raised card and the platform's button template has no shadow to give
    /// it - nor anything to cancel the grey it paints under the pointer. The button itself is
    /// the shared ghost, which draws no box at all, so the only thing that ever changes is the
    /// chip: its fill, its shadow, and the colour of the mark.
    /// </remarks>
    private void ConfigureView(Button button, string name, Geometry shape, bool list)
    {
        var glyph = Icon.Stroke(shape, 14, Ink.Faint);

        var chip = new Border
        {
            Width = 26,
            Height = 26,
            CornerRadius = new CornerRadius(7),
            Background = Brushes.Transparent,
            Child = glyph
        };
        chip.Transitions = new Transitions
        {
            new BrushTransition
            {
                Property = Border.BackgroundProperty,
                Duration = TimeSpan.FromMilliseconds(140)
            },
            new BoxShadowsTransition
            {
                Property = Border.BoxShadowProperty,
                Duration = TimeSpan.FromMilliseconds(140)
            }
        };

        button.Classes.Add("ghost");
        button.Width = 26;
        button.Height = 26;
        button.Padding = new Thickness(0);
        button.CornerRadius = new CornerRadius(7);
        button.Background = Brushes.Transparent;
        button.BorderThickness = new Thickness(0);
        button.Cursor = new Cursor(StandardCursorType.Hand);
        button.VerticalAlignment = VerticalAlignment.Center;
        button.Content = chip;
        AutomationProperties.SetName(button, name);
        ToolTip.SetTip(button, name);
        button.Click += (_, _) => ShowList(list);
        button.PointerEntered += (_, _) =>
        {
            if (_listView != list) glyph.Stroke = Ink.Text2;
        };
        button.PointerExited += (_, _) => PaintViews();

        if (list)
        {
            _viewListGlyph = glyph;
            _viewListChip = chip;
        }
        else
        {
            _viewGridGlyph = glyph;
            _viewGridChip = chip;
        }
    }

    private void PaintViews()
    {
        PaintView(_viewGridChip, _viewGridGlyph, !_listView);
        PaintView(_viewListChip, _viewListGlyph, _listView);
    }

    private static void PaintView(Border chip, Glyph glyph, bool active)
    {
        chip.Background = active ? ThemeResources.Brush("PickerViewActiveFill") : Brushes.Transparent;
        chip.BoxShadow = active ? ThemeResources.Shadow("PickerViewActiveShadow") : default;
        glyph.Stroke = active ? Ink.Heading : Ink.Faint;
    }

    /// <summary>Switches the drawing of the listing, remembers it, and draws it again.</summary>
    private void ShowList(bool list)
    {
        if (_listView == list) return;
        _listView = list;
        PickerViewPreference.Save(list);
        PaintViews();
        BuildFolderRows();
    }

    /// <summary>
    /// The tray's third switch: whether covers show the first photographs of their folder.
    /// </summary>
    /// <remarks>
    /// It is a setting rather than a view. On a network drive every cover is a read across the
    /// wire, and a client who keeps their order there must be able to turn that off; the
    /// answer is kept beside the choice of view.
    /// </remarks>
    private Control PreviewsToggle()
    {
        _previewGlyph = Icon.Stroke(IconGeometry.Image, 14, Ink.Faint);
        _previewChip = new Border
        {
            Width = 26,
            Height = 26,
            CornerRadius = new CornerRadius(7),
            Background = Brushes.Transparent,
            Child = _previewGlyph
        };

        _previewButton.Classes.Add("ghost");
        _previewButton.Width = 26;
        _previewButton.Height = 26;
        _previewButton.Padding = new Thickness(0);
        _previewButton.CornerRadius = new CornerRadius(7);
        _previewButton.Background = Brushes.Transparent;
        _previewButton.BorderThickness = new Thickness(0);
        _previewButton.Cursor = new Cursor(StandardCursorType.Hand);
        _previewButton.VerticalAlignment = VerticalAlignment.Center;
        _previewButton.Content = _previewChip;
        _previewButton.Click += (_, _) => ShowPreviews(!_previewsOn);
        _previewButton.PointerEntered += (_, _) =>
        {
            if (!_previewsOn) _previewGlyph.Stroke = Ink.Text2;
        };
        _previewButton.PointerExited += (_, _) => PaintPreviews();

        PaintPreviews();
        return _previewButton;
    }

    private void PaintPreviews()
    {
        _previewChip.Background = _previewsOn
            ? ThemeResources.Brush("PickerViewActiveFill")
            : Brushes.Transparent;
        _previewChip.BoxShadow = _previewsOn
            ? ThemeResources.Shadow("PickerViewActiveShadow")
            : default;
        _previewGlyph.Stroke = _previewsOn ? Ink.Heading : Ink.Faint;

        string tip = _previewsOn ? "Скрыть превью папок" : "Показывать превью папок";
        AutomationProperties.SetName(_previewButton, tip);
        ToolTip.SetTip(_previewButton, tip);
    }

    private void ShowPreviews(bool on)
    {
        if (_previewsOn == on) return;
        _previewsOn = on;
        PickerViewPreference.SavePreviews(on);
        PaintPreviews();
        BuildFolderRows();
    }

    private void ConfigureBack()
    {
        _back.Width = 32;
        _back.Height = 32;
        _back.Padding = new Thickness(0);
        _back.CornerRadius = new CornerRadius(9);
        _back.Background = Ink.Sunken;
        _back.Foreground = Ink.Text;
        _back.BorderBrush = Ink.PlaceActive;
        _back.BorderThickness = new Thickness(1);
        _back.Cursor = new Cursor(StandardCursorType.Hand);
        var glyph = Icon.Stroke(IconGeometry.ArrowLeft, 15, Ink.Text2);
        _back.Content = glyph;
        _back.VerticalAlignment = VerticalAlignment.Center;
        _back.Transitions = Fade();
        AutomationProperties.SetName(_back, "Назад");
        ToolTip.SetTip(_back, "Назад");
        _back.Click += (_, _) => GoBack();
        _back.Opacity = 0.32;
        _back.PointerEntered += (_, _) =>
        {
            if (!_back.IsEnabled) return;
            _back.Background = Ink.PlaceActive;
            glyph.Stroke = Ink.Heading;
        };
        _back.PointerExited += (_, _) =>
        {
            _back.Background = Ink.Sunken;
            glyph.Stroke = Ink.Text2;
        };
    }

    private Control SearchField()
    {
        _filter.Watermark = "Найти папку";
        _filter.FontSize = 12;
        _filter.Height = 34;
        _filter.MinHeight = 0;
        _filter.Width = 176;
        _filter.VerticalAlignment = VerticalAlignment.Center;
        // The words sit in the middle of the box, not along its top edge: the box is taller than
        // the line, and a field whose text is pinned up reads as one that is half empty.
        _filter.VerticalContentAlignment = VerticalAlignment.Center;
        _filter.CornerRadius = new CornerRadius(9);
        _filter.Background = Ink.Sunken;
        _filter.BorderBrush = Ink.PlaceActive;
        _filter.BorderThickness = new Thickness(1);
        _filter.Padding = new Thickness(30, 0, 28, 0);
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
        // The ghost template draws no box under the pointer, which is what the design asks
        // for - but its shared style also hands any label inside it the quiet ink, and that is
        // what turned this button grey. The words are a text block of their own, carrying the
        // accent themselves, so the button can stay ghost and the words stay gold.
        _selectAll.Classes.Add("ghost");
        _selectAllLabel.Text = "Выбрать все";
        _selectAllLabel.FontSize = 11;
        _selectAllLabel.FontWeight = FontWeight.Bold;
        _selectAllLabel.Foreground = Ink.Accent;
        _selectAll.Content = _selectAllLabel;
        _selectAll.Padding = new Thickness(8, 4);
        _selectAll.FontSize = 11;
        _selectAll.FontWeight = FontWeight.Bold;
        _selectAll.CornerRadius = new CornerRadius(7);
        _selectAll.Background = Brushes.Transparent;
        _selectAll.Foreground = Ink.Accent;
        _selectAll.BorderThickness = new Thickness(0);
        _selectAll.Cursor = new Cursor(StandardCursorType.Hand);
        _selectAll.VerticalAlignment = VerticalAlignment.Center;
        _selectAll.Transitions = Fade();
        _selectAll.Click += (_, _) => ToggleEveryVisible();
        _selectAll.PointerEntered += (_, _) =>
        {
            if (_selectAll.IsEnabled) _selectAll.Background = Ink.GoldTint;
        };
        _selectAll.PointerExited += (_, _) => _selectAll.Background = Brushes.Transparent;
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
            Margin = new Thickness(-4, 8, -4, 0),
            Padding = new Thickness(4, 0)
        };
        Grid.SetRow(scroll, 1);

        var sum = new Border
        {
            BorderBrush = Ink.Border,
            BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(0, 11, 0, 0),
            Child = SummaryRow()
        };
        Grid.SetRow(sum, 2);

        var column = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*,Auto"),
            Children = { headBorder, scroll, sum }
        };

        return new Border
        {
            Background = Ink.SunkenFaint,
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
            Children = { folders }
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
            Background = Ink.SumFill,
            BorderBrush = Ink.SumRing,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(9, 7),
            Child = new StackPanel { Children = { Kicker(label), value } }
        };
    }

    /// <summary>
    /// The report of a broken choice - and nothing at all when the choice is sound.
    /// </summary>
    /// <remarks>
    /// It is a layer over the listing rather than a row beside it, and it is only in the sheet
    /// when there is something to say. Standing there saying "Папки готовы к загрузке" was a
    /// box of words about the ordinary case; a fixed height on top of that meant a long error
    /// was cut off and scrolled inside its own box. The layer now takes the height of the
    /// words it holds, and the words are the ones that broke the choice.
    /// </remarks>
    private Control MessageStrip()
    {
        _logText.FontSize = 12;
        _logText.Foreground = Ink.DangerInk;
        _logText.TextWrapping = TextWrapping.Wrap;
        _logText.LineHeight = 17;
        _logText.VerticalAlignment = VerticalAlignment.Center;

        // Opaque, not a wash: the block floats over the listing, and a translucent fill let
        // the folder names behind it read through the sentence that reports the mistake.
        _logBox.Background = Ink.DangerPaper;
        _logBox.BorderBrush = Ink.DangerPaperRing;
        _logBox.BorderThickness = new Thickness(1);
        _logBox.CornerRadius = new CornerRadius(10);
        _logBox.Padding = new Thickness(12, 10);
        _logBox.Margin = new Thickness(2, 0, 8, 10);
        _logBox.VerticalAlignment = VerticalAlignment.Bottom;
        _logBox.BoxShadow = ThemeResources.Shadow("ShadowSm");
        _logBox.IsVisible = false;
        _logBox.Child = _logText;

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
                Icon.Stroke(IconGeometry.Lock, 13, Ink.Teal),
                new TextBlock
                {
                    Text = "Исходные файлы не изменяются",
                    FontSize = 11,
                    Foreground = Ink.Faint,
                    VerticalAlignment = VerticalAlignment.Center
                }
            }
        };

        // The two buttons of every panel in this program, not a pair made for this one: the same
        // quiet one that answers "Отмена", and the same filled one whose cap, frame, shadow and
        // one-pixel lift under the pointer are defined once in Styles/Cards.axaml. Built here by
        // hand, they had no lift, a shadow wrapped round them by an outer border, and a fill
        // that was gold in both themes - a lamp on a page that is supposed to be lit by the sun.
        var cancel = new Button
        {
            Classes = { "ghost" },
            Content = "Отмена",
            Height = 42,
            Padding = new Thickness(20, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        cancel.Click += (_, _) => Leave();

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { cancel, _ok }
        };

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        grid.Children.Add(note);
        Grid.SetColumn(actions, 1);
        grid.Children.Add(actions);

        return new Border
        {
            Background = Ink.SunkenSoft,
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
        _okLabel.VerticalAlignment = VerticalAlignment.Center;

        _okArrow = Icon.Stroke(IconGeometry.ArrowRight, 16, Ink.OnPrimary);

        _ok.Content = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { _okLabel, _okArrow }
        };
        _ok.Classes.Add("primary");
        _ok.Cursor = new Cursor(StandardCursorType.Hand);
        _ok.Click += async (_, _) => await ConfirmAsync();
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

        // Only the switch is set here. The colours of the filled button, including the ones it
        // turns while it cannot be pressed, belong to Button.primary - a value written here
        // would outrank the style and freeze the button at it.
        bool ready = count > 0 && !_hasError;
        _ok.IsEnabled = ready;
        _ok.Cursor = new Cursor(ready ? StandardCursorType.Hand : StandardCursorType.Arrow);
        _okArrow.Stroke = ready
            ? ThemeResources.Brush("ActionInk")
            : ThemeResources.Brush("Slate400");
    }

    /// <summary>
    /// Marks the place whose folders are on screen, the way the prototype does: a gold bar up
    /// the row's left edge, a warmer wash behind it, and its mark and word in the accent.
    /// </summary>
    private void PaintPlaces()
    {
        foreach (var row in _placeRows)
        {
            bool on = IsCurrentPlace(row.Path);

            row.Button.Background = on ? Ink.PlaceActive : Brushes.Transparent;
            row.Button.BorderBrush = on ? Ink.PlaceRing : Brushes.Transparent;
            row.Label.Foreground = on ? Ink.Heading : Ink.Text2;
            row.Mark.Stroke = on ? Ink.Accent : Ink.Faint;
            row.Bar.IsVisible = on;
        }
    }

    /// <summary>
    /// Whether <paramref name="path"/> is the place the panel is in.
    /// </summary>
    /// <remarks>
    /// Inside one counts. The panel opens beside the project's folders, which is a folder on
    /// the Desktop rather than the Desktop itself, and a quick-access row that went dark the
    /// moment somebody opened a folder inside it would be a list with no place marked at all.
    /// The deepest matching place wins, because a drive contains every place on it: C:\ must
    /// not light up as the current place when the list is showing the Desktop.
    /// </remarks>
    private bool IsCurrentPlace(string path)
    {
        if (string.IsNullOrEmpty(_currentPath)) return false;

        string current = System.IO.Path.TrimEndingDirectorySeparator(_currentPath);
        string? best = null;

        foreach (var row in _placeRows)
        {
            string place = System.IO.Path.TrimEndingDirectorySeparator(row.Path);
            if (place.Length == 0) continue;

            string prefix = place.EndsWith(System.IO.Path.DirectorySeparatorChar)
                ? place
                : place + System.IO.Path.DirectorySeparatorChar;

            bool within = string.Equals(current, place, StringComparison.OrdinalIgnoreCase) ||
                          current.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);

            if (within && (best is null || place.Length > best.Length)) best = place;
        }

        return best is not null &&
               string.Equals(best, System.IO.Path.TrimEndingDirectorySeparator(path), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The bar under the project's count: the share of the result that this visit adds.
    /// </summary>
    private void UpdateProjectBar()
    {
        if (_projectTrack is null) return;

        int added = _chosen.Count(c => !_existing.Contains(c.Path));
        int total = _existingCount + added;
        double share = total == 0 ? 0 : (double)added / total;

        _projectTrack.ColumnDefinitions = new ColumnDefinitions(
            string.Create(CultureInfo.InvariantCulture, $"{share:0.####}*,{1 - share:0.####}*"));
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
            // The word first: a bare "C:\" is a letter in a list of folders, while "Диск C:\"
            // says what kind of place it is. A name the disk carries itself follows in brackets.
            string label = drive.VolumeLabel;
            yield return (string.IsNullOrWhiteSpace(label)
                    ? $"Диск {drive.Name}"
                    : $"Диск {drive.Name} ({label})",
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

        // A different listing: what was remembered belongs to the folder being left.
        _loadedPreviews.Clear();

        _currentPath = path;
        BuildCrumbs(path);
        _back.IsEnabled = _historyAt > 0;
        _back.Opacity = _historyAt > 0 ? 1 : 0.32;
        PaintPlaces();
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
        // Whatever was loading belongs to the listing that is being replaced.
        _previewLoad?.Cancel();
        _previewLoad = _previewsOn ? new CancellationTokenSource() : null;
        _previewTargets.Clear();
        _folderViews.Clear();

        _foldersBox.Children.Clear();
        _listBox.Children.Clear();
        _cards.Clear();

        var needle = _filter.Text?.Trim() ?? string.Empty;
        _visible.Clear();

        foreach (var folder in _folders)
        {
            if (needle.Length > 0 &&
                folder.Name.IndexOf(needle, StringComparison.CurrentCultureIgnoreCase) < 0) continue;

            _visible.Add(folder);

            if (_listView)
                _listBox.Children.Add(FolderRow(folder));
            else
                _foldersBox.Children.Add(FolderCard(folder));
        }

        _foundCaption.Text = Folders(_visible.Count);

        bool empty = _visible.Count == 0;
        _foldersBox.IsVisible = !empty && !_listView;
        _listBox.IsVisible = !empty && _listView;
        _emptyHost.IsVisible = empty;
        _emptyHost.Content = empty ? EmptyListing(needle.Length > 0) : null;

        // The cards were built before the middle column had its final width, so the row is
        // measured again once the panel has been arranged.
        LayoutCards(_foldersBox.Bounds.Width, force: true);
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            LayoutCards(_foldersBox.Bounds.Width, force: true));
        UpdateSelectAll();

        // After the row has been arranged: a card has no position on screen before that, and
        // its position is what decides which covers are worth decoding.
        Avalonia.Threading.Dispatcher.UIThread.Post(
            LoadVisiblePreviews,
            Avalonia.Threading.DispatcherPriority.Background);
    }

    /// <summary>
    /// Asks for the covers that are on screen or one row below it, and for nothing else.
    /// </summary>
    /// <remarks>
    /// A folder of two hundred subfolders must not start eight hundred decodes because it was
    /// opened. The cards are not virtualised, so "on screen" is answered by where the card sits
    /// inside the scrolling viewport, with a screen of margin above and below so a preview is
    /// usually ready before its row arrives.
    /// </remarks>
    private void LoadVisiblePreviews()
    {
        if (!_previewsOn || _previewLoad is null || _folderViewport is null) return;

        // Before the sheet has been measured every card looks like it is on screen, so a row
        // of two hundred would decode in full. Nothing is on screen until there is a screen.
        if (_folderViewport.Bounds.Height < 40) return;

        foreach (var target in _previewTargets)
        {
            if (target.Requested || target.Folder.Files == 0) continue;
            if (!IsNearViewport(target.Card)) continue;

            target.Requested = true;
            _ = LoadPreviewAsync(target);
        }
    }

    private bool IsNearViewport(Border card)
    {
        if (_folderViewport is null) return false;

        // The card's own coordinates say nothing about scrolling: translated into the viewport
        // they already include it.
        var origin = card.TranslatePoint(new Point(0, 0), _folderViewport);
        if (origin is not { } point) return false;

        const double margin = 300;
        return point.Y + card.Bounds.Height >= -margin &&
               point.Y <= _folderViewport.Bounds.Height + margin;
    }

    private async Task LoadPreviewAsync(PreviewTarget target)
    {
        var token = _previewLoad?.Token ?? CancellationToken.None;

        IReadOnlyList<Bitmap> pictures;
        try
        {
            pictures = await FolderPreviewCache.GetAsync(target.Folder.FullPath, target.Count, token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (pictures.Count == 0 || token.IsCancellationRequested) return;

        Remember(target.Folder.FullPath, pictures);

        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (token.IsCancellationRequested || !_previewsOn) return;
            FillPreview(target, pictures);
        });
    }

    /// <summary>
    /// Puts pictures on a target that already has them from earlier in this listing.
    /// </summary>
    /// <remarks>
    /// Only when there are enough for the cover in question: a row's single picture is not
    /// the card's four, and showing one where four belong would make the cover change shape
    /// once the rest arrived. The answer is marked as given, so the loader does not ask again.
    /// </remarks>
    private void FillRememberedPreview(PreviewTarget target)
    {
        if (!_loadedPreviews.TryGetValue(target.Folder.FullPath, out var pictures)) return;
        if (pictures.Count < target.Count) return;

        target.Requested = true;
        FillPreview(target, pictures);
    }

    private void Remember(string folder, IReadOnlyList<Bitmap> pictures)
    {
        if (!_loadedPreviews.ContainsKey(folder) && _loadedPreviews.Count >= RememberedFolders)
            _loadedPreviews.Remove(_loadedPreviews.Keys.First());

        _loadedPreviews[folder] = pictures;
    }

    /// <summary>
    /// Draws what came back into the cover: four cells on a card, one picture in a list row.
    /// </summary>
    /// <remarks>
    /// The folder mark stays where it was until there is a photograph to replace it, so a
    /// folder with nothing in it - or one whose files cannot be read - looks exactly as it did.
    /// </remarks>
    private void FillPreview(PreviewTarget target, IReadOnlyList<Bitmap> pictures)
    {
        if (target.Filled) return;
        target.Filled = true;

        var board = new Grid
        {
            Background = Ink.Sunken,
            ColumnDefinitions = new ColumnDefinitions("*,*"),
            RowDefinitions = new RowDefinitions("*,*")
        };

        int cells = target.Count == 1 ? 1 : 4;

        for (int index = 0; index < pictures.Count && index < cells; index++)
        {
            var cell = new Border
            {
                CornerRadius = new CornerRadius(4),
                Margin = new Thickness(1.5),
                ClipToBounds = true,
                Child = new Image
                {
                    Source = pictures[index],
                    Stretch = Stretch.UniformToFill
                }
            };

            if (target.Count == 1)
            {
                Grid.SetColumnSpan(cell, 2);
                Grid.SetRowSpan(cell, 2);
            }
            else
            {
                Grid.SetColumn(cell, index % 2);
                Grid.SetRow(cell, index / 2);
            }

            board.Children.Add(cell);
        }

        target.Mark.IsVisible = false;
        target.Host.Children.Insert(0, board);
    }

    private static Control EmptyListing(bool filtered)
    {
        var ring = new Border
        {
            Width = 56,
            Height = 56,
            CornerRadius = new CornerRadius(17),
            Background = Gradient("PickerEmptyFill"),
            BorderBrush = ThemeResources.Brush("PickerEmptyRing"),
            BorderThickness = new Thickness(1),
            HorizontalAlignment = HorizontalAlignment.Center,
            Child = Icon.Stroke(IconGeometry.Folder, 25, Ink.Accent)
        };

        return new StackPanel
        {
            Spacing = 9,
            HorizontalAlignment = HorizontalAlignment.Center,
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
        PaintChoices();
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
        _selectAllLabel.Text = allChosen ? "Снять выбор" : "Выбрать все";
        _selectAll.IsEnabled = _visible.Count > 0;
        _selectAll.Opacity = _visible.Count > 0 ? 1 : 0.4;
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

        var mark = Icon.Stroke(IconGeometry.Folder, 42, chosen ? Ink.OnIcon : Ink.Accent);
        mark.HorizontalAlignment = HorizontalAlignment.Center;
        mark.Opacity = 0.92;

        var glow = new Border
        {
            Background = Gradient("PickerCoverGlow"),
            IsHitTestVisible = false
        };

        var cover = new Border
        {
            Height = Math.Round((_cardWidth - 14) * 0.75),
            CornerRadius = new CornerRadius(11),
            Background = Ink.Sunken,
            BorderBrush = Ink.CoverRing,
            BorderThickness = new Thickness(1),
            ClipToBounds = true
        };

        var tickInk = Icon.Stroke(IconGeometry.Tick, 13, Ink.OnPrimary);

        var tick = new Border
        {
            Width = 21,
            Height = 21,
            CornerRadius = new CornerRadius(7),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 6, 6, 0),
            IsVisible = chosen,
            Background = Gradient("PickerTickFill"),
            BoxShadow = ThemeResources.Shadow("PickerTickShadow"),
            Child = tickInk
        };

        var bookLabel = new TextBlock
        {
            Text = $"Книга {order}",
            FontSize = 9.5,
            FontWeight = FontWeight.ExtraBold,
            Foreground = Ink.ChipInk,
            VerticalAlignment = VerticalAlignment.Center
        };

        var book = new Border
        {
            Height = 19,
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(7, 0),

            // At the bottom, not over the top-left picture: the cover now shows photographs,
            // and a plate across one of them hides the thing the preview exists for.
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(6, 0, 0, 6),
            IsVisible = chosen,
            Background = Ink.Chip,
            BoxShadow = ThemeResources.Shadow("PickerBookRing"),
            Child = bookLabel
        };

        // The arrow is the second target: it opens the folder, and it is on screen only while
        // the pointer is on the card, so a card that is merely passing under the hand shows
        // nothing it did not mean to.
        var openGlyph = Icon.Stroke(IconGeometry.ArrowRight, 13, Ink.ChipInk);

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
            Background = Ink.OpenFill,
            BorderBrush = Ink.BookEdge,
            BorderThickness = new Thickness(1),
            Cursor = new Cursor(StandardCursorType.Hand),
            Content = openGlyph
        };
        open.Transitions = new Transitions
        {
            new DoubleTransition { Property = Visual.OpacityProperty, Duration = TimeSpan.FromMilliseconds(160) },
            new BrushTransition { Property = TemplatedControl.BackgroundProperty, Duration = TimeSpan.FromMilliseconds(160) }
        };
        AutomationProperties.SetName(open, $"Открыть папку {folder.Name}");
        ToolTip.SetTip(open, folder.FullPath);
        open.Click += (_, _) => Navigate(folder.FullPath);
        open.PointerEntered += (_, _) =>
        {
            open.Background = Ink.Gold;
            openGlyph.Stroke = Ink.OnPrimary;
        };
        open.PointerExited += (_, _) =>
        {
            open.Background = Ink.OpenFill;
            openGlyph.Stroke = Ink.ChipInk;
        };

        var coverHost = new Panel { Children = { glow, mark, book, tick, open } };
        cover.Child = coverHost;

        var metaMark = Icon.Stroke(IconGeometry.Image, 11, Ink.Faint);
        metaMark.Opacity = 0.8;

        var name = new TextBlock
        {
            Text = folder.Name,
            FontSize = 12.5,
            FontWeight = FontWeight.Bold,
            Foreground = chosen ? Ink.Heading : Ink.Text,
            TextTrimming = TextTrimming.CharacterEllipsis
        };

        var copy = new StackPanel
        {
            Spacing = 3,
            Margin = new Thickness(3, 9, 3, 0),
            Children =
            {
                name,
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
            Background = chosen ? Gradient("PickerCardChosen") : Ink.CardFill,
            BorderBrush = chosen ? Ink.CardChosenRing : Ink.Subtle,
            BorderThickness = new Thickness(1),
            Cursor = new Cursor(StandardCursorType.Hand),
            Focusable = true,
            Child = new StackPanel { Children = { cover, copy } }
        };
        card.Transitions = new Transitions
        {
            new BrushTransition { Property = TemplatedControl.BackgroundProperty, Duration = TimeSpan.FromMilliseconds(180) },
            new BrushTransition { Property = TemplatedControl.BorderBrushProperty, Duration = TimeSpan.FromMilliseconds(180) }
        };

        var view = new FolderView
        {
            Folder = folder,
            Body = card,
            Mark = mark,
            Tick = tick,
            TickInk = tickInk,
            Name = name,
            Book = book,
            BookLabel = bookLabel
        };
        _folderViews[folder.FullPath] = view;
        PaintFolder(view);

        card.PointerEntered += (_, _) =>
        {
            open.Opacity = 1;
            PaintHover(view);
            card.RenderTransform = new TranslateTransform(0, -2);
            mark.RenderTransform = new TranslateTransform(0, -2);
        };

        card.PointerExited += (_, _) =>
        {
            open.Opacity = 0;
            card.RenderTransform = null;
            mark.RenderTransform = null;
            PaintFolder(view);
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

        var target = new PreviewTarget(folder, card, coverHost, mark, 4);
        _previewTargets.Add(target);
        FillRememberedPreview(target);

        return card;
    }

    /// <summary>
    /// One folder as a line of the list: the box that ticks it, its picture, its name, what it
    /// holds, and the arrow that opens it.
    /// </summary>
    /// <remarks>
    /// The same two targets as a card, at the same places. The arrow is here as well as on the
    /// card, though the prototype's list hides it: a view in which a folder cannot be opened is
    /// a view in which half the panel does not work.
    /// </remarks>
    private Control FolderRow(FolderEntry folder)
    {
        bool chosen = _selected.Contains(folder.FullPath);

        var boxInk = Icon.Stroke(IconGeometry.Tick, 11,
            chosen ? Ink.OnPrimary : Brushes.Transparent);

        var box = new Border
        {
            Width = 18,
            Height = 18,
            CornerRadius = new CornerRadius(6),
            VerticalAlignment = VerticalAlignment.Center,
            Background = chosen ? Gradient("PickerTickFill") : Ink.BoxFill,
            BorderBrush = chosen ? Brushes.Transparent : Ink.BoxRing,
            BorderThickness = new Thickness(1.5),
            Child = boxInk
        };

        var mark = Icon.Stroke(IconGeometry.Folder, 22, chosen ? Ink.OnIcon : Ink.Accent);
        var coverHost = new Panel { Children = { mark } };

        var cover = new Border
        {
            Width = 58,
            Height = 42,
            CornerRadius = new CornerRadius(7),
            Background = Ink.Sunken,
            BorderBrush = Ink.CoverRing,
            BorderThickness = new Thickness(1),
            ClipToBounds = true,
            VerticalAlignment = VerticalAlignment.Center,
            Child = coverHost
        };

        var name = new TextBlock
        {
            Text = folder.Name,
            FontSize = 12.5,
            FontWeight = FontWeight.Bold,
            Foreground = chosen ? Ink.Heading : Ink.Text2,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center
        };

        var metaMark = Icon.Stroke(IconGeometry.Image, 11, Ink.Faint);
        metaMark.Opacity = 0.8;

        var meta = new StackPanel
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
        };

        var openGlyph = Icon.Stroke(IconGeometry.ArrowRight, 13, Ink.ChipInk);

        var open = new Button
        {
            Width = 24,
            Height = 24,
            Padding = new Thickness(0),
            CornerRadius = new CornerRadius(8),
            VerticalAlignment = VerticalAlignment.Center,
            Opacity = 0,
            Background = Ink.OpenFill,
            BorderBrush = Ink.BookEdge,
            BorderThickness = new Thickness(1),
            Cursor = new Cursor(StandardCursorType.Hand),
            Content = openGlyph
        };
        open.Transitions = new Transitions
        {
            new DoubleTransition { Property = Visual.OpacityProperty, Duration = TimeSpan.FromMilliseconds(160) },
            new BrushTransition { Property = TemplatedControl.BackgroundProperty, Duration = TimeSpan.FromMilliseconds(160) }
        };
        AutomationProperties.SetName(open, $"Открыть папку {folder.Name}");
        ToolTip.SetTip(open, folder.FullPath);
        open.Click += (_, _) => Navigate(folder.FullPath);
        open.PointerEntered += (_, _) =>
        {
            open.Background = Ink.Gold;
            openGlyph.Stroke = Ink.OnPrimary;
        };
        open.PointerExited += (_, _) =>
        {
            open.Background = Ink.OpenFill;
            openGlyph.Stroke = Ink.ChipInk;
        };

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*,Auto,Auto") };
        grid.Children.Add(box);
        Grid.SetColumn(cover, 1);
        cover.Margin = new Thickness(9, 0, 0, 0);
        grid.Children.Add(cover);
        Grid.SetColumn(name, 2);
        name.Margin = new Thickness(11, 0, 0, 0);
        grid.Children.Add(name);
        Grid.SetColumn(meta, 3);
        meta.Margin = new Thickness(14, 0, 0, 0);
        grid.Children.Add(meta);
        Grid.SetColumn(open, 4);
        open.Margin = new Thickness(8, 0, 0, 0);
        grid.Children.Add(open);

        var row = new Border
        {
            CornerRadius = new CornerRadius(11),
            Padding = new Thickness(6, 6, 10, 6),
            Background = chosen ? Gradient("PickerCardChosen") : Ink.CardFill,
            BorderBrush = chosen ? Ink.CardChosenRing : Ink.Subtle,
            BorderThickness = new Thickness(1),
            Cursor = new Cursor(StandardCursorType.Hand),
            Focusable = true,
            Child = grid
        };
        row.Transitions = Fade();

        var view = new FolderView
        {
            Folder = folder,
            Body = row,
            Mark = mark,
            Tick = box,
            TickInk = boxInk,
            Name = name,
            Row = true
        };
        _folderViews[folder.FullPath] = view;
        PaintFolder(view);

        row.PointerEntered += (_, _) =>
        {
            open.Opacity = 1;
            PaintHover(view);
        };

        row.PointerExited += (_, _) =>
        {
            open.Opacity = 0;
            PaintFolder(view);
        };

        row.PointerPressed += (_, e) =>
        {
            if (IsWithin(e.Source, open)) return;
            Toggle(folder);
        };

        row.KeyDown += (_, e) =>
        {
            if (e.Key is not (Key.Enter or Key.Space)) return;
            Toggle(folder);
            e.Handled = true;
        };

        AutomationProperties.SetName(row, chosen
            ? $"Убрать {folder.Name} из проекта"
            : $"Выбрать папку {folder.Name}");

        if (folder.Files == 0)
            ToolTip.SetTip(row, "В папке нет фотографий — для книги она не подойдёт");

        var target = new PreviewTarget(folder, row, coverHost, mark, 1);
        _previewTargets.Add(target);
        FillRememberedPreview(target);

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
            _chosen.Add(new ChosenFolder(folder.FullPath, folder.Files));
        }

        RefreshChosen();
        PaintChoices();
        _ = RevalidateAsync();
    }

    /// <summary>
    /// Repaints every card and row from the current choice, leaving the controls in place.
    /// </summary>
    private void PaintChoices()
    {
        foreach (var view in _folderViews.Values)
            PaintFolder(view);

        UpdateSelectAll();
    }

    /// <summary>
    /// One card or row, drawn from the choice as it stands now.
    /// </summary>
    /// <remarks>
    /// One place that knows what a chosen folder looks like, so the state a card is built in
    /// and the state it is repainted into cannot disagree.
    /// </remarks>
    private void PaintFolder(FolderView view)
    {
        bool chosen = _selected.Contains(view.Folder.FullPath);
        int order = chosen ? _selected.IndexOf(view.Folder.FullPath) + 1 : 0;

        view.Body.Background = chosen ? Gradient("PickerCardChosen") : Ink.CardFill;
        view.Body.BorderBrush = chosen ? Ink.CardChosenRing : Ink.Subtle;
        view.Mark.Stroke = chosen ? Ink.OnIcon : Ink.Accent;

        AutomationProperties.SetName(view.Body, chosen
            ? $"Убрать {view.Folder.Name} из проекта"
            : $"Выбрать папку {view.Folder.Name}");

        if (view.Row)
        {
            view.Tick.Background = chosen ? Gradient("PickerTickFill") : Ink.BoxFill;
            view.Tick.BorderBrush = chosen ? Brushes.Transparent : Ink.BoxRing;
            view.TickInk.Stroke = chosen ? Ink.OnPrimary : Brushes.Transparent;

            if (view.Name is not null)
                view.Name.Foreground = chosen ? Ink.Heading : Ink.Text2;
        }
        else
        {
            view.Tick.IsVisible = chosen;

            if (view.Book is not null) view.Book.IsVisible = chosen;
            if (view.BookLabel is not null) view.BookLabel.Text = $"Книга {order}";
            if (view.Name is not null) view.Name.Foreground = chosen ? Ink.Heading : Ink.Text;
        }

        if (view.Body.IsPointerOver) PaintHover(view);
        else if (!view.Row) view.Body.BoxShadow = chosen
            ? ThemeResources.Shadow("PickerCardChosenShadow")
            : default;
    }

    private void PaintHover(FolderView view)
    {
        bool chosen = _selected.Contains(view.Folder.FullPath);

        view.Body.Background = chosen ? Gradient("PickerCardChosen") : Ink.CardHover;
        view.Body.BorderBrush = chosen ? Ink.CardChosenRing : Ink.CardLineHover;

        if (!view.Row)
            view.Body.BoxShadow = chosen
                ? ThemeResources.Shadow("PickerCardChosenShadow")
                : ThemeResources.Shadow("PickerCardLift");
    }

    private void RefreshChosen()
    {
        _chosenBox.Children.Clear();
        _chosenCount.Text = Folders(_chosen.Count);
        UpdateProjectBar();

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
            Background = Ink.Sunken,
            BorderBrush = Ink.CoverRing,
            BorderThickness = new Thickness(1),
            VerticalAlignment = VerticalAlignment.Center,
            Child = Icon.Stroke(IconGeometry.Folder, 15, Ink.Accent)
        };

        var caption = new TextBlock
        {
            Text = entry.HasError
                ? "папка не подходит"
                : $"Книга {index + 1} · {Photos(entry.Files)}",
            FontSize = 10,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = entry.HasError ? Ink.DangerMark : Ink.Faint
        };

        // The warning belongs on the line it explains, beside those words: added to the column
        // instead, it dropped below the caption and dragged the row's text off centre.
        var captionRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Children = { caption }
        };

        if (entry.HasError)
        {
            var warning = Icon.Stroke(IconGeometry.Warning, 12, Ink.DangerMark);
            warning.Margin = new Thickness(5, 0, 0, 0);
            warning.VerticalAlignment = VerticalAlignment.Center;
            captionRow.Children.Add(warning);
        }

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
                captionRow
            }
        };

        var walk = new Button
        {
            Classes = { "ghost" },
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
            Classes = { "ghost" },
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
        drop.Transitions = new Transitions
        {
            new DoubleTransition { Property = Visual.OpacityProperty, Duration = TimeSpan.FromMilliseconds(140) },
            new BrushTransition { Property = TemplatedControl.BackgroundProperty, Duration = TimeSpan.FromMilliseconds(140) }
        };
        drop.Click += (_, _) =>
        {
            _selected.RemoveAll(p => string.Equals(p, entry.Path, StringComparison.OrdinalIgnoreCase));
            _chosen.Remove(entry);
            RefreshChosen();
            PaintChoices();
            _ = RevalidateAsync();
        };
        drop.PointerEntered += (_, _) =>
        {
            drop.Background = Ink.DangerWash;
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
            Background = entry.HasError ? Ink.DangerWash : Brushes.Transparent,
            CornerRadius = new CornerRadius(9),
            Padding = new Thickness(4, 5),
            Child = grid
        };

        // The row says what it is by the cross that arrives, not by a grey field behind the
        // name: a folder already in the project is not a different thing under the pointer, and
        // the wash read as a second kind of selection.
        row.PointerEntered += (_, _) => drop.Opacity = 1;
        row.PointerExited += (_, _) =>
        {
            drop.Opacity = 0;
            row.Background = entry.HasError ? Ink.DangerWash : Brushes.Transparent;
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
            Background = Gradient("PickerEmptyFill"),
            BorderBrush = ThemeResources.Brush("PickerEmptyRing"),
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

    /// <summary>
    /// Shows the report of a broken choice, or takes the layer away entirely.
    /// </summary>
    private void SetError(string? message)
    {
        bool broken = !string.IsNullOrWhiteSpace(message);
        _hasError = broken;

        _logBox.IsVisible = broken;
        _logText.Text = broken ? message! : string.Empty;

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

    /// <summary>
    /// Takes the caret out of the search box when the press is anywhere else on the sheet.
    /// </summary>
    /// <remarks>
    /// The field and the buttons are left alone: each of them takes the focus itself when it
    /// is pressed, and a button that kept it is how Enter goes on meaning "press the thing I
    /// last pressed". Everything else - the page around the cards, the cards themselves, the
    /// columns - is not a place to type, and the blinking line belongs nowhere but the field.
    /// </remarks>
    private void OnSheetPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!_filter.IsFocused) return;
        if (e.Source is not Visual source) return;

        for (Visual? walk = source; walk is not null; walk = walk.GetVisualParent())
        {
            if (ReferenceEquals(walk, _filter) || walk is Button) return;
        }

        TopLevel.GetTopLevel(this)?.FocusManager?.ClearFocus();
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

    /// <summary>
    /// A quiet word that acts: "Очистить", and the like.
    /// </summary>
    /// <remarks>
    /// The prototype's link warms to red rather than to gold: it is the one control in the
    /// column that takes folders away, and a gold wash made it look like the button that adds
    /// them. The padding is the prototype's four by seven, so it sits inside a row of text
    /// rather than pushing one apart.
    /// </remarks>
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
            Cursor = new Cursor(StandardCursorType.Hand),
            VerticalContentAlignment = VerticalAlignment.Center
        };
        button.Classes.Add("ghost");
        button.Transitions = Fade();

        button.Click += (_, _) => onClick();
        button.PointerEntered += (_, _) =>
        {
            button.Background = Ink.DangerWash;
            button.Foreground = Ink.DangerStrong;
        };
        button.PointerExited += (_, _) =>
        {
            button.Background = Brushes.Transparent;
            button.Foreground = Ink.Text2;
        };

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
    /// The 0.14 - 0.18 second ease every quiet control of this panel answers with.
    /// </summary>
    /// <remarks>
    /// A wash that appears between two frames reads as a jump, and in a panel whose whole job
    /// is to say where the pointer is, a jump is the answer arriving late. The prototype
    /// animates colour, border and lift; so does this.
    /// </remarks>
    private static Transitions Fade() => new()
    {
        new BrushTransition
        {
            Property = TemplatedControl.BackgroundProperty,
            Duration = TimeSpan.FromMilliseconds(150)
        },
        new BrushTransition
        {
            Property = TemplatedControl.BorderBrushProperty,
            Duration = TimeSpan.FromMilliseconds(150)
        },
        new BrushTransition
        {
            Property = TemplatedControl.ForegroundProperty,
            Duration = TimeSpan.FromMilliseconds(150)
        },
        new BrushTransition
        {
            Property = TextBlock.ForegroundProperty,
            Duration = TimeSpan.FromMilliseconds(150)
        },
        new BrushTransition
        {
            Property = Shape.StrokeProperty,
            Duration = TimeSpan.FromMilliseconds(150)
        }
    };

    /// <summary>A gradient the theme names, for the plates and washes of this panel.</summary>
    private static IBrush Gradient(string token) => ThemeResources.Fill(token);

    /// <summary>The quiet word that acts, drawn as the prototype's ghost button.</summary>

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
                Classes = { "ghost" },
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
                BorderBrush = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(6, 4),
                CornerRadius = new CornerRadius(7),
                Foreground = Ink.Text2,
                Cursor = new Cursor(StandardCursorType.Hand),
                VerticalAlignment = VerticalAlignment.Center
            };
            button.Transitions = Fade();
            AutomationProperties.SetName(button, step.Label);
            button.Click += (_, _) => Navigate(step.Path);
            Hover(button, ThemeResources.Brush("PickerCardHover"), Brushes.Transparent);
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

            // Four to three, the prototype's cover: a fixed height made the picture of a folder
            // a letterbox on a wide card and a strip on a narrow one.
            cover.Height = Math.Round((card - 14) * 0.75);
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
        PaintChoices();
        _ = RevalidateAsync();
    }

    /// <summary>
    /// A card or a row's controls that a choice changes, kept so the choice can be drawn on
    /// the control that is already there.
    /// </summary>
    private sealed class FolderView
    {
        public required FolderEntry Folder { get; init; }
        public required Border Body { get; init; }
        public required Glyph Mark { get; init; }
        public required Border Tick { get; init; }
        public required Glyph TickInk { get; init; }
        public TextBlock? Name { get; init; }
        public Border? Book { get; init; }
        public TextBlock? BookLabel { get; init; }
        public bool Row { get; init; }
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
    /// A cover waiting for the first photographs of its folder.
    /// </summary>
    /// <remarks>
    /// The card is kept so the cover can be asked whether it is on screen, and
    /// <see cref="Requested"/> so one cover is decoded once per listing rather than once per
    /// scroll event.
    /// </remarks>
    private sealed class PreviewTarget
    {
        public PreviewTarget(FolderEntry folder, Border card, Panel host, Control mark, int count)
        {
            Folder = folder;
            Card = card;
            Host = host;
            Mark = mark;
            Count = count;
        }

        public FolderEntry Folder { get; }
        public Border Card { get; }
        public Panel Host { get; }
        public Control Mark { get; }
        public int Count { get; }
        public bool Requested { get; set; }
        public bool Filled { get; set; }
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
    private static class Icon
    {
        /// <summary>
        /// A glyph on the prototype's 24 grid, drawn at <paramref name="size"/>.
        /// </summary>
        /// <remarks>
        /// The grid is carried inside a viewbox rather than stretched onto the control. That is
        /// the difference between the design's 17-pixel folder and a folder whose bounding box
        /// is 17 pixels: the second is a third larger than the drawing beside it, and every
        /// mark in this panel was that third too big.
        /// </remarks>
        public static Glyph Stroke(Geometry data, double size, IBrush brush) => new(data, size, brush);
    }

    /// <summary>One stroke on the 24 grid, scaled to the size it is drawn at.</summary>
    private sealed class Glyph : Viewbox
    {
        private readonly Shape _path;

        public Glyph(Geometry data, double size, IBrush stroke)
        {
            Width = size;
            Height = size;
            Stretch = Stretch.Uniform;
            StretchDirection = StretchDirection.Both;
            HorizontalAlignment = HorizontalAlignment.Center;
            VerticalAlignment = VerticalAlignment.Center;

            _path = new Avalonia.Controls.Shapes.Path
            {
                Data = data,
                Width = 24,
                Height = 24,
                Stretch = Stretch.None,
                Stroke = stroke,
                StrokeThickness = 1.7,
                StrokeLineCap = PenLineCap.Round,
                StrokeJoin = PenLineJoin.Round
            };

            Child = _path;
        }

        public IBrush Stroke
        {
            get => _path.Stroke!;
            set => _path.Stroke = value;
        }

        public double StrokeThickness
        {
            get => _path.StrokeThickness;
            set => _path.StrokeThickness = value;
        }
    }

    /// <summary>The shapes this panel draws, on the prototype's 24 grid.</summary>
    private static class IconGeometry
    {
        // The paths are the prototype's own. A glyph that keeps the grid is drawn at the size
        // the design gives it; a glyph stretched to fill its box is the same icon at forty
        // percent more weight, which is what the header badge looked like.
        public static Geometry Folder =>
            Geometry.Parse("M 3.5 8 A 2.5 2.5 0 0 1 6 5.5 H 9.6 L 11.4 7.7 H 18 A 2.5 2.5 0 0 1 20.5 10.2 V 17.2 A 2.5 2.5 0 0 1 18 19.7 H 6 A 2.5 2.5 0 0 1 3.5 17.2 Z");

        public static Geometry Drive =>
            Geometry.Parse("M 7.4 4.5 H 16.6 L 20.6 14.1 L 18.2 19.5 H 5.8 L 3.4 14.1 Z M 3.6 14.1 H 20.4 M 7.3 16.8 A 0.9 0.9 0 1 0 9.1 16.8 A 0.9 0.9 0 1 0 7.3 16.8");

        public static Geometry Tick =>
            Geometry.Parse("M 5 12.6 L 9.6 17.2 L 19 6.6");

        public static Geometry Close =>
            Geometry.Parse("M 6.5 6.5 L 17.5 17.5 M 17.5 6.5 L 6.5 17.5");

        public static Geometry Warning =>
            Geometry.Parse("M 12 3.6 L 21.4 20 H 2.6 Z M 12 9.4 V 14.2 M 12 17.1 V 17.2");

        public static Geometry ArrowLeft =>
            Geometry.Parse("M 20 12 H 4.6 M 10.6 5.6 L 4.2 12 L 10.6 18.4");

        public static Geometry ArrowRight =>
            Geometry.Parse("M 4 12 H 19 M 13 6 L 19 12 L 13 18");

        public static Geometry ViewGrid =>
            Geometry.Parse("M5.3 3.5h3.4a1.8 1.8 0 0 1 1.8 1.8v3.4a1.8 1.8 0 0 1-1.8 1.8H5.3a1.8 1.8 0 0 1-1.8-1.8V5.3a1.8 1.8 0 0 1 1.8-1.8zM15.3 3.5h3.4a1.8 1.8 0 0 1 1.8 1.8v3.4a1.8 1.8 0 0 1-1.8 1.8h-3.4a1.8 1.8 0 0 1-1.8-1.8V5.3a1.8 1.8 0 0 1 1.8-1.8zM5.3 13.5h3.4a1.8 1.8 0 0 1 1.8 1.8v3.4a1.8 1.8 0 0 1-1.8 1.8H5.3a1.8 1.8 0 0 1-1.8-1.8v-3.4a1.8 1.8 0 0 1 1.8-1.8zM15.3 13.5h3.4a1.8 1.8 0 0 1 1.8 1.8v3.4a1.8 1.8 0 0 1-1.8 1.8h-3.4a1.8 1.8 0 0 1-1.8-1.8v-3.4a1.8 1.8 0 0 1 1.8-1.8z");

        public static Geometry ViewList =>
            Geometry.Parse("M4 6.5H20M4 12H20M4 17.5H13");

        public static Geometry ChevronRight =>
            Geometry.Parse("M 9.5 5.5 L 16 12 L 9.5 18.5");

        public static Geometry FolderPlus =>
            Geometry.Parse("M 3.5 8 A 2.5 2.5 0 0 1 6 5.5 H 9.6 L 11.4 7.7 H 18 A 2.5 2.5 0 0 1 20.5 10.2 V 13 M 3.5 8 V 17.2 A 2.5 2.5 0 0 0 6 19.7 H 12 M 17.5 14 V 20 M 14.5 17 H 20.5");

        public static Geometry Search =>
            Geometry.Parse("M 4.6 11 A 6.4 6.4 0 1 0 17.4 11 A 6.4 6.4 0 1 0 4.6 11 M 15.8 15.8 L 20.5 20.5");

        public static Geometry Info =>
            Geometry.Parse("M 3 12 A 9 9 0 1 0 21 12 A 9 9 0 1 0 3 12 M 12 11 V 16.5 M 12 7.8 H 12.01");

        public static Geometry Lock =>
            Geometry.Parse("M 7.4 10 H 16.6 A 2.4 2.4 0 0 1 19 12.4 V 17.6 A 2.4 2.4 0 0 1 16.6 20 H 7.4 A 2.4 2.4 0 0 1 5 17.6 V 12.4 A 2.4 2.4 0 0 1 7.4 10 Z M 8 10 V 7.5 A 4 4 0 0 1 16 7.5 V 10");

        public static Geometry Image =>
            Geometry.Parse("M 6 4.5 H 18 A 2.5 2.5 0 0 1 20.5 7 V 17 A 2.5 2.5 0 0 1 18 19.5 H 6 A 2.5 2.5 0 0 1 3.5 17 V 7 A 2.5 2.5 0 0 1 6 4.5 Z M 9 8.4 A 1.6 1.6 0 1 0 9 11.6 A 1.6 1.6 0 1 0 9 8.4 M 4.5 17 L 9.3 12.6 A 1.5 1.5 0 0 1 11.3 12.6 L 14.3 15.3 L 16.5 13.3 A 1.5 1.5 0 0 1 18.5 13.3 L 20.1 14.7");

        public static Geometry Monitor =>
            Geometry.Parse("M 5.2 4.2 H 18.8 A 2.4 2.4 0 0 1 21.2 6.6 V 13.8 A 2.4 2.4 0 0 1 18.8 16.2 H 5.2 A 2.4 2.4 0 0 1 2.8 13.8 V 6.6 A 2.4 2.4 0 0 1 5.2 4.2 Z M 9 19.8 H 15 M 12 16.2 V 19.8");

        public static Geometry Document =>
            Geometry.Parse("M 6 3.6 H 13.2 L 18.6 9 V 19.2 A 1.8 1.8 0 0 1 16.8 21 H 6 A 1.8 1.8 0 0 1 4.2 19.2 V 5.4 A 1.8 1.8 0 0 1 6 3.6 Z M 13 3.8 V 9 H 18.2 M 8 13 H 15 M 8 16.4 H 12.6");

        public static Geometry Download =>
            Geometry.Parse("M 12 3.4 V 12.6 M 8.6 9.4 L 12 12.8 L 15.4 9.4 M 4.4 14.4 V 17.8 A 2.4 2.4 0 0 0 6.8 20.2 H 17.2 A 2.4 2.4 0 0 0 19.6 17.8 V 14.4");
    }

    /// <summary>The colours this shell's windows use, in one place.</summary>
    private static class Ink
    {
        public static IBrush Surface => ThemeResources.Brush("Surface");
        public static IBrush Muted => ThemeResources.Brush("SurfaceMuted");
        public static IBrush Sunken => ThemeResources.Brush("Sunken");
        public static IBrush SunkenSoft => ThemeResources.Brush("SunkenSoft");
        public static IBrush SunkenFaint => ThemeResources.Brush("SunkenFaint");
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
        public static IBrush ModalEdge => ThemeResources.Brush("ModalEdge");
        public static IBrush Subtle => ThemeResources.Brush("BorderSubtle");
        public static IBrush Gold => ThemeResources.Brush("Gold");
        public static IBrush GoldDeep => ThemeResources.Brush("GoldDeep");
        public static IBrush GoldHover => ThemeResources.Brush("GoldHover");
        public static IBrush GoldPressed => ThemeResources.Brush("GoldPressed");
        public static IBrush GoldTint => ThemeResources.Brush("OpenHoverFill");
        public static IBrush Ok => ThemeResources.Brush("Emerald600");

        // The quiet wells of this panel: a wash of the modal's own ink, translucent on purpose
        // so the side column and a hovered row inside it stack the way the prototype's do.
        public static IBrush PlaceActive => ThemeResources.Brush("PickerPlaceActive");
        public static IBrush PlaceRing => ThemeResources.Brush("PickerPlaceRing");
        public static IBrush BookEdge => ThemeResources.Brush("PickerBookEdge");
        public static IBrush ChipWash => ThemeResources.Brush("PickerChipWash");
        public static IBrush ChipRing => ThemeResources.Brush("PickerChipRing");
        public static IBrush CardFill => ThemeResources.Brush("PickerCardFill");
        public static IBrush CardHover => ThemeResources.Brush("PickerCardHover");
        public static IBrush CardLineHover => ThemeResources.Brush("PickerCardLineHover");
        public static IBrush CardChosenRing => ThemeResources.Brush("PickerCardChosenRing");
        public static IBrush CoverRing => ThemeResources.Brush("PickerCoverRing");
        public static IBrush BarTrack => ThemeResources.Brush("PickerBarTrack");
        public static IBrush ProjectRing => ThemeResources.Brush("PickerProjectRing");
        public static IBrush SumFill => ThemeResources.Brush("PickerSumFill");
        public static IBrush SumRing => ThemeResources.Brush("PickerSumRing");
        public static IBrush OpenFill => ThemeResources.Brush("PickerOpenFill");
        public static IBrush BoxFill => ThemeResources.Brush("PickerBoxFill");
        public static IBrush BoxRing => ThemeResources.Brush("PickerBoxRing");
        public static IBrush OnIcon => ThemeResources.Brush("PickerOnIcon");
        public static IBrush BadgeInk => ThemeResources.Brush("PickerBadgeInk");
        public static IBrush IconRing => ThemeResources.Brush("PickerIconRing");
        public static IBrush IconWash => ThemeResources.Brush("PickerIconWash");
        public static IBrush Teal => ThemeResources.Brush("KeepInk");
        public static IBrush DangerWash => ThemeResources.Brush("DangerWash");

        /// <summary>The report of a broken choice: the one coloured fill here that is opaque.</summary>
        public static IBrush DangerPaper => ThemeResources.Brush("DangerPaperFill");
        public static IBrush DangerPaperRing => ThemeResources.Brush("DangerPaperRing");

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

    /// <summary>One row of the quick-access column, kept so the current one can be marked.</summary>
    private sealed class PlaceRowView
    {
        public PlaceRowView(string path, Button button, Glyph mark, TextBlock label, Border bar)
        {
            Path = path;
            Button = button;
            Mark = mark;
            Label = label;
            Bar = bar;
        }

        public string Path { get; }
        public Button Button { get; }
        public Glyph Mark { get; }
        public TextBlock Label { get; }
        public Border Bar { get; }
    }
}
