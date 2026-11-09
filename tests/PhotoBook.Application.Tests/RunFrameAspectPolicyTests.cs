using PhotoBook.Presentation;

namespace PhotoBook.Application.Tests;

/// <summary>
/// Which format the cards of a combined run follow. One accidental photograph must not
/// reshape ten matching spreads, and a tie must not average two formats into a third.
/// </summary>
public class RunFrameAspectPolicyTests
{
    [Fact]
    public void One_measured_spread_decides_the_run()
    {
        Assert.Equal(0.682, RunFrameAspectPolicy.Decide(new[] { 0.682 }, null), 3);
    }

    [Fact]
    public void A_wide_photograph_does_not_reshape_a_run_of_narrow_spreads()
    {
        var narrow = Enumerable.Repeat(0.682, 10);
        var aspects = narrow.Append(1.406).ToList();

        Assert.Equal(0.682, RunFrameAspectPolicy.Decide(aspects, 0.682), 3);
    }

    [Fact]
    public void Similar_sizes_of_one_paper_count_as_one_format()
    {
        // 2457x3602 and 2460x3600 are the same portrait paper, not two formats.
        var aspects = new[] { 0.682, 0.683, 0.684, 1.406 };

        Assert.Equal(0.683, RunFrameAspectPolicy.Decide(aspects, null), 3);
    }

    [Fact]
    public void A_tie_keeps_the_shape_already_on_screen()
    {
        // One narrow spread on screen, one wide photograph just dropped in. Changing the
        // whole project on a 1:1 tie is exactly what the majority rule must not do.
        Assert.Equal(0.682, RunFrameAspectPolicy.Decide(new[] { 0.682, 1.406 }, 0.682), 3);
    }

    [Fact]
    public void A_tie_without_a_current_shape_uses_the_first_spread()
    {
        Assert.Equal(1.406, RunFrameAspectPolicy.Decide(new[] { 1.406, 0.682 }, null), 3);
        Assert.Equal(0.682, RunFrameAspectPolicy.Decide(new[] { 0.682, 1.406 }, null), 3);
    }

    [Fact]
    public void No_spreads_fall_back_to_the_design_shape()
    {
        Assert.Equal(1.6, RunFrameAspectPolicy.Decide(Array.Empty<double>(), null), 3);
    }
}
