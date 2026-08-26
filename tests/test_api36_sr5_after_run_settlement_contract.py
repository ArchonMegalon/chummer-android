import ast
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
DRIVER = ROOT / "tests" / "run_api36_sr5_after_run_settlement_e2e.py"


def test_after_run_driver_is_syntactically_valid_and_release_honest() -> None:
    source = DRIVER.read_text(encoding="utf-8")
    ast.parse(source)
    assert "--build-provenance-manifest" in source
    assert "load_and_verify_manifest" in source
    assert '"status": "unavailable"' in source
    assert '"executionStatus": "not-run"' in source
    assert '"physicalDeviceProof": False' in source
    assert '"releaseEvidenceEligible": False' in source
    assert "return 3" in source
    assert '"releaseAttested": True' not in source
    assert "device-pass" not in source
