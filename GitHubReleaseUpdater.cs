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
                await SafeWriteLineAsync(log, $"UPDATE|No compatible release asset was found for {context.RuntimeIdentifier}.");
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
        var exactName = $"{context.ReleasePackageId}-{release.TagName}-{context.RuntimeIdentifier}.zip";
        return release.Assets.FirstOrDefault(asset => string.Equals(asset.Name, exactName, StringComparison.OrdinalIgnoreCase))
               ?? release.Assets.FirstOrDefault(asset =>
                   asset.Name.EndsWith($"-{context.RuntimeIdentifier}.zip", StringComparison.OrdinalIgnoreCase));
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
        if (string.IsNullOrWhiteSpace(targetBinaryName) ||
            !File.Exists(Path.Combine(extractPath, targetBinaryName)))
        {
            throw new InvalidOperationException($"Release asset {asset.Name} does not contain {targetBinaryName}.");
        }

        var logPath = Path.Combine(context.BaseDirectory, ".rtftableexporter-update.log");
        if (OperatingSystem.IsWindows())
        {
            var scriptPath = Path.Combine(tempRoot, "apply-update.ps1");
            await File.WriteAllTextAsync(scriptPath, BuildWindowsScript(extractPath, context.BaseDirectory, Process.GetCurrentProcess().Id, targetBinaryName, logPath), cancellationToken);
            StartDetachedProcess("powershell", $"-NoProfile -ExecutionPolicy Bypass -File \"{scriptPath}\"");
            return true;
        }

        var shellScriptPath = Path.Combine(tempRoot, "apply-update.sh");
        await File.WriteAllTextAsync(shellScriptPath, BuildLinuxScript(extractPath, context.BaseDirectory, Process.GetCurrentProcess().Id, targetBinaryName, logPath), cancellationToken);
        StartDetachedProcess("/bin/sh", $"\"{shellScriptPath}\"");
        return true;
    }

    private static string BuildWindowsScript(string sourceDirectory, string targetDirectory, int processId, string binaryName, string logPath)
        => $$"""
$ErrorActionPreference = "Stop"
$source = '{{EscapePowerShell(sourceDirectory)}}'
$target = '{{EscapePowerShell(targetDirectory)}}'
$pidToWait = {{processId}}
$log = '{{EscapePowerShell(logPath)}}'

try {
    while (Get-Process -Id $pidToWait -ErrorAction SilentlyContinue) {
        Start-Sleep -Milliseconds 500
    }

    Copy-Item -Path (Join-Path $source '*') -Destination $target -Recurse -Force
    $binary = Join-Path $target '{{EscapePowerShell(binaryName)}}'
    if (Test-Path $binary) {
        [System.IO.File]::SetAttributes($binary, [System.IO.FileAttributes]::Normal)
    }
}
catch {
    Add-Content -LiteralPath $log -Value ('[' + [DateTimeOffset]::UtcNow.ToString('O') + '] ' + $_.Exception.Message)
}
finally {
    try { Remove-Item -LiteralPath (Split-Path -Parent $source) -Recurse -Force -ErrorAction SilentlyContinue } catch {}
}
""";

    private static string BuildLinuxScript(string sourceDirectory, string targetDirectory, int processId, string binaryName, string logPath)
        => $$"""
#!/bin/sh
set +e
SOURCE='{{EscapeShell(sourceDirectory)}}'
TARGET='{{EscapeShell(targetDirectory)}}'
PID_TO_WAIT={{processId}}
LOG_FILE='{{EscapeShell(logPath)}}'
BINARY_NAME='{{EscapeShell(binaryName)}}'

while kill -0 "$PID_TO_WAIT" 2>/dev/null; do
  sleep 1
done

cp -Rf "$SOURCE"/. "$TARGET"/ 2>>"$LOG_FILE"
chmod +x "$TARGET/$BINARY_NAME" 2>>"$LOG_FILE"
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
