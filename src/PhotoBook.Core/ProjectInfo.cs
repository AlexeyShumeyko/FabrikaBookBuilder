using System;
using System.Collections.ObjectModel;
using System.Text.Json.Serialization;

namespace PhotoBook.Core
{
    /// <summary>How far the project has been taken.</summary>
    public enum ProjectStatus
    {
        /// <summary>A book still has an empty slot.</summary>
        NotFilled,

        /// <summary>Every slot filled, nothing exported yet.</summary>
        Ready,

        /// <summary>Every slot filled, and the output folder is still there.</summary>
        SuccessfullyCompleted
    }

    public class ProjectInfo : ObservableObject
    {
        private string _name = string.Empty;
        private string _filePath = string.Empty;
        private int _bookCount;
        private int _pageCount;
        private ProjectStatus _status;
        private DateTime _lastModified;
        private DateTime _createdDate;
        private AppMode _mode;
        private string _id = Guid.NewGuid().ToString();
        private int _totalPhotoCount;

        [JsonPropertyName("id")]
        public string Id
        {
            get => _id;
            set
            {
                // A record can carry an empty or null id, and the file name IS the id, so
                // one of those would point the whole project at nothing.
                if (string.IsNullOrEmpty(value))
                {
                    value = Guid.NewGuid().ToString();
                }
                _id = value;
                OnPropertyChanged();
            }
        }

        public string Name
        {
            get => _name;
            set => SetProperty(ref _name, value);
        }

        public string FilePath
        {
            get => _filePath;
            set => SetProperty(ref _filePath, value);
        }

        public int BookCount
        {
            get => _bookCount;
            set => SetProperty(ref _bookCount, value);
        }

        public int PageCount
        {
            get => _pageCount;
            set => SetProperty(ref _pageCount, value);
        }

        public ProjectStatus Status
        {
            get => _status;
            set => SetProperty(ref _status, value);
        }

        public DateTime LastModified
        {
            get => _lastModified;
            set => SetProperty(ref _lastModified, value);
        }

        public AppMode Mode
        {
            get => _mode;
            set => SetProperty(ref _mode, value);
        }

        public DateTime CreatedDate
        {
            get => _createdDate;
            set => SetProperty(ref _createdDate, value);
        }

        // ---------------------------------------------------------------
        //  Project card preview strip (presentation only)
        // ---------------------------------------------------------------
        //  The mockup puts three 48x48 photos plus a "+N" tile on every project
        //  card, so the card carries a slice of the project's actual photos.
        //
        //  [JsonIgnore] on all of it: ProjectInfo is the type persisted in
        //  projects.json, and a handful of absolute photo paths per project would
        //  bloat the index and go stale the moment a file is moved. They are
        //  re-read from the project file each time the list loads.

        /// <summary>Up to three photo paths shown as the card's preview strip.</summary>
        /// <summary>
        /// The photographs a card shows, and the total behind the "+N" tile.
        /// </summary>
        /// <remarks>
        /// <b>Persisted, on purpose.</b> Both used to be marked "do not save", which meant the
        /// list had to open every project file to draw every card: thirty-nine cards meant
        /// reading and parsing 1.2 MB on the way to the screen, every time the tab was pressed.
        /// The cost of the list grew with the total size of everything on the machine instead of
        /// with the number of cards. They are written whenever a project is saved - see
        /// <c>ProjectCardData</c> and <c>ProjectRepository.SaveAsync</c>.
        /// </remarks>
        [JsonPropertyName("previewPhotos")]
        private ObservableCollection<string> _previewPhotos = new();

        /// <summary>
        /// The photographs a card shows, and the total behind the "+N" tile.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>Persisted, on purpose.</b> Both used to be marked "do not save", which meant the
        /// list had to open every project file to draw every card: thirty-nine cards meant
        /// reading and parsing 1.2 MB on the way to the screen, every time the tab was pressed.
        /// The cost of the list grew with the total size of everything on the machine instead of
        /// with the number of cards.
        /// </para>
        /// <para>
        /// <b>It has a setter, and that is not optional.</b> A collection property with only a
        /// getter is written by the serialiser and <b>never read back</b>: System.Text.Json does
        /// not fill a property it cannot assign. The index grew by three kilobytes and a half of
        /// picture paths, and every time it was read back the paths were gone - so every card
        /// was blank on load, every visit refilled them, and nothing ever became fast. A
        /// property that looks persisted and is not read back is worse than one that was never
        /// written at all, because the file says it worked.
        /// </para>
        /// </remarks>
        [JsonPropertyName("previewPhotos")]
        public ObservableCollection<string> PreviewPhotos
        {
            get => _previewPhotos;
            set => SetProperty(ref _previewPhotos, value ?? new ObservableCollection<string>());
        }

        /// <summary>How many photos the project uses in total, across all books.</summary>
        [JsonPropertyName("totalPhotoCount")]
        public int TotalPhotoCount
        {
            get => _totalPhotoCount;
            set
            {
                SetProperty(ref _totalPhotoCount, value);
                // Both drive the "+N" tile, so they have to be re-raised with it.
                OnPropertyChanged(nameof(RemainingPhotoCount));
                OnPropertyChanged(nameof(HasPreviewPhotos));
            }
        }

        /// <summary>Photos not shown as a thumbnail, i.e. the number in the "+N" tile.</summary>
        [JsonIgnore]
        public int RemainingPhotoCount => Math.Max(0, TotalPhotoCount - PreviewPhotos.Count);

        /// <summary>False hides the whole strip for a project that has no photos yet.</summary>
        [JsonIgnore]
        public bool HasPreviewPhotos => PreviewPhotos.Count > 0;
    }
}


