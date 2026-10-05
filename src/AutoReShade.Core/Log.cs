using System.Text;

namespace AutoReShade.Core;

/// <summary>Tiny thread-safe file logger. Keeps one file and restarts it when it grows too big.</summary>
public static class Log
{
    private const long MaxFileBytes = 2 * 1024 * 1024;
    private static readonly object Gate = new();
    private static string? _file;

    public static string? FilePath => _file;

    public static void Initialize(string logsDir)
    {
        Directory.CreateDirectory(logsDir);
        lock (Gate)
        {
            _file = Path.Combine(logsDir, "autoreshade.log");
            try
            {
                var info = new FileInfo(_file);
                if (info.Exists && info.Length > MaxFileBytes)
                {
                    File.Copy(_file, Path.Combine(logsDir, "autoreshade.previous.log"), overwrite: true);
                    File.Delete(_file);
                }
            }
            catch (IOException)
            {
                // Logging must never take the app down.
            }
        }
    }

    public static void Info(string message) => Write("INFO", message);
    public static void Warn(string message) => Write("WARN", message);
    public static void Error(string message, Exception? ex = null) =>
        Write("ERROR", ex is null ? message : $"{message}: {ex}");

    private static void Write(string level, string message)
    {
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}{Environment.NewLine}";
        System.Diagnostics.Debug.Write(line);
        lock (Gate)
        {
            if (_file is null) return;
            try
            {
                File.AppendAllText(_file, line, new UTF8Encoding(false));
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
