using System.Text;
using System.Text.RegularExpressions;

namespace AIEngineeringWorkspace.Infrastructure;

internal static class RuntimeLog
{
    private const int DefaultRetentionDays = 14;
    private const int DefaultMaxFiles = 50;
    private const int DefaultMaxFileMb = 10;

    private static readonly object Sync = new();
    private static readonly Regex BearerRegex = new(@"(?i)\bBearer\s+[A-Za-z0-9\-._~+/]+=*", RegexOptions.Compiled);
    private static readonly Regex SensitivePairRegex = new(@"(?i)\b(password|passwd|pwd|token|access_token|refresh_token|client_secret|authorization|api_key)\b(\s*[:=]\s*)([^\s;&]+)", RegexOptions.Compiled);
    private static readonly Regex SensitiveQueryRegex = new(@"(?i)([?&](?:password|passwd|pwd|token|access_token|refresh_token|auth|authorization|key|api_key|client_secret)=)[^&#\s]+", RegexOptions.Compiled);

    private static StreamWriter? _writer;
    private static string? _path;
    private static string? _logDirectory;
    private static string? _sessionStamp;
    private static int _part;
    private static bool _initialized;
    private static bool _enabled = true;
    private static int _retentionDays = DefaultRetentionDays;
    private static int _maxFiles = DefaultMaxFiles;
    private static long _maxFileBytes = DefaultMaxFileMb * 1024L * 1024L;

    public static string CurrentPath => !_enabled ? "(runtime logging disabled)" : _path ?? "(not initialized)";
    public static bool IsEnabled => _enabled;

    public static void Initialize()
    {
        lock (Sync)
        {
            if (_initialized)
            {
                return;
            }

            _initialized = true;
            _enabled = ReadEnabled();
            _retentionDays = ReadIntEnvironment("AIEW_LOG_RETENTION_DAYS", DefaultRetentionDays, 1, 3650);
            _maxFiles = ReadIntEnvironment("AIEW_LOG_MAX_FILES", DefaultMaxFiles, 1, 1000);
            var maxFileMb = ReadIntEnvironment("AIEW_LOG_MAX_MB", DefaultMaxFileMb, 1, 1024);
            _maxFileBytes = maxFileMb * 1024L * 1024L;

            if (!_enabled)
            {
                return;
            }

            _logDirectory = ResolveLogDirectory();
            Directory.CreateDirectory(_logDirectory);
            CleanupOldLogs(_logDirectory);

            _sessionStamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
            _part = 0;
            OpenWriter();
            WriteUnlocked("INFO ", $"Runtime logging initialized. Path='{_path}'; RetentionDays={_retentionDays}; MaxFiles={_maxFiles}; MaxFileMB={maxFileMb}; BestEffortSensitiveRedaction=True");
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
            if (!_initialized)
            {
                Initialize();
            }

            if (!_enabled)
            {
                return;
            }

            if (_writer is null)
            {
                OpenWriter();
            }

            if (_writer is not null && _writer.BaseStream.Length >= _maxFileBytes)
            {
                RotateWriter();
            }

            WriteUnlocked(level, message);
        }
    }

    private static void WriteUnlocked(string level, string message)
    {
        var safeMessage = RedactSensitiveText(message);
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] [T{Environment.CurrentManagedThreadId:D2}] {safeMessage}";
        _writer?.WriteLine(line);
    }

    private static void RotateWriter()
    {
        _writer?.Flush();
        _writer?.Dispose();
        _writer = null;
        _part++;
        OpenWriter();
        WriteUnlocked("INFO ", $"Runtime log rotated. Part={_part}; Path='{_path}'");
    }

    private static void OpenWriter()
    {
        if (!_enabled)
        {
            return;
        }

        _logDirectory ??= ResolveLogDirectory();
        Directory.CreateDirectory(_logDirectory);
        _sessionStamp ??= DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");

        var suffix = _part == 0 ? string.Empty : $"_part{_part:D2}";
        _path = Path.Combine(_logDirectory, $"AIEngineeringWorkspace_{_sessionStamp}{suffix}.log");
        _writer = new StreamWriter(new FileStream(_path, FileMode.Create, FileAccess.Write, FileShare.ReadWrite), new UTF8Encoding(false))
        {
            AutoFlush = true
        };
    }

    private static string RedactSensitiveText(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text;
        }

        var redacted = BearerRegex.Replace(text, "Bearer [REDACTED]");
        redacted = SensitivePairRegex.Replace(redacted, match => $"{match.Groups[1].Value}{match.Groups[2].Value}[REDACTED]");
        redacted = SensitiveQueryRegex.Replace(redacted, match => $"{match.Groups[1].Value}[REDACTED]");
        return redacted;
    }

    private static string FormatException(string message, Exception? ex)
        => ex is null ? message : $"{message}{Environment.NewLine}{ex}";

    private static bool ReadEnabled()
    {
        var value = Environment.GetEnvironmentVariable("AIEW_RUNTIME_LOG");
        return !string.Equals(value, "0", StringComparison.OrdinalIgnoreCase) &&
               !string.Equals(value, "false", StringComparison.OrdinalIgnoreCase) &&
               !string.Equals(value, "off", StringComparison.OrdinalIgnoreCase);
    }

    private static int ReadIntEnvironment(string name, int fallback, int min, int max)
    {
        var value = Environment.GetEnvironmentVariable(name);
        return int.TryParse(value, out var parsed) ? Math.Clamp(parsed, min, max) : fallback;
    }

    private static string ResolveLogDirectory()
    {
        var repositoryRoot = FindRepositoryRoot();
        if (!string.IsNullOrWhiteSpace(repositoryRoot))
        {
            return Path.Combine(repositoryRoot, "logs", "runtime");
        }

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localAppData, "AIEngineeringWorkspace", "logs", "runtime");
    }

    private static void CleanupOldLogs(string directory)
    {
        try
        {
            var files = new DirectoryInfo(directory)
                .EnumerateFiles("AIEngineeringWorkspace_*.log", SearchOption.TopDirectoryOnly)
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .ToList();

            var cutoff = DateTime.UtcNow.AddDays(-_retentionDays);
            foreach (var file in files.Where(file => file.LastWriteTimeUtc < cutoff))
            {
                TryDelete(file);
            }

            files = new DirectoryInfo(directory)
                .EnumerateFiles("AIEngineeringWorkspace_*.log", SearchOption.TopDirectoryOnly)
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .ToList();

            foreach (var file in files.Skip(_maxFiles))
            {
                TryDelete(file);
            }
        }
        catch
        {
            // Logging cleanup must never prevent application startup.
        }
    }

    private static void TryDelete(FileInfo file)
    {
        try
        {
            file.Delete();
        }
        catch
        {
            // Best-effort retention cleanup only.
        }
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
