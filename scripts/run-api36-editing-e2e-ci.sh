#!/usr/bin/env bash
set -euo pipefail

profile="${CHUMMER_E2E_PROFILE:?CHUMMER_E2E_PROFILE is required}"
if [[ "$profile" != "phone" ]]; then
  echo "CHUMMER_E2E_PROFILE must be phone; tablet beta proof is deferred." >&2
  exit 64
fi
journey="${CHUMMER_E2E_JOURNEY:?CHUMMER_E2E_JOURNEY is required}"
case "$journey" in
  full-editing|creation-prerequisite|career-active-skill-advance) ;;
  *)
    echo "Unsupported CHUMMER_E2E_JOURNEY: $journey" >&2
    exit 64
    ;;
esac

android_home="${ANDROID_HOME:?ANDROID_HOME is required}"
adb_path="$android_home/platform-tools/adb"
apk_path="$RUNNER_TEMP/chummer-android-apk/chummer-android-x64-debug.apk"
evidence_root="$RUNNER_TEMP/chummer-api36-evidence/$profile/$journey"

test -x "$adb_path"
test -f "$apk_path"
install -d -m 0755 "$evidence_root"
printf 'profile=%s\njourney=%s\napk_sha256=%s\n' \
  "$profile" \
  "$journey" \
  "$(sha256sum "$apk_path" | cut -d ' ' -f 1)" \
  >"$evidence_root/execution-started.txt"

case "$journey" in
  full-editing)
    python3 chummer-android/tests/run_api36_editing_e2e.py \
      --adb "$adb_path" \
      --apk "$apk_path" \
      --serial emulator-5554 \
      --profile "$profile" \
      --evidence "$evidence_root/screenshots" \
      --receipt "$evidence_root/receipt.json"
    ;;
  creation-prerequisite)
    python3 chummer-android/tests/run_api36_creation_prerequisite_e2e.py \
      --adb "$adb_path" \
      --apk "$apk_path" \
      --serial emulator-5554 \
      --evidence "$evidence_root/screenshots" \
      --receipt "$evidence_root/receipt.json"
    ;;
  career-active-skill-advance)
    python3 chummer-android/tests/run_api36_career_active_skill_advance_e2e.py \
      --adb "$adb_path" \
      --apk "$apk_path" \
      --serial emulator-5554 \
      --workspace-root "${GITHUB_WORKSPACE:?GITHUB_WORKSPACE is required}" \
      --evidence "$evidence_root/screenshots" \
      --receipt "$evidence_root/receipt.json"
    ;;
esac
