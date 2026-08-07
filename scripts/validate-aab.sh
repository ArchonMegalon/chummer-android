#!/usr/bin/env bash
set -euo pipefail

if [[ $# -ne 1 ]]; then
  echo "usage: CHUMMER_BUNDLETOOL_JAR=/path/to/bundletool.jar $0 /path/to/chummer.aab" >&2
  exit 64
fi

repo_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
aab_path="$(realpath "$1")"
bundletool_path="${CHUMMER_BUNDLETOOL_JAR:-}"
java_command="${CHUMMER_JAVA:-java}"

if [[ ! -f "$aab_path" ]]; then
  echo "AAB not found: $aab_path" >&2
  exit 66
fi

if [[ -z "$bundletool_path" || ! -f "$bundletool_path" ]]; then
  echo "Set CHUMMER_BUNDLETOOL_JAR to the pinned official bundletool JAR." >&2
  exit 78
fi

temporary_dir="$(mktemp -d)"
trap 'rm -rf "$temporary_dir"' EXIT

if ! "$java_command" -jar "$bundletool_path" validate --bundle="$aab_path" > "$temporary_dir/bundletool-validate.log" 2>&1; then
  sed -n '1,240p' "$temporary_dir/bundletool-validate.log" >&2
  exit 65
fi
echo "bundletool validation passed."
"$java_command" -jar "$bundletool_path" dump manifest --bundle="$aab_path" > "$temporary_dir/manifest.xml"
python3 "$repo_dir/scripts/inspect_aab.py" "$aab_path" "$temporary_dir/manifest.xml"
sha256sum "$aab_path"
