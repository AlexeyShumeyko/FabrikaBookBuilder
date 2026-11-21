using System;
using Avalonia.Controls;
using PhotoBook.Application;
using PhotoBook.Core;
using PhotoBook.Presentation.ViewModels;

namespace PhotoBook.Shell.Navigation
{
    /// <summary>
    /// Where this shell is, and how it gets somewhere else.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The project list needs four things from the shell around it: the current mode, how to
    /// open a project, how to end a session, and where an opened project should be handed.
    /// Everything else it decides for itself.
    /// </para>
    /// <para>
    /// <b>The navigator drives the window.</b> It is the one that knows which screen a mode
    /// means, and it is the one that makes sure an editor has its project before its screen
    /// exists to show it. The window holds whatever it is given - the same shape as the WPF
    /// shell's navigator, which is the point: one behaviour, written twice.
    /// </para>
    /// </remarks>
    public class AvaloniaNavigator : IShellNavigator
    {
        private readonly IScreenViewFactory _screens;
        private readonly UniqueFoldersViewModel _uniqueFolders;
        private readonly CombinedModeViewModel _combined;
        private readonly SiteOrderViewModel _siteOrder;
        private readonly SiteUploadViewModel _upload;
        private readonly Application.Sites.ISiteSessionStore _sessions;

        private ContentControl? _host;
        private AppMode _currentMode = AppMode.ProjectList;
        private AppMode? _previousMode;
        private AppMode? _sessionMode;

        /// <summary>Whether a save of the order's choices is already running.</summary>
        private bool _savingChoice;

        /// <summary>What the shell will do once a password arrives, when it had to stop and ask.</summary>
        private Action? _afterSignIn;
        private (Project Project, ProjectInfo Record)? _pending;

        public AvaloniaNavigator(
            IScreenViewFactory screens,
            UniqueFoldersViewModel uniqueFolders,
            CombinedModeViewModel combined,
            SiteOrderViewModel siteOrder,
            SiteUploadViewModel upload,
            Application.Sites.ISiteSessionStore sessions,
            Func<ProjectListViewModel> projectListProvider)
        {
            _screens = screens;
            _uniqueFolders = uniqueFolders;
            _combined = combined;
            _siteOrder = siteOrder;
            _upload = upload;
            _sessions = sessions;
            ProjectListProvider = projectListProvider;

            // The header reads the name off the editor rather than keeping a copy, so that a
            // copy cannot fall behind. A copy needs telling, though, and a rename is neither
            // a new project nor a new screen - it is the same project under a name someone
            // just typed, which is why neither of the events above would ever have fired.
            _uniqueFolders.PropertyChanged += OnEditorPropertyChanged;
            _combined.PropertyChanged += OnEditorPropertyChanged;

            // An export finishes the session, and that is the one thing an editor cannot do
            // for itself: it has no way to say which screen comes next. The other shell
            // connects the same two events to the same call, and this one was missing them -
            // so after a finished export the editor stayed on screen with a project that the
            // list already showed as ready to print.
            _uniqueFolders.ProjectExported += (_, _) => EndSession();
            _combined.ProjectExported += (_, _) => EndSession();

            // The order page is a page of the open project rather than a session of its own:
            // leaving it puts the editor back under the same project, untouched.
            _uniqueFolders.OrderRequested += (_, _) => OpenOrder(AppMode.UniqueFolders);
            _combined.OrderRequested += (_, _) => OpenOrder(AppMode.Combined);
            _siteOrder.BackRequested += (_, _) => CurrentMode = _sessionMode ?? AppMode.ProjectList;
            _siteOrder.ChoiceChanged += (_, _) => SaveChoice();

            // Sending is a screen of its own: the order page hands over a priced plan, and
            // this one shows what will be written before anything is written.
            _siteOrder.UploadRequested += (_, _) => OpenUpload();

            // An expired token is the shell's business: the screen only knows the site refused
            // it, and a password belongs in the window that asks for passwords.
            _upload.NeedsSignIn += (_, _) => SignInRequested?.Invoke(this, EventArgs.Empty);
        }

        private void OnEditorPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName is nameof(UniqueFoldersViewModel.ProjectName)) ProjectNameChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>Builds the next projects list, once per visit.</summary>
        public Func<ProjectListViewModel> ProjectListProvider { get; }

        /// <summary>Raised when the upload screen is ready and the shell should show it.</summary>
        /// <remarks>
        /// The shell decides how: a window over the order page, not a page of its own, so the
        /// customer stays where the order was assembled while it is being written.
        /// </remarks>
        public event EventHandler? UploadWindowRequested;

        /// <summary>The upload screen, prepared and ready to be shown.</summary>
        public SiteUploadViewModel UploadScreen => _upload;

        /// <summary>Raised when a screen needs a password the shell has to ask for.</summary>
        public event EventHandler? SignInRequested;

        /// <summary>Raised whenever the mode changes, so a shell can react.</summary>
        public event EventHandler<AppMode>? ModeChanged;

        /// <summary>
        /// Raised when a project arrives or a session ends.
        /// </summary>
        /// <remarks>
        /// Separate from <see cref="ModeChanged"/> because the two do not move together: a
        /// project can arrive without the screen changing, and the session can end while the
        /// screen stays put. The header needs to hear about both, and folding them into one
        /// event would either miss one or fire on every tab click for nothing.
        /// </remarks>
        public event EventHandler? ProjectChanged;

        /// <summary>
        /// Raised when the open project is renamed, so the header can read the new name.
        /// </summary>
        /// <remarks>
        /// A rename arrives as nothing but a property notification on an editor: no mode
        /// moved and no project arrived. Without this the top bar would keep showing the old
        /// name beside a panel showing the new one, and the two are on screen at once.
        /// </remarks>
        public event EventHandler? ProjectNameChanged;

        /// <summary>
        /// Which mode the current session belongs to, or null when no project is open.
        /// </summary>
        /// <remarks>
        /// This is what keeps an editor tab enabled while its project is open and disables it
        /// again after going back to the list. Reading it off the editor instead would keep
        /// the tab alive forever: an editor never forgets its project, only the session ends.
        /// </remarks>
        public AppMode? SessionMode => _sessionMode;

        public AppMode CurrentMode
        {
            get => _currentMode;
            set
            {
                bool changed = _currentMode != value;
                _previousMode = _currentMode;
                _currentMode = value;
                if (changed || _host?.Content is null) Show();
                ModeChanged?.Invoke(this, value);
            }
        }

        /// <summary>
        /// Gives the navigator the place it puts screens. The window does not exist when the
        /// container is built, so this comes afterwards - and it is the only thing the
        /// navigator needs from the window.
        /// </summary>
        public void Attach(ContentControl host)
        {
            _host = host;
            Show();
        }

        /// <summary>Puts the current mode's screen on the window.</summary>
        public void Show()
        {
            if (_host is null) return;

            // The projects list is built per visit - it exists to show the projects as they
            // are at the moment it is opened - so it is created here rather than kept. The
            // editors are singletons and come back exactly as they were left.
            object? viewModel = _currentMode switch
            {
                AppMode.UniqueFolders => _uniqueFolders,
                AppMode.Combined => _combined,
                AppMode.SiteOrder => _siteOrder,
                AppMode.Help => null,
                AppMode.Support => null,
                _ => ProjectListProvider()
            };

            // The project is delivered BEFORE the screen is built. The editor therefore has
            // its books when the view is first bound, and the list is laid out once against a
            // finished collection instead of being built empty and then filled item by item
            // while the window is already on screen. That fill was measured at 3.3 s of
            // layout on a fifty-book run - the freeze the person feels after the project
            // looks open.
            DeliverPending();

            // The page reads the project as it stands the moment it is opened, and the
            // project is delivered above, so the books are already there.
            if (_currentMode == AppMode.SiteOrder && _sessionMode is { } sessionMode)
            {
                _siteOrder.Prepare(ProjectOf(sessionMode), NameOf(sessionMode));

                // The catalog is read after the page has its books: a slow or missing network
                // must not keep the order from opening, and the list is one read for the page.
                _ = _siteOrder.LoadFormatsAsync();
            }

            _host.Content = _screens.CreateScreen(_currentMode, viewModel);

            // The mode-select screen has no view model: it asks a question and raises the
            // answer. Who acts on the answer is this class's business, not the screen's, so
            // the subscription is made here and nowhere else.
            if (_host.Content is Views.StartScreenView startScreen)
                startScreen.Chosen += CreateProject;

            // The guide and the support page each offer the other, and neither knows how a
            // screen is put up. The two subscriptions are made here, where the answer is
            // already known to be a mode rather than a window.
            if (_host.Content is Views.HelpView help)
                help.SupportRequested += (_, _) => CurrentMode = AppMode.Support;

            if (_host.Content is Views.SupportView support)
                support.GuideRequested += (_, _) => CurrentMode = AppMode.Help;
        }

        /// <summary>
        /// Writes the project after a format was chosen. The choice lives in the book, so the
        /// save is the same one the editor's own button performs - not a second way to store a
        /// project, and not a reason to end the session.
        /// </summary>
        private async void SaveChoice()
        {
            // One save at a time. A save refreshes the list the window is built from, and that
            // refresh can come back as another choice; without this guard the two called each
            // other until the stack ran out.
            if (_savingChoice) return;

            if (_sessionMode is not { } mode) return;

            var editor = EditorFor(mode);
            if (editor is null) return;

            _savingChoice = true;
            try
            {
                await editor.QuickSaveAsync();
            }
            catch (Exception ex)
            {
                _siteOrder.ShowMessage("Не удалось сохранить проект: " + ex.Message);
            }
            finally
            {
                _savingChoice = false;
            }
        }

        /// <summary>Opens the upload screen for the plan the order page has in hand.</summary>
        /// <remarks>
        /// The plan is what the page just priced. Without a session the window goes on asking for
        /// nothing: the shell asks for the password and repeats this same step once the customer
        /// is in, so sending leads to the sign-in rather than to a message about it.
        /// </remarks>
        private void OpenUpload()
        {
            if (_siteOrder.PendingUpload is not { } plan)
                return;

            if (_sessions.Load() is not { } session)
            {
                AskToSignIn(OpenUpload);
                return;
            }

            _upload.Prepare(session, plan.Summary, plan.Groups, plan.Problems, plan.Notes);

            UploadWindowRequested?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// Hands the screen the session the customer has just obtained, so the run can go on.
        /// </summary>
        /// <remarks>
        /// Nothing happens without a session: a cancelled sign-in leaves the screen as it was,
        /// with the reason already on it, rather than pretending the run can continue.
        /// </remarks>
        public void ResumeAfterSignIn()
        {
            if (_sessions.Load() is not { } session)
            {
                _afterSignIn = null;
                return;
            }

            _upload.RefreshSession(session);

            var then = _afterSignIn;
            _afterSignIn = null;
            then?.Invoke();
        }

        private void OpenOrder(AppMode mode)
        {
            // The order page is the site's: formats, parameters and prices are the site's own
            // answers, so the page opens only for a customer the site knows.
            if (_sessions.Load() is null)
            {
                AskToSignIn(() => OpenOrder(mode));
                return;
            }

            _sessionMode = mode;
            CurrentMode = AppMode.SiteOrder;
        }

        /// <summary>
        /// Asks the shell for a password and remembers what the customer was doing.
        /// </summary>
        /// <remarks>
        /// One pending step, not a queue: the customer asked to go somewhere once, and a second
        /// answer about the same place would only send them twice. A closed sign-in window drops
        /// the step, so the answer to a cancelled question is nothing.
        /// </remarks>
        private void AskToSignIn(Action after)
        {
            _afterSignIn = after;
            SignInRequested?.Invoke(this, EventArgs.Empty);
        }

        private string? NameOf(AppMode mode) => mode switch
        {
            AppMode.UniqueFolders => _uniqueFolders.ProjectName,
            AppMode.Combined => _combined.ProjectName,
            _ => null
        };

        private PhotoBook.Core.Project? ProjectOf(AppMode mode) => mode switch
        {
            AppMode.UniqueFolders => _uniqueFolders.Project,
            AppMode.Combined => _combined.Project,
            _ => null
        };

        public void OpenProject(AppMode mode)
        {
            _sessionMode = mode;
            CurrentMode = mode;
        }

        /// <summary>
        /// Starts a project that does not exist yet: the editor is emptied, then the mode
        /// opens.
        /// </summary>
        /// <remarks>
        /// The reset is not optional. The editors are singletons, so without it "Создать
        /// проект" carries the previous session's books and project record into the new
        /// project - and the first save then overwrites the project that was open before.
        /// </remarks>
        public void CreateProject(AppMode mode)
        {
            if (mode is not (AppMode.UniqueFolders or AppMode.Combined)) return;

            EditorFor(mode)?.ResetProjectCommand.Execute(null);

            _sessionMode = mode;
            CurrentMode = mode;
        }

        public void EndSession()
        {
            _sessionMode = null;
            CurrentMode = AppMode.ProjectList;
            ProjectChanged?.Invoke(this, EventArgs.Empty);
        }

        public IProjectEditor? EditorFor(AppMode mode) => mode switch
        {
            AppMode.UniqueFolders => _uniqueFolders,
            AppMode.Combined => _combined,
            _ => null
        };

        /// <summary>
        /// Hands an opened project to the editor that owns its mode.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The hand-over is held rather than performed when the caller asks. The screen for a
        /// mode is created after the mode changes, and an editor asked for a project before
        /// its screen exists has nowhere to show it - which in the WPF shell needed two
        /// dispatches at Loaded priority, and is the reason that wait is written once, here,
        /// rather than at every call site.
        /// </para>
        /// <para>
        /// Delivery happens before the screen is built, which in this engine is synchronous:
        /// the editor holds the project before the content is created, so the first layout
        /// sees a finished collection. No waiting, and no project arriving at an editor that
        /// cannot see it yet.
        /// </para>
        /// </remarks>
        public void HandProjectToEditor(Project project, ProjectInfo record)
            => _pending = (project, record);

        private void DeliverPending()
        {
            if (_pending is not { } pending) return;
            _pending = null;

            _sessionMode = pending.Record.Mode;
            EditorFor(pending.Record.Mode)?.SetProject(pending.Project, pending.Record);

            ProjectChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
