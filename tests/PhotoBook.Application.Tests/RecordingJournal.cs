using System;
using System.Collections.Generic;
using PhotoBook.Application;

namespace PhotoBook.Application.Tests
{
    /// <summary>Keeps what the program wrote down, so a test can insist on it.</summary>
    internal sealed class RecordingJournal : IJournal
    {
        internal List<(JournalLevel Level, string Source, string Message)> Entries { get; } = new();

        public void Write(JournalLevel level, string source, string message, Exception? error = null) =>
            Entries.Add((level, source, message));
    }
}
