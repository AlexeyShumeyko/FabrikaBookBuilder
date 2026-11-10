using System;
using System.Threading;
using System.Threading.Tasks;
using PhotoBook.Application;
using PhotoBook.Application.Sites;

namespace PhotoBook.Presentation.ViewModels
{
    /// <summary>
    /// The sign-in window's state: what was typed, what the program is doing, and what to say.
    /// </summary>
    /// <remarks>
    /// <para>
    /// There is no "remember password" here and no place to put one. The platform issues a
    /// seven-day token; that token is what the program keeps, and the password is used once and
    /// forgotten.
    /// </para>
    /// <para>
    /// The password field is cleared on every answer, not only on success. Leaving a wrong
    /// password in the box invites a second attempt with the same wrong text, and leaving a
    /// right one there leaves a secret on screen for whoever walks past.
    /// </para>
    /// <para>
    /// Messages are the ones the sign-in service produced, which are written for a person: a
    /// wrong password says so without hinting at which site the account lives on, and a lost
    /// connection is reported as a connection problem rather than as a refusal.
    /// </para>
    /// </remarks>
    public sealed class SiteLoginViewModel : ViewModelBase
    {
        private readonly ISiteAuthService _auth;
        private readonly ISiteSessionStore _store;

        private string _email = string.Empty;
        private string _password = string.Empty;
        private string _message = string.Empty;
        private bool _isBusy;
        private SiteSession? _session;

        /// <summary>Raised once a session is in hand, so the shell can show whose account it is.</summary>
        public event EventHandler<SiteSession>? SignedIn;

        public SiteLoginViewModel(ISiteAuthService auth, ISiteSessionStore store, IUiThread? ui = null)
        {
            _auth = auth ?? throw new ArgumentNullException(nameof(auth));
            _store = store ?? throw new ArgumentNullException(nameof(store));

            if (ui is not null)
                Ui = ui;

            // Opening the window with a live session already in hand is not an error: the
            // customer closed the program for an hour, not for a week.
            _session = _store.Load();
            if (_session?.AccountEmail is { Length: > 0 } restored)
                _email = restored;
        }

        /// <summary>The address typed, or the one the stored session was made with.</summary>
        public string Email
        {
            get => _email;
            set => SetProperty(ref _email, value ?? string.Empty);
        }

        /// <summary>Never shown, never stored, and emptied after every attempt.</summary>
        public string Password
        {
            get => _password;
            set => SetProperty(ref _password, value ?? string.Empty);
        }

        /// <summary>What went wrong, in words for the person in front of the window.</summary>
        public string Message
        {
            get => _message;
            set
            {
                if (SetProperty(ref _message, value ?? string.Empty))
                    OnPropertyChanged(nameof(HasMessage));
            }
        }

        /// <summary>Whether there is anything to show above the button.</summary>
        public bool HasMessage => _message.Length > 0;

        /// <summary>The button's own caption, which says what the program is doing.</summary>
        public string SignInText => IsBusy ? "Проверяем…" : "Войти";

        /// <summary>What the window says instead of the form when a session is already held.</summary>
        public string SignedInText => _session?.AccountEmail is { Length: > 0 } address
            ? "Вы вошли в аккаунт " + address + "."
            : "Вы уже вошли в аккаунт.";

        /// <summary>True while the two sites are being asked; the form is disabled then.</summary>
        public bool IsBusy
        {
            get => _isBusy;
            private set
            {
                if (!SetProperty(ref _isBusy, value)) return;

                OnPropertyChanged(nameof(CanSignIn));
                OnPropertyChanged(nameof(SignInText));
            }
        }

        /// <summary>Whether the button may be pressed right now.</summary>
        public bool CanSignIn => !IsBusy;

        /// <summary>Whether a usable session is already in hand.</summary>
        public bool IsSignedIn => _session is not null;

        /// <summary>What the header shows: the account's address, or the invitation to sign in.</summary>
        public string AccountText =>
            _session?.AccountEmail is { Length: > 0 } address ? address : "Войти";

        /// <summary>
        /// Signs in on whichever site owns the account, and keeps the session if one comes back.
        /// </summary>
        /// <remarks>
        /// An empty form is answered without a request: two sites asked for nothing is load on
        /// a shared platform for no reason at all.
        /// </remarks>
        public async Task SignInAsync(CancellationToken cancellationToken = default)
        {
            if (IsBusy)
                return;

            Message = string.Empty;

            if (string.IsNullOrWhiteSpace(Email) || string.IsNullOrEmpty(Password))
            {
                Message = "Введите почту и пароль.";
                return;
            }

            IsBusy = true;

            try
            {
                var result = await _auth
                    .SignInAsync(Email.Trim(), Password, cancellationToken)
                    .ConfigureAwait(true);

                if (!result.IsSuccess || result.Value is null)
                {
                    Message = result.Error ?? "Не удалось войти. Попробуйте позже.";
                    Password = string.Empty;
                    return;
                }

                var session = result.Value;
                _session = session;
                Email = session.AccountEmail ?? Email.Trim();
                Password = string.Empty;

                try
                {
                    _store.Save(session);
                }
                catch (Exception error) when (error is InvalidOperationException or UnauthorizedAccessException)
                {
                    // The session works for this run; it just will not survive a restart.
                    Message = "Вход выполнен, но сессию сохранить не удалось — после перезапуска потребуется войти снова.";
                }

                OnPropertyChanged(nameof(IsSignedIn));
                OnPropertyChanged(nameof(AccountText));
                OnPropertyChanged(nameof(SignedInText));
                SignedIn?.Invoke(this, session);
            }
            finally
            {
                IsBusy = false;
            }
        }
    }
}
