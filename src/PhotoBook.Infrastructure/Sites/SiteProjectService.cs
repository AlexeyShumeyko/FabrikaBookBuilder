using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using PhotoBook.Application;
using PhotoBook.Application.Sites;
using PhotoBook.Application.Sites.Projects;

namespace PhotoBook.Infrastructure.Sites
{
    /// <summary>Creates a project through the endpoint the site's own uploader calls.</summary>
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
    /// </remarks>
    internal sealed class SiteProjectService : ISiteProjectService
    {
        private const string CreatePath = "creators/projects/create";
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
