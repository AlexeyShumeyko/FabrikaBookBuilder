using System;
using System.Diagnostics;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using PhotoBook.Infrastructure;

namespace PhotoBook.Shell.Views;

/// <summary>
/// The help button's answer for now: what is being prepared, and where the journal is.
/// </summary>
/// <remarks>
/// <para>
/// What a customer can be given today is the journal. The program records every failure in
/// one file, and without that file an answer to "it did not work" is a guess. So the window
/// says two things - what is being prepared, and where the journal is - and offers one click
/// to open the folder it is in.
/// </para>
/// <para>
/// The path is in a read-only box rather than a label with a "copy" button beside it: a
/// ghost button on white paper is invisible until you know it is there, and a box can be
/// selected and copied without being told how.
/// </para>
/// </remarks>
public class HelpWindow : Window
{
    public HelpWindow()
    {
        Title = "Помощь";
        Width = 520;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brushes.Transparent;
        FontFamily = new FontFamily("Inter");

        Content = Build();

        KeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape && e.Key != Key.Enter) return;
            Close();
            e.Handled = true;
        };
    }

    private Control Build()
    {
        var close = new Button
        {
            Content = "Понятно",
            MinWidth = 104,
            Padding = new Thickness(16, 9),
            FontSize = 12,
            FontWeight = FontWeight.SemiBold,
            CornerRadius = new CornerRadius(12),
            Background = ConfirmWindow.Brush("#4F46E5"),
            BorderThickness = new Thickness(0),
            Foreground = Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Right,
            Cursor = new Cursor(StandardCursorType.Hand)
        };
        close.Click += (_, _) => Close();

        var openFolder = new Button
        {
            Content = "Открыть папку с журналом",
            Padding = new Thickness(14, 9),
            FontSize = 12,
            FontWeight = FontWeight.SemiBold,
            CornerRadius = new CornerRadius(12),
            Background = ConfirmWindow.Brush("#FFFFFF"),
            BorderBrush = ConfirmWindow.Brush("#E2E8F0"),
            BorderThickness = new Thickness(1),
            Foreground = ConfirmWindow.Brush("#334155"),
            HorizontalAlignment = HorizontalAlignment.Left,
            Cursor = new Cursor(StandardCursorType.Hand)
        };
        openFolder.Click += (_, _) => OpenJournalFolder();

        var body = new StackPanel { Margin = new Thickness(24, 22, 24, 20), Spacing = 0 };

        body.Children.Add(Header());
        body.Children.Add(Line(
            "Здесь будет полное описание программы: что делает каждая функция, как устроена работа с проектом, ответы на частые вопросы и сведения о том, что программа делает с фотографиями и где их хранит.",
            48));

        body.Children.Add(new Border { Height = 1, Background = ConfirmWindow.Brush("#F1F5F9"), Margin = new Thickness(0, 18, 0, 16) });

        body.Children.Add(Line("Пока раздела нет, вот что поможет быстрее:", 48, heading: true));

        var card = new StackPanel { Spacing = 4 };
        card.Children.Add(new TextBlock
        {
            Text = "Если что-то не сработало",
            FontSize = 13,
            FontWeight = FontWeight.SemiBold,
            Foreground = ConfirmWindow.Brush("#334155")
        });
        card.Children.Add(new TextBlock
        {
            Text = "Программа записывает каждую ошибку в один файл. Приложите его к обращению в поддержку - по нему будет видно, что именно произошло.",
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            LineHeight = 18,
            Foreground = ConfirmWindow.Brush("#64748B"),
            Margin = new Thickness(0, 4, 0, 0)
        });
        card.Children.Add(new Border { Height = 1, Background = ConfirmWindow.Brush("#E2E8F0"), Margin = new Thickness(0, 12, 0, 10) });
        card.Children.Add(new TextBlock { Text = "Журнал:", FontSize = 12, Foreground = ConfirmWindow.Brush("#64748B") });
        card.Children.Add(JournalPathBox());

        body.Children.Add(new Border
        {
            Background = ConfirmWindow.Brush("#F8FAFC"),
            CornerRadius = new CornerRadius(8),
            BorderBrush = ConfirmWindow.Brush("#F1F5F9"),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(14, 12),
            Margin = new Thickness(48, 10, 0, 0),
            Child = card
        });

        var openRow = new StackPanel { Margin = new Thickness(48, 12, 0, 0) };
        openRow.Children.Add(openFolder);
        body.Children.Add(openRow);

        body.Children.Add(new Border { Height = 1, Background = ConfirmWindow.Brush("#F1F5F9"), Margin = new Thickness(0, 18, 0, 16) });

        var closeRow = new StackPanel { Orientation = Orientation.Horizontal };
        closeRow.Children.Add(close);
        body.Children.Add(closeRow);

        return new Border
        {
            Background = ConfirmWindow.Brush("#FFFFFF"),
            CornerRadius = new CornerRadius(16),
            BorderBrush = ConfirmWindow.Brush("#E2E8F0"),
            BorderThickness = new Thickness(1),
            BoxShadow = new BoxShadows(BoxShadow.Parse("0 12 50 0 #40000000")),
            Child = body
        };
    }

    private static Control Header() => new Grid
    {
        ColumnDefinitions = new ColumnDefinitions("Auto,*"),
        Margin = new Thickness(0, 0, 0, 16),
        Children =
        {
            new Border
            {
                Width = 36,
                Height = 36,
                CornerRadius = new CornerRadius(10),
                Background = ConfirmWindow.Brush("#EEF2FF"),
                VerticalAlignment = VerticalAlignment.Center,
                Child = Glyph("", 15, "#4F46E5")
            },
            new StackPanel
            {
                Margin = new Thickness(12, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Children =
                {
                    new TextBlock
                    {
                        Text = "Помощь",
                        FontSize = 16,
                        FontWeight = FontWeight.Bold,
                        Foreground = ConfirmWindow.Brush("#0F172A")
                    },
                    new TextBlock
                    {
                        Text = "Раздел готовится и появится в ближайших обновлениях",
                        FontSize = 12,
                        Foreground = ConfirmWindow.Brush("#64748B"),
                        Margin = new Thickness(0, 2, 0, 0)
                    }
                }
            }
        }
    };

    private static Control JournalPathBox()
    {
        var box = new TextBox
        {
            Text = Diagnostics.JournalPath,
            IsReadOnly = true,
            TextWrapping = TextWrapping.Wrap,
            FontFamily = new FontFamily("Consolas"),
            FontSize = 12,
            Padding = new Thickness(8, 6),
            CornerRadius = new CornerRadius(8),
            Background = ConfirmWindow.Brush("#FFFFFF"),
            BorderBrush = ConfirmWindow.Brush("#E2E8F0"),
            BorderThickness = new Thickness(1),
            Margin = new Thickness(0, 4, 0, 0)
        };

        return box;
    }

    /// <summary>
    /// Opens the folder the journal is in, and creates it if it is not there yet.
    /// </summary>
    /// <remarks>
    /// Explorer refusing is not worth a message box: the path is on screen two lines above,
    /// so the person can get there without us. What is worth doing is making the folder
    /// exist, so that the very next failure has somewhere to be written.
    /// </remarks>
    private static void OpenJournalFolder()
    {
        try
        {
            var folder = Path.GetDirectoryName(Diagnostics.JournalPath);
            if (string.IsNullOrEmpty(folder)) return;

            Directory.CreateDirectory(folder);

            Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true });
        }
        catch
        {
            // The path is on screen. Nothing to add by interrupting the person about it.
        }
    }

    private static Control Line(string text, double left, bool heading = false) => new TextBlock
    {
        Text = text,
        FontSize = 13,
        TextWrapping = TextWrapping.Wrap,
        LineHeight = 20,
        Foreground = ConfirmWindow.Brush(heading ? "#334155" : "#475569"),
        FontWeight = heading ? FontWeight.SemiBold : FontWeight.Normal,
        Margin = new Thickness(left, 0, 0, 0)
    };

    private static Control Glyph(string code, double size, string brush) => new TextBlock
    {
        Text = code,
        FontFamily = new FontFamily("Segoe MDL2 Assets"),
        FontSize = size,
        Foreground = ConfirmWindow.Brush(brush),
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center
    };
}
