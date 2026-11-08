using System;
using System.IO;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace PhotoBook.Shell.Views;

/// <summary>
/// Says that the files are written, and where they went.
/// </summary>
/// <remarks>
/// <para>
/// It answers which of the two things the person wants next, and the answer is the whole
/// reason it exists: "Открыть папку" hands the result over to the file manager, "Вернуться к
/// проектам" leaves it where it is. Closing the window counts as the second - a window closed
/// from its own corner is not a request to open anything.
/// </para>
/// <para>
/// The count is read from the folder itself rather than from the export's own bookkeeping: the
/// number that finished the run is a number the sampler trusts without checking, and a folder
/// on disk is the one thing that cannot be wrong about what is in it.
/// </para>
/// </remarks>
public partial class ExportSuccessWindow : Window
{
    private readonly string _folder;

    /// <summary>True when the person asked to open the output folder.</summary>
    public bool GoToFolder { get; private set; }

    public ExportSuccessWindow(string folder)
    {
        InitializeComponent();

        _folder = folder;
        PathText.Text = folder;
        SummaryText.Text = BuildSummary(folder);

        KeyDown += OnWindowKeyDown;
    }

    private static string BuildSummary(string folder)
    {
        int files = 0;

        try
        {
            if (Directory.Exists(folder))
                files = Directory.GetFiles(folder, "*.jpg", SearchOption.AllDirectories).Length;
        }
        catch
        {
            // A count is a nicety; never let it break the window.
        }

        return files > 0
            ? $"Готово, {Files(files)}. Папка готова к загрузке на печать."
            : "Все файлы сформированы и готовы к отправке на печать.";
    }

    /// <summary>"1 файл", "4 файла", "11 файлов" - a count that reads as a sentence.</summary>
    private static string Files(int n)
    {
        string word = (n % 100) is >= 11 and <= 14 ? "файлов"
            : (n % 10) == 1 ? "файл"
            : (n % 10) is >= 2 and <= 4 ? "файла"
            : "файлов";

        return $"{n} {word}";
    }

    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) Close();
    }

    private void OnOpenFolderClick(object? sender, RoutedEventArgs e)
    {
        GoToFolder = true;
        Close();
    }

    private void OnReturnClick(object? sender, RoutedEventArgs e) => Close();
}
