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
    /// <para>
    /// The card is part of the window here, not decoration: the window has no system frame and
    /// a transparent background, so without it the form floated over whatever was behind the
    /// program and the fields had nothing to sit on. The eighteen pixels of margin are where
    /// the shadow falls.
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
            Width = 476;
            SizeToContent = SizeToContent.Height;
            CanResize = false;
            ShowInTaskbar = false;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = Brushes.Transparent;
            FontFamily = new FontFamily("Inter");

            // A sheet on the program's own background, like every other window here: a system
            // frame with its own buttons would look like another program's dialog.
            SystemDecorations = SystemDecorations.None;

            _email = Field("Почта", secret: false);
            _email.Bind(TextBox.TextProperty, new Binding(nameof(SiteLoginViewModel.Email))
            {
                Mode = BindingMode.TwoWay
            });

            _password = Field("Пароль", secret: true);
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
                Margin = new Thickness(0, 2, 0, 10)
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
                CornerRadius = new CornerRadius(12),
                BorderThickness = new Thickness(0),
                Cursor = new Cursor(StandardCursorType.Hand)
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

            Content = Build(close);

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

        /// <summary>
        /// One input on the card.
        /// </summary>
        /// <remarks>
        /// The theme's own box is a different grey from the card and the two looked like one
        /// surface with a faint line across it. A white box with a hairline border, on white
        /// paper, is the shape every other field in this program has.
        /// </remarks>
        private static TextBox Field(string watermark, bool secret) => new()
        {
            Watermark = watermark,
            PasswordChar = secret ? '●' : default,
            FontSize = 13,
            Padding = new Thickness(12, 9),
            CornerRadius = new CornerRadius(10),
            Background = ConfirmWindow.Brush("#FFFFFF"),
            BorderBrush = ConfirmWindow.Brush("#CBD5E1"),
            BorderThickness = new Thickness(1),
            Margin = new Thickness(0, 0, 0, 10)
        };

        private Control Build(Button close)
        {
            var heading = new TextBlock
            {
                Text = "Вход в аккаунт",
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

            var subtitle = new TextBlock
            {
                Text = "Почта и пароль от аккаунта на сайте фабрики.",
                FontSize = 12,
                Foreground = ConfirmWindow.Brush("#475569"),
                TextWrapping = TextWrapping.Wrap,
                LineHeight = 18,
                Margin = new Thickness(48, 6, 0, 18)
            };

            var rule = new Border
            {
                Height = 1,
                Background = ConfirmWindow.Brush("#F1F5F9"),
                Margin = new Thickness(0, 18, 0, 16)
            };

            var body = new StackPanel
            {
                Margin = new Thickness(24, 22, 24, 20),
                Spacing = 0,
                Children = { header, subtitle, _form, _already, rule, close }
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
