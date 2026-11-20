using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using PhotoBook.Presentation.ViewModels;
using PhotoBook.Shell.Services;

namespace PhotoBook.Shell.Views
{
    /// <summary>
    /// Who is signed in, and the way out of the account.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The header has room for one line, so it carries the name. The address is here, where
    /// there is room for it and where somebody checking whether they are in the right account
    /// will look.
    /// </para>
    /// <para>
    /// Signing out is not confirmed: it destroys nothing the customer cannot get back by typing
    /// the password again, and a question in front of a reversible action teaches people to
    /// click through questions.
    /// </para>
    /// </remarks>
    public class AccountWindow : Window
    {
        private readonly SiteLoginViewModel _model;

        public AccountWindow(SiteLoginViewModel model)
        {
            _model = model ?? throw new ArgumentNullException(nameof(model));

            Title = "Аккаунт";
            Width = 436;
            SizeToContent = SizeToContent.Height;
            CanResize = false;
            ShowInTaskbar = false;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = Brushes.Transparent;
            SystemDecorations = SystemDecorations.None;

            Content = Build();

            KeyDown += (_, e) =>
            {
                if (e.Key != Avalonia.Input.Key.Escape)
                    return;

                Close();
                e.Handled = true;
            };
        }

        private Control Build()
        {
            var heading = new TextBlock
            {
                Text = "Аккаунт",
                FontSize = 16,
                FontWeight = FontWeight.Bold,
                Foreground = ThemeResources.Brush("TextStrong"),
                VerticalAlignment = VerticalAlignment.Center
            };

            var tile = new Border
            {
                Width = 36,
                Height = 36,
                CornerRadius = new CornerRadius(10),
                Background = ThemeResources.Brush("Brand50"),
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock
                {
                    Text = ((char)0xE77B).ToString(),
                    FontFamily = new FontFamily("Segoe MDL2 Assets"),
                    FontSize = 15,
                    Foreground = ThemeResources.Brush("Accent"),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };

            Grid.SetColumn(tile, 0);
            Grid.SetColumn(heading, 1);

            var header = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("Auto,*"),
                Children = { tile, heading }
            };

            var name = new TextBlock
            {
                FontSize = 14,
                FontWeight = FontWeight.SemiBold,
                Foreground = ThemeResources.Brush("TextStrong"),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(48, 16, 0, 0)
            };
            name.Bind(TextBlock.TextProperty, new Binding(nameof(SiteLoginViewModel.AccountText)) { Source = _model });

            var email = new TextBlock
            {
                FontSize = 12,
                Foreground = ThemeResources.Brush("TextMuted"),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(48, 4, 0, 0)
            };
            email.Bind(TextBlock.TextProperty, new Binding(nameof(SiteLoginViewModel.EmailText)) { Source = _model });

            var rule = new Border
            {
                Height = 1,
                Background = ThemeResources.Brush("BorderDefault"),
                Margin = new Thickness(0, 18, 0, 16)
            };

            var signOut = new Button
            {
                Content = "Выйти",
                Padding = new Thickness(16, 9),
                Background = ThemeResources.Brush("Red50"),
                BorderBrush = ThemeResources.Brush("Red200"),
                BorderThickness = new Thickness(1),
                Foreground = ThemeResources.Brush("Red700"),
                FontSize = 12,
                FontWeight = FontWeight.SemiBold,
                CornerRadius = new CornerRadius(12),
                Cursor = new Cursor(StandardCursorType.Hand)
            };
            signOut.Click += (_, _) =>
            {
                _model.SignOut();
                Close();
            };

            var close = new Button
            {
                Content = "Закрыть",
                Padding = new Thickness(16, 9),
                Background = ThemeResources.Brush("Surface"),
                BorderBrush = ThemeResources.Brush("BorderDefault"),
                BorderThickness = new Thickness(1),
                Foreground = ThemeResources.Brush("TextDefault"),
                FontSize = 12,
                FontWeight = FontWeight.SemiBold,
                CornerRadius = new CornerRadius(12),
                Cursor = new Cursor(StandardCursorType.Hand)
            };
            close.Click += (_, _) => Close();

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Spacing = 8,
                Children = { signOut, close }
            };

            var body = new StackPanel
            {
                Margin = new Thickness(24, 22, 24, 20),
                Spacing = 0,
                Children = { header, name, email, rule, buttons }
            };

            return new Border
            {
                Margin = new Thickness(18),
                Background = ThemeResources.Brush("Surface"),
                CornerRadius = new CornerRadius(16),
                BorderBrush = ThemeResources.Brush("BorderDefault"),
                BorderThickness = new Thickness(1),
                BoxShadow = ThemeResources.Shadow("ShadowModal"),
                Child = body
            };
        }
    }
}
