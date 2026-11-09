using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace PhotoBook.Shell.Views
{
    /// <summary>
    /// Something the user has to read and acknowledge.
    /// </summary>
    /// <remarks>
    /// One button, so it reads as the same window as the questions and not as a message from
    /// another program - which is what the system box it replaces looked like.
    /// </remarks>
    public class NotifyWindow : Window
    {
        private readonly string _message;
        private readonly string _okText;
        private readonly bool _destructive;

        public NotifyWindow(string okText, string message, bool destructive)
        {
            _okText = okText;
            _message = message;
            _destructive = destructive;

            Width = 440;
            SizeToContent = SizeToContent.Height;
            CanResize = false;
            ShowInTaskbar = false;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background = Brushes.White;

            Content = Build();

            KeyDown += (_, e) =>
            {
                if (e.Key == Key.Escape || e.Key == Key.Enter) Close();
            };
        }

        private Control Build()
        {
            var accent = _destructive ? ConfirmWindow.Brush("#DC2626") : ConfirmWindow.Brush("#4F46E5");

            var ok = new Button
            {
                Content = _okText,
                Padding = new Thickness(16, 9),
                Background = accent,
                Foreground = Brushes.White,
                FontSize = 12,
                FontWeight = FontWeight.SemiBold,
                CornerRadius = new CornerRadius(10),
                BorderThickness = new Thickness(0),
                HorizontalAlignment = HorizontalAlignment.Right
            };
            ok.Click += (_, _) => Close();

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
                        Foreground = ConfirmWindow.Brush("#334155"),
                        MaxWidth = 392
                    },
                    ok
                }
            };
        }
    }
}
