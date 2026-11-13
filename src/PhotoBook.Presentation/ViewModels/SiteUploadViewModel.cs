using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PhotoBook.Application;
using PhotoBook.Application.Sites;
using PhotoBook.Application.Sites.Books;
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

        /// <summary>Pages are going up, one file at a time.</summary>
        Uploading,

        /// <summary>The site is being read back to see what actually arrived.</summary>
        Checking,

        /// <summary>Everything arrived.</summary>
        Done,

        /// <summary>Stopped by a refusal, a lost connection or the customer.</summary>
        Stopped
    }

    /// <summary>One group the screen will send, with everything the site needs for it.</summary>
    /// <param name="Number">The group's place in the order, starting at one.</param>
    /// <param name="Group">The books laid out into this project.</param>
    /// <param name="Request">The product, its format and the uploader's integration.</param>
    /// <param name="Pages">The spreads of the group's single book, in page order.</param>
    public sealed record UploadGroup(
        int Number,
        OrderGroup Group,
        CreateProjectRequest Request,
        IReadOnlyList<PageFile> Pages);

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
    /// The screen sends one book per project. A group with several books is reported as
    /// unsupported rather than sent as one long book: the site's project holds one item today,
    /// and pretending otherwise would put the second book's pages into the first.
    /// </para>
    /// </remarks>
    public sealed partial class SiteUploadViewModel : ViewModelBase
    {
        private readonly IProjectCreationService _creation;
        private readonly IBookUploadService _upload;
        private readonly IProjectCheckService _check;

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
        private int _uploaded;
        private int _total;

        /// <summary>Pages the site took in the groups before this one, for the bar.</summary>
        private int _completedBeforeGroup;

        public SiteUploadViewModel(
            IProjectCreationService creation,
            IBookUploadService upload,
            IProjectCheckService check,
            IUiThread? ui = null)
        {
            _creation = creation ?? throw new ArgumentNullException(nameof(creation));
            _upload = upload ?? throw new ArgumentNullException(nameof(upload));
            _check = check ?? throw new ArgumentNullException(nameof(check));

            if (ui is not null)
                Ui = ui;
        }

        /// <summary>Raised when the site refused the token; the shell opens the sign-in window.</summary>
        public event EventHandler? NeedsSignIn;

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

        /// <summary>Pages sent so far against the pages the run has.</summary>
        public int Uploaded
        {
            get => _uploaded;
            private set => SetProperty(ref _uploaded, value);
        }

        /// <summary>Pages the run has in total.</summary>
        public int Total
        {
            get => _total;
            private set => SetProperty(ref _total, value);
        }

        /// <summary>How far the bar is, from zero to one.</summary>
        public double ProgressValue
        {
            get => _progressValue;
            private set => SetProperty(ref _progressValue, value);
        }

        /// <summary>The project's address on the site, or an empty string when unknown.</summary>
        public string Url
        {
            get => _url;
            private set
            {
                if (SetProperty(ref _url, value ?? string.Empty))
                    OnPropertyChanged(nameof(HasUrl));
            }
        }

        /// <summary>Whether there is a link to offer.</summary>
        public bool HasUrl => _url.Length > 0;

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

        /// <summary>One line per group: the project, its pages, and what is wrong.</summary>
        public IReadOnlyList<UploadGroupResult> Results
        {
            get => _results;
            private set => SetProperty(ref _results, value);
        }

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

        /// <summary>Whether the button may be pressed.</summary>
        public bool CanUpload => !IsBusy && _session is not null && _summary is { CanUpload: true } &&
            _groups.Count > 0 && _groups.All(group => group.Pages.Count > 0 && group.Group.Books.Count == 1);

        /// <summary>Whether the customer may stop the run.</summary>
        public bool CanCancel => IsBusy;

        /// <summary>
        /// Shows what is about to be sent. Costs the site nothing: the summary and the groups are
        /// answers the previous screens already have.
        /// </summary>
        public void Prepare(SiteSession session, SubmissionSummary summary, IReadOnlyList<UploadGroup> groups)
        {
            _session = session ?? throw new ArgumentNullException(nameof(session));
            _summary = summary ?? throw new ArgumentNullException(nameof(summary));
            _groups = groups ?? throw new ArgumentNullException(nameof(groups));

            Step = UploadStep.Ready;
            Uploaded = 0;
            _completedBeforeGroup = 0;
            Total = _groups.Sum(group => group.Pages.Count);
            ProgressValue = 0;
            Results = Array.Empty<UploadGroupResult>();
            Url = string.Empty;
            ResultText = string.Empty;
            ProgressText = Total == 0 ? string.Empty : "Страниц к отправке: " + Total + ".";

            Message = Blockers();

            OnPropertyChanged(nameof(Summary));
            OnPropertyChanged(nameof(CanUpload));
            OnPropertyChanged(nameof(ButtonText));
        }

        /// <summary>What the screen says instead of a run: everything that blocks the button.</summary>
        private string Blockers()
        {
            var lines = new List<string>();

            if (_summary is not null)
                lines.AddRange(_summary.Blockers);

            foreach (var group in _groups.Where(group => group.Group.Books.Count > 1))
            {
                lines.Add(
                    "Группа " + group.Number + ": в проекте несколько книг. Сейчас на сайт уходит одна книга на " +
                    "проект — разложите книги по одной или подождите, пока появится поддержка нескольких книг.");
            }

            foreach (var group in _groups.Where(group => group.Pages.Count == 0))
                lines.Add("Группа " + group.Number + ": в книге нет страниц.");

            return string.Join(Environment.NewLine, lines);
        }
    }
}
