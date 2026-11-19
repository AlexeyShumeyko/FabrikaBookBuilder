using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;

namespace PhotoBook.Core
{
    public class Book : ObservableObject
    {
        private long? _siteProductId;
        private long? _siteVariantId;
        private string? _siteProductTitle;
        private string? _siteVariantTitle;
        private Dictionary<long, long> _siteOptions = new();
        private string? _folderPath;
        private string? _name;
        private Page? _cover;
        private int _bookIndex;
        private ObservableCollection<int> _pageSlots = new();
        private ObservableCollection<int> _allSlots = new();
        private ObservableCollection<Page> _allSlotsPages = new();

        public Book()
        {
            Pages = new ObservableCollection<Page>();
            Pages.CollectionChanged += Pages_CollectionChanged;
            _isValid = false;
            _allSlots.Add(0); // slot 0 is the cover, and a book always has one
            UpdatePageSlots();

            if (_cover != null)
            {
                _cover.PropertyChanged += Page_PropertyChanged;
            }
        }

        private void Page_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(Page.SourcePath) || e.PropertyName == nameof(Page.ThumbnailPath) || e.PropertyName == nameof(Page.IsEmpty))
            {
                OnPropertyChanged(nameof(Cover));
                OnPropertyChanged(nameof(Pages));
                OnPropertyChanged(nameof(AllSlotsPages));
                UpdateIsValid();
            }
        }

        private void Pages_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.OldItems != null)
            {
                foreach (Page page in e.OldItems)
                {
                    page.PropertyChanged -= Page_PropertyChanged;
                }
            }

            if (e.NewItems != null)
            {
                foreach (Page page in e.NewItems)
                {
                    page.PropertyChanged += Page_PropertyChanged;
                }
            }

            UpdatePageSlots();
            OnPropertyChanged(nameof(Pages));
            UpdateIsValid();
        }

        public void UpdatePageSlots()
        {
            var pagesWithoutCover = Pages.Where(p => !p.IsCover).ToList();
            // A "page" in this model is a two-page spread, and that is what a slot holds.
            var newSlots = Enumerable.Range(1, pagesWithoutCover.Count).ToList();

            _pageSlots.Clear();
            foreach (var slot in newSlots)
            {
                _pageSlots.Add(slot);
            }

            // The display list is the cover followed by the spreads.
            _allSlots.Clear();
            _allSlots.Add(0);
            foreach (var slot in newSlots)
            {
                _allSlots.Add(slot);
            }

            _allSlotsPages.Clear();
            if (Cover != null)
            {
                _allSlotsPages.Add(Cover);
            }
            foreach (var page in pagesWithoutCover.OrderBy(p => p.Index))
            {
                _allSlotsPages.Add(page);
            }

            // Publish the export name for every slot so the UI can show the numbering the
            // batch uploader will see. Kept in sync here so no view has to reach back up to
            // the owning Book to work out which book a Page belongs to.
            RefreshExportFileNames();

            // The frame is shaped last: it needs the slots above to be in place so the
            // cover and the spreads are all known.
            UpdateFrameAspect();

            OnPropertyChanged(nameof(PageSlots));
            OnPropertyChanged(nameof(AllSlots));
            OnPropertyChanged(nameof(AllSlotsPages));
        }

        /// <summary>
        /// Re-reads what depends on WHICH photographs the book holds, without rebuilding the
        /// slots themselves.
        /// </summary>
        /// <remarks>
        /// The slot lists are the book's structure - a cover and N spreads - and putting a
        /// photograph into one of them does not change the structure. <see cref="UpdatePageSlots"/>
        /// was called for that anyway, and emptying a collection the screen is bound to and
        /// refilling it in the same breath is what left a dozen stale frames in one book after
        /// a drop while the file on disk was right: the counters and the captions come from the
        /// pages themselves, and a page announces its own change.
        /// </remarks>
        public void RefreshSlots()
        {
            RefreshExportFileNames();
            UpdateFrameAspect();
        }

        // ------------------------------------------------------------------
        //  Card frame ratio - the whole policy lives in these four constants
        // ------------------------------------------------------------------

        /// <summary>
        /// Width / height used when nothing is known about the photos, and the value the
        /// mockup uses (aspect-[16/10]).
        /// </summary>
        private const double DefaultFrameAspect = 1.6;

        /// <summary>
        /// Portrait limit. The most extreme real format is 0.67 (20x30, 10x15 and the
        /// 2457x3602 post-prints), so 0.60 leaves those uncropped.
        /// </summary>
        private const double FrameAspectMin = 0.60;

        /// <summary>
        /// Landscape limit. 20x20 / 25x25 / 30x30 spreads measure 1.9 and are the widest
        /// format that appears more than once, so they stay uncropped too. A 2.1 folder
        /// loses 5% and a 3.0 panorama keeps a 37% cut - a panorama shown as a letterbox
        /// strip was judged worse than showing its middle.
        /// </summary>
        private const double FrameAspectMax = 1.90;

        /// <summary>
        /// The frame shape for this book, pushed onto every page so the card template can
        /// bind it. Median, not mean: in every mixed folder the odd photo out is the
        /// single cover, and the median keeps the frame on the format the 24-48 spreads
        /// actually share.
        ///
        /// One frame per book, not per project. A project-wide frame cannot satisfy "no
        /// empty space and at most 5-10% cropped" - 0.67 and 1.9 in one frame cuts ~30%
        /// off one of them. Within a book every card is still identical, so the grid does
        /// not go ragged. To try a project-wide frame instead, compare against
        /// <see cref="AllBooks"/>-level data in the callers of this method.
        /// </summary>
        private void UpdateFrameAspect()
        {
            var ratios = new List<double>();
            if (Cover != null && Cover.HasDimensions) ratios.Add(Cover.AspectRatio);
            foreach (var p in Pages)
                if (p != null && !p.IsCover && p.HasDimensions)
                    ratios.Add(p.AspectRatio);

            ApplyFrameAspect(ratios.Count > 0 ? Median(ratios) : DefaultFrameAspect);
        }

        /// <summary>
        /// Forces one frame shape onto every slot of this book.
        ///
        /// The combined run uses it: a print run is all one format, so the run decides a
        /// single shape and stamps it on each book. Without this a photo dropped into
        /// book 1 reshaped only book 1 and the rest of the run kept the default. The
        /// unique mode never calls it - there every book is its own folder and keeps its
        /// own median.
        /// </summary>
        public void ApplyFrameAspect(double aspect)
        {
            double clamped = Math.Clamp(aspect, FrameAspectMin, FrameAspectMax);

            if (Cover != null) Cover.FrameAspect = clamped;
            foreach (var p in Pages)
                if (p != null) p.FrameAspect = clamped;

            // Only shout when it actually changed. This runs on every assignment in the
            // run, and an unconditional notification here is a loop waiting to happen:
            // anything listening for FrameAspect ends up calling straight back into the
            // code that set it.
            if (Math.Abs(FrameAspect - clamped) < 0.0001) return;

            FrameAspect = clamped;
            OnPropertyChanged(nameof(FrameAspect));
        }

        private static double Median(List<double> values)
        {
            values.Sort();
            int mid = values.Count / 2;
            return values.Count % 2 == 1 ? values[mid] : (values[mid - 1] + values[mid]) / 2d;
        }

        /// <summary>
        /// Writes the export file name (KKK-FF.jpg) onto every slot of this book.
        /// Must mirror <c>ExportService.GenerateFileName</c>; that method is the single
        /// source of truth for what actually lands on disk.
        /// </summary>
        private void RefreshExportFileNames()
        {
            if (Cover != null)
                Cover.ExportFileName = $"{BookIndex:D3}-{0:D2}.jpg";

            foreach (var page in Pages.Where(p => !p.IsCover))
                page.ExportFileName = $"{BookIndex:D3}-{page.Index:D2}.jpg";
        }

        public string? FolderPath
        {
            get => _folderPath;
            set => SetProperty(ref _folderPath, value);
        }

        public string? Name
        {
            get => _name;
            set => SetProperty(ref _name, value);
        }

        public Page? Cover
        {
            get => _cover;
            set
            {
                if (_cover != null)
                {
                    _cover.PropertyChanged -= Page_PropertyChanged;
                }

                if (SetProperty(ref _cover, value))
                {
                    if (_cover != null)
                    {
                        _cover.PropertyChanged += Page_PropertyChanged;
                    }
                    UpdatePageSlots();
                    UpdateIsValid();
                }
            }
        }

        public int BookIndex
        {
            get => _bookIndex;
            set
            {
                if (SetProperty(ref _bookIndex, value))
                {
                    // The export file name is prefixed with the book index, so renumbering
                    // a book has to refresh every slot's name.
                    RefreshExportFileNames();
                }
            }
        }

        public ObservableCollection<Page> Pages { get; }

        /// <summary>
        /// The site format this book is ordered in, once the customer has chosen one.
        /// </summary>
        /// <remarks>
        /// Stored on the book rather than on the run: one run can hold books of different
        /// formats, and the site prices each of them by its own product and variant. A book
        /// without a format is a book nobody has picked a format for yet - it cannot be priced,
        /// and it cannot be sent.
        /// </remarks>
        public long? SiteProductId
        {
            get => _siteProductId;
            set => SetProperty(ref _siteProductId, value);
        }

        /// <summary>The variant of <see cref="SiteProductId"/>: the size and the cover.</summary>
        public long? SiteVariantId
        {
            get => _siteVariantId;
            set => SetProperty(ref _siteVariantId, value);
        }

        /// <summary>The product's title, kept so a card can say the format without the catalog.</summary>
        public string? SiteProductTitle
        {
            get => _siteProductTitle;
            set => SetProperty(ref _siteProductTitle, value);
        }

        /// <summary>The variant's title as the site writes it, e.g. «21×30, мягкая обложка».</summary>
        public string? SiteVariantTitle
        {
            get => _siteVariantTitle;
            set => SetProperty(ref _siteVariantTitle, value);
        }

        /// <summary>Whether a site format has been chosen for this book.</summary>
        public bool HasSiteFormat => SiteProductId is > 0 && SiteVariantId is > 0;

        /// <summary>
        /// What the customer chose for the format's parameters: option id to element id.
        /// </summary>
        /// <remarks>
        /// The site prices and produces by these choices, so they belong to the book and are
        /// saved with the project - a format whose parameters are not chosen has no price at
        /// all, and asking again after a restart would make the customer repeat work they had
        /// already done. The ids are the site's own; nothing here is interpreted.
        /// </remarks>
        public IReadOnlyDictionary<long, long> SiteOptions => _siteOptions;

        /// <summary>Records one parameter of the format, or replaces the choice already made.</summary>
        public void SetSiteOption(long optionId, long elementId)
        {
            if (optionId <= 0 || elementId <= 0)
                throw new ArgumentOutOfRangeException(nameof(optionId), "Параметр и его значение — идентификаторы сайта.");

            if (_siteOptions.TryGetValue(optionId, out var current) && current == elementId)
                return;

            _siteOptions[optionId] = elementId;
            OnPropertyChanged(nameof(SiteOptions));
        }

        /// <summary>Drops one parameter's answer, for when the site stops offering it.</summary>
        public void RemoveSiteOption(long optionId)
        {
            if (_siteOptions.Remove(optionId))
                OnPropertyChanged(nameof(SiteOptions));
        }

        /// <summary>
        /// Forgets the parameter choices, which is what happens when the format changes: the
        /// choices belong to the format that was chosen before.
        /// </summary>
        public void ClearSiteOptions()
        {
            if (_siteOptions.Count == 0)
                return;

            _siteOptions.Clear();
            OnPropertyChanged(nameof(SiteOptions));
        }

        /// <summary>
        /// Width / height of this book's card frames. Same value on every page of the
        /// book - see <see cref="UpdateFrameAspect"/> for why it is per book and how it is
        /// clamped.
        /// </summary>
        public double FrameAspect { get; private set; } = 1.6;

        public ObservableCollection<int> PageSlots => _pageSlots;

        // What the view binds to: the cover at 0, then the spreads at 1, 2, 3...
        public ObservableCollection<int> AllSlots => _allSlots;
        public ObservableCollection<Page> AllSlotsPages => _allSlotsPages;

        private bool _isValid;

        public bool IsValid
        {
            get
            {
                return Cover != null && !Cover.IsEmpty && Pages.All(p => !p.IsEmpty);
            }
        }

        private void UpdateIsValid()
        {
            var newValue = Cover != null && !Cover.IsEmpty && Pages.All(p => !p.IsEmpty);
            if (_isValid != newValue)
            {
                _isValid = newValue;
                OnPropertyChanged(nameof(IsValid));
            }

            // The counter names are raised unconditionally: filling the second of five
            // spreads changes them while IsValid stays false, and a change-gated raise
            // would leave the label stuck on "0 из 5".
            OnPropertyChanged(nameof(FilledSlotCount));
            OnPropertyChanged(nameof(TotalSlotCount));
            OnPropertyChanged(nameof(ProgressText));
            OnPropertyChanged(nameof(IsFilled));
        }

        /// <summary>
        /// Slots holding a photo, cover included. Feeds the "Готово 2 из 4" label - the
        /// same wording the unique mode uses, so both screens read identically.
        /// </summary>
        public int FilledSlotCount
        {
            get
            {
                int n = Cover != null && !Cover.IsEmpty ? 1 : 0;
                foreach (var p in Pages)
                    if (p != null && !p.IsCover && !p.IsEmpty) n++;
                return n;
            }
        }

        /// <summary>Slots in this book, cover included.</summary>
        public int TotalSlotCount
        {
            get
            {
                int n = Cover != null ? 1 : 0;
                foreach (var p in Pages)
                    if (p != null && !p.IsCover) n++;
                return n;
            }
        }

        public string ProgressText => $"Готово {FilledSlotCount} из {TotalSlotCount}";

        /// <summary>
        /// Every slot has a photo. An empty book is never "filled", so a structure with
        /// no books cannot report itself as complete.
        /// </summary>
        public bool IsFilled => TotalSlotCount > 0 && FilledSlotCount == TotalSlotCount;

        public Book Clone()
        {
            var clone = new Book
            {
                FolderPath = FolderPath,
                Name = Name,
                BookIndex = BookIndex,
                Cover = Cover?.Clone(),
                SiteProductId = SiteProductId,
                SiteVariantId = SiteVariantId,
                SiteProductTitle = SiteProductTitle,
                SiteVariantTitle = SiteVariantTitle
            };

            foreach (var option in SiteOptions)
                clone.SetSiteOption(option.Key, option.Value);

            foreach (var page in Pages)
            {
                clone.Pages.Add(page.Clone());
            }

            return clone;
        }
    }
}





