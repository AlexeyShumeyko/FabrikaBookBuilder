using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using PhotoBook.Application;
using PhotoBook.Application.Sites;
using PhotoBook.Application.Sites.Projects;

namespace PhotoBook.Infrastructure.Sites
{
    /// <summary>Creates a project through the endpoint the site's own uploader calls, and reads it back.</summary>
    /// <remarks>
    /// <para>
    /// The body is the one measured from the browser: <c>creator</c> names the uploader,
    /// <c>productId</c> and <c>productComponentId</c> name the product and its format, and
    /// <c>integrationId</c> is the site's id for that uploader on that product. Without the
    /// integration the endpoint answers 404 with a model-not-found text, which is why it is
    /// carried through rather than guessed here.
    /// </para>
    /// <para>
    /// Nothing in this class decides whether a project is needed: the call creates one every
    /// time it is accepted. Not asking twice is <see cref="ProjectCreationService"/>'s job.
    /// </para>
    /// <para>
    /// Reading takes two requests because the two documents say different things: the editor
    /// document holds the books and their pages, and the account list is the only place that
    /// names the product's page and says whether the customer can see the project at all. Both
    /// are reads, so both may be repeated safely.
    /// </para>
    /// </remarks>
    internal sealed class SiteProjectService : ISiteProjectService
    {
        private const string CreatePath = "creators/projects/create";
        private const string EditPath = "creators/projects/edit";
        private const string ListPath = "creators/projects?with_update_price=1";
        private const string Creator = "photo-book-uploader";

        private readonly HttpClient _http;
        private readonly TimeSpan _retryDelay;

        public SiteProjectService(HttpClient http, TimeSpan? retryDelay = null)
        {
            _http = http ?? throw new ArgumentNullException(nameof(http));
            _retryDelay = retryDelay ?? TimeSpan.FromMilliseconds(500);
        }

        public async Task<Result<SiteProject>> CreateAsync(
            SiteSession session,
            CreateProjectRequest request,
            CancellationToken cancellationToken = default)
        {
            if (session is null)
                throw new ArgumentNullException(nameof(session));
            if (request is null)
                throw new ArgumentNullException(nameof(request));

            var client = new SiteApiClient(session.Region, _http, _retryDelay);

            var response = await client
                .PostJsonAsync(
                    CreatePath,
                    new
                    {
                        creator = Creator,
                        productId = request.ProductId,
                        productComponentId = request.ProductComponentId,
                        integrationId = request.IntegrationId
                    },
                    session.AccessToken,
                    cancellationToken)
                .ConfigureAwait(false);

            if (!response.IsSuccess || response.Document is null)
            {
                return Result<SiteProject>.Fail(
                    response.Error?.Message ?? "Сайт не создал проект.");
            }

            using var document = response.Document;

            if (!document.RootElement.TryGetProperty("data", out var data))
                return Result<SiteProject>.Fail("Сайт не назвал созданный проект.");

            var id = Number(data, "id");
            if (id <= 0)
                return Result<SiteProject>.Fail("Сайт не назвал номер созданного проекта.");

            var productId = Number(data, "product_id");
            var title = Text(data, "title") ?? string.Empty;

            return Result<SiteProject>.Ok(
                new SiteProject(id, productId > 0 ? productId : request.ProductId, title));
        }

        public async Task<Result<SiteProjectSnapshot>> ReadAsync(
            SiteSession session,
            long projectId,
            CancellationToken cancellationToken = default)
        {
            if (session is null)
                throw new ArgumentNullException(nameof(session));
            if (projectId <= 0)
                return Result<SiteProjectSnapshot>.Fail("У проекта нет номера.");

            var client = new SiteApiClient(session.Region, _http, _retryDelay);

            var edit = await client
                .GetAsync(
                    EditPath + "?projectId=" + projectId.ToString(CultureInfo.InvariantCulture),
                    session.AccessToken,
                    cancellationToken)
                .ConfigureAwait(false);

            if (!edit.IsSuccess || edit.Document is null)
            {
                return Result<SiteProjectSnapshot>.Fail(
                    edit.Error?.Message ?? "Сайт не отдал проект.");
            }

            long productId;
            string title;
            List<SiteProjectItem> items;

            using (var document = edit.Document)
            {
                if (!document.RootElement.TryGetProperty("data", out var data))
                    return Result<SiteProjectSnapshot>.Fail("Сайт не отдал проект.");

                productId = Number(data, "product_id");
                title = Text(data, "title") ?? string.Empty;
                items = Items(data);
            }

            var (visible, productUrl) = await InAccountAsync(client, session, projectId, cancellationToken)
                .ConfigureAwait(false);

            return Result<SiteProjectSnapshot>.Ok(
                new SiteProjectSnapshot(projectId, productId, title, productUrl, visible, items));
        }

        /// <summary>Whether the account's list shows the project, and the product's page there.</summary>
        private static async Task<(bool Visible, string ProductUrl)> InAccountAsync(
            SiteApiClient client,
            SiteSession session,
            long projectId,
            CancellationToken cancellationToken)
        {
            var list = await client
                .GetAsync(ListPath, session.AccessToken, cancellationToken)
                .ConfigureAwait(false);

            if (!list.IsSuccess || list.Document is null)
                return (false, string.Empty);

            using var document = list.Document;

            if (!document.RootElement.TryGetProperty("data", out var data) ||
                data.ValueKind != JsonValueKind.Array)
            {
                return (false, string.Empty);
            }

            foreach (var project in data.EnumerateArray())
            {
                if (Number(project, "id") == projectId)
                    return (true, Text(project, "product_url") ?? string.Empty);
            }

            return (false, string.Empty);
        }

        /// <summary>The books of the editor document, each with the spreads the site holds.</summary>
        private static List<SiteProjectItem> Items(JsonElement data)
        {
            var items = new List<SiteProjectItem>();

            if (!data.TryGetProperty("items", out var entries) || entries.ValueKind != JsonValueKind.Array)
                return items;

            foreach (var entry in entries.EnumerateArray())
            {
                var pages = new List<SiteProjectPage>();

                if (entry.TryGetProperty("doublePages", out var spreads) &&
                    spreads.ValueKind == JsonValueKind.Array)
                {
                    foreach (var spread in spreads.EnumerateArray())
                    {
                        pages.Add(new SiteProjectPage(
                            (int)Number(spread, "position"),
                            Alerts(spread)));
                    }
                }

                items.Add(new SiteProjectItem(
                    Number(entry, "id"),
                    Text(entry, "title"),
                    (int)Number(entry, "quantity"),
                    pages));
            }

            return items;
        }

        /// <summary>What the site complains about on one page, in its own words.</summary>
        private static IReadOnlyList<string> Alerts(JsonElement spread)
        {
            var alerts = new List<string>();

            if (!spread.TryGetProperty("alerts", out var entries) || entries.ValueKind != JsonValueKind.Array)
                return alerts;

            foreach (var entry in entries.EnumerateArray())
            {
                var text = entry.ValueKind == JsonValueKind.String
                    ? entry.GetString()
                    : entry.GetRawText();

                if (!string.IsNullOrWhiteSpace(text))
                    alerts.Add(text);
            }

            return alerts;
        }

        /// <summary>A number the site may send as a number or as a string.</summary>
        private static long Number(JsonElement root, string property)
        {
            if (!root.TryGetProperty(property, out var element))
                return 0;

            return element.ValueKind switch
            {
                JsonValueKind.Number when element.TryGetInt64(out var number) => number,
                JsonValueKind.String when long.TryParse(element.GetString(), out var parsed) => parsed,
                _ => 0
            };
        }

        private static string? Text(JsonElement root, string property)
        {
            if (!root.TryGetProperty(property, out var element) || element.ValueKind != JsonValueKind.String)
                return null;

            var value = element.GetString()?.Trim();
            return string.IsNullOrEmpty(value) ? null : value;
        }
    }
}
