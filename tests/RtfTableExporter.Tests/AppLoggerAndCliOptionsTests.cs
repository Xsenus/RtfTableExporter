using System.Text;

namespace RtfTableExporter.Tests;

public sealed class AppLoggerAndCliOptionsTests
{
    [Fact]
    public void ParseBootstrapLoggingOptions_RecognizesLogArguments()
    {
        var options = CliOptions.ParseBootstrapLoggingOptions([
            "--log-path",
            "custom.log",
            "--no-file-log",
        ]);

        Assert.True(options.DisableFileLog);
        Assert.Equal("custom.log", options.LogPath);
    }

    [Fact]
    public void Parse_CapturesLoggingOptions()
    {
        var options = CliOptions.Parse([
            "--input",
            "report.rtf",
            "--log-path",
            "logs\\run.log",
            "--no-file-log",
        ]);

        Assert.Single(options.Inputs);
        Assert.Equal("report.rtf", options.Inputs[0]);
        Assert.True(options.DisableFileLog);
        Assert.Equal("logs\\run.log", options.LogPath);
    }

    [Fact]
    public void AppLogger_WritesStructuredFileLog()
    {
        using var sandbox = new TestSandbox();
        string? logPath;

        using (var logger = AppLogger.Create(
                   "RtfTableExporter",
                   sandbox.DirectoryPath,
                   new BootstrapLoggingOptions(DisableFileLog: false, LogPath: null)))
        {
            logger.Info("Application started.", ("pid", 1234), ("args", "--help"));
            logger.Error("Conversion failed.", new InvalidOperationException("boom"), ("inputPath", "report.rtf"));
            logPath = logger.LogPath;
        }

        Assert.False(string.IsNullOrWhiteSpace(logPath));
        Assert.True(File.Exists(logPath));

        var content = File.ReadAllText(logPath!, Encoding.UTF8);
        Assert.Contains("INFO | Application started.", content, StringComparison.Ordinal);
        Assert.Contains("pid=1234", content, StringComparison.Ordinal);
        Assert.Contains("ERROR | Conversion failed.", content, StringComparison.Ordinal);
        Assert.Contains("inputPath=report.rtf", content, StringComparison.Ordinal);
        Assert.Contains("InvalidOperationException: boom", content, StringComparison.Ordinal);
    }

    [Fact]
    public void AppLogger_CanBeDisabled()
    {
        using var sandbox = new TestSandbox();
        using var logger = AppLogger.Create(
            "RtfTableExporter",
            sandbox.DirectoryPath,
            new BootstrapLoggingOptions(DisableFileLog: true, LogPath: null));

        logger.Info("This message should not be written.");

        Assert.False(logger.IsEnabled);
        Assert.Null(logger.LogPath);
        Assert.Empty(Directory.GetFiles(sandbox.DirectoryPath, "*.log", SearchOption.TopDirectoryOnly));
    }
}
