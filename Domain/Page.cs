using System;
using System.Text.Json.Serialization;

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
        private int _imageWidth;
        private int _imageHeight;
        private double _frameAspect;
        private bool _isShared;

        public string? SourcePath
        {
            get => _sourcePath;
            set
            {
                if (SetProperty(ref _sourcePath, value))
                {
                    // Уведомляем об изменении IsEmpty при изменении SourcePath
                    OnPropertyChanged(nameof(IsEmpty));
                    OnPropertyChanged(nameof(SlotLabel));
                    OnPropertyChanged(nameof(IsSlotLabelAccent));
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
            set
            {
                if (SetProperty(ref _isCover, value))
                {
                    OnPropertyChanged(nameof(SlotLabel));
                    OnPropertyChanged(nameof(IsSlotLabelAccent));
                }
            }
        }

        public int Index
        {
            get => _index;
            set
            {
                if (SetProperty(ref _index, value))
                    OnPropertyChanged(nameof(SlotLabel));
            }
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
        /// Pixel size of the source file, filled once from disk and then kept in the
        /// project file.
        ///
        /// The card frame is shaped from these numbers instead of a fixed ratio: the real
        /// formats in one folder differ far too much for one frame (measured 0.67 for
        /// post-prints, 1.0 square, 1.4 for 21x30, 1.9 for 20x20/25x25/30x30 spreads,
        /// 2.7-3.0 for panoramas). Persisting the size means the frame does not depend on
        /// re-reading every file on each open, and it survives the photos being moved.
        /// </summary>
        public int ImageWidth
        {
            get => _imageWidth;
            set => SetProperty(ref _imageWidth, value);
        }

        public int ImageHeight
        {
            get => _imageHeight;
            set => SetProperty(ref _imageHeight, value);
        }

        public bool HasDimensions => _imageWidth > 0 && _imageHeight > 0;

        public double AspectRatio => HasDimensions ? (double)_imageWidth / _imageHeight : 0d;

        /// <summary>
        /// Ratio of the frame this page's card should use, decided by its book and pushed
        /// onto the page so the card template can bind it without reaching for the book.
        /// Derived, so it is not persisted - <see cref="ImageWidth"/> and
        /// <see cref="ImageHeight"/> are the source of truth.
        /// </summary>
        [JsonIgnore]
        public double FrameAspect
        {
            get => _frameAspect;
            set => SetProperty(ref _frameAspect, value);
        }

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
        /// True when this same photo stands in a slot of ANOTHER book, which the slot
        /// shows as "Общий разворот" / "Общая обложка".
        ///
        /// Stamped by the editor rather than computed here: a Page does not know the other
        /// books, and the count has to be refreshed whenever any of them changes. Two slots
        /// of the same book holding one photo do NOT count - that is a duplicate, not a
        /// shared spread.
        /// </summary>
        [JsonIgnore]
        public bool IsShared
        {
            get => _isShared;
            set
            {
                if (SetProperty(ref _isShared, value))
                {
                    OnPropertyChanged(nameof(SlotLabel));
                    OnPropertyChanged(nameof(IsSlotLabelAccent));
                }
            }
        }

        /// <summary>
        /// Nudges the view to re-read ThumbnailPath after a background thumbnail pass
        /// finishes, so newly generated previews appear without rebuilding the whole view.
        /// </summary>
        public void RaiseThumbnailChanged() => OnPropertyChanged(nameof(ThumbnailPath));

        /// <summary>
        /// What the slot's footer says: "Обложка", "Разворот 2", "Разворот 2 (Сквозной)"
        /// or "Пустой слот".
        ///
        /// Assembled here rather than in the view: the shared case is three conditions in
        /// one string, and XAML style triggers cannot say "the last match wins" without a
        /// pile of MultiTriggers that nobody can read afterwards.
        /// </summary>
        public string SlotLabel
        {
            get
            {
                if (IsEmpty) return "Пустой слот";
                if (IsShared) return IsCover ? "Общая обложка" : $"Разворот {Index} (Сквозной)";
                return IsCover ? "Обложка" : $"Разворот {Index}";
            }
        }

        /// <summary>
        /// The footer label is the brand colour for the cover and for a run-wide spread,
        /// grey otherwise - the same distinction the photo badge makes.
        /// </summary>
        public bool IsSlotLabelAccent => !IsEmpty && (IsCover || IsShared);

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
                ExportFileName = ExportFileName,
                ImageWidth = ImageWidth,
                ImageHeight = ImageHeight
            };
        }
    }
}





