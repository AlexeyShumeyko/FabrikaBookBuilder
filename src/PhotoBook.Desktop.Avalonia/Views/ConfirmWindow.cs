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
    /// </remarks>
    public class ConfirmWindow : Window
    {
        private readonly bool _destructive;
        private readonly string _message;
        private readonly string _confirmText;

        public ConfirmWindow(string confirmText, string message, bool destructive)
        {
            _destructive = destructive;
            _message = message;
            _confirmText = confirmText;

            Width = 440;
            SizeToContent = SizeToContent.Height;
            CanResize = false;
            ShowInTaskbar = false;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background = Brushes.White;

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
                Margin = new Thickness(24),
                Spacing = 20,
                Children =
                {
                    new TextBlock
                    {
                        Text = _message,
                        TextWrapping = TextWrapping.Wrap,
                        FontSize = 13,
                        Foreground = Brush("#334155"),
                        MaxWidth = 392
                    },
                    buttons
                }
            };
        }

        private void Answer(bool accepted) => Close(accepted);

        internal static IBrush Brush(string hex) => new SolidColorBrush(Color.Parse(hex));
    }
}