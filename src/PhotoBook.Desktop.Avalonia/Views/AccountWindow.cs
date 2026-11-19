using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using PhotoBook.Presentation.ViewModels;

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
            FontFamily = new FontFamily("Inter");
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
                Foreground = ConfirmWindow.Brush("#0F172A"),
                VerticalAlignment = VerticalAlignment.Center
            };

            var tile = new Border
            {
                Width = 36,
                Height = 36,
                CornerRadius = new CornerRadius(10),
                Background = ConfirmWindow.Brush("#EEF2FF"),
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock
                {
                    Text = ((char)0xE77B).ToString(),
                    FontFamily = new FontFamily("Segoe MDL2 Assets"),
                    FontSize = 15,
                    Foreground = ConfirmWindow.Brush("#4F46E5"),
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
                Foreground = ConfirmWindow.Brush("#0F172A"),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(48, 16, 0, 0)
            };
            name.Bind(TextBlock.TextProperty, new Binding(nameof(SiteLoginViewModel.AccountText)) { Source = _model });

            var email = new TextBlock
            {
                FontSize = 12,
                Foreground = ConfirmWindow.Brush("#475569"),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(48, 4, 0, 0)
            };
            email.Bind(TextBlock.TextProperty, new Binding(nameof(SiteLoginViewModel.EmailText)) { Source = _model });

            var rule = new Border
            {
                Height = 1,
                Background = ConfirmWindow.Brush("#F1F5F9"),
                Margin = new Thickness(0, 18, 0, 16)
            };

            var signOut = new Button
            {
                Content = "Выйти",
                Padding = new Thickness(16, 9),
                Background = ConfirmWindow.Brush("#FEF2F2"),
                BorderBrush = ConfirmWindow.Brush("#FECACA"),
                BorderThickness = new Thickness(1),
                Foreground = ConfirmWindow.Brush("#B91C1C"),
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
                Background = ConfirmWindow.Brush("#FFFFFF"),
                BorderBrush = ConfirmWindow.Brush("#E2E8F0"),
                BorderThickness = new Thickness(1),
                Foreground = ConfirmWindow.Brush("#334155"),
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
                Background = ConfirmWindow.Brush("#FFFFFF"),
                CornerRadius = new CornerRadius(16),
                BorderBrush = ConfirmWindow.Brush("#E2E8F0"),
                BorderThickness = new Thickness(1),
                BoxShadow = new BoxShadows(BoxShadow.Parse("0 12 50 0 #40000000")),
                Child = body
            };
        }
    }
}
