param(
    [string]$Configuration = "Release",
    [string]$Version = "1.0.0-local",
    [string]$GitHubRepository = "",
    [string[]]$RuntimeIdentifiers = @("win-x64", "linux-x64", "linux-musl-x64", "linux-arm64")
)

$ErrorActionPreference = "Stop"

$projectPath = Join-Path $PSScriptRoot "RtfTableExporter.csproj"
$outputRoot = Join-Path $PSScriptRoot "artifacts\\publish"

foreach ($rid in $RuntimeIdentifiers) {
    $target = Join-Path $outputRoot $rid
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
}
