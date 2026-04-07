namespace RtfTableExporter;

internal sealed class CliOptions
{
    public bool ShowHelp { get; init; }

    public required IReadOnlyList<string> Inputs { get; init; }

    public string? OutputPath { get; init; }

    public required string Delimiter { get; init; }

    public required TextFileEncodingKind OutputEncoding { get; init; }

    public bool DisableAutoUpdate { get; init; }

    public string? GitHubRepositoryOverride { get; init; }

    public static CliOptions Parse(string[] args)
    {
        var inputs = new List<string>();
        string? outputPath = null;
        var delimiter = "|";
        var outputEncoding = TextFileEncodingKind.Cp1251;
        var disableAutoUpdate = false;
        string? gitHubRepositoryOverride = null;

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            switch (arg)
            {
                case "-h":
                case "--help":
                    return new CliOptions
                    {
                        ShowHelp = true,
                        Inputs = Array.Empty<string>(),
                        Delimiter = delimiter,
                        OutputEncoding = outputEncoding,
                    };

                case "-i":
                case "--input":
                    inputs.Add(ReadValue(args, ref i, arg));
                    break;

                case "-o":
                case "--output":
                case "--output-dir":
                    outputPath = ReadValue(args, ref i, arg);
                    break;

                case "-d":
                case "--delimiter":
                case "--separator":
                    delimiter = ParseDelimiter(ReadValue(args, ref i, arg));
                    break;

                case "--tab":
                    delimiter = "\t";
                    break;

                case "--encoding":
                case "--output-encoding":
                    outputEncoding = ParseOutputEncoding(ReadValue(args, ref i, arg));
                    break;

                case "--foxpro":
                    outputEncoding = TextFileEncodingKind.Cp1251;
                    break;

                case "--github-repo":
                    gitHubRepositoryOverride = ReadValue(args, ref i, arg);
                    break;

                case "--no-update-check":
                    disableAutoUpdate = true;
                    break;

                case "--largest-table":
                    break;

                case "--all-tables":
                case "--table-index":
                    throw new CliException("This build exports the income and expense sections only. --all-tables and --table-index are not supported.");

                default:
                    if (arg.StartsWith("-", StringComparison.Ordinal))
                    {
                        throw new CliException($"Unknown option: {arg}");
                    }

                    inputs.Add(arg);
                    break;
            }
        }

        return new CliOptions
        {
            Inputs = inputs,
            OutputPath = outputPath,
            Delimiter = delimiter,
            OutputEncoding = outputEncoding,
            DisableAutoUpdate = disableAutoUpdate,
            GitHubRepositoryOverride = gitHubRepositoryOverride,
        };
    }

    private static string ReadValue(string[] args, ref int index, string optionName)
    {
        if (index + 1 >= args.Length)
        {
            throw new CliException($"Missing value for {optionName}.");
        }

        index++;
        return args[index];
    }

    private static string ParseDelimiter(string rawValue)
    {
        if (string.IsNullOrEmpty(rawValue))
        {
            throw new CliException("Delimiter cannot be empty.");
        }

        if (string.Equals(rawValue, "tab", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(rawValue, "\\t", StringComparison.Ordinal))
        {
            return "\t";
        }

        var builder = new System.Text.StringBuilder(rawValue.Length);
        for (var i = 0; i < rawValue.Length; i++)
        {
            var current = rawValue[i];
            if (current != '\\' || i == rawValue.Length - 1)
            {
                builder.Append(current);
                continue;
            }

            i++;
            builder.Append(rawValue[i] switch
            {
                't' => '\t',
                'r' => '\r',
                'n' => '\n',
                '\\' => '\\',
                _ => rawValue[i],
            });
        }

        if (builder.Length == 0)
        {
            throw new CliException("Delimiter cannot be empty.");
        }

        return builder.ToString();
    }

    private static TextFileEncodingKind ParseOutputEncoding(string rawValue)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            throw new CliException("Encoding cannot be empty.");
        }

        return rawValue.Trim().ToLowerInvariant() switch
        {
            "cp1251" => TextFileEncodingKind.Cp1251,
            "1251" => TextFileEncodingKind.Cp1251,
            "windows-1251" => TextFileEncodingKind.Cp1251,
            "ansi" => TextFileEncodingKind.Cp1251,
            "foxpro" => TextFileEncodingKind.Cp1251,
            "utf8" => TextFileEncodingKind.Utf8,
            "utf-8" => TextFileEncodingKind.Utf8,
            "utf8bom" => TextFileEncodingKind.Utf8Bom,
            "utf-8-bom" => TextFileEncodingKind.Utf8Bom,
            "utf8-bom" => TextFileEncodingKind.Utf8Bom,
            _ => throw new CliException($"Unsupported encoding: {rawValue}. Supported values: cp1251, utf8, utf8-bom."),
        };
    }
}

internal sealed class CliException(string message) : Exception(message);
