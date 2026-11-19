using System;
using System.Collections.Generic;

using System.Linq;
using System.Threading.Tasks;
using PhotoBook.Core;

namespace PhotoBook.Application
{
    internal class ExportService : IExportService
    {
        private readonly IFileService _fileService;

        public ExportService(IFileService fileService)
        {
            _fileService = fileService;
        }

        public string GenerateFileName(int bookIndex, int fileIndex)
        {
            return $"{bookIndex:D3}-{fileIndex:D2}.jpg";
        }

        public async Task<Result> ExportProjectAsync(Project project, string outputFolder)
            => await ExportProjectAsync(project, outputFolder, new ExportOptions());

        public async Task<Result> ExportProjectAsync(Project project, string outputFolder, ExportOptions options)
        {
            if (project == null || string.IsNullOrWhiteSpace(outputFolder))
                return Result.Fail("Не выбран проект или папка для выгрузки.");

            options ??= new ExportOptions();

            try
            {
                if (!_fileService.DirectoryExists(outputFolder))
                {
                    _fileService.EnsureDirectory(outputFolder);
                }

                // Build the full work list first so progress can be reported by file count
                // instead of jumping in uneven steps while the copies run in parallel.
                var work = new List<(string Source, string Destination)>();
                var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                // The combined run writes the smallest set the client's site accepts: one
                // 000-FF file per position every book shares, and one KKK-FF file per book
                // that overrides it. The folders mode is untouched and keeps writing a file
                // per slot, because there every book is a different set of photographs.
                if (project.Mode == AppMode.Combined)
                {
                    var plan = CombinedExportPlanner.Build(project, GenerateFileName);
                    foreach (var entry in plan.Entries)
                    {
                        work.Add((entry.SourcePath,
                                  System.IO.Path.Combine(outputFolder, UniqueName(entry.FileName, usedNames))));
                    }
                }
                else
                {
                    foreach (var book in project.Books)
                    {
                        var bookIndex = book.BookIndex;
                        var bookFolder = outputFolder;

                        if (options.PerBookSubfolders)
                        {
                            bookFolder = System.IO.Path.Combine(outputFolder, SanitizeFolderName($"{bookIndex:D3} {book.Name}"));

                            // The per-book folder must exist BEFORE its first file is copied
                            // into it. It is created here rather than left to CopyFileAsync
                            // because the whole work list is built up front, and a book whose
                            // folder is missing fails inside Task.WhenAll - which surfaces as
                            // "could not copy" with no hint about which folder, or as a raw
                            // exception with nothing in the UI to point at.
                            if (!_fileService.DirectoryExists(bookFolder))
                            {
                                _fileService.EnsureDirectory(bookFolder);
                            }
                        }

                        if (book.Cover != null && !string.IsNullOrEmpty(book.Cover.SourcePath))
                        {
                            work.Add((book.Cover.SourcePath,
                                      System.IO.Path.Combine(bookFolder, UniqueName(GenerateFileName(bookIndex, 0), usedNames))));
                        }

                        // Ordered by index: the order on disk has to be the order in the book, and
                        // the list is not kept in it.
                        var pageIndex = 1;
                        foreach (var page in book.Pages.Where(p => !p.IsEmpty).OrderBy(p => p.Index))
                        {
                            work.Add((page.SourcePath!,
                                      System.IO.Path.Combine(bookFolder, UniqueName(GenerateFileName(bookIndex, pageIndex), usedNames))));
                            pageIndex++;
                        }
                    }
                }

                if (work.Count == 0)
                    return Result.Ok();

                // A project whose photographs were deleted used to be copied file by file: the
                // first missing one threw, the message named a copy failure, and a folder on a
                // network share that no longer answered made the window look hung while every
                // path was tried. The whole list is checked first, so the answer is one sentence
                // saying what is missing and what to do about it.
                var missing = work
                    .Where(item => !_fileService.FileExists(item.Source))
                    .Select(item => System.IO.Path.GetFileName(item.Source))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (missing.Count > 0)
                {
                    var shown = string.Join(", ", missing.Take(10));
                    if (missing.Count > 10)
                        shown += " и ещё " + (missing.Count - 10);

                    return Result.Fail(
                        "Не удалось выгрузить: файлы проекта не найдены — " + shown +
                        ". Верните файлы на место или создайте проект заново.");
                }

                var completed = 0;
                var gate = new object();
                var failures = new List<(string Source, Exception Error)>();

                await Task.WhenAll(work.Select(async item =>
                {
                    try
                    {
                        await _fileService.CopyFileAsync(item.Source, item.Destination);
                    }
                    catch (Exception ex)
                    {
                        // One unreadable photograph or one folder that refuses writes must not
                        // turn into "could not copy" for the whole run: the file is kept, and
                        // the sentence below names it.
                        lock (gate)
                            failures.Add((item.Source, ex));

                        return;
                    }

                    lock (gate)
                    {
                        completed++;
                        options.Progress?.Report((double)completed / work.Count);
                    }
                }));

                if (failures.Count > 0)
                {
                    var first = failures
                        .OrderBy(failure => failure.Source, StringComparer.OrdinalIgnoreCase)
                        .First();

                    return Result.Fail(
                        DescribeCopyFailure(first.Error, first.Source, outputFolder, failures.Count));
                }

                return Result.Ok();
            }
            catch (Exception ex)
            {
                // The reason used to be thrown away here: the photographer was told to check
                // that the folder is writable, whatever had actually gone wrong, and the
                // journal had nothing either.
                return Result.Fail(DescribeCopyFailure(ex, null, outputFolder, 0));
            }
        }

        /// <summary>
        /// Why the copies did not happen, in words the photographer can act on.
        /// </summary>
        /// <remarks>
        /// The dialog used to say "Не удалось" and nothing else, which left the customer with no
        /// choice but to call support. The system's own text is kept where it says something, but
        /// the cases that actually happen - a folder the account may not write to, a full disk, a
        /// photograph open in another program - are named, with the file or the folder in them.
        /// </remarks>
        private static string DescribeCopyFailure(
            Exception error,
            string? source,
            string? folder,
            int failedFiles)
        {
            var file = string.IsNullOrWhiteSpace(source)
                ? null
                : System.IO.Path.GetFileName(source);

            var folderText = string.IsNullOrWhiteSpace(folder) ? "выбранную папку" : $"«{folder}»";
            var count = failedFiles > 1 ? $" Не удалось скопировать файлов: {failedFiles}." : string.Empty;

            switch (Code(error))
            {
                case 5:
                    return $"Не удалось выгрузить: нет прав на запись в папку {folderText}. " +
                           "Выберите другую папку — например, на рабочем столе." + count;

                case 32:
                case 33:
                    return $"Не удалось выгрузить: файл «{file ?? "страницы"}» занят другой программой. " +
                           "Закройте её — просмотр фотографий или проводник — и повторите экспорт." + count;

                case 39:
                case 112:
                    return "Не удалось выгрузить: на диске не хватает места. " +
                           "Освободите место или выберите другую папку и повторите экспорт." + count;

                case 3:
                case 2:
                case 53:
                    return $"Не удалось выгрузить: папка {folderText} недоступна. " +
                           "Проверьте, что диск или сетевая папка подключены, и повторите экспорт." + count;

                case 206:
                    return "Не удалось выгрузить: путь к папке слишком длинный. " +
                           "Выберите папку ближе к корню диска." + count;

                default:
                    {
                        var reason = string.IsNullOrWhiteSpace(error.Message)
                            ? "неизвестная ошибка"
                            : error.Message.Trim();

                        var what = file is null ? "файлы проекта" : $"файл «{file}»";

                        return $"Не удалось выгрузить {what}: {reason}. " +
                               "Проверьте папку назначения и свободное место и повторите экспорт." + count;
                    }
            }
        }

        /// <summary>The Windows code inside an exception, or zero when it has none.</summary>
        private static int Code(Exception error) => error.HResult & 0xFFFF;

        /// <summary>
        /// Guards against two books resolving to the same file name, which would silently
        /// overwrite one of them once per-book subfolders are switched on.
        /// </summary>
        private static string UniqueName(string fileName, HashSet<string> used)
        {
            if (used.Add(fileName)) return fileName;

            var dir = System.IO.Path.GetDirectoryName(fileName) ?? string.Empty;
            var stem = System.IO.Path.GetFileNameWithoutExtension(fileName);
            var ext = System.IO.Path.GetExtension(fileName);

            for (int i = 2; i < 10_000; i++)
            {
                var candidate = System.IO.Path.Combine(dir, $"{stem}-{i}{ext}");
                if (used.Add(candidate)) return candidate;
            }
            return fileName;
        }

        private static string SanitizeFolderName(string name)
        {
            var invalid = System.IO.Path.GetInvalidFileNameChars();
            var cleaned = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim();
            return string.IsNullOrWhiteSpace(cleaned) ? "Book" : cleaned;
        }
    }
}

