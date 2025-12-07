using System;

namespace PhotoBookRenamer.Models
{
    public class Page : ViewModelBase
    {
        private string? _sourcePath;
        private string? _thumbnailPath;
        private bool _isCover;
        private int _index;
        private int _displayIndex;
        private bool _isLocked;
        private string? _fileName;

        public string? SourcePath
        {
            get => _sourcePath;
            set
            {
                if (SetProperty(ref _sourcePath, value))
                {
                    // Уведомляем об изменении IsEmpty при изменении SourcePath
                    OnPropertyChanged(nameof(IsEmpty));
                }
            }
        }

        public string? ThumbnailPath
        {
            get => _thumbnailPath;
            set => SetProperty(ref _thumbnailPath, value);
        }

        public bool IsCover
        {
            get => _isCover;
            set => SetProperty(ref _isCover, value);
        }

        public int Index
        {
            get => _index;
            set => SetProperty(ref _index, value);
        }

        public int DisplayIndex
        {
            get => _displayIndex;
            set => SetProperty(ref _displayIndex, value);
        }

        public bool IsLocked
        {
            get => _isLocked;
            set => SetProperty(ref _isLocked, value);
        }

        public bool IsEmpty => string.IsNullOrEmpty(SourcePath);

        public string? FileName
        {
            get
            {
                if (_fileName != null) return _fileName;
                if (string.IsNullOrEmpty(SourcePath)) return null;
                return System.IO.Path.GetFileName(SourcePath);
            }
            set => SetProperty(ref _fileName, value);
        }

        public Page Clone()
        {
            return new Page
            {
                SourcePath = SourcePath,
                ThumbnailPath = ThumbnailPath,
                IsCover = IsCover,
                Index = Index,
                DisplayIndex = DisplayIndex,
                IsLocked = IsLocked,
                FileName = FileName
            };
        }
    }
}





