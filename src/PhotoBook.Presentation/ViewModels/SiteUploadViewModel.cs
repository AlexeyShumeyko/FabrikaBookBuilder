using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PhotoBook.Application;
using PhotoBook.Application.Sites;
using PhotoBook.Application.Sites.Books;
using PhotoBook.Application.Sites.Books.Files;
using PhotoBook.Application.Sites.Books.Grouping;
using PhotoBook.Application.Sites.Books.Submission;
using PhotoBook.Application.Sites.Completion;
using PhotoBook.Application.Sites.Projects;
using PhotoBook.Application.Sites.Upload;

namespace PhotoBook.Presentation.ViewModels
{
    /// <summary>Where the upload screen is in its work.</summary>
    public enum UploadStep
    {
        /// <summary>Nothing sent yet; the button is waiting for the customer.</summary>
        Ready,

        /// <summary>Projects are being created on the site.</summary>
        Creating,

        /// <summary>Pages are going up, a couple of files at a time.</summary>
        Uploading,

        /// <summary>The site is being read back to see what actually arrived.</summary>
        Checking,

        /// <summary>Everything arrived.</summary>
        Done,

        /// <summary>Stopped by a refusal, a lost connection or the customer.</summary>
        Stopped
    }

    /// <summary>What became of one group, for the list under the button.</summary>
    /// <param name="Number">The group's place in the order.</param>
    /// <param name="ProjectId">The project the site holds, or zero.</param>
    /// <param name="Uploaded">Pages the site accepted.</param>
    /// <param name="Total">Pages the group has.</param>
    /// <param name="Problems">What does not match, or why nothing was sent.</param>
    public sealed record UploadGroupResult(
        int Number,
        long ProjectId,
        int Uploaded,
        int Total,
        IReadOnlyList<string> Problems)
    {
        /// <summary>Whether the site holds every page and nothing is wrong.</summary>
        public bool IsComplete => ProjectId > 0 && Uploaded >= Total && Problems.Count == 0;

        /// <summary>The project on the site, or the note that there is none.</summary>
        public string ProjectText => ProjectId > 0
            ? "Проект " + ProjectId + " на сайте"
            : "Проект не создан";

        /// <summary>What arrived, in the site's own counting of pages.</summary>
        public string PagesText => "Страниц на сайте: " + Uploaded + " из " + Total;

        /// <summary>What does not match, one line each; empty when everything arrived.</summary>
        public string ProblemsText => string.Join(System.Environment.NewLine, Problems);

        /// <summary>Whether there is anything wrong with this group.</summary>
        public bool HasProblems => Problems.Count > 0;
    }

    /// <summary>
    /// The upload screen: what is about to be sent, how far it got, and what the site holds.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The screen is the only place that starts a write. Everything before it - the books, the
    /// checks, the groups, the price - is answers already in hand, so opening the screen costs
    /// the site nothing. Pressing the button runs the four steps in order and reports each one:
    /// create, send, read back, and what is left for the customer to do by hand.
    /// </para>
    /// <para>
    /// A refusal stops the run rather than skipping the group. An expired session raises
    /// <see cref="NeedsSignIn"/> because the password belongs to the sign-in window, not here,
    /// and a lost connection leaves the button able to continue: what the site already has is
    /// written down, so the next press starts at the page that failed.
    /// </para>
    /// <para>
    /// A group is one project on the site, and it holds every book of its format: the books go
    /// up one after another, each against the number the site gave it, so a project of three
    /// books ends up with three covers and three sets of spreads rather than one long book.
    /// </para>
    /// </remarks>
    public sealed partial class SiteUploadViewModel : ViewModelBase
    {
        private readonly IProjectCreationService _creation;
        private readonly IBookUploadService _upload;
        private readonly IProjectCheckService _check;
        private readonly ICreatedProjectStore? _created;
        private readonly IUploadedProjectStore? _uploadedProjects;
        private readonly OrderFileCheckService? _fileCheck;
        private CancellationTokenSource? _cancelFiles;

        private SiteSession? _session;
        private SubmissionSummary? _summary;
        private IReadOnlyList<UploadGroup> _groups = Array.Empty<UploadGroup>();
        private CancellationTokenSource? _cancel;

        private UploadStep _step = UploadStep.Ready;
        private string _message = string.Empty;
        private string _progressText = string.Empty;
        private double _progressValue;
        private string _url = string.Empty;
        private string _resultText = string.Empty;
        private IReadOnlyList<UploadGroupResult> _results = Array.Empty<UploadGroupResult>();
        private IReadOnlyList<string> _problems = Array.Empty<string>();
        private int _uploaded;
        private int _total;

        /// <summary>Pages the site took in the groups before this one, for the bar.</summary>
        private int _completedBeforeGroup;

        public SiteUploadViewModel(
            IProjectCreationService creation,
            IBookUploadService upload,
            IProjectCheckService check,
            IUiThread? ui = null,
            ICreatedProjectStore? created = null,
            IUploadedProjectStore? uploaded = null)
        {
            _creation = creation ?? throw new ArgumentNullException(nameof(creation));
            _upload = upload ?? throw new ArgumentNullException(nameof(upload));
            _check = check ?? throw new ArgumentNullException(nameof(check));
            _created = created;
            _uploadedProjects = uploaded;

            if (ui is not null)
                Ui = ui;
        }

        /// <summary>Raised when the site refused the token; the shell opens the sign-in window.</summary>
        public event EventHandler? NeedsSignIn;

        /// <summary>Raised when the customer leaves the screen for the order behind it.</summary>
        public event EventHandler? BackRequested;

        /// <summary>Raised when the customer leaves a finished run for the projects list.</summary>
        public event EventHandler? ProjectsRequested;

        /// <summary>The summary the button was enabled by; null until the screen is prepared.</summary>
        public SubmissionSummary? Summary => _summary;

        /// <summary>Where the run is, for the caption and for enabling the button.</summary>
        public UploadStep Step
        {
            get => _step;
            private set
            {
                if (!SetProperty(ref _step, value)) return;

                OnPropertyChanged(nameof(IsBusy));
                OnPropertyChanged(nameof(CanUpload));
                OnPropertyChanged(nameof(CanCancel));
                OnPropertyChanged(nameof(ButtonText));
                OnPropertyChanged(nameof(IsDone));
                OnPropertyChanged(nameof(IsConfirming));
                OnPropertyChanged(nameof(IsRunning));
                OnPropertyChanged(nameof(IsFinished));
                OnPropertyChanged(nameof(HeaderText));
            }
        }

        /// <summary>Everything the customer needs to read before or after pressing.</summary>
        public string Message
        {
            get => _message;
            private set
            {
                if (SetProperty(ref _message, value ?? string.Empty))
                    OnPropertyChanged(nameof(HasMessage));
            }
        }

        /// <summary>Whether there is anything to show under the list.</summary>
        public bool HasMessage => _message.Length > 0;

        /// <summary>What the site is doing now, in one line.</summary>
        public string ProgressText
        {
            get => _progressText;
            private set => SetProperty(ref _progressText, value ?? string.Empty);
        }

        /// <summary>Files sent so far against the files the run has.</summary>
        public int Uploaded
        {
            get => _uploaded;
            private set
            {
                if (!SetProperty(ref _uploaded, value))
                    return;

                OnPropertyChanged(nameof(UploadedText));
            }
        }

        /// <summary>Files the run has in total, the covers counted.</summary>
        public int Total
        {
            get => _total;
            private set
            {
                if (!SetProperty(ref _total, value))
                    return;

                OnPropertyChanged(nameof(UploadedText));
            }
        }

        /// <summary>How far the bar is, from zero to a hundred.</summary>
        /// <remarks>
        /// The bar's own scale, not a quotient: the view used to bind this to a bar scaled to a
        /// hundred while the page wrote a fraction, so a finished run moved it by one percent.
        /// </remarks>
        public double ProgressValue
        {
            get => _progressValue;
            private set
            {
                if (!SetProperty(ref _progressValue, value))
                    return;

                OnPropertyChanged(nameof(PercentText));
            }
        }

        /// <summary>How far the run is, in percent, as the window prints it.</summary>
        public string PercentText =>
            Math.Round(ProgressValue).ToString("0", System.Globalization.CultureInfo.CurrentCulture) + "%";

        /// <summary>How many files are sent, in the words the window shows under the bar.</summary>
        public string UploadedText => "Отправлено " + Uploaded + " из " + Total + " файлов";

        /// <summary>The project's address on the site, or an empty string when unknown.</summary>
        public string Url
        {
            get => _url;
            private set
            {
                if (!SetProperty(ref _url, value ?? string.Empty))
                    return;

                OnPropertyChanged(nameof(HasUrl));
                OnPropertyChanged(nameof(CanOpenSite));
                OnPropertyChanged(nameof(OnSiteLink));
            }
        }

        /// <summary>Whether there is a link to offer.</summary>
        public bool HasUrl => _url.Length > 0;

        /// <summary>Whether the screen may offer to open the project on the site.</summary>
        public bool CanOpenSite => HasUrl;

        /// <summary>
        /// The address the button opens: the project's own page on the site the session belongs
        /// to, as the site's read-back named it. Nothing is built here, so a wrong site cannot
        /// come from this screen.
        /// </summary>
        public string OnSiteLink => _url;

        /// <summary>
        /// Says that the address did not open, without making the upload look failed.
        /// </summary>
        /// <remarks>
        /// The project is on the site either way, so the customer is given the address to open
        /// by hand rather than a message that the run did not work.
        /// </remarks>
        public void ReportLinkFailure() =>
            Message = "Проект на сайте, но ссылка не открылась. Откройте вручную: " + _url;

        /// <summary>What the run ended with, for the customer to keep or act on.</summary>
        public string ResultText
        {
            get => _resultText;
            private set
            {
                if (SetProperty(ref _resultText, value ?? string.Empty))
                    OnPropertyChanged(nameof(HasResult));
            }
        }

        /// <summary>Whether the run ended and there is something to read.</summary>
        public bool HasResult => _resultText.Length > 0;

        /// <summary>Whether there is a line per project to show.</summary>
        public bool HasResults => _results.Count > 0;

        /// <summary>One line per group: the project, its pages, and what is wrong.</summary>
        public IReadOnlyList<UploadGroupResult> Results
        {
            get => _results;
            private set
            {
                if (SetProperty(ref _results, value))
                    OnPropertyChanged(nameof(HasResults));
            }
        }

        /// <summary>Whether the run finished and the result is on the screen.</summary>
        public bool IsDone => Step == UploadStep.Done;

        /// <summary>Whether the window is still asking whether to send.</summary>
        public bool IsConfirming => Step == UploadStep.Ready;

        /// <summary>Whether the site is being written to right now.</summary>
        public bool IsRunning => Step is UploadStep.Creating or UploadStep.Uploading or UploadStep.Checking;

        /// <summary>Whether the run has ended, one way or another.</summary>
        public bool IsFinished => Step is UploadStep.Done or UploadStep.Stopped;

        /// <summary>Projects the order will create, as the confirmation says it.</summary>
        public int ProjectCount => Summary?.ProjectCount ?? 0;

        /// <summary>Books going in those projects.</summary>
        public int BookCount => Summary?.BookCount ?? 0;

        /// <summary>The order's total as the site wrote it.</summary>
        public string ConfirmTotalText => Summary?.TotalText ?? string.Empty;

        /// <summary>What the window calls the moment it is in.</summary>
        public string HeaderText => Step switch
        {
            UploadStep.Ready => "Отправить заказ на сайт?",
            UploadStep.Done => "Заказ на сайте",
            UploadStep.Stopped => "Загрузка остановлена",
            _ => "Отправляем на сайт"
        };

        /// <summary>Leaves a finished run for the projects list.</summary>
        public void GoToProjects() => ProjectsRequested?.Invoke(this, EventArgs.Empty);

        /// <summary>Whether the site is being written to right now.</summary>
        public bool IsBusy => Step is UploadStep.Creating or UploadStep.Uploading or UploadStep.Checking;

        /// <summary>What the button says, which is also what pressing it will do.</summary>
        public string ButtonText => Step switch
        {
            UploadStep.Creating => "Создаём проект…",
            UploadStep.Uploading => "Загружаем…",
            UploadStep.Checking => "Проверяем…",
            UploadStep.Done => "Готово",
            _ when _uploaded > 0 => "Продолжить загрузку",
            _ => "Загрузить на сайт"
        };

        /// <summary>
        /// Whether the button may be pressed: the order may go, and the files have been read and
        /// found sendable, with every question answered by the customer.
        /// </summary>
        public bool CanUpload => !IsBusy && _session is not null &&
            _summary is { CanUpload: true } && _problems.Count == 0 &&
            _groups.Count > 0 && _groups.All(group => group.Pages.Count > 0);

        /// <summary>Whether the customer may stop the run.</summary>
        public bool CanCancel => IsBusy;

        /// <summary>
        /// Shows what is about to be sent. Costs the site nothing: the summary and the groups are
        /// answers the previous screens already have.
        /// </summary>
        /// <param name="session">The session the run will write with.</param>
        /// <param name="summary">The order as the customer reviews it.</param>
        /// <param name="groups">The groups to send, one project each.</param>
        /// <param name="problems">
        /// Books the order page left out, and why. Any entry keeps the button off: a run that
        /// silently skipped a book would leave a project the customer never asked for.
        /// </param>
        /// <param name="notes">What the customer should know but need not fix.</param>
        public void Prepare(
            SiteSession session,
            SubmissionSummary summary,
            IReadOnlyList<UploadGroup> groups,
            IReadOnlyList<string>? problems = null,
            IReadOnlyList<string>? notes = null)
        {
            _session = session ?? throw new ArgumentNullException(nameof(session));
            _summary = summary ?? throw new ArgumentNullException(nameof(summary));
            _groups = groups ?? throw new ArgumentNullException(nameof(groups));
            _problems = problems ?? Array.Empty<string>();

            Step = UploadStep.Ready;
            Uploaded = 0;
            _completedBeforeGroup = 0;
            Total = _groups.Sum(group => group.Pages.Count);
            ProgressValue = 0;
            Results = Array.Empty<UploadGroupResult>();
            Url = string.Empty;
            ResultText = string.Empty;
            ProgressText = Total == 0 ? string.Empty : "Файлов к отправке: " + Total + ".";

            Message = Blockers(_problems, notes ?? Array.Empty<string>());

            OnPropertyChanged(nameof(Summary));
            OnPropertyChanged(nameof(CanUpload));
            OnPropertyChanged(nameof(ButtonText));
            OnPropertyChanged(nameof(ProjectCount));
            OnPropertyChanged(nameof(BookCount));
            OnPropertyChanged(nameof(ConfirmTotalText));
        }

        /// <summary>
        /// Takes the session the shell has just obtained, without rebuilding the screen.
        /// </summary>
        /// <remarks>
        /// An expired token stops a run halfway, and the customer signs in again in the shell's
        /// own window. What the site already holds was written down, so the same screen carries
        /// on from the page that failed - losing the plan and the price to a password prompt
        /// would be a second refusal for no reason.
        /// </remarks>
        public void RefreshSession(SiteSession session)
        {
            _session = session ?? throw new ArgumentNullException(nameof(session));

            OnPropertyChanged(nameof(CanUpload));
        }

        /// <summary>What the screen says instead of a run: everything that blocks the button.</summary>
        private string Blockers(IReadOnlyList<string> problems, IReadOnlyList<string> notes)
        {
            var lines = new List<string>(problems);

            if (_summary is not null)
                lines.AddRange(_summary.Blockers);

            // Notes explain the plan and do not keep the button off, so they come last.
            foreach (var note in notes)
                lines.Add(note);

            foreach (var group in _groups.Where(group => group.Pages.Count == 0))
                lines.Add("Группа " + group.Number + ": в книге нет страниц.");

            return string.Join(Environment.NewLine, lines);
        }
    }
}
