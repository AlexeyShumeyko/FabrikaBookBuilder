using System.Collections;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;

namespace PhotoBook.Shell.Controls;

/// <summary>
/// A filter: a control that opens, a list of choices, and the choice that was made.
/// </summary>
/// <remarks>
/// <para>
/// It exists because this was a <c>ComboBox</c> whose template had been replaced, and it
/// did not work. The list opened and nothing in it could be chosen.
/// </para>
/// <para>
/// The cause was the shape of the thing, not the styling. A dropdown's list lives in a
/// popup, and a popup is a window of its own with its own tree. The binding that carries
/// "this one was chosen" from the row back to the control had to cross that boundary, and a
/// binding addressed to an ancestor does not cross it - it resolves to nothing. So the
/// choice never reached the program, and because the program never heard about a choice,
/// nothing closed the list. The popup stayed open until its own button was pressed again,
/// which is exactly what it did.
/// </para>
/// <para>
/// A control of its own does not have that problem, because there is no boundary to cross:
/// the code that owns the button and the code that owns the list are the same class, and
/// the choice is handed over in a method rather than through an arrangement of bindings.
/// </para>
/// <para>
/// Closing on a click elsewhere is not written here because it is not needed: a popup
/// dismisses itself when light dismiss is on, which is the default, and it is left on.
/// That is also what makes moving between the two filters work - clicking the other one
/// dismisses this list and opens that one, with no bookkeeping.
/// </para>
/// </remarks>
public sealed partial class FilterDropDown : UserControl
{
    private bool _suppressListEcho;

    public FilterDropDown()
    {
        InitializeComponent();

        PART_Button.Click += OnButtonClick;
        PART_List.SelectionChanged += OnListSelectionChanged;
    }

    /// <summary>The choices on offer. A plain enumerable of anything with a label.</summary>
    public IEnumerable? Items
    {
        get => GetValue(ItemsProperty);
        set => SetValue(ItemsProperty, value);
    }

    public static readonly StyledProperty<IEnumerable?> ItemsProperty =
        AvaloniaProperty.Register<FilterDropDown, IEnumerable?>(nameof(Items));

    /// <summary>The chosen one. Two way, because the program may change it as well.</summary>
    public object? SelectedItem
    {
        get => GetValue(SelectedItemProperty);
        set => SetValue(SelectedItemProperty, value);
    }

    public static readonly StyledProperty<object?> SelectedItemProperty =
        AvaloniaProperty.Register<FilterDropDown, object?>(nameof(SelectedItem));

    /// <summary>What a person who cannot see the control would be told it is.</summary>
    public string AutomationName
    {
        get => GetValue(AutomationNameProperty);
        set => SetValue(AutomationNameProperty, value);
    }

    public static readonly StyledProperty<string?> AutomationNameProperty =
        AvaloniaProperty.Register<FilterDropDown, string?>(nameof(AutomationName));

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == ItemsProperty)
        {
            PART_List.ItemsSource = Items;
        }

        // The program can change the choice without the list being open - clearing the
        // filters does exactly that - and the row that shows it has to follow. The flag
        // stops the two sides chasing each other: writing the list from here raises its
        // SelectionChanged, and without the flag that would write the property straight
        // back and the program would be told about a choice nobody made.
        if (change.Property == SelectedItemProperty)
        {
            _suppressListEcho = true;
            PART_List.SelectedItem = SelectedItem;
            _suppressListEcho = false;
        }
    }

    private void OnButtonClick(object? sender, RoutedEventArgs e)
    {
        PART_Popup.IsOpen = !PART_Popup.IsOpen;

        if (!PART_Popup.IsOpen)
        {
            return;
        }

        // Open on the row that is chosen, so a person opening a list of three sees which of
        // the three they are on instead of having to hunt for it.
        _suppressListEcho = true;
        PART_List.SelectedItem = SelectedItem;
        _suppressListEcho = false;
        PART_List.ScrollIntoView(PART_List.SelectedItem);
    }

    private void OnListSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_suppressListEcho)
        {
            return;
        }

        if (PART_List.SelectedItem is null)
        {
            return;
        }

        SelectedItem = PART_List.SelectedItem;
        PART_Popup.IsOpen = false;
    }
}