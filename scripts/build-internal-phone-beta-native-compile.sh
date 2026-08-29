#!/usr/bin/env bash
set -euo pipefail

repo_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd -P)"
project="$repo_dir/tests/Chummer.Android.Native.CompileCheck/Chummer.Android.Native.CompileCheck.csproj"
lock="$repo_dir/tests/Chummer.Android.Native.CompileCheck/packages.lock.json"
dotnet_command="${CHUMMER_DOTNET:-dotnet}"
proof_tmp=""
receipt_output=""
evidence_output=""
output_ready="false"
proof_complete="false"
evidence_persisted="false"
current_stage="preflight"
evidence_names=(
  authority-intake.log
  authority-binding.json
  restore.log
  owned-compile-graph.log
  compile-graph.json
  build.log
  command-journal.jsonl
)

fail() {
  printf 'internal_phone_beta_native_compile=blocked stage=%s publication_authorized=false\n' "$1" >&2
  exit 2
}

cleanup() {
  local status="$?"
  trap - EXIT HUP INT TERM
  if [[ "$status" -ne 0 && "$output_ready" == "true" && -n "$proof_tmp" \
        && "$proof_complete" != "true" \
        && ! -e "$receipt_output" && ! -L "$receipt_output" ]]; then
    local failure_tmp journal_sha256 journal_size evidence_json
    failure_tmp="$proof_tmp/failure-receipt.json"
    journal_sha256=""
    journal_size=0
    if [[ -f "$proof_tmp/command-journal.jsonl" ]]; then
      journal_sha256="$(sha256sum "$proof_tmp/command-journal.jsonl" | cut -d' ' -f1)"
      journal_size="$(stat -c '%s' "$proof_tmp/command-journal.jsonl")"
    fi
    evidence_json="$(build_evidence_json)"
    jq -n \
      --arg contractName "chummer.android.internal-phone-beta-native-compile/v1" \
      --arg failureStage "$current_stage" \
      --arg journalSha256 "$journal_sha256" \
      --argjson journalSizeBytes "$journal_size" \
      --arg evidenceDirectory "$evidence_output" \
      --argjson evidence "$evidence_json" \
      '{
        contractName: $contractName,
        status: "blocked",
        authorityClass: "internal_phone_beta_only",
        publicationAuthorized: false,
        retryPerformed: false,
        failureStage: $failureStage,
        journalSha256: $journalSha256,
        journalSizeBytes: $journalSizeBytes,
        evidenceDirectory: $evidenceDirectory,
        evidence: $evidence,
        proofScope: "Native.CompileCheck_dependency_only",
        doesNotAssert: ["full_maui_build", "core_data_lang_content", "api36_device_execution", "google_play_upload", "public_release_readiness"]
      }' >"$failure_tmp"
    chmod 0600 "$failure_tmp"
    if persist_evidence; then
      ln -- "$failure_tmp" "$receipt_output" || true
    fi
  fi
  if [[ "$evidence_persisted" == "true" && -n "$proof_tmp" && -d "$proof_tmp" ]]; then
    rm -rf -- "$proof_tmp"
  fi
  exit "$status"
}

build_evidence_json() {
  local rows name path digest size
  rows='[]'
  for name in "${evidence_names[@]}"; do
    path="$proof_tmp/$name"
    [[ -f "$path" && ! -L "$path" ]] || continue
    digest="$(sha256sum "$path" | cut -d' ' -f1)"
    size="$(stat -c '%s' "$path")"
    rows="$(jq -cn \
      --argjson rows "$rows" \
      --arg path "$name" \
      --arg sha256 "$digest" \
      --argjson sizeBytes "$size" \
      '$rows + [{path: $path, sha256: $sha256, sizeBytes: $sizeBytes}]')"
  done
  printf '%s\n' "$rows"
}

persist_evidence() {
  local name source
  [[ "$evidence_persisted" != "true" ]] || return 0
  mkdir -m 0700 -- "$evidence_output" || return 1
  for name in "${evidence_names[@]}"; do
    source="$proof_tmp/$name"
    [[ -f "$source" && ! -L "$source" ]] || continue
    ln -- "$source" "$evidence_output/$name" || return 1
  done
  evidence_persisted="true"
}

require_canonical_file() {
  local variable="$1"
  local value="${!variable:-}"
  [[ -n "$value" && "$value" == /* && ! -L "$value" && -f "$value" ]] \
    || fail "input-$variable-not-absolute-regular"
  [[ "$(realpath -e -- "$value")" == "$value" ]] \
    || fail "input-$variable-not-canonical"
}

require_canonical_directory() {
  local variable="$1"
  local value="${!variable:-}"
  [[ -n "$value" && "$value" == /* && ! -L "$value" && -d "$value" ]] \
    || fail "input-$variable-not-absolute-directory"
  [[ "$(realpath -e -- "$value")" == "$value" ]] \
    || fail "input-$variable-not-canonical"
}

require_absent_output() {
  local variable="$1"
  local value="${!variable:-}"
  local parent
  [[ -n "$value" && "$value" == /* && ! -e "$value" && ! -L "$value" ]] \
    || fail "input-$variable-not-absent-absolute"
  parent="$(dirname -- "$value")"
  [[ ! -L "$parent" && -d "$parent" && "$(realpath -e -- "$parent")" == "$parent" ]] \
    || fail "input-$variable-parent-not-canonical"
}

trap cleanup EXIT
trap 'exit 129' HUP
trap 'exit 130' INT
trap 'exit 143' TERM

for command in cut date dirname git jq ln mkdir mktemp python3 realpath rm sha256sum stat; do
  command -v "$command" >/dev/null 2>&1 || fail "missing-command-$command"
done
command -v "$dotnet_command" >/dev/null 2>&1 || fail "missing-dotnet"

require_canonical_directory CHUMMER_PRESENTATION_ROOT
require_canonical_directory CHUMMER_INTERNAL_PHONE_BETA_PACKAGE_FEED
require_canonical_directory CHUMMER_INTERNAL_PHONE_BETA_NUGET_PACKAGES
require_canonical_file CHUMMER_CURRENT_UI_PACKAGE_AUTHORITY_RECEIPT
require_absent_output CHUMMER_INTERNAL_PHONE_BETA_BUILD_RECEIPT
receipt_output="$CHUMMER_INTERNAL_PHONE_BETA_BUILD_RECEIPT"
evidence_output="$receipt_output.evidence"
[[ ! -e "$evidence_output" && ! -L "$evidence_output" ]] \
  || fail "build-evidence-output-not-absent"
output_ready="true"

proof_parent="$(dirname -- "$CHUMMER_INTERNAL_PHONE_BETA_BUILD_RECEIPT")"
[[ ! -L "$proof_parent" && -d "$proof_parent" ]] || fail "build-receipt-parent-invalid"
proof_tmp="$(mktemp -d "$proof_parent/.internal-phone-beta-native.XXXXXX")"
chmod 0700 "$proof_tmp"

for forbidden in \
  CHUMMER_CORE_ENGINE_ROOT \
  CHUMMER_RUN_SERVICES_ROOT \
  CHUMMER_HUB_REGISTRY_ROOT \
  CHUMMER_UI_KIT_ROOT \
  CHUMMER_MEDIA_FACTORY_ROOT; do
  [[ -z "${!forbidden:-}" ]] || fail "source-sibling-variable-$forbidden"
done

[[ -f "$lock" && ! -L "$lock" ]] || fail "compile-lock-missing"
sdk_version="$($dotnet_command --version)"
[[ "$sdk_version" == "10.0.111" ]] || fail "dotnet-sdk-not-10.0.111"
[[ -z "$(git -C "$repo_dir" status --porcelain --untracked-files=all)" ]] \
  || fail "android-proof-head-not-clean"

authority_binding="$proof_tmp/authority-binding.json"
restore_log="$proof_tmp/restore.log"
compile_graph="$proof_tmp/compile-graph.json"
build_log="$proof_tmp/build.log"
receipt_tmp="$proof_tmp/build-receipt.json"
journal="$proof_tmp/command-journal.jsonl"
deadline_epoch="$(( $(date +%s) + 3600 ))"

run_bounded() {
  local phase="$1"
  local output="$2"
  shift 2
  current_stage="$phase"
  python3 "$repo_dir/scripts/run_internal_phone_beta_bounded.py" \
    --journal "$journal" \
    --output "$output" \
    --phase "$phase" \
    --timeout-seconds 900 \
    --deadline-epoch "$deadline_epoch" \
    -- "$@"
}

run_bounded authority-intake "$proof_tmp/authority-intake.log" \
  python3 "$repo_dir/scripts/verify_internal_phone_beta_package_authority.py" \
  --android-root "$repo_dir" \
  --presentation-root "$CHUMMER_PRESENTATION_ROOT" \
  --receipt "$CHUMMER_CURRENT_UI_PACKAGE_AUTHORITY_RECEIPT" \
  --package-feed "$CHUMMER_INTERNAL_PHONE_BETA_PACKAGE_FEED" \
  --output "$authority_binding"

package_args=(
  "-p:ChummerPresentationRoot=$CHUMMER_PRESENTATION_ROOT"
  "-p:ChummerDesktopRuntimeIdentifiers="
  "-p:ChummerUseLocalCompatibilityTree=false"
  "-p:ChummerUseLockedOwnerContractPackages=true"
  "-p:RestoreLockedMode=true"
  "-p:RestorePackagesWithLockFile=true"
  "-p:NuGetAudit=false"
  "-p:ChummerContractsPackageVersion=0.0.0-packageplane.candidate.shfebd698752e19"
  "-p:ChummerCoreRuntimePackageVersion=0.0.0-packageplane.candidate.shfebd698752e19"
  "-p:ChummerCampaignContractsPackageVersion=0.1.0-preview"
  "-p:ChummerRunContractsPackageVersion=0.1.0-packageplane.candidate.sh66c418a5004f"
  "-p:ChummerHubRegistryContractsPackageVersion=0.1.0-packageplane.candidate.sh66c418a5004f"
  "-p:ChummerUiKitPackageVersion=0.1.0-preview"
)

export DOTNET_CLI_USE_MSBUILD_SERVER=0
export MSBUILDDISABLENODEREUSE=1
export NUGET_PACKAGES="$CHUMMER_INTERNAL_PHONE_BETA_NUGET_PACKAGES"

run_bounded locked-restore "$restore_log" \
  "$dotnet_command" restore "$project" \
  --locked-mode \
  --disable-parallel \
  --packages "$CHUMMER_INTERNAL_PHONE_BETA_NUGET_PACKAGES" \
  --source "$CHUMMER_INTERNAL_PHONE_BETA_PACKAGE_FEED" \
  --source https://api.nuget.org/v3/index.json \
  --ignore-failed-sources \
  "${package_args[@]}"

run_bounded owned-compile-graph "$proof_tmp/owned-compile-graph.log" \
  python3 "$repo_dir/scripts/verify_native_compile_graph.py" \
  --repo-root "$repo_dir" \
  --workspace-root "$(realpath -e -- "$repo_dir/../..")" \
  --project "$project" \
  --require-assets
run_bounded package-compile-graph "$compile_graph" \
  python3 "$repo_dir/scripts/verify_internal_phone_beta_compile_graph.py" \
  --android-root "$repo_dir" \
  --presentation-root "$CHUMMER_PRESENTATION_ROOT" \
  --project "$project"

run_bounded serialized-native-compile "$build_log" \
  "$dotnet_command" build "$project" \
  --configuration Release \
  --no-restore \
  --warnaserror \
  -m:1 \
  -nr:false \
  --disable-build-servers \
  -p:UseSharedCompilation=false \
  -p:BuildInParallel=false \
  "${package_args[@]}"

current_stage="post-compile-seal"
artifact="$repo_dir/tests/Chummer.Android.Native.CompileCheck/bin/Release/net10.0/Chummer.Android.Native.CompileCheck.dll"
[[ -f "$artifact" && ! -L "$artifact" ]] || fail "native-compile-artifact-missing"
[[ -z "$(git -C "$repo_dir" status --porcelain --untracked-files=all)" ]] \
  || fail "android-proof-head-dirtied"
android_commit="$(git -C "$repo_dir" rev-parse HEAD)"
android_tree="$(git -C "$repo_dir" rev-parse HEAD^{tree})"
presentation_commit="$(git -C "$CHUMMER_PRESENTATION_ROOT" rev-parse HEAD)"
presentation_tree="$(git -C "$CHUMMER_PRESENTATION_ROOT" rev-parse HEAD^{tree})"
artifact_sha256="$(sha256sum "$artifact" | cut -d' ' -f1)"
artifact_size="$(stat -c '%s' "$artifact")"
lock_sha256="$(sha256sum "$lock" | cut -d' ' -f1)"
assets="$repo_dir/tests/Chummer.Android.Native.CompileCheck/obj/project.assets.json"
assets_sha256="$(sha256sum "$assets" | cut -d' ' -f1)"
restore_sha256="$(sha256sum "$restore_log" | cut -d' ' -f1)"
build_sha256="$(sha256sum "$build_log" | cut -d' ' -f1)"
compile_graph_sha256="$(sha256sum "$compile_graph" | cut -d' ' -f1)"
authority_binding_sha256="$(sha256sum "$authority_binding" | cut -d' ' -f1)"
journal_sha256="$(sha256sum "$journal" | cut -d' ' -f1)"
journal_size="$(stat -c '%s' "$journal")"
evidence_json="$(build_evidence_json)"

jq -n \
  --arg contractName "chummer.android.internal-phone-beta-native-compile/v1" \
  --arg schema "chummer.android.internal-phone-beta-native-compile/v1" \
  --arg sdkVersion "$sdk_version" \
  --arg producerSdkVersion "10.0.103" \
  --arg androidCommit "$android_commit" \
  --arg androidTree "$android_tree" \
  --arg presentationCommit "$presentation_commit" \
  --arg presentationTree "$presentation_tree" \
  --arg authorityReceiptSha256 "940d4cf0d77bb371e50b1cb3fb566089843a945c097b73a122522db1a673b547" \
  --arg authorityCacheManifestSha256 "b31e6f2b1903d9cab0cfe550c2892b9bb0ffc1183bbb8bb2eab4289b1710b09c" \
  --arg packageAuthoritySha256 "42e01c93a863882022cf156d86674cda1fbaecba7b9a1112323a27e42dd73a61" \
  --arg desktopRuntimeLockSha256 "613ad62809e64e884b5f3f775bce2b127bda97c4aaa04d2e3ca8f089a743709b" \
  --arg authorityBindingSha256 "$authority_binding_sha256" \
  --arg journalSha256 "$journal_sha256" \
  --argjson journalSizeBytes "$journal_size" \
  --arg evidenceDirectory "$evidence_output" \
  --argjson evidence "$evidence_json" \
  --arg compileGraphSha256 "$compile_graph_sha256" \
  --arg restoreOutputSha256 "$restore_sha256" \
  --arg buildOutputSha256 "$build_sha256" \
  --arg lockSha256 "$lock_sha256" \
  --argjson lockSizeBytes "$(stat -c '%s' "$lock")" \
  --arg assetsSha256 "$assets_sha256" \
  --arg artifactPath "tests/Chummer.Android.Native.CompileCheck/bin/Release/net10.0/Chummer.Android.Native.CompileCheck.dll" \
  --arg artifactSha256 "$artifact_sha256" \
  --argjson artifactSizeBytes "$artifact_size" \
  '{
    contractName: $contractName,
    schema: $schema,
    status: "pass",
    authorityClass: "internal_phone_beta_only",
    publicationAuthorized: false,
    dependencyMode: "locked_package_no_siblings",
    packageOnly: true,
    restoreLockedMode: true,
    sourceCheckoutsPresent: false,
    siblingsAllowed: false,
    serializedBuild: true,
    sdkVersion: $sdkVersion,
    producerSdkVersion: $producerSdkVersion,
    androidCommit: $androidCommit,
    androidTree: $androidTree,
    androidWorktreeClean: true,
    presentationCommit: $presentationCommit,
    presentationTree: $presentationTree,
    authorityReceiptSha256: $authorityReceiptSha256,
    authorityCacheManifestSha256: $authorityCacheManifestSha256,
    packageAuthoritySha256: $packageAuthoritySha256,
    desktopRuntimeLockSha256: $desktopRuntimeLockSha256,
    authorityBindingSha256: $authorityBindingSha256,
    executionBounds: {perCommandSeconds: 900, totalSeconds: 3600, processGroupTermination: true},
    journalSha256: $journalSha256,
    journalSizeBytes: $journalSizeBytes,
    evidenceDirectory: $evidenceDirectory,
    evidence: $evidence,
    evidenceBindings: ($evidence | map({key: .path, value: {sha256: .sha256, sizeBytes: .sizeBytes}}) | from_entries),
    compileGraphSha256: $compileGraphSha256,
    restoreOutputSha256: $restoreOutputSha256,
    buildOutputSha256: $buildOutputSha256,
    lockSha256: $lockSha256,
    lockSizeBytes: $lockSizeBytes,
    assetsSha256: $assetsSha256,
    artifact: {
      path: $artifactPath,
      kind: "native_compile_check_dependency_dll",
      scope: "Native.CompileCheck_dependency_only",
      sha256: $artifactSha256,
      sizeBytes: $artifactSizeBytes,
      fullMauiArtifact: false
    },
    phaseResults: {
      authorityIntake: {status: "pass"},
      lockedRestore: {status: "pass"},
      ownedCompileGraph: {status: "pass"},
      packageCompileGraph: {status: "pass"},
      serializedNativeCompile: {status: "pass", warnings: 0, errors: 0}
    },
    proofScope: "Native.CompileCheck_dependency_only",
    fullMauiBuild: false,
    coreDataLangContentVerified: false,
    laterDeviceGateRequirements: ["full_maui_build", "core_data_lang_content", "apk_install", "physical_api36_execution"],
    doesNotAssert: ["full_maui_build", "core_data_lang_content", "api36_device_execution", "google_play_upload", "public_release_readiness", "publication_authority", "tablet_readiness"]
  }' >"$receipt_tmp"
chmod 0600 "$receipt_tmp"
persist_evidence || fail "build-evidence-persist-failed"
python3 "$repo_dir/scripts/verify_internal_phone_beta_compile_receipt.py" \
  --receipt "$receipt_tmp" \
  --evidence-directory "$evidence_output" \
  >/dev/null || fail "build-evidence-verification-failed"
ln -- "$receipt_tmp" "$CHUMMER_INTERNAL_PHONE_BETA_BUILD_RECEIPT" \
  || fail "build-receipt-output-collision"
proof_complete="true"
receipt_sha256="$(sha256sum "$CHUMMER_INTERNAL_PHONE_BETA_BUILD_RECEIPT" | cut -d' ' -f1)"
printf 'internal_phone_beta_native_compile=pass publication_authorized=false receipt=%s receipt_sha256=%s artifact=%s artifact_sha256=%s\n' \
  "$CHUMMER_INTERNAL_PHONE_BETA_BUILD_RECEIPT" "$receipt_sha256" "$artifact" "$artifact_sha256"
