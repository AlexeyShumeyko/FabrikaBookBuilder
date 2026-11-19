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
                files.Select(path => new PageFile(path, 1024)).ToList(),
                CoverPartId: 93,
                CoverFile: new PageFile("cover.jpg", 1024));

        private static GroupRequest Group(params Book[] books) =>
            new(
                new OrderGroup(SiteRegion.Com, books),
                new CreateProjectRequest(4494, 9255, 37556));

        private static GroupRequest PricedGroup(decimal price, params Book[] books) =>
            new(
                new OrderGroup(SiteRegion.Com, books),
                new CreateProjectRequest(4494, 9255, 37556),
                price);

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
        public async Task NamesTheCreatedProjectWithTheProgramPrefix()
        {
            var projects = new StubProjects(1001);
            var named = new GroupRequest(
                new OrderGroup(SiteRegion.Com, new[] { Book(9255) }),
                new CreateProjectRequest(4494, 9255, 37556, "Лето 2026"),
                2048m);

            await Service(projects, new MemoryCreated()).CreateAsync(Session, new[] { named });

            Assert.Equal(new[] { "FFStudio: Лето 2026" }, projects.Titles);
        }

        [Fact]
        public void PrefixesEveryNameAndFallsBackWhenThereIsNone()
        {
            Assert.Equal("FFStudio: Лето", SiteProjectTitle.For("  Лето  "));
            Assert.Equal("FFStudio: Проект", SiteProjectTitle.For("   "));
            Assert.Equal("FFStudio: Проект", SiteProjectTitle.For(null));
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

        /// <summary>
        /// A record that no longer answers must not stop the upload: the site may hold the project
        /// under another account, or the customer deleted it by hand. The books are sent as a new
        /// project instead of the run ending on a sentence nobody can act on.
        /// </summary>
        [Fact]
        public async Task CreatesANewProjectWhenTheRememberedOneCannotBeRead()
        {
            var projects = new StubProjects(1002) { ReadFails = true };
            var created = new MemoryCreated();
            var group = Group(Book(9255));
            created.Remember(GroupKey.For(SiteRegion.Com, group.Group.Books), 1001);

            var plan = await new ProjectCreationService(projects, created)
                .CreateAsync(Session, new[] { group });

            var sent = Assert.Single(plan.Groups);
            Assert.Equal(GroupCreationState.Created, sent.State);
            Assert.Equal(1002, sent.ProjectId);
            Assert.Single(projects.Asked);
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

        [Fact]
        public async Task FillsOneProjectWithEveryBookOfTheGroup()
        {
            var projects = new StubProjects(1001);
            var books = new[] { Book(9255), Book(9255), Book(9255) };

            var plan = await new ProjectCreationService(projects, new MemoryCreated())
                .CreateAsync(Session, new[] { PricedGroup(3000m, books) });

            var created = Assert.Single(plan.Groups);
            Assert.Equal(new long[] { 5000, 7000, 7001 }, created.BookIds);
            Assert.Equal(new[] { (1001L, 2), (1001L, 3) }, projects.Added.ToArray());
            Assert.Equal(3, Assert.Single(projects.SetUps).Parameters.Books);
            Assert.Equal(3000m, Assert.Single(projects.SetUps).Parameters.Price);
        }

        [Fact]
        public async Task StopsAGroupWhenABookCannotBeAdded()
        {
            var projects = new StubProjects(1001) { AddBookFails = true };

            var plan = await new ProjectCreationService(projects, new MemoryCreated())
                .CreateAsync(Session, new[] { PricedGroup(3000m, Book(9255), Book(9255)) });

            var group = Assert.Single(plan.Groups);
            Assert.True(group.HasProject);
            Assert.Contains("добавить книгу", group.Error, StringComparison.Ordinal);
            Assert.Empty(projects.SetUps);
        }

        /// <summary>
        /// A project an earlier run made: its books are read back so the pages land in them
        /// instead of being stacked on the first one.
        /// </summary>
        [Fact]
        public async Task ReadsTheBooksOfAProjectFoundInTheRecord()
        {
            var projects = new StubProjects(1001) { ItemsInExistingProject = 2 };
            var created = new MemoryCreated();
            var group = Group(Book(9255), Book(9255));
            created.Remember(GroupKey.For(SiteRegion.Com, group.Group.Books), 1001);

            var plan = await new ProjectCreationService(projects, created)
                .CreateAsync(Session, new[] { group });

            var sent = Assert.Single(plan.Groups);
            Assert.Equal(new long[] { 9001, 9002 }, sent.BookIds);
            Assert.Empty(projects.Added);
            Assert.Null(sent.Error);
        }

        [Fact]
        public async Task NamesTheParametersAndThePriceRightAfterCreatingTheProject()
        {
            var projects = new StubProjects(1001);
            var book = Book(9255, @"C:.jpg");

            var plan = await new ProjectCreationService(projects, new MemoryCreated())
                .CreateAsync(Session, new[] { PricedGroup(1024.50m, book) });

            Assert.True(plan.IsComplete);
            var named = Assert.Single(projects.SetUps);
            Assert.Equal(1001, named.ProjectId);
            Assert.Equal(9255, named.Parameters.VariantId);
            Assert.Equal(1, named.Parameters.Books);
            Assert.Equal(1024.50m, named.Parameters.Price);
            Assert.Equal(10, named.Parameters.PartQuantities[94]);
        }

        [Fact]
        public async Task SaysWhenTheSiteRefusedTheProjectsParameters()
        {
            var projects = new StubProjects(1001) { SetUpFails = true };

            var plan = await new ProjectCreationService(projects, new MemoryCreated())
                .CreateAsync(Session, new[] { PricedGroup(1024.50m, Book(9255)) });

            var group = Assert.Single(plan.Groups);
            Assert.True(group.HasProject);
            Assert.Contains("параметры", group.Error, StringComparison.Ordinal);
        }

        [Fact]
        public async Task ACreatedProjectIsWrittenDownWithItsNumberAndBooks()
        {
            var journal = new RecordingJournal();

            await new ProjectCreationService(new StubProjects(1001), new MemoryCreated(), journal)
                .CreateAsync(Session, new[] { Group(Book(9255), Book(9255)) });

            Assert.Contains(
                journal.Entries,
                entry => entry.Level == JournalLevel.Information &&
                         entry.Message.Contains("Проект 1001 создан", StringComparison.Ordinal) &&
                         entry.Message.Contains("книг 2", StringComparison.Ordinal));
        }

        [Fact]
        public async Task AFailedGroupIsWrittenDownWithWhatTheSiteSaid()
        {
            var journal = new RecordingJournal();

            await new ProjectCreationService(new StubProjects((long?)null), new MemoryCreated(), journal)
                .CreateAsync(Session, new[] { Group(Book(9255)) });

            var entry = Assert.Single(journal.Entries);
            Assert.Equal(JournalLevel.Warning, entry.Level);
            Assert.Equal("Создание проекта", entry.Source);
            Assert.Contains("книг 1", entry.Message, StringComparison.Ordinal);
            Assert.Contains("Сайт не создал проект", entry.Message, StringComparison.Ordinal);
        }

        private sealed class StubProjects : ISiteProjectService
        {
            private readonly Queue<long?> _answers;

            internal StubProjects(params long?[] answers) => _answers = new Queue<long?>(answers);

            internal bool SetUpFails { get; init; }

            private long _nextItemId = 7000;

            internal List<CreateProjectRequest> Asked { get; } = new();

            public Task<Result<long>> AddBookAsync(
                SiteSession session,
                long projectId,
                int position,
                CancellationToken cancellationToken = default)
            {
                Added.Add((projectId, position));

                if (AddBookFails)
                    return Task.FromResult(Result<long>.Fail("Сайт не добавил книгу."));

                var id = _nextItemId++;
                return Task.FromResult(Result<long>.Ok(id));
            }

            internal List<(long ProjectId, int Position)> Added { get; } = new();

            internal bool AddBookFails { get; init; }

            internal long ItemsInExistingProject { get; init; } = 1;

            internal bool ReadFails { get; init; }

            public Task<Result> SetUpAsync(
                SiteSession session,
                long projectId,
                long productId,
                ProjectParameters parameters,
                string title,
                CancellationToken cancellationToken = default)
            {
                SetUps.Add((projectId, parameters));
                Titles.Add(title);

                return Task.FromResult(SetUpFails
                    ? Result.Fail("Сайт не принял параметры.")
                    : Result.Ok());
            }

            internal List<(long ProjectId, ProjectParameters Parameters)> SetUps { get; } = new();

            internal List<string> Titles { get; } = new();

            public Task<Result<SiteProjectSnapshot>> ReadAsync(
                SiteSession session,
                long projectId,
                CancellationToken cancellationToken = default)
            {
                if (ReadFails)
                {
                    return Task.FromResult(Result<SiteProjectSnapshot>.Fail(
                        "Сайт не отдал проект."));
                }

                var items = Enumerable.Range(1, (int)ItemsInExistingProject)
                    .Select(index => new SiteProjectItem(9000 + index, null, 1, Array.Empty<SiteProjectPage>()))
                    .ToList();

                return Task.FromResult(Result<SiteProjectSnapshot>.Ok(
                    new SiteProjectSnapshot(projectId, 4494, string.Empty, "/layflat", true, items)));
            }

            public Task<Result<SiteProject>> CreateAsync(
                SiteSession session,
                CreateProjectRequest request,
                CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Asked.Add(request);

                var answer = _answers.Count > 0 ? _answers.Dequeue() : null;

                return Task.FromResult(answer is { } id
                    ? Result<SiteProject>.Ok(new SiteProject(id, request.ProductId, string.Empty, 5000))
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

            public void Forget(string key) => _stored.Remove(key);
        }
    }
}
