using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
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
            Width = 400;
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
                Margin = new Thickness(0, 0, 0, 14)
            };

            var name = new TextBlock
            {
                FontSize = 14,
                FontWeight = FontWeight.SemiBold,
                Foreground = ConfirmWindow.Brush("#0F172A"),
                TextWrapping = TextWrapping.Wrap
            };
            name.Bind(TextBlock.TextProperty, new Binding(nameof(SiteLoginViewModel.AccountText)) { Source = _model });

            var email = new TextBlock
            {
                FontSize = 12,
                Foreground = ConfirmWindow.Brush("#475569"),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 4, 0, 0)
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
                Foreground = ConfirmWindow.Brush("#B91C1C"),
                FontSize = 12,
                FontWeight = FontWeight.SemiBold,
                CornerRadius = new CornerRadius(10),
                BorderThickness = new Thickness(0)
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
                Background = ConfirmWindow.Brush("#F1F5F9"),
                Foreground = ConfirmWindow.Brush("#334155"),
                FontSize = 12,
                FontWeight = FontWeight.SemiBold,
                CornerRadius = new CornerRadius(10),
                BorderThickness = new Thickness(0)
            };
            close.Click += (_, _) => Close();

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Spacing = 8,
                Children = { signOut, close }
            };

            return new StackPanel
            {
                Margin = new Thickness(24, 22, 24, 20),
                Spacing = 0,
                Children = { heading, name, email, rule, buttons }
            };
        }
    }
}
