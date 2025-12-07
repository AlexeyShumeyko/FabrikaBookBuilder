using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;

namespace PhotoBookRenamer.Models
{
    public class Project : ViewModelBase
    {
        private AppMode _mode;
        private string? _outputFolder;
        private bool _isValid;

        public Project()
        {
            Books = new ObservableCollection<Book>();
            AvailableFiles = new ObservableCollection<string>();
            Books.CollectionChanged += Books_CollectionChanged;
            _isValid = false;
            UpdateIsValid();
        }

        private void Books_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            // Отписываемся от удаленных книг
            if (e.OldItems != null)
            {
                foreach (Book book in e.OldItems)
                {
                    book.PropertyChanged -= Book_PropertyChanged;
                }
            }
            
            // Подписываемся на добавленные книги
            if (e.NewItems != null)
            {
                foreach (Book book in e.NewItems)
                {
                    book.PropertyChanged += Book_PropertyChanged;
                }
            }
            
            UpdateIsValid();
        }

        private void Book_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(Book.IsValid))
            {
                UpdateIsValid();
            }
        }

        private void UpdateIsValid()
        {
            var newValue = Books.Count > 0 && Books.All(b => b.IsValid);
            if (SetProperty(ref _isValid, newValue, nameof(IsValid)))
            {
                // Уведомляем об изменении IsValid
            }
        }

        public AppMode Mode
        {
            get => _mode;
            set => SetProperty(ref _mode, value);
        }

        public string? OutputFolder
        {
            get => _outputFolder;
            set => SetProperty(ref _outputFolder, value);
        }

        public ObservableCollection<Book> Books { get; }
        public ObservableCollection<string> AvailableFiles { get; }

        public bool IsValid
        {
            get => _isValid;
            private set => SetProperty(ref _isValid, value);
        }

        public Project Clone()
        {
            var clone = new Project
            {
                Mode = Mode,
                OutputFolder = OutputFolder
            };

            foreach (var book in Books)
            {
                clone.Books.Add(book.Clone());
            }

            foreach (var file in AvailableFiles)
            {
                clone.AvailableFiles.Add(file);
            }

            return clone;
        }
    }
}

