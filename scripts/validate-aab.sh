#!/usr/bin/env bash
set -euo pipefail
PATH=/usr/bin:/bin
export PATH

# Protected release validation executes this script from a sealed descriptor.
# Resolve every operating-system utility to a root-owned absolute path so a
# caller-controlled PATH cannot change the program executed after validation
# credentials have been admitted.
cut() { /usr/bin/cut "$@"; }
mktemp() { /usr/bin/mktemp "$@"; }
openssl() { /usr/bin/openssl "$@"; }
rm() { /usr/bin/rm "$@"; }
sed() { /usr/bin/sed "$@"; }
sha256sum() { /usr/bin/sha256sum "$@"; }

if [[ $# -ne 1 ]]; then
  echo "usage: CHUMMER_BUNDLETOOL_JAR=/path/to/bundletool.jar $0 /path/to/chummer.aab" >&2
  exit 64
fi

repo_dir="${CHUMMER_VALIDATOR_REPO_ROOT:-$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)}"
aab_path="$1"
bundletool_path="${CHUMMER_BUNDLETOOL_JAR:-}"
java_command="${CHUMMER_JAVA:-java}"
jarsigner_command="${CHUMMER_JARSIGNER:-jarsigner}"
keytool_command="${CHUMMER_KEYTOOL:-keytool}"
upload_certificate_path="${CHUMMER_ANDROID_UPLOAD_CERTIFICATE_PATH:-}"
python_command="${CHUMMER_PYTHON3:-python3}"
inspect_aab_script="${CHUMMER_INSPECT_AAB_SCRIPT:-$repo_dir/scripts/inspect_aab.py}"
proof_exclusion_script="${CHUMMER_PROOF_EXCLUSION_SCRIPT:-$repo_dir/scripts/verify_release_aab_excludes_api36_proof.py}"

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
"$python_command" -I -E -S "$inspect_aab_script" "$aab_path" "$temporary_dir/manifest.xml"
"$python_command" -I -E -S "$proof_exclusion_script" "$aab_path"

if [[ -n "$upload_certificate_path" ]]; then
  if [[ ! -f "$upload_certificate_path" ]]; then
    echo "Upload certificate not found: $upload_certificate_path" >&2
    exit 66
  fi

  # A private upload certificate is expected to be self-signed. Avoid
  # jarsigner's strict mode here because it reports that expected condition as
  # an error; the explicit fingerprint comparison below is the trust check.
  if ! "$jarsigner_command" -verify -certs "$aab_path" > "$temporary_dir/jarsigner-verify.log" 2>&1; then
    sed -n '1,240p' "$temporary_dir/jarsigner-verify.log" >&2
    exit 65
  fi
  "$keytool_command" -printcert -jarfile "$aab_path" -rfc > "$temporary_dir/aab-signer.pem"

  expected_upload_fingerprint="$(openssl x509 -in "$upload_certificate_path" -noout -fingerprint -sha256 | cut -d= -f2)"
  actual_upload_fingerprint="$(openssl x509 -in "$temporary_dir/aab-signer.pem" -noout -fingerprint -sha256 | cut -d= -f2)"
  if [[ -z "$expected_upload_fingerprint" || "$actual_upload_fingerprint" != "$expected_upload_fingerprint" ]]; then
    echo "AAB signer does not match the configured Chummer upload certificate." >&2
    exit 65
  fi
  printf 'AAB JAR signature and upload certificate verified: %s\n' "$actual_upload_fingerprint"
fi

sha256sum "$aab_path"
