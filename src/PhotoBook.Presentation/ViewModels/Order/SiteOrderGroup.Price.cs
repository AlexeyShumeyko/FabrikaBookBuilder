using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using PhotoBook.Application.Sites.Books.Order;
using PhotoBook.Application.Sites.Books.Pricing;
using PhotoBook.Application.Sites.Pricing;
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
                _priceProblem = "Цена считается после входа на сайт.";
                NotifyProblem();
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
                    SetPlan(null);
                    _priceProblem = read.Error ?? "Описание формата не открылось.";
                    NotifyProblem();
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
                    // The order could not be assembled at all. Each refused book carries its
                    // own sentence, so the page opens that book instead of repeating the same
                    // list over every group.
                    AttachDraft(priced.Draft);
                    SetPlan(null);
                    NotifyProblem();
                    return;
                }

                AttachPrice(priced.Quote);

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
                NotifyProblem();
            }
            catch (Exception ex)
            {
                _priceProblem = "Цену не удалось получить: " + ex.Message;
                NotifyProblem();
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
            NotifyProblem();

            _changed();

            if (plan is { Groups.Count: > 0 })
                StartFileCheck(plan);
            else
                ForgetFiles();
        }

        /// <summary>Puts every refused book's own reason on its own row.</summary>
        private void AttachDraft(OrderDraft? draft)
        {
            foreach (var row in Books)
                row.ClearProblem();

            _draftNote = null;

            var placed = draft?.BookProblems;
            if (placed is null || placed.Count == 0)
            {
                if (draft is { Problems.Count: > 0 })
                    _draftNote = string.Join(" ", draft.Problems);

                return;
            }

            var lines = new List<string>();

            foreach (var problem in placed)
            {
                var row = Books.FirstOrDefault(item => ReferenceEquals(item.Book, problem.Book));
                if (row is null)
                {
                    lines.Add(problem.Message);
                    continue;
                }

                row.ShowProblem(WithoutName(problem.Message, row.Name), expand: true);
            }

            if (lines.Count > 0)
                _draftNote = string.Join(" ", lines);
        }

        /// <summary>Explains a price the site refused, per book or once for the whole group.</summary>
        private void AttachPrice(GroupQuote quote)
        {
            foreach (var row in Books)
                row.ClearProblem();

            _priceProblem = null;

            var missing = quote.Books.Where(book => !book.IsPriced).ToList();
            if (missing.Count == 0)
                return;

            // Every book refused means the group's answers are the problem, not a book: one
            // sentence beside the parameters is worth more than the same line on five rows.
            if (missing.Count == quote.Books.Count)
            {
                var check = missing[0].Check;

                _priceProblem = check.Outcome == SitePriceOutcome.Invalid
                    ? "Сайт не продаёт книги с таким сочетанием параметров. " +
                      "Выберите другие параметры формата."
                    : "Сайт не назвал цену: " + (check.Error ?? "сайт не ответил") + ".";
                return;
            }

            foreach (var book in missing)
            {
                var row = Books.FirstOrDefault(item => ReferenceEquals(item.Book, book.Book.Origin));
                if (row is not null)
                    row.ShowProblem(PriceProblemOf(book.Check));
            }

            _priceProblem = "Сайт не назвал цену для книг: " +
                string.Join(", ", missing.Select(book => book.Number)) +
                ". Откройте книгу, чтобы увидеть причину.";
        }

        private static string PriceProblemOf(SitePriceCheck check) => check.Outcome switch
        {
            SitePriceOutcome.Invalid => "Сайт не продаёт книгу с такими параметрами. " +
                "Выберите другие параметры формата.",
            SitePriceOutcome.Error => "Цену не удалось получить: " + (check.Error ?? "сайт не ответил") + ".",
            _ => check.Error ?? "Цена не получена.",
        };

        /// <summary>The draft's sentence without the book's name, which the row already shows.</summary>
        private static string WithoutName(string message, string name)
        {
            var value = (message ?? string.Empty).Trim();
            var prefix = name + ": ";

            if (value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                value = value.Substring(prefix.Length);

            return value.Length == 0
                ? value
                : char.ToUpperInvariant(value[0]) + value.Substring(1);
        }
    }
}
