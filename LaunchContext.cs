using System.Text.Json;

namespace RtfTableExporter;

internal sealed record LaunchContext(string[] EffectiveArgs, string WorkingDirectory, string? AcknowledgementPath);

internal sealed record ResumeLaunchState(
    string WorkingDirectory,
    string[] Arguments,
    bool DisableAutoUpdateOnce,
    string? AcknowledgementPath);

internal static class LaunchContextResolver
{
    private const string ResumeStateOptionName = "--resume-state";
    private const string NoUpdateCheckOptionName = "--no-update-check";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static LaunchContext Resolve(string[] args)
    {
        var resumeStatePath = GetResumeStatePath(args);
        if (string.IsNullOrWhiteSpace(resumeStatePath))
        {
            return new LaunchContext(args, Environment.CurrentDirectory, null);
        }

        var state = LoadResumeState(resumeStatePath);
        var effectiveArgs = state.Arguments ?? Array.Empty<string>();
        if (state.DisableAutoUpdateOnce &&
            !effectiveArgs.Any(arg => string.Equals(arg, NoUpdateCheckOptionName, StringComparison.Ordinal)))
        {
            effectiveArgs = effectiveArgs.Concat([NoUpdateCheckOptionName]).ToArray();
        }

        var workingDirectory = string.IsNullOrWhiteSpace(state.WorkingDirectory)
            ? Environment.CurrentDirectory
            : state.WorkingDirectory;

        return new LaunchContext(effectiveArgs, workingDirectory, state.AcknowledgementPath);
    }

    public static void AcknowledgeResume(LaunchContext context)
    {
        if (string.IsNullOrWhiteSpace(context.AcknowledgementPath))
        {
            return;
        }

        var fullPath = Path.GetFullPath(context.AcknowledgementPath);
        var directory = Path.GetDirectoryName(fullPath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new InvalidOperationException($"Acknowledgement file path is invalid: {fullPath}");
        }

        Directory.CreateDirectory(directory);
        File.WriteAllText(fullPath, DateTimeOffset.UtcNow.ToString("O"));
    }

    private static string? GetResumeStatePath(string[] args)
    {
        for (var i = 0; i < args.Length; i++)
        {
            if (!string.Equals(args[i], ResumeStateOptionName, StringComparison.Ordinal))
            {
                continue;
            }

            if (i + 1 >= args.Length)
            {
                throw new InvalidOperationException($"Missing value for {ResumeStateOptionName}.");
            }

            return args[i + 1];
        }

        return null;
    }

    private static ResumeLaunchState LoadResumeState(string statePath)
    {
        var fullPath = Path.GetFullPath(statePath);
        if (!File.Exists(fullPath))
        {
            throw new InvalidOperationException($"Resume state file was not found: {fullPath}");
        }

        var json = File.ReadAllText(fullPath);
        var state = JsonSerializer.Deserialize<ResumeLaunchState>(json, JsonOptions);
        if (state is null)
        {
            throw new InvalidOperationException($"Resume state file is invalid: {fullPath}");
        }

        return state with
        {
            Arguments = state.Arguments ?? Array.Empty<string>(),
        };
    }
}
