using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using PhotoBook.Application.Sites.Books.Files;

namespace PhotoBook.Presentation.ViewModels
{
    /// <summary>One file of a book as the list shows it: what it is, its name, and its problem.</summary>
    /// <remarks>
    /// The level decides the colour, not the presence of the text. A file the order cannot go
    /// with is red; a file inside the site's own tolerance is a remark and stays grey. Painting
    /// every line red made a saveable book look broken, and painting a real problem grey hid it.
    /// </remarks>
    public sealed record UploadFileRow(
        string Title,
        string FileName,
        string? Problem,
        FileAlertLevel? Level = null)
    {
        /// <summary>Whether the site said something about this file.</summary>
        public bool HasProblem => !string.IsNullOrEmpty(Problem);

        /// <summary>Whether the site's words are a problem rather than a remark.</summary>
        public bool IsProblem => HasProblem && Level == FileAlertLevel.Blocking;

        /// <summary>Whether the site only remarked on the file.</summary>
        public bool IsRemark => HasProblem && Level == FileAlertLevel.Information;
    }

    /// <summary>One book on the upload screen: its word, its files, and whether it is open.</summary>
    /// <remarks>
    /// A book with something wrong opens itself, because the customer's next question is which
    /// file, and a list that hid it behind a click would make them ask it for every book. A clean
    /// book stays closed and says so in one word.
    /// </remarks>
    public sealed class UploadBookRow : ViewModelBase
    {
        private bool _isExpanded;

        public UploadBookRow(int number, string name, BookStatus status, IReadOnlyList<UploadFileRow> files)
        {
            Number = number;
            Name = name ?? string.Empty;
            Status = status ?? throw new ArgumentNullException(nameof(status));
            Files = files ?? Array.Empty<UploadFileRow>();

            // A book opens when it needs an answer: a refusal, a file the site would trim, or a
            // book that is short. It stays closed when the site only remarked on a file - six
            // books each with one tolerance note is a wall of rows nobody asked to see.
            _isExpanded = status.HasProblem || Files.Any(file => file.IsProblem);
        }

        public int Number { get; }

        public string Name { get; }

        public BookStatus Status { get; }

        public IReadOnlyList<UploadFileRow> Files { get; }

        /// <summary>The book's place in the order, as the column shows it.</summary>
        public string NumberText => Number.ToString(CultureInfo.CurrentCulture);

        /// <summary>«Готова», «Ошибка в файлах» and the rest, in one word.</summary>
        public string StatusText => Status.Text;

        /// <summary>Whether the book needs attention, which the list shows in red.</summary>
        public bool IsFailed => Status.HasProblem;

        /// <summary>What the check said about the book as a whole, one line each.</summary>
        public string ReasonsText => string.Join(Environment.NewLine, Status.Reasons);

        /// <summary>Whether there is anything to read in the book's expansion.</summary>
        public bool HasReasons => Status.Reasons.Count > 0;

        /// <summary>Whether the book has files to show at all.</summary>
        public bool HasFiles => Files.Count > 0;

        /// <summary>Whether the book's files are listed under it.</summary>
        public bool IsExpanded
        {
            get => _isExpanded;
            set => SetProperty(ref _isExpanded, value);
        }
    }
}
