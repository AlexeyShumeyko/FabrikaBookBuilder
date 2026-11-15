using System;
using PhotoBook.Application.Sites.Catalog;

namespace PhotoBook.Presentation.ViewModels
{
    /// <summary>One format the customer can pick for a book.</summary>
    /// <param name="Product">The product it belongs to.</param>
    /// <param name="Format">The product's format.</param>
    public sealed record FormatChoice(CatalogProduct Product, CatalogFormat Format)
    {
        /// <summary>The line the picker shows: «Лайфлат 21x30».</summary>
        public string DisplayText => Product.Name + " " + Format.Name;

        /// <summary>Whether this is the pair of ids the book carries.</summary>
        public bool Matches(PhotoBook.Core.Book book) =>
            book.SiteProductId == Product.Id && book.SiteVariantId == Format.Id;
    }

    /// <summary>One choice inside a parameter, as the picker shows it.</summary>
    public sealed record ParameterElement(OptionElement Element)
    {
        /// <summary>The site's own name for the choice.</summary>
        public string Title => Element.Title;
    }
}
