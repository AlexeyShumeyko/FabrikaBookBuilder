using System;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace PhotoBookRenamer.Domain
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
    }
}



