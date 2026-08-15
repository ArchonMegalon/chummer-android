#!/usr/bin/env bash
set -euo pipefail

repo_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
project_path="$repo_dir/src/Chummer.Android/Chummer.Android.csproj"
solution_path="$repo_dir/Chummer.Android.slnx"
compile_check_path="$repo_dir/tests/Chummer.Android.Native.CompileCheck/Chummer.Android.Native.CompileCheck.csproj"
framework="net10.0-android36.0"
# The Play release lane is arm64-only. A single RID also keeps the shared
# net10.0 project-reference restore graph deterministic on clean hosts.
runtime_identifier="android-arm64"
dotnet_command="${CHUMMER_DOTNET:-dotnet}"
approval_token="install-android-sdk36-jdk-and-accept-licenses"

if [[ "${CHUMMER_ANDROID_TOOLCHAIN_APPROVAL:-}" != "$approval_token" ]]; then
  echo "Android SDK/JDK installation and Android license acceptance were not explicitly approved." >&2
  echo "Set CHUMMER_ANDROID_TOOLCHAIN_APPROVAL=$approval_token only after approval." >&2
  exit 64
fi

toolchain_input="${CHUMMER_ANDROID_TOOLCHAIN_DIR:-}"
if [[ -z "$toolchain_input" || "$toolchain_input" != /* ]]; then
  echo "CHUMMER_ANDROID_TOOLCHAIN_DIR must be an explicit absolute path outside the repository." >&2
  exit 64
fi
if [[ "$toolchain_input" == "/" || "$toolchain_input" == "$repo_dir" || "$toolchain_input" == "$repo_dir/"* ]]; then
  echo "Refusing broad or repository-contained Android toolchain directory: $toolchain_input" >&2
  exit 64
fi
if [[ -L "$toolchain_input" ]]; then
  echo "Refusing symlinked Android toolchain directory: $toolchain_input" >&2
  exit 64
fi

mkdir -p "$toolchain_input"
toolchain_dir="$(realpath "$toolchain_input")"
if [[ "$toolchain_dir" == "$repo_dir" || "$toolchain_dir" == "$repo_dir/"* ]]; then
  echo "Resolved Android toolchain directory must remain outside the repository: $toolchain_dir" >&2
  exit 64
fi
android_sdk_dir="$toolchain_dir/android-sdk"
java_sdk_dir="$toolchain_dir/microsoft-jdk"
environment_path="$toolchain_dir/chummer-android-toolchain.env"
if [[ -L "$android_sdk_dir" || -L "$java_sdk_dir" ]]; then
  echo "Refusing symlinked Android SDK or Java SDK directory." >&2
  exit 64
fi
if [[ -e "$environment_path" && "${CHUMMER_ANDROID_TOOLCHAIN_REPLACE_ENV:-}" != "replace" ]]; then
  echo "Refusing to replace existing environment file without CHUMMER_ANDROID_TOOLCHAIN_REPLACE_ENV=replace." >&2
  exit 73
fi
if [[ -L "$environment_path" || ( -e "$environment_path" && ! -f "$environment_path" ) ]]; then
  echo "Refusing unsafe Android toolchain environment-file target." >&2
  exit 73
fi
mkdir -p "$android_sdk_dir" "$java_sdk_dir"

"$dotnet_command" restore "$solution_path" \
  --disable-parallel \
  -p:ChummerAndroidRuntimeIdentifier="$runtime_identifier" \
  -p:ChummerDesktopRuntimeIdentifiers= \
  -p:ChummerUseLocalCompatibilityTree=true \
  -p:AndroidSdkDirectory="$android_sdk_dir" \
  -p:JavaSdkDirectory="$java_sdk_dir"

"$dotnet_command" build "$project_path" \
  -t:InstallAndroidDependencies \
  -f "$framework" \
  --no-restore \
  -p:ChummerUseLocalCompatibilityTree=true \
  -p:AndroidSdkDirectory="$android_sdk_dir" \
  -p:JavaSdkDirectory="$java_sdk_dir" \
  -p:AcceptAndroidSDKLicenses=True

"$dotnet_command" build "$project_path" \
  -c Debug \
  -f "$framework" \
  --no-restore \
  -m:1 \
  --disable-build-servers \
  -p:UseSharedCompilation=false \
  -p:BuildInParallel=false \
  -p:ChummerAndroidRuntimeIdentifier="$runtime_identifier" \
  -p:ChummerDesktopRuntimeIdentifiers= \
  -p:ChummerUseLocalCompatibilityTree=true \
  -p:AndroidSdkDirectory="$android_sdk_dir" \
  -p:JavaSdkDirectory="$java_sdk_dir"

"$dotnet_command" build "$compile_check_path" \
  -c Debug \
  --no-restore \
  -m:1 \
  --disable-build-servers \
  -p:UseSharedCompilation=false \
  -p:BuildInParallel=false \
  -p:ChummerDesktopRuntimeIdentifiers= \
  -p:ChummerUseLocalCompatibilityTree=true

umask 077
environment_temp="$environment_path.tmp.$$"
trap 'rm -f "$environment_temp"' EXIT
{
  printf 'export AndroidSdkDirectory=%q\n' "$android_sdk_dir"
  printf 'export JavaSdkDirectory=%q\n' "$java_sdk_dir"
  printf 'export ANDROID_HOME=%q\n' "$android_sdk_dir"
  printf 'export ANDROID_SDK_ROOT=%q\n' "$android_sdk_dir"
  printf 'export JAVA_HOME=%q\n' "$java_sdk_dir"
} > "$environment_temp"
chmod 0600 "$environment_temp"
mv -f "$environment_temp" "$environment_path"
trap - EXIT

printf 'Android build environment is ready and the current arm64 Debug worktree compiled.\n'
printf 'Environment file: %s\n' "$environment_path"
