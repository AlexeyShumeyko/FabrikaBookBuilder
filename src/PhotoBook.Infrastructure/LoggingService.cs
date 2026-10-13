using System;
using PhotoBook.Application;
using Microsoft.Extensions.Logging;

namespace PhotoBook.Infrastructure
{
    /// <summary>
    /// The editors record through here. It exists so that no view model writes a file,
    /// and so that all of them reach the same one.
    /// </summary>
    /// <remarks>
    /// The entries go to the journal AND to the console logger. The console is kept because
    /// it is how the program is run from a terminal during development; the journal is
    /// there because a program started from a shortcut has no console at all, which is how
    /// twenty-one failures were recorded nowhere for as long as this existed.
    /// </remarks>
    internal class LoggingService : ILoggingService
    {
        private readonly ILogger<LoggingService> _logger;

        public LoggingService(ILogger<LoggingService> logger)
        {
            _logger = logger;
        }

        public void LogInfo(string message)
        {
            _logger.LogInformation(message);
            Journal.Write(JournalLevel.Information, "editor", message);
        }

        public void LogError(string message, Exception? exception = null)
        {
            _logger.LogError(exception, message);
            Journal.Write(JournalLevel.Error, "editor", message, exception);
        }

        public void LogWarning(string message)
        {
            _logger.LogWarning(message);
            Journal.Write(JournalLevel.Warning, "editor", message);
        }
    }
}
