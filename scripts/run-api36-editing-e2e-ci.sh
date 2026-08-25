#!/usr/bin/env bash
set -euo pipefail

profile="${CHUMMER_E2E_PROFILE:?CHUMMER_E2E_PROFILE is required}"
if [[ "$profile" != "phone" ]]; then
  echo "CHUMMER_E2E_PROFILE must be phone; tablet beta proof is deferred." >&2
  exit 64
fi
journey="${CHUMMER_E2E_JOURNEY:?CHUMMER_E2E_JOURNEY is required}"
case "$journey" in
  full-editing|creation-prerequisite|career-active-skill-advance|career-weapon-fire|career-notoriety) ;;
  *)
    echo "Unsupported CHUMMER_E2E_JOURNEY: $journey" >&2
    exit 64
    ;;
esac

android_home="${ANDROID_HOME:?ANDROID_HOME is required}"
adb_path="$android_home/platform-tools/adb"
apk_path="$RUNNER_TEMP/chummer-android-apk/chummer-android-x64-debug.apk"
evidence_root="$RUNNER_TEMP/chummer-api36-evidence/$profile/$journey"
artifact_id="${CHUMMER_E2E_APK_ARTIFACT_ID:?CHUMMER_E2E_APK_ARTIFACT_ID is required}"
artifact_digest="${CHUMMER_E2E_APK_ARTIFACT_DIGEST:?CHUMMER_E2E_APK_ARTIFACT_DIGEST is required}"
artifact_name="${CHUMMER_E2E_APK_ARTIFACT_NAME:?CHUMMER_E2E_APK_ARTIFACT_NAME is required}"
artifact_attempt="${CHUMMER_E2E_APK_ARTIFACT_ATTEMPT:?CHUMMER_E2E_APK_ARTIFACT_ATTEMPT is required}"
expected_apk_sha256="${CHUMMER_E2E_APK_SHA256:?CHUMMER_E2E_APK_SHA256 is required}"
run_id="${GITHUB_RUN_ID:?GITHUB_RUN_ID is required}"

[[ "$artifact_id" =~ ^[1-9][0-9]*$ ]]
[[ "$artifact_digest" =~ ^(sha256:)?[0-9a-f]{64}$ ]]
[[ "$artifact_attempt" =~ ^[1-9][0-9]*$ ]]
[[ "$expected_apk_sha256" =~ ^[0-9a-f]{64}$ ]]
test "$artifact_name" = "chummer-android-api36-x64-debug-${run_id}-${artifact_attempt}"

test -x "$adb_path"
test -f "$apk_path"
actual_apk_sha256="$(sha256sum "$apk_path" | cut -d ' ' -f 1)"
test "$actual_apk_sha256" = "$expected_apk_sha256"
install -d -m 0755 "$evidence_root"
declare -Ar driver_journeys=(
  [full-editing]="full"
  [creation-prerequisite]="creation-prerequisite"
  [career-active-skill-advance]="career-active-skill-advance"
  [career-weapon-fire]="career-weapon-fire"
  [career-notoriety]="career-notoriety"
)
driver_journey="${driver_journeys[$journey]:?missing explicit driver journey mapping}"
printf 'profile=%s\nmatrix_journey=%s\ndriver_journey=%s\nartifact_id=%s\nartifact_digest=%s\nartifact_name=%s\nartifact_attempt=%s\napk_sha256=%s\n' \
  "$profile" \
  "$journey" \
  "$driver_journey" \
  "$artifact_id" \
  "$artifact_digest" \
  "$artifact_name" \
  "$artifact_attempt" \
  "$actual_apk_sha256" \
  >"$evidence_root/execution-started.txt"

case "$journey" in
  full-editing)
    python3 chummer-android/tests/run_api36_editing_e2e.py \
      --adb "$adb_path" \
      --apk "$apk_path" \
      --serial emulator-5554 \
      --profile "$profile" \
      --journey full \
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
  career-weapon-fire)
    python3 chummer-android/tests/run_api36_career_weapon_fire_e2e.py \
      --adb "$adb_path" \
      --apk "$apk_path" \
      --serial emulator-5554 \
      --workspace-root "${GITHUB_WORKSPACE:?GITHUB_WORKSPACE is required}" \
      --evidence "$evidence_root/screenshots" \
      --receipt "$evidence_root/receipt.json"
    ;;
  career-notoriety)
    python3 chummer-android/tests/run_api36_career_notoriety_e2e.py \
      --adb "$adb_path" \
      --apk "$apk_path" \
      --serial emulator-5554 \
      --workspace-root "${GITHUB_WORKSPACE:?GITHUB_WORKSPACE is required}" \
      --evidence "$evidence_root/screenshots" \
      --receipt "$evidence_root/receipt.json"
    ;;
esac

python3 chummer-android/scripts/finalize-api36-e2e-journey-receipt.py \
  --receipt "$evidence_root/receipt.json" \
  --run-id "$run_id" \
  --matrix-journey "$journey" \
  --driver-journey "$driver_journey" \
  --artifact-id "$artifact_id" \
  --artifact-digest "$artifact_digest" \
  --artifact-name "$artifact_name" \
  --artifact-attempt "$artifact_attempt" \
  --apk-sha256 "$expected_apk_sha256"
(
  cd "$evidence_root"
  sha256sum receipt.json >receipt.json.sha256
  sha256sum --check receipt.json.sha256
)
