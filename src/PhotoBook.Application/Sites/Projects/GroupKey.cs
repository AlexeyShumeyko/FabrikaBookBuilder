using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using PhotoBook.Application.Sites.Books;

namespace PhotoBook.Application.Sites.Projects
{
    /// <summary>The name a group is remembered by, so it is never created twice.</summary>
    /// <remarks>
    /// <para>
    /// The platform has no idempotency key: a second create request makes a second project.
    /// So a group is named here by what makes it that group - the site, the product, its
    /// format and the files that go into it, in order - and that name is what the local record
    /// keeps. Rebuilding the same project twice yields the same name; adding a photograph
    /// yields another, which is what the customer would expect.
    /// </para>
    /// <para>
    /// The files are part of the name because a book whose pages changed is not the project
    /// that already exists on the site. Their order matters for the same reason: the same
    /// photographs in another order are another book.
    /// </para>
    /// </remarks>
    public static class GroupKey
    {
        /// <summary>The key of one group on one site.</summary>
        public static string For(SiteRegion region, IReadOnlyList<Book> books)
        {
            if (region is null)
                throw new ArgumentNullException(nameof(region));
            if (books is null)
                throw new ArgumentNullException(nameof(books));

            var text = new StringBuilder();
            text.Append(region.Code).Append('|');

            foreach (var book in books)
            {
                if (book is null)
                    continue;

                text.Append(book.Product.Id).Append('/').Append(book.Variant.Id).Append('|');

                foreach (var file in book.Files ?? Array.Empty<PageFile>())
                {
                    if (file is null)
                        continue;

                    text.Append(file.Path).Append(':').Append(file.LengthBytes).Append('|');
                }
            }

            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString()));
            return Convert.ToHexString(hash);
        }
    }
}
