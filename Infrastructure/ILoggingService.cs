namespace PhotoBookRenamer.Infrastructure
{
    public interface ILoggingService
    {
        void LogInfo(string message);
        void LogError(string message, System.Exception? exception = null);
        void LogWarning(string message);
    }
}





