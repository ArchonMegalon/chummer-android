#!/usr/bin/env bash
set -euo pipefail
umask 077

repo_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd -P)"
project_path="$repo_dir/src/Chummer.Android/Chummer.Android.csproj"
dotnet_command="${CHUMMER_DOTNET:-dotnet}"
configuration="Release"
framework="net10.0-android36.0"
runtime_id="android-arm64"
package_id="com.myexternalbrain.chummer"
expected_upload_certificate_sha256="D9:C4:B6:35:12:15:44:D5:52:2A:BF:1E:C2:DF:DA:3C:19:38:AA:B9:3D:67:26:BB:93:C9:87:1E:C9:ED:1D:15"
expected_bundletool_sha256="a099cfa1543f55593bc2ed16a70a7c67fe54b1747bb7301f37fdfd6d91028e29"
nuget_org_source="https://api.nuget.org/v3/index.json"
release_tmp=""
seal_tmp=""

fail() {
  printf 'android_release=failed stage=%s\n' "$1" >&2
  exit 1
}

cleanup() {
  local status="$?"
  trap - EXIT HUP INT TERM
  if [[ -n "$seal_tmp" && -f "$seal_tmp" ]]; then
    rm -f -- "$seal_tmp"
  fi
  if [[ -n "$release_tmp" && -d "$release_tmp" ]]; then
    rm -rf -- "$release_tmp"
  fi
  exit "$status"
}

require_command() {
  command -v "$1" >/dev/null 2>&1 || fail "missing-command-$1"
}

require_secret_variable() {
  local variable_name="$1"
  [[ -n "${!variable_name:-}" ]] || fail "signing-variable-$variable_name-missing"
  export "${variable_name?}"
}

require_private_regular_file() {
  local variable_name="$1"
  local value="${!variable_name:-}"
  local resolved permissions
  [[ -n "$value" && "$value" == /* ]] || fail "input-$variable_name-not-absolute"
  [[ ! -L "$value" && -f "$value" ]] || fail "input-$variable_name-not-regular"
  resolved="$(realpath -e -- "$value")"
  [[ "$value" == "$resolved" ]] || fail "input-$variable_name-not-canonical"
  permissions="$(stat -c '%a' -- "$resolved")"
  (( (8#$permissions & 077) == 0 )) || fail "input-$variable_name-not-owner-only"
  [[ "$(stat -c '%u' -- "$resolved")" == "$(id -u)" ]] \
    || fail "input-$variable_name-not-owner-owned"
  printf -v "$variable_name" '%s' "$resolved"
  export "${variable_name?}"
}

require_exact_directory() {
  local variable_name="$1"
  local value="${!variable_name:-}"
  local resolved
  [[ -n "$value" && "$value" == /* ]] || fail "input-$variable_name-not-absolute"
  [[ ! -L "$value" && -d "$value" ]] || fail "input-$variable_name-not-directory"
  resolved="$(realpath -e -- "$value")"
  [[ "$value" == "$resolved" ]] || fail "input-$variable_name-not-canonical"
  printf -v "$variable_name" '%s' "$resolved"
  export "${variable_name?}"
}

require_private_directory() {
  local variable_name="$1"
  local value permissions
  require_exact_directory "$variable_name"
  value="${!variable_name}"
  permissions="$(stat -c '%a' -- "$value")"
  (( (8#$permissions & 077) == 0 )) || fail "input-$variable_name-not-owner-only"
  [[ "$(stat -c '%u' -- "$value")" == "$(id -u)" ]] \
    || fail "input-$variable_name-not-owner-owned"
}

seal_file_no_clobber() {
  local source_path="$1"
  local destination_path="$2"
  local mode="$3"
  local destination_dir destination_name
  destination_dir="$(dirname -- "$destination_path")"
  destination_name="$(basename -- "$destination_path")"
  [[ ! -e "$destination_path" && ! -L "$destination_path" ]] \
    || fail "sealed-output-already-exists"
  seal_tmp="$(mktemp "$destination_dir/.${destination_name}.seal.XXXXXX")"
  install -m "$mode" -- "$source_path" "$seal_tmp"
  ln -- "$seal_tmp" "$destination_path" || fail "sealed-output-collision"
  rm -f -- "$seal_tmp"
  seal_tmp=""
}

trap cleanup EXIT
trap 'exit 129' HUP
trap 'exit 130' INT
trap 'exit 143' TERM

for required in basename chmod cmp cut dirname env git id install jq ln mkdir mktemp openssl python3 realpath rm sha256sum stat; do
  require_command "$required"
done
require_command "$dotnet_command"

workspace_root="${CHUMMER_COMPLETE_ROOT:-}"
[[ -n "$workspace_root" && "$workspace_root" == /* ]] || fail "coherent-workspace-root-missing"
[[ ! -L "$workspace_root" && -d "$workspace_root" ]] || fail "coherent-workspace-root-invalid"
workspace_root="$(realpath -e -- "$workspace_root")"
[[ "$repo_dir" == "$workspace_root/chummer-android" ]] || fail "android-source-not-coherent-sibling"

require_exact_directory AndroidSdkDirectory
require_exact_directory JavaSdkDirectory
require_exact_directory CHUMMER_INTERNAL_PHONE_BETA_PACKAGE_FEED
require_private_directory NUGET_PACKAGES
prepared_packages="$NUGET_PACKAGES"
release_input_root="$(dirname -- "$prepared_packages")"
[[ ! -L "$release_input_root" && -d "$release_input_root" \
  && "$(realpath -e -- "$release_input_root")" == "$release_input_root" \
  && "$(stat -c '%u' -- "$release_input_root")" == "$(id -u)" ]] \
  || fail "release-input-root-invalid"
release_input_permissions="$(stat -c '%a' -- "$release_input_root")"
(( (8#$release_input_permissions & 077) == 0 )) \
  || fail "release-input-root-not-owner-only"
case "$release_input_root/" in
  "$workspace_root/"*) fail "release-input-root-inside-workspace" ;;
esac
require_private_regular_file CHUMMER_ANDROID_RELEASE_PACKAGE_AUTHORITY
require_private_regular_file CHUMMER_CURRENT_UI_PACKAGE_AUTHORITY_RECEIPT
[[ -f "$AndroidSdkDirectory/platforms/android-36/android.jar" ]] \
  || fail "android-api36-platform-missing"
[[ -x "$AndroidSdkDirectory/build-tools/36.0.0/aapt2" ]] \
  || fail "android-build-tools36-missing"
for java_tool in java javac jarsigner keytool; do
  [[ -x "$JavaSdkDirectory/bin/$java_tool" ]] || fail "java-tool-$java_tool-missing"
done
export CHUMMER_JAVA="$JavaSdkDirectory/bin/java"
export CHUMMER_JARSIGNER="$JavaSdkDirectory/bin/jarsigner"
export CHUMMER_KEYTOOL="$JavaSdkDirectory/bin/keytool"
export DOTNET_CLI_USE_MSBUILD_SERVER=0
export MSBUILDDISABLENODEREUSE=1
authority_root="$(dirname -- "$CHUMMER_INTERNAL_PHONE_BETA_PACKAGE_FEED")"
[[ ! -L "$authority_root" && -d "$authority_root" \
  && "$(realpath -e -- "$authority_root")" == "$authority_root" ]] \
  || fail "release-authority-root-invalid"
python3 "$repo_dir/scripts/materialize_release_package_authority.py" \
  --android-root "$repo_dir" \
  --workspace-root "$workspace_root" \
  --presentation-root "$workspace_root/chummer-presentation" \
  --receipt "$CHUMMER_CURRENT_UI_PACKAGE_AUTHORITY_RECEIPT" \
  --package-feed "$CHUMMER_INTERNAL_PHONE_BETA_PACKAGE_FEED" \
  --verify-existing "$CHUMMER_ANDROID_RELEASE_PACKAGE_AUTHORITY"
python3 "$repo_dir/scripts/preflight_native_android_toolchain.py" \
  --repo-root "$repo_dir" \
  --dotnet "$dotnet_command" \
  --android-sdk "$AndroidSdkDirectory" \
  --java-sdk "$JavaSdkDirectory"

# The complete test process tree must never inherit signing material. Keep
# this boundary before any signing-variable dereference/export and scrub both
# the release inputs and adjacent provisioning/recovery secrets by name.
release_test_environment=(
  -u AndroidSigningKeyStore
  -u ChummerAndroidSigningStorePass
  -u ChummerAndroidSigningKeyPass
  -u ChummerAndroidSigningKeyAlias
  -u CHUMMER_ANDROID_UPLOAD_CERTIFICATE_PATH
  -u CHUMMER_ANDROID_PREFLIGHT_STORE_PASSWORD
  -u CHUMMER_ANDROID_SIGNING_DIR
  -u CHUMMER_PROVISION_STORE_PASSWORD
  -u CHUMMER_RECOVERY_STORE_PASSWORD
)
env "${release_test_environment[@]}" \
  python3 -m unittest discover -s "$repo_dir/tests" -v

IFS=$'\t' read -r version_name version_code < <(
  python3 "$repo_dir/scripts/read_android_version.py" "$project_path"
)
[[ "$version_name" == "0.1.0-preview.10" && "$version_code" == "10" ]] \
  || fail "preview10-version-contract-drift"

artifact_dir="$repo_dir/artifacts"
mkdir -p -- "$artifact_dir"
[[ ! -L "$artifact_dir" && "$(realpath -e -- "$artifact_dir")" == "$repo_dir/artifacts" ]] \
  || fail "artifact-directory-not-canonical"
output_aab="$artifact_dir/chummer-android-$version_name-upload.aab"
output_hash="$output_aab.sha256"
output_graph="$artifact_dir/chummer-android-$version_name-source-graph.json"
for output_path in "$output_aab" "$output_hash" "$output_graph"; do
  [[ ! -e "$output_path" && ! -L "$output_path" ]] \
    || fail "versioned-output-already-exists"
done

release_tmp="$(mktemp -d "$release_input_root/.chummer-android-$version_name.release.XXXXXX")"
chmod 0700 "$release_tmp"
staged_graph="$release_tmp/source-graph.json"
staged_publish_dir="$release_tmp/publish"
selected_package_feed="$release_tmp/selected-owner-feed"
isolated_packages="$release_tmp/nuget-packages"
restore_manifest="$release_tmp/restore-consumption.json"
mkdir -m 0700 -- "$staged_publish_dir" "$selected_package_feed" \
  "$isolated_packages"
core_version="$(jq -er \
  '.packagePins | map(.version) | unique | if length == 1 then .[0] else error("Core versions disagree") end' \
  "$CHUMMER_ANDROID_RELEASE_PACKAGE_AUTHORITY")"
campaign_version="$(jq -er \
  '.ownerPackagePins[] | select(.package_id == "Chummer.Campaign.Contracts") | .version' \
  "$CHUMMER_ANDROID_RELEASE_PACKAGE_AUTHORITY")"
run_version="$(jq -er \
  '.ownerPackagePins[] | select(.package_id == "Chummer.Run.Contracts") | .version' \
  "$CHUMMER_ANDROID_RELEASE_PACKAGE_AUTHORITY")"
registry_version="$(jq -er \
  '.ownerPackagePins[] | select(.package_id == "Chummer.Hub.Registry.Contracts") | .version' \
  "$CHUMMER_ANDROID_RELEASE_PACKAGE_AUTHORITY")"
ui_kit_version="$(jq -er \
  '.ownerPackagePins[] | select(.package_id == "Chummer.Ui.Kit") | .version' \
  "$CHUMMER_ANDROID_RELEASE_PACKAGE_AUTHORITY")"
python3 "$repo_dir/scripts/verify_release_source_graph.py" \
  --android-root "$repo_dir" \
  --workspace-root "$workspace_root" \
  --package-authority "$CHUMMER_ANDROID_RELEASE_PACKAGE_AUTHORITY" \
  --authority-root "$authority_root" \
  --output "$staged_graph"
python3 "$repo_dir/scripts/verify_release_publish_output.py" \
  --publish-dir "$staged_publish_dir" \
  --package-id "$package_id" \
  --require-empty

python3 "$repo_dir/scripts/seal_release_restore_consumption.py" snapshot-feed \
  --authority "$CHUMMER_ANDROID_RELEASE_PACKAGE_AUTHORITY" \
  --source "$CHUMMER_INTERNAL_PHONE_BETA_PACKAGE_FEED" \
  --destination "$selected_package_feed" \
  || fail "selected-owner-feed-snapshot"
python3 "$repo_dir/scripts/seal_release_restore_consumption.py" assert-clean \
  --workspace-root "$workspace_root" \
  || fail "release-workspace-stale-bin-obj"

NUGET_PACKAGES="$isolated_packages"
export NUGET_PACKAGES
"$dotnet_command" restore "$project_path" \
  --locked-mode \
  --force-evaluate \
  --disable-parallel \
  --no-http-cache \
  --packages "$isolated_packages" \
  --source "$selected_package_feed" \
  --source "$nuget_org_source" \
  -p:ChummerAndroidRuntimeIdentifier="$runtime_id" \
  -p:ChummerDesktopRuntimeIdentifiers= \
  -p:ChummerPresentationRoot="$workspace_root/chummer-presentation" \
  -p:ChummerCoreEngineRoot="$workspace_root/chummer-core-engine" \
  -p:ChummerUseLocalCompatibilityTree=false \
  -p:ChummerUseLockedOwnerContractPackages=true \
  -p:RestoreLockedMode=true \
  -p:RestorePackagesWithLockFile=true \
  -p:NuGetAudit=false \
  -p:ChummerContractsPackageVersion="$core_version" \
  -p:ChummerCoreRuntimePackageVersion="$core_version" \
  -p:ChummerCampaignContractsPackageVersion="$campaign_version" \
  -p:ChummerRunContractsPackageVersion="$run_version" \
  -p:ChummerHubRegistryContractsPackageVersion="$registry_version" \
  -p:ChummerUiKitPackageVersion="$ui_kit_version" \
  -p:AndroidSdkDirectory="$AndroidSdkDirectory" \
  -p:JavaSdkDirectory="$JavaSdkDirectory"

assets_path="$repo_dir/src/Chummer.Android/obj/project.assets.json"
[[ -f "$assets_path" && ! -L "$assets_path" ]] || fail "locked-restore-assets-missing"
jq -e --arg package_root "$isolated_packages" \
  '.packageFolders | keys | length == 1 and \
   ((.[0] | rtrimstr("/")) == $package_root)' "$assets_path" >/dev/null \
  || fail "locked-restore-assets-package-root-drift"
python3 "$repo_dir/scripts/verify_native_compile_graph.py" \
  --repo-root "$repo_dir" \
  --project "$project_path" \
  --workspace-root "$workspace_root" \
  --assets-only

python3 "$repo_dir/scripts/seal_release_restore_consumption.py" materialize \
  --input-root "$release_tmp" \
  --workspace-root "$workspace_root" \
  --authority "$CHUMMER_ANDROID_RELEASE_PACKAGE_AUTHORITY" \
  --owner-feed "$selected_package_feed" \
  --packages-root "$isolated_packages" \
  --project-lock "$repo_dir/src/Chummer.Android/packages.lock.json" \
  --manifest "$restore_manifest" \
  || fail "locked-restore-consumption-seal"

# Signing material is admitted only after every test and non-signing release
# preflight has completed. None of these values is written to argv or logs.
require_private_regular_file AndroidSigningKeyStore
require_private_regular_file CHUMMER_ANDROID_UPLOAD_CERTIFICATE_PATH
require_private_regular_file CHUMMER_BUNDLETOOL_JAR
case "$AndroidSigningKeyStore" in
  "$repo_dir"/*) fail "signing-keystore-inside-repository" ;;
esac
case "$CHUMMER_ANDROID_UPLOAD_CERTIFICATE_PATH" in
  "$repo_dir"/*) fail "upload-certificate-inside-repository" ;;
esac
require_secret_variable ChummerAndroidSigningStorePass
require_secret_variable ChummerAndroidSigningKeyPass
require_secret_variable ChummerAndroidSigningKeyAlias
[[ "$ChummerAndroidSigningKeyAlias" =~ ^[A-Za-z0-9._-]+$ ]] \
  || fail "signing-key-alias-invalid"

bundletool_sha256="$(sha256sum "$CHUMMER_BUNDLETOOL_JAR" | cut -d' ' -f1)"
[[ "$bundletool_sha256" == "$expected_bundletool_sha256" ]] \
  || fail "bundletool-digest-mismatch"
certificate_sha256="$(openssl x509 -in "$CHUMMER_ANDROID_UPLOAD_CERTIFICATE_PATH" \
  -noout -fingerprint -sha256 | cut -d= -f2)"
[[ "$certificate_sha256" == "$expected_upload_certificate_sha256" ]] \
  || fail "upload-certificate-pin-mismatch"

export CHUMMER_ANDROID_PREFLIGHT_STORE_PASSWORD="$ChummerAndroidSigningStorePass"
if ! "$CHUMMER_KEYTOOL" -exportcert -rfc \
  -keystore "$AndroidSigningKeyStore" \
  -storetype PKCS12 \
  -storepass:env CHUMMER_ANDROID_PREFLIGHT_STORE_PASSWORD \
  -alias "$ChummerAndroidSigningKeyAlias" \
  -file "$release_tmp/keystore-certificate.pem" \
  >"$release_tmp/keytool-preflight.log" 2>&1; then
  unset CHUMMER_ANDROID_PREFLIGHT_STORE_PASSWORD
  fail "signing-keystore-preflight"
fi
unset CHUMMER_ANDROID_PREFLIGHT_STORE_PASSWORD
keystore_certificate_sha256="$(openssl x509 -in "$release_tmp/keystore-certificate.pem" \
  -noout -fingerprint -sha256 | cut -d= -f2)"
[[ "$keystore_certificate_sha256" == "$expected_upload_certificate_sha256" ]] \
  || fail "signing-keystore-certificate-mismatch"

python3 "$repo_dir/scripts/seal_release_restore_consumption.py" verify \
  --input-root "$release_tmp" \
  --workspace-root "$workspace_root" \
  --authority "$CHUMMER_ANDROID_RELEASE_PACKAGE_AUTHORITY" \
  --owner-feed "$selected_package_feed" \
  --packages-root "$isolated_packages" \
  --project-lock "$repo_dir/src/Chummer.Android/packages.lock.json" \
  --manifest "$restore_manifest" \
  || fail "locked-restore-consumption-pre-publish"
"$dotnet_command" publish "$project_path" \
  --configuration "$configuration" \
  --framework "$framework" \
  --self-contained true \
  --output "$staged_publish_dir" \
  --no-restore \
  -m:1 \
  --disable-build-servers \
  -p:UseSharedCompilation=false \
  -p:BuildInParallel=false \
  -p:ChummerAndroidRuntimeIdentifier="$runtime_id" \
  -p:ChummerDesktopRuntimeIdentifiers= \
  -p:ChummerPresentationRoot="$workspace_root/chummer-presentation" \
  -p:ChummerCoreEngineRoot="$workspace_root/chummer-core-engine" \
  -p:ChummerUseLocalCompatibilityTree=false \
  -p:ChummerUseLockedOwnerContractPackages=true \
  -p:RestoreLockedMode=true \
  -p:RestorePackagesWithLockFile=true \
  -p:NuGetAudit=false \
  -p:ChummerContractsPackageVersion="$core_version" \
  -p:ChummerCoreRuntimePackageVersion="$core_version" \
  -p:ChummerCampaignContractsPackageVersion="$campaign_version" \
  -p:ChummerRunContractsPackageVersion="$run_version" \
  -p:ChummerHubRegistryContractsPackageVersion="$registry_version" \
  -p:ChummerUiKitPackageVersion="$ui_kit_version" \
  -p:AndroidSdkDirectory="$AndroidSdkDirectory" \
  -p:JavaSdkDirectory="$JavaSdkDirectory" \
  -p:PublishDir="$staged_publish_dir/" \
  -p:AndroidPackageFormats=aab

unset ChummerAndroidSigningStorePass
unset ChummerAndroidSigningKeyPass
unset ChummerAndroidSigningKeyAlias
unset AndroidSigningKeyStore

python3 "$repo_dir/scripts/seal_release_restore_consumption.py" verify \
  --input-root "$release_tmp" \
  --workspace-root "$workspace_root" \
  --authority "$CHUMMER_ANDROID_RELEASE_PACKAGE_AUTHORITY" \
  --owner-feed "$selected_package_feed" \
  --packages-root "$isolated_packages" \
  --project-lock "$repo_dir/src/Chummer.Android/packages.lock.json" \
  --manifest "$restore_manifest" \
  || fail "locked-restore-consumption-post-publish"

source_aab="$(python3 "$repo_dir/scripts/verify_release_publish_output.py" \
  --publish-dir "$staged_publish_dir" \
  --package-id "$package_id" \
  --resolve-exact-signed-aab)" || fail "fresh-signed-release-bundle-invalid"

"$repo_dir/scripts/validate-aab.sh" "$source_aab"
python3 "$repo_dir/scripts/verify_release_source_graph.py" \
  --android-root "$repo_dir" \
  --workspace-root "$workspace_root" \
  --package-authority "$CHUMMER_ANDROID_RELEASE_PACKAGE_AUTHORITY" \
  --authority-root "$authority_root" \
  --verify-existing "$staged_graph"
python3 "$repo_dir/scripts/materialize_release_package_authority.py" \
  --android-root "$repo_dir" \
  --workspace-root "$workspace_root" \
  --presentation-root "$workspace_root/chummer-presentation" \
  --receipt "$CHUMMER_CURRENT_UI_PACKAGE_AUTHORITY_RECEIPT" \
  --package-feed "$CHUMMER_INTERNAL_PHONE_BETA_PACKAGE_FEED" \
  --verify-existing "$CHUMMER_ANDROID_RELEASE_PACKAGE_AUTHORITY"

source_sha256="$(sha256sum "$source_aab" | cut -d' ' -f1)"
graph_sha256="$(sha256sum "$staged_graph" | cut -d' ' -f1)"
seal_file_no_clobber "$source_aab" "$output_aab" 0644
[[ "$(sha256sum "$output_aab" | cut -d' ' -f1)" == "$source_sha256" ]] \
  || fail "sealed-aab-digest-mismatch"
seal_file_no_clobber "$staged_graph" "$output_graph" 0600
printf '%s  artifacts/%s\n%s  artifacts/%s\n' \
  "$source_sha256" "$(basename "$output_aab")" \
  "$graph_sha256" "$(basename "$output_graph")" \
  > "$release_tmp/aab.sha256"
seal_file_no_clobber "$release_tmp/aab.sha256" "$output_hash" 0600
(cd "$repo_dir" && sha256sum --check "$output_hash" >/dev/null) \
  || fail "sealed-hash-verification"

printf 'android_release=sealed version=%s code=%s aab=%s sha256=%s source_graph=%s source_graph_sha256=%s\n' \
  "$version_name" "$version_code" "$output_aab" "$source_sha256" "$output_graph" "$graph_sha256"
