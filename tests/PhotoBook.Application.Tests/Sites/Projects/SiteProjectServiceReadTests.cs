using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using PhotoBook.Application.Sites;
using PhotoBook.Infrastructure.Sites;

namespace PhotoBook.Application.Tests.Sites
{
    /// <summary>
    /// Reading a project back is what makes the end of an upload a fact rather than a hope, so
    /// the two documents it comes from are parsed here as the site sends them.
    /// </summary>
    public class SiteProjectServiceReadTests
    {
        private static readonly SiteSession Session =
            new(SiteRegion.Com, "token-value", new DateTimeOffset(2026, 11, 10, 12, 0, 0, TimeSpan.Zero));

        private const string Edit = "{\"data\":{\"id\":527733,\"readonly\":false,\"product_id\":4494," +
            "\"creator\":\"photo-book-uploader\",\"title\":\"\",\"created_at\":1794396214,\"params\":[]," +
            "\"items\":[{\"id\":2834170,\"title\":null,\"quantity\":1,\"doublePages\":[" +
            "{\"id\":1,\"project_item_id\":2834170,\"position\":0,\"params\":{\"color_space\":\"rgb\"},\"alerts\":[]}," +
            "{\"id\":2,\"project_item_id\":2834170,\"position\":1,\"params\":{\"color_space\":\"rgb\"},\"alerts\":[]}" +
            "]}]}}";

        private static readonly string List =
            "{\"data\":[{\"id\":527733,\"product_id\":4494,\"product_url\":\"/layflat\",\"alerts\":[],\"quantity\":1}]}";

        [Fact]
        public async Task ReadsTheBooksThePagesAndTheAccountsViewOfThem()
        {
            var read = await Read(Edit, List);

            Assert.True(read.IsSuccess);
            var project = read.Value!;
            Assert.Equal(527733, project.Id);
            Assert.Equal(4494, project.ProductId);
            Assert.Equal(1, project.BookCount);
            Assert.Equal(2, project.PageCount);
            Assert.Equal(new[] { 0, 1 }, project.Items[0].Pages.Select(page => page.Position));
            Assert.True(project.IsVisibleInAccount);
            Assert.Equal("/layflat", project.ProductUrl);
        }

        [Fact]
        public async Task UnderstandsAStringPositionAndTheSitesOwnPageAlerts()
        {
            var edit = Edit
                .Replace("\"position\":1", "\"position\":\"1\"")
                .Replace("\"alerts\":[]}]}]}}", "\"alerts\":[\"Изображение меньше рекомендованного\"]}]}]}}");

            var read = await Read(edit, List);

            Assert.True(read.IsSuccess);
            var pages = read.Value!.Items[0].Pages;
            Assert.Equal(1, pages[1].Position);
            Assert.Contains(pages[1].Alerts, alert => alert.Contains("Изображение меньше"));
        }

        [Fact]
        public async Task AProjectMissingFromTheAccountsListIsNotVisible()
        {
            var read = await Read(Edit, "{\"data\":[]}");

            Assert.True(read.IsSuccess);
            Assert.False(read.Value!.IsVisibleInAccount);
            Assert.Equal(string.Empty, read.Value.ProductUrl);
        }

        [Fact]
        public async Task AProjectTheSiteWillNotReadCarriesTheSitesReason()
        {
            var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));

            var result = await new SiteProjectService(new HttpClient(handler), TimeSpan.Zero)
                .ReadAsync(Session, 527733);

            Assert.False(result.IsSuccess);
            Assert.Contains("войти заново", result.Error);
        }

        [Fact]
        public async Task AsksTheSiteTheSessionBelongsTo()
        {
            var handler = new StubHttpMessageHandler(request =>
                request.RequestUri!.ToString().Contains("projects/edit")
                    ? Json(Edit)
                    : Json(List));

            await new SiteProjectService(new HttpClient(handler), TimeSpan.Zero)
                .ReadAsync(Session, 527733);

            Assert.All(handler.Urls, url => Assert.Contains("/site/p4m3j/", url));
            Assert.Contains(handler.Urls, url => url.Contains("projects/edit?projectId=527733"));
        }

        private static async Task<Result<PhotoBook.Application.Sites.Projects.SiteProjectSnapshot>> Read(
            string edit,
            string list)
        {
            var handler = new StubHttpMessageHandler(request =>
                request.RequestUri!.ToString().Contains("projects/edit")
                    ? Json(edit)
                    : Json(list));

            return await new SiteProjectService(new HttpClient(handler), TimeSpan.Zero)
                .ReadAsync(Session, 527733);
        }

        private static HttpResponseMessage Json(string body) =>
            new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }
}
