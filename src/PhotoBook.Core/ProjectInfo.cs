using System;
using System.Collections.ObjectModel;
using System.Text.Json.Serialization;

namespace PhotoBook.Core
{
    public enum ProjectStatus
    {
        NotFilled,      // Не заполнен
        Ready,          // Готов
        SuccessfullyCompleted  // Успешно завершён
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
                // При десериализации из JSON value может быть null или пустым
                if (string.IsNullOrEmpty(value))
                {
                    value = Guid.NewGuid().ToString();
                }
                // Используем прямое присваивание для поля, чтобы избежать проблем с десериализацией
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
        [JsonIgnore]
        public ObservableCollection<string> PreviewPhotos { get; } = new ObservableCollection<string>();

        /// <summary>How many photos the project uses in total, across all books.</summary>
        [JsonIgnore]
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



