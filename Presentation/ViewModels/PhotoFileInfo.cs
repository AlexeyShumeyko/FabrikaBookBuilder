using System;
using System.IO;

namespace PhotoBookRenamer.Presentation.ViewModels
{
    /// <summary>
    /// Where a photo stands in the run. Derived from the books, never stored: a photo
    /// applied to the same spread of every book is not "marked shared" anywhere, it simply
    /// stands at that position in every book, and the label follows from looking.
    ///
    /// "Shared" means run-wide and nothing looser. The same photograph in two DIFFERENT
    /// positions, or twice inside one book, is the photographer's own repetition - the
    /// owner calls that out explicitly - and calling it shared would hide the one button
    /// ("Во все книги") that actually makes a spread shared.
    /// </summary>
    public enum PhotoUsage
    {
        /// <summary>In no book at all.</summary>
        Free = 0,

        /// <summary>In at least one book, but not as the run-wide photo anywhere.</summary>
        Assigned = 1,

        /// <summary>The run-wide photo for at least one position - every book holds it there.</summary>
        Shared = 2
    }

    /// <summary>
    /// One loaded photo, as the file list needs to show it: the name, the pixel size and
    /// whether it is still free.
    ///
    /// Deliberately NOT part of the saved project. The project file keeps its
    /// <c>AvailableFiles</c> list of plain paths, so old projects keep opening; this is
    /// rebuilt in memory from those paths plus whatever the books are holding. The pixel
    /// size is read from disk once and then cached for the session - it is only a caption,
    /// unlike the page sizes that decide the card frame.
    /// </summary>
    public class PhotoFileInfo : ViewModelBase
    {
        private int _imageWidth;
        private int _imageHeight;
        private int _usageCount;
        private bool _isRunWide;

        public PhotoFileInfo(string path)
        {
            Path = path ?? throw new ArgumentNullException(nameof(path));
        }

        /// <summary>Full path, as stored in the project's AvailableFiles list.</summary>
        public string Path { get; }

        public string Name => System.IO.Path.GetFileName(Path);

        /// <summary>
        /// Name shortened for the narrow list column. Width-based trimming in the view
        /// handles the common case; this is the backstop for a name so long that even the
        /// first line says nothing.
        /// </summary>
        public string ShortName => Name.Length <= 30 ? Name : Name.Substring(0, 29) + "…";

        public int ImageWidth
        {
            get => _imageWidth;
            private set
            {
                if (SetProperty(ref _imageWidth, value))
                    OnPropertyChanged(nameof(DimensionsText));
            }
        }

        public int ImageHeight
        {
            get => _imageHeight;
            private set
            {
                if (SetProperty(ref _imageHeight, value))
                    OnPropertyChanged(nameof(DimensionsText));
            }
        }

        public bool HasDimensions => _imageWidth > 0 && _imageHeight > 0;

        /// <summary>Books holding this photo, at any position.</summary>
        public int UsageCount
        {
            get => _usageCount;
            private set
            {
                if (!SetProperty(ref _usageCount, value)) return;
                OnPropertyChanged(nameof(Usage));
                OnPropertyChanged(nameof(UsageText));
            }
        }

        /// <summary>True when every book holds this photo at the same position.</summary>
        public bool IsRunWide
        {
            get => _isRunWide;
            private set
            {
                if (!SetProperty(ref _isRunWide, value)) return;
                OnPropertyChanged(nameof(Usage));
                OnPropertyChanged(nameof(UsageText));
            }
        }

        public PhotoUsage Usage => _usageCount switch
        {
            0 => PhotoUsage.Free,
            1 => PhotoUsage.Assigned,
            _ => _isRunWide ? PhotoUsage.Shared : PhotoUsage.Assigned
        };

        public string UsageText => Usage switch
        {
            PhotoUsage.Free => "Свободен",
            PhotoUsage.Assigned => "Назначен",
            _ => "Общий"
        };
        /// <summary>"3500 × 2333 px". Placeholder until the file has been measured.</summary>
        public string DimensionsText => HasDimensions ? $"{ImageWidth} × {ImageHeight} px" : "…";

        /// <summary>Fills the caption once, the first time the size is known.</summary>
        public void SetDimensions(int width, int height)
        {
            if (width <= 0 || height <= 0) return;
            if (width == _imageWidth && height == _imageHeight) return;

            _imageWidth = width;
            _imageHeight = height;
            OnPropertyChanged(nameof(ImageWidth));
            OnPropertyChanged(nameof(ImageHeight));
            OnPropertyChanged(nameof(HasDimensions));
            OnPropertyChanged(nameof(DimensionsText));
        }

        /// <summary>
        /// Sets how many books hold the photo and whether it is the run-wide one.
        /// Raises nothing when neither changed.
        /// </summary>
        public void SetUsage(int bookCount, bool runWide)
        {
            UsageCount = bookCount < 0 ? 0 : bookCount;
            IsRunWide = runWide;
        }
    }
}
