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
using PhotoBook.Infrastructure;

namespace PhotoBookRenamer.Presentation.ViewModels
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
        /// The spread that decided the shape, by path. While that photo is still in the
        /// run the shape does not move again - a second spread of the same format must
        /// not nudge the cards, and one odd photo pasted into a spread slot must not
        /// average with the rest. A median did both wrong: with an even number of spreads
        /// it returns the midpoint of the middle pair, so a stray 1.9 next to 0.68 gave
        /// 1.29 and the cards swung there and back as more photos arrived.
        ///
        /// Anchoring on a path also makes the shape recoverable without a reset button:
        /// clear that photo, or empty the run and build it again, and the next spread
        /// becomes the anchor. What it must not be is permanent.
        /// </summary>
        private string? _runFrameAspectSource;

        /// <summary>
        /// Decides the frame shape for every book of the run: the first SPREAD in reading
        /// order, the cover never counting. The cover only holds the fort while there is
        /// no spread yet, because a client may well load the cover first.
        /// </summary>
        private void UpdateRunFrameAspect()
        {
            List<string> spreads = new();
            double? firstSpreadAspect = null;

            foreach (var book in Books)
            {
                foreach (var page in book.Pages)
                {
                    if (page == null || page.IsCover || page.IsEmpty || !page.HasDimensions) continue;

                    spreads.Add(page.SourcePath!);
                    firstSpreadAspect ??= page.AspectRatio;
                }
            }

            if (spreads.Count > 0)
            {
                // Keep the anchor while it is still there, so loading photo after photo
                // leaves the cards alone.
                if (_runFrameAspectSource != null && spreads.Contains(_runFrameAspectSource))
                {
                    StampRunAspect(_runFrameAspect ?? firstSpreadAspect!.Value);
                    return;
                }

                // Anchor lost - cleared, or the run was rebuilt from scratch. Re-anchor.
                _runFrameAspect = firstSpreadAspect;
                _runFrameAspectSource = spreads[0];
                StampRunAspect(_runFrameAspect!.Value);
                return;
            }

            // No spreads at all. The shape resets to the default, and the COVER IS IGNORED
            // - our rule, and the second half of it: a cover is the one photo whose
            // format a run cannot rely on, it is often a scan or a mock-up, and letting it
            // reshape every card in the project was the "обложка меняет размер" complaint.
            // Until a spread arrives the cards stay on the default 16:10.
            _runFrameAspect = null;
            _runFrameAspectSource = null;
            StampRunAspect(1.6);
        }
    }
}
