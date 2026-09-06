from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]


def test_coalesced_refresh_has_no_deferred_control_replacement_window() -> None:
    page = (ROOT / "src/Chummer.Android/Native/NativePageBase.cs").read_text(
        encoding="utf-8"
    )

    assert "NativeRefreshCoalescer _coordinatorRefresh" in page
    changed = page.split("private void OnCoordinatorChanged", 1)[1].split(
        "private bool IsCurrentAppearanceGeneration", 1
    )[0]
    assert "_coordinatorRefresh.MarkPending(appearanceGeneration);" in changed
    schedule = page.split("private void TryScheduleCoordinatorRefresh", 1)[1].split(
        "private void DispatchCoordinatorRefresh", 1
    )[0]
    assert "TryGetPendingRequest(" in schedule
    assert schedule.index("TryGetPendingRequest(") < schedule.index(
        "if (TryDeferCoordinatorRefresh())"
    )
    assert "_coordinatorRefresh.DiscardPending(" in schedule
    assert "_coordinatorRefresh.TrySchedulePending(appearanceGeneration)" in schedule
    assert "Volatile.Read(ref _appearanceRefreshActive) > 0" in page
    drain = page.split(
        "private async Task DrainCoordinatorRefreshAsync(long appearanceGeneration)", 1
    )[1]
    assert "|| Volatile.Read(ref _appearanceRefreshActive) > 0" in drain
    assert drain.index("Volatile.Read(ref _appearanceRefreshActive) > 0") < drain.index(
        "_coordinatorRefresh.TryTakePending(appearanceGeneration)"
    )
    dispatch = page.split("private void DispatchCoordinatorRefresh", 1)[1].split(
        "protected virtual bool TryDispatchCoordinatorRefresh", 1
    )[0]
    assert "if (!TryDispatchCoordinatorRefresh(" in dispatch
    assert dispatch.count("_coordinatorRefresh.ReleaseSchedule(appearanceGeneration);") == 2
    finally_block = drain.split("finally", 1)[1]
    assert finally_block.index("ReleaseSchedule(appearanceGeneration)") < finally_block.index(
        "TryScheduleCoordinatorRefresh(Volatile.Read(ref _appearanceGeneration))"
    )
    assert "Task.Delay(CoordinatorRefreshSettleDelay)" not in page
    assert "CoordinatorRefreshSettleDelay" not in page


def test_coalescer_keeps_one_follow_up_for_state_changed_during_render() -> None:
    coalescer = (
        ROOT / "src/Chummer.Android/Native/NativeRefreshCoalescer.cs"
    ).read_text(encoding="utf-8")

    assert "private sealed record PendingRefresh(long Generation, long RequestId);" in coalescer
    assert "public long MarkPending(long generation)" in coalescer
    assert "public bool TryGetPendingRequest(long generation, out long requestId)" in coalescer
    assert "public bool DiscardPending(long generation, long requestId)" in coalescer
    assert "observed.RequestId == requestId" in coalescer
    assert "Interlocked.CompareExchange(" in coalescer
    assert "ref _scheduledGeneration" in coalescer
    assert "public bool Complete(bool allowReschedule)" in coalescer


def test_page_actions_reject_overlapping_taps_without_queueing_mutations() -> None:
    page = (ROOT / "src/Chummer.Android/Native/NativePageBase.cs").read_text(
        encoding="utf-8"
    )
    gate = (ROOT / "src/Chummer.Android/Native/NativePageActionGate.cs").read_text(
        encoding="utf-8"
    )

    assert page.count("if (!_actionGate.TryClaim())") == 2
    assert page.count("_actionGate.Release();") == 2
    assert "Interlocked.CompareExchange(ref _claimed, 1, 0)" in gate
    assert "_runningActionDepth" not in page
