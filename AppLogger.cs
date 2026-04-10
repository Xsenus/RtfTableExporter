using System.Globalization;
using System.Text;

namespace RtfTableExporter;

internal sealed class AppLogger : IDisposable
{
    private const long MaxLogFileSizeBytes = 5 * 1024 * 1024;

    private readonly object syncRoot = new();
    private readonly StreamWriter? writer;
    private bool disposed;

    private AppLogger(StreamWriter? writer, string? logPath)
    {
        this.writer = writer;
        LogPath = logPath;
    }

    public string? LogPath { get; }

    public bool IsEnabled => writer is not null;

    public static AppLogger Create(string applicationName, string baseDirectory, BootstrapLoggingOptions options)
    {
        if (options.DisableFileLog)
        {
            return new AppLogger(null, null);
        }

        var logPath = ResolveLogPath(applicationName, baseDirectory, options.LogPath);

        try
        {
            var directory = Path.GetDirectoryName(logPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            RotateIfNeeded(logPath);

            var stream = new FileStream(logPath, FileMode.OpenOrCreate, FileAccess.Write, FileShare.ReadWrite);
            stream.Seek(0, SeekOrigin.End);

            var encoding = stream.Length == 0
                ? new UTF8Encoding(encoderShouldEmitUTF8Identifier: true)
                : new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

            var writer = new StreamWriter(stream, encoding)
            {
                AutoFlush = true,
                NewLine = Environment.NewLine,
            };

            return new AppLogger(writer, logPath);
        }
        catch
        {
            return new AppLogger(null, logPath);
        }
    }

    public void Info(string message, params (string Key, object? Value)[] details)
        => Write("INFO", message, null, details);

    public void Warning(string message, params (string Key, object? Value)[] details)
        => Write("WARN", message, null, details);

    public void Error(string message, Exception? exception = null, params (string Key, object? Value)[] details)
        => Write("ERROR", message, exception, details);

    public static string FormatCommandLine(IReadOnlyList<string> arguments)
        => arguments.Count == 0
            ? string.Empty
            : string.Join(" ", arguments.Select(QuoteArgument));

    public void Dispose()
    {
        lock (syncRoot)
        {
            if (disposed)
            {
                return;
            }

            disposed = true;

            try
            {
                writer?.Dispose();
            }
            catch
            {
                // Ignore log disposal failures.
            }
        }
    }

    private void Write(string level, string message, Exception? exception, IReadOnlyList<(string Key, object? Value)> details)
    {
        lock (syncRoot)
        {
            if (disposed || writer is null)
            {
                return;
            }

            try
            {
                writer.WriteLine(BuildLine(level, message, exception, details));
            }
            catch
            {
                // Never fail the application because of the log.
            }
        }
    }

    private static string ResolveLogPath(string applicationName, string baseDirectory, string? rawLogPath)
    {
        if (string.IsNullOrWhiteSpace(rawLogPath))
        {
            return Path.Combine(baseDirectory, $"{applicationName}.log");
        }

        return Path.GetFullPath(rawLogPath);
    }

    private static void RotateIfNeeded(string logPath)
    {
        try
        {
            var fileInfo = new FileInfo(logPath);
            if (!fileInfo.Exists || fileInfo.Length < MaxLogFileSizeBytes)
            {
                return;
            }

            var backupPath = $"{logPath}.1";
            if (File.Exists(backupPath))
            {
                File.Delete(backupPath);
            }

            File.Move(logPath, backupPath);
        }
        catch
        {
            // Ignore log rotation failures.
        }
    }

    private static string BuildLine(
        string level,
        string message,
        Exception? exception,
        IReadOnlyList<(string Key, object? Value)> details)
    {
        var builder = new StringBuilder();
        builder.Append(DateTimeOffset.Now.ToString("O", CultureInfo.InvariantCulture));
        builder.Append(" | ");
        builder.Append(level);
        builder.Append(" | ");
        builder.Append(Sanitize(message));

        for (var i = 0; i < details.Count; i++)
        {
            var (key, value) = details[i];
            if (string.IsNullOrWhiteSpace(key))
            {
                continue;
            }

            builder.Append(" | ");
            builder.Append(Sanitize(key));
            builder.Append('=');
            builder.Append(Sanitize(FormatValue(value)));
        }

        if (exception is not null)
        {
            builder.Append(" | exception=");
            builder.Append(Sanitize(exception.ToString()));
        }

        return builder.ToString();
    }

    private static string FormatValue(object? value)
        => value switch
        {
            null => "null",
            DateTimeOffset dateTimeOffset => dateTimeOffset.ToString("O", CultureInfo.InvariantCulture),
            DateTime dateTime => dateTime.ToString("O", CultureInfo.InvariantCulture),
            IEnumerable<string> sequence => string.Join(";", sequence.Select(Sanitize)),
            _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty,
        };

    private static string Sanitize(string? value)
        => string.IsNullOrEmpty(value)
            ? string.Empty
            : value
                .Replace("\r", "\\r", StringComparison.Ordinal)
                .Replace("\n", "\\n", StringComparison.Ordinal);

    private static string QuoteArgument(string argument)
    {
        if (string.IsNullOrEmpty(argument))
        {
            return "\"\"";
        }

        if (!argument.Any(char.IsWhiteSpace) &&
            argument.IndexOfAny(['"', '\\']) < 0)
        {
            return argument;
        }

        var escaped = argument.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal);

        return $"\"{escaped}\"";
    }
}

internal sealed record BootstrapLoggingOptions(bool DisableFileLog, string? LogPath);
