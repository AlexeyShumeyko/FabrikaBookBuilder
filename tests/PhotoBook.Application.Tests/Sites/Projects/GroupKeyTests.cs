using System.Collections.Generic;
using System.Linq;
using PhotoBook.Application.Sites;
using PhotoBook.Application.Sites.Books;
using PhotoBook.Application.Sites.Catalog;
using PhotoBook.Application.Sites.Projects;

namespace PhotoBook.Application.Tests.Sites.Projects
{
    /// <summary>
    /// The key is the only thing standing between a retry and a second project on the site, so
    /// what makes two groups the same group is pinned here.
    /// </summary>
    public class GroupKeyTests
    {
        private static Book Book(long variantId, params string[] files) =>
            new(
                new SiteProduct(4494, "layflat", "Лайфлат", "photo-book", new List<ProductVariant>()),
                new ProductVariant(variantId, "21x30", new List<ProductOption>(), new List<ProductPart>()),
                PagePartId: 94,
                new List<SelectedPart> { new(94, new PartSelection(new Dictionary<long, long>(), 10)) },
                files.Select(path => new PageFile(path, 1024)).ToList());

        [Fact]
        public void GivesTheSameKeyToTheSameGroup()
        {
            var first = GroupKey.For(SiteRegion.Com, new[] { Book(9255, @"C:\1.jpg", @"C:\2.jpg") });
            var second = GroupKey.For(SiteRegion.Com, new[] { Book(9255, @"C:\1.jpg", @"C:\2.jpg") });

            Assert.Equal(first, second);
        }

        [Fact]
        public void AnotherFolderIsAnotherGroup()
        {
            var first = GroupKey.For(SiteRegion.Com, new[] { Book(9255, @"C:\1.jpg") });
            var second = GroupKey.For(SiteRegion.Com, new[] { Book(9255, @"C:\other\1.jpg") });

            Assert.NotEqual(first, second);
        }

        [Fact]
        public void AnotherPageOrderIsAnotherGroup()
        {
            var first = GroupKey.For(SiteRegion.Com, new[] { Book(9255, @"C:\1.jpg", @"C:\2.jpg") });
            var second = GroupKey.For(SiteRegion.Com, new[] { Book(9255, @"C:\2.jpg", @"C:\1.jpg") });

            Assert.NotEqual(first, second);
        }

        [Fact]
        public void TheOtherSiteIsAnotherGroup()
        {
            var onCom = GroupKey.For(SiteRegion.Com, new[] { Book(9255, @"C:\1.jpg") });
            var onBy = GroupKey.For(SiteRegion.By, new[] { Book(9255, @"C:\1.jpg") });

            Assert.NotEqual(onCom, onBy);
        }

        [Fact]
        public void TheOtherFormatIsAnotherGroup()
        {
            var first = GroupKey.For(SiteRegion.Com, new[] { Book(9255, @"C:\1.jpg") });
            var second = GroupKey.For(SiteRegion.Com, new[] { Book(9256, @"C:\1.jpg") });

            Assert.NotEqual(first, second);
        }

        [Fact]
        public void AnAddedPhotographIsAnotherGroup()
        {
            var first = GroupKey.For(SiteRegion.Com, new[] { Book(9255, @"C:\1.jpg") });
            var second = GroupKey.For(SiteRegion.Com, new[] { Book(9255, @"C:\1.jpg", @"C:\2.jpg") });

            Assert.NotEqual(first, second);
        }
    }
}
