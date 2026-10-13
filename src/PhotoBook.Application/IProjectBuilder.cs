using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using PhotoBook.Core;

namespace PhotoBook.Application
{
    /// <summary>
    /// Assembles a project out of a list of folders: one book per folder, the cover
    /// detected, and the card layout measured from the real photograph sizes.
    /// </summary>
    /// <remarks>
    /// Only the folders mode builds its projects this way. Combined mode asks the user for
    /// photographs instead, so it has nothing to call here.
    /// </remarks>
    public interface IProjectBuilder
    {
        Task<Project> CreateProjectFromFoldersAsync(List<string> folderPaths);
    }

    internal class ProjectBuilder : IProjectBuilder
    {
        private readonly IFileService _fileService;
        private readonly IImageService _imageService;

        public ProjectBuilder(IFileService fileService, IImageService imageService)
        {
            _fileService = fileService;
            _imageService = imageService;
        }

        public async Task<Project> CreateProjectFromFoldersAsync(List<string> folderPaths)
        {
            var project = new Project { Mode = AppMode.UniqueFolders };

            var books = await Task.WhenAll(folderPaths.Select(async folderPath =>
            {
                var folderName = System.IO.Path.GetFileName(folderPath);

                var files = await _fileService.GetJpegFilesAsync(folderPath);
                var coverPath = await _imageService.DetectCoverAsync(files.ToArray());

                var book = new Book
                {
                    FolderPath = folderPath,
                    Name = folderName,
                    Cover = new Page
                    {
                        SourcePath = coverPath,
                        IsCover = true,
                        Index = 0
                    }
                };

                // The cover is not one of the pages: it lives in Cover, and the spreads start at 1.
                var pageIndex = 1;
                foreach (var file in files.Where(f => f != coverPath).OrderBy(f => f))
                {
                    book.Pages.Add(new Page
                    {
                        SourcePath = file,
                        IsCover = false,
                        Index = pageIndex,
                        DisplayIndex = pageIndex
                    });
                    pageIndex++;
                }

                return book;
            }));

            foreach (var book in books)
            {
                project.Books.Add(book);
            }

            for (int i = 0; i < project.Books.Count; i++)
            {
                project.Books[i].BookIndex = i + 1;
            }

            // The card layout is built from the real sizes of the photographs, so they are
            // read here rather than on the first draw.
            await ProjectImageSizes.FillMissingAsync(project, _imageService);

            return project;
        }
    }
}
