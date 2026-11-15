using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using PhotoBook.Application.Sites;
using PhotoBook.Application.Sites.Catalog;
using CoreBook = PhotoBook.Core.Book;

namespace PhotoBook.Presentation.ViewModels
{
    /// <summary>
    /// The parameters of every format chosen in the order, as the page asks them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Questions belong to the format, not to a copy: ten books of one format are one identical
    /// order, so the page asks once and writes the answer into every book of that format. What
    /// is asked comes from the uploader's own document, which the catalog service keeps, so
    /// returning to the page costs the site no request.
    /// </para>
    /// <para>
    /// An answer changes what the site offers next - an interlayer exists for paper and not for
    /// cardboard - so the block is rebuilt from the answers stored in the books rather than
    /// patched in place.
    /// </para>
    /// </remarks>
    public sealed class SiteOrderParameters
    {
        private readonly ISiteCatalogService? _catalog;
        private readonly ISiteSessionStore? _sessions;

        private IReadOnlyList<OrderFormatParameters> _blocks = Array.Empty<OrderFormatParameters>();

        public SiteOrderParameters(ISiteCatalogService? catalog, ISiteSessionStore? sessions)
        {
            _catalog = catalog;
            _sessions = sessions;
        }

        /// <summary>Raised when an answer was written into the books.</summary>
        public event EventHandler? Answered;

        /// <summary>The blocks on the page, one per format.</summary>
        public IReadOnlyList<OrderFormatParameters> Blocks
        {
            get => _blocks;
            private set => _blocks = value ?? Array.Empty<OrderFormatParameters>();
        }

        /// <summary>Whether every required parameter of every format has an answer.</summary>
        public bool IsAnswered => Blocks.All(block => block.IsAnswered);

        /// <summary>The reason the parameters could not be read, if they could not.</summary>
        public string? Problem { get; private set; }

        /// <summary>Reads the questions for the formats the rows carry right now.</summary>
        public async Task LoadAsync(IReadOnlyList<OrderBookRow> rows)
        {
            Problem = null;

            if (_catalog is null || _sessions is null)
                return;

            var session = _sessions.Load();
            if (session is null)
                return;

            var blocks = new List<OrderFormatParameters>();

            foreach (var choice in rows
                         .Select(row => row.SelectedFormat)
                         .Where(choice => choice is not null)
                         .Select(choice => choice!)
                         .DistinctBy(choice => (choice.Product.Id, choice.Format.Id)))
            {
                var block = await BuildAsync(session.Region, choice, rows).ConfigureAwait(true);

                if (block is null)
                    continue;

                blocks.Add(block);
            }

            Blocks = blocks;
        }

        /// <summary>Rebuilds one format's block after its answers changed something.</summary>
        public async Task ReloadAsync(FormatChoice choice, IReadOnlyList<OrderBookRow> rows)
        {
            if (_catalog is null || _sessions is null)
                return;

            var session = _sessions.Load();
            if (session is null)
                return;

            var rebuilt = await BuildAsync(session.Region, choice, rows).ConfigureAwait(true);
            if (rebuilt is null)
                return;

            Blocks = Blocks
                .Select(block => block.Choice == choice ? rebuilt : block)
                .ToList();
        }

        /// <summary>Forgets the questions, for when the order no longer has formats.</summary>
        public void Clear()
        {
            Problem = null;
            Blocks = Array.Empty<OrderFormatParameters>();
        }

        private async Task<OrderFormatParameters?> BuildAsync(
            SiteRegion region,
            FormatChoice choice,
            IReadOnlyList<OrderBookRow> rows)
        {
            var document = await _catalog!
                .GetFormatAsync(region, choice.Product.Id, choice.Format.Id)
                .ConfigureAwait(true);

            if (!document.IsSuccess || document.Value is null)
            {
                Problem = document.Error ?? "Описание формата не открылось.";
                return null;
            }

            var variant = document.Value.Variants.FirstOrDefault(item => item.Id == choice.Format.Id);
            if (variant is null)
            {
                Problem = "Сайт не описал выбранный формат.";
                return null;
            }

            return new OrderFormatParameters(choice, BuildRows(choice, variant, rows));
        }

        /// <summary>
        /// The parameters the site would accept now, with the answers already stored in the books.
        /// </summary>
        private List<OrderParameterRow> BuildRows(
            FormatChoice choice,
            ProductVariant variant,
            IReadOnlyList<OrderBookRow> rows)
        {
            var books = BooksOf(choice, rows);
            var chosen = books.Count > 0
                ? books[0].SiteOptions
                : new Dictionary<long, long>();

            var rowsOut = new List<OrderParameterRow>();

            foreach (var option in OfferedParameters.Of(variant))
            {
                if (!OfferedParameters.Available(new[] { option }, chosen).Any())
                    continue;

                var selected = option.Elements
                    .Where(element => chosen.TryGetValue(option.Id, out var id) && id == element.Id)
                    .Select(element => new ParameterElement(element))
                    .FirstOrDefault();

                rowsOut.Add(new OrderParameterRow(
                    option,
                    option.Elements.Select(element => new ParameterElement(element)).ToList(),
                    selected,
                    () => OnAnswered(choice, rows)));
            }

            return rowsOut;
        }

        private async void OnAnswered(FormatChoice choice, IReadOnlyList<OrderBookRow> rows)
        {
            var block = Blocks.FirstOrDefault(item => item.Choice == choice);
            if (block is null) return;

            foreach (var book in BooksOf(choice, rows))
            {
                foreach (var row in block.Parameters)
                {
                    if (row.Selected is { } selected)
                        book.SetSiteOption(row.OptionId, selected.Element.Id);
                    else
                        book.RemoveSiteOption(row.OptionId);
                }
            }

            Answered?.Invoke(this, EventArgs.Empty);

            try
            {
                await ReloadAsync(choice, rows).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                Problem = "Параметры формата не обновились: " + ex.Message;
            }
        }

        private static IReadOnlyList<CoreBook> BooksOf(FormatChoice choice, IReadOnlyList<OrderBookRow> rows) =>
            rows
                .Where(row => row.SelectedFormat is { } taken &&
                              taken.Product.Id == choice.Product.Id &&
                              taken.Format.Id == choice.Format.Id)
                .Select(row => row.Book)
                .ToList();
    }
}
