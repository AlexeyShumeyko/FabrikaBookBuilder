using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using PhotoBook.Application.Sites.Upload;

namespace PhotoBook.Presentation.ViewModels
{
    /// <summary>The pricing half of one group: when the site is asked, and what it said.</summary>
    /// <remarks>
    /// A group is one project on the site, so it is priced as one: the books of another format
    /// are a different project, and a single total over both would be a price for an order the
    /// site is never asked to make. The group asks for its own price as soon as its answers are
    /// complete, once per settled set of answers rather than on every keystroke, because the
    /// platform is shared and one book is one request.
    /// </remarks>
    public sealed partial class SiteOrderGroup
    {
        /// <summary>Asks the site for the group's price, and builds the plan it would send.</summary>
        private void PriceIfReady()
        {
            if (!IsReady || _prices is null)
                return;

            // Runs are not overlapped: while the site is answering, another answer only marks the
            // price stale, and the same run asks once more at the end.
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

        /// <summary>Prices this group's books and shows the answer book by book.</summary>
        public async Task RecalculateAsync()
        {
            if (_catalog is null || _sessions is null || _prices is null || Books.Count == 0)
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

                SetPlan(null);

                var priced = await _prices
                    .PriceAsync(session, Books.Select(row => row.Book).ToList(), formats)
                    .ConfigureAwait(true);

                if (priced.Quote is null)
                {
                    Note(string.Join(Environment.NewLine, priced.Problems));
                    return;
                }

                // The price comes back per assembled book; the row it belongs to is found by
                // identity, because the assembly may have left a book out and counting along the
                // list would then put one book's price on another.
                foreach (var quote in priced.Quote.Books)
                {
                    var row = Books.FirstOrDefault(item => ReferenceEquals(item.Book, quote.Book.Origin));

                    if (row is not null && quote.IsPriced)
                        row.ShowPrice(quote.Check.Quote!.Formatted);
                }

                // The price is the site's own text, and the number behind it is what the page
                // sums over its groups. A group whose plan cannot be built - the site named no
                // uploader for the format - is still a group the customer has a price for.
                if (priced.IsPriced)
                {
                    ShowTotal(priced.Quote!.Total, priced.Quote.Currency, priced.TotalText);
                }
                else
                {
                    ShowTotal(0, string.Empty, string.Empty);
                }

                SetPlan(UploadPlans.Build(session.Region, priced.Draft, priced.Quote, formats));

                Note(priced.IsPriced
                    ? string.Empty
                    : string.Join(Environment.NewLine, priced.Problems));
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

        /// <summary>Keeps the plan, the price and the reading of the files in step with it.</summary>
        private void SetPlan(UploadPlan? plan)
        {
            _plan = plan;

            OnPropertyChanged(nameof(Plan));
            OnPropertyChanged(nameof(CanGo));
            OnPropertyChanged(nameof(ProblemText));

            if (plan is { Problems.Count: > 0 })
                Note(string.Join(Environment.NewLine, plan.Problems));

            _changed();

            if (plan is { Groups.Count: > 0 })
                StartFileCheck(plan);
            else
                ForgetFiles();
        }
    }
}
