using System;

namespace PhotoBook.Application.Sites.Projects
{
    /// <summary>
    /// The name a project is given on the site, so that the account can tell this program's
    /// projects apart at a glance.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The site creates a project with an empty title and ignores the one sent with the creation
    /// request; the name is taken from the parameters that follow, which the program sends before
    /// any page. The title therefore costs no extra request.
    /// </para>
    /// <para>
    /// The prefix is added here rather than typed by the customer: it belongs to every project the
    /// program makes, and no screen offers to change or remove it. What the customer named the
    /// project stays readable after the prefix.
    /// </para>
    /// </remarks>
    public static class SiteProjectTitle
    {
        /// <summary>What every project of this program is named with.</summary>
        public const string Prefix = "FFStudio";

        /// <summary>The title for a project: the prefix and the name the customer gave it.</summary>
        public static string For(string? name) =>
            Prefix + ": " + (string.IsNullOrWhiteSpace(name) ? "Проект" : name.Trim());
    }
}
