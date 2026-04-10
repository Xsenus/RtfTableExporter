param(
    [string]$Configuration = "Release",
    [string]$Version = "1.0.14-local",
    [string]$GitHubRepository = "",
    [string[]]$RuntimeIdentifiers = @("win-x64", "win-x86", "linux-x64", "linux-musl-x64", "linux-arm64")
)

$ErrorActionPreference = "Stop"

$projectPath = Join-Path $PSScriptRoot "RtfTableExporter.csproj"
$outputRoot = Join-Path $PSScriptRoot "artifacts\\publish"
$releaseReadmePath = Join-Path $PSScriptRoot "RELEASE_README.md"

foreach ($rid in $RuntimeIdentifiers) {
    $target = Join-Path $outputRoot $rid
    if (Test-Path -LiteralPath $target) {
        Remove-Item -LiteralPath $target -Recurse -Force
    }

    dotnet publish $projectPath `
        -c $Configuration `
        -r $rid `
        --self-contained true `
        -p:PublishSingleFile=true `
        -p:EnableCompressionInSingleFile=true `
        -p:DebugType=None `
        -p:DebugSymbols=false `
        -p:Version=$Version `
        -p:GitHubRepository=$GitHubRepository `
        -o $target

    Copy-Item -LiteralPath $releaseReadmePath -Destination (Join-Path $target "README.md") -Force
}
