using System.Collections.Generic;

namespace PhotoBook.Application.Sites.Upload
{
    /// <summary>One group the upload will send as a project on the site.</summary>
    /// <param name="Number">The group's place in the order, starting at one.</param>
    /// <param name="Group">The books laid out into this project.</param>
    /// <param name="Request">The product, its format and the uploader's integration.</param>
    /// <param name="Pages">The files of every book of the group, in page order.</param>
    /// <param name="Price">The project's price for all its books, as the site's calculator named it.</param>
    /// <param name="Currency">The currency the site named with that price, empty when it named none.</param>
    public sealed record UploadGroup(
        int Number,
        Books.OrderGroup Group,
        Projects.CreateProjectRequest Request,
        IReadOnlyList<Books.PageFile> Pages,
        decimal Price = 0,
        string Currency = "");
}
