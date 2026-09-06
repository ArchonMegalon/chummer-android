#!/bin/bash
set -euo pipefail
set +a
umask 077
PATH=/usr/bin:/bin
export PATH

# This process is deliberately a non-authoritative unsigned builder. Reject all
# ambient signing material before the first external command. A same-UID caller
# can read owner-only files, so no local path or environment secret can confer
# signing authority.
ambient_signing_input=false
for release_secret_variable in \
  AndroidSigningKeyStore \
  ChummerAndroidSigningStorePass \
  ChummerAndroidSigningKeyPass \
  ChummerAndroidSigningKeyAlias \
  CHUMMER_ANDROID_PREFLIGHT_STORE_PASSWORD \
  CHUMMER_ANDROID_SIGNING_DIR \
  CHUMMER_PROVISION_STORE_PASSWORD \
  CHUMMER_RECOVERY_STORE_PASSWORD \
  CHUMMER_ANDROID_RELEASE_APPROVER_PRIVATE_KEY \
  CHUMMER_ANDROID_BUILD_ATTESTATION_PRIVATE_KEY \
  CHUMMER_ANDROID_GITHUB_PROVENANCE_TOKEN_FILE; do
  if [[ -v "$release_secret_variable" ]]; then
    ambient_signing_input=true
  fi
  unset "$release_secret_variable"
done
unset release_secret_variable
if [[ "$ambient_signing_input" == true ]]; then
  printf 'android_release=failed stage=external-signer-required-readable-signing-input-rejected publication_authorized=false\n' >&2
  exit 1
fi
unset ambient_signing_input

# No child receives loader, language-runtime, TLS-keylog, Java, or MSBuild
# startup injection from the caller.
for hostile_startup_variable in \
  BASH_ENV ENV CDPATH GIT_EXEC_PATH \
  LD_AUDIT LD_LIBRARY_PATH LD_PRELOAD \
  DYLD_INSERT_LIBRARIES DYLD_LIBRARY_PATH \
  PYTHONHOME PYTHONPATH PYTHONSTARTUP PYTHONINSPECT \
  OPENSSL_CONF OPENSSL_ENGINES OPENSSL_MODULES \
  SSLKEYLOGFILE SSL_CERT_FILE SSL_CERT_DIR \
  JAVA_TOOL_OPTIONS JDK_JAVA_OPTIONS _JAVA_OPTIONS CLASSPATH \
  DOTNET_ROOT DOTNET_ROOT_X64 MSBuildSDKsPath MSBUILD_EXE_PATH \
  NUGET_PLUGIN_PATHS COREHOST_TRACEFILE; do
  unset "$hostile_startup_variable"
done
unset hostile_startup_variable

# Defensive child-environment scrub for every unsigned test subprocess.
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
  -u CHUMMER_ANDROID_TWO_GREEN_ELIGIBILITY_RECEIPT
  -u CHUMMER_ANDROID_TWO_GREEN_RELEASE_APPROVAL
  -u CHUMMER_ANDROID_RELEASE_APPROVER_PRIVATE_KEY
  -u CHUMMER_ANDROID_BUILD_ATTESTATION_PRIVATE_KEY
  -u CHUMMER_ANDROID_GITHUB_PROVENANCE_TOKEN_FILE
  -u CHUMMER_DOTNET
  -u SSLKEYLOGFILE
)
export -n CHUMMER_ANDROID_UPLOAD_CERTIFICATE_PATH 2>/dev/null || true
unset CHUMMER_ANDROID_TWO_GREEN_ELIGIBILITY_RECEIPT
unset CHUMMER_ANDROID_TWO_GREEN_RELEASE_APPROVAL
caller_dotnet="${CHUMMER_DOTNET:-}"
export -n caller_dotnet 2>/dev/null || true
unset CHUMMER_DOTNET

repo_dir="${CHUMMER_RELEASE_REPO_ROOT:-$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd -P)}"
[[ -n "$repo_dir" && "$repo_dir" == /* && ! -L "$repo_dir" && -d "$repo_dir" ]] || exit 1
repo_dir="$(cd "$repo_dir" && pwd -P)"
unset CHUMMER_RELEASE_REPO_ROOT
project_path="$repo_dir/src/Chummer.Android/Chummer.Android.csproj"

# Do not dispatch release commands through caller-controlled PATH lookup. These
# shell functions are parsed from the sealed release script and invoke only the
# canonical root-owned system paths.
basename() { /usr/bin/basename "$@"; }
chmod() { /usr/bin/chmod "$@"; }
cmp() { /usr/bin/cmp "$@"; }
cut() { /usr/bin/cut "$@"; }
dirname() { /usr/bin/dirname "$@"; }
env() { /usr/bin/env "$@"; }
git() { /usr/bin/git "$@"; }
id() { /usr/bin/id "$@"; }
install() { /usr/bin/install "$@"; }
jq() { /usr/bin/jq "$@"; }
mkdir() { /usr/bin/mkdir "$@"; }
mktemp() { /usr/bin/mktemp "$@"; }
openssl() { /usr/bin/openssl "$@"; }
python3() {
  if [[ "${1:-}" == "-c" ]]; then
    /usr/bin/python3 -I -E -S "$@"
    return
  fi
  /usr/bin/python3 -I -E -S -c \
    'import pathlib,runpy,sys; p=pathlib.Path(sys.argv[1]).resolve(strict=True); sys.path.insert(0, str(p.parent)); sys.argv=sys.argv[1:]; runpy.run_path(str(p), run_name="__main__")' \
    "$@"
}
realpath() { /usr/bin/realpath "$@"; }
rm() { /usr/bin/rm "$@"; }
sha256sum() { /usr/bin/sha256sum "$@"; }
stat() { /usr/bin/stat "$@"; }
dotnet_command=""
configuration="Release"
framework="net10.0-android36.0"
runtime_id="android-arm64"
package_id="com.myexternalbrain.chummer"
expected_upload_certificate_sha256="D9:C4:B6:35:12:15:44:D5:52:2A:BF:1E:C2:DF:DA:3C:19:38:AA:B9:3D:67:26:BB:93:C9:87:1E:C9:ED:1D:15"
expected_bundletool_sha256="a099cfa1543f55593bc2ed16a70a7c67fe54b1747bb7301f37fdfd6d91028e29"
nuget_org_source="https://api.nuget.org/v3/index.json"
release_tmp=""

fail() {
  printf 'android_release=failed stage=%s\n' "$1" >&2
  exit 1
}

cleanup() {
  local status="$?"
  trap - EXIT HUP INT TERM
  if [[ -n "$release_tmp" && -d "$release_tmp" ]]; then
    rm -rf -- "$release_tmp"
  fi
  exit "$status"
}

require_command() {
  command -v "$1" >/dev/null 2>&1 || fail "missing-command-$1"
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

trap cleanup EXIT
trap 'exit 129' HUP
trap 'exit 130' INT
trap 'exit 143' TERM

for required in basename chmod cmp cut dirname env git id install jq mkdir mktemp openssl python3 realpath rm sha256sum stat; do
  require_command "$required"
done
expected_version_name="${CHUMMER_ANDROID_EXPECTED_VERSION_NAME:-}"
expected_version_code="${CHUMMER_ANDROID_EXPECTED_VERSION_CODE:-}"
[[ -n "$expected_version_name" && -n "$expected_version_code" ]] \
  || fail "release-version-intent-missing"
release_version_pair="$(python3 "$repo_dir/scripts/verify_android_release_intent.py" \
  --project "$project_path" \
  --expected-version-name "$expected_version_name" \
  --expected-version-code "$expected_version_code")" \
  || fail "release-version-intent-invalid"
IFS=$'\t' read -r version_name version_code version_extra <<< "$release_version_pair"
[[ -n "$version_name" && -n "$version_code" && -z "${version_extra:-}" \
  && "$release_version_pair" == "$version_name"$'\t'"$version_code" ]] \
  || fail "release-version-intent-ambiguous"
unset release_version_pair expected_version_name expected_version_code version_extra
unset CHUMMER_ANDROID_EXPECTED_VERSION_NAME CHUMMER_ANDROID_EXPECTED_VERSION_CODE

require_private_regular_file CHUMMER_ANDROID_RELEASE_TOOLCHAIN_AUTHORITY
release_toolchain_authority="$CHUMMER_ANDROID_RELEASE_TOOLCHAIN_AUTHORITY"
toolchain_identity="$(python3 "$repo_dir/scripts/sign_android_release_build_attestation.py" \
  verify-toolchain \
  --authority "$CHUMMER_ANDROID_RELEASE_TOOLCHAIN_AUTHORITY")" \
  || fail "trusted-release-toolchain-invalid"
trusted_dotnet="$(jq -er '.dotnetPath' <<<"$toolchain_identity")" \
  || fail "trusted-dotnet-path-absent"
trusted_java_sdk="$(jq -er '.javaSdkRoot' <<<"$toolchain_identity")" \
  || fail "trusted-java-sdk-path-absent"
[[ -z "$caller_dotnet" || "$caller_dotnet" == "$trusted_dotnet" ]] \
  || fail "caller-dotnet-differs-from-trusted-toolchain"
[[ -n "${JavaSdkDirectory:-}" && "$(realpath -e -- "$JavaSdkDirectory")" == "$trusted_java_sdk" ]] \
  || fail "caller-java-sdk-differs-from-trusted-toolchain"
dotnet_command="$trusted_dotnet"
JavaSdkDirectory="$trusted_java_sdk"
unset toolchain_identity trusted_dotnet trusted_java_sdk caller_dotnet
export -n CHUMMER_ANDROID_RELEASE_TOOLCHAIN_AUTHORITY 2>/dev/null || true
unset CHUMMER_ANDROID_RELEASE_TOOLCHAIN_AUTHORITY

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
two_green_receipt="$release_input_root/ANDROID_API36_TWO_GREEN_ELIGIBILITY.generated.json"
two_green_approval="$release_input_root/ANDROID_API36_TWO_GREEN_RELEASE_APPROVAL.generated.json"
for protected_input in "$two_green_receipt" "$two_green_approval"; do
  [[ ! -L "$protected_input" && -f "$protected_input" \
    && "$(realpath -e -- "$protected_input")" == "$protected_input" \
    && "$(stat -c '%u' -- "$protected_input")" == "$(id -u)" ]] \
    || fail "two-green-protected-input-invalid"
  protected_permissions="$(stat -c '%a' -- "$protected_input")"
  (( (8#$protected_permissions & 077) == 0 )) \
    || fail "two-green-protected-input-not-owner-only"
done
unset protected_input protected_permissions
eligibility_sha256="$(sha256sum "$two_green_receipt" | cut -d' ' -f1)"
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
python3 "$repo_dir/scripts/verify_api36_two_green_release_eligibility.py" \
  --receipt "$two_green_receipt" \
  --approval "$two_green_approval" \
  --android-root "$repo_dir" \
  --expected-version-name "$version_name" \
  --expected-version-code "$version_code" \
  --package-authority "$CHUMMER_ANDROID_RELEASE_PACKAGE_AUTHORITY" >/dev/null \
  || fail "two-green-release-input-binding-invalid"
python3 "$repo_dir/scripts/preflight_native_android_toolchain.py" \
  --repo-root "$repo_dir" \
  --dotnet "$dotnet_command" \
  --android-sdk "$AndroidSdkDirectory" \
  --java-sdk "$JavaSdkDirectory"

# The complete test process tree receives a second, defensive environment
# scrub even though the imported variables are already non-exported.
env "${release_test_environment[@]}" \
  python3 -m unittest discover -s "$repo_dir/tests" -v

artifact_dir="$release_input_root/artifacts"
mkdir -p -- "$artifact_dir"
[[ ! -L "$artifact_dir" && "$(realpath -e -- "$artifact_dir")" == "$release_input_root/artifacts" ]] \
  || fail "artifact-directory-not-canonical"
output_aab="$artifact_dir/chummer-android-$version_name-unsigned.aab"
output_hash="$output_aab.sha256"
output_graph="$artifact_dir/chummer-android-$version_name-source-graph.json"
for output_path in "$output_aab" "$output_hash" "$output_graph"; do
  [[ ! -e "$output_path" && ! -L "$output_path" ]] \
    || fail "versioned-output-already-exists"
done

release_tmp="$(mktemp -d "$release_input_root/.chummer-android-$version_name.release.XXXXXX")"
chmod 0700 "$release_tmp"
release_attempt_id="$(basename -- "$release_tmp")"
restore_drift_diagnostic="$release_input_root/$release_attempt_id.restore-drift.json"
[[ ! -e "$restore_drift_diagnostic" && ! -L "$restore_drift_diagnostic" ]] \
  || fail "restore-drift-diagnostic-already-exists"
staged_graph="$release_tmp/source-graph.json"
staged_publish_dir="$release_tmp/publish"
selected_package_feed="$release_tmp/selected-owner-feed"
isolated_packages="$release_tmp/nuget-packages"
routed_locks="$release_tmp/project-locks"
restore_manifest="$release_tmp/restore-consumption.json"
release_intermediate="$release_tmp/intermediate"
external_signer_request="$release_input_root/$release_attempt_id.external-signer-request.json"
[[ ! -e "$external_signer_request" && ! -L "$external_signer_request" ]] \
  || fail "external-signer-request-already-exists"
mkdir -m 0700 -- "$staged_publish_dir" "$selected_package_feed" \
  "$isolated_packages" "$routed_locks" "$release_intermediate"
install -m 0600 -- \
  "$repo_dir/src/Chummer.Android/packages.lock.json" \
  "$routed_locks/Chummer.Android.packages.lock.json"
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
  --expected-version-name "$version_name" \
  --expected-version-code "$version_code" \
  --output "$staged_graph"
python3 "$repo_dir/scripts/verify_api36_two_green_release_eligibility.py" \
  --receipt "$two_green_receipt" \
  --approval "$two_green_approval" \
  --android-root "$repo_dir" \
  --expected-version-name "$version_name" \
  --expected-version-code "$version_code" \
  --package-authority "$CHUMMER_ANDROID_RELEASE_PACKAGE_AUTHORITY" \
  --source-graph "$staged_graph" >/dev/null \
  || fail "two-green-release-graph-binding-invalid"
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
  -p:CustomBeforeMicrosoftCommonProps="$repo_dir/eng/ReleaseRestoreRouting.props" \
  -p:ChummerReleaseLockRoot="$routed_locks" \
  -p:ChummerReleaseIntermediateRoot="$release_intermediate" \
  -p:NuGetAudit=false \
  -p:ChummerContractsPackageVersion="$core_version" \
  -p:ChummerCoreRuntimePackageVersion="$core_version" \
  -p:ChummerCampaignContractsPackageVersion="$campaign_version" \
  -p:ChummerRunContractsPackageVersion="$run_version" \
  -p:ChummerHubRegistryContractsPackageVersion="$registry_version" \
  -p:ChummerUiKitPackageVersion="$ui_kit_version" \
  -p:AndroidSdkDirectory="$AndroidSdkDirectory" \
  -p:JavaSdkDirectory="$JavaSdkDirectory"

for lock_name in \
  Chummer.Android.packages.lock.json \
  Chummer.Desktop.Runtime.packages.lock.json \
  Chummer.Presentation.packages.lock.json; do
  lock_path="$routed_locks/$lock_name"
  [[ ! -L "$lock_path" && -f "$lock_path" \
    && "$(stat -c '%u' -- "$lock_path")" == "$(id -u)" ]] \
    || fail "routed-project-lock-$lock_name-invalid"
  lock_permissions="$(stat -c '%a' -- "$lock_path")"
  (( (8#$lock_permissions & 077) == 0 )) \
    || fail "routed-project-lock-$lock_name-not-owner-only"
done
cmp --silent \
  "$repo_dir/src/Chummer.Android/packages.lock.json" \
  "$routed_locks/Chummer.Android.packages.lock.json" \
  || fail "routed-android-lock-drift"

assets_path="$release_intermediate/Chummer.Android/project.assets.json"
[[ -f "$assets_path" && ! -L "$assets_path" ]] || fail "locked-restore-assets-missing"
jq -e --arg package_root "$isolated_packages" \
  '.packageFolders | keys | length == 1 and
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
  --routed-lock-root "$routed_locks" \
  --project-lock "$repo_dir/src/Chummer.Android/packages.lock.json" \
  --manifest "$restore_manifest" \
  || fail "locked-restore-consumption-seal"

# Recheck the complete release binding immediately before the unsigned build.
# This build-user process deliberately admits no production signing key. Under
# the same-UID filesystem attacker model, even an owner-only mode-0600 key is
# readable and replaceable. Signing is a separate privileged external lane.
python3 "$repo_dir/scripts/verify_api36_two_green_release_eligibility.py" \
  --receipt "$two_green_receipt" \
  --approval "$two_green_approval" \
  --android-root "$repo_dir" \
  --expected-version-name "$version_name" \
  --expected-version-code "$version_code" \
  --package-authority "$CHUMMER_ANDROID_RELEASE_PACKAGE_AUTHORITY" \
  --source-graph "$staged_graph" >/dev/null \
  || fail "two-green-pre-build-binding-invalid"
python3 "$repo_dir/scripts/verify_release_private_key_hygiene.py" \
  --repo-root "$repo_dir" \
  || fail "repository-private-key-hygiene"
require_private_regular_file CHUMMER_ANDROID_UPLOAD_CERTIFICATE_PATH
require_private_regular_file CHUMMER_BUNDLETOOL_JAR
case "$CHUMMER_ANDROID_UPLOAD_CERTIFICATE_PATH" in
  "$repo_dir"/*) fail "upload-certificate-inside-repository" ;;
esac
for forbidden_signing_input in \
  AndroidSigningKeyStore \
  ChummerAndroidSigningStorePass \
  ChummerAndroidSigningKeyPass \
  ChummerAndroidSigningKeyAlias; do
  [[ ! -v "$forbidden_signing_input" ]] \
    || fail "external-signer-required-readable-signing-input-rejected"
done
unset forbidden_signing_input

bundletool_sha256="$(sha256sum "$CHUMMER_BUNDLETOOL_JAR" | cut -d' ' -f1)"
[[ "$bundletool_sha256" == "$expected_bundletool_sha256" ]] \
  || fail "bundletool-digest-mismatch"
certificate_sha256="$(openssl x509 -in "$CHUMMER_ANDROID_UPLOAD_CERTIFICATE_PATH" \
  -noout -fingerprint -sha256 | cut -d= -f2)"
[[ "$certificate_sha256" == "$expected_upload_certificate_sha256" ]] \
  || fail "upload-certificate-pin-mismatch"

env "${release_test_environment[@]}" \
  python3 "$repo_dir/scripts/seal_release_restore_consumption.py" verify \
  --input-root "$release_tmp" \
  --workspace-root "$workspace_root" \
  --authority "$CHUMMER_ANDROID_RELEASE_PACKAGE_AUTHORITY" \
  --owner-feed "$selected_package_feed" \
  --packages-root "$isolated_packages" \
  --routed-lock-root "$routed_locks" \
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
  -p:CustomBeforeMicrosoftCommonProps="$repo_dir/eng/ReleaseRestoreRouting.props" \
  -p:ChummerReleaseLockRoot="$routed_locks" \
  -p:ChummerReleaseIntermediateRoot="$release_intermediate" \
  -p:NuGetAudit=false \
  -p:ChummerContractsPackageVersion="$core_version" \
  -p:ChummerCoreRuntimePackageVersion="$core_version" \
  -p:ChummerCampaignContractsPackageVersion="$campaign_version" \
  -p:ChummerRunContractsPackageVersion="$run_version" \
  -p:ChummerHubRegistryContractsPackageVersion="$registry_version" \
  -p:ChummerUiKitPackageVersion="$ui_kit_version" \
  -p:AndroidSdkDirectory="$AndroidSdkDirectory" \
  -p:JavaSdkDirectory="$JavaSdkDirectory" \
  -p:ApplicationDisplayVersion="$version_name" \
  -p:ApplicationVersion="$version_code" \
  -p:PublishDir="$staged_publish_dir/" \
  -p:AndroidKeyStore=false \
  -p:AndroidPackageFormats=aab

python3 "$repo_dir/scripts/seal_release_restore_consumption.py" verify \
  --input-root "$release_tmp" \
  --workspace-root "$workspace_root" \
  --authority "$CHUMMER_ANDROID_RELEASE_PACKAGE_AUTHORITY" \
  --owner-feed "$selected_package_feed" \
  --packages-root "$isolated_packages" \
  --routed-lock-root "$routed_locks" \
  --project-lock "$repo_dir/src/Chummer.Android/packages.lock.json" \
  --manifest "$restore_manifest" \
  --drift-diagnostic "$restore_drift_diagnostic" \
  || fail "locked-restore-consumption-post-publish"

source_aab="$(python3 "$repo_dir/scripts/verify_release_publish_output.py" \
  --publish-dir "$staged_publish_dir" \
  --package-id "$package_id" \
  --resolve-exact-unsigned-aab)" || fail "fresh-unsigned-release-bundle-invalid"

# Capture, validate, and promote the unsigned handoff inside one process. The
# non-authoritative request records exact descriptor-held inputs, but it never
# grants signing or publication. A separately hosted privileged signer must
# independently replay every bound authority before returning a signed AAB.
/usr/bin/python3 "$repo_dir/scripts/sign_android_release_build_attestation.py" prepare-external-signer \
  --aab "$source_aab" \
  --source-graph "$staged_graph" \
  --output-aab "$output_aab" \
  --output-source-graph "$output_graph" \
  --output-sidecar "$output_hash" \
  --output-request "$external_signer_request" \
  --two-green-receipt "$two_green_receipt" \
  --two-green-approval "$two_green_approval" \
  --workspace-root "$workspace_root" \
  --package-authority "$CHUMMER_ANDROID_RELEASE_PACKAGE_AUTHORITY" \
  --authority-root "$authority_root" \
  --bundletool "$CHUMMER_BUNDLETOOL_JAR" \
  --upload-certificate "$CHUMMER_ANDROID_UPLOAD_CERTIFICATE_PATH" \
  --java-tool-authority "$release_toolchain_authority" \
  || fail "unsigned-external-signer-handoff"
source_sha256="$(/usr/bin/sha256sum "$output_aab" | /usr/bin/cut -d' ' -f1)"
graph_sha256="$(/usr/bin/sha256sum "$output_graph" | /usr/bin/cut -d' ' -f1)"
(cd "$repo_dir" && /usr/bin/sha256sum --check "$output_hash" >/dev/null) \
  || fail "sealed-hash-verification"

printf 'android_release=external-signer-required version=%s code=%s unsigned_aab=%s sha256=%s source_graph=%s source_graph_sha256=%s signer_request=%s two_green_receipt_sha256=%s signing_authorized=false publication_authorized=false google_play_upload_authorized=false\n' \
  "$version_name" "$version_code" "$output_aab" "$source_sha256" \
  "$output_graph" "$graph_sha256" "$external_signer_request" "$eligibility_sha256"
exit 3
