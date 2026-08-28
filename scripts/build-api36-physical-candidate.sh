#!/usr/bin/env bash
set -euo pipefail

repo_dir="$(cd "${BASH_SOURCE[0]%/*}/.." && pwd -P)"
project="$repo_dir/src/Chummer.Android/Chummer.Android.csproj"
lock="$repo_dir/src/Chummer.Android/packages.lock.json"
dotnet_command="/usr/lib/dotnet/dotnet"
python_command="/usr/bin/python3.12"
android_sdk_root="/home/tibor/.cache/chummer-android-toolchain/android-sdk"
java_home="/home/tibor/.cache/chummer-android-toolchain/microsoft-jdk"
java_command="$java_home/bin/java"
javac_command="$java_home/bin/javac"
jarsigner_command="$java_home/bin/jarsigner"
apksigner_command="$android_sdk_root/build-tools/36.0.0/apksigner"
android_workload_manifest="/home/tibor/.dotnet/sdk-manifests/10.0.100/microsoft.net.sdk.android/36.1.69/WorkloadManifest.json"
maui_workload_manifest="/home/tibor/.dotnet/sdk-manifests/10.0.100/microsoft.net.sdk.maui/10.0.20/WorkloadManifest.json"
android_build_tools_version="36.0.0"
cut_command="/usr/bin/cut"
dirname_command="/usr/bin/dirname"
find_command="/usr/bin/find"
git_command="/usr/bin/git"
jq_command="/usr/bin/jq"
mkdir_command="/usr/bin/mkdir"
realpath_command="/usr/bin/realpath"
sha256sum_command="/usr/bin/sha256sum"
chmod_command="/usr/bin/chmod"
current_stage="preflight"
evidence_dir=""
journal=""
raw_journal=""
delegate_journal=""

fail() {
  printf 'api36_physical_candidate=blocked stage=%s retry_performed=false publication_authorized=false\n' "$1" >&2
  exit 2
}

on_exit() {
  local status="$?"
  trap - EXIT HUP INT TERM
  if [[ "$status" -ne 0 && -n "$evidence_dir" && -d "$evidence_dir" ]]; then
    "$jq_command" -n \
      --arg contractName "chummer.android.api36-arm64-physical-build-provenance/v2" \
      --arg failureStage "$current_stage" \
      '{contractName:$contractName,status:"blocked",publicationAuthorized:false,retryPerformed:false,failureStage:$failureStage}' \
      >"$evidence_dir/blocked.json" 2>/dev/null || true
    "$chmod_command" 0600 "$evidence_dir/blocked.json" 2>/dev/null || true
  fi
  exit "$status"
}

require_file() {
  local variable="$1" value="${!1:-}"
  [[ -n "$value" && "$value" == /* && -f "$value" && ! -L "$value" ]] \
    || fail "input-$variable-not-absolute-regular"
  [[ "$("$realpath_command" -e -- "$value")" == "$value" ]] \
    || fail "input-$variable-not-canonical"
}

require_directory() {
  local variable="$1" value="${!1:-}"
  [[ -n "$value" && "$value" == /* && -d "$value" && ! -L "$value" ]] \
    || fail "input-$variable-not-absolute-directory"
  [[ "$("$realpath_command" -e -- "$value")" == "$value" ]] \
    || fail "input-$variable-not-canonical"
}

require_absent_output() {
  local variable="$1" value="${!1:-}" parent
  [[ -n "$value" && "$value" == /* && ! -e "$value" && ! -L "$value" ]] \
    || fail "input-$variable-not-absent-absolute"
  parent="$("$dirname_command" -- "$value")"
  [[ -d "$parent" && ! -L "$parent" && "$("$realpath_command" -e -- "$parent")" == "$parent" ]] \
    || fail "input-$variable-parent-not-canonical"
}

require_sha256() {
  local path="$1" expected="$2" label="$3" actual
  actual="$("$sha256sum_command" "$path" | "$cut_command" -d' ' -f1)"
  [[ "$actual" == "$expected" ]] || fail "toolchain-sha256-$label"
}

run_bounded() {
  local phase="$1" output="$2"
  shift 2
  current_stage="$phase"
  "$python_command" "$repo_dir/scripts/materialize-api36-physical-build-provenance.py" run-bounded \
    --journal "$journal" \
    --raw-journal "$raw_journal" \
    --delegate-journal "$delegate_journal" \
    --output "$output" \
    --phase "$phase" \
    --timeout-seconds 1800 \
    --deadline-epoch "$deadline_epoch" \
    --invocation-started-epoch "$invocation_started_epoch" \
    --working-directory "$repo_dir" \
    "${bounded_environment_arguments[@]}" \
    -- "$@"
}

trap on_exit EXIT
trap 'exit 129' HUP
trap 'exit 130' INT
trap 'exit 143' TERM

for command in \
  "$cut_command" /usr/bin/date "$dirname_command" "$find_command" "$git_command" "$jq_command" \
  "$mkdir_command" "$realpath_command" "$sha256sum_command" "$chmod_command" \
  "$python_command" "$dotnet_command" \
  "$java_command" "$javac_command" "$jarsigner_command" "$apksigner_command" \
  "$java_home/bin/keytool" \
  "$android_sdk_root/build-tools/36.0.0/aapt2" \
  "$android_sdk_root/build-tools/36.0.0/zipalign" \
  "$android_sdk_root/build-tools/36.0.0/lib/apksigner.jar" \
  "$android_sdk_root/build-tools/36.0.0/package.xml" \
  "$android_sdk_root/platforms/android-36/android.jar" \
  "$android_sdk_root/platforms/android-36/package.xml" \
  "$android_sdk_root/platform-tools/adb" \
  "$android_sdk_root/platform-tools/package.xml" "$java_home/release" \
  "$android_workload_manifest" "$maui_workload_manifest"; do
  [[ -f "$command" && ! -L "$command" && "$(/usr/bin/realpath -e -- "$command")" == "$command" ]] \
    || fail "missing-or-noncanonical-toolchain-file"
done

require_sha256 "$dotnet_command" "1c13be7f10008294dfd25f0fc0cd7c88e26d3dbaf8e16019af6c5bb53dd0259d" dotnet
require_sha256 "$java_home/release" "6bd25f1446259442ae9cfdd1d9d7b6094aa7e3cf05bcbddb842e2f2b5facac4c" jdk-release
require_sha256 "$java_command" "2878f3c82270ae7f2bc0c94dbde65718a5a97387ed3ad4b1ce9047948f8b401e" java
require_sha256 "$javac_command" "899fa6dab44db00429d59959cb2ca53169ad4393841dbbae14a0debcdb9fe2a8" javac
require_sha256 "$jarsigner_command" "07e52b7729ed7355c280f6766970b8d5dc9942e741ed5af0330cfc09699eb548" jarsigner
require_sha256 "$java_home/bin/keytool" "7bb11637313a640810ec568ffb7e12d90e423c8c81356fc0416d7547047fa144" keytool
require_sha256 "$android_sdk_root/platforms/android-36/package.xml" "2110f8ec9c213a77e287e4e92d89e28dd770e4377c24350758cbddebb75de9f3" platform-package
require_sha256 "$android_sdk_root/platforms/android-36/android.jar" "d9eb9da824d9e247a352f570f01e1169e725b2954bca9e283a71786c59b59f9a" android-jar
require_sha256 "$android_sdk_root/build-tools/36.0.0/package.xml" "a1d29ea87385aa2b8997c7f65968e0c52e8efb4f73ed4cf1df54df808acde6b8" build-tools-package
require_sha256 "$apksigner_command" "b47549e373b895ce6ca620d0c7887e674d9615ffa837a86ac601dcfd04adb0f0" apksigner
require_sha256 "$android_sdk_root/build-tools/36.0.0/lib/apksigner.jar" "3716d9311e55d2b0918a2fd9d54ba9e406c5f6abeea700b287f11259bc163dec" apksigner-jar
require_sha256 "$android_sdk_root/build-tools/36.0.0/aapt2" "1a6a396b9cd071f7040071fdd108718cb98c3c9f4960044f373b288993d19eb7" aapt2
require_sha256 "$android_sdk_root/build-tools/36.0.0/zipalign" "c5f559e946de5a9e7d58792181db20383b228877812136bc469d97ae00a43b0a" zipalign
require_sha256 "$android_sdk_root/platform-tools/package.xml" "b7253bc2352e6bd5fdc2aa5da4f452ee4c3b6bdc93f20a87d39ee680a91af97c" platform-tools-package
require_sha256 "$android_sdk_root/platform-tools/adb" "372d800c04c3272729afade8a85d95a70fb1c7e74062d9ab17a92eb7b618096c" canonical-adb
require_sha256 "$android_workload_manifest" "e520a5f491b933774ed06c48e8adf3a6878ad8a6cd320180a3395080cf362644" android-workload-manifest
require_sha256 "$maui_workload_manifest" "e2506ea1897fca4cf528fa2e950d3267477e28e5253f1e7781520058742ced10" maui-workload-manifest

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
[[ "$("$sha256sum_command" "$lock" | "$cut_command" -d' ' -f1)" == "9037d4afc11dd8661dfbcccbc67a9f814d110fb17cf985cf215268e12ae3583e" ]] \
  || fail "full-project-lock-digest-mismatch"
[[ "$($dotnet_command --version)" == "10.0.111" ]] || fail "dotnet-sdk-not-10.0.111"
[[ -z "$("$git_command" -C "$repo_dir" status --porcelain=v1 --untracked-files=all)" ]] \
  || fail "android-candidate-not-clean"
[[ "$("$git_command" -C "$CHUMMER_PRESENTATION_ROOT" rev-parse HEAD)" == "a8a317aff534dc5fd47f2db1bc39466799021990" ]] \
  || fail "w41-presentation-commit-mismatch"
[[ "$("$git_command" -C "$CHUMMER_PRESENTATION_ROOT" rev-parse 'HEAD^{tree}')" == "f8214243280030de5d134351f39ea4b23afbe394" ]] \
  || fail "w41-presentation-tree-mismatch"
[[ -z "$("$git_command" -C "$CHUMMER_PRESENTATION_ROOT" status --porcelain=v1 --untracked-files=all)" ]] \
  || fail "w41-presentation-not-clean"
[[ "$("$sha256sum_command" "$CHUMMER_PRESENTATION_ROOT/Chummer.Presentation/packages.lock.json" | "$cut_command" -d' ' -f1)" == "568fd2c602494329d19fbe8d9a2c83a4c2e82754b50e31141b192c1af7ccf964" ]] \
  || fail "w41-presentation-lock-mismatch"
[[ "$("$sha256sum_command" "$CHUMMER_PRESENTATION_ROOT/Chummer.Desktop.Runtime/packages.lock.json" | "$cut_command" -d' ' -f1)" == "202a29a35b4768c3306349ee40a34d8f23ada97c0b0ef11e104763b5ff9cc60e" ]] \
  || fail "w41-desktop-lock-mismatch"
[[ "$("$git_command" -C "$CHUMMER_CORE_CONTENT_ROOT" rev-parse HEAD)" == "2fb2ae9bb48e5a1a6b25a174ba88008ce995fcd5" ]] \
  || fail "core-content-commit-mismatch"
[[ -z "$("$git_command" -C "$CHUMMER_CORE_CONTENT_ROOT" status --porcelain=v1 --untracked-files=all -- Chummer/data Chummer/lang)" ]] \
  || fail "core-content-not-clean"

evidence_dir="$CHUMMER_API36_BUILD_PROVENANCE.evidence"
[[ ! -e "$evidence_dir" && ! -L "$evidence_dir" ]] || fail "evidence-output-collision"
"$mkdir_command" -m 0700 -- "$evidence_dir"
journal="$evidence_dir/command-journal.jsonl"
raw_journal="$evidence_dir/raw-command-journal.jsonl"
delegate_journal="$evidence_dir/delegate-command-journal.jsonl"
invocation_started_epoch="$(/usr/bin/date +%s)"
deadline_epoch="$(( invocation_started_epoch + 7200 ))"

bounded_environment=(
  "ANDROID_HOME=$android_sdk_root"
  "ANDROID_SDK_ROOT=$android_sdk_root"
  "DOTNET_CLI_HOME=/home/tibor"
  "DOTNET_CLI_TELEMETRY_OPTOUT=1"
  "DOTNET_CLI_USE_MSBUILD_SERVER=0"
  "DOTNET_ROOT=/usr/lib/dotnet"
  "HOME=/home/tibor"
  "JAVA_HOME=$java_home"
  "LANG=C.UTF-8"
  "LC_ALL=C.UTF-8"
  "MSBUILDDISABLENODEREUSE=1"
  "NUGET_PACKAGES=$CHUMMER_API36_NUGET_PACKAGES"
  "PATH=$java_home/bin:/usr/lib/dotnet:/usr/bin:/bin"
  "TMPDIR=/tmp"
)
bounded_environment_arguments=()
for environment_entry in "${bounded_environment[@]}"; do
  bounded_environment_arguments+=(--environment "$environment_entry")
done

run_bounded toolchain-intake "$evidence_dir/toolchain.log" \
  "$python_command" "$repo_dir/scripts/materialize-api36-physical-build-provenance.py" \
  capture-workloads --dotnet "$dotnet_command" \
  --android-workload-manifest "$android_workload_manifest" \
  --maui-workload-manifest "$maui_workload_manifest" \
  --android-sdk-packages "$CHUMMER_ANDROID_SDK_PACKAGES_XML" \
  --android-sdk-root "$android_sdk_root" \
  --java "$java_command" \
  --javac "$javac_command" \
  --jarsigner "$jarsigner_command" \
  --apksigner "$apksigner_command" \
  --output "$evidence_dir/dotnet-workloads.json"

run_bounded source-graph-intake "$evidence_dir/source-graph.log" \
  "$python_command" "$repo_dir/scripts/verify_release_source_graph.py" \
  --android-root "$repo_dir" \
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
  "-p:AndroidSdkDirectory=$android_sdk_root"
  "-p:AndroidSdkBuildToolsVersion=$android_build_tools_version"
  "-p:JavaSdkDirectory=$java_home"
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
mapfile -d '' apk_outputs < <("$find_command" "$("$dirname_command" -- "$apk")" -maxdepth 1 -type f -name '*.apk' -print0)
[[ "${#apk_outputs[@]}" -eq 1 && "${apk_outputs[0]}" == "$apk" ]] \
  || fail "apk-output-inventory-not-exact"
[[ -z "$("$find_command" "$("$dirname_command" -- "$apk")" -maxdepth 1 -type l -print -quit)" ]] \
  || fail "apk-output-symlink-present"
run_bounded apk-signature-verification "$evidence_dir/signing-phase.log" \
  "$python_command" "$repo_dir/scripts/materialize-api36-physical-build-provenance.py" \
  verify-apk-signing \
  --apk "$apk" \
  --apksigner "$apksigner_command" \
  --jarsigner "$jarsigner_command" \
  --receipt "$evidence_dir/signing-receipt.json" \
  --apksigner-log "$evidence_dir/apksigner.log" \
  --jarsigner-log "$evidence_dir/jarsigner.log"
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
  --signing-phase-log "$evidence_dir/signing-phase.log" \
  --apksigner-log "$evidence_dir/apksigner.log" \
  --jarsigner-log "$evidence_dir/jarsigner.log" \
  --signing-receipt "$evidence_dir/signing-receipt.json" \
  --content-apk-log "$evidence_dir/content-apk.log" \
  --source-graph-seal-log "$evidence_dir/source-graph-seal.log" \
  --command-journal "$journal" \
  --raw-command-journal "$raw_journal" \
  --delegate-command-journal "$delegate_journal" \
  --android-sdk-packages "$CHUMMER_ANDROID_SDK_PACKAGES_XML" \
  --android-sdk-root "$android_sdk_root" \
  --android-workload-manifest "$android_workload_manifest" \
  --maui-workload-manifest "$maui_workload_manifest" \
  --dotnet-workloads "$evidence_dir/dotnet-workloads.json" \
  --java-path "$java_command" \
  --javac-path "$javac_command" \
  --jarsigner-path "$jarsigner_command" \
  --apksigner-path "$apksigner_command" \
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

[[ -z "$("$git_command" -C "$repo_dir" status --porcelain=v1 --untracked-files=all)" ]] \
  || fail "android-candidate-dirtied"
current_stage="complete"
printf 'api36_physical_candidate=pass publication_authorized=false provenance=%s provenance_sha256=%s apk=%s apk_sha256=%s\n' \
  "$CHUMMER_API36_BUILD_PROVENANCE" \
  "$("$sha256sum_command" "$CHUMMER_API36_BUILD_PROVENANCE" | "$cut_command" -d' ' -f1)" \
  "$apk" \
  "$("$sha256sum_command" "$apk" | "$cut_command" -d' ' -f1)"
