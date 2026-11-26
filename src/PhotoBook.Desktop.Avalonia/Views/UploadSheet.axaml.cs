using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Transformation;
using Avalonia.Threading;
using PhotoBook.Presentation.ViewModels;

namespace PhotoBook.Shell.Views
{
    /// <summary>What the upload panel answers with, when its own buttons are not enough.</summary>
    /// <remarks>
    /// A reference type on purpose: the panel is an <see cref="OverlaySheet{T}"/>, which is
    /// constrained to classes, and the absence of an answer means the panel was closed - which
    /// is the one reading that does not act on anything.
    /// </remarks>
    public sealed class UploadAnswer
    {
        private UploadAnswer()
        {
        }

        /// <summary>The run goes on, out of the way, and the page is the customer's again.</summary>
        public static readonly UploadAnswer Minimized = new();

        /// <summary>The order is finished with, so the session it belonged to ends.</summary>
        public static readonly UploadAnswer Projects = new();
    }

    /// <summary>
    /// The panel that asks, shows the run and reports it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// It is a frame, not a screen: what it shows and what may happen are the view model's, and
    /// the panel only turns clicks into its calls. That is why the same view model can be handed
    /// to it as is, and why leaving it is the only decision the panel makes itself.
    /// </para>
    /// <para>
    /// A run is never closed, only put aside: the shell takes this panel down and leaves a plate
    /// in the corner of the window with the same bar and the same percentage, so the customer
    /// can work while it goes and come back to it. Closing during a run is that same request,
    /// because a cross that silently threw the panel away would leave a run nobody could watch.
    /// </para>
    /// </remarks>
    public partial class UploadSheet : OverlaySheet<UploadAnswer>
    {
        private readonly SiteUploadViewModel _screen;

        public UploadSheet(SiteUploadViewModel screen)
        {
            InitializeComponent();

            _screen = screen;
            DataContext = screen;

            KeyDown += OnKeyDown;
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);

            // Out of nothing and into place, the way every panel in this program arrives; and
            // the focus asked for again once the layer is up, or Escape is swallowed by the
            // button on the page that opened this one.
            Focus();

            Dispatcher.UIThread.Post(
                () =>
                {
                    Card.RenderTransform = TransformOperations.Parse("translate(0px,0px) scale(1)");
                    Card.Opacity = 1;
                    Focus();
                },
                DispatcherPriority.Loaded);
        }

        private void OnKeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key != Key.Escape)
                return;

            e.Handled = true;

            if (_screen.IsRunning)
                Finish(UploadAnswer.Minimized);
            else
                Leave();
        }

        private void OnSendClick(object? sender, RoutedEventArgs e) => _ = _screen.UploadAsync();

        private void OnCancelClick(object? sender, RoutedEventArgs e) => _screen.Cancel();

        private void OnMinimizeClick(object? sender, RoutedEventArgs e) => Finish(UploadAnswer.Minimized);

        private void OnOpenSiteClick(object? sender, RoutedEventArgs e)
        {
            if (!_screen.CanOpenSite)
                return;

            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = _screen.OnSiteLink,
                    UseShellExecute = true
                });
            }
            catch (Exception)
            {
                // The browser is a convenience; the project is already on the site, so the
                // panel hands the customer the address instead of calling the run failed.
                _screen.ReportLinkFailure();
            }
        }

        /// <summary>Puts the order's address on the clipboard, which is what people do with it.</summary>
        private async void OnCopyLinkClick(object? sender, RoutedEventArgs e)
        {
            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard is null)
                return;

            try
            {
                await clipboard.SetTextAsync(_screen.OnSiteLink);
            }
            catch (Exception)
            {
                // A clipboard that refuses is the system's business, not a failed upload.
            }
        }

        /// <summary>Leaves for the projects list, which is where a finished order belongs.</summary>
        private void OnProjectsClick(object? sender, RoutedEventArgs e) => Finish(UploadAnswer.Projects);

        private void OnCloseClick(object? sender, RoutedEventArgs e) => Leave();
    }
}
