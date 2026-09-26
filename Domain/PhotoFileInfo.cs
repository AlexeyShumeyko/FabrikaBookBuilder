using System;
using System.IO;

namespace PhotoBookRenamer.Domain
{
    /// <summary>
    /// How many slots across the whole project use a photo. Derived from the books, never
    /// stored: a photo applied to spread 5 of every book is not "marked shared" anywhere,
    /// it simply appears in five slots, and the label follows from counting them.
    /// </summary>
    public enum PhotoUsage
    {
        /// <summary>In no slot at all.</summary>
        Free = 0,

        /// <summary>In exactly one slot.</summary>
        Assigned = 1,

        /// <summary>In two or more slots - a run shared between books.</summary>
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

        /// <summary>Slots across all books holding this photo.</summary>
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

        public PhotoUsage Usage => _usageCount switch
        {
            0 => PhotoUsage.Free,
            1 => PhotoUsage.Assigned,
            _ => PhotoUsage.Shared
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

        /// <summary>Sets the slot count; raises nothing when it did not change.</summary>
        public void SetUsage(int count) => UsageCount = count < 0 ? 0 : count;
    }
}
