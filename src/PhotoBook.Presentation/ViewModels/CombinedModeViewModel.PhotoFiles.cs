using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PhotoBook.Core;
using PhotoBook.Presentation;
using PhotoBook.Application;

namespace PhotoBook.Presentation.ViewModels
{
    /// <summary>The files the photographer brought, measured against the frame of the run.</summary>
    public partial class CombinedModeViewModel : ViewModelBase, IProjectEditor
    {

        /// <summary>
        /// Mirrors AvailableFiles into <see cref="PhotoFiles"/>. Entries that survive keep
        /// the size already read for them, so re-syncing after a drop does not re-measure
        /// hundreds of files; only the new ones are measured, off the UI thread.
        /// </summary>
        private void SyncPhotoFiles()
        {
            var wanted = AvailableFiles
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var present = new HashSet<string>(wanted, StringComparer.OrdinalIgnoreCase);
            for (int i = PhotoFiles.Count - 1; i >= 0; i--)
            {
                if (!present.Contains(PhotoFiles[i].Path))
                    PhotoFiles.RemoveAt(i);
            }

            var toMeasure = new List<PhotoFileInfo>();
            foreach (var path in wanted)
            {
                bool exists = PhotoFiles.Any(f => string.Equals(f.Path, path, StringComparison.OrdinalIgnoreCase));
                if (exists) continue;

                var info = new PhotoFileInfo(path);
                PhotoFiles.Add(info);
                toMeasure.Add(info);
            }

            OnPropertyChanged(nameof(FilteredPhotoFiles));
            RefreshPhotoUsage();

            if (toMeasure.Count > 0)
                Background.Run(() => MeasurePhotoFilesAsync(toMeasure), "measure photos");
        }

        /// <summary>Reads each new file's pixel size for the caption in the list.</summary>
        private async Task MeasurePhotoFilesAsync(List<PhotoFileInfo> files)
        {
            foreach (var file in files)
            {
                try
                {
                    var (width, height) = await _imageService.GetImageDimensionsAsync(file.Path);
                    file.SetDimensions(width, height);
                }
                catch
                {
                    // A file that cannot be measured keeps the "…" placeholder; the photo
                    // itself still loads in a slot, and the page's own size decides the
                    // frame, so this caption is not worth failing over.
                }
            }
        }

        // ------------------------------------------------------------------
        //  One frame shape for the whole run
        // ------------------------------------------------------------------

        private double? _runFrameAspect;

        /// <summary>
        /// Decides the frame shape for every book of the run from the SPREADS in it, and
        /// stamps that one shape onto every card. The cover never counts: it is the one
        /// photograph whose format a run cannot rely on - often a scan or a mock-up - and
        /// letting it reshape the project was the "обложка меняет размер" complaint.
        /// </summary>
        /// <remarks>
        /// The decision is a majority of the measured spreads (see
        /// <see cref="RunFrameAspectPolicy"/>), counted once per FILE rather than once per
        /// slot. A shared spread is copied into every book, and counting copies would let a
        /// run of one book outvote a run of ten; the photographs are what the format is
        /// about, not how many books happen to show them.
        ///
        /// This runs after every change of dimensions as well as after structure changes,
        /// so the first measured spread re-shapes the run immediately and emptying or
        /// replacing a photo lets the remaining majority take over.
        /// </remarks>
        private void UpdateRunFrameAspect()
        {
            var aspects = new List<double>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var book in Books)
            {
                foreach (var page in book.Pages)
                {
                    if (page == null || page.IsCover || page.IsEmpty || !page.HasDimensions) continue;
                    if (!seen.Add(page.SourcePath!)) continue;

                    aspects.Add(page.AspectRatio);
                }
            }

            _runFrameAspect = aspects.Count > 0
                ? RunFrameAspectPolicy.Decide(aspects, _runFrameAspect)
                : null;

            // No spreads at all falls back to the default 16:10; with spreads, to the
            // majority format. The cover is absent from the decision in both cases.
            StampRunAspect(_runFrameAspect ?? RunFrameAspectPolicy.DefaultAspect);
        }
    }
}
