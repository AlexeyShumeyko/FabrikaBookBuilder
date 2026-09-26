using System;

namespace PhotoBookRenamer.Domain
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
        private string? _exportFileName;

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

        /// <summary>
        /// The file name this slot will be exported as, e.g. "001-02.jpg".
        ///
        /// Maintained by <see cref="Book.UpdatePageSlots"/> rather than computed in the
        /// view, because a Page does not know which book owns it. The UI shows this so
        /// the photographer can see the numbering the batch uploader will see.
        /// </summary>
        public string? ExportFileName
        {
            get => _exportFileName;
            set => SetProperty(ref _exportFileName, value);
        }

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

        /// <summary>
        /// Nudges the view to re-read ThumbnailPath after a background thumbnail pass
        /// finishes, so newly generated previews appear without rebuilding the whole view.
        /// </summary>
        public void RaiseThumbnailChanged() => OnPropertyChanged(nameof(ThumbnailPath));

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
                FileName = FileName,
                ExportFileName = ExportFileName
            };
        }
    }
}





