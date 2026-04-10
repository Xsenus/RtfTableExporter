using System.Text;

namespace RtfTableExporter;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = new UTF8Encoding(false);
        Console.InputEncoding = new UTF8Encoding(false);
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        AppLogger? logger = null;

        try
        {
            var launchContext = LaunchContextResolver.Resolve(args);
            Environment.CurrentDirectory = launchContext.WorkingDirectory;

            var bootstrapContext = AppRuntimeContext.Create(repositoryOverride: null);
            var bootstrapLoggingOptions = CliOptions.ParseBootstrapLoggingOptions(launchContext.EffectiveArgs);
            logger = AppLogger.Create(bootstrapContext.ApplicationName, bootstrapContext.BaseDirectory, bootstrapLoggingOptions);

            logger.Info(
                "Application started.",
                ("pid", Environment.ProcessId),
                ("version", bootstrapContext.CurrentVersion),
                ("runtime", bootstrapContext.RuntimeIdentifier),
                ("workingDirectory", launchContext.WorkingDirectory),
                ("baseDirectory", bootstrapContext.BaseDirectory),
                ("processPath", bootstrapContext.ProcessPath ?? string.Empty),
                ("logPath", logger.LogPath ?? string.Empty),
                ("args", AppLogger.FormatCommandLine(launchContext.EffectiveArgs)));

            var options = CliOptions.Parse(launchContext.EffectiveArgs);
            var context = string.IsNullOrWhiteSpace(options.GitHubRepositoryOverride)
                ? bootstrapContext
                : AppRuntimeContext.Create(options.GitHubRepositoryOverride);

            logger.Info(
                "Command line parsed.",
                ("inputCount", options.Inputs.Count),
                ("outputPath", options.OutputPath ?? string.Empty),
                ("delimiter", options.Delimiter == "\t" ? "\\t" : options.Delimiter),
                ("encoding", options.OutputEncoding),
                ("autoUpdateDisabled", options.DisableAutoUpdate),
                ("fileLogDisabled", options.DisableFileLog),
                ("gitHubRepository", context.GitHubRepository ?? string.Empty));

            LaunchContextResolver.AcknowledgeResume(launchContext);
            if (!string.IsNullOrWhiteSpace(launchContext.AcknowledgementPath))
            {
                logger.Info("Updated version takeover acknowledged.", ("acknowledgementPath", launchContext.AcknowledgementPath));
            }

            if (options.ShowHelp)
            {
                logger.Info("Help requested.");
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
                logger,
                CancellationToken.None);

            if (updateResult.Handled)
            {
                logger.Info("Execution completed by updated version.", ("exitCode", updateResult.ExitCode));
                return updateResult.ExitCode;
            }

            var result = BatchProcessor.Run(options, context, logger);

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
            logger.Info(
                "Application finished.",
                ("exitCode", result.ExitCode),
                ("successCount", result.Successes.Count),
                ("failureCount", result.Failures.Count),
                ("noInputFilesFound", result.NoInputFilesFound));
            return result.ExitCode;
        }
        catch (CliException ex)
        {
            logger?.Error("Invalid command line.", ex, ("args", AppLogger.FormatCommandLine(args)));
            Console.Error.WriteLine(ex.Message);
            WriteUsage();
            return 1;
        }
        catch (Exception ex)
        {
            logger?.Error("Fatal application error.", ex);
            Console.Error.WriteLine(ex.Message);
            return 4;
        }
        finally
        {
            logger?.Dispose();
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
        Console.WriteLine("      --log-path <file>      File log path. Default is RtfTableExporter.log next to the executable.");
        Console.WriteLine("      --no-file-log          Disable file logging for this run.");
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
