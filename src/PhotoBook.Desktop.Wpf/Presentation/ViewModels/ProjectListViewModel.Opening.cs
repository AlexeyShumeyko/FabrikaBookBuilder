using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PhotoBook.Core;
using PhotoBook.Application;
using PhotoBookRenamer.Presentation.Views;

namespace PhotoBookRenamer.Presentation.ViewModels
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

                var projectInfoCopy = new ProjectInfo
                {
                    Id = projectId,
                    Name = projectName,
                    FilePath = projectFilePath,
                    Mode = projectMode,
                    BookCount = projectBookCount,
                    PageCount = projectPageCount,
                    Status = projectStatus,
                    CreatedDate = projectCreatedDate,
                    LastModified = projectLastModified
                };

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

                // The screen changes hands only after the record has been written.
                OpenProjectMode(projectMode);

                var finalProjectInfo = new ProjectInfo
                {
                    Id = capturedProjectId,
                    Name = capturedProjectName,
                    FilePath = capturedProjectFilePath,
                    Mode = capturedProjectMode,
                    BookCount = capturedProjectBookCount,
                    Status = capturedProjectStatus,
                    CreatedDate = capturedProjectCreatedDate,
                    LastModified = capturedProjectLastModified
                };

                // The editor is handed its project only once MainViewModel has built the
                // view. Two dispatches, not one: the first is after the view exists, the
                // second after it has finished loading and its bindings are live.
                System.Windows.Application.Current.Dispatcher.BeginInvoke(
                    System.Windows.Threading.DispatcherPriority.Loaded,
                    new System.Action(() =>
                    {
                        System.Windows.Application.Current.Dispatcher.BeginInvoke(
                            System.Windows.Threading.DispatcherPriority.Loaded,
                            new System.Action(() =>
                            {
                                if (System.Windows.Application.Current.MainWindow is MainWindow)
                                {
                                    // The shell decides which editor owns the mode; this
                                    // list asks rather than deciding for itself.
                                    var editor = _navigator.EditorFor(capturedProjectMode);

                                    // Both editors were handed an identical ProjectInfo,
                                    // which is why these two branches used to be the same code
                                    // with one line different. The shell knows which editor
                                    // owns the mode, so there is only one branch left.
                                    if (editor == null) return;

                                    var setProjectInfo = new ProjectInfo
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

                                    editor.SetProject(capturedProject, setProjectInfo);
                                }
                            }));
                    }));
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Ошибка открытия проекта: {ex.Message}";
            }
        }

    }
}
