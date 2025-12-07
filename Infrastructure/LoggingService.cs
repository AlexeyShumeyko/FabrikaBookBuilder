using System;
using Microsoft.Extensions.Logging;

namespace PhotoBookRenamer.Infrastructure
{
    public class LoggingService : ILoggingService
    {
        private readonly ILogger<LoggingService> _logger;

        public LoggingService(ILogger<LoggingService> logger)
        {
            _logger = logger;
        }

        public void LogInfo(string message)
        {
            _logger.LogInformation(message);
        }

        public void LogError(string message, Exception? exception = null)
        {
            _logger.LogError(exception, message);
        }

        public void LogWarning(string message)
        {
            _logger.LogWarning(message);
        }
    }
}





