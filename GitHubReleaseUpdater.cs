using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RtfTableExporter;

internal static class GitHubReleaseUpdater
{
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan TakeoverTimeout = TimeSpan.FromSeconds(10);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static async Task<AutoUpdateExecutionResult> TryAutoUpdateAsync(
        CliOptions options,
        AppRuntimeContext context,
        IReadOnlyList<string> restartArguments,
        string workingDirectory,
        TextWriter output,
        TextWriter log,
        AppLogger? logger,
        CancellationToken cancellationToken)
    {
        if (options.DisableAutoUpdate || !context.CanSelfUpdate || string.IsNullOrWhiteSpace(context.GitHubRepository))
        {
            logger?.Info(
                "Auto-update skipped.",
                ("disabledByOption", options.DisableAutoUpdate),
                ("canSelfUpdate", context.CanSelfUpdate),
                ("repositoryConfigured", !string.IsNullOrWhiteSpace(context.GitHubRepository)));
            return AutoUpdateExecutionResult.NotHandled;
        }

        if (!CanWriteToDirectory(context.BaseDirectory))
        {
            logger?.Warning("Auto-update skipped because the application directory is not writable.", ("baseDirectory", context.BaseDirectory));
            await SafeWriteLineAsync(log, $"UPDATE|Application directory is not writable: {context.BaseDirectory}");
            return AutoUpdateExecutionResult.NotHandled;
        }

        var statePath = Path.Combine(context.BaseDirectory, ".rtftableexporter-update-state.json");

        try
        {
            logger?.Info(
                "Checking GitHub release updates.",
                ("repository", context.GitHubRepository ?? string.Empty),
                ("currentVersion", context.CurrentVersion),
                ("runtime", context.RuntimeIdentifier));

            GitHubRelease? latestRelease;
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                timeout.CancelAfter(RequestTimeout);
                latestRelease = await GetLatestReleaseAsync(context.GitHubRepository!, timeout.Token);
            }

            PersistState(statePath, context, latestRelease?.TagName);

            logger?.Info(
                "Latest release lookup completed.",
                ("latestTag", latestRelease?.TagName ?? string.Empty));

            if (latestRelease is null || !IsNewerVersion(latestRelease.TagName, context.CurrentVersion))
            {
                logger?.Info("No newer release is available.");
                return AutoUpdateExecutionResult.NotHandled;
            }

            var asset = SelectAsset(latestRelease, context);
            if (asset is null)
            {
                logger?.Warning(
                    "No compatible update asset was found.",
                    ("latestTag", latestRelease.TagName),
                    ("runtime", DescribeRuntime(context)));
                await SafeWriteLineAsync(log, $"UPDATE|No compatible release asset was found for {DescribeRuntime(context)}.");
                return AutoUpdateExecutionResult.NotHandled;
            }

            logger?.Info(
                "Compatible update asset selected.",
                ("latestTag", latestRelease.TagName),
                ("assetName", asset.Name));
            await SafeWriteLineAsync(log, $"UPDATE|Applying update {latestRelease.TagName} before processing.");
            var updateResult = await DownloadScheduleAndRunUpdatedAsync(
                asset,
                context,
                restartArguments,
                workingDirectory,
                output,
                log,
                logger,
                cancellationToken);

            logger?.Info(
                "Auto-update execution finished.",
                ("handled", updateResult.Handled),
                ("exitCode", updateResult.ExitCode));
            return updateResult;
        }
        catch (OperationCanceledException)
        {
            logger?.Warning("Auto-update lookup timed out.", ("timeoutSeconds", RequestTimeout.TotalSeconds));
            // Silent timeout to avoid slowing normal use.
            return AutoUpdateExecutionResult.NotHandled;
        }
        catch (Exception ex)
        {
            logger?.Error("Auto-update failed.", ex);
            AppendUpdateLog(context.BaseDirectory, $"[{DateTimeOffset.UtcNow:O}] {ex}");
            await SafeWriteLineAsync(log, $"UPDATE|{ex.Message}");
            return AutoUpdateExecutionResult.NotHandled;
        }
    }

    private static void PersistState(string statePath, AppRuntimeContext context, string? lastSeenTag)
    {
        try
        {
            var state = new UpdateState(
                DateTimeOffset.UtcNow,
                context.GitHubRepository,
                context.RuntimeIdentifier,
                context.CurrentVersion,
                lastSeenTag);

            var json = JsonSerializer.Serialize(state, JsonOptions);
            File.WriteAllText(statePath, json);
        }
        catch
        {
            // Ignore state persistence failures.
        }
    }

    private static async Task<GitHubRelease?> GetLatestReleaseAsync(string repository, CancellationToken cancellationToken)
    {
        using var client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("RtfTableExporter", "1.0"));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

        using var response = await client.GetAsync(
            $"https://api.github.com/repos/{repository}/releases/latest",
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonSerializer.DeserializeAsync<GitHubRelease>(stream, JsonOptions, cancellationToken);
    }

    private static bool IsNewerVersion(string releaseTag, string currentVersion)
    {
        if (!TryParseVersion(releaseTag, out var latest) || !TryParseVersion(currentVersion, out var current))
        {
            return false;
        }

        return latest > current;
    }

    private static bool TryParseVersion(string value, out Version version)
    {
        var normalized = value.Trim();
        if (normalized.StartsWith("v", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[1..];
        }

        var dashIndex = normalized.IndexOf('-');
        if (dashIndex >= 0)
        {
            normalized = normalized[..dashIndex];
        }

        return Version.TryParse(normalized, out version!);
    }

    private static GitHubAsset? SelectAsset(GitHubRelease release, AppRuntimeContext context)
    {
        foreach (var runtimeIdentifier in GetRuntimeIdentifierCandidates(context))
        {
            var exactName = $"{context.ReleasePackageId}-{release.TagName}-{runtimeIdentifier}.zip";
            var exactMatch = release.Assets.FirstOrDefault(asset =>
                string.Equals(asset.Name, exactName, StringComparison.OrdinalIgnoreCase));

            if (exactMatch is not null)
            {
                return exactMatch;
            }
        }

        foreach (var runtimeIdentifier in GetRuntimeIdentifierCandidates(context))
        {
            var suffixMatch = release.Assets.FirstOrDefault(asset =>
                asset.Name.EndsWith($"-{runtimeIdentifier}.zip", StringComparison.OrdinalIgnoreCase));

            if (suffixMatch is not null)
            {
                return suffixMatch;
            }
        }

        return null;
    }

    private static async Task<AutoUpdateExecutionResult> DownloadScheduleAndRunUpdatedAsync(
        GitHubAsset asset,
        AppRuntimeContext context,
        IReadOnlyList<string> restartArguments,
        string workingDirectory,
        TextWriter output,
        TextWriter log,
        AppLogger? logger,
        CancellationToken cancellationToken)
    {
        var processPath = context.ProcessPath;
        if (string.IsNullOrWhiteSpace(processPath))
        {
            logger?.Warning("Auto-update skipped because the process path is empty.");
            return AutoUpdateExecutionResult.NotHandled;
        }

        var tempRoot = Path.Combine(Path.GetTempPath(), $"{context.ReleasePackageId}-update-{Guid.NewGuid():N}");
        var archivePath = Path.Combine(tempRoot, asset.Name);
        var extractPath = Path.Combine(tempRoot, "package");
        var resumeStatePath = Path.Combine(tempRoot, "resume-state.json");
        var acknowledgementPath = Path.Combine(tempRoot, "takeover.ok");
        var keepTempRoot = false;

        try
        {
            Directory.CreateDirectory(extractPath);
            logger?.Info("Created temporary update directory.", ("tempRoot", tempRoot));

            await PersistResumeStateAsync(
                resumeStatePath,
                workingDirectory,
                restartArguments,
                acknowledgementPath,
                cancellationToken);
            logger?.Info("Persisted resume state for the updated version.", ("resumeStatePath", resumeStatePath));

            using (var client = new HttpClient())
            {
                client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("RtfTableExporter", "1.0"));
                await using var source = await client.GetStreamAsync(asset.BrowserDownloadUrl, cancellationToken);
                await using var destination = File.Create(archivePath);
                await source.CopyToAsync(destination, cancellationToken);
            }
            logger?.Info("Downloaded update package.", ("archivePath", archivePath), ("assetName", asset.Name));

            ZipFile.ExtractToDirectory(archivePath, extractPath, overwriteFiles: true);
            logger?.Info("Extracted update package.", ("extractPath", extractPath));

            var targetBinaryName = Path.GetFileName(processPath);
            if (string.IsNullOrWhiteSpace(targetBinaryName))
            {
                logger?.Warning("Auto-update skipped because the target binary name could not be resolved.");
                return AutoUpdateExecutionResult.NotHandled;
            }

            var sourceBinaryName = ResolveSourceBinaryName(extractPath, targetBinaryName, context.ApplicationName);
            if (string.IsNullOrWhiteSpace(sourceBinaryName))
            {
                throw new InvalidOperationException(
                    $"Release asset {asset.Name} does not contain {targetBinaryName} or {GetCanonicalBinaryName(context.ApplicationName)}.");
            }

            var updatedBinaryPath = Path.Combine(extractPath, sourceBinaryName);
            EnsureBinaryIsExecutable(updatedBinaryPath);
            logger?.Info(
                "Prepared updated binary for launch.",
                ("updatedBinaryPath", updatedBinaryPath),
                ("targetBinaryName", targetBinaryName));

            var updateResult = await RunUpdatedBinaryAsync(
                updatedBinaryPath,
                resumeStatePath,
                acknowledgementPath,
                workingDirectory,
                output,
                log,
                logger,
                token => SchedulePermanentInstallAsync(
                    extractPath,
                    context.BaseDirectory,
                    Process.GetCurrentProcess().Id,
                    sourceBinaryName,
                    targetBinaryName,
                    log,
                    logger,
                    token),
                cancellationToken);

            keepTempRoot = updateResult.Handled;
            return updateResult;
        }
        finally
        {
            if (!keepTempRoot)
            {
                logger?.Info("Cleaning temporary update directory.", ("tempRoot", tempRoot));
                TryDeleteDirectory(tempRoot);
            }
        }
    }

    private static string BuildWindowsScript(
        string sourceDirectory,
        string targetDirectory,
        int processId,
        string sourceBinaryName,
        string targetBinaryName,
        string logPath)
        => $$"""
$ErrorActionPreference = "Stop"
$source = '{{EscapePowerShell(sourceDirectory)}}'
$target = '{{EscapePowerShell(targetDirectory)}}'
$pidToWait = {{processId}}
$log = '{{EscapePowerShell(logPath)}}'
$sourceBinary = Join-Path $source '{{EscapePowerShell(sourceBinaryName)}}'
$targetBinary = Join-Path $target '{{EscapePowerShell(targetBinaryName)}}'
$sourceReadme = Join-Path $source 'README.md'
$targetReadme = Join-Path $target 'README.md'

try {
    while (Get-Process -Id $pidToWait -ErrorAction SilentlyContinue) {
        Start-Sleep -Milliseconds 500
    }

    New-Item -ItemType Directory -Path $target -Force | Out-Null
    Copy-Item -LiteralPath $sourceBinary -Destination $targetBinary -Force
    if (Test-Path $targetBinary) {
        [System.IO.File]::SetAttributes($targetBinary, [System.IO.FileAttributes]::Normal)
    }

    if (Test-Path $sourceReadme) {
        Copy-Item -LiteralPath $sourceReadme -Destination $targetReadme -Force
    }
}
catch {
    Add-Content -LiteralPath $log -Value ('[' + [DateTimeOffset]::UtcNow.ToString('O') + '] ' + $_.Exception.Message)
}
finally {
    try { Remove-Item -LiteralPath (Split-Path -Parent $source) -Recurse -Force -ErrorAction SilentlyContinue } catch {}
}
""";

    private static string BuildLinuxScript(
        string sourceDirectory,
        string targetDirectory,
        int processId,
        string sourceBinaryName,
        string targetBinaryName,
        string logPath)
        => $$"""
#!/bin/sh
set +e
SOURCE='{{EscapeShell(sourceDirectory)}}'
TARGET='{{EscapeShell(targetDirectory)}}'
PID_TO_WAIT={{processId}}
LOG_FILE='{{EscapeShell(logPath)}}'
SOURCE_BINARY_NAME='{{EscapeShell(sourceBinaryName)}}'
TARGET_BINARY_NAME='{{EscapeShell(targetBinaryName)}}'
SOURCE_BINARY="$SOURCE/$SOURCE_BINARY_NAME"
TARGET_BINARY="$TARGET/$TARGET_BINARY_NAME"
EXIT_CODE=0

while kill -0 "$PID_TO_WAIT" 2>/dev/null; do
  sleep 1
done

mkdir -p "$TARGET" 2>>"$LOG_FILE" || EXIT_CODE=$?
if [ "$EXIT_CODE" -eq 0 ] && [ ! -f "$SOURCE_BINARY" ]; then
  printf '%s\n' "Release binary was not found: $SOURCE_BINARY" >>"$LOG_FILE"
  EXIT_CODE=1
fi

if [ "$EXIT_CODE" -eq 0 ]; then
  cp -f "$SOURCE_BINARY" "$TARGET_BINARY" 2>>"$LOG_FILE" || EXIT_CODE=$?
fi

if [ "$EXIT_CODE" -eq 0 ]; then
  chmod +x "$TARGET_BINARY" 2>>"$LOG_FILE" || EXIT_CODE=$?
fi

if [ "$EXIT_CODE" -eq 0 ] && [ -f "$SOURCE/README.md" ]; then
  cp -f "$SOURCE/README.md" "$TARGET/README.md" 2>>"$LOG_FILE" || EXIT_CODE=$?
fi

rm -rf "$(dirname "$SOURCE")" >/dev/null 2>&1
exit "$EXIT_CODE"
""";

    private static void StartDetachedProcess(string fileName, string arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            CreateNoWindow = true,
            UseShellExecute = false,
            WindowStyle = ProcessWindowStyle.Hidden,
        };

        Process.Start(startInfo);
    }

    private static string EscapePowerShell(string value)
        => value.Replace("'", "''", StringComparison.Ordinal);

    private static string EscapeShell(string value)
        => value.Replace("'", "'\"'\"'", StringComparison.Ordinal);

    private static async Task PersistResumeStateAsync(
        string resumeStatePath,
        string workingDirectory,
        IReadOnlyList<string> restartArguments,
        string acknowledgementPath,
        CancellationToken cancellationToken)
    {
        var state = new ResumeLaunchState(
            string.IsNullOrWhiteSpace(workingDirectory) ? Environment.CurrentDirectory : workingDirectory,
            restartArguments.ToArray(),
            DisableAutoUpdateOnce: true,
            AcknowledgementPath: acknowledgementPath);

        var json = JsonSerializer.Serialize(state, JsonOptions);
        await File.WriteAllTextAsync(resumeStatePath, json, cancellationToken);
    }

    private static async Task<AutoUpdateExecutionResult> RunUpdatedBinaryAsync(
        string updatedBinaryPath,
        string resumeStatePath,
        string acknowledgementPath,
        string workingDirectory,
        TextWriter output,
        TextWriter log,
        AppLogger? logger,
        Func<CancellationToken, Task> schedulePermanentInstallAsync,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = updatedBinaryPath,
            WorkingDirectory = string.IsNullOrWhiteSpace(workingDirectory) ? Environment.CurrentDirectory : workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        startInfo.ArgumentList.Add("--resume-state");
        startInfo.ArgumentList.Add(resumeStatePath);

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Failed to start updated binary: {updatedBinaryPath}");
        logger?.Info(
            "Started updated binary.",
            ("updatedBinaryPath", updatedBinaryPath),
            ("pid", process.Id),
            ("resumeStatePath", resumeStatePath));

        var outputTask = PumpReaderAsync(process.StandardOutput, output, cancellationToken);
        var errorTask = PumpReaderAsync(process.StandardError, log, cancellationToken);

        var takeoverConfirmed = await WaitForTakeoverAsync(process, acknowledgementPath, cancellationToken);
        if (!takeoverConfirmed)
        {
            logger?.Warning("Updated version did not confirm takeover.", ("updatedBinaryPath", updatedBinaryPath));
            TryStopProcess(process);
            await process.WaitForExitAsync(CancellationToken.None);
            await Task.WhenAll(outputTask, errorTask);
            await SafeWriteLineAsync(log, "UPDATE|Updated version did not confirm takeover. Continuing with the current version.");
            return AutoUpdateExecutionResult.NotHandled;
        }

        logger?.Info("Updated version confirmed takeover.", ("acknowledgementPath", acknowledgementPath));
        await schedulePermanentInstallAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        await Task.WhenAll(outputTask, errorTask);
        logger?.Info("Updated binary finished.", ("exitCode", process.ExitCode));
        return new AutoUpdateExecutionResult(Handled: true, ExitCode: process.ExitCode);
    }

    private static async Task PumpReaderAsync(StreamReader reader, TextWriter writer, CancellationToken cancellationToken)
    {
        while (true)
        {
            var line = await reader.ReadLineAsync(cancellationToken);
            if (line is null)
            {
                break;
            }

            await SafeWriteLineAsync(writer, line);
        }
    }

    private static async Task<bool> WaitForTakeoverAsync(Process process, string acknowledgementPath, CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow + TakeoverTimeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (File.Exists(acknowledgementPath))
            {
                return true;
            }

            if (process.HasExited)
            {
                return false;
            }

            await Task.Delay(100, cancellationToken);
        }

        return File.Exists(acknowledgementPath);
    }

    private static void EnsureBinaryIsExecutable(string binaryPath)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        File.SetUnixFileMode(
            binaryPath,
            UnixFileMode.UserRead |
            UnixFileMode.UserWrite |
            UnixFileMode.UserExecute |
            UnixFileMode.GroupRead |
            UnixFileMode.GroupExecute |
            UnixFileMode.OtherRead |
            UnixFileMode.OtherExecute);
    }

    private static void TryStopProcess(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // Ignore cleanup failures.
        }
    }

    private static void TryDeleteDirectory(string directoryPath)
    {
        try
        {
            if (Directory.Exists(directoryPath))
            {
                Directory.Delete(directoryPath, recursive: true);
            }
        }
        catch
        {
            // Ignore cleanup failures.
        }
    }

    private static async Task SchedulePermanentInstallAsync(
        string sourceDirectory,
        string targetDirectory,
        int processId,
        string sourceBinaryName,
        string targetBinaryName,
        TextWriter log,
        AppLogger? logger,
        CancellationToken cancellationToken)
    {
        var logPath = Path.Combine(targetDirectory, ".rtftableexporter-update.log");

        try
        {
            if (OperatingSystem.IsWindows())
            {
                var scriptPath = Path.Combine(Path.GetDirectoryName(sourceDirectory)!, "apply-update.ps1");
                await File.WriteAllTextAsync(
                    scriptPath,
                    BuildWindowsScript(
                        sourceDirectory,
                        targetDirectory,
                        processId,
                        sourceBinaryName,
                        targetBinaryName,
                        logPath),
                    cancellationToken);
                StartDetachedProcess("powershell", $"-NoProfile -ExecutionPolicy Bypass -File \"{scriptPath}\"");
                logger?.Info("Scheduled permanent update installation.", ("scriptPath", scriptPath));
                return;
            }

            var shellScriptPath = Path.Combine(Path.GetDirectoryName(sourceDirectory)!, "apply-update.sh");
            await File.WriteAllTextAsync(
                shellScriptPath,
                    BuildLinuxScript(
                        sourceDirectory,
                        targetDirectory,
                        processId,
                        sourceBinaryName,
                    targetBinaryName,
                        logPath),
                cancellationToken);
            StartDetachedProcess("/bin/sh", $"\"{shellScriptPath}\"");
            logger?.Info("Scheduled permanent update installation.", ("scriptPath", shellScriptPath));
        }
        catch (Exception ex)
        {
            logger?.Error("Failed to schedule permanent update installation.", ex, ("targetDirectory", targetDirectory));
            AppendUpdateLog(targetDirectory, $"[{DateTimeOffset.UtcNow:O}] Failed to schedule permanent update install: {ex}");
            await SafeWriteLineAsync(log, "UPDATE|Failed to schedule permanent installation of the new version.");
        }
    }

    private static IEnumerable<string> GetRuntimeIdentifierCandidates(AppRuntimeContext context)
    {
        yield return context.RuntimeIdentifier;

        if (!string.IsNullOrWhiteSpace(context.ObservedRuntimeIdentifier) &&
            !string.Equals(context.RuntimeIdentifier, context.ObservedRuntimeIdentifier, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(context.ObservedRuntimeIdentifier, "unknown", StringComparison.OrdinalIgnoreCase))
        {
            yield return context.ObservedRuntimeIdentifier;
        }
    }

    private static string DescribeRuntime(AppRuntimeContext context)
        => string.Equals(context.RuntimeIdentifier, context.ObservedRuntimeIdentifier, StringComparison.OrdinalIgnoreCase)
            ? context.RuntimeIdentifier
            : $"{context.RuntimeIdentifier} (reported as {context.ObservedRuntimeIdentifier})";

    private static string? ResolveSourceBinaryName(string extractPath, string targetBinaryName, string applicationName)
        => GetBinaryNameCandidates(targetBinaryName, applicationName)
            .FirstOrDefault(candidate => File.Exists(Path.Combine(extractPath, candidate)));

    private static IEnumerable<string> GetBinaryNameCandidates(string targetBinaryName, string applicationName)
    {
        yield return targetBinaryName;

        var canonicalBinaryName = GetCanonicalBinaryName(applicationName);
        if (!string.Equals(targetBinaryName, canonicalBinaryName, StringComparison.OrdinalIgnoreCase))
        {
            yield return canonicalBinaryName;
        }
    }

    private static string GetCanonicalBinaryName(string applicationName)
        => OperatingSystem.IsWindows()
            ? $"{applicationName}.exe"
            : applicationName;

    private static bool CanWriteToDirectory(string directoryPath)
    {
        try
        {
            Directory.CreateDirectory(directoryPath);
            var probePath = Path.Combine(directoryPath, $".rtftableexporter-write-test-{Guid.NewGuid():N}.tmp");
            using (File.Create(probePath, 1, FileOptions.DeleteOnClose))
            {
            }

            if (File.Exists(probePath))
            {
                File.Delete(probePath);
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    private static async Task SafeWriteLineAsync(TextWriter writer, string message)
    {
        try
        {
            await writer.WriteLineAsync(message);
        }
        catch
        {
            // Ignore logging failures.
        }
    }

    private static void AppendUpdateLog(string baseDirectory, string message)
    {
        try
        {
            var logPath = Path.Combine(baseDirectory, ".rtftableexporter-update.log");
            File.AppendAllText(logPath, $"{message}{Environment.NewLine}");
        }
        catch
        {
            // Ignore log failures.
        }
    }
}

internal sealed record UpdateState(
    DateTimeOffset LastCheckedUtc,
    string? Repository,
    string RuntimeIdentifier,
    string CurrentVersion,
    string? LastSeenTag);

internal readonly record struct AutoUpdateExecutionResult(bool Handled, int ExitCode)
{
    public static AutoUpdateExecutionResult NotHandled => new(false, 0);
}

internal sealed class GitHubRelease
{
    [JsonPropertyName("tag_name")]
    public string TagName { get; set; } = string.Empty;

    [JsonPropertyName("assets")]
    public List<GitHubAsset> Assets { get; set; } = [];
}

internal sealed class GitHubAsset
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("browser_download_url")]
    public string BrowserDownloadUrl { get; set; } = string.Empty;
}
