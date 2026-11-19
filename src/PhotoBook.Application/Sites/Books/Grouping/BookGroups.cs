using System;
using System.Collections.Generic;
using System.Linq;

namespace PhotoBook.Application.Sites.Books.Grouping
{
    /// <summary>The groups a project's books were laid out into, and what is wrong with them.</summary>
    /// <remarks>
    /// Groups and problems together rather than either alone: a manual layout that mixes two
    /// formats is still a layout the customer made, and the window has to show it as it is and
    /// say what is wrong with it, instead of quietly rearranging books behind their back.
    /// </remarks>
    public sealed record BookGroups(IReadOnlyList<OrderGroup> Groups, IReadOnlyList<string> Problems)
    {
        /// <summary>Whether the layout may be sent: at least one group and nothing to fix.</summary>
        public bool IsUsable => Problems.Count == 0 && Groups.Count > 0;

        /// <summary>Books in the layout, across its groups.</summary>
        public int BookCount => Groups.Sum(group => group.BookCount);
    }
}
