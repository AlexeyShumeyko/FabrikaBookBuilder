using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;

namespace PhotoBookRenamer.Domain
{
    public class Book : ViewModelBase
    {
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
            _isValid = false; // Инициализируем значение
            _allSlots.Add(0); // Обложка всегда есть
            UpdatePageSlots();
            
            // Подписываемся на изменения обложки, если она уже установлена
            if (_cover != null)
            {
                _cover.PropertyChanged += Page_PropertyChanged;
            }
        }

        private void Page_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            // Уведомляем об изменении Cover или Pages, чтобы обновить биндинги
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
            // Отписываемся от удаленных страниц
            if (e.OldItems != null)
            {
                foreach (Page page in e.OldItems)
                {
                    page.PropertyChanged -= Page_PropertyChanged;
                }
            }
            
            // Подписываемся на добавленные страницы
            if (e.NewItems != null)
            {
                foreach (Page page in e.NewItems)
                {
                    page.PropertyChanged += Page_PropertyChanged;
                }
            }
            
            UpdatePageSlots();
            // Уведомляем об изменении коллекции Pages для обновления конвертеров
            OnPropertyChanged(nameof(Pages));
            UpdateIsValid();
        }

        public void UpdatePageSlots()
        {
            var pagesWithoutCover = Pages.Where(p => !p.IsCover).ToList();
            // Создаем слоты для разворотов (Pages - это развороты)
            var newSlots = Enumerable.Range(1, pagesWithoutCover.Count).ToList();
            
            // Обновляем коллекцию слотов страниц
            _pageSlots.Clear();
            foreach (var slot in newSlots)
            {
                _pageSlots.Add(slot);
            }
            
            // Обновляем коллекцию всех слотов (обложка + развороты)
            _allSlots.Clear();
            _allSlots.Add(0); // Обложка
            foreach (var slot in newSlots)
            {
                _allSlots.Add(slot);
            }
            
            _allSlotsPages.Clear();
            if (Cover != null)
            {
                _allSlotsPages.Add(Cover); // Обложка
            }
            foreach (var page in pagesWithoutCover.OrderBy(p => p.Index))
            {
                _allSlotsPages.Add(page);
            }

            // Publish the export name for every slot so the UI can show the numbering the
            // batch uploader will see. Kept in sync here so no view has to reach back up to
            // the owning Book to work out which book a Page belongs to.
            RefreshExportFileNames();

            OnPropertyChanged(nameof(PageSlots));
            OnPropertyChanged(nameof(AllSlots));
            OnPropertyChanged(nameof(AllSlotsPages));
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
                // Отписываемся от старой обложки
                if (_cover != null)
                {
                    _cover.PropertyChanged -= Page_PropertyChanged;
                }
                
                if (SetProperty(ref _cover, value))
                {
                    // Подписываемся на новую обложку
                    if (_cover != null)
                    {
                        _cover.PropertyChanged += Page_PropertyChanged;
                    }
                    UpdatePageSlots(); // Обновляем слоты при изменении обложки
                    UpdateIsValid(); // Обновляем IsValid при изменении обложки
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
        
        public ObservableCollection<int> PageSlots => _pageSlots;
        
        // Коллекция для отображения: обложка (0) + страницы (1, 2, 3...)
        public ObservableCollection<int> AllSlots => _allSlots;
        public ObservableCollection<Page> AllSlotsPages => _allSlotsPages;

        private bool _isValid;
        
        public bool IsValid
        {
            get
            {
                // Вычисляем значение каждый раз при обращении
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
        }

        public Book Clone()
        {
            var clone = new Book
            {
                FolderPath = FolderPath,
                Name = Name,
                BookIndex = BookIndex,
                Cover = Cover?.Clone()
            };

            foreach (var page in Pages)
            {
                clone.Pages.Add(page.Clone());
            }

            return clone;
        }
    }
}





