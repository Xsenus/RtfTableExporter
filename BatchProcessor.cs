namespace RtfTableExporter;

internal static class BatchProcessor
{
    public static BatchRunResult Run(CliOptions options, AppRuntimeContext context, AppLogger? logger = null)
    {
        logger?.Info("Starting input discovery.");
        var discovery = DiscoverInputFiles(options, context);
        logger?.Info(
            "Input discovery finished.",
            ("fileCount", discovery.Files.Count),
            ("failureCount", discovery.Failures.Count));

        foreach (var failure in discovery.Failures)
        {
            logger?.Warning(
                "Input discovery issue.",
                ("inputPath", failure.InputPath),
                ("message", failure.Message));
        }

        if (discovery.Files.Count == 0)
        {
            logger?.Warning("No supported input files were found.", ("baseDirectory", context.BaseDirectory));
            return new BatchRunResult(
                Array.Empty<FileSuccess>(),
                discovery.Failures,
                NoInputFilesFound: true);
        }

        var outputTarget = ResolveOutputTarget(options.OutputPath, discovery.Files.Count, context.BaseDirectory);
        logger?.Info(
            "Resolved output target.",
            ("outputKind", outputTarget.Kind),
            ("outputPath", outputTarget.Path));
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
                logger?.Info(
                    "Starting file conversion.",
                    ("inputPath", inputFile),
                    ("outputPath", outputPath));
                var result = RtfTableConverter.Convert(inputFile, outputPath, options.Delimiter, options.OutputEncoding);
                successes.Add(new FileSuccess(result.InputPath, result.OutputPath, result.RowCount, result.ColumnCount));
                logger?.Info(
                    "File conversion completed.",
                    ("inputPath", result.InputPath),
                    ("outputPath", result.OutputPath),
                    ("rowCount", result.RowCount),
                    ("columnCount", result.ColumnCount));
            }
            catch (Exception ex)
            {
                failures.Add(new FileFailure(inputFile, outputPath, ex.Message));
                logger?.Error(
                    "File conversion failed.",
                    ex,
                    ("inputPath", inputFile),
                    ("outputPath", outputPath));
            }
        }

        logger?.Info(
            "Batch processing finished.",
            ("successCount", successes.Count),
            ("failureCount", failures.Count));
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
            foreach (var file in EnumerateSupportedInputFiles(context.BaseDirectory))
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
            foreach (var file in EnumerateSupportedInputFiles(fullPath))
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

        if (!RtfTableConverter.IsSupportedInputExtension(fullPath))
        {
            failures.Add(new FileFailure(input, null, "Only .rtf and .docx files are supported."));
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
            .Where(RtfTableConverter.IsSupportedInputExtension)
            .ToList();

        if (matches.Count == 0)
        {
            failures.Add(new FileFailure(input, null, "Wildcard did not match any supported .rtf or .docx files."));
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

    private static IEnumerable<string> EnumerateSupportedInputFiles(string directory)
        => Directory
            .EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly)
            .Where(RtfTableConverter.IsSupportedInputExtension);

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
