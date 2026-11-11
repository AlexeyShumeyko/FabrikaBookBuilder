namespace PhotoBook.Application.Sites.Books.Grouping
{
    /// <summary>How the books of a project are laid out over the site's projects.</summary>
    public enum GroupingMode
    {
        /// <summary>One project per format; books of the same format travel together.</summary>
        InOneProject,

        /// <summary>Every book becomes its own project on the site.</summary>
        EachBookSeparate,

        /// <summary>The customer put the books into groups by hand.</summary>
        Manual
    }
}
