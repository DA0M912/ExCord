using System.IO;

namespace ExCord.Services;

public static class LoggingService
{
    private const long MaxLogBytes = 5L * 1024L * 1024L;
    private static readonly object SyncRoot = new();

    public static string LogPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "ExCord",
        "log.txt");

    public static void Initialize()
    {
        try
        {
            var directory = Path.GetDirectoryName(LogPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            RotateLogIfNeeded();
        }
        catch
        {
        }
    }

    private static void RotateLogIfNeeded()
    {
        try
        {
            if (!File.Exists(LogPath))
            {
                return;
            }

            var logFileInfo = new FileInfo(LogPath);
            if (logFileInfo.Length <= MaxLogBytes)
            {
                return;
            }

            var directory = Path.GetDirectoryName(LogPath);
            if (string.IsNullOrWhiteSpace(directory))
            {
                return;
            }

            var oldLogPath = Path.Combine(directory, "log.old.txt");
            if (File.Exists(oldLogPath))
            {
                File.Delete(oldLogPath);
            }

            File.Move(LogPath, oldLogPath);
        }
        catch
        {
        }
    }

    public static void LogException(Exception ex, string context = "")
    {
        if (ex is null)
        {
            return;
        }

        lock (SyncRoot)
        {
            try
            {
                Initialize();
                var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
                var message = string.IsNullOrWhiteSpace(context)
                    ? $"[{timestamp}] {ex}"
                    : $"[{timestamp}] [{context}] {ex}";

                File.AppendAllText(LogPath, message + Environment.NewLine + ex.StackTrace + Environment.NewLine + Environment.NewLine);
            }
            catch
            {
            }
        }
    }

    public static void LogMessage(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        lock (SyncRoot)
        {
            try
            {
                Initialize();
                var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
                File.AppendAllText(LogPath, $"[{timestamp}] {message}{Environment.NewLine}");
            }
            catch
            {
            }
        }
    }
}
