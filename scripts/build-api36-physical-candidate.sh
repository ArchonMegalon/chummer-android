#!/usr/bin/env bash
set -euo pipefail

repo_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd -P)"
project="$repo_dir/src/Chummer.Android/Chummer.Android.csproj"
lock="$repo_dir/src/Chummer.Android/packages.lock.json"
dotnet_command="${CHUMMER_DOTNET:-dotnet}"
java_command="${CHUMMER_JAVA:-java}"
javac_command="${CHUMMER_JAVAC:-javac}"
android_build_tools_version="${CHUMMER_ANDROID_BUILD_TOOLS_VERSION:-36.0.0}"
current_stage="preflight"
evidence_dir=""
journal=""
raw_journal=""

fail() {
  printf 'api36_physical_candidate=blocked stage=%s retry_performed=false publication_authorized=false\n' "$1" >&2
  exit 2
}

on_exit() {
  local status="$?"
  trap - EXIT HUP INT TERM
  if [[ "$status" -ne 0 && -n "$evidence_dir" && -d "$evidence_dir" ]]; then
    jq -n \
      --arg contractName "chummer.android.api36-arm64-physical-build-provenance/v2" \
      --arg failureStage "$current_stage" \
      '{contractName:$contractName,status:"blocked",publicationAuthorized:false,retryPerformed:false,failureStage:$failureStage}' \
      >"$evidence_dir/blocked.json" 2>/dev/null || true
    chmod 0600 "$evidence_dir/blocked.json" 2>/dev/null || true
  fi
  exit "$status"
}

require_file() {
  local variable="$1" value="${!1:-}"
  [[ -n "$value" && "$value" == /* && -f "$value" && ! -L "$value" ]] \
    || fail "input-$variable-not-absolute-regular"
  [[ "$(realpath -e -- "$value")" == "$value" ]] \
    || fail "input-$variable-not-canonical"
}

require_directory() {
  local variable="$1" value="${!1:-}"
  [[ -n "$value" && "$value" == /* && -d "$value" && ! -L "$value" ]] \
    || fail "input-$variable-not-absolute-directory"
  [[ "$(realpath -e -- "$value")" == "$value" ]] \
    || fail "input-$variable-not-canonical"
}

require_absent_output() {
  local variable="$1" value="${!1:-}" parent
  [[ -n "$value" && "$value" == /* && ! -e "$value" && ! -L "$value" ]] \
    || fail "input-$variable-not-absent-absolute"
  parent="$(dirname -- "$value")"
  [[ -d "$parent" && ! -L "$parent" && "$(realpath -e -- "$parent")" == "$parent" ]] \
    || fail "input-$variable-parent-not-canonical"
}

run_bounded() {
  local phase="$1" output="$2"
  shift 2
  current_stage="$phase"
  "$python_command" "$repo_dir/scripts/materialize-api36-physical-build-provenance.py" run-bounded \
    --journal "$journal" \
    --raw-journal "$raw_journal" \
    --output "$output" \
    --phase "$phase" \
    --timeout-seconds 1800 \
    --deadline-epoch "$deadline_epoch" \
    --working-directory "$repo_dir" \
    "${bounded_environment_arguments[@]}" \
    -- "$@"
}

trap on_exit EXIT
trap 'exit 129' HUP
trap 'exit 130' INT
trap 'exit 143' TERM

for command in cut date dirname find git jq mkdir python3 realpath sha256sum; do
  command -v "$command" >/dev/null 2>&1 || fail "missing-command-$command"
done
command -v "$dotnet_command" >/dev/null 2>&1 || fail "missing-dotnet"
command -v "$java_command" >/dev/null 2>&1 || fail "missing-java"
command -v "$javac_command" >/dev/null 2>&1 || fail "missing-javac"
python_command="$(realpath -e -- "$(command -v python3)")"
dotnet_command="$(realpath -e -- "$(command -v "$dotnet_command")")"
java_command="$(realpath -e -- "$(command -v "$java_command")")"
javac_command="$(realpath -e -- "$(command -v "$javac_command")")"
[[ "$(dirname -- "$java_command")" == "$(dirname -- "$javac_command")" ]] \
  || fail "java-javac-not-one-jdk"

require_directory CHUMMER_RELEASE_WORKSPACE_ROOT
require_directory CHUMMER_PRESENTATION_ROOT
require_directory CHUMMER_CORE_CONTENT_ROOT
require_directory CHUMMER_W5_COMPILE_EVIDENCE
require_directory CHUMMER_INTERNAL_PHONE_BETA_PACKAGE_FEED
require_directory CHUMMER_API36_OFFLINE_NUGET_FEED
require_directory CHUMMER_API36_NUGET_PACKAGES
require_file CHUMMER_RELEASE_PACKAGE_AUTHORITY_V2
require_file CHUMMER_RELEASE_SOURCE_GRAPH
require_file CHUMMER_W5_COMPILE_RECEIPT
require_file CHUMMER_ANDROID_SDK_PACKAGES_XML
require_absent_output CHUMMER_API36_BUILD_PROVENANCE

for forbidden in \
  CHUMMER_CORE_ENGINE_ROOT \
  CHUMMER_RUN_SERVICES_ROOT \
  CHUMMER_HUB_REGISTRY_ROOT \
  CHUMMER_UI_KIT_ROOT \
  CHUMMER_MEDIA_FACTORY_ROOT \
  AndroidSigningKeyStore \
  ChummerAndroidSigningStorePass \
  ChummerAndroidSigningKeyAlias \
  ChummerAndroidSigningKeyPass; do
  [[ -z "${!forbidden:-}" ]] || fail "forbidden-variable-$forbidden"
done

[[ -f "$lock" && ! -L "$lock" ]] || fail "full-project-lock-missing"
[[ "$(sha256sum "$lock" | cut -d' ' -f1)" == "9037d4afc11dd8661dfbcccbc67a9f814d110fb17cf985cf215268e12ae3583e" ]] \
  || fail "full-project-lock-digest-mismatch"
[[ "$($dotnet_command --version)" == "10.0.111" ]] || fail "dotnet-sdk-not-10.0.111"
[[ -z "$(git -C "$repo_dir" status --porcelain=v1 --untracked-files=all)" ]] \
  || fail "android-candidate-not-clean"
[[ "$(git -C "$CHUMMER_PRESENTATION_ROOT" rev-parse HEAD)" == "a8a317aff534dc5fd47f2db1bc39466799021990" ]] \
  || fail "w41-presentation-commit-mismatch"
[[ "$(git -C "$CHUMMER_PRESENTATION_ROOT" rev-parse 'HEAD^{tree}')" == "f8214243280030de5d134351f39ea4b23afbe394" ]] \
  || fail "w41-presentation-tree-mismatch"
[[ -z "$(git -C "$CHUMMER_PRESENTATION_ROOT" status --porcelain=v1 --untracked-files=all)" ]] \
  || fail "w41-presentation-not-clean"
[[ "$(sha256sum "$CHUMMER_PRESENTATION_ROOT/Chummer.Presentation/packages.lock.json" | cut -d' ' -f1)" == "568fd2c602494329d19fbe8d9a2c83a4c2e82754b50e31141b192c1af7ccf964" ]] \
  || fail "w41-presentation-lock-mismatch"
[[ "$(sha256sum "$CHUMMER_PRESENTATION_ROOT/Chummer.Desktop.Runtime/packages.lock.json" | cut -d' ' -f1)" == "202a29a35b4768c3306349ee40a34d8f23ada97c0b0ef11e104763b5ff9cc60e" ]] \
  || fail "w41-desktop-lock-mismatch"
[[ "$(git -C "$CHUMMER_CORE_CONTENT_ROOT" rev-parse HEAD)" == "2fb2ae9bb48e5a1a6b25a174ba88008ce995fcd5" ]] \
  || fail "core-content-commit-mismatch"
[[ -z "$(git -C "$CHUMMER_CORE_CONTENT_ROOT" status --porcelain=v1 --untracked-files=all -- Chummer/data Chummer/lang)" ]] \
  || fail "core-content-not-clean"

evidence_dir="$CHUMMER_API36_BUILD_PROVENANCE.evidence"
[[ ! -e "$evidence_dir" && ! -L "$evidence_dir" ]] || fail "evidence-output-collision"
mkdir -m 0700 -- "$evidence_dir"
journal="$evidence_dir/command-journal.jsonl"
raw_journal="$evidence_dir/raw-command-journal.jsonl"
deadline_epoch="$(( $(date +%s) + 7200 ))"

sdk_root="$(dirname -- "$CHUMMER_ANDROID_SDK_PACKAGES_XML")"
java_home="$(dirname -- "$(dirname -- "$java_command")")"
bounded_environment=(
  "ANDROID_HOME=$sdk_root"
  "DOTNET_CLI_HOME=${DOTNET_CLI_HOME:-$HOME}"
  "DOTNET_CLI_TELEMETRY_OPTOUT=1"
  "DOTNET_CLI_USE_MSBUILD_SERVER=0"
  "HOME=$HOME"
  "JAVA_HOME=$java_home"
  "LANG=C.UTF-8"
  "LC_ALL=C.UTF-8"
  "MSBUILDDISABLENODEREUSE=1"
  "NUGET_PACKAGES=$CHUMMER_API36_NUGET_PACKAGES"
  "PATH=$PATH"
  "TMPDIR=${TMPDIR:-/tmp}"
)
bounded_environment_arguments=()
for environment_entry in "${bounded_environment[@]}"; do
  bounded_environment_arguments+=(--environment "$environment_entry")
done

run_bounded toolchain-intake "$evidence_dir/toolchain.log" \
  "$python_command" "$repo_dir/scripts/materialize-api36-physical-build-provenance.py" \
  capture-workloads --dotnet "$dotnet_command" --output "$evidence_dir/dotnet-workloads.json"

run_bounded source-graph-intake "$evidence_dir/source-graph.log" \
  "$python_command" "$repo_dir/scripts/verify_release_source_graph.py" \
  --android-root "$repo_dir" \
  --presentation-root "$CHUMMER_PRESENTATION_ROOT" \
  --core-content-root "$CHUMMER_CORE_CONTENT_ROOT" \
  --workspace-root "$CHUMMER_RELEASE_WORKSPACE_ROOT" \
  --package-authority "$CHUMMER_RELEASE_PACKAGE_AUTHORITY_V2" \
  --verify-existing "$CHUMMER_RELEASE_SOURCE_GRAPH"

run_bounded core-content-intake "$evidence_dir/content-source.log" \
  "$python_command" "$repo_dir/scripts/verify_android_content_bundle.py" \
  --repo-root "$repo_dir" \
  --core-root "$CHUMMER_CORE_CONTENT_ROOT" \
  --manifest "$repo_dir/src/Chummer.Android/Content/chummer-content-manifest.json" \
  --receipt "$evidence_dir/content-source-receipt.json" \
  --check

run_bounded w5-build-input-intake "$evidence_dir/build-inputs.log" \
  "$python_command" "$repo_dir/scripts/materialize-api36-physical-build-provenance.py" \
  check-inputs \
  --android-root "$repo_dir" \
  --presentation-root "$CHUMMER_PRESENTATION_ROOT" \
  --core-content-root "$CHUMMER_CORE_CONTENT_ROOT" \
  --w5-receipt "$CHUMMER_W5_COMPILE_RECEIPT" \
  --w5-evidence-directory "$CHUMMER_W5_COMPILE_EVIDENCE" \
  --source-graph "$CHUMMER_RELEASE_SOURCE_GRAPH" \
  --package-authority "$repo_dir/eng/internal-phone-beta-package-authority.json" \
  --release-package-authority-v2 "$CHUMMER_RELEASE_PACKAGE_AUTHORITY_V2" \
  --content-source-receipt "$evidence_dir/content-source-receipt.json" \
  --full-project-lock "$lock"

package_args=(
  "-p:ChummerPresentationRoot=$CHUMMER_PRESENTATION_ROOT"
  "-p:ChummerCoreEngineRoot=$CHUMMER_CORE_CONTENT_ROOT"
  "-p:ChummerDesktopRuntimeIdentifiers="
  "-p:ChummerUseLocalCompatibilityTree=false"
  "-p:ChummerUseLockedOwnerContractPackages=true"
  "-p:RestoreLockedMode=true"
  "-p:RestorePackagesWithLockFile=true"
  "-p:NuGetAudit=false"
  "-p:AndroidSdkBuildToolsVersion=$android_build_tools_version"
  "-p:ChummerContractsPackageVersion=0.1.0-packageplane.breaking.shb04ff26f6d538.auth91a48eed5b819"
  "-p:ChummerCoreRuntimePackageVersion=0.1.0-packageplane.breaking.shb04ff26f6d538.auth91a48eed5b819"
  "-p:ChummerCampaignContractsPackageVersion=0.1.0-packageplane.android.sh1215f9389779e"
  "-p:ChummerRunContractsPackageVersion=0.1.0-packageplane.android.sh1215f9389779e"
  "-p:ChummerRunHubContractsPackageVersion=0.1.0-packageplane.android.sh1215f9389779e"
  "-p:ChummerRunHubPackageVersion=0.1.0-packageplane.android.sh1215f9389779e"
  "-p:ChummerHubRegistryContractsPackageVersion=0.1.0-packageplane.candidate.sh66c418a5004f"
  "-p:ChummerUiKitPackageVersion=0.1.0-packageplane.android.shd51ecd99cf720"
)

export DOTNET_CLI_USE_MSBUILD_SERVER=0
export MSBUILDDISABLENODEREUSE=1
export NUGET_PACKAGES="$CHUMMER_API36_NUGET_PACKAGES"

run_bounded locked-full-restore "$evidence_dir/restore.log" \
  "$dotnet_command" restore "$project" \
  --locked-mode \
  --disable-parallel \
  --no-http-cache \
  --packages "$CHUMMER_API36_NUGET_PACKAGES" \
  --source "$CHUMMER_INTERNAL_PHONE_BETA_PACKAGE_FEED" \
  --source "$CHUMMER_API36_OFFLINE_NUGET_FEED" \
  "${package_args[@]}"

apk="$repo_dir/src/Chummer.Android/bin/Debug/net10.0-android36.0/android-arm64/com.myexternalbrain.chummer-Signed.apk"
[[ ! -e "$apk" && ! -L "$apk" ]] || fail "apk-output-must-be-absent-before-build"

run_bounded serialized-full-maui-build "$evidence_dir/build.log" \
  "$dotnet_command" build "$project" \
  --configuration Debug \
  --framework net10.0-android36.0 \
  --runtime android-arm64 \
  --no-restore \
  --warnaserror \
  -m:1 \
  -nr:false \
  --disable-build-servers \
  -p:UseSharedCompilation=false \
  -p:BuildInParallel=false \
  -p:AndroidPackageFormats=apk \
  "${package_args[@]}"

[[ -f "$apk" && ! -L "$apk" ]] || fail "full-maui-arm64-apk-missing"
mapfile -d '' apk_outputs < <(find "$(dirname -- "$apk")" -maxdepth 1 -type f -name '*.apk' -print0)
[[ "${#apk_outputs[@]}" -eq 1 && "${apk_outputs[0]}" == "$apk" ]] \
  || fail "apk-output-inventory-not-exact"
[[ -z "$(find "$(dirname -- "$apk")" -maxdepth 1 -type l -print -quit)" ]] \
  || fail "apk-output-symlink-present"
run_bounded apk-content-verification "$evidence_dir/content-apk.log" \
  "$python_command" "$repo_dir/scripts/verify_android_content_bundle.py" \
  --repo-root "$repo_dir" \
  --core-root "$CHUMMER_CORE_CONTENT_ROOT" \
  --manifest "$repo_dir/src/Chummer.Android/Content/chummer-content-manifest.json" \
  --apk "$apk" \
  --receipt "$evidence_dir/content-apk-receipt.json" \
  --check

run_bounded post-build-source-graph-seal "$evidence_dir/source-graph-seal.log" \
  "$python_command" "$repo_dir/scripts/verify_release_source_graph.py" \
  --android-root "$repo_dir" \
  --workspace-root "$CHUMMER_RELEASE_WORKSPACE_ROOT" \
  --package-authority "$CHUMMER_RELEASE_PACKAGE_AUTHORITY_V2" \
  --verify-existing "$CHUMMER_RELEASE_SOURCE_GRAPH"

current_stage="provenance-seal"
"$python_command" "$repo_dir/scripts/materialize-api36-physical-build-provenance.py" \
  materialize \
  --android-root "$repo_dir" \
  --w5-receipt "$CHUMMER_W5_COMPILE_RECEIPT" \
  --w5-evidence-directory "$CHUMMER_W5_COMPILE_EVIDENCE" \
  --source-graph "$CHUMMER_RELEASE_SOURCE_GRAPH" \
  --package-authority "$repo_dir/eng/internal-phone-beta-package-authority.json" \
  --release-package-authority-v2 "$CHUMMER_RELEASE_PACKAGE_AUTHORITY_V2" \
  --content-source-receipt "$evidence_dir/content-source-receipt.json" \
  --content-apk-receipt "$evidence_dir/content-apk-receipt.json" \
  --full-project-lock "$lock" \
  --assets "$repo_dir/src/Chummer.Android/obj/project.assets.json" \
  --toolchain-log "$evidence_dir/toolchain.log" \
  --source-graph-log "$evidence_dir/source-graph.log" \
  --content-source-log "$evidence_dir/content-source.log" \
  --build-inputs-log "$evidence_dir/build-inputs.log" \
  --restore-log "$evidence_dir/restore.log" \
  --build-log "$evidence_dir/build.log" \
  --content-apk-log "$evidence_dir/content-apk.log" \
  --source-graph-seal-log "$evidence_dir/source-graph-seal.log" \
  --command-journal "$journal" \
  --raw-command-journal "$raw_journal" \
  --android-sdk-packages "$CHUMMER_ANDROID_SDK_PACKAGES_XML" \
  --dotnet-workloads "$evidence_dir/dotnet-workloads.json" \
  --java-path "$java_command" \
  --javac-path "$javac_command" \
  --dotnet-path "$dotnet_command" \
  --python-path "$python_command" \
  --release-workspace-root "$CHUMMER_RELEASE_WORKSPACE_ROOT" \
  --package-feed "$CHUMMER_INTERNAL_PHONE_BETA_PACKAGE_FEED" \
  --offline-feed "$CHUMMER_API36_OFFLINE_NUGET_FEED" \
  --nuget-packages "$CHUMMER_API36_NUGET_PACKAGES" \
  --android-build-tools-version "$android_build_tools_version" \
  --dotnet-version "$($dotnet_command --version)" \
  --apk "$apk" \
  --output "$CHUMMER_API36_BUILD_PROVENANCE"

[[ -z "$(git -C "$repo_dir" status --porcelain=v1 --untracked-files=all)" ]] \
  || fail "android-candidate-dirtied"
current_stage="complete"
printf 'api36_physical_candidate=pass publication_authorized=false provenance=%s provenance_sha256=%s apk=%s apk_sha256=%s\n' \
  "$CHUMMER_API36_BUILD_PROVENANCE" \
  "$(sha256sum "$CHUMMER_API36_BUILD_PROVENANCE" | cut -d' ' -f1)" \
  "$apk" \
  "$(sha256sum "$apk" | cut -d' ' -f1)"
