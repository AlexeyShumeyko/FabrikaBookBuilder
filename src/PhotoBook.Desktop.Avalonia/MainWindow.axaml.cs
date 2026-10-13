using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;

namespace PhotoBook.Shell;

public partial class MainWindow : Window
{
    /// <summary>Where the owner's real photographs are, for an honest measurement.</summary>
    private static readonly string[] PhotoFolders =
    {
        @"C:\Users\KAIZEN\Desktop\Тесты",
    };

    private readonly Stopwatch _startup = Stopwatch.StartNew();

    public MainWindow()
    {
        InitializeComponent();
    }

    private void OnOpened(object? sender, EventArgs e)
    {
        var openedAt = _startup.Elapsed;

        // The window is up. Everything after this is a number, not a question.
        var folder = PhotoFolders.FirstOrDefault(Directory.Exists);
        if (folder == null)
        {
            Report.Text = $"no photograph folder found among:\n  {string.Join("\n  ", PhotoFolders)}";
            return;
        }

        // Off the UI thread: a hundred decodes on this thread would freeze the window and
        // make the measurement of everything else meaningless.
        var report = Report;
        Task.Run(() => JpegProbe.Run(folder, photos: 12, repeats: 5))
            .ContinueWith(t =>
            {
                var text = $"window shown after {openedAt.TotalMilliseconds:F0} ms\n\n"
                           + (t.IsFaulted ? t.Exception?.GetBaseException().Message : t.Result);
                Dispatcher.UIThread.Post(() => report.Text = text);
            });
    }
}