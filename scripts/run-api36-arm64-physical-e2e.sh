#!/usr/bin/env bash
set -euo pipefail

repo_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd -P)"
bounded="$repo_dir/scripts/run_internal_phone_beta_bounded.py"
capture="$repo_dir/scripts/capture-api36-arm64-physical-device.py"
finalizer="$repo_dir/scripts/finalize-api36-arm64-physical-journey-receipt.py"
aggregate="$repo_dir/scripts/verify-api36-arm64-physical-aggregate.py"
current_phase="preflight"
output_root=""

fail() {
  printf 'api36_arm64_six_journey=blocked phase=%s publication_authorized=false\n' "$1" >&2
  exit 2
}

on_exit() {
  local status="$?"
  trap - EXIT HUP INT TERM
  if [[ "$status" -ne 0 && -n "$output_root" && -d "$output_root" ]]; then
    local blocked="$output_root/blocked.json"
    if [[ ! -e "$blocked" && ! -L "$blocked" ]]; then
      printf '{"failurePhase":"%s","publicationAuthorized":false,"status":"blocked"}\n' "$current_phase" >"$blocked" || true
      chmod 0600 "$blocked" 2>/dev/null || true
    fi
  fi
  exit "$status"
}

require_file() {
  local variable="$1" value="${!1:-}"
  [[ -n "$value" && "$value" == /* && -f "$value" && ! -L "$value" ]] \
    || fail "input-$variable-not-absolute-regular"
  [[ "$(realpath -e -- "$value")" == "$value" ]] \
    || fail "input-$variable-not-canonical"
}

require_directory() {
  local variable="$1" value="${!1:-}"
  [[ -n "$value" && "$value" == /* && -d "$value" && ! -L "$value" ]] \
    || fail "input-$variable-not-absolute-directory"
  [[ "$(realpath -e -- "$value")" == "$value" ]] \
    || fail "input-$variable-not-canonical"
}

run_bounded() {
  local phase="$1" log="$2"
  shift 2
  current_phase="$phase"
  python3 "$bounded" \
    --journal "$output_root/command-journal.jsonl" \
    --output "$log" \
    --phase "$phase" \
    --timeout-seconds 3600 \
    --deadline-epoch "$deadline_epoch" \
    -- "$@"
}

trap on_exit EXIT
trap 'exit 129' HUP
trap 'exit 130' INT
trap 'exit 143' TERM

for command in date dirname mkdir python3 realpath; do
  command -v "$command" >/dev/null 2>&1 || fail "missing-command-$command"
done

for variable in \
  CHUMMER_API36_ARM64_APK \
  CHUMMER_RELEASE_SOURCE_GRAPH \
  CHUMMER_API36_BUILD_PROVENANCE \
  CHUMMER_ADB \
  CHUMMER_PRIORITY_DRIVER \
  CHUMMER_CAREER_DRIVER \
  CHUMMER_BEFORE_RUN_DRIVER \
  CHUMMER_AFTER_RUN_DRIVER \
  CHUMMER_DOWNTIME_DRIVER \
  CHUMMER_PLAYTIME_DRIVER; do
  require_file "$variable"
done
require_directory CHUMMER_RELEASE_WORKSPACE_ROOT

[[ "${CHUMMER_DEVICE_SERIAL:-}" =~ ^[A-Za-z0-9][A-Za-z0-9._:-]{0,127}$ ]] \
  || fail "device-serial-invalid"
output_root="${CHUMMER_API36_ARM64_OUTPUT_ROOT:-}"
[[ -n "$output_root" && "$output_root" == /* && ! -e "$output_root" && ! -L "$output_root" ]] \
  || fail "output-root-must-be-absent-absolute"
output_parent="$(dirname -- "$output_root")"
[[ -d "$output_parent" && ! -L "$output_parent" && "$(realpath -e -- "$output_parent")" == "$output_parent" ]] \
  || fail "output-root-parent-not-canonical"
case "$output_root/" in
  "$repo_dir"/*) fail "output-root-inside-source-worktree" ;;
esac

declare -A drivers=(
  [priority]="$CHUMMER_PRIORITY_DRIVER"
  [career]="$CHUMMER_CAREER_DRIVER"
  [before-run]="$CHUMMER_BEFORE_RUN_DRIVER"
  [after-run]="$CHUMMER_AFTER_RUN_DRIVER"
  [downtime]="$CHUMMER_DOWNTIME_DRIVER"
  [playtime]="$CHUMMER_PLAYTIME_DRIVER"
)
declare -A expected_driver=(
  [priority]="run_api36_sr5_priority_legal_path_e2e.py"
  [career]="run_api36_sr5_career_active_skill_wizard_e2e.py"
  [before-run]="run_api36_sr5_before_run_edge_physical_e2e.py"
  [after-run]="run_api36_sr5_after_run_settlement_e2e.py"
  [downtime]="run_api36_sr5_downtime_calendar_e2e.py"
  [playtime]="run_api36_sr5_playtime_weapon_physical_e2e.py"
)
journeys=(priority career before-run after-run downtime playtime)
for journey in "${journeys[@]}"; do
  [[ "$(basename -- "${drivers[$journey]}")" == "${expected_driver[$journey]}" ]] \
    || fail "driver-basename-mismatch-$journey"
done

mkdir -m 0700 -- "$output_root"
mkdir -m 0700 -- "$output_root/raw" "$output_root/evidence" "$output_root/seals" "$output_root/logs"
deadline_epoch="$(( $(date +%s) + 28800 ))"

run_bounded build-authority-preflight "$output_root/logs/build-authority-preflight.log" \
  python3 "$aggregate" preflight \
  --apk "$CHUMMER_API36_ARM64_APK" \
  --source-graph "$CHUMMER_RELEASE_SOURCE_GRAPH" \
  --build-provenance "$CHUMMER_API36_BUILD_PROVENANCE"

run_bounded physical-device-capture "$output_root/logs/physical-device-capture.log" \
  python3 "$capture" \
  --adb "$CHUMMER_ADB" \
  --serial "$CHUMMER_DEVICE_SERIAL" \
  --output "$output_root/device-observation.json"

for journey in "${journeys[@]}"; do
  raw="$output_root/raw/$journey.json"
  evidence="$output_root/evidence/$journey"
  seal="$output_root/seals/$journey.json"
  run_bounded "journey-$journey" "$output_root/logs/journey-$journey.log" \
    python3 "${drivers[$journey]}" \
    --adb "$CHUMMER_ADB" \
    --apk "$CHUMMER_API36_ARM64_APK" \
    --build-provenance-manifest "$CHUMMER_API36_BUILD_PROVENANCE" \
    --serial "$CHUMMER_DEVICE_SERIAL" \
    --evidence "$evidence" \
    --receipt "$raw" \
    --workspace-root "$CHUMMER_RELEASE_WORKSPACE_ROOT" \
    --allow-destructive-disposable-device
  run_bounded "seal-$journey" "$output_root/logs/seal-$journey.log" \
    python3 "$finalizer" \
    --journey-id "$journey" \
    --raw-receipt "$raw" \
    --restart-evidence "$evidence/process-restart-verified.txt" \
    --apk "$CHUMMER_API36_ARM64_APK" \
    --source-graph "$CHUMMER_RELEASE_SOURCE_GRAPH" \
    --build-provenance "$CHUMMER_API36_BUILD_PROVENANCE" \
    --device-observation "$output_root/device-observation.json" \
    --output "$seal"
done

aggregate_args=(
  --apk "$CHUMMER_API36_ARM64_APK"
  --source-graph "$CHUMMER_RELEASE_SOURCE_GRAPH"
  --build-provenance "$CHUMMER_API36_BUILD_PROVENANCE"
  --device-observation "$output_root/device-observation.json"
)
for journey in "${journeys[@]}"; do
  aggregate_args+=(
    --raw-receipt "$journey=$output_root/raw/$journey.json"
    --restart-evidence "$journey=$output_root/evidence/$journey/process-restart-verified.txt"
    --journey-seal "$journey=$output_root/seals/$journey.json"
  )
done

run_bounded aggregate-materialize "$output_root/logs/aggregate-materialize.log" \
  python3 "$aggregate" materialize "${aggregate_args[@]}" \
  --output "$output_root/six-journey-aggregate.json"
run_bounded aggregate-verify "$output_root/logs/aggregate-verify.log" \
  python3 "$aggregate" verify "${aggregate_args[@]}" \
  --aggregate "$output_root/six-journey-aggregate.json"

current_phase="complete"
printf 'api36_arm64_six_journey=pass publication_authorized=false aggregate=%s\n' \
  "$output_root/six-journey-aggregate.json"
