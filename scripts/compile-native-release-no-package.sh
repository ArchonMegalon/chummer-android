#!/usr/bin/env bash
set -euo pipefail

repo_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
project_path="$repo_dir/src/Chummer.Android/Chummer.Android.csproj"
compile_check_path="$repo_dir/tests/Chummer.Android.Native.CompileCheck/Chummer.Android.Native.CompileCheck.csproj"
interaction_tests_path="$repo_dir/tests/Chummer.Android.Native.InteractionTests/Chummer.Android.Native.InteractionTests.csproj"
play_review_tests_path="$repo_dir/tests/Chummer.Android.PlayReview.Tests/Chummer.Android.PlayReview.Tests.csproj"
play_review_binding_check_path="$repo_dir/tests/Chummer.Android.PlayReview.BindingCompileCheck/Chummer.Android.PlayReview.BindingCompileCheck.csproj"
dotnet_command="${CHUMMER_DOTNET:-dotnet}"
framework="net10.0-android36.0"
runtime_identifier="android-arm64"

require_governed_source_root() {
  local variable="$1" value="${!1:-}" resolved
  [[ -n "$value" && "$value" == /* && ! -L "$value" && -d "$value" ]] || {
    printf '%s must be an explicit absolute non-symlink directory for the governed Release compile.\n' "$variable" >&2
    exit 64
  }
  resolved="$(realpath -e -- "$value")"
  [[ "$resolved" == "$value" ]] || {
    printf '%s must be canonical for the governed Release compile.\n' "$variable" >&2
    exit 64
  }
}

require_governed_source_root CHUMMER_PRESENTATION_ROOT
require_governed_source_root CHUMMER_CORE_ENGINE_ROOT

python3 "$repo_dir/scripts/preflight_native_android_toolchain.py" \
  --repo-root "$repo_dir" \
  --dotnet "$dotnet_command"
python3 "$repo_dir/scripts/verify_native_compile_graph.py" \
  --repo-root "$repo_dir" \
  --project "$compile_check_path"
python3 "$repo_dir/scripts/verify_native_compile_graph.py" \
  --repo-root "$repo_dir" \
  --project "$project_path" \
  --assets-only

"$dotnet_command" build "$interaction_tests_path" \
  --configuration Release \
  --no-restore \
  -m:1 \
  --disable-build-servers \
  -p:UseSharedCompilation=false \
  -p:BuildInParallel=false \
  -p:ChummerDesktopRuntimeIdentifiers= \
  -p:ChummerUseLocalCompatibilityTree=true

"$dotnet_command" run \
  --project "$interaction_tests_path" \
  --configuration Release \
  --no-build \
  --no-restore \
  --disable-build-servers

"$dotnet_command" run \
  --project "$play_review_tests_path" \
  --configuration Release \
  --disable-build-servers

"$dotnet_command" build "$play_review_binding_check_path" \
  --configuration Release \
  --framework "$framework" \
  --disable-build-servers \
  -p:AndroidSdkDirectory="${AndroidSdkDirectory:-${ANDROID_SDK_ROOT:-${ANDROID_HOME:-}}}" \
  -p:JavaSdkDirectory="${JavaSdkDirectory:-${JAVA_HOME:-}}"

set +e
"$dotnet_command" build "$project_path" \
  -t:Compile \
  --configuration Release \
  --framework "$framework" \
  --no-restore \
  -m:1 \
  --disable-build-servers \
  -p:UseSharedCompilation=false \
  -p:BuildInParallel=false \
  -p:ChummerAndroidRuntimeIdentifier="$runtime_identifier" \
  -p:ChummerDesktopRuntimeIdentifiers= \
  -p:ChummerPresentationRoot="$CHUMMER_PRESENTATION_ROOT" \
  -p:ChummerCoreEngineRoot="$CHUMMER_CORE_ENGINE_ROOT" \
  -p:ChummerUseLocalCompatibilityTree=true \
  -p:AndroidSdkDirectory="${AndroidSdkDirectory:-${ANDROID_SDK_ROOT:-${ANDROID_HOME:-}}}" \
  -p:JavaSdkDirectory="${JavaSdkDirectory:-${JAVA_HOME:-}}"
compile_status=$?
set -e
if [[ "$compile_status" -ne 0 ]]; then
  echo "Native Android C# compile failed after the pinned toolchain preflight passed." >&2
  exit "$compile_status"
fi

printf 'Native Android Release Compile target passed; no package was produced.\n'
