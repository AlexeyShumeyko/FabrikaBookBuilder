using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using PhotoBook.Application;
using PhotoBook.Core;
using PhotoBook.Infrastructure;

namespace PhotoBook.Shell.Views;

/// <summary>
/// Asks where an export goes, and then makes it.
/// </summary>
/// <remarks>
/// <para>
/// It shows what will be produced before anything is copied, because the folder is always a
/// new one and the name of that folder is the only irreversible thing on this screen.
/// </para>
/// <para>
/// The markup is a card from the program's own design system rather than a page of borders
/// written by hand in a constructor: the first version drew its own greys, its own radii and
/// its own type sizes, and the result read as a window from another program that happened to
/// be open. What is left in this file is only what changes while the window is up.
/// </para>
/// </remarks>
public partial class ExportWindow : Window
{
    private readonly Project _project;
    private readonly IExportService _export;

    private string _targetFolder;
    private bool _running;
    private bool _succeeded;
    private string _exportedFolder = string.Empty;

    /// <summary>The folder the export went to, once it has.</summary>
    public string ExportedFolder => _exportedFolder;

    /// <summary>Whether the export finished. A window that was closed is not a failure.</summary>
    public bool Exported => _succeeded;

    public ExportWindow(Project project, IExportService exportService, string? defaultFolder = null)
    {
        InitializeComponent();

        _project = project;
        _export = exportService;

        _targetFolder = defaultFolder
                        ?? ExportFolderNaming.UniqueExportFolder(
                            Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                            ExportFolderNaming.NameFor(project));

        ShowTargetFolder();
        ShowCounts();

        // Hidden in the combined mode: a shared file cannot live in one book's folder, and
        // that mode's naming is the whole point.
        SubfoldersBox.IsVisible = _project.Mode != AppMode.Combined;

        KeyDown += OnWindowKeyDown;
    }

    // ------------------------------------------------------------------- the window --

    private void ShowTargetFolder()
    {
        FolderText.Text = _targetFolder;
        ToolTip.SetTip(FolderText, _targetFolder);
    }

    private void ShowCounts()
    {
        BooksValue.Text = (_project.Books?.Count ?? 0).ToString();

        // "Слотов", not "файлов": in the combined mode the produced set is smaller than the
        // slot count by design - a shared spread is one file - and two numbers with the same
        // word in them read as a contradiction.
        FilesCaption.Text = _project.Mode == AppMode.Combined ? "Слотов" : "Файлов";
        FilesValue.Text = FilesIn(_project).ToString();
    }

    private static int FilesIn(Project project) =>
        project.Books?.Sum(b =>
        {
            int n = 0;
            if (b.Cover is { IsEmpty: false }) n++;
            if (b.Pages is not null) n += b.Pages.Count(p => !p.IsCover && !p.IsEmpty);
            return n;
        }) ?? 0;

    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        if (_running) return;

        Close();
        e.Handled = true;
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e)
    {
        if (!_running) Close();
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        if (!_running) Close();
    }

    // -------------------------------------------------------------------- actions --

    /// <summary>
    /// Choosing a parent does not choose the folder.
    /// </summary>
    /// <remarks>
    /// The export still gets its own new folder inside whatever was chosen. That is the whole
    /// point of the rule, and it is why this button cannot be a way to write straight over a
    /// previous run.
    /// </remarks>
    private async void OnBrowseClick(object? sender, RoutedEventArgs e)
    {
        var folders = await PickFoldersAsync();
        if (folders is not { Count: > 0 }) return;

        _targetFolder = ExportFolderNaming.UniqueExportFolder(folders[0]);
        ShowTargetFolder();
    }

    private async Task<List<string>?> PickFoldersAsync()
    {
        var window = Owner as Window ?? MainWindowOf();
        var storage = window?.StorageProvider;
        if (storage is null || !storage.CanOpen) return null;

        var picked = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Выберите папку для экспорта",
            AllowMultiple = false
        });

        return picked.Count == 0
            ? null
            : picked.Select(f => f.TryGetLocalPath()).Where(p => p is not null).Select(p => p!).ToList();
    }

    private async void OnExportClick(object? sender, RoutedEventArgs e) => await RunAsync();

    private async Task RunAsync()
    {
        if (_running) return;

        _running = true;
        Progress.Value = 0;
        ProgressPanel.IsVisible = true;
        ProgressText.Text = string.Empty;
        ExportButton.IsEnabled = false;
        CancelButton.IsEnabled = false;

        try
        {
            var options = new ExportOptions
            {
                PerBookSubfolders = SubfoldersCheck.IsChecked ?? false,
                Progress = new Progress<double>(Report)
            };

            var result = await _export.ExportProjectAsync(_project, _targetFolder, options);

            _succeeded = result.IsSuccess;
            _exportedFolder = result.IsSuccess ? _targetFolder : string.Empty;

            if (result.IsSuccess) Close(_targetFolder);
            else ProgressText.Text = "Не удалось";
        }
        catch (Exception ex)
        {
            _succeeded = false;
            ProgressText.Text = ex.Message;
        }
        finally
        {
            _running = false;
            ExportButton.IsEnabled = true;
            CancelButton.IsEnabled = true;
        }
    }

    private void Report(double value)
    {
        Progress.Value = value;
        ProgressText.Text = $"{value:0}%";
    }

    private static Window? MainWindowOf() =>
        (Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
}
