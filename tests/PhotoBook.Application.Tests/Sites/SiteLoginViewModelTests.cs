using System;
using System.Threading;
using System.Threading.Tasks;
using PhotoBook.Application;
using PhotoBook.Application.Sites;
using PhotoBook.Presentation.ViewModels;

namespace PhotoBook.Application.Tests.Sites
{
    /// <summary>
    /// The sign-in form is where a customer's password is typed, so what it keeps and what it
    /// clears are part of the contract rather than a matter of taste.
    /// </summary>
    public class SiteLoginViewModelTests
    {
        private static readonly DateTimeOffset Now = new(2026, 11, 10, 12, 0, 0, TimeSpan.Zero);

        private static SiteSession Session(string email = "customer@example.com") =>
            new(SiteRegion.Com, "token-value", Now.AddDays(7), email);

        [Fact]
        public async Task AsksForMissingFieldsWithoutCallingTheSites()
        {
            var auth = new FakeAuth(_ => Result<SiteSession>.Fail("unused"));
            var model = new SiteLoginViewModel(auth, new FakeStore());

            await model.SignInAsync();

            Assert.Equal("Введите почту и пароль.", model.Message);
            Assert.Equal(0, auth.Calls);
            Assert.False(model.IsSignedIn);
        }

        [Fact]
        public async Task ReportsAWrongPasswordAndClearsTheField()
        {
            var auth = new FakeAuth(_ => Result<SiteSession>.Fail("Неверная почта или пароль."));
            var store = new FakeStore();
            var model = new SiteLoginViewModel(auth, store)
            {
                Email = "customer@example.com",
                Password = "wrong"
            };

            await model.SignInAsync();

            Assert.Equal("Неверная почта или пароль.", model.Message);
            Assert.Equal(string.Empty, model.Password);
            Assert.False(model.IsSignedIn);
            Assert.Null(store.Saved);
            Assert.Equal("customer@example.com", auth.LastEmail);
        }

        [Fact]
        public async Task ReportsALostConnectionAsAConnectionProblem()
        {
            var auth = new FakeAuth(_ => Result<SiteSession>.Fail(
                "Нет связи с сайтом. Проверьте интернет и попробуйте снова."));
            var model = new SiteLoginViewModel(auth, new FakeStore())
            {
                Email = "customer@example.com",
                Password = "secret"
            };

            await model.SignInAsync();

            Assert.Equal("Нет связи с сайтом. Проверьте интернет и попробуйте снова.", model.Message);
            Assert.Equal(string.Empty, model.Password);
        }

        [Fact]
        public async Task KeepsTheSessionAndSaysWhoseAccountItIs()
        {
            var auth = new FakeAuth(email => Result<SiteSession>.Ok(Session(email)));
            var store = new FakeStore();
            var model = new SiteLoginViewModel(auth, store)
            {
                Email = "  customer@example.com  ",
                Password = "secret"
            };

            SiteSession? signedIn = null;
            model.SignedIn += (_, session) => signedIn = session;

            await model.SignInAsync();

            Assert.True(model.IsSignedIn);
            Assert.Equal("customer@example.com", auth.LastEmail);
            Assert.Equal("customer@example.com", store.Saved!.AccountEmail);
            Assert.Equal("customer@example.com", model.AccountText);
            Assert.Equal("customer@example.com", model.Email);
            Assert.Equal(string.Empty, model.Password);
            Assert.Equal(string.Empty, model.Message);
            Assert.NotNull(signedIn);
        }

        [Fact]
        public void StartsSignedInWhenASessionWasStored()
        {
            var store = new FakeStore { Stored = Session("restored@example.com") };
            var model = new SiteLoginViewModel(new FakeAuth(_ => Result<SiteSession>.Fail("unused")), store);

            Assert.True(model.IsSignedIn);
            Assert.Equal("restored@example.com", model.AccountText);
            Assert.Equal("restored@example.com", model.Email);
            Assert.Equal("Вы вошли в аккаунт restored@example.com.", model.SignedInText);
        }

        [Fact]
        public async Task SignsInEvenWhenTheSessionCannotBeStored()
        {
            var auth = new FakeAuth(_ => Result<SiteSession>.Ok(Session()));
            var model = new SiteLoginViewModel(auth, new FakeStore { FailOnSave = true })
            {
                Email = "customer@example.com",
                Password = "secret"
            };

            await model.SignInAsync();

            Assert.True(model.IsSignedIn);
            Assert.Contains("сохранить не удалось", model.Message);
        }

        [Fact]
        public async Task SaysWhatItIsDoingWhileTheSitesAreAsked()
        {
            var pending = new TaskCompletionSource<Result<SiteSession>>();
            var auth = new FakeAuth(_ => pending.Task);
            var model = new SiteLoginViewModel(auth, new FakeStore())
            {
                Email = "customer@example.com",
                Password = "secret"
            };

            var attempt = model.SignInAsync();

            Assert.True(model.IsBusy);
            Assert.False(model.CanSignIn);
            Assert.Equal("Проверяем…", model.SignInText);

            pending.SetResult(Result<SiteSession>.Ok(Session()));
            await attempt;

            Assert.False(model.IsBusy);
            Assert.Equal("Войти", model.SignInText);
        }

        private sealed class FakeAuth : ISiteAuthService
        {
            private readonly Func<string, Result<SiteSession>> _respond;
            private readonly Func<string, Task<Result<SiteSession>>>? _respondAsync;

            public FakeAuth(Func<string, Result<SiteSession>> respond) => _respond = respond;

            public FakeAuth(Func<string, Task<Result<SiteSession>>> respondAsync)
            {
                _respondAsync = respondAsync;
                _respond = _ => Result<SiteSession>.Fail("unused");
            }

            public int Calls { get; private set; }

            public string? LastEmail { get; private set; }

            public string? LastPassword { get; private set; }

            public Task<Result<SiteSession>> SignInAsync(
                string email,
                string password,
                CancellationToken cancellationToken = default)
            {
                Calls++;
                LastEmail = email;
                LastPassword = password;

                return _respondAsync is null
                    ? Task.FromResult(_respond(email))
                    : _respondAsync(email);
            }
        }

        private sealed class FakeStore : ISiteSessionStore
        {
            public SiteSession? Stored { get; set; }

            public SiteSession? Saved { get; private set; }

            public bool FailOnSave { get; set; }

            public SiteSession? Load() => Stored;

            public void Save(SiteSession session)
            {
                if (FailOnSave)
                    throw new InvalidOperationException("Windows не сохранила сессию, код 5.");

                Saved = session;
                Stored = session;
            }

            public void Clear() => Stored = null;
        }
    }
}
