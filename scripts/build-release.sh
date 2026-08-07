#!/usr/bin/env bash
set -euo pipefail

repo_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
project_path="$repo_dir/src/Chummer.Android/Chummer.Android.csproj"
dotnet_command="${CHUMMER_DOTNET:-dotnet}"
configuration="Release"
framework="net10.0-android36.0"
runtime_id="android-arm64"
package_id="com.myexternalbrain.chummer"
version_name="0.1.0-preview.1"

python3 -m unittest discover -s "$repo_dir/tests" -v

"$dotnet_command" publish "$project_path" \
  --configuration "$configuration" \
  --framework "$framework" \
  --runtime "$runtime_id" \
  --self-contained true \
  -p:AndroidPackageFormats=aab

publish_dir="$repo_dir/src/Chummer.Android/bin/$configuration/$framework/$runtime_id/publish"
if [[ -n "${AndroidSigningKeyStore:-}" ]]; then
  source_aab="$publish_dir/$package_id-Signed.aab"
  output_aab="$repo_dir/artifacts/chummer-android-$version_name-upload.aab"
else
  source_aab="$publish_dir/$package_id.aab"
  output_aab="$repo_dir/artifacts/chummer-android-$version_name-unsigned.aab"
fi

if [[ ! -f "$source_aab" ]]; then
  echo "Expected release bundle was not produced: $source_aab" >&2
  exit 70
fi

mkdir -p "$repo_dir/artifacts"
install -m 0644 "$source_aab" "$output_aab"
"$repo_dir/scripts/validate-aab.sh" "$output_aab"
