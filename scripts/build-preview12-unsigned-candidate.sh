#!/usr/bin/env bash
set -euo pipefail
umask 077

repo_dir="$(cd "${BASH_SOURCE[0]%/*}/.." && pwd -P)"
project="$repo_dir/src/Chummer.Android/Chummer.Android.csproj"
dotnet_command="${CHUMMER_DOTNET:?CHUMMER_DOTNET is required}"
workspace_root="${CHUMMER_COMPLETE_ROOT:?CHUMMER_COMPLETE_ROOT is required}"
output_dir="${CHUMMER_PREVIEW12_OUTPUT_DIR:?CHUMMER_PREVIEW12_OUTPUT_DIR is required}"
presentation_root="${CHUMMER_PRESENTATION_ROOT:?CHUMMER_PRESENTATION_ROOT is required}"
core_root="${CHUMMER_CORE_ENGINE_ROOT:?CHUMMER_CORE_ENGINE_ROOT is required}"
run_services_root="${CHUMMER_RUN_SERVICES_ROOT:?CHUMMER_RUN_SERVICES_ROOT is required}"
registry_root="${CHUMMER_HUB_REGISTRY_ROOT:?CHUMMER_HUB_REGISTRY_ROOT is required}"
ui_kit_root="${CHUMMER_UI_KIT_ROOT:?CHUMMER_UI_KIT_ROOT is required}"
media_root="${CHUMMER_MEDIA_FACTORY_ROOT:?CHUMMER_MEDIA_FACTORY_ROOT is required}"

fail() {
  printf 'preview12_unsigned_candidate=blocked stage=%s signing_authorized=false publication_authorized=false\n' "$1" >&2
  exit 1
}

for forbidden in \
  AndroidSigningKeyStore ChummerAndroidSigningStorePass \
  ChummerAndroidSigningKeyPass ChummerAndroidSigningKeyAlias \
  CHUMMER_ANDROID_UPLOAD_CERTIFICATE_PATH \
  CHUMMER_ANDROID_RELEASE_APPROVER_PRIVATE_KEY \
  CHUMMER_ANDROID_BUILD_ATTESTATION_PRIVATE_KEY \
  PLAY_SERVICE_ACCOUNT_JSON PLAY_PUBLISHER_CREDENTIALS GOOGLE_APPLICATION_CREDENTIALS; do
  [[ ! -v "$forbidden" ]] || fail "signing-or-publication-input-present"
done
unset forbidden

for root_name in workspace_root output_dir presentation_root core_root \
  run_services_root registry_root ui_kit_root media_root; do
  root_value="${!root_name}"
  [[ "$root_value" == /* && ! -L "$root_value" && -d "$root_value" ]] \
    || fail "unsafe-$root_name"
  [[ "$(realpath -e -- "$root_value")" == "$root_value" ]] || fail "noncanonical-$root_name"
done
unset root_name root_value
[[ "$repo_dir" == "$workspace_root/chummer-android" ]] || fail "android-root-outside-coherent-workspace"
[[ -x "$dotnet_command" && ! -L "$dotnet_command" ]] || fail "dotnet-unavailable"
[[ -z "$(find "$output_dir" -mindepth 1 -maxdepth 1 -print -quit)" ]] || fail "output-not-empty"
[[ -z "$(git -C "$repo_dir" status --porcelain=v1 --untracked-files=all)" ]] || fail "source-dirty"

version_pair="$(python3 "$repo_dir/scripts/read_android_version.py" "$project")" || fail "version-read"
[[ "$version_pair" == $'0.1.0-preview.12\t12' ]] || fail "version-not-preview12-code12"
source_commit="$(git -C "$repo_dir" rev-parse HEAD)"
source_epoch="$(git -C "$repo_dir" show -s --format=%ct HEAD)"
[[ "$source_commit" =~ ^[0-9a-f]{40}$ && "$source_epoch" =~ ^[1-9][0-9]*$ ]] \
  || fail "source-identity-invalid"
export SOURCE_DATE_EPOCH="$source_epoch"
export TZ=UTC

local_tree_args=(
  "-p:RestorePackagesWithLockFile=true"
  "-p:NuGetLockFilePath=obj/preview12.${source_commit}.packages.lock.json"
  "-p:ChummerPresentationRoot=$presentation_root"
  "-p:ChummerCoreEngineRoot=$core_root"
  "-p:ChummerLocalContractsProject=$core_root/Chummer.Contracts/Chummer.Contracts.csproj"
  "-p:ChummerLocalCampaignContractsProject=$run_services_root/Chummer.Campaign.Contracts/Chummer.Campaign.Contracts.csproj"
  "-p:ChummerLocalHubRegistryContractsProject=$registry_root/Chummer.Hub.Registry.Contracts/Chummer.Hub.Registry.Contracts.csproj"
  "-p:ChummerLocalRunContractsProject=$run_services_root/Chummer.Run.Contracts/Chummer.Run.Contracts.csproj"
  "-p:ChummerLocalUiKitProject=$ui_kit_root/src/Chummer.Ui.Kit/Chummer.Ui.Kit.csproj"
  "-p:ChummerLocalMediaContractsProject=$media_root/src/Chummer.Media.Contracts/Chummer.Media.Contracts.csproj"
)

"$dotnet_command" restore "$project" \
  --disable-parallel \
  -p:ChummerAndroidRuntimeIdentifier=android-arm64 \
  -p:ChummerDesktopRuntimeIdentifiers= \
  -p:ChummerUseLocalCompatibilityTree=true \
  "${local_tree_args[@]}"

publish_dir="$output_dir/publish"
install -d -m 0700 "$publish_dir"
"$dotnet_command" publish "$project" \
  --configuration Release \
  --framework net10.0-android36.0 \
  --runtime android-arm64 \
  --self-contained true \
  --no-restore \
  --output "$publish_dir" \
  -m:1 \
  --disable-build-servers \
  -p:UseSharedCompilation=false \
  -p:BuildInParallel=false \
  -p:ContinuousIntegrationBuild=true \
  -p:Deterministic=true \
  -p:ChummerAndroidRuntimeIdentifier=android-arm64 \
  -p:ChummerDesktopRuntimeIdentifiers= \
  -p:ChummerUseLocalCompatibilityTree=true \
  -p:ApplicationDisplayVersion=0.1.0-preview.12 \
  -p:ApplicationVersion=12 \
  -p:AndroidKeyStore=false \
  -p:AndroidPackageFormats=aab \
  "${local_tree_args[@]}"

mapfile -t candidates < <(find "$publish_dir" -maxdepth 1 -type f -name '*.aab' -print)
[[ "${#candidates[@]}" -eq 1 ]] || fail "unsigned-aab-cardinality"
[[ "${candidates[0]##*/}" == "com.myexternalbrain.chummer.aab" ]] \
  || fail "unsigned-aab-name"
normalized="$output_dir/chummer-android-0.1.0-preview.12-unsigned.aab"
python3 "$repo_dir/scripts/normalize_preview12_unsigned_aab.py" \
  --source "${candidates[0]}" \
  --output "$normalized" || fail "unsigned-aab-normalization"
[[ -f "$normalized" && ! -L "$normalized" ]] || fail "normalized-aab-absent"
[[ -z "$(git -C "$repo_dir" status --porcelain=v1 --untracked-files=all)" ]] || fail "source-mutated"
sha256sum "$normalized"
printf 'preview12_unsigned_candidate=pass source=%s signing_authorized=false google_play_upload_authorized=false publication_authorized=false\n' "$source_commit"
