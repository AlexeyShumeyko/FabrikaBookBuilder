using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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

        /// <summary>Whether a block is being rebuilt right now.</summary>
        private bool _reloading;


        public SiteOrderParameters(ISiteCatalogService? catalog, ISiteSessionStore? sessions)
        {
            _catalog = catalog;
            _sessions = sessions;
        }

        /// <summary>Raised when an answer was written into the books.</summary>
        public event EventHandler? Answered;

        /// <summary>The blocks on the page, one per format.</summary>
        /// <remarks>
        /// The collection is updated, never replaced: the window measures the panels while it
        /// arranges them, and a list swapped underneath a measuring panel is what crashed the
        /// page once.
        /// </remarks>
        public ObservableCollection<OrderFormatParameters> Blocks { get; } =
            new ObservableCollection<OrderFormatParameters>();

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

            var choices = rows
                .Select(row => row.SelectedFormat)
                .Where(choice => choice is not null)
                .Select(choice => choice!)
                .DistinctBy(choice => (choice.Product.Id, choice.Format.Id))
                .ToList();

            for (var index = Blocks.Count - 1; index >= 0; index--)
            {
                if (!choices.Contains(Blocks[index].Choice))
                    Blocks.RemoveAt(index);
            }

            foreach (var choice in choices)
            {
                var offered = await BuildRowsAsync(session.Region, choice, rows).ConfigureAwait(true);
                if (offered is null)
                    continue;

                var existing = Blocks.FirstOrDefault(block => block.Choice == choice);

                if (existing is null)
                    Blocks.Add(new OrderFormatParameters(choice, offered));
                else
                    existing.Sync(offered);
            }
        }

        /// <summary>Rebuilds one format's block after its answers changed something.</summary>
        public async Task ReloadAsync(FormatChoice choice, IReadOnlyList<OrderBookRow> rows)
        {
            if (_catalog is null || _sessions is null)
                return;

            var session = _sessions.Load();
            if (session is null)
                return;

            var offered = await BuildRowsAsync(session.Region, choice, rows).ConfigureAwait(true);
            if (offered is null)
                return;

            Blocks.FirstOrDefault(block => block.Choice == choice)?.Sync(offered);
        }

        /// <summary>Forgets the questions, for when the order no longer has formats.</summary>
        public void Clear()
        {
            Problem = null;
            Blocks.Clear();
        }

        private async Task<List<OrderParameterRow>?> BuildRowsAsync(
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

            return BuildRows(choice, variant, rows);
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

            // One rebuild at a time. A rebuild updates the window, and the window answering
            // that update is not a new answer: reloading again from inside the first one is how
            // the page once rebuilt itself until the stack ran out.
            if (_reloading)
                return;

            _reloading = true;
            try
            {
                await ReloadAsync(choice, rows).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                Problem = "Параметры формата не обновились: " + ex.Message;
            }
            finally
            {
                _reloading = false;
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
