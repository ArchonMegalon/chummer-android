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
presentation_root="$(cd "${CHUMMER_PRESENTATION_ROOT:-$repo_dir/../chummer-presentation}" && pwd -P)"
core_engine_root="$(cd "${CHUMMER_CORE_ENGINE_ROOT:-$presentation_root/../chummer-core-engine}" && pwd -P)"
workspace_root="$(cd "$presentation_root/.." && pwd -P)"
run_services_root="$(cd "$workspace_root/chummer.run-services" && pwd -P)"
hub_registry_root="$(cd "$workspace_root/chummer-hub-registry" && pwd -P)"
ui_kit_root="$(cd "$workspace_root/chummer-ui-kit" && pwd -P)"
media_factory_root="$(cd "$workspace_root/fleet/repos/chummer-media-factory" && pwd -P)"
local_tree_args=(
  "-p:RestorePackagesWithLockFile=true"
  "-p:NuGetLockFilePath=obj/chummer.local-compat.${runtime_identifier}.packages.lock.json"
  "-p:ChummerPresentationRoot=$presentation_root"
  "-p:ChummerCoreEngineRoot=$core_engine_root"
  "-p:ChummerLocalContractsProject=$core_engine_root/Chummer.Contracts/Chummer.Contracts.csproj"
  "-p:ChummerLocalCampaignContractsProject=$run_services_root/Chummer.Campaign.Contracts/Chummer.Campaign.Contracts.csproj"
  "-p:ChummerLocalHubRegistryContractsProject=$hub_registry_root/Chummer.Hub.Registry.Contracts/Chummer.Hub.Registry.Contracts.csproj"
  "-p:ChummerLocalRunContractsProject=$run_services_root/Chummer.Run.Contracts/Chummer.Run.Contracts.csproj"
  "-p:ChummerLocalUiKitProject=$ui_kit_root/src/Chummer.Ui.Kit/Chummer.Ui.Kit.csproj"
  "-p:ChummerLocalMediaContractsProject=$media_factory_root/src/Chummer.Media.Contracts/Chummer.Media.Contracts.csproj"
)

case "$runtime_identifier" in
  android-arm64|android-x64) ;;
  *)
    echo "CHUMMER_ANDROID_RUNTIME_ID must be android-arm64 or android-x64." >&2
    exit 64
    ;;
esac

proof_build_args=()
case "${CHUMMER_API36_PROOF_INSTRUMENTATION:-false}" in
  false) ;;
  true)
    if [[ "$runtime_identifier" != "android-x64" ]]; then
      echo "API-36 proof instrumentation is restricted to android-x64." >&2
      exit 64
    fi
    proof_build_id="${CHUMMER_API36_PROOF_BUILD_ID:?CHUMMER_API36_PROOF_BUILD_ID is required}"
    [[ "$proof_build_id" =~ ^[A-Za-z0-9._-]{1,128}$ ]] || {
      echo "CHUMMER_API36_PROOF_BUILD_ID is not a bounded token." >&2
      exit 64
    }
    if [[ -n "$(git -C "$repo_dir" status --porcelain=v1 --untracked-files=all)" ]]; then
      echo "API-36 proof instrumentation requires a clean Android source tree." >&2
      exit 1
    fi
    proof_source_commit="$(git -C "$repo_dir" rev-parse HEAD)"
    proof_source_tree="$(git -C "$repo_dir" rev-parse 'HEAD^{tree}')"
    proof_gate_sha256="$(sha256sum "$repo_dir/eng/api36-sr5-wizard-gate-authority.json" | cut -d ' ' -f 1)"
    proof_build_args=(
      "-p:ChummerApi36ProofInstrumentation=true"
      "-p:ChummerApi36ProofSourceCommit=$proof_source_commit"
      "-p:ChummerApi36ProofSourceTree=$proof_source_tree"
      "-p:ChummerApi36ProofGateContractSha256=$proof_gate_sha256"
      "-p:ChummerApi36ProofBuildId=$proof_build_id"
    )
    ;;
  *)
    echo "CHUMMER_API36_PROOF_INSTRUMENTATION must be true or false." >&2
    exit 64
    ;;
esac

"$dotnet_command" restore "$solution_path" \
  --disable-parallel \
  -p:ChummerAndroidRuntimeIdentifier="$runtime_identifier" \
  -p:ChummerDesktopRuntimeIdentifiers= \
  -p:ChummerUseLocalCompatibilityTree=true \
  "${proof_build_args[@]}" \
  "${local_tree_args[@]}"

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
  -p:ChummerUseLocalCompatibilityTree=true \
  "${proof_build_args[@]}" \
  "${local_tree_args[@]}"

"$dotnet_command" build "$compile_check_path" \
  --configuration Debug \
  --no-restore \
  -m:1 \
  --disable-build-servers \
  -p:UseSharedCompilation=false \
  -p:BuildInParallel=false \
  -p:ChummerDesktopRuntimeIdentifiers= \
  -p:ChummerUseLocalCompatibilityTree=true \
  "${local_tree_args[@]}"

"$dotnet_command" build "$interaction_tests_path" \
  --configuration Debug \
  --no-restore \
  -m:1 \
  --disable-build-servers \
  -p:UseSharedCompilation=false \
  -p:BuildInParallel=false \
  -p:ChummerDesktopRuntimeIdentifiers= \
  -p:ChummerUseLocalCompatibilityTree=true \
  "${local_tree_args[@]}"

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
  --disable-build-servers \
  -p:ChummerUseLocalCompatibilityTree=true \
  "${local_tree_args[@]}"

"$dotnet_command" build "$play_review_binding_check_path" \
  --configuration Debug \
  --framework "$framework" \
  --disable-build-servers \
  -p:ChummerUseLocalCompatibilityTree=true \
  "${local_tree_args[@]}" \
  -p:AndroidSdkDirectory="${AndroidSdkDirectory:-${ANDROID_SDK_ROOT:-${ANDROID_HOME:-}}}" \
  -p:JavaSdkDirectory="${JavaSdkDirectory:-${JAVA_HOME:-}}"
