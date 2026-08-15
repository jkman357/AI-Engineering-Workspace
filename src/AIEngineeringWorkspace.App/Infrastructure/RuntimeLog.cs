using System.Text;

namespace AIEngineeringWorkspace.Infrastructure;

internal static class RuntimeLog
{
    private static readonly object Sync = new();
    private static StreamWriter? _writer;
    private static string? _path;

    public static string CurrentPath => _path ?? "(not initialized)";

    public static void Initialize()
    {
        lock (Sync)
        {
            if (_writer is not null)
            {
                return;
            }

            var root = FindRepositoryRoot() ?? AppContext.BaseDirectory;
            var logDir = Path.Combine(root, "logs", "runtime");
            Directory.CreateDirectory(logDir);

            var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
            _path = Path.Combine(logDir, $"AIEngineeringWorkspace_{stamp}.log");
            _writer = new StreamWriter(new FileStream(_path, FileMode.CreateNew, FileAccess.Write, FileShare.ReadWrite), new UTF8Encoding(false))
            {
                AutoFlush = true
            };
        }
    }

    public static void Debug(string message) => Write("DEBUG", message);
    public static void Info(string message) => Write("INFO ", message);
    public static void Warn(string message) => Write("WARN ", message);
    public static void Error(string message, Exception? ex = null) => Write("ERROR", FormatException(message, ex));
    public static void Fatal(string message, Exception? ex = null) => Write("FATAL", FormatException(message, ex));

    public static void Shutdown()
    {
        lock (Sync)
        {
            _writer?.Flush();
            _writer?.Dispose();
            _writer = null;
        }
    }

    private static void Write(string level, string message)
    {
        lock (Sync)
        {
            if (_writer is null)
            {
                Initialize();
            }

            var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] [T{Environment.CurrentManagedThreadId:D2}] {message}";
            _writer?.WriteLine(line);
        }
    }

    private static string FormatException(string message, Exception? ex)
    {
        if (ex is null)
        {
            return message;
        }

        return $"{message}{Environment.NewLine}{ex}";
    }

    private static string? FindRepositoryRoot()
    {
        var candidates = new[]
        {
            Directory.GetCurrentDirectory(),
            AppContext.BaseDirectory
        };

        foreach (var start in candidates)
        {
            var dir = new DirectoryInfo(start);
            for (var i = 0; i < 8 && dir is not null; i++, dir = dir.Parent)
            {
                if (File.Exists(Path.Combine(dir.FullName, "AI-Engineering-Workspace.sln")))
                {
                    return dir.FullName;
                }
            }
        }

        return null;
    }
}
