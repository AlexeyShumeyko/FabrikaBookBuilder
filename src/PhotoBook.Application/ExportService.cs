using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using PhotoBook.Core;

namespace PhotoBookRenamer.Application
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

        public async Task<bool> ExportProjectAsync(Project project, string outputFolder)
            => await ExportProjectAsync(project, outputFolder, new ExportOptions());

        public async Task<bool> ExportProjectAsync(Project project, string outputFolder, ExportOptions options)
        {
            if (project == null || string.IsNullOrWhiteSpace(outputFolder))
                return false;

            options ??= new ExportOptions();

            try
            {
                if (!Directory.Exists(outputFolder))
                {
                    Directory.CreateDirectory(outputFolder);
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
                                  Path.Combine(outputFolder, UniqueName(entry.FileName, usedNames))));
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
                            bookFolder = Path.Combine(outputFolder, SanitizeFolderName($"{bookIndex:D3} {book.Name}"));

                            // The per-book folder must exist BEFORE its first file is copied
                            // into it. It is created here rather than left to CopyFileAsync
                            // because the whole work list is built up front, and a book whose
                            // folder is missing fails inside Task.WhenAll - which surfaces as
                            // "could not copy" with no hint about which folder, or as a raw
                            // exception with nothing in the UI to point at.
                            if (!Directory.Exists(bookFolder))
                            {
                                Directory.CreateDirectory(bookFolder);
                            }
                        }

                        // Копируем обложку
                        if (book.Cover != null && !string.IsNullOrEmpty(book.Cover.SourcePath))
                        {
                            work.Add((book.Cover.SourcePath,
                                      Path.Combine(bookFolder, UniqueName(GenerateFileName(bookIndex, 0), usedNames))));
                        }

                        // Копируем страницы (сортируем по индексу для правильного порядка)
                        var pageIndex = 1;
                        foreach (var page in book.Pages.Where(p => !p.IsEmpty).OrderBy(p => p.Index))
                        {
                            work.Add((page.SourcePath!,
                                      Path.Combine(bookFolder, UniqueName(GenerateFileName(bookIndex, pageIndex), usedNames))));
                            pageIndex++;
                        }
                    }
                }

                if (work.Count == 0)
                    return true;

                var completed = 0;
                var gate = new object();

                await Task.WhenAll(work.Select(async item =>
                {
                    await _fileService.CopyFileAsync(item.Source, item.Destination);
                    lock (gate)
                    {
                        completed++;
                        options.Progress?.Report((double)completed / work.Count);
                    }
                }));

                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Guards against two books resolving to the same file name, which would silently
        /// overwrite one of them once per-book subfolders are switched on.
        /// </summary>
        private static string UniqueName(string fileName, HashSet<string> used)
        {
            if (used.Add(fileName)) return fileName;

            var dir = Path.GetDirectoryName(fileName) ?? string.Empty;
            var stem = Path.GetFileNameWithoutExtension(fileName);
            var ext = Path.GetExtension(fileName);

            for (int i = 2; i < 10_000; i++)
            {
                var candidate = Path.Combine(dir, $"{stem}-{i}{ext}");
                if (used.Add(candidate)) return candidate;
            }
            return fileName;
        }

        private static string SanitizeFolderName(string name)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var cleaned = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim();
            return string.IsNullOrWhiteSpace(cleaned) ? "Book" : cleaned;
        }
    }
}

