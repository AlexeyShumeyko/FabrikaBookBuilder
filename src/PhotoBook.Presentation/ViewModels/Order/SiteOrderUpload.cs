using System;
using PhotoBook.Application.Sites.Upload;

namespace PhotoBook.Presentation.ViewModels
{
    /// <summary>
    /// The order as something to send: the plan the customer may confirm, and the ask itself.
    /// </summary>
    /// <remarks>
    /// Sending is a write, so the offer lives apart from the page that collects formats and
    /// prices: the page hands over a plan when it has a complete one and drops it the moment an
    /// answer changes, and this type is what decides whether that plan may become a request.
    /// </remarks>
    public sealed class SiteOrderUpload
    {
        private readonly Action _changed;

        private UploadPlan? _plan;

        public SiteOrderUpload(Action changed) =>
            _changed = changed ?? throw new ArgumentNullException(nameof(changed));

        /// <summary>Raised when the customer asked to send a complete plan.</summary>
        public event EventHandler? Requested;

        /// <summary>The plan the upload screen is to send, once the customer has asked for it.</summary>
        public UploadPlan? Pending { get; private set; }

        /// <summary>
        /// The plan the page is offering right now, asked for or not.
        /// </summary>
        /// <remarks>
        /// The file check reads this one: it is about the order as it stands, and a check that
        /// waited for the customer to press send would leave the list wordless until then.
        /// </remarks>
        public UploadPlan? Offered { get; private set; }

        /// <summary>Whether there is a complete plan to send right now.</summary>
        public bool CanUpload => _plan is { IsComplete: true };

        /// <summary>Keeps the offer in step with the answer the page just received.</summary>
        public void Offer(UploadPlan? plan)
        {
            _plan = plan;
            Offered = plan;
            Pending = null;

            _changed();
        }

        /// <summary>Hands the plan to the shell, or does nothing when there is none.</summary>
        public void Request()
        {
            if (_plan is not { IsComplete: true })
                return;

            Pending = _plan;
            Requested?.Invoke(this, EventArgs.Empty);
        }
    }
}
