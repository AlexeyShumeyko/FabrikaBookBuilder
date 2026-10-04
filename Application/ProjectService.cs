using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using PhotoBook.Core;
using PhotoBookRenamer.Infrastructure;

namespace PhotoBookRenamer.Application
{
    /// <summary>
    /// Building a project out of folders, and remembering what it looked like a moment ago.
    /// </summary>
    /// <remarks>
    /// Storing a project is not here any more - that is <see cref="IProjectRepository"/>,
    /// which owns the file format. This class used to do both, and used to read and write
    /// files itself with <c>System.IO.File</c>, which is why the layer that decides what a
    /// project is also knew where projects are kept.
    /// </remarks>
    public class ProjectService : IProjectService
    {
        private readonly IFileService _fileService;
        private readonly IImageService _imageService;
        private readonly Stack<Project> _undoStack = new();
        private readonly Stack<Project> _redoStack = new();

        public ProjectService(IFileService fileService, IImageService imageService)
        {
            _fileService = fileService;
            _imageService = imageService;
        }

        public bool CanUndo => _undoStack.Count > 0;
        public bool CanRedo => _redoStack.Count > 0;

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

                // Обложка НЕ добавляется в Pages, она только в свойстве Cover
                // Добавляем только страницы (не обложку)
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

            // Добавляем все книги в проект
            foreach (var book in books)
            {
                project.Books.Add(book);
            }

            // Устанавливаем индексы книг
            for (int i = 0; i < project.Books.Count; i++)
            {
                project.Books[i].BookIndex = i + 1;
            }

            // Кадр карточек формируется по реальным размерам фото, поэтому они нужны
            // сразу при создании проекта.
            await ProjectImageSizes.FillMissingAsync(project, _imageService);

            return project;
        }

        public void SaveState(Project project)
        {
            if (project == null) return;

            var clone = project.Clone();
            _undoStack.Push(clone);
            _redoStack.Clear(); // Очищаем redo при новом действии
        }

        public Project? Undo(Project currentProject)
        {
            if (!CanUndo || currentProject == null) return null;

            // Сохраняем текущее состояние в redo
            _redoStack.Push(currentProject.Clone());

            // Восстанавливаем предыдущее состояние
            var previousState = _undoStack.Pop();
            return previousState;
        }

        public Project? Redo(Project currentProject)
        {
            if (!CanRedo || currentProject == null) return null;

            // Сохраняем текущее состояние в undo
            _undoStack.Push(currentProject.Clone());

            // Восстанавливаем следующее состояние
            var nextState = _redoStack.Pop();
            return nextState;
        }

        public void ClearHistory()
        {
            _undoStack.Clear();
            _redoStack.Clear();
        }
    }
}
