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
    /// Asks for the account on the site: an address, a password, and one button.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The same password works on both sites and the customer is never asked which one the
    /// account belongs to - the sign-in service asks both and keeps the answer. Nothing here
    /// mentions a region, so a message cannot suggest that the wrong site was picked.
    /// </para>
    /// <para>
    /// There is no "remember password": the program keeps the platform's token, not the
    /// password, and the field is emptied after every attempt. Escape closes, Enter signs in.
    /// </para>
    /// <para>
    /// A window opened while a session is already stored says whose account it is instead of
    /// asking for the same password again - the token lasts seven days, and the customer
    /// closed the program for lunch, not for a week.
    /// </para>
    /// </remarks>
    public class LoginWindow : Window
    {
        private readonly SiteLoginViewModel _model;
        private readonly TextBox _email;
        private readonly TextBox _password;
        private readonly TextBlock _message;
        private readonly Button _submit;
        private readonly StackPanel _form;
        private readonly StackPanel _already;

        public LoginWindow(SiteLoginViewModel model)
        {
            _model = model ?? throw new ArgumentNullException(nameof(model));
            DataContext = model;

            Title = "Вход в аккаунт";
            Width = 440;
            SizeToContent = SizeToContent.Height;
            CanResize = false;
            ShowInTaskbar = false;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = Brushes.Transparent;
            FontFamily = new FontFamily("Inter");

            // A sheet on the program's own background, like every other window here: a system
            // frame with its own buttons would look like another program's dialog.
            SystemDecorations = SystemDecorations.None;

            _email = new TextBox
            {
                Watermark = "Почта",
                FontSize = 14,
                Margin = new Thickness(0, 0, 0, 10)
            };
            _email.Bind(TextBox.TextProperty, new Binding(nameof(SiteLoginViewModel.Email))
            {
                Mode = BindingMode.TwoWay
            });

            _password = new TextBox
            {
                Watermark = "Пароль",
                PasswordChar = '●',
                FontSize = 14,
                Margin = new Thickness(0, 0, 0, 10)
            };
            _password.Bind(TextBox.TextProperty, new Binding(nameof(SiteLoginViewModel.Password))
            {
                Mode = BindingMode.TwoWay
            });

            _message = new TextBlock
            {
                FontSize = 12,
                Foreground = ConfirmWindow.Brush("#B91C1C"),
                TextWrapping = TextWrapping.Wrap,
                LineHeight = 18,
                Margin = new Thickness(0, 0, 0, 10)
            };
            _message.Bind(TextBlock.TextProperty, new Binding(nameof(SiteLoginViewModel.Message)));
            _message.Bind(IsVisibleProperty, new Binding(nameof(SiteLoginViewModel.HasMessage)));

            _submit = new Button
            {
                Content = "Войти",
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                Padding = new Thickness(16, 10),
                Background = ConfirmWindow.Brush("#4F46E5"),
                Foreground = Brushes.White,
                FontSize = 13,
                FontWeight = FontWeight.SemiBold,
                CornerRadius = new CornerRadius(10),
                BorderThickness = new Thickness(0)
            };
            _submit.Bind(ContentControl.ContentProperty, new Binding(nameof(SiteLoginViewModel.SignInText)));
            _submit.Bind(IsEnabledProperty, new Binding(nameof(SiteLoginViewModel.CanSignIn)));
            _submit.Click += async (_, _) => await SignInAsync();

            _form = new StackPanel
            {
                Spacing = 0,
                Children = { _email, _password, _message, _submit }
            };

            var close = new Button
            {
                Content = "Закрыть",
                HorizontalAlignment = HorizontalAlignment.Right,
                Padding = new Thickness(16, 9),
                Background = ConfirmWindow.Brush("#F1F5F9"),
                Foreground = ConfirmWindow.Brush("#334155"),
                FontSize = 12,
                FontWeight = FontWeight.SemiBold,
                CornerRadius = new CornerRadius(10),
                BorderThickness = new Thickness(0)
            };
            close.Click += (_, _) => Close();

            var who = new TextBlock
            {
                FontSize = 13,
                Foreground = ConfirmWindow.Brush("#334155"),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 14)
            };
            who.Bind(TextBlock.TextProperty, new Binding(nameof(SiteLoginViewModel.SignedInText)));

            _already = new StackPanel
            {
                Spacing = 12,
                Children = { who }
            };
            _already.Bind(IsVisibleProperty, new Binding(nameof(SiteLoginViewModel.IsSignedIn)));
            _form.Bind(IsVisibleProperty, new Binding("!IsSignedIn"));

            Content = Build();

            model.SignedIn += (_, _) => Close(true);

            KeyDown += async (_, e) =>
            {
                switch (e.Key)
                {
                    case Key.Escape:
                        Close();
                        e.Handled = true;
                        break;

                    case Key.Enter:
                        e.Handled = true;
                        await SignInAsync();
                        break;
                }
            };

            // Typing goes straight into the address once the window is up; a form that needs a
            // click before the first key is a form that starts with a wasted second.
            if (!model.IsSignedIn)
                Opened += (_, _) => _email.Focus();
        }

        private async System.Threading.Tasks.Task SignInAsync()
        {
            if (!_model.CanSignIn)
                return;

            await _model.SignInAsync();

            // The password field is emptied by the view model; the caret belongs back in it,
            // so a corrected attempt starts where the mistake was.
            if (!_model.IsSignedIn)
                _password.Focus();
        }

        private Control Build()
        {
            var heading = new TextBlock
            {
                Text = "Вход в аккаунт",
                FontSize = 16,
                FontWeight = FontWeight.Bold,
                Foreground = ConfirmWindow.Brush("#0F172A"),
                Margin = new Thickness(0, 0, 0, 6)
            };

            var subtitle = new TextBlock
            {
                Text = "Почта и пароль от аккаунта на сайте фабрики.",
                FontSize = 12,
                Foreground = ConfirmWindow.Brush("#475569"),
                TextWrapping = TextWrapping.Wrap,
                LineHeight = 18,
                Margin = new Thickness(0, 0, 0, 18)
            };

            var rule = new Border
            {
                Height = 1,
                Background = ConfirmWindow.Brush("#F1F5F9"),
                Margin = new Thickness(0, 18, 0, 16)
            };

            var close = new Button
            {
                Content = "Закрыть",
                HorizontalAlignment = HorizontalAlignment.Right,
                Padding = new Thickness(16, 9),
                Background = ConfirmWindow.Brush("#F1F5F9"),
                Foreground = ConfirmWindow.Brush("#334155"),
                FontSize = 12,
                FontWeight = FontWeight.SemiBold,
                CornerRadius = new CornerRadius(10),
                BorderThickness = new Thickness(0)
            };
            close.Click += (_, _) => Close();

            var footer = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Children = { close }
            };

            return new StackPanel
            {
                Margin = new Thickness(24, 22, 24, 20),
                Spacing = 0,
                Children = { heading, subtitle, _form, _already, rule, footer }
            };
        }
    }
}
