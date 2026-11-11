using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PhotoBook.Application.Sites;
using PhotoBook.Application.Sites.Books;
using PhotoBook.Application.Sites.Books.Pricing;
using PhotoBook.Application.Sites.Pricing;

namespace PhotoBook.Application.Tests.Sites.Books.Pricing
{
    /// <summary>
    /// The order's total is the sum of its books, and a book the site would not price is named
    /// rather than swallowed into that sum.
    /// </summary>
    public class GroupQuoteServiceTests
    {
        private static readonly SiteSession Session =
            new(SiteRegion.Com, "token-value", new DateTimeOffset(2026, 11, 10, 12, 0, 0, TimeSpan.Zero));

        private static SitePriceCheck Price(decimal amount) =>
            SitePriceCheck.Valid(new SitePriceQuote(amount, amount.ToString("0.00") + " RUB", "RUB"));

        private static Book Book() => BookPriceQueryTests.Book(
            BookPriceQueryTests.Part(94, 10, (65738, 420268)));

        private static GroupQuoteService Service(StubPrices prices) => new(prices);

        [Fact]
        public async Task PricesEveryBookAndTheOrderMatchesTheirSum()
        {
            var prices = new StubPrices(Price(542m), Price(1410m));

            var quote = await Service(prices).QuoteAsync(Session, new[] { Book(), Book() });

            Assert.True(quote.IsPriced);
            Assert.Equal(1952m, quote.Total);
            Assert.Equal("1 952,00 RUB", quote.TotalText.Replace('\u00A0', ' '));
            Assert.Empty(quote.Problems);
            Assert.Equal(2, prices.Asked.Count);
        }

        [Fact]
        public async Task ReportsARefusedBookByItsPlaceInTheOrder()
        {
            var prices = new StubPrices(Price(542m), SitePriceCheck.Invalid());

            var quote = await Service(prices).QuoteAsync(Session, new[] { Book(), Book() });

            Assert.False(quote.IsPriced);
            Assert.Equal(542m, quote.Total);
            Assert.Equal(new[] { "Книга 2: Комбинация недоступна." }, quote.Problems);
        }

        [Fact]
        public async Task ReportsALostConnectionAsSomethingOtherThanARefusal()
        {
            var prices = new StubPrices(SitePriceCheck.Failed("Нет соединения с сайтом."));

            var quote = await Service(prices).QuoteAsync(Session, new[] { Book() });

            Assert.False(quote.IsPriced);
            Assert.Equal(0m, quote.Total);
            Assert.Equal(new[] { "Книга 1: Нет соединения с сайтом." }, quote.Problems);
        }

        [Fact]
        public async Task KeepsAnOrderWithoutBooksOutOfTheSum()
        {
            var prices = new StubPrices();

            var quote = await Service(prices).QuoteAsync(Session, Array.Empty<Book>());

            Assert.False(quote.IsPriced);
            Assert.Equal(0m, quote.Total);
            Assert.Equal(string.Empty, quote.Currency);
            Assert.Equal(new[] { "В заказе нет ни одной книги." }, quote.Problems);
            Assert.Empty(prices.Asked);
        }

        [Fact]
        public async Task AsksOneBookAtATimeAndKeepsTheirOrder()
        {
            var prices = new StubPrices(Price(1m), Price(2m), Price(3m));

            var quote = await Service(prices).QuoteAsync(Session, new[] { Book(), Book(), Book() });

            Assert.Equal(new[] { 1, 2, 3 }, quote.Books.Select(book => book.Number).ToList());
            Assert.Equal(3, prices.Asked.Count);
            Assert.Equal(6m, quote.Total);
        }

        [Fact]
        public async Task StopsBeforeTheFirstBookWhenCancelled()
        {
            var prices = new StubPrices(Price(542m));
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                Service(prices).QuoteAsync(Session, new[] { Book() }, cancellation.Token));

            Assert.Empty(prices.Asked);
        }

        private sealed class StubPrices : ISitePriceService
        {
            private readonly Queue<SitePriceCheck> _answers;

            internal StubPrices(params SitePriceCheck[] answers) =>
                _answers = new Queue<SitePriceCheck>(answers);

            internal List<PriceQuery> Asked { get; } = new();

            public Task<SitePriceCheck> CheckAsync(
                SiteSession session,
                PriceQuery query,
                CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Asked.Add(query);

                return Task.FromResult(
                    _answers.Count > 0 ? _answers.Dequeue() : SitePriceCheck.Invalid());
            }
        }
    }
}
