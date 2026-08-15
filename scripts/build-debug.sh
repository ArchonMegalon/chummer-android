#!/usr/bin/env bash
set -euo pipefail

repo_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
solution_path="$repo_dir/Chummer.Android.slnx"
project_path="$repo_dir/src/Chummer.Android/Chummer.Android.csproj"
compile_check_path="$repo_dir/tests/Chummer.Android.Native.CompileCheck/Chummer.Android.Native.CompileCheck.csproj"
dotnet_command="${CHUMMER_DOTNET:-dotnet}"
framework="net10.0-android36.0"
runtime_identifier="${CHUMMER_ANDROID_RUNTIME_ID:-android-arm64}"

case "$runtime_identifier" in
  android-arm64|android-x64) ;;
  *)
    echo "CHUMMER_ANDROID_RUNTIME_ID must be android-arm64 or android-x64." >&2
    exit 64
    ;;
esac

"$dotnet_command" restore "$solution_path" \
  --disable-parallel \
  -p:ChummerAndroidRuntimeIdentifier="$runtime_identifier" \
  -p:ChummerDesktopRuntimeIdentifiers= \
  -p:ChummerUseLocalCompatibilityTree=true

"$dotnet_command" build "$project_path" \
  --configuration Debug \
  --framework "$framework" \
  --no-restore \
  -m:1 \
  --disable-build-servers \
  -p:UseSharedCompilation=false \
  -p:BuildInParallel=false \
  -p:ChummerAndroidRuntimeIdentifier="$runtime_identifier" \
  -p:ChummerDesktopRuntimeIdentifiers= \
  -p:ChummerUseLocalCompatibilityTree=true

"$dotnet_command" build "$compile_check_path" \
  --configuration Debug \
  --no-restore \
  -m:1 \
  --disable-build-servers \
  -p:UseSharedCompilation=false \
  -p:BuildInParallel=false \
  -p:ChummerDesktopRuntimeIdentifiers= \
  -p:ChummerUseLocalCompatibilityTree=true
