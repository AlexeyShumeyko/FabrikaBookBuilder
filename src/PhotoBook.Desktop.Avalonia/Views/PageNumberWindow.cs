using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using PhotoBook.Shell.Services;

namespace PhotoBook.Shell.Views;

/// <summary>
/// Asks for a spread number to move a spread to.
/// </summary>
/// <remarks>
/// Built in code rather than in markup because it is the smallest of the windows and its
/// whole content is one field and two buttons. Every number in it - the width, the margins,
/// the corners, the colours - is the number the other shell uses, and they are written out
/// here next to what they are for.
/// </remarks>
public class PageNumberWindow : Window
{
    private readonly int _maxPageNumber;
    private readonly TextBox _field;

    public PageNumberWindow(int maxPageNumber)
    {
        _maxPageNumber = maxPageNumber;

        Title = "Назначить разворот";
        Width = 420;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brushes.Transparent;

        // No system frame. The reference draws these as a sheet on the app's own
        // background, and a title bar with its own minimise and close buttons is a
        // window belonging to something else.
        SystemDecorations = SystemDecorations.None;

        _field = new TextBox
        {
            FontSize = 14,
            Margin = new Thickness(0, 0, 0, 18),
            Watermark = $"От 1 до {maxPageNumber}"
        };
        ToolTip.SetTip(_field, $"От 1 до {maxPageNumber}");

        Content = Build();

        KeyDown += (_, e) =>
        {
            switch (e.Key)
            {
                case Key.Escape:
                    Close(null);
                    e.Handled = true;
                    break;

                case Key.Enter:
                    Accept();
                    e.Handled = true;
                    break;
            }
        };
    }

    private void Accept()
    {
        // The number is read back rather than trusted: the field can hold anything, and a
        // spread that does not exist is not something to discover at export time.
        if (int.TryParse(_field.Text, out int number) && number >= 1 && number <= _maxPageNumber)
            Close(number);
    }

    private Control Build()
    {
        var cancel = new Button
        {
            Content = "Отмена",
            Margin = new Thickness(0, 0, 8, 0),
            Padding = new Thickness(14, 9),
            FontSize = 12,
            FontWeight = FontWeight.SemiBold,
            CornerRadius = new CornerRadius(12),
            Background = ThemeResources.Brush("Surface"),
            BorderBrush = ThemeResources.Brush("BorderDefault"),
            BorderThickness = new Thickness(1),
            Foreground = ThemeResources.Brush("TextDefault"),
            HorizontalAlignment = HorizontalAlignment.Right
        };
        cancel.Click += (_, _) => Close(null);

        var accept = new Button
        {
            Content = "Переместить",
            Padding = new Thickness(16, 9),
            FontSize = 12,
            FontWeight = FontWeight.SemiBold,
            CornerRadius = new CornerRadius(12),
            Background = ThemeResources.Brush("Gold"),
            BorderThickness = new Thickness(0),
            Foreground = ThemeResources.Brush("OnAccent"),
            HorizontalAlignment = HorizontalAlignment.Right
        };
        accept.Click += (_, _) => Accept();

        return new Border
        {
            Background = ThemeResources.Brush("Surface"),
            CornerRadius = new CornerRadius(16),
            BorderBrush = ThemeResources.Brush("BorderDefault"),
            BorderThickness = new Thickness(1),
            BoxShadow = ThemeResources.Shadow("ShadowModal"),
            Child = new StackPanel
            {
                Margin = new Thickness(24),
                Spacing = 0,
                Children =
                {
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Margin = new Thickness(0, 0, 0, 16),
                        Children =
                        {
                            new Border
                            {
                                Width = 36,
                                Height = 36,
                                CornerRadius = new CornerRadius(10),
                                Background = ThemeResources.Brush("Brand50"),
                                VerticalAlignment = VerticalAlignment.Center,
                                Child = new TextBlock
                                {
                                    // The glyph as a code point, not as the character.
                                    // See the same note in the help window.
                                    Text = ((char)0xE8FD).ToString(),
                                    FontSize = 15,
                                    Foreground = ThemeResources.Brush("Accent"),
                                    HorizontalAlignment = HorizontalAlignment.Center,
                                    VerticalAlignment = VerticalAlignment.Center
                                }
                            },
                            new TextBlock
                            {
                                Text = "Назначить разворот",
                                FontSize = 16,
                                FontWeight = FontWeight.Bold,
                                Foreground = ThemeResources.Brush("TextStrong"),
                                Margin = new Thickness(12, 0, 0, 0),
                                VerticalAlignment = VerticalAlignment.Center
                            }
                        }
                    },

                    new TextBlock
                    {
                        Text = "Введите номер, на который нужно переместить этот разворот.",
                        FontSize = 12,
                        TextWrapping = TextWrapping.Wrap,
                        Foreground = ThemeResources.Brush("TextMuted"),
                        Margin = new Thickness(0, 0, 0, 12)
                    },

                    _field,

                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Children = { cancel, accept }
                    }
                }
            }
        };
    }
}
