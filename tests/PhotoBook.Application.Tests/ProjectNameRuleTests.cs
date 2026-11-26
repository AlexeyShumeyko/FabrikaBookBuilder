using System;
using System.Globalization;
using System.Linq;
using PhotoBook.Core;

namespace PhotoBook.Application.Tests;

/// <summary>
/// A project is named in twenty-five characters, spaces counted, and a project nobody named
/// is called by the moment it started.
/// </summary>
public class ProjectNameRuleTests
{
    [Fact]
    public void Spaces_count_towards_the_limit()
    {
        var name = string.Concat(Enumerable.Repeat("a ", 15));

        Assert.Equal(25, ProjectNameRule.Limit(name).Length);
    }

    [Fact]
    public void A_name_of_exactly_twenty_five_characters_is_kept_whole()
    {
        var name = new string('Ж', ProjectNameRule.MaxLength);

        Assert.Equal(name, ProjectNameRule.Limit(name));
    }

    [Fact]
    public void A_shorter_name_is_kept_whole()
    {
        Assert.Equal("Лето 2026", ProjectNameRule.Limit("Лето 2026"));
    }

    [Fact]
    public void Nothing_is_a_name_of_no_characters()
    {
        Assert.Equal(string.Empty, ProjectNameRule.Limit(null));
        Assert.Equal(string.Empty, ProjectNameRule.Limit(string.Empty));
    }

    [Fact]
    public void A_name_of_emoji_is_cut_between_characters_and_not_inside_one()
    {
        var name = string.Concat(Enumerable.Repeat("😀", 30));

        var limited = ProjectNameRule.Limit(name);

        Assert.Equal(ProjectNameRule.MaxLength, new StringInfo(limited).LengthInTextElements);
        Assert.False(char.IsHighSurrogate(limited[^1]));
    }

    [Fact]
    public void An_unnamed_project_is_called_by_its_date_and_time()
    {
        var started = new DateTime(2026, 11, 26, 12, 27, 0);

        var name = ProjectNameRule.Unnamed(started);

        Assert.Equal("2026-11-26 12:27", name);
        Assert.True(name.Length <= ProjectNameRule.MaxLength);
    }
}
