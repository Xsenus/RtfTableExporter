using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RtfTableExporter;

internal static class GitHubReleaseUpdater
{
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(5);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static async Task TryAutoUpdateAsync(
        CliOptions options,
        AppRuntimeContext context,
        TextWriter log,
        CancellationToken cancellationToken)
    {
        if (options.DisableAutoUpdate || !context.CanSelfUpdate || string.IsNullOrWhiteSpace(context.GitHubRepository))
        {
            return;
        }

        if (!CanWriteToDirectory(context.BaseDirectory))
        {
            await SafeWriteLineAsync(log, $"UPDATE|Application directory is not writable: {context.BaseDirectory}");
            return;
        }

        var statePath = Path.Combine(context.BaseDirectory, ".rtftableexporter-update-state.json");

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(RequestTimeout);

            var latestRelease = await GetLatestReleaseAsync(context.GitHubRepository!, timeout.Token);
            PersistState(statePath, context, latestRelease?.TagName);

            if (latestRelease is null || !IsNewerVersion(latestRelease.TagName, context.CurrentVersion))
            {
                return;
            }

            var asset = SelectAsset(latestRelease, context);
            if (asset is null)
            {
                await SafeWriteLineAsync(log, $"UPDATE|No compatible release asset was found for {DescribeRuntime(context)}.");
                return;
            }

            var scheduled = await DownloadAndScheduleAsync(asset, context, timeout.Token);
            if (scheduled)
            {
                await SafeWriteLineAsync(log, $"UPDATE|Scheduled update to {latestRelease.TagName}. It will be applied after the process exits.");
            }
        }
        catch (OperationCanceledException)
        {
            // Silent timeout to avoid slowing normal use.
        }
        catch (Exception ex)
        {
            AppendUpdateLog(context.BaseDirectory, $"[{DateTimeOffset.UtcNow:O}] {ex}");
            await SafeWriteLineAsync(log, $"UPDATE|{ex.Message}");
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

    private static async Task<bool> DownloadAndScheduleAsync(
        GitHubAsset asset,
        AppRuntimeContext context,
        CancellationToken cancellationToken)
    {
        var processPath = context.ProcessPath;
        if (string.IsNullOrWhiteSpace(processPath))
        {
            return false;
        }

        var tempRoot = Path.Combine(Path.GetTempPath(), $"{context.ReleasePackageId}-update-{Guid.NewGuid():N}");
        var archivePath = Path.Combine(tempRoot, asset.Name);
        var extractPath = Path.Combine(tempRoot, "package");
        Directory.CreateDirectory(extractPath);

        using (var client = new HttpClient())
        {
            client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("RtfTableExporter", "1.0"));
            await using var source = await client.GetStreamAsync(asset.BrowserDownloadUrl, cancellationToken);
            await using var destination = File.Create(archivePath);
            await source.CopyToAsync(destination, cancellationToken);
        }

        ZipFile.ExtractToDirectory(archivePath, extractPath, overwriteFiles: true);

        var targetBinaryName = Path.GetFileName(processPath);
        if (string.IsNullOrWhiteSpace(targetBinaryName))
        {
            return false;
        }

        var sourceBinaryName = ResolveSourceBinaryName(extractPath, targetBinaryName, context.ApplicationName);
        if (string.IsNullOrWhiteSpace(sourceBinaryName))
        {
            throw new InvalidOperationException(
                $"Release asset {asset.Name} does not contain {targetBinaryName} or {GetCanonicalBinaryName(context.ApplicationName)}.");
        }

        var logPath = Path.Combine(context.BaseDirectory, ".rtftableexporter-update.log");
        if (OperatingSystem.IsWindows())
        {
            var scriptPath = Path.Combine(tempRoot, "apply-update.ps1");
            await File.WriteAllTextAsync(
                scriptPath,
                BuildWindowsScript(extractPath, context.BaseDirectory, Process.GetCurrentProcess().Id, sourceBinaryName, targetBinaryName, logPath),
                cancellationToken);
            StartDetachedProcess("powershell", $"-NoProfile -ExecutionPolicy Bypass -File \"{scriptPath}\"");
            return true;
        }

        var shellScriptPath = Path.Combine(tempRoot, "apply-update.sh");
        await File.WriteAllTextAsync(
            shellScriptPath,
            BuildLinuxScript(extractPath, context.BaseDirectory, Process.GetCurrentProcess().Id, sourceBinaryName, targetBinaryName, logPath),
            cancellationToken);
        StartDetachedProcess("/bin/sh", $"\"{shellScriptPath}\"");
        return true;
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

while kill -0 "$PID_TO_WAIT" 2>/dev/null; do
  sleep 1
done

mkdir -p "$TARGET" 2>>"$LOG_FILE"
if [ ! -f "$SOURCE_BINARY" ]; then
  printf '%s\n' "Release binary was not found: $SOURCE_BINARY" >>"$LOG_FILE"
  exit 1
fi

cp -f "$SOURCE_BINARY" "$TARGET_BINARY" 2>>"$LOG_FILE"
chmod +x "$TARGET_BINARY" 2>>"$LOG_FILE"
if [ -f "$SOURCE/README.md" ]; then
  cp -f "$SOURCE/README.md" "$TARGET/README.md" 2>>"$LOG_FILE"
fi
rm -rf "$(dirname "$SOURCE")" >/dev/null 2>&1
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
