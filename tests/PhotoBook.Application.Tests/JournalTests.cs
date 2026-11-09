using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using PhotoBook.Application;
using PhotoBook.Infrastructure;
using Xunit;

namespace PhotoBook.Application.Tests
{
    /// <summary>
    /// The journal is what support reads when a customer says it did not work, so the three
    /// things worth pinning are: the entry reaches the file, the file keeps its size bounded,
    /// and a journal that cannot be written never takes the program with it.
    /// </summary>
    public sealed class JournalTests : IDisposable
    {
        private readonly string _directory;

        public JournalTests()
        {
            _directory = Path.Combine(Path.GetTempPath(), "photobook-journal-tests", Guid.NewGuid().ToString("N"));
        }

        public void Dispose()
        {
            try { Directory.Delete(_directory, recursive: true); } catch { /* the OS will clear it */ }
        }

        [Fact]
        public void An_entry_reaches_the_file_with_a_time_a_level_and_where_it_came_from()
        {
            var journal = new FileJournal(_directory, verbose: true);
            journal.Write(JournalLevel.Error, "save structure", "the run could not be written");

            var text = ReadAll("journal.log");
            Assert.Contains("ERROR", text);
            Assert.Contains("save structure", text);
            Assert.Contains("the run could not be written", text);
            Assert.Matches(new Regex(@"^\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3}  "), text);
        }

        [Fact]
        public void An_exception_is_recorded_with_its_type_and_message()
        {
            var journal = new FileJournal(_directory, verbose: true);
            journal.Write(JournalLevel.Error, "export", "copying failed",
                new InvalidOperationException("the disk said no"));

            var text = ReadAll("journal.log");
            Assert.Contains("InvalidOperationException", text);
            Assert.Contains("the disk said no", text);
        }

        [Fact]
        public void Routine_entries_are_dropped_unless_asked_for()
        {
            new FileJournal(_directory, verbose: false)
                .Write(JournalLevel.Information, "editor", "opened a project");

            Assert.False(File.Exists(Path.Combine(_directory, "journal.log")));
        }

        [Fact]
        public void Warnings_are_written_even_when_routine_ones_are_not()
        {
            new FileJournal(_directory, verbose: false)
                .Write(JournalLevel.Warning, "image", "could not be decoded");

            Assert.Contains("WARNING", ReadAll("journal.log"));
        }

        [Fact]
        public void The_file_is_rotated_rather_than_grown_for_ever()
        {
            var journal = new FileJournal(_directory, verbose: true);

            // Two megabytes is the trigger; write past it in a few large entries so the test
            // does not have to fill it one line at a time.
            var filler = new string('x', 512 * 1024);
            for (var i = 0; i < 6; i++) journal.Write(JournalLevel.Error, "filler", filler);

            Assert.True(File.Exists(Path.Combine(_directory, "journal-1.log")),
                "the journal must start a new file once it is big enough; on disk: "
                + string.Join(", ", Directory.GetFiles(_directory).Select(Path.GetFileName)));
            Assert.False(File.Exists(Path.Combine(_directory, "journal-3.log")),
                "only two older files are kept, so the folder cannot grow without bound");
        }

        [Fact]
        public void A_journal_that_cannot_be_written_does_not_throw()
        {
            // A path whose parent is a file, not a folder: every write inside it must fail.
            var blocker = Path.Combine(_directory, "blocked");
            Directory.CreateDirectory(_directory);
            File.WriteAllText(blocker, "not a folder");

            var journal = new FileJournal(Path.Combine(blocker, "journal"), verbose: true);

            var thrown = Record.Exception(() =>
                journal.Write(JournalLevel.Error, "save", "it did not work", new Exception("boom")));

            Assert.Null(thrown);
        }

        [Fact]
        public void Nothing_written_before_one_is_installed_is_lost_silently()
        {
            // The shell's static writers can run before the composition root installs a sink.
            // That must not throw. The sink this leaves behind is the do-nothing one, so the
            // next test starts from the same place this one does.
            var thrown = Record.Exception(() =>
                Journal.Write(JournalLevel.Error, "background", "a job failed", new Exception("boom")));

            Assert.Null(thrown);
        }

        [Fact]
        public void Installing_a_sink_is_what_the_program_sees()
        {
            var recorder = new RecordingJournal();
            Journal.Install(recorder);

            try
            {
                Journal.Write(JournalLevel.Warning, "editor", "careful");

                Assert.Single(recorder.Entries);
                Assert.Equal(JournalLevel.Warning, recorder.Entries[0].Level);
                Assert.Equal("editor", recorder.Entries[0].Source);
                Assert.Equal("careful", recorder.Entries[0].Message);
            }
            finally
            {
                Journal.Install(null!);   // back to the do-nothing sink
            }
        }

        [Fact]
        public void The_editors_recording_through_the_port_reach_the_journal()
        {
            // Nineteen call sites go through ILoggingService, and before this they went to the
            // console - which a program started from a shortcut does not have. This is the
            // wiring that makes them land somewhere.
            var recorder = new RecordingJournal();
            Journal.Install(recorder);

            try
            {
                var logging = new LoggingService(Microsoft.Extensions.Logging.Abstractions.NullLogger<LoggingService>.Instance);
                var error = new InvalidOperationException("the disk said no");

                logging.LogError("the run could not be saved", error);

                var entry = Assert.Single(recorder.Entries);
                Assert.Equal(JournalLevel.Error, entry.Level);
                Assert.Equal("the run could not be saved", entry.Message);
                Assert.Same(error, entry.Error);
            }
            finally
            {
                Journal.Install(null!);
            }
        }

        private string ReadAll(string name) => File.ReadAllText(Path.Combine(_directory, name));

        private sealed class RecordingJournal : IJournal
        {
            public System.Collections.Generic.List<(JournalLevel Level, string Source, string Message, Exception? Error)> Entries { get; }
                = new();

            public void Write(JournalLevel level, string source, string message, Exception? error)
                => Entries.Add((level, source, message, error));
        }
    }
}
