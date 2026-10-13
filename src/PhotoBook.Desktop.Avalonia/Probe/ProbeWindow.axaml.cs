using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;

namespace PhotoBook.Shell;

/// <summary>
/// The step 3.0 instrument: can this engine turn a real photograph into a tile fast enough
/// for the product's design to hold. Opened with --probe. Not part of the product.
/// </summary>
public partial class ProbeWindow : Window
{
    /// <summary>Where the real photographs are, for an honest measurement.</summary>
    private static readonly string[] PhotoFolders =
    {
        @"C:\Users\KAIZEN\Desktop\Тесты",
    };

    private readonly long _openedAt = Environment.TickCount64;

    public ProbeWindow()
    {
        InitializeComponent();
    }

    private void OnOpened(object? sender, EventArgs e) => Measure();

    private void OnMeasureClick(object? sender, RoutedEventArgs e) => Measure();

    private void Measure()
    {
        // Recursive: the photographs sit in per-roll subfolders, and a measurement over a
        // folder that happens to be empty is not a measurement.
        var folder = PhotoFolders.FirstOrDefault(f => Directory.Exists(f) && HasJpegs(f));
        if (folder == null)
        {
            Report.Text = "no photograph folder found among:\n  " + string.Join("\n  ", PhotoFolders);
            return;
        }

        // Off the UI thread: a dozen decodes on this thread would freeze the window and make
        // every other number here meaningless.
        var report = Report;
        Task.Run(() => JpegProbe.Run(folder, photos: 12, repeats: 5))
            .ContinueWith(t =>
            {
                var text = $"window shown after {Environment.TickCount64 - _openedAt} ms\n\n"
                           + (t.IsFaulted ? t.Exception?.GetBaseException().Message : t.Result);
                Dispatcher.UIThread.Post(() => report.Text = text);
            });
    }

    private static bool HasJpegs(string folder) =>
        Directory.EnumerateFiles(folder, "*.jpg", SearchOption.AllDirectories).Any();
}