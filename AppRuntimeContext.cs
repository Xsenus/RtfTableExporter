using System.Reflection;
using System.Runtime.InteropServices;

namespace RtfTableExporter;

internal sealed record AppRuntimeContext(
    string ApplicationName,
    string BaseDirectory,
    string? ProcessPath,
    string CurrentVersion,
    string? GitHubRepository,
    string RuntimeIdentifier,
    string ObservedRuntimeIdentifier,
    string ReleasePackageId)
{
    public bool CanSelfUpdate =>
        !string.IsNullOrWhiteSpace(ProcessPath) &&
        !string.Equals(Path.GetFileNameWithoutExtension(ProcessPath), "dotnet", StringComparison.OrdinalIgnoreCase);

    public static AppRuntimeContext Create(string? repositoryOverride)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var assemblyName = assembly.GetName().Name ?? "RtfTableExporter";
        var baseDirectory = Path.GetFullPath(AppContext.BaseDirectory);
        var processPath = Environment.ProcessPath;
        var version = ResolveVersion(assembly);
        var repository = ResolveRepository(assembly, repositoryOverride);
        var observedRuntimeIdentifier = ResolveObservedRuntimeIdentifier();
        var runtimeIdentifier = ResolveRuntimeIdentifier(observedRuntimeIdentifier);

        return new AppRuntimeContext(
            assemblyName,
            baseDirectory,
            processPath,
            version,
            repository,
            runtimeIdentifier,
            observedRuntimeIdentifier,
            assemblyName);
    }

    private static string ResolveVersion(Assembly assembly)
    {
        var informationalVersion = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informationalVersion))
        {
            var plusIndex = informationalVersion.IndexOf('+');
            return plusIndex >= 0 ? informationalVersion[..plusIndex] : informationalVersion;
        }

        return assembly.GetName().Version?.ToString() ?? "0.0.0";
    }

    private static string? ResolveRepository(Assembly assembly, string? repositoryOverride)
    {
        foreach (var candidate in new[]
                 {
                     repositoryOverride,
                     Environment.GetEnvironmentVariable("RTF_TABLE_EXPORTER_GITHUB_REPOSITORY"),
                     GetAssemblyMetadata(assembly, "GitHubRepository"),
                     GetAssemblyMetadata(assembly, "RepositoryUrl"),
                 })
        {
            var normalized = NormalizeRepository(candidate);
            if (!string.IsNullOrWhiteSpace(normalized))
            {
                return normalized;
            }
        }

        return null;
    }

    private static string? GetAssemblyMetadata(Assembly assembly, string key)
        => assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(attribute => string.Equals(attribute.Key, key, StringComparison.OrdinalIgnoreCase))
            ?.Value;

    private static string? NormalizeRepository(string? rawValue)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return null;
        }

        var value = rawValue.Trim();
        if (Uri.TryCreate(value, UriKind.Absolute, out var uri))
        {
            if (!string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            value = uri.AbsolutePath.Trim('/');
        }

        var parts = value.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length >= 2 ? $"{parts[0]}/{parts[1]}" : null;
    }

    private static string ResolveObservedRuntimeIdentifier()
    {
        var runtimeIdentifier = RuntimeInformation.RuntimeIdentifier;
        return string.IsNullOrWhiteSpace(runtimeIdentifier) ? "unknown" : runtimeIdentifier;
    }

    private static string ResolveRuntimeIdentifier(string observedRuntimeIdentifier)
    {
        var architecture = ResolveArchitecture();
        if (OperatingSystem.IsWindows())
        {
            return $"win-{architecture}";
        }

        if (OperatingSystem.IsLinux())
        {
            var os = IsMusl(observedRuntimeIdentifier)
                ? "linux-musl"
                : "linux";

            return $"{os}-{architecture}";
        }

        if (!string.IsNullOrWhiteSpace(observedRuntimeIdentifier) &&
            !string.Equals(observedRuntimeIdentifier, "unknown", StringComparison.OrdinalIgnoreCase))
        {
            return observedRuntimeIdentifier;
        }

        return $"unknown-{architecture}";
    }

    private static string ResolveArchitecture()
        => RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 => "x64",
            Architecture.Arm64 => "arm64",
            Architecture.X86 => "x86",
            Architecture.Arm => "arm",
            _ => RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant(),
        };

    private static bool IsMusl(string observedRuntimeIdentifier)
        => OperatingSystem.IsLinux() &&
           observedRuntimeIdentifier.Contains("musl", StringComparison.OrdinalIgnoreCase);
}
