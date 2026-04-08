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
            var launchContext = LaunchContextResolver.Resolve(args);
            Environment.CurrentDirectory = launchContext.WorkingDirectory;

            var options = CliOptions.Parse(launchContext.EffectiveArgs);
            var context = AppRuntimeContext.Create(options.GitHubRepositoryOverride);
            LaunchContextResolver.AcknowledgeResume(launchContext);

            if (options.ShowHelp)
            {
                WriteUsage();
                return 0;
            }

            var updateResult = await GitHubReleaseUpdater.TryAutoUpdateAsync(
                options,
                context,
                launchContext.EffectiveArgs,
                launchContext.WorkingDirectory,
                Console.Out,
                Console.Error,
                CancellationToken.None);

            if (updateResult.Handled)
            {
                return updateResult.ExitCode;
            }

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
                Console.Error.WriteLine($"ERROR|scan|No .rtf or .docx files were found in {context.BaseDirectory}");
            }

            Console.Out.WriteLine($"SUMMARY|{result.Successes.Count}|{result.Failures.Count}");
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
        Console.WriteLine("  RtfTableExporter [input1.rtf input2.docx ...] [--output <dir|file>] [--delimiter \"|\"] [--encoding cp1251]");
        Console.WriteLine("  RtfTableExporter --input file1.rtf --input file2.docx --output out --encoding utf8-bom");
        Console.WriteLine("  RtfTableExporter");
        Console.WriteLine();
        Console.WriteLine("Behavior:");
        Console.WriteLine("  - The update check runs before file processing.");
        Console.WriteLine("  - If an update is installed, the app restarts and continues with the same arguments.");
        Console.WriteLine("  - If the updated version does not confirm takeover, the current version continues processing.");
        Console.WriteLine("  - If no input is passed, all .rtf and .docx files next to the executable are processed.");
        Console.WriteLine("  - Supported report forms are detected automatically: 0531857 and 0503152.");
        Console.WriteLine("  - For 0531857, the income and expense sections are exported.");
        Console.WriteLine("  - For 0503152, the single report table is exported.");
        Console.WriteLine("  - For unknown forms, all meaningful tables are exported as-is.");
        Console.WriteLine("  - Column headers and final section totals are skipped.");
        Console.WriteLine("  - For known forms, data rows are written; 0531857 also keeps 'Итого по коду БК' rows.");
        Console.WriteLine("  - Existing .txt files are overwritten when possible.");
        Console.WriteLine("  - If one file fails, the rest continue.");
        Console.WriteLine("  - Auto-update requires write access to the executable directory.");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  -i, --input <path>         Add a file, directory, or wildcard mask.");
        Console.WriteLine("  -o, --output <path>        Output directory. For a single file, .txt path is also allowed.");
        Console.WriteLine("  -d, --delimiter <value>    Output separator. Default is | . Use \\t or tab for TAB.");
        Console.WriteLine("      --encoding <value>     Output encoding: cp1251, utf8, utf8-bom. Default is cp1251.");
        Console.WriteLine("      --foxpro               Shortcut for --encoding cp1251.");
        Console.WriteLine("      --tab                  Shortcut for TAB separator.");
        Console.WriteLine("      --github-repo <repo>   GitHub repo in owner/name format for self-update.");
        Console.WriteLine("      --no-update-check      Disable GitHub release update check for this run.");
        Console.WriteLine("      --largest-table        Accepted for compatibility, no effect.");
        Console.WriteLine("  -h, --help                 Show help.");
        Console.WriteLine();
        Console.WriteLine("Exit codes:");
        Console.WriteLine("  0 success");
        Console.WriteLine("  1 invalid arguments");
        Console.WriteLine("  2 no input .rtf or .docx files found");
        Console.WriteLine("  3 all conversions failed");
        Console.WriteLine("  4 fatal error");
        Console.WriteLine("  5 partial success");
    }
}
