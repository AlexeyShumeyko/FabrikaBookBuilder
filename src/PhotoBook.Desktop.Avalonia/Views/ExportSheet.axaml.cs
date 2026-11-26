using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Transformation;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
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
/// A panel over the page rather than a window of its own: the page behind it is dimmed and
/// blurred, which is what says the panel is on top and belongs to this program. A window
/// cannot blur what stands behind it, and a frameless white rectangle on a white page had no
/// edge to say it was in front either. Escape and the close button mean cancel, Enter means
/// export, and the answer - the folder written to - is the only thing the panel completes
/// with. The dimming and blurring of the page behind belongs to the shell.
/// </para>
/// <para>
/// What is left in this file is only what changes while the panel is up; the markup is the
/// program's own design system rather than a page of borders written by hand.
/// </para>
/// </remarks>
public partial class ExportSheet : OverlaySheet<string>
{
    private readonly Project _project;
    private readonly IExportService _export;

    private string _targetFolder;
    private bool _running;

    public ExportSheet(Project project, IExportService exportService, string? defaultFolder = null)
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

        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        // Focusable so that Escape and Enter reach it without the person having to click
        // something first. The first attempt usually finds the layer still hidden - the host
        // puts the panel in before it shows the layer - so it is made again once the frame is
        // up; a panel that never takes focus swallows neither key, and the button behind it
        // keeps the focus it was pressed with.
        Focus();

        Dispatcher.UIThread.Post(
            () =>
            {
                Card.RenderTransform = TransformOperations.Parse("translate(0px,0px) scale(1)");
                Card.Opacity = 1;
                Focus();
            },
            DispatcherPriority.Loaded);
    }

    // ------------------------------------------------------------------- the panel --

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

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            if (!_running) Leave();
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            if (!_running) _ = RunAsync();
            e.Handled = true;
        }
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e)
    {
        if (!_running) Leave();
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        if (!_running) Leave();
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
        // The panel lives in the window rather than owning one, so the picker hangs off the
        // top level it was put in - the window this sheet is standing over.
        var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
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
        ErrorPanel.IsVisible = false;
        ErrorText.Text = string.Empty;
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

            if (result.IsSuccess)
            {
                Finish(_targetFolder);
                return;
            }

            // The service answers with a sentence naming the file, the folder or the disk; the
            // panel shows it rather than the word "Не удалось" it used to leave behind.
            ShowError(result.Error ?? "Не удалось выгрузить файлы.");
        }
        catch (Exception ex)
        {
            ShowError("Не удалось выгрузить: " + ex.Message);
        }
        finally
        {
            _running = false;
            ExportButton.IsEnabled = true;
            CancelButton.IsEnabled = true;
        }
    }

    private void ShowError(string reason)
    {
        ProgressPanel.IsVisible = false;
        ErrorText.Text = reason;
        ErrorPanel.IsVisible = true;
    }

    private void Report(double value)
    {
        Progress.Value = value;
        ProgressText.Text = $"{value:0}%";
    }
}
