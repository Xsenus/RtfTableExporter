#!/usr/bin/env bash
set -euo pipefail

configuration="${1:-Release}"
version="${2:-1.0.12-local}"
github_repository="${3:-}"
project_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
project_path="$project_dir/RtfTableExporter.csproj"
output_root="$project_dir/artifacts/publish"
release_readme_path="$project_dir/RELEASE_README.md"
rids=("win-x64" "win-x86" "linux-x64" "linux-musl-x64" "linux-arm64")

for rid in "${rids[@]}"; do
  rm -rf "$output_root/$rid"

  dotnet publish "$project_path" \
    -c "$configuration" \
    -r "$rid" \
    --self-contained true \
    -p:PublishSingleFile=true \
    -p:EnableCompressionInSingleFile=true \
    -p:DebugType=None \
    -p:DebugSymbols=false \
    -p:Version="$version" \
    -p:GitHubRepository="$github_repository" \
    -o "$output_root/$rid"

  cp "$release_readme_path" "$output_root/$rid/README.md"
done
