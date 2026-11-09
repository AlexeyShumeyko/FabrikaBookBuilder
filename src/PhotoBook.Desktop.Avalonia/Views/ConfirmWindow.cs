using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace PhotoBook.Shell.Views
{
    /// <summary>
    /// A yes/no question in this shell's own window.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Built in code rather than markup for one reason: this is the first window in the
    /// Avalonia shell, and the markup pipeline is the thing most likely to need three
    /// attempts. Once there is a second window and the shapes have settled, they move to
    /// markup together with the screens.
    /// </para>
    /// <para>
    /// Escape, the close button and "no" all mean no, and that is the only safe reading for a
    /// question whose answer can destroy something. Enter means yes, because a question the
    /// user cannot answer from the keyboard is half a question.
    /// </para>
    /// <para>
    /// Two different names live here and it matters which is which. The heading on screen is
    /// whatever the question is about - "Уменьшить структуру?" - and the window's own title
    /// is the same for every question in the program, "Подтверждение". The title is what a
    /// window is called, and a question that borrowed its heading for it meant there was no
    /// way to say "is a question open?" at all.
    /// </para>
    /// </remarks>
    public class ConfirmWindow : Window
    {
        private readonly bool _destructive;
        private readonly string _title;
        private readonly string _message;
        private readonly string _confirmText;

        public ConfirmWindow(string title, string confirmText, string message, bool destructive)
        {
            _destructive = destructive;
            _title = title;
            _message = message;
            _confirmText = confirmText;

            Width = 440;
            SizeToContent = SizeToContent.Height;
            CanResize = false;
            ShowInTaskbar = false;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background = Brushes.White;
            Title = "Подтверждение";

            Content = Build();

            KeyDown += OnKeyDown;
        }

        private void OnKeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape) Answer(false);
            if (e.Key == Key.Enter) Answer(true);
        }

        private Control Build()
        {
            var accent = _destructive ? Brush("#DC2626") : Brush("#4F46E5");

            // A question is marked by whether it can take something away, so the icon's two
            // colours are the whole of the warning: red when the answer can destroy, the
            // brand's own colours when it cannot.
            var iconTint = _destructive ? Brush("#DC2626") : Brush("#4F46E5");
            var iconTintPale = _destructive ? Brush("#FEF2F2") : Brush("#EEF2FF");

            var iconHost = new Border
            {
                Width = 36,
                Height = 36,
                CornerRadius = new CornerRadius(10),
                Background = iconTintPale,
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock
                {
                    // A private-use glyph, written as its number so that no round trip
                    // through a file can turn it into a different character.
                    Text = ((char)0xE7BA).ToString(),
                    FontFamily = new FontFamily("Segoe MDL2 Assets"),
                    FontSize = 15,
                    Foreground = iconTint,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };

            var heading = new Grid
            {
                Margin = new Thickness(0, 0, 0, 14),
                ColumnDefinitions =
                {
                    new ColumnDefinition(GridLength.Auto),
                    new ColumnDefinition(GridLength.Star)
                },
                Children =
                {
                    iconHost
                }
            };
            Grid.SetColumn(iconHost, 0);

            var headingText = new TextBlock
            {
                Text = _title,
                FontSize = 16,
                FontWeight = FontWeight.Bold,
                Foreground = Brush("#0F172A"),
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(12, 0, 0, 0)
            };
            Grid.SetColumn(headingText, 1);
            heading.Children.Add(headingText);

            // The message sits under the heading rather than beside the icon, which is why
            // it carries the icon's width as its own left edge.
            var message = new TextBlock
            {
                Text = _message,
                FontSize = 12,
                Foreground = Brush("#475569"),
                TextWrapping = TextWrapping.Wrap,
                LineHeight = 19,
                Margin = new Thickness(48, 0, 0, 0)
            };

            var rule = new Border
            {
                Height = 1,
                Background = Brush("#F1F5F9"),
                Margin = new Thickness(0, 20, 0, 18)
            };

            var confirm = new Button
            {
                Content = _confirmText,
                Padding = new Thickness(16, 9),
                Background = accent,
                Foreground = Brushes.White,
                FontSize = 12,
                FontWeight = FontWeight.SemiBold,
                CornerRadius = new CornerRadius(10),
                BorderThickness = new Thickness(0)
            };
            confirm.Click += (_, _) => Answer(true);

            var cancel = new Button
            {
                Content = "Отмена",
                Padding = new Thickness(16, 9),
                Background = Brush("#F1F5F9"),
                Foreground = Brush("#334155"),
                FontSize = 12,
                FontWeight = FontWeight.SemiBold,
                CornerRadius = new CornerRadius(10),
                BorderThickness = new Thickness(0)
            };
            cancel.Click += (_, _) => Answer(false);

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Spacing = 8,
                Children = { cancel, confirm }
            };

            return new StackPanel
            {
                Margin = new Thickness(24, 22, 24, 20),
                Spacing = 0,
                Children = { heading, message, rule, buttons }
            };
        }

        private void Answer(bool accepted) => Close(accepted);

        internal static IBrush Brush(string hex) => new SolidColorBrush(Color.Parse(hex));
    }
}
