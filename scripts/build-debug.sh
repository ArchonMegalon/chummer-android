#!/usr/bin/env bash
set -euo pipefail

repo_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
solution_path="$repo_dir/Chummer.Android.slnx"
project_path="$repo_dir/src/Chummer.Android/Chummer.Android.csproj"
compile_check_path="$repo_dir/tests/Chummer.Android.Native.CompileCheck/Chummer.Android.Native.CompileCheck.csproj"
interaction_tests_path="$repo_dir/tests/Chummer.Android.Native.InteractionTests/Chummer.Android.Native.InteractionTests.csproj"
play_review_tests_path="$repo_dir/tests/Chummer.Android.PlayReview.Tests/Chummer.Android.PlayReview.Tests.csproj"
play_review_binding_check_path="$repo_dir/tests/Chummer.Android.PlayReview.BindingCompileCheck/Chummer.Android.PlayReview.BindingCompileCheck.csproj"
compile_graph_verifier="$repo_dir/scripts/verify_native_compile_graph.py"
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

python3 "$compile_graph_verifier" \
  --repo-root "$repo_dir" \
  --project "$compile_check_path" \
  --require-assets
python3 "$compile_graph_verifier" \
  --repo-root "$repo_dir" \
  --project "$project_path" \
  --assets-only

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

"$dotnet_command" build "$interaction_tests_path" \
  --configuration Debug \
  --no-restore \
  -m:1 \
  --disable-build-servers \
  -p:UseSharedCompilation=false \
  -p:BuildInParallel=false \
  -p:ChummerDesktopRuntimeIdentifiers= \
  -p:ChummerUseLocalCompatibilityTree=true

"$dotnet_command" run \
  --project "$interaction_tests_path" \
  --configuration Debug \
  --no-build \
  --no-restore \
  --disable-build-servers

"$dotnet_command" run \
  --project "$play_review_tests_path" \
  --configuration Debug \
  --no-restore \
  --disable-build-servers

"$dotnet_command" build "$play_review_binding_check_path" \
  --configuration Debug \
  --framework "$framework" \
  --disable-build-servers \
  -p:AndroidSdkDirectory="${AndroidSdkDirectory:-${ANDROID_SDK_ROOT:-${ANDROID_HOME:-}}}" \
  -p:JavaSdkDirectory="${JavaSdkDirectory:-${JAVA_HOME:-}}"
