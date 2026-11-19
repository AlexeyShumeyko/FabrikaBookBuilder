using System;
using System.Threading;
using System.Threading.Tasks;
using PhotoBook.Application;
using PhotoBook.Application.Sites;
using PhotoBook.Application.Sites.Catalog;

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
        private readonly ISiteProfileService _profiles;
        private readonly ISiteCatalogCache _catalog;
        private readonly Func<DateTimeOffset> _now;

        private string _email = string.Empty;
        private string _password = string.Empty;
        private string _message = string.Empty;
        private bool _isBusy;
        private SiteSession? _session;
        private SiteProfile? _profile;

        /// <summary>Raised once a session is in hand, so the shell can show whose account it is.</summary>
        public event EventHandler<SiteSession>? SignedIn;

        /// <summary>
        /// Raised when the site refused the stored token, so the shell can ask for the password
        /// before the customer runs into the refusal again.
        /// </summary>
        public event EventHandler? SessionExpired;

        public SiteLoginViewModel(
            ISiteAuthService auth,
            ISiteSessionStore store,
            ISiteProfileService profiles,
            ISiteCatalogCache catalog,
            IUiThread? ui = null,
            Func<DateTimeOffset>? now = null)
        {
            _auth = auth ?? throw new ArgumentNullException(nameof(auth));
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _profiles = profiles ?? throw new ArgumentNullException(nameof(profiles));
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            _now = now ?? (() => DateTimeOffset.UtcNow);

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

        /// <summary>What the header shows: the person's name, or the invitation to sign in.</summary>
        public string AccountText =>
            _profile?.DisplayName is { Length: > 0 } name
                ? name
                : _session?.AccountEmail is { Length: > 0 } address
                    ? address
                    : "Войти";

        /// <summary>The account's address, or nothing when nobody is signed in.</summary>
        public string EmailText => _session?.AccountEmail ?? string.Empty;

        /// <summary>What the site says the account is called, once it has been read.</summary>
        public SiteProfile? Profile => _profile;

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
                OnPropertyChanged(nameof(EmailText));

                // The window closes on this event, and the name is fetched afterwards: a slow
                // profile must not hold the sign-in window open after the password was accepted.
                SignedIn?.Invoke(this, session);

                await LoadProfileAsync(session, cancellationToken).ConfigureAwait(true);
            }
            finally
            {
                IsBusy = false;
            }
        }

        /// <summary>
        /// Drops a session the site has refused, keeping the address for the next attempt.
        /// </summary>
        /// <remarks>
        /// <para>
        /// This is a soft stop rather than an error: the token is gone, so the header goes back
        /// to the invitation and the sign-in window opens. Nothing is deleted except the token
        /// and the catalog cache, and the address stays filled in so the customer does not retype
        /// what the program already knows.
        /// </para>
        /// <para>
        /// The event is what lets the interrupted step continue: whoever was working holds its
        /// own state, and signs in through the same window a fresh start would use.
        /// </para>
        /// </remarks>
        public void ExpireSession()
        {
            if (_session is null)
                return;

            _store.Clear();
            _catalog.Clear();

            _session = null;
            _profile = null;
            Password = string.Empty;
            Message = "Сессия закончилась. Войдите снова.";

            OnPropertyChanged(nameof(IsSignedIn));
            OnPropertyChanged(nameof(AccountText));
            OnPropertyChanged(nameof(SignedInText));
            OnPropertyChanged(nameof(EmailText));
            OnPropertyChanged(nameof(Profile));

            SessionExpired?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// Reads the account's name after a restart, when only the stored session is known.
        /// </summary>
        /// <remarks>
        /// The header shows the address until this answers, so a lost connection costs a name
        /// and nothing else. It is asked once per run, not on a timer.
        /// </remarks>
        public async Task RefreshProfileAsync(CancellationToken cancellationToken = default)
        {
            if (_session is null || _profile is not null || IsBusy)
                return;

            await LoadProfileAsync(_session, cancellationToken).ConfigureAwait(true);
        }

        /// <summary>
        /// Forgets the account: the token, the name, and the catalog the site had loaded.
        /// </summary>
        /// <remarks>
        /// The cache goes with the token because the next person at this machine is a different
        /// account even when they are the same customer, and options read for one account are
        /// not a thing to hand to the next. The store journals a token Windows refused to
        /// remove, so a failed sign-out leaves a trace instead of looking clean.
        /// </remarks>
        public void SignOut()
        {
            if (_session is null)
                return;

            _store.Clear();
            _catalog.Clear();

            _session = null;
            _profile = null;
            Email = string.Empty;
            Password = string.Empty;
            Message = string.Empty;

            OnPropertyChanged(nameof(IsSignedIn));
            OnPropertyChanged(nameof(AccountText));
            OnPropertyChanged(nameof(SignedInText));
            OnPropertyChanged(nameof(EmailText));
            OnPropertyChanged(nameof(Profile));
        }

        /// <summary>
        /// Fills in the profile, or leaves the address standing in for it.
        /// </summary>
        /// <remarks>
        /// A profile that cannot be read is not a failed sign-in: the token works, projects can
        /// be listed, and the only thing missing is a nicer word in the header. The reason is
        /// not shown for the same reason - there is nothing for the customer to do about it.
        /// </remarks>
        private async Task LoadProfileAsync(SiteSession session, CancellationToken cancellationToken)
        {
            // A running clock says what the site would say: a token past its date is refused.
            // Asking anyway costs a request that can only come back unauthorised.
            if (session.IsExpiredAt(_now()))
            {
                ExpireSession();
                return;
            }

            var result = await _profiles.GetAsync(session, cancellationToken).ConfigureAwait(true);
            if (!result.IsSuccess || result.Value is null)
                return;

            _profile = result.Value;
            OnPropertyChanged(nameof(Profile));
            OnPropertyChanged(nameof(AccountText));
            OnPropertyChanged(nameof(EmailText));
        }
    }
}
