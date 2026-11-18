using System;
using System.Linq;
using System.Threading.Tasks;
using PhotoBook.Application.Sites.Upload;

namespace PhotoBook.Presentation.ViewModels
{
    /// <summary>The pricing half of the order page: when the site is asked, and what it said.</summary>
    /// <remarks>
    /// The page asks for a price itself, once the answers are complete. Keeping that here rather
    /// than among the assembling keeps one question in one place: a price is what the site says
    /// about a settled set of answers, and the page never asks twice for the same set.
    /// </remarks>
    public sealed partial class SiteOrderViewModel
    {
        private bool _isPricing;
        private Task _priceRun = Task.CompletedTask;
        private bool _repriceAfterRun;


        /// <summary>Whether the site is being asked for a price right now.</summary>
        public bool IsPricing
        {
            get => _isPricing;
            private set
            {
                if (!SetProperty(ref _isPricing, value))
                    return;

                // A price run also settles whether the order may be sent: the plan it builds
                // is what enables the button, and the button was staying grey because only the
                // price button was told the run had finished.
                OnPropertyChanged(nameof(CanUpload));
                OnPropertyChanged(nameof(BlockingText));
                OnPropertyChanged(nameof(HasBlockingText));
            }
        }

        /// <summary>
        /// The price the page is asking for right now, so a reader can wait for the answer.
        /// </summary>
        public Task Pricing => _priceRun;

        /// <summary>
        /// Asks the site for the order's price and shows it book by book.
        /// </summary>
        /// <remarks>
        /// The books travel as the order itself, not as the page's own idea of it: the same
        /// assembly that the upload will use builds the request, so the price shown is the price
        /// of what would be sent. A book that cannot be assembled is reported by name, and the
        /// rest of the order is still priced - the total is worth having even when one book
        /// needs an answer.
        /// </remarks>
        public async Task RecalculateAsync()
        {
            if (_catalog is null || _sessions is null || _prices is null || _project is null)
                return;

            var session = _sessions.Load();
            if (session is null)
            {
                Note("Цена считается после входа на сайт.");
                return;
            }

            IsPricing = true;
            try
            {
                var read = await ChosenFormatDocuments
                    .ReadAsync(_catalog, session.Region, Books.Select(row => row.SelectedFormat))
                    .ConfigureAwait(true);

                if (!read.IsSuccess || read.Value is null)
                {
                    Note(read.Error ?? "Описание формата не открылось.");
                    return;
                }

                var formats = read.Value;

                foreach (var row in Books)
                    row.ForgetPrice();

                TotalText = string.Empty;

                var priced = await _prices
                    .PriceAsync(session, _project, formats)
                    .ConfigureAwait(true);

                if (priced.Quote is null)
                {
                    Note(string.Join(Environment.NewLine, priced.Problems));
                    return;
                }

                for (var index = 0; index < Books.Count && index < priced.Quote.Books.Count; index++)
                {
                    var book = priced.Quote.Books[index];
                    Books[index].ShowPrice(book.IsPriced ? book.Check.Quote!.Formatted : "не названа");
                }

                if (priced.IsPriced)
                {
                    TotalText = priced.TotalText;
                    Note(string.Empty);
                }
                else
                {
                    Note(string.Join(Environment.NewLine, priced.Problems));
                }

                // The plan is built from the answer that was just priced, so the button sends
                // exactly what the customer saw a total for.
                SetPlan(UploadPlans.Build(session.Region, priced.Draft, priced.Quote, formats));
            }
            catch (Exception ex)
            {
                Note("Цена не получена: " + ex.Message);
            }
            finally
            {
                IsPricing = false;
            }
        }

        /// <summary>
        /// Asks the site for the price as soon as the order is answerable, without a button.
        /// </summary>
        /// <remarks>
        /// Runs are not overlapped: while the site is answering, another answer only marks the
        /// price stale, and the same run asks once more at the end. That way the total shown is
        /// the total of the answers on the page, never of the set they replaced.
        /// </remarks>
        private void PriceIfReady()
        {
            if (!IsReady || _prices is null)
                return;

            if (!_priceRun.IsCompleted)
            {
                _repriceAfterRun = true;
                return;
            }

            _priceRun = RunPriceAsync();
        }

        private async Task RunPriceAsync()
        {
            do
            {
                _repriceAfterRun = false;
                await RecalculateAsync().ConfigureAwait(true);
            }
            while (_repriceAfterRun && IsReady);
        }
    }
}
