using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PhotoBook.Application.Sites;
using PhotoBook.Application.Sites.Completion;
using PhotoBook.Application.Sites.Projects;

namespace PhotoBook.Application.Tests.Sites.Completion
{
    /// <summary>
    /// The roadmap's criterion: a difference between what was sent and what the site holds is
    /// shown, not called success. These tests pin every difference the check can name.
    /// </summary>
    public class ProjectCheckServiceTests
    {
        private static readonly SiteSession Session =
            new(SiteRegion.Com, "token-value", new DateTimeOffset(2026, 11, 10, 12, 0, 0, TimeSpan.Zero));

        [Fact]
        public async Task AProjectThatHoldsEveryPageMatchesAndCarriesItsLink()
        {
            var check = await Check(Project(pages: 10, books: 2), new ProjectExpectation(new[] { 10, 10 }));

            Assert.True(check.Matches);
            Assert.Empty(check.Differences);
            Assert.True(check.HasUrl);
            Assert.Equal("https://fabrika-fotoknigi.com/layflat?projectId=527733", check.Url);
            Assert.Equal(20, check.Project.PageCount);
        }

        [Fact]
        public async Task AProjectTheAccountCannotSeeIsADifference()
        {
            var check = await Check(Project(pages: 4, visible: false), new ProjectExpectation(new[] { 4 }));

            Assert.False(check.Matches);
            Assert.Contains(check.Differences, line => line.Contains("списке проектов аккаунта"));
        }

        [Fact]
        public async Task ABookWithAnotherNumberOfPagesIsADifference()
        {
            var check = await Check(Project(pages: 9), new ProjectExpectation(new[] { 10 }));

            Assert.False(check.Matches);
            Assert.Contains(
                check.Differences,
                line => line.Contains("Книга 1") && line.Contains("9") && line.Contains("10"));
        }

        [Fact]
        public async Task AnotherNumberOfBooksIsADifference()
        {
            var check = await Check(Project(pages: 4, books: 1), new ProjectExpectation(new[] { 4, 4 }));

            Assert.False(check.Matches);
            Assert.Contains(check.Differences, line => line.Contains("1 книг") && line.Contains("2"));
        }

        [Fact]
        public async Task PagesNumberedOutOfOrderAreADifference()
        {
            var project = new SiteProjectSnapshot(
                527733,
                4494,
                string.Empty,
                "/layflat",
                true,
                new[] { new SiteProjectItem(1, null, 1, new[] { Page(0), Page(2), Page(1) }) });

            var check = await Check(project, new ProjectExpectation(new[] { 3 }));

            Assert.False(check.Matches);
            Assert.Contains(check.Differences, line => line.Contains("не подряд"));
        }

        [Fact]
        public async Task WhatTheSiteComplainsAboutOnAPageIsShown()
        {
            var project = new SiteProjectSnapshot(
                527733,
                4494,
                string.Empty,
                "/layflat",
                true,
                new[]
                {
                    new SiteProjectItem(
                        1,
                        null,
                        1,
                        new[] { Page(0), Page(1, "Изображение меньше рекомендованного"), Page(2) })
                });

            var check = await Check(project, new ProjectExpectation(new[] { 3 }));

            Assert.False(check.Matches);
            Assert.Contains(
                check.Differences,
                line => line.Contains("страница 2") && line.Contains("Изображение меньше"));
        }

        [Fact]
        public async Task AProjectWithoutAProductPageHasNoLinkRatherThanAGuess()
        {
            var project = new SiteProjectSnapshot(527733, 4494, string.Empty, string.Empty, true, Array.Empty<SiteProjectItem>());

            var check = await Check(project, new ProjectExpectation(Array.Empty<int>()));

            Assert.False(check.HasUrl);
            Assert.Equal(string.Empty, check.Url);
        }

        [Fact]
        public async Task AProjectTheSiteWillNotReadIsReportedWithItsReason()
        {
            var service = new StubProjects
            {
                Failure = "Нужно войти заново: сеанс истёк."
            };

            var result = await new ProjectCheckService(service)
                .CheckAsync(Session, 527733, new ProjectExpectation(new[] { 4 }));

            Assert.False(result.IsSuccess);
            Assert.Equal("Нужно войти заново: сеанс истёк.", result.Error);
        }

        private static async Task<ProjectCheck> Check(SiteProjectSnapshot project, ProjectExpectation expected)
        {
            var result = await new ProjectCheckService(new StubProjects { Project = project })
                .CheckAsync(Session, project.Id, expected);

            Assert.True(result.IsSuccess);
            return result.Value!;
        }

        private static SiteProjectSnapshot Project(int pages, int books = 1, bool visible = true)
        {
            var items = Enumerable.Range(0, books)
                .Select(book => new SiteProjectItem(
                    book + 1,
                    null,
                    1,
                    Enumerable.Range(0, pages).Select(position => Page(position)).ToList()))
                .ToList();

            return new SiteProjectSnapshot(527733, 4494, string.Empty, "/layflat", visible, items);
        }

        private static SiteProjectPage Page(int position, params string[] alerts) =>
            new(position, alerts);

        private sealed class StubProjects : ISiteProjectService
        {
            internal SiteProjectSnapshot? Project { get; init; }

            internal string? Failure { get; init; }

            public Task<Result<SiteProject>> CreateAsync(
                SiteSession session,
                CreateProjectRequest request,
                CancellationToken cancellationToken = default) =>
                throw new NotSupportedException("The check never creates projects.");

            public Task<Result<SiteProjectSnapshot>> ReadAsync(
                SiteSession session,
                long projectId,
                CancellationToken cancellationToken = default) =>
                Task.FromResult(Project is null
                    ? Result<SiteProjectSnapshot>.Fail(Failure ?? "Сайт не отдал проект.")
                    : Result<SiteProjectSnapshot>.Ok(Project));
        }
    }
}
