namespace RtfTableExporter;

internal static class BatchProcessor
{
    public static BatchRunResult Run(CliOptions options, AppRuntimeContext context)
    {
        var discovery = DiscoverInputFiles(options, context);
        if (discovery.Files.Count == 0)
        {
            return new BatchRunResult(
                Array.Empty<FileSuccess>(),
                discovery.Failures,
                NoInputFilesFound: true);
        }

        var outputTarget = ResolveOutputTarget(options.OutputPath, discovery.Files.Count, context.BaseDirectory);
        EnsureOutputLocationExists(outputTarget);

        var successes = new List<FileSuccess>();
        var failures = new List<FileFailure>(discovery.Failures);

        foreach (var inputFile in discovery.Files)
        {
            var outputPath = outputTarget.Kind == OutputTargetKind.SingleFile
                ? outputTarget.Path
                : Path.Combine(outputTarget.Path, $"{Path.GetFileNameWithoutExtension(inputFile)}.txt");

            try
            {
                var result = RtfTableConverter.Convert(inputFile, outputPath, options.Delimiter, options.OutputEncoding);
                successes.Add(new FileSuccess(result.InputPath, result.OutputPath, result.RowCount, result.ColumnCount));
            }
            catch (Exception ex)
            {
                failures.Add(new FileFailure(inputFile, outputPath, ex.Message));
            }
        }

        return new BatchRunResult(successes, failures, NoInputFilesFound: false);
    }

    private static InputDiscoveryResult DiscoverInputFiles(CliOptions options, AppRuntimeContext context)
    {
        var files = new List<string>();
        var failures = new List<FileFailure>();
        var seen = new HashSet<string>(OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal);

        if (options.Inputs.Count == 0)
        {
            foreach (var file in Directory.EnumerateFiles(context.BaseDirectory, "*.rtf", SearchOption.TopDirectoryOnly))
            {
                if (seen.Add(file))
                {
                    files.Add(file);
                }
            }

            return new InputDiscoveryResult(files, failures);
        }

        foreach (var input in options.Inputs)
        {
            DiscoverInput(input, files, failures, seen);
        }

        return new InputDiscoveryResult(files, failures);
    }

    private static void DiscoverInput(
        string input,
        ICollection<string> files,
        ICollection<FileFailure> failures,
        ISet<string> seen)
    {
        var trimmed = input.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            failures.Add(new FileFailure(input, null, "Input path is empty."));
            return;
        }

        if (HasWildcard(trimmed))
        {
            DiscoverWildcard(trimmed, files, failures, seen);
            return;
        }

        var fullPath = Path.GetFullPath(trimmed);
        if (Directory.Exists(fullPath))
        {
            foreach (var file in Directory.EnumerateFiles(fullPath, "*.rtf", SearchOption.TopDirectoryOnly))
            {
                if (seen.Add(file))
                {
                    files.Add(file);
                }
            }

            return;
        }

        if (!File.Exists(fullPath))
        {
            failures.Add(new FileFailure(input, null, "Input file or directory was not found."));
            return;
        }

        if (!string.Equals(Path.GetExtension(fullPath), ".rtf", StringComparison.OrdinalIgnoreCase))
        {
            failures.Add(new FileFailure(input, null, "Only .rtf files are supported."));
            return;
        }

        if (seen.Add(fullPath))
        {
            files.Add(fullPath);
        }
    }

    private static void DiscoverWildcard(
        string input,
        ICollection<string> files,
        ICollection<FileFailure> failures,
        ISet<string> seen)
    {
        var fullPath = Path.GetFullPath(input);
        var directory = Path.GetDirectoryName(fullPath);
        var fileNameMask = Path.GetFileName(fullPath);

        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            failures.Add(new FileFailure(input, null, "Wildcard directory was not found."));
            return;
        }

        var matches = Directory
            .EnumerateFiles(directory, fileNameMask, SearchOption.TopDirectoryOnly)
            .Where(path => string.Equals(Path.GetExtension(path), ".rtf", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (matches.Count == 0)
        {
            failures.Add(new FileFailure(input, null, "Wildcard did not match any .rtf files."));
            return;
        }

        foreach (var match in matches)
        {
            if (seen.Add(match))
            {
                files.Add(match);
            }
        }
    }

    private static bool HasWildcard(string value)
        => value.IndexOfAny(['*', '?']) >= 0;

    private static OutputTarget ResolveOutputTarget(string? rawOutputPath, int inputCount, string executableDirectory)
    {
        if (string.IsNullOrWhiteSpace(rawOutputPath))
        {
            return new OutputTarget(OutputTargetKind.Directory, executableDirectory);
        }

        var fullPath = Path.GetFullPath(rawOutputPath);
        var directoryExplicit = rawOutputPath.EndsWith(Path.DirectorySeparatorChar) ||
                                rawOutputPath.EndsWith(Path.AltDirectorySeparatorChar);

        var looksLikeFile = !directoryExplicit && !string.IsNullOrWhiteSpace(Path.GetExtension(fullPath));
        if (looksLikeFile)
        {
            if (inputCount != 1)
            {
                throw new CliException("A file output path can be used only when exactly one input file is processed.");
            }

            return new OutputTarget(OutputTargetKind.SingleFile, fullPath);
        }

        return new OutputTarget(OutputTargetKind.Directory, fullPath);
    }

    private static void EnsureOutputLocationExists(OutputTarget target)
    {
        var directory = target.Kind == OutputTargetKind.SingleFile
            ? Path.GetDirectoryName(target.Path)
            : target.Path;

        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new CliException("Output directory could not be resolved.");
        }

        Directory.CreateDirectory(directory);
    }
}

internal sealed record BatchRunResult(
    IReadOnlyList<FileSuccess> Successes,
    IReadOnlyList<FileFailure> Failures,
    bool NoInputFilesFound)
{
    public int ExitCode => NoInputFilesFound
        ? 2
        : Successes.Count == 0 && Failures.Count > 0
            ? 3
            : Successes.Count > 0 && Failures.Count > 0
                ? 5
                : 0;
}

internal sealed record FileSuccess(string InputPath, string OutputPath, int RowCount, int ColumnCount);

internal sealed record FileFailure(string InputPath, string? OutputPath, string Message);

internal sealed record InputDiscoveryResult(IReadOnlyList<string> Files, IReadOnlyList<FileFailure> Failures);

internal enum OutputTargetKind
{
    Directory,
    SingleFile,
}

internal sealed record OutputTarget(OutputTargetKind Kind, string Path);
