using PhotoBookRenamer.Domain;

namespace PhotoBook.Application.Tests;

/// <summary>
/// The slot layout of a book: which photograph sits where on the card, and what name it
/// will carry after the export. The card renders from this list and the uploader writes
/// those names, so a change here is visible on screen and in the customer's folder.
/// </summary>
public class BookSlotLayoutTests
{
    private static Book ThreeSpreads(int bookIndex = 7)
    {
        var book = new Book
        {
            BookIndex = bookIndex,
            Name = $"Book {bookIndex}",
            Cover = TestProject.Page("/p/cover.jpg", 0, isCover: true)
        };
        book.Pages.Add(TestProject.Page("/p/a.jpg", 1));
        book.Pages.Add(TestProject.Page("/p/b.jpg", 2));
        book.Pages.Add(TestProject.Page("/p/c.jpg", 3));

        book.UpdatePageSlots();
        return book;
    }

    [Fact]
    public void Slot_zero_is_the_cover_and_the_spreads_follow_in_order()
    {
        var book = ThreeSpreads();

        Assert.Equal(new[] { 0, 1, 2, 3 }, book.AllSlots);
        Assert.Equal(
            new[] { "/p/cover.jpg", "/p/a.jpg", "/p/b.jpg", "/p/c.jpg" },
            book.AllSlotsPages.Select(p => p.SourcePath));
    }

    [Fact]
    public void Spreads_are_laid_out_in_index_order_not_in_the_order_they_were_added()
    {
        var book = new Book
        {
            BookIndex = 1,
            Cover = TestProject.Page("/p/cover.jpg", 0, isCover: true)
        };
        book.Pages.Add(TestProject.Page("/p/third.jpg", 3));
        book.Pages.Add(TestProject.Page("/p/first.jpg", 1));
        book.Pages.Add(TestProject.Page("/p/second.jpg", 2));

        book.UpdatePageSlots();

        Assert.Equal(
            new[] { "/p/cover.jpg", "/p/first.jpg", "/p/second.jpg", "/p/third.jpg" },
            book.AllSlotsPages.Select(p => p.SourcePath));
    }

    [Fact]
    public void Every_slot_carries_the_name_the_export_will_use()
    {
        var book = ThreeSpreads(bookIndex: 7);

        Assert.Equal("007-00.jpg", book.Cover!.ExportFileName);
        Assert.Equal(new[] { "007-01.jpg", "007-02.jpg", "007-03.jpg" },
            book.Pages.Select(p => p.ExportFileName));
    }

    [Fact]
    public void The_export_name_is_rebuilt_when_the_book_number_changes()
    {
        var book = ThreeSpreads(bookIndex: 1);
        Assert.Equal("001-01.jpg", book.Pages[0].ExportFileName);

        book.BookIndex = 12;
        book.UpdatePageSlots();

        Assert.Equal("012-00.jpg", book.Cover!.ExportFileName);
        Assert.Equal("012-01.jpg", book.Pages[0].ExportFileName);
    }

    [Fact]
    public void A_book_with_only_a_cover_has_exactly_one_slot()
    {
        var book = new Book
        {
            BookIndex = 1,
            Cover = TestProject.Page("/p/cover.jpg", 0, isCover: true)
        };

        book.UpdatePageSlots();

        Assert.Equal(new[] { 0 }, book.AllSlots);
        Assert.Single(book.AllSlotsPages);
        Assert.Equal("001-00.jpg", book.Cover!.ExportFileName);
    }

    [Fact]
    public void A_book_is_valid_only_when_nothing_is_empty()
    {
        var full = ThreeSpreads();
        Assert.True(full.IsValid);

        // The counters feed the "Готово N из M" label on the card, and they count the cover:
        // three spreads and a cover make four slots.
        Assert.Equal(4, full.FilledSlotCount);
        Assert.Equal(4, full.TotalSlotCount);
        Assert.True(full.IsFilled);

        full.Pages[1].SourcePath = null;
        Assert.False(full.IsValid);
    }

    [Fact]
    public void An_empty_slot_is_kept_in_the_layout_so_the_card_shows_the_gap()
    {
        var book = ThreeSpreads();
        book.Pages[1].SourcePath = null;
        book.UpdatePageSlots();

        Assert.Equal(4, book.AllSlotsPages.Count);
        Assert.True(book.AllSlotsPages[2].IsEmpty);
        Assert.Equal(3, book.FilledSlotCount);
        Assert.False(book.IsFilled);
    }
}