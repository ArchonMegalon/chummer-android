#!/usr/bin/env bash
set -euo pipefail

profile="${CHUMMER_E2E_PROFILE:?CHUMMER_E2E_PROFILE is required}"
if [[ "$profile" != "phone" ]]; then
  echo "CHUMMER_E2E_PROFILE must be phone; tablet beta proof is deferred." >&2
  exit 64
fi
journey="${CHUMMER_E2E_JOURNEY:?CHUMMER_E2E_JOURNEY is required}"
case "$journey" in
  creation-prerequisite|career-active-skill-advance|career-weapon-fire|before-run-edge|playtime-short-burst|downtime-calendar|after-run-settlement) ;;
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
gate_contract_path="chummer-android/eng/api36-sr5-wizard-gate-authority.json"

python3 chummer-android/scripts/api36_wizard_gate_contract.py \
  --manifest "$gate_contract_path"
gate_contract_sha256="$(sha256sum "$gate_contract_path" | cut -d ' ' -f 1)"

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
  [creation-prerequisite]="creation-prerequisite"
  [career-active-skill-advance]="career-active-skill-advance"
  [career-weapon-fire]="career-weapon-fire"
  [before-run-edge]="before-run-edge"
  [playtime-short-burst]="playtime-short-burst"
  [downtime-calendar]="sr5-downtime-calendar"
  [after-run-settlement]="sr5-after-run-settlement"
)
driver_journey="${driver_journeys[$journey]:?missing explicit driver journey mapping}"
printf 'profile=%s\nmatrix_journey=%s\ndriver_journey=%s\ngate_contract_sha256=%s\nartifact_id=%s\nartifact_digest=%s\nartifact_name=%s\nartifact_attempt=%s\napk_sha256=%s\n' \
  "$profile" \
  "$journey" \
  "$driver_journey" \
  "$gate_contract_sha256" \
  "$artifact_id" \
  "$artifact_digest" \
  "$artifact_name" \
  "$artifact_attempt" \
  "$actual_apk_sha256" \
  >"$evidence_root/execution-started.txt"

case "$journey" in
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
  before-run-edge)
    python3 chummer-android/tests/run_api36_sr5_before_run_edge_e2e.py \
      --adb "$adb_path" \
      --apk "$apk_path" \
      --serial emulator-5554 \
      --workspace-root "${GITHUB_WORKSPACE:?GITHUB_WORKSPACE is required}" \
      --evidence "$evidence_root/screenshots" \
      --receipt "$evidence_root/receipt.json"
    ;;
  playtime-short-burst)
    python3 chummer-android/tests/run_api36_sr5_playtime_short_burst_e2e.py \
      --adb "$adb_path" \
      --apk "$apk_path" \
      --serial emulator-5554 \
      --workspace-root "${GITHUB_WORKSPACE:?GITHUB_WORKSPACE is required}" \
      --evidence "$evidence_root/screenshots" \
      --receipt "$evidence_root/receipt.json"
    ;;
  downtime-calendar)
    python3 chummer-android/tests/run_api36_sr5_downtime_calendar_hosted_e2e.py \
      --adb "$adb_path" \
      --apk "$apk_path" \
      --serial emulator-5554 \
      --workspace-root "${GITHUB_WORKSPACE:?GITHUB_WORKSPACE is required}" \
      --evidence "$evidence_root/screenshots" \
      --receipt "$evidence_root/receipt.json"
    ;;
  after-run-settlement)
    python3 chummer-android/tests/run_api36_sr5_after_run_settlement_hosted_e2e.py \
      --adb "$adb_path" \
      --apk "$apk_path" \
      --serial emulator-5554 \
      --evidence "$evidence_root/screenshots" \
      --receipt "$evidence_root/receipt.json"
    ;;
esac

python3 chummer-android/scripts/finalize-api36-e2e-journey-receipt.py \
  --receipt "$evidence_root/receipt.json" \
  --gate-contract "$gate_contract_path" \
  --run-id "$run_id" \
  --matrix-journey "$journey" \
  --driver-journey "$driver_journey" \
  --artifact-id "$artifact_id" \
  --artifact-digest "$artifact_digest" \
  --artifact-name "$artifact_name" \
  --artifact-attempt "$artifact_attempt" \
  --apk-sha256 "$expected_apk_sha256"

python3 chummer-android/scripts/materialize-api36-proof-environment-receipt.py \
  journey \
  --apk "$apk_path" \
  --expected-apk-sha256 "$expected_apk_sha256" \
  --journey-receipt "$evidence_root/receipt.json" \
  --matrix-journey "$journey" \
  --android-sdk-root "$android_home" \
  --gate-contract "$gate_contract_path" \
  --policy chummer-android/eng/api36-proof-environment-authority.json \
  --output "$evidence_root/environment-receipt.json"
(
  cd "$evidence_root"
  sha256sum receipt.json >receipt.json.sha256
  sha256sum --check receipt.json.sha256
  sha256sum environment-receipt.json >environment-receipt.json.sha256
  sha256sum --check environment-receipt.json.sha256
)
