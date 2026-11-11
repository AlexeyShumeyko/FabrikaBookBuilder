using System;
using System.Collections.Generic;

namespace PhotoBook.Application.Sites.Books.Files
{
    /// <summary>Checks one book's files against what the site asks of a page.</summary>
    /// <remarks>
    /// <para>
    /// The rules take facts, not the disk: the file's presence and the picture's size are read
    /// elsewhere and arrive as <see cref="PageFileFacts"/>. So each rule is decided in one place,
    /// is tested without a file system, and the batch run of 4.3 only has to gather facts and
    /// hand them over.
    /// </para>
    /// <para>
    /// This is not the gate - <see cref="BookReadiness"/> is. The gate blocks on pages that are
    /// empty or beyond the book; this list says what is wrong with the files that are there, and
    /// at what level, so the preparation screen can explain a refusal instead of just showing it.
    /// </para>
    /// </remarks>
    public static class BookFileCheck
    {
        /// <summary>What is wrong with the files, in page order.</summary>
        /// <param name="pages">The book's files, in page order, with what the disk said.</param>
        /// <param name="rule">What a page of this book has to be.</param>
        public static IReadOnlyList<FileAlert> Check(
            IReadOnlyList<PageFileFacts> pages,
            PageImageRule rule)
        {
            if (pages is null)
                throw new ArgumentNullException(nameof(pages));
            if (rule is null)
                throw new ArgumentNullException(nameof(rule));

            var alerts = new List<FileAlert>();

            for (var index = 0; index < pages.Count; index++)
            {
                var page = pages[index];
                if (page is null)
                    continue;

                var alert = Describe(page, index + 1, rule);
                if (alert is not null)
                    alerts.Add(alert);
            }

            return alerts;
        }

        private static FileAlert? Describe(PageFileFacts page, int pageNumber, PageImageRule rule)
        {
            var name = System.IO.Path.GetFileName(page.Path);

            if (!page.Exists)
            {
                return new FileAlert(
                    FileAlertLevel.Blocking,
                    pageNumber,
                    $"Страница {pageNumber}: файл «{name}» не найден.");
            }

            if (!rule.Accepts(page.Path))
            {
                return new FileAlert(
                    FileAlertLevel.Blocking,
                    pageNumber,
                    $"Страница {pageNumber}: «{name}» — сайт принимает только JPEG (.jpg).");
            }

            if (page.Size is not { } size)
            {
                return new FileAlert(
                    FileAlertLevel.Blocking,
                    pageNumber,
                    $"Страница {pageNumber}: «{name}» не читается как изображение.");
            }

            var required = rule.Required;

            if (rule.IsTooSmall(size))
            {
                return new FileAlert(
                    FileAlertLevel.Blocking,
                    pageNumber,
                    $"Страница {pageNumber}: «{name}» — {size} при требуемых {required}. " +
                    "Файл меньше страницы, сайт его не примет.");
            }

            if (rule.IsTooLarge(size))
            {
                return new FileAlert(
                    FileAlertLevel.NeedsConfirmation,
                    pageNumber,
                    $"Страница {pageNumber}: «{name}» — {size} при требуемых {required}. " +
                    "Сайт может обрезать файл по краям.");
            }

            if (rule.IsWithinTolerance(size))
            {
                return new FileAlert(
                    FileAlertLevel.Information,
                    pageNumber,
                    $"Страница {pageNumber}: «{name}» — {size} вместо {required}, " +
                    $"в пределах допуска сайта ±{rule.TolerancePixels} px.");
            }

            return null;
        }
    }
}
