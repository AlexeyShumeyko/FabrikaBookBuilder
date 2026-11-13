using System;
using System.Linq;
using PhotoBook.Application.Sites.Books;
using PhotoBook.Core;
using PhotoBook.Presentation.ViewModels;

namespace PhotoBook.Application.Tests.Sites
{
    /// <summary>
    /// The order page is reached from the project's panel and shows what would go to the site.
    /// Its job is to be honest on the way in: a project that is not assembled is shown with the
    /// reason, and the customer learns it before anything is created on the site.
    /// </summary>
    public class SiteOrderViewModelTests
    {
        [Fact]
        public void ListsTheBooksOfAnAssembledProject()
        {
            var project = TestProject.Combined(
                TestProject.Book(1, "/c1.jpg", "/p1.jpg", "/p2.jpg"),
                TestProject.Book(2, "/c2.jpg", "/p3.jpg", "/p4.jpg"));

            var page = new SiteOrderViewModel();
            page.Prepare(project, "Лето 2026");

            Assert.True(page.IsReady);
            Assert.Equal("Лето 2026", page.ProjectName);
            Assert.Equal(2, page.Books.Count);
            Assert.Equal(4, page.SpreadCount);
            Assert.Equal("2 книг", page.BookCountText);
            Assert.All(page.Books, book => Assert.True(book.IsReady));
            Assert.Equal(new[] { "Book 1", "Book 2" }, page.Books.Select(book => book.Name));
            Assert.Equal(new[] { "Готова", "Готова" }, page.Books.Select(book => book.ConditionText));
        }

        [Fact]
        public void ShowsAHalfFilledBookAndWhyItCannotGo()
        {
            var project = TestProject.Combined(
                TestProject.Book(1, "/c1.jpg", "/p1.jpg", "/p2.jpg"),
                TestProject.Book(2, "/c2.jpg", "/p3.jpg", string.Empty));

            var page = new SiteOrderViewModel();
            page.Prepare(project);

            Assert.False(page.IsReady);
            Assert.True(page.HasMessage);
            Assert.Contains("заполнено 2 из 3 слотов", page.Message, StringComparison.Ordinal);
            Assert.Equal("Не хватает фотографий", page.Books[1].ConditionText);
            // A project without a name is still shown as a project rather than as an empty line.
            Assert.Equal("Проект", page.ProjectName);
        }

        [Fact]
        public void SaysABookHasNoPhotosAtAll()
        {
            var project = TestProject.Combined(TestProject.Book(1, string.Empty));

            var page = new SiteOrderViewModel();
            page.Prepare(project);

            Assert.False(page.IsReady);
            Assert.Contains("нет ни одной фотографии", page.Message, StringComparison.Ordinal);
            Assert.Equal(BookCondition.NoPhotos, page.Books[0].Condition);
        }

        [Fact]
        public void CountsPhotosTheOrderWillNotCarry()
        {
            var project = TestProject.Combined(TestProject.Book(1, "/c1.jpg", "/p1.jpg"));
            project.AvailableFiles.Add("/left-behind.jpg");

            var page = new SiteOrderViewModel();
            page.Prepare(project);

            Assert.Contains("Фотографий вне книги: 1", page.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void SaysTheParameterStepComesNextWhenEverythingIsAssembled()
        {
            var page = new SiteOrderViewModel();
            page.Prepare(TestProject.Combined(TestProject.Book(1, "/c1.jpg", "/p1.jpg")), "Проект");

            Assert.True(page.IsReady);
            Assert.True(page.HasMessage);
            Assert.Contains("следующем шаге", page.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void AnUnopenedProjectHasNothingToShow()
        {
            var page = new SiteOrderViewModel();
            page.Prepare(null);

            Assert.False(page.IsReady);
            Assert.Empty(page.Books);
            Assert.Equal("Проект не открыт.", page.Message);
        }

        [Fact]
        public void GoingBackAsksTheShell()
        {
            var page = new SiteOrderViewModel();
            bool asked = false;
            page.BackRequested += (_, _) => asked = true;

            page.Back();

            Assert.True(asked);
        }
    }
}
