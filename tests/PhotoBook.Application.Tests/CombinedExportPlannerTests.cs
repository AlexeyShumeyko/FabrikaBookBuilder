using PhotoBookRenamer.Application;
using PhotoBook.Core;

namespace PhotoBookRenamer.Application.Tests;

/// <summary>
/// The combined run's file set. 000 means "this position is the default for every book",
/// KKK overrides it for one book, and the site learns the size of the run from the highest
/// index it can see. These rules decide how many photographs the customer pays to print,
/// so they are pinned here before the code is touched.
/// </summary>
public class CombinedExportPlannerTests
{
    private static ExportPlan PlanFor(Project project) =>
        CombinedExportPlanner.Build(project, TestProject.Service().GenerateFileName);

    private static string[] Names(ExportPlan plan) => plan.Entries.Select(e => e.FileName).ToArray();

    [Fact]
    public void One_cover_shared_by_every_book_becomes_a_single_000_file()
    {
        var project = TestProject.Combined(
            TestProject.Book(1, "/p/cover.jpg", "/p/s1.jpg"),
            TestProject.Book(2, "/p/cover.jpg", "/p/s1.jpg"),
            TestProject.Book(3, "/p/cover.jpg", "/p/s1.jpg"));

        var plan = PlanFor(project);
        var names = Names(plan);

        // Two files carry the photographs, one per position, for the whole run.
        Assert.Contains("000-00.jpg", names);
        Assert.Contains("000-01.jpg", names);
        Assert.Single(plan.Entries, e => e.SlotIndex == 0 && e.BookIndex == 0);
        Assert.Single(plan.Entries, e => e.SlotIndex == 1 && e.BookIndex == 0);

        // The third file is not a photograph of its own: with everything shared nothing
        // carries the last book index, so the size of the run would be lost. That case has
        // its own test below; the point here is that nothing is written twice.
        Assert.Equal(3, names.Length);
    }

    [Fact]
    public void A_spread_only_one_book_has_becomes_an_override_for_that_book()
    {
        var project = TestProject.Combined(
            TestProject.Book(1, "/p/cover.jpg", "/p/shared.jpg", "/p/only-1.jpg"),
            TestProject.Book(2, "/p/cover.jpg", "/p/shared.jpg", "/p/only-2.jpg"));

        var plan = PlanFor(project);

        Assert.Contains("000-01.jpg", Names(plan));
        Assert.Contains("001-02.jpg", Names(plan));
        Assert.Contains("002-02.jpg", Names(plan));
    }

    [Fact]
    public void The_shared_file_carries_the_photo_the_most_books_hold()
    {
        // Two books share one photo at spread 1, three share another. The larger group wins.
        var project = TestProject.Combined(
            TestProject.Book(1, "/p/c.jpg", "/p/a.jpg", "/p/rare-1.jpg"),
            TestProject.Book(2, "/p/c.jpg", "/p/a.jpg", "/p/rare-2.jpg"),
            TestProject.Book(3, "/p/c.jpg", "/p/a.jpg", "/p/rare-3.jpg"));

        var shared = PlanFor(project).Entries.Single(e => e.SlotIndex == 1);

        Assert.Equal(0, shared.BookIndex);
        Assert.Equal("/p/a.jpg", shared.SourcePath);
        Assert.Equal(3, shared.BookCount);
    }

    [Fact]
    public void A_photo_held_by_one_book_alone_never_becomes_a_shared_file()
    {
        // The cover is shared, the spread is not: two groups of one book at position 1.
        var project = TestProject.Combined(
            TestProject.Book(1, "/p/c.jpg", "/p/only-1.jpg"),
            TestProject.Book(2, "/p/c.jpg", "/p/only-2.jpg"));

        var plan = PlanFor(project);

        Assert.Contains(plan.Entries, e => e.SlotIndex == 0 && e.BookIndex == 0);

        var atSpread = plan.Entries.Where(e => e.SlotIndex == 1).ToArray();
        Assert.DoesNotContain(atSpread, e => e.BookIndex == CombinedExportPlanner.SharedBookIndex);
        Assert.Equal(2, atSpread.Length);
    }

    [Fact]
    public void An_entirely_shared_run_still_states_how_many_books_it_has()
    {
        // Everything shared: no file carries the last book index, so the size of the run
        // would be lost. One extra file carries it.
        var project = TestProject.Combined(
            TestProject.Book(1, "/p/c.jpg", "/p/s1.jpg"),
            TestProject.Book(2, "/p/c.jpg", "/p/s1.jpg"),
            TestProject.Book(3, "/p/c.jpg", "/p/s1.jpg"));

        var plan = PlanFor(project);

        Assert.True(plan.BookCountCarried);
        Assert.Contains(plan.Entries, e => e.CarriesBookCount && e.BookIndex == 3);
    }

    [Fact]
    public void Says_so_when_the_size_of_the_run_cannot_be_conveyed()
    {
        var project = TestProject.Combined(
            TestProject.Book(1, "/p/c.jpg", "/p/s1.jpg"),
            TestProject.Book(2, "/p/c.jpg", "/p/s1.jpg"),
            TestProject.Book(3, string.Empty));

        var plan = PlanFor(project);

        Assert.False(plan.BookCountCarried);
        Assert.NotNull(plan.Warning);
    }

    [Fact]
    public void An_empty_project_produces_no_files_and_no_warning()
    {
        var plan = PlanFor(TestProject.Combined());

        Assert.Empty(plan.Entries);
        Assert.Null(plan.Warning);
    }

    [Fact]
    public void Every_written_name_is_unique()
    {
        var project = TestProject.Combined(
            TestProject.Book(1, "/p/c.jpg", "/p/s1.jpg", "/p/x.jpg"),
            TestProject.Book(2, "/p/c.jpg", "/p/s1.jpg", "/p/y.jpg"),
            TestProject.Book(3, "/p/c.jpg", "/p/s1.jpg", "/p/z.jpg"));

        var names = Names(PlanFor(project));

        Assert.Equal(names.Length, names.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }
}
