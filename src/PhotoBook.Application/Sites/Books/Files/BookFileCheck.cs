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
    /// A file the site would trim is a refusal too: there is no box to tick that makes an
    /// oversized cover the page the customer ordered, so the sentence names the file and sends
    /// the customer back to the file or to the format's parameters.
    /// </para>
    /// </remarks>
    public static class BookFileCheck
    {
        /// <summary>What is wrong with the files, in page order.</summary>
        /// <param name="pages">The book's files, in page order, with what the disk said.</param>
        /// <param name="rule">What a page of this book has to be.</param>
        /// <param name="title">
        /// What to call the files when they are not pages, such as «Обложка»: the cover is one
        /// file with its own size, and calling it «Страница 1» would misname it to the customer.
        /// </param>
        /// <param name="pageWord">
        /// What the product calls one page, «Страница» by default. A layflat counts spreads, and
        /// a message that called them pages would name a thing the customer never assembled.
        /// </param>
        public static IReadOnlyList<FileAlert> Check(
            IReadOnlyList<PageFileFacts> pages,
            PageImageRule rule,
            string? title = null,
            string pageWord = "Страница")
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

                var alert = Describe(page, index + 1, rule, title, pageWord);
                if (alert is not null)
                    alerts.Add(alert);
            }

            return alerts;
        }

        private static FileAlert? Describe(
            PageFileFacts page,
            int pageNumber,
            PageImageRule rule,
            string? title,
            string pageWord)
        {
            var name = System.IO.Path.GetFileName(page.Path);
            var where = title ?? pageWord + " " + pageNumber;
            var number = title is null ? pageNumber : (int?)null;

            if (!page.Exists)
            {
                return new FileAlert(
                    FileAlertLevel.Blocking,
                    number,
                    $"{where}: файл «{name}» не найден.");
            }

            if (!rule.Accepts(page.Path))
            {
                return new FileAlert(
                    FileAlertLevel.Blocking,
                    number,
                    $"{where}: «{name}» — сайт принимает только JPEG (.jpg).");
            }

            if (page.Size is not { } size)
            {
                return new FileAlert(
                    FileAlertLevel.Blocking,
                    number,
                    $"{where}: «{name}» не читается как изображение.");
            }

            var required = rule.Required;

            if (rule.IsTooSmall(size))
            {
                return new FileAlert(
                    FileAlertLevel.Blocking,
                    number,
                    $"{where}: «{name}» — {size} при требуемых {required}. " +
                    "Файл меньше страницы, сайт его не примет.");
            }

            if (rule.IsTooLarge(size))
            {
                return new FileAlert(
                    FileAlertLevel.Blocking,
                    number,
                    $"{where}: «{name}» — {size} при требуемых {required}. " +
                    "Файл больше страницы, сайт напечатает его с обрезанными краями.");
            }

            if (rule.IsWithinTolerance(size))
            {
                return new FileAlert(
                    FileAlertLevel.Information,
                    number,
                    $"{where}: «{name}» — {size} вместо {required}, " +
                    $"в пределах допуска сайта ±{rule.TolerancePixels} px.");
            }

            return null;
        }
    }
}
