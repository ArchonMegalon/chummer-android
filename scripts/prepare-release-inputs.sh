#!/usr/bin/env bash
set -euo pipefail
umask 077

repo_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd -P)"
project_path="$repo_dir/src/Chummer.Android/Chummer.Android.csproj"
dotnet_command="${CHUMMER_DOTNET:-dotnet}"
nuget_org_source="https://api.nuget.org/v3/index.json"

fail() {
  printf 'android_release_inputs=failed stage=%s publication_authorized=false\n' "$1" >&2
  exit 1
}

require_command() {
  command -v "$1" >/dev/null 2>&1 || fail "missing-command-$1"
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

for required in cmp dirname find git id install jq mkdir python3 realpath stat; do
  require_command "$required"
done
require_command "$dotnet_command"

workspace_root="${CHUMMER_COMPLETE_ROOT:-}"
[[ -n "$workspace_root" && "$workspace_root" == /* ]] \
  || fail "coherent-workspace-root-missing"
[[ ! -L "$workspace_root" && -d "$workspace_root" ]] \
  || fail "coherent-workspace-root-invalid"
workspace_root="$(realpath -e -- "$workspace_root")"
[[ "$repo_dir" == "$workspace_root/chummer-android" ]] \
  || fail "android-source-not-coherent-sibling"
presentation_root="$workspace_root/chummer-presentation"
core_root="$workspace_root/chummer-core-engine"
[[ ! -L "$presentation_root" && -d "$presentation_root" ]] \
  || fail "presentation-source-missing"
[[ ! -L "$core_root" && -d "$core_root" ]] || fail "core-source-missing"

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
unset release_version_pair version_extra

require_exact_directory AndroidSdkDirectory
require_exact_directory JavaSdkDirectory
require_private_regular_file CHUMMER_CURRENT_UI_PACKAGE_AUTHORITY_RECEIPT
require_private_regular_file CHUMMER_ANDROID_TWO_GREEN_ELIGIBILITY_RECEIPT
require_exact_directory CHUMMER_INTERNAL_PHONE_BETA_PACKAGE_FEED
eligibility_sha256="${CHUMMER_ANDROID_TWO_GREEN_ELIGIBILITY_SHA256:-}"
[[ "$eligibility_sha256" =~ ^[0-9a-f]{64}$ ]] \
  || fail "two-green-eligibility-sha256-invalid"
python3 "$repo_dir/scripts/verify_api36_two_green_release_eligibility.py" \
  --receipt "$CHUMMER_ANDROID_TWO_GREEN_ELIGIBILITY_RECEIPT" \
  --expected-receipt-sha256 "$eligibility_sha256" \
  --android-root "$repo_dir" \
  --expected-version-name "$version_name" \
  --expected-version-code "$version_code" >/dev/null \
  || fail "two-green-eligibility-invalid"

input_dir="${CHUMMER_ANDROID_RELEASE_INPUT_DIR:-}"
[[ -n "$input_dir" && "$input_dir" == /* ]] || fail "release-input-directory-not-absolute"
[[ ! -L "$input_dir" && -d "$input_dir" ]] || fail "release-input-directory-not-directory"
input_dir="$(realpath -e -- "$input_dir")"
case "$input_dir/" in
  "$workspace_root/"*) fail "release-input-directory-inside-workspace" ;;
esac
[[ "$(stat -c '%u' -- "$input_dir")" == "$(id -u)" ]] \
  || fail "release-input-directory-not-owner-owned"
input_permissions="$(stat -c '%a' -- "$input_dir")"
(( (8#$input_permissions & 077) == 0 )) || fail "release-input-directory-not-owner-only"
[[ -z "$(find "$input_dir" -mindepth 1 -maxdepth 1 -print -quit)" ]] \
  || fail "release-input-directory-not-empty"

authority="$input_dir/chummer.android.release-package-authority.v2.json"
nuget_packages="$input_dir/nuget-packages"
preparation_obj="$input_dir/preparation-obj"
project_locks="$input_dir/project-locks"
environment_file="$input_dir/release-inputs.env"
prepared_eligibility="$input_dir/ANDROID_API36_TWO_GREEN_ELIGIBILITY.generated.json"
mkdir -m 0700 -- "$nuget_packages" "$preparation_obj" "$project_locks"
install -m 0600 -- \
  "$CHUMMER_ANDROID_TWO_GREEN_ELIGIBILITY_RECEIPT" \
  "$prepared_eligibility"
CHUMMER_ANDROID_TWO_GREEN_ELIGIBILITY_RECEIPT="$prepared_eligibility"
export CHUMMER_ANDROID_TWO_GREEN_ELIGIBILITY_RECEIPT
install -m 0600 -- \
  "$repo_dir/src/Chummer.Android/packages.lock.json" \
  "$project_locks/Chummer.Android.packages.lock.json"

python3 "$repo_dir/scripts/materialize_release_package_authority.py" \
  --android-root "$repo_dir" \
  --workspace-root "$workspace_root" \
  --presentation-root "$presentation_root" \
  --receipt "$CHUMMER_CURRENT_UI_PACKAGE_AUTHORITY_RECEIPT" \
  --package-feed "$CHUMMER_INTERNAL_PHONE_BETA_PACKAGE_FEED" \
  --output "$authority"

core_version="$(jq -er \
  '.packagePins | map(.version) | unique | if length == 1 then .[0] else error("Core versions disagree") end' \
  "$authority")"
campaign_version="$(jq -er \
  '.ownerPackagePins[] | select(.package_id == "Chummer.Campaign.Contracts") | .version' \
  "$authority")"
run_version="$(jq -er \
  '.ownerPackagePins[] | select(.package_id == "Chummer.Run.Contracts") | .version' \
  "$authority")"
registry_version="$(jq -er \
  '.ownerPackagePins[] | select(.package_id == "Chummer.Hub.Registry.Contracts") | .version' \
  "$authority")"
ui_kit_version="$(jq -er \
  '.ownerPackagePins[] | select(.package_id == "Chummer.Ui.Kit") | .version' \
  "$authority")"

package_arguments=(
  "-p:ChummerPresentationRoot=$presentation_root"
  "-p:ChummerCoreEngineRoot=$core_root"
  "-p:ChummerDesktopRuntimeIdentifiers="
  "-p:ChummerUseLocalCompatibilityTree=false"
  "-p:ChummerUseLockedOwnerContractPackages=true"
  "-p:RestoreLockedMode=true"
  "-p:RestorePackagesWithLockFile=true"
  "-p:CustomBeforeMicrosoftCommonProps=$repo_dir/eng/ReleaseRestoreRouting.props"
  "-p:ChummerReleaseLockRoot=$project_locks"
  "-p:ChummerReleaseIntermediateRoot=$preparation_obj"
  "-p:NuGetAudit=false"
  "-p:AndroidSdkDirectory=$AndroidSdkDirectory"
  "-p:AndroidSdkBuildToolsVersion=36.0.0"
  "-p:JavaSdkDirectory=$JavaSdkDirectory"
  "-p:ChummerContractsPackageVersion=$core_version"
  "-p:ChummerCoreRuntimePackageVersion=$core_version"
  "-p:ChummerCampaignContractsPackageVersion=$campaign_version"
  "-p:ChummerRunContractsPackageVersion=$run_version"
  "-p:ChummerHubRegistryContractsPackageVersion=$registry_version"
  "-p:ChummerUiKitPackageVersion=$ui_kit_version"
)

export DOTNET_CLI_USE_MSBUILD_SERVER=0
export MSBUILDDISABLENODEREUSE=1
export NUGET_PACKAGES="$nuget_packages"
"$dotnet_command" restore "$project_path" \
  --locked-mode \
  --force-evaluate \
  --disable-parallel \
  --no-http-cache \
  --packages "$nuget_packages" \
  --source "$CHUMMER_INTERNAL_PHONE_BETA_PACKAGE_FEED" \
  --source "$nuget_org_source" \
  "${package_arguments[@]}"
for lock_name in \
  Chummer.Android.packages.lock.json \
  Chummer.Desktop.Runtime.packages.lock.json \
  Chummer.Presentation.packages.lock.json; do
  lock_path="$project_locks/$lock_name"
  [[ ! -L "$lock_path" && -f "$lock_path" \
    && "$(stat -c '%u' -- "$lock_path")" == "$(id -u)" ]] \
    || fail "routed-project-lock-$lock_name-invalid"
  lock_permissions="$(stat -c '%a' -- "$lock_path")"
  (( (8#$lock_permissions & 077) == 0 )) \
    || fail "routed-project-lock-$lock_name-not-owner-only"
done
cmp --silent \
  "$repo_dir/src/Chummer.Android/packages.lock.json" \
  "$project_locks/Chummer.Android.packages.lock.json" \
  || fail "routed-android-lock-drift"
python3 "$repo_dir/scripts/verify_native_compile_graph.py" \
  --repo-root "$repo_dir" \
  --project "$project_path" \
  --workspace-root "$workspace_root" \
  --assets-root "$preparation_obj/Chummer.Android" \
  --assets-only
python3 "$repo_dir/scripts/materialize_release_package_authority.py" \
  --android-root "$repo_dir" \
  --workspace-root "$workspace_root" \
  --presentation-root "$presentation_root" \
  --receipt "$CHUMMER_CURRENT_UI_PACKAGE_AUTHORITY_RECEIPT" \
  --package-feed "$CHUMMER_INTERNAL_PHONE_BETA_PACKAGE_FEED" \
  --verify-existing "$authority"
python3 "$repo_dir/scripts/verify_api36_two_green_release_eligibility.py" \
  --receipt "$CHUMMER_ANDROID_TWO_GREEN_ELIGIBILITY_RECEIPT" \
  --expected-receipt-sha256 "$eligibility_sha256" \
  --android-root "$repo_dir" \
  --expected-version-name "$version_name" \
  --expected-version-code "$version_code" \
  --package-authority "$authority" >/dev/null \
  || fail "two-green-release-input-binding-invalid"

{
  printf 'export CHUMMER_ANDROID_RELEASE_PACKAGE_AUTHORITY=%q\n' "$authority"
  printf 'export CHUMMER_CURRENT_UI_PACKAGE_AUTHORITY_RECEIPT=%q\n' \
    "$CHUMMER_CURRENT_UI_PACKAGE_AUTHORITY_RECEIPT"
  printf 'export CHUMMER_INTERNAL_PHONE_BETA_PACKAGE_FEED=%q\n' \
    "$CHUMMER_INTERNAL_PHONE_BETA_PACKAGE_FEED"
  printf 'export NUGET_PACKAGES=%q\n' "$nuget_packages"
  printf 'export CHUMMER_ANDROID_TWO_GREEN_ELIGIBILITY_RECEIPT=%q\n' \
    "$CHUMMER_ANDROID_TWO_GREEN_ELIGIBILITY_RECEIPT"
  printf 'export CHUMMER_ANDROID_TWO_GREEN_ELIGIBILITY_SHA256=%q\n' \
    "$eligibility_sha256"
  printf 'export CHUMMER_ANDROID_EXPECTED_VERSION_NAME=%q\n' "$version_name"
  printf 'export CHUMMER_ANDROID_EXPECTED_VERSION_CODE=%q\n' "$version_code"
} > "$environment_file"
chmod 0600 "$environment_file"

printf 'android_release_inputs=prepared authority=%s environment=%s package_cache=%s two_green_sha256=%s publication_authorized=false\n' \
  "$authority" "$environment_file" "$nuget_packages" "$eligibility_sha256"
