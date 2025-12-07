using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using PhotoBookRenamer.Models;

namespace PhotoBookRenamer.Services
{
    public class ExportService : IExportService
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
        {
            try
            {
                if (!Directory.Exists(outputFolder))
                {
                    Directory.CreateDirectory(outputFolder);
                }

                var tasks = new List<Task>();

                foreach (var book in project.Books)
                {
                    var bookIndex = book.BookIndex;

                    // Копируем обложку
                    if (book.Cover != null && !string.IsNullOrEmpty(book.Cover.SourcePath))
                    {
                        var coverFileName = GenerateFileName(bookIndex, 0);
                        var coverDest = Path.Combine(outputFolder, coverFileName);
                        tasks.Add(_fileService.CopyFileAsync(book.Cover.SourcePath, coverDest));
                    }

                    // Копируем страницы (сортируем по индексу для правильного порядка)
                    var pageIndex = 1;
                    foreach (var page in book.Pages.Where(p => !p.IsEmpty).OrderBy(p => p.Index))
                    {
                        var pageFileName = GenerateFileName(bookIndex, pageIndex);
                        var pageDest = Path.Combine(outputFolder, pageFileName);
                        tasks.Add(_fileService.CopyFileAsync(page.SourcePath!, pageDest));
                        pageIndex++;
                    }
                }

                await Task.WhenAll(tasks);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}

