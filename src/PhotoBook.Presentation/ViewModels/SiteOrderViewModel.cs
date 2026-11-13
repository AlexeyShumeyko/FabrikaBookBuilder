using System;
using System.Collections.Generic;
using System.Linq;
using PhotoBook.Application.Sites.Books;
using CoreProject = PhotoBook.Core.Project;

namespace PhotoBook.Presentation.ViewModels
{
    /// <summary>One book of the order as the page lists it.</summary>
    /// <param name="Number">The book's number in the project, starting at one.</param>
    /// <param name="Name">Its name, or «Книга N» when it has none.</param>
    /// <param name="Spreads">Spreads in the book, the cover not counted.</param>
    /// <param name="Photos">Slots holding a photo, the cover counted.</param>
    /// <param name="Condition">What the book still needs before it can go.</param>
    public sealed record OrderBookRow(int Number, string Name, int Spreads, int Photos, BookCondition Condition)
    {
        /// <summary>How the row describes the book's state.</summary>
        public string ConditionText => Condition switch
        {
            BookCondition.Ready => "Готова",
            BookCondition.NoPhotos => "Нет фотографий",
            _ => "Не хватает фотографий"
        };

        /// <summary>Whether the book may go to the site as it stands.</summary>
        public bool IsReady => Condition == BookCondition.Ready;
    }

    /// <summary>
    /// The order page: the project's books, what is wrong with them, and the way on.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The page is reached from the project's own panel, beside export, and only a fully
    /// assembled project gets there: an order assembled from a half-filled book would be a
    /// project on the site that nobody meant to make.
    /// </para>
    /// <para>
    /// What the page shows is read from the project every time it is opened, not stored: the
    /// customer changes the project, comes back, and must see the books as they are now.
    /// </para>
    /// <para>
    /// The book's format, its options and its price belong to the parameter step, which does not
    /// exist yet, so the page says that plainly instead of offering a button that cannot work.
    /// </para>
    /// </remarks>
    public sealed class SiteOrderViewModel : ViewModelBase
    {
        private IReadOnlyList<OrderBookRow> _books = Array.Empty<OrderBookRow>();
        private string _projectName = string.Empty;
        private int _spreadCount;
        private string _message = string.Empty;
        private bool _isReady;

        /// <summary>Raised when the customer asks to go back to the project.</summary>
        public event EventHandler? BackRequested;

        /// <summary>The books of the order, in the project's order.</summary>
        public IReadOnlyList<OrderBookRow> Books
        {
            get => _books;
            private set
            {
                if (SetProperty(ref _books, value))
                    OnPropertyChanged(nameof(BookCountText));
            }
        }

        /// <summary>How many books are going, in words.</summary>
        public string BookCountText => Books.Count == 1
            ? "1 книга"
            : Books.Count + " книг";

        /// <summary>The project's name, as the header shows it.</summary>
        public string ProjectName
        {
            get => _projectName;
            private set => SetProperty(ref _projectName, value ?? string.Empty);
        }

        /// <summary>Spreads across the order, which is what the site counts.</summary>
        public int SpreadCount
        {
            get => _spreadCount;
            private set => SetProperty(ref _spreadCount, value);
        }

        /// <summary>What the customer has to know before going on.</summary>
        public string Message
        {
            get => _message;
            private set
            {
                if (SetProperty(ref _message, value ?? string.Empty))
                    OnPropertyChanged(nameof(HasMessage));
            }
        }

        /// <summary>Whether there is anything to say.</summary>
        public bool HasMessage => _message.Length > 0;

        /// <summary>Whether the project is assembled well enough to be sent at all.</summary>
        public bool IsReady
        {
            get => _isReady;
            private set => SetProperty(ref _isReady, value);
        }

        /// <summary>
        /// Reads the project into the page. A project that is not assembled comes back with the
        /// books still listed and the reason why nothing can be sent.
        /// </summary>
        public void Prepare(CoreProject? project, string? projectName = null)
        {
            if (project is null)
            {
                Books = Array.Empty<OrderBookRow>();
                ProjectName = string.Empty;
                SpreadCount = 0;
                IsReady = false;
                Message = "Проект не открыт.";
                return;
            }

            var inventory = BookInventory.From(project);

            Books = inventory.Books
                .Select(book => new OrderBookRow(book.Number, book.Name, book.Spreads, book.Photos, book.Condition))
                .ToList();

            ProjectName = string.IsNullOrWhiteSpace(projectName) ? "Проект" : projectName!.Trim();
            SpreadCount = inventory.Books.Sum(book => book.Spreads);
            IsReady = inventory.AllReady;
            Message = Problems(inventory);
        }

        /// <summary>Goes back to the project the page was opened from.</summary>
        public void Back() => BackRequested?.Invoke(this, EventArgs.Empty);

        /// <summary>Why the order cannot go, or the note that says what comes next.</summary>
        private string Problems(BookInventory inventory)
        {
            var lines = new List<string>();

            foreach (var book in inventory.Books.Where(book => !book.CanUpload))
            {
                lines.Add(book.Name + ": " +
                    (book.Condition == BookCondition.NoPhotos
                        ? "нет ни одной фотографии."
                        : "заполнено " + book.Photos + " из " + book.Slots + " слотов."));
            }

            if (inventory.Books.Count == 0)
                lines.Add("В проекте нет ни одной книги.");

            if (inventory.LeftoverPhotos > 0)
            {
                lines.Add("Фотографий вне книги: " + inventory.LeftoverPhotos +
                    " — они не попадут на сайт.");
            }

            if (lines.Count == 0)
            {
                lines.Add("Книги собраны. Форматы, цена и отправка появятся здесь на следующем шаге — " +
                    "сейчас эта страница показывает, что именно уйдёт на сайт.");
            }

            return string.Join(Environment.NewLine, lines);
        }
    }
}
