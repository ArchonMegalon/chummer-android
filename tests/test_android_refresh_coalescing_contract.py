from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]


def test_coalesced_refresh_has_no_deferred_control_replacement_window() -> None:
    page = (ROOT / "src/Chummer.Android/Native/NativePageBase.cs").read_text(
        encoding="utf-8"
    )

    assert "NativeRefreshCoalescer _coordinatorRefresh" in page
    assert "if (_coordinatorRefresh.Request())" in page
    assert "_coordinatorRefresh.TryTakePending()" in page
    assert "Volatile.Read(ref _appearanceRefreshActive) > 0" in page
    assert page.index("Volatile.Read(ref _appearanceRefreshActive) > 0") < page.index(
        "if (_coordinatorRefresh.Request())"
    )
    assert "Task.Delay(CoordinatorRefreshSettleDelay)" not in page
    assert "CoordinatorRefreshSettleDelay" not in page


def test_coalescer_keeps_one_follow_up_for_state_changed_during_render() -> None:
    coalescer = (
        ROOT / "src/Chummer.Android/Native/NativeRefreshCoalescer.cs"
    ).read_text(encoding="utf-8")

    assert "Interlocked.Exchange(ref _pending, 1)" in coalescer
    assert "Interlocked.CompareExchange(ref _scheduled, 1, 0)" in coalescer
    assert "public bool Complete(bool allowReschedule)" in coalescer
