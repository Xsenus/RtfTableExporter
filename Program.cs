using System.Text;

namespace RtfTableExporter;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = new UTF8Encoding(false);
        Console.InputEncoding = new UTF8Encoding(false);
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        try
        {
            var options = CliOptions.Parse(args);
            if (options.ShowHelp)
            {
                WriteUsage();
                return 0;
            }

            var context = AppRuntimeContext.Create(options.GitHubRepositoryOverride);
            var result = BatchProcessor.Run(options, context);

            foreach (var success in result.Successes)
            {
                Console.Out.WriteLine($"SAVED|{success.InputPath}|{success.OutputPath}");
            }

            foreach (var failure in result.Failures)
            {
                var outputPart = string.IsNullOrWhiteSpace(failure.OutputPath)
                    ? string.Empty
                    : $"|{failure.OutputPath}";

                Console.Error.WriteLine($"ERROR|{failure.InputPath}{outputPart}|{failure.Message}");
            }

            if (result.NoInputFilesFound)
            {
                Console.Error.WriteLine($"ERROR|scan|No .rtf files were found in {context.BaseDirectory}");
            }

            Console.Out.WriteLine($"SUMMARY|{result.Successes.Count}|{result.Failures.Count}");

            await GitHubReleaseUpdater.TryAutoUpdateAsync(options, context, Console.Error, CancellationToken.None);
            return result.ExitCode;
        }
        catch (CliException ex)
        {
            Console.Error.WriteLine(ex.Message);
            WriteUsage();
            return 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 4;
        }
    }

    private static void WriteUsage()
    {
        Console.WriteLine("RtfTableExporter");
        Console.WriteLine("Usage:");
        Console.WriteLine("  RtfTableExporter [input1.rtf input2.rtf ...] [--output <dir|file>] [--delimiter \"|\"]");
        Console.WriteLine("  RtfTableExporter --input file1.rtf --input file2.rtf --output out");
        Console.WriteLine("  RtfTableExporter");
        Console.WriteLine();
        Console.WriteLine("Behavior:");
        Console.WriteLine("  - If no input is passed, all .rtf files next to the executable are processed.");
        Console.WriteLine("  - The income and expense sections are exported.");
        Console.WriteLine("  - Column headers and final section totals are skipped.");
        Console.WriteLine("  - Data rows and 'Итого по коду БК' rows are written.");
        Console.WriteLine("  - Existing .txt files are overwritten when possible.");
        Console.WriteLine("  - If one file fails, the rest continue.");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  -i, --input <path>         Add a file, directory, or wildcard mask.");
        Console.WriteLine("  -o, --output <path>        Output directory. For a single file, .txt path is also allowed.");
        Console.WriteLine("  -d, --delimiter <value>    Output separator. Default is | . Use \\t or tab for TAB.");
        Console.WriteLine("      --tab                  Shortcut for TAB separator.");
        Console.WriteLine("      --github-repo <repo>   GitHub repo in owner/name format for self-update.");
        Console.WriteLine("      --no-update-check      Disable GitHub release update check for this run.");
        Console.WriteLine("      --largest-table        Accepted for compatibility, no effect.");
        Console.WriteLine("  -h, --help                 Show help.");
        Console.WriteLine();
        Console.WriteLine("Exit codes:");
        Console.WriteLine("  0 success");
        Console.WriteLine("  1 invalid arguments");
        Console.WriteLine("  2 no input .rtf files found");
        Console.WriteLine("  3 all conversions failed");
        Console.WriteLine("  4 fatal error");
        Console.WriteLine("  5 partial success");
    }
}
