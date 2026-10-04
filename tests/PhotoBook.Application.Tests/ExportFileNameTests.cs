using PhotoBookRenamer.Application;

namespace PhotoBookRenamer.Application.Tests;

/// <summary>
/// The naming contract. The client's print site accepts KKK-FF.jpg and nothing else, so
/// these are not style assertions: a change here is a change in what the customer sends
/// to the printer.
/// </summary>
public class ExportFileNameTests
{
    private static ExportService Service() => TestProject.Service();

    [Theory]
    [InlineData(1, 0, "001-00.jpg")]
    [InlineData(1, 1, "001-01.jpg")]
    [InlineData(3, 5, "003-05.jpg")]
    [InlineData(10, 9, "010-09.jpg")]
    [InlineData(29, 19, "029-19.jpg")]
    [InlineData(100, 100, "100-100.jpg")]
    public void Writes_the_form_the_print_site_accepts(int bookIndex, int fileIndex, string expected)
    {
        Assert.Equal(expected, Service().GenerateFileName(bookIndex, fileIndex));
    }

    [Fact]
    public void Pads_the_book_index_to_three_digits_and_the_position_to_two()
    {
        var name = Service().GenerateFileName(7, 3);

        Assert.Equal("007-03", Path.GetFileNameWithoutExtension(name));
    }

    [Fact]
    public void Keeps_the_extension_lower_case()
    {
        Assert.EndsWith(".jpg", Service().GenerateFileName(1, 1), StringComparison.Ordinal);
    }

    [Fact]
    public void Uses_a_forward_slash_free_name_so_it_can_live_on_any_volume()
    {
        var name = Service().GenerateFileName(2, 4);

        Assert.DoesNotContain('/', name);
        Assert.DoesNotContain('\\', name);
        Assert.Equal(Path.GetFileName(name), name);
    }
}
