using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PhotoBook.Application.Sites;
using PhotoBook.Application.Sites.Books;
using PhotoBook.Application.Sites.Books.Pricing;
using PhotoBook.Application.Sites.Catalog;
using PhotoBook.Application.Sites.Projects;

namespace PhotoBook.Application.Tests.Sites.Projects
{
    /// <summary>
    /// The roadmap's criterion lives here: a connection that broke after the site made a
    /// project must not make a second one on the next attempt.
    /// </summary>
    public class ProjectCreationServiceTests
    {
        private static readonly SiteSession Session =
            new(SiteRegion.Com, "token-value", new DateTimeOffset(2026, 11, 10, 12, 0, 0, TimeSpan.Zero));

        private static Book Book(long variantId, params string[] files) =>
            new(
                new SiteProduct(4494, "layflat", "Лайфлат", "photo-book", new List<ProductVariant>()),
                new ProductVariant(variantId, "21x30", new List<ProductOption>(), new List<ProductPart>()),
                PagePartId: 94,
                new List<SelectedPart> { new(94, new PartSelection(new Dictionary<long, long>(), 10)) },
                files.Select(path => new PageFile(path, 1024)).ToList());

        private static GroupRequest Group(params Book[] books) =>
            new(
                new OrderGroup(SiteRegion.Com, books),
                new CreateProjectRequest(4494, 9255, 37556));

        private static ProjectCreationService Service(StubProjects projects, MemoryCreated created) =>
            new(projects, created);

        [Fact]
        public async Task CreatesEachGroupAndRemembersIt()
        {
            var projects = new StubProjects(1001, 1002);
            var created = new MemoryCreated();

            var plan = await Service(projects, created)
                .CreateAsync(Session, new[] { Group(Book(9255)), Group(Book(9255, @"C:\1.jpg")) });

            Assert.True(plan.IsComplete);
            Assert.Equal(new long[] { 1001, 1002 }, plan.Groups.Select(group => group.ProjectId));
            Assert.Equal(2, projects.Asked.Count);
            Assert.Equal(2, created.Keys.Count());
        }

        [Fact]
        public async Task DoesNotCreateTheSameGroupTwice()
        {
            var projects = new StubProjects(1001, 1002);
            var created = new MemoryCreated();
            var groups = new[] { Group(Book(9255)), Group(Book(9255, @"C:\1.jpg")) };

            await Service(projects, created).CreateAsync(Session, groups);
            var second = await Service(projects, created).CreateAsync(Session, groups);

            Assert.Equal(2, projects.Asked.Count);
            Assert.All(second.Groups, group => Assert.Equal(GroupCreationState.AlreadyCreated, group.State));
            Assert.Equal(new long[] { 1001, 1002 }, second.Groups.Select(group => group.ProjectId));
            Assert.True(second.IsComplete);
        }

        [Fact]
        public async Task ReportsPartialSuccessAndKeepsGoing()
        {
            var projects = new StubProjects(1001, null, 1003);
            var created = new MemoryCreated();

            var plan = await Service(projects, created).CreateAsync(
                Session,
                new[] { Group(Book(9255)), Group(Book(9255, @"C:\1.jpg")), Group(Book(9255, @"C:\2.jpg")) });

            Assert.False(plan.IsComplete);
            var failure = Assert.Single(plan.Failures);
            Assert.Equal(2, failure.Number);
            Assert.Equal(GroupCreationState.Failed, failure.State);
            Assert.Equal(0, failure.ProjectId);
            Assert.Equal(new[] { "Группа 2: Сайт не создал проект." }, plan.Problems);
            Assert.Equal(new long[] { 1001, 0, 1003 }, plan.Groups.Select(group => group.ProjectId));
            Assert.Equal(2, created.Keys.Count());
        }

        [Fact]
        public async Task WarnsWhenTheCreatedProjectCouldNotBeWrittenDown()
        {
            var projects = new StubProjects(1001);
            var created = new MemoryCreated { WritesFail = true };

            var plan = await Service(projects, created).CreateAsync(Session, new[] { Group(Book(9255)) });

            var group = Assert.Single(plan.Groups);
            Assert.Equal(GroupCreationState.Created, group.State);
            Assert.True(group.HasProject);
            Assert.Contains("может создаться снова", group.Error);
        }

        [Fact]
        public async Task StopsBetweenGroupsWhenCancelled()
        {
            var projects = new StubProjects(1001, 1002);
            var created = new MemoryCreated();
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                Service(projects, created).CreateAsync(Session, new[] { Group(Book(9255)) }, cancellation.Token));

            Assert.Empty(projects.Asked);
        }

        private sealed class StubProjects : ISiteProjectService
        {
            private readonly Queue<long?> _answers;

            internal StubProjects(params long?[] answers) => _answers = new Queue<long?>(answers);

            internal List<CreateProjectRequest> Asked { get; } = new();

            public Task<Result<SiteProjectSnapshot>> ReadAsync(
                SiteSession session,
                long projectId,
                CancellationToken cancellationToken = default) =>
                throw new NotSupportedException("Creation never reads a project back here.");

            public Task<Result<SiteProject>> CreateAsync(
                SiteSession session,
                CreateProjectRequest request,
                CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Asked.Add(request);

                var answer = _answers.Count > 0 ? _answers.Dequeue() : null;

                return Task.FromResult(answer is { } id
                    ? Result<SiteProject>.Ok(new SiteProject(id, request.ProductId, string.Empty))
                    : Result<SiteProject>.Fail("Сайт не создал проект."));
            }
        }

        private sealed class MemoryCreated : ICreatedProjectStore
        {
            private readonly Dictionary<string, long> _stored = new(StringComparer.Ordinal);

            internal bool WritesFail { get; init; }

            internal IEnumerable<string> Keys => _stored.Keys;

            public long? Find(string key) => _stored.TryGetValue(key, out var id) ? id : null;

            public Result Remember(string key, long projectId)
            {
                if (WritesFail)
                    return Result.Fail("Запись не удалась.");

                _stored[key] = projectId;
                return Result.Ok();
            }
        }
    }
}
