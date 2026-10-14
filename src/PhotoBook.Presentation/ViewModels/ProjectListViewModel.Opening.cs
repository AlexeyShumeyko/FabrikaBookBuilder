using System.Windows.Input;
// System.Windows.Input is where ICommand lives in every engine, not only WPF -
// Avalonia kept the name for exactly this reason. Only System.Windows itself is
// the part that binds to one engine.
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PhotoBook.Core;
using PhotoBook.Application;

namespace PhotoBook.Presentation.ViewModels
{
    /// <summary>Getting from a card on the list into an editor.</summary>
    public partial class ProjectListViewModel : ViewModelBase
    {
        private void CreateProject()
        {
            CurrentMode = AppMode.StartScreen;
        }

        private void OpenProject(ProjectInfo? projectInfo)
        {
            if (projectInfo != null)
            {
                var projectInfoCopy = new ProjectInfo
                {
                    Id = projectInfo.Id,
                    Name = projectInfo.Name,
                    FilePath = projectInfo.FilePath,
                    Mode = projectInfo.Mode,
                    BookCount = projectInfo.BookCount,
                    PageCount = projectInfo.PageCount,
                    Status = projectInfo.Status,
                    CreatedDate = projectInfo.CreatedDate,
                    LastModified = projectInfo.LastModified
                };
                _ = OpenProjectAsync(projectInfoCopy);
            }
        }

        private async Task OpenProjectAsync(ProjectInfo projectInfo)
        {
            try
            {

                var projectId = projectInfo.Id ?? string.Empty;
                var projectName = projectInfo.Name ?? string.Empty;
                var projectMode = projectInfo.Mode;
                var projectBookCount = projectInfo.BookCount;
                var projectPageCount = projectInfo.PageCount;
                var projectStatus = projectInfo.Status;
                var projectLastModified = projectInfo.LastModified;
                var projectCreatedDate = projectInfo.CreatedDate;

                if (string.IsNullOrEmpty(projectId))
                {
                    ErrorMessage = $"Ошибка: проект '{projectName}' не имеет Id. Невозможно открыть проект.";
                    return;
                }

                // The file name IS the Id: one file per project, and a card that lost
                // its Id has nothing to open.
                var projectsDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "PhotoBookRenamer",
                    "Projects");

                if (!Directory.Exists(projectsDir))
                {
                    Directory.CreateDirectory(projectsDir);
                }

                // Built from the Id rather than taken from the record: the two have been
                // seen to disagree, and the wrong one means the project cannot be opened.
                var projectFilePath = Path.Combine(projectsDir, $"{projectId}.json");

                Project? project = null;

                if (System.IO.File.Exists(projectFilePath))
                {
                    try
                    {
                        project = await _projects.LoadAsync(projectFilePath);

                        if (project == null)
                        {
                            project = new Project { Mode = projectMode };
                        }
                        else if (project.Books == null)
                        {
                            project = new Project { Mode = projectMode };
                        }
                    }
                    catch (Exception ex)
                    {
                        ErrorMessage = $"Ошибка загрузки проекта из файла {projectFilePath}: {ex.Message}";
                        project = new Project { Mode = projectMode };
                    }
                }
                else
                {
                    // No file yet means a project made here and not saved.
                    project = new Project { Mode = projectMode };
                }

                // Everything is read into locals and one record is built BEFORE the
                // hand-over is deferred: by the time it runs, the list has moved on.
                var capturedProjectId = projectId;
                var capturedProjectName = projectName;
                var capturedProjectFilePath = projectFilePath;
                var capturedProjectMode = projectMode;
                var capturedProjectBookCount = projectBookCount;
                var capturedProjectPageCount = projectPageCount;
                var capturedProjectStatus = projectStatus;
                var capturedProjectCreatedDate = projectCreatedDate;
                var capturedProjectLastModified = projectLastModified;
                var capturedProject = project;

                // One record for the hand-over, built from the locals because the list has moved on by
                // the time it arrives. It carries PageCount as well as BookCount: the editor
                // reads it on arrival, and a record without it arrives as a project claiming
                // to have no pages - a difference nobody would see until an export came up
                // short. The index copy written to disk is a separate record on purpose.
                var editorRecord = new ProjectInfo
                {
                    Id = capturedProjectId,
                    Name = capturedProjectName,
                    FilePath = capturedProjectFilePath,
                    Mode = capturedProjectMode,
                    BookCount = capturedProjectBookCount,
                    PageCount = capturedProjectPageCount,
                    Status = capturedProjectStatus,
                    CreatedDate = capturedProjectCreatedDate,
                    LastModified = capturedProjectLastModified
                };

                // The project is handed over FIRST and the screen opens SECOND.
                //
                // The other way round there is nothing waiting to be delivered at the moment
                // the mode changes, so the editor comes up empty, and the project handed over
                // a moment later sits and waits for the next time the screen changes. That is
                // why going back to the list and opening the same project again worked, and
                // why it looked like the program loading everything twice.
                //
                // Handing over first is what the waiting is for: the record is in place
                // before the editor's screen exists, and is delivered the moment it does.
                _navigator.HandProjectToEditor(capturedProject, editorRecord);

                OpenProjectMode(projectMode);
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Ошибка открытия проекта: {ex.Message}";
            }
        }

    }
}
