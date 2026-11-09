using System;
using System.IO;
using System.Text;
using PhotoBook.Application;

namespace PhotoBook.Infrastructure
{
    /// <summary>
    /// Writes the journal to one file under the user's own profile, and rotates it so it can
    /// never grow without bound.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The location is %LOCALAPPDATA%\PhotoBookRenamer\journal.log, next to the projects. It
    /// is not %TEMP%: Windows is free to empty that, and a report that disappears on a reboot
    /// is worth nothing to support.
    /// </para>
    /// <para>
    /// Rotation keeps the current file and two older ones, so a customer's last few sessions
    /// are always there and the folder never becomes something we have to ask them to clean.
    /// </para>
    /// <para>
    /// Nothing here may throw. A disk that is full, a folder that has been removed, a reader
    /// that has the file open - a program that fails while recording its own failure tells
    /// nobody anything at all.
    /// </para>
    /// </remarks>
    internal sealed class FileJournal : IJournal
    {
        private const long RotateAtBytes = 2 * 1024 * 1024;
        private const int KeptFiles = 2;

        private readonly object _gate = new();
        private readonly string _directory;
        private readonly string _path;
        private readonly bool _verbose;

        public FileJournal(string directory, bool verbose = false)
        {
            _directory = directory;
            _path = Path.Combine(directory, "journal.log");
            _verbose = verbose;
        }

        public void Write(JournalLevel level, string source, string message, Exception? error = null)
        {
            if (!_verbose && level == JournalLevel.Information) return;

            var line = Format(level, source, message, error);

            lock (_gate)
            {
                try
                {
                    Directory.CreateDirectory(_directory);
                    Rotate();
                    File.AppendAllText(_path, line + Environment.NewLine, new UTF8Encoding(false));
                }
                catch
                {
                    // A journal that cannot be written is not worth failing the program over.
                }
            }
        }

        private static string Format(JournalLevel level, string source, string message, Exception? error)
        {
            var text = string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                "{0:yyyy-MM-dd HH:mm:ss.fff}  {1,-11}  {2,-28}  {3}",
                DateTime.Now, level.ToString().ToUpperInvariant(), source, message);

            if (error == null) return text;

            var builder = new StringBuilder(text);
            builder.AppendLine();
            builder.Append("                             ");
            builder.Append(error.GetType().Name).Append(": ").Append(error.Message);

            if (!string.IsNullOrEmpty(error.StackTrace))
            {
                builder.AppendLine();
                foreach (var line in error.StackTrace.Split('\n'))
                {
                    builder.Append("                             ").Append(line.TrimEnd());
                }
            }

            return builder.ToString();
        }

        private void Rotate()
        {
            var info = new FileInfo(_path);
            if (!info.Exists || info.Length < RotateAtBytes) return;

            // "journal-1.log", not "journal.log.1": a customer folder with journal.log next to
            // journal.log.1 reads like a stray copy rather than an older file.
            var oldest = RotatedName(KeptFiles);
            if (File.Exists(oldest)) File.Delete(oldest);

            for (var i = KeptFiles - 1; i >= 0; i--)
            {
                var from = i == 0 ? _path : RotatedName(i);
                var to = RotatedName(i + 1);
                if (File.Exists(from)) File.Move(from, to);
            }
        }

        private string RotatedName(int index) =>
            Path.Combine(_directory, $"journal-{index}.log");
    }
}
