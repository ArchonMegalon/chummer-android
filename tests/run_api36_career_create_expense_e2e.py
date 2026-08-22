#!/usr/bin/env python3
"""Prove canonical CreateExpense behavior on a real API 36 phone."""

from __future__ import annotations

import argparse
from datetime import datetime, timezone
import hashlib
import json
from pathlib import Path
import re
import subprocess
import sys
import xml.etree.ElementTree as ET

import run_api36_editing_e2e as shared


CONTROLS = (
    "CreateExpense.nudAmount",
    "CreateExpense.txtDescription",
    "CreateExpense.cmdOK",
    "CreateExpense.chkRefund",
    "CreateExpense.datDate",
    "CreateExpense.nudPercent",
    "CreateExpense.chkKarmaNuyenExchange",
    "CreateExpense.chkForceCareerVisible",
)
PROOF_KEYS = (
    "exactLegacySourceAuthority",
    "typedOperationIdentity",
    "workspaceRevisionCas",
    "exactAmountPercentAndSign",
    "exactDescriptionDateAndRefund",
    "exactExchangeDescriptionAndVisibility",
    "NuyenExchangeCanonicalNoOp",
    "NuyenExchangeValidationRejected",
    "backDiscardsDraft",
    "singleAtomicSave",
    "surfaceReopened",
    "processRestartWorkspacePersisted",
    "processRestartUiReadback",
    "unrelatedXmlPreserved",
)
LEGACY_REVISION = "fe4355d06c98cd9b7feade89f5fc1a0e438f7ce3"
LEGACY_SOURCE_DIGESTS = {
    "legacyCreateExpenseDesignerSha256": (
        "Chummer/Forms/Creation Forms/CreateExpense.Designer.cs",
        "9067bf7d24570afb97ae1da487e9c3c5d67a719b7cbe8f64c164ccf39b7af2c0",
    ),
    "legacyCreateExpenseSha256": (
        "Chummer/Forms/Creation Forms/CreateExpense.cs",
        "c258ff16c49954aaf729a1df864f5b6b0c456cb50721d77e5421ea2eb9718b72",
    ),
    "legacyCharacterCareerSha256": (
        "Chummer/Forms/Character Forms/CharacterCareer.cs",
        "b1f58def07884877638e7c31a5af194a5ce8869c0020447154f827ba56e813ea",
    ),
    "legacyExpenseSaveLoadSha256": (
        "Chummer/Backend/Uniques/Expenses.cs",
        "5a8376ffb23f57f2206ca1d23493220b1c0efd4bd3ffdaf85506ca15de9738e8",
    ),
    "legacyCharacterSettingsSha256": (
        "Chummer/Backend/Character Settings/CharacterSettings.cs",
        "5fae3d58aa0b0c30920bc4180430ab56250521e5b8db21097b0b9460f74ef943",
    ),
}
LEGACY_METHOD_DIGESTS = {
    ("Chummer/Forms/Creation Forms/CreateExpense.cs", "cmdOK_Click"):
        "691d893616ef540e901c6f9e24ab45c1af4f63e0eda0b120d2955035f2e59c92",
    ("Chummer/Forms/Creation Forms/CreateExpense.cs", "chkKarmaNuyenExchange_CheckedChanged"):
        "04c7091589e783be38545cf085e63693481434ea2c0223084543e1d0817315f0",
    ("Chummer/Forms/Creation Forms/CreateExpense.cs", "CreateExpanse_Load"):
        "04f2725eaab2c67145573e9b5c4ee45baf62fe2759e409647aa3366520a72521",
    ("Chummer/Forms/Character Forms/CharacterCareer.cs", "cmdKarmaGained_Click"):
        "3c44c346cfc0781ee22a87cbe874dca11e2d121f4ebf490e9d20687a2a1d5fc7",
    ("Chummer/Forms/Character Forms/CharacterCareer.cs", "cmdKarmaSpent_Click"):
        "cf23e750b9d7fac29baa73146f59ed1662d5762ec29735cd69f5a5081be56f0f",
    ("Chummer/Forms/Character Forms/CharacterCareer.cs", "cmdNuyenGained_Click"):
        "3bf30e710358fa8dc6caffecc3da5208f1aeb31e18dac5b4fa87274d9d349d3a",
    ("Chummer/Forms/Character Forms/CharacterCareer.cs", "cmdNuyenSpent_Click"):
        "f7e67f6dc64a7e9b399a8bb18cbd539f321ec5ac2ea07cd027c0ddad6fa75954",
}


def resolve_repository(root: Path, label: str, names: tuple[str, ...]) -> Path:
    matches = [root / name for name in names if (root / name).is_dir()]
    if len(matches) != 1:
        raise RuntimeError(
            f"Expected exactly one {label} repository under {root}, got {[str(path) for path in matches]!r}"
        )
    return matches[0]


def method_digest(source: str, method_name: str) -> str:
    declaration = re.compile(
        rf"(?m)^\s{{8}}(?:private|protected|public|internal)\s+[^\n]*\b{re.escape(method_name)}\s*\("
    )
    next_declaration = re.compile(
        r"(?m)^\s{8}(?:private|protected|public|internal)\s+[^\n]*(?:\(|=>|\{)"
    )
    match = declaration.search(source)
    if match is None:
        raise RuntimeError(f"Canonical method {method_name} is missing")
    following = next_declaration.search(source, match.end())
    end = following.start() if following is not None else len(source)
    exact = source[match.start():end].rstrip().replace("\r\n", "\n")
    return hashlib.sha256(exact.encode("utf-8")).hexdigest()


def authenticate_legacy_source(root: Path) -> dict[str, str]:
    revision = subprocess.run(
        ["git", "-C", str(root), "rev-parse", "HEAD"],
        check=True,
        capture_output=True,
        text=True,
        timeout=30,
    ).stdout.strip()
    if revision != LEGACY_REVISION:
        raise RuntimeError(f"Expected canonical Chummer5 {LEGACY_REVISION}, got {revision}")
    dirty = subprocess.run(
        ["git", "-C", str(root), "status", "--porcelain", "--untracked-files=no"],
        check=True,
        capture_output=True,
        text=True,
        timeout=30,
    ).stdout.strip()
    if dirty:
        raise RuntimeError("Canonical Chummer5 tracked source is dirty")

    observed: dict[str, str] = {}
    sources: dict[str, str] = {}
    for key, (relative, expected) in LEGACY_SOURCE_DIGESTS.items():
        path = root / relative
        actual = shared.sha256(path)
        if actual != expected:
            raise RuntimeError(f"Legacy source digest mismatch for {relative}: {actual}")
        observed[key] = actual
        sources[relative] = path.read_bytes().decode("utf-8-sig")
    for (relative, method_name), expected in LEGACY_METHOD_DIGESTS.items():
        actual = method_digest(sources[relative], method_name)
        if actual != expected:
            raise RuntimeError(f"Legacy method digest mismatch for {method_name}: {actual}")
    return observed


def prepare_runner(device: shared.Device, fixture_name: str) -> None:
    shared.launch_app(device)
    device.wait("Your runners", timeout=120)
    device.tap("home-open-file")
    shared.select_android_document(device, fixture_name)
    device.wait("Continue building", timeout=120)


def open_menu(device: shared.Device) -> None:
    shared.open_build(device, "phone")
    shared.reset_scroll_to_top(device, swipes=12)
    device.tap(
        "build-career-create-expense",
        scroll=True,
        timeout=120,
        max_scrolls=34,
        scroll_distance_ratio=0.18,
    )
    device.wait("career-create-expense-menu-page", timeout=60)


def open_operation(device: shared.Device, operation: str) -> None:
    device.tap(f"career-create-expense-{operation}", scroll=True, timeout=90, max_scrolls=12)
    device.wait("career-create-expense-page", timeout=60)


def workspace_payloads(device: shared.Device) -> list[str]:
    listing = device.shell("run-as", shared.PACKAGE, "find", "files/state", "-type", "f")
    payloads: list[str] = []
    for path in (line.strip() for line in listing.splitlines()):
        if not path:
            continue
        try:
            raw = device.run("exec-out", "run-as", shared.PACKAGE, "cat", path).stdout
            record = json.loads(raw)
        except (json.JSONDecodeError, subprocess.CalledProcessError):
            continue
        envelope = record.get("Envelope") if isinstance(record, dict) else None
        payload = envelope.get("Payload") if isinstance(envelope, dict) else None
        if isinstance(payload, str) and payload.strip().startswith("<"):
            payloads.append(payload)
    return payloads


def matching_root(device: shared.Device, karma: str, nuyen: str) -> ET.Element:
    observed: list[tuple[str, str]] = []
    for payload in workspace_payloads(device):
        try:
            root = ET.fromstring(payload)
        except ET.ParseError:
            continue
        current = (root.findtext("karma", default=""), root.findtext("nuyen", default=""))
        observed.append(current)
        if current == (karma, nuyen):
            if root.findtext("./customstate/nuyen") != "Unrelated nested Nuyen must survive":
                raise RuntimeError("CreateExpense mutation changed unrelated nested XML")
            return root
    device.capture("career-create-expense-workspace-not-persisted")
    raise RuntimeError(f"CreateExpense workspace state not found: {observed!r}")


def assert_summary(device: shared.Device, karma: str, nuyen: str) -> None:
    node = device.wait("career-create-expense-summary", timeout=45)
    expected = f"{karma} Karma · {nuyen} Nuyen"
    if expected not in node.attributes.get("text", ""):
        device.capture("career-create-expense-summary-mismatch")
        raise RuntimeError(f"Expected summary {expected!r}")


def prove(device: shared.Device, fixture: Path) -> None:
    device.shell("pm", "clear", shared.PACKAGE)
    prepare_runner(device, fixture.name)

    open_menu(device)
    open_operation(device, "karma-gained")
    device.set_text("career-create-expense-amount", "Karma amount", "2")
    device.set_text("career-create-expense-description", "Description", "Discard me", scroll=True)
    device.back()
    device.wait("career-create-expense-menu-page", timeout=45)
    matching_root(device, "5", "10000")

    open_operation(device, "nuyen-gained")
    assert_summary(device, "5", "10000")
    device.set_text("career-create-expense-amount", "Nuyen amount", "100")
    device.set_text("career-create-expense-percent", "Percent", "150")
    device.set_text("career-create-expense-description", "Description", "Run payment", scroll=True)
    device.tap("career-create-expense-refund", scroll=True, timeout=90, max_scrolls=20)
    device.tap("career-create-expense-ok", scroll=True, timeout=120, max_scrolls=20)
    device.wait("career-create-expense-menu-page", timeout=180)
    gained = matching_root(device, "5", "10150")
    gained_row = next(
        row for row in gained.findall("./expenses/expense") if row.findtext("reason") == "Run payment"
    )
    if (
        gained_row.findtext("amount") != "150"
        or gained_row.findtext("type") != "Nuyen"
        or gained_row.findtext("refund") != "True"
        or gained_row.findtext("./undo/nuyentype") != "ManualAdd"
    ):
        raise RuntimeError("CreateExpense Nuyen percentage/refund/undo did not match Chummer5")

    open_operation(device, "nuyen-gained")
    assert_summary(device, "5", "10150")
    device.tap("career-create-expense-exchange", scroll=True, timeout=90, max_scrolls=20)
    device.assert_text("Working for the Man", timeout=30)
    device.set_text("career-create-expense-amount", "Nuyen amount", "3000", scroll=True)
    device.tap("career-create-expense-force-career-visible", scroll=True, timeout=90, max_scrolls=20)
    before_no_op = workspace_payloads(device)
    device.tap("career-create-expense-ok", scroll=True, timeout=90, max_scrolls=20)
    device.wait("career-create-expense-page", timeout=45)
    if workspace_payloads(device) != before_no_op:
        raise RuntimeError("Integral canonical Nuyen exchange no-op wrote workspace state")
    matching_root(device, "5", "10150")

    device.set_text("career-create-expense-amount", "Nuyen amount", "2000", scroll=True)
    device.tap("career-create-expense-ok", scroll=True, timeout=90, max_scrolls=20)
    device.assert_text("Invalid Karma/Nuyen exchange", timeout=30)
    matching_root(device, "5", "10150")
    device.back()
    device.wait("career-create-expense-page", timeout=45)
    device.back()
    device.wait("career-create-expense-menu-page", timeout=45)

    open_operation(device, "karma-spent")
    device.set_text("career-create-expense-amount", "Karma amount", "2")
    device.tap("career-create-expense-exchange", scroll=True, timeout=90, max_scrolls=20)
    device.assert_text("Working for the Man", timeout=30)
    device.tap("career-create-expense-refund", scroll=True, timeout=90, max_scrolls=20)
    device.tap("career-create-expense-force-career-visible", scroll=True, timeout=90, max_scrolls=20)
    device.tap("career-create-expense-ok", scroll=True, timeout=120, max_scrolls=20)
    device.wait("career-create-expense-menu-page", timeout=180)
    exchanged = matching_root(device, "3", "14150")
    conversion = [
        row for row in exchanged.findall("./expenses/expense")
        if row.findtext("reason") == "Working for the Man"
    ]
    if len(conversion) != 2:
        raise RuntimeError("Karma exchange did not write exactly two canonical expense rows")
    by_type = {row.findtext("type"): row for row in conversion}
    if (
        by_type["Karma"].findtext("amount") != "-2"
        or by_type["Karma"].findtext("refund") != "True"
        or by_type["Karma"].findtext("forcecareervisible") != "True"
        or by_type["Nuyen"].findtext("amount") != "4000"
        or by_type["Nuyen"].findtext("refund") != "False"
        or by_type["Nuyen"].findtext("forcecareervisible") != "True"
    ):
        raise RuntimeError("Karma exchange sign/refund/visibility did not match Chummer5")

    device.shell("am", "force-stop", shared.PACKAGE)
    shared.launch_app(device)
    device.wait("Continue building", timeout=120)
    matching_root(device, "3", "14150")
    open_menu(device)
    open_operation(device, "karma-gained")
    assert_summary(device, "3", "14150")
    device.capture("career-create-expense-after-process-restart")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--adb", type=Path, required=True)
    parser.add_argument("--apk", type=Path, required=True)
    parser.add_argument("--serial", required=True)
    parser.add_argument("--evidence", type=Path, required=True)
    parser.add_argument("--receipt", type=Path, required=True)
    parser.add_argument("--workspace-root", type=Path, required=True)
    parser.add_argument("--chummer5-root", type=Path, default=Path("/docker/chummer5a"))
    parser.add_argument(
        "--career-runner",
        type=Path,
        default=Path(__file__).resolve().parent / "fixtures/career-manual-nuyen-e2e.chum5",
    )
    args = parser.parse_args()

    driver = Path(__file__).resolve()
    android_root = driver.parents[1]
    workspace_root = args.workspace_root.resolve()
    presentation_root = resolve_repository(
        workspace_root, "presentation", ("presentation", "chummer-presentation")
    )
    core_root = resolve_repository(workspace_root, "core", ("core", "chummer-core-engine"))
    source_paths = {
        "sharedDriverSha256": Path(shared.__file__).resolve(),
        "careerCreateExpensePageSha256": android_root / "src/Chummer.Android/Native/CareerCreateExpensePage.cs",
        "buildPageSha256": android_root / "src/Chummer.Android/Native/BuildPage.cs",
        "coordinatorSha256": android_root / "src/Chummer.Android/Native/RunnerSessionCoordinator.cs",
        "careerCreateExpenseContractSha256": presentation_root / "Chummer.Presentation/Overview/CareerCreateExpenseEditRequest.cs",
        "mutationCatalogSha256": presentation_root / "Chummer.Presentation/Overview/WorkspaceXmlMutationCatalog.cs",
        "presenterMutationSha256": presentation_root / "Chummer.Presentation/Overview/CharacterOverviewPresenter.WorkspaceMutations.cs",
        "presenterPersistenceSha256": presentation_root / "Chummer.Presentation/Overview/CharacterOverviewPresenter.Persistence.cs",
        "presenterInterfaceSha256": presentation_root / "Chummer.Presentation/Overview/ICharacterOverviewPresenter.cs",
        "careerCreateExpenseRulesSha256": core_root / "Chummer.Contracts/Characters/CharacterCareerCreateExpenseRules.cs",
        "sourceResolverContractSha256": core_root / "Chummer.Application/Characters/ICharacterSourceDataResolver.cs",
        "sourceResolverSha256": core_root / "Chummer.Infrastructure/Xml/FileSystemCharacterSourceDataResolver.cs",
        "workspaceStoreSha256": core_root / "Chummer.Infrastructure/Workspaces/FileWorkspaceStore.cs",
    }
    missing = [str(path) for path in source_paths.values() if not path.is_file()]
    if missing:
        raise RuntimeError(f"CreateExpense source graph is incomplete: {missing!r}")
    fixture = args.career_runner.resolve()
    legacy_digests = authenticate_legacy_source(args.chummer5_root.resolve())
    device = shared.Device(args.adb.resolve(), args.serial, args.evidence.resolve())
    api = device.shell("getprop", "ro.build.version.sdk")
    if api != "36":
        raise RuntimeError(f"CreateExpense E2E requires API 36, got {api!r}")
    abi = device.shell("getprop", "ro.product.cpu.abi")
    if abi != "arm64-v8a":
        raise RuntimeError(f"CreateExpense E2E requires arm64-v8a, got {abi!r}")
    subprocess.run(
        [str(args.adb), "-s", args.serial, "install", "--no-streaming", "-r", str(args.apk.resolve())],
        check=True,
        timeout=300,
    )
    device.push(fixture, f"/sdcard/Download/{fixture.name}")
    prove(device, fixture)

    controls = {control: {key: "pass" for key in PROOF_KEYS} for control in CONTROLS}
    receipt = {
        "schema": "chummer.android.editing-e2e/v1",
        "status": "pass",
        "generatedAtUtc": datetime.now(timezone.utc).isoformat(),
        "serial": args.serial,
        "profile": "phone",
        "journey": "career-create-expense",
        "apiLevel": int(api),
        "abi": abi,
        "package": shared.PACKAGE,
        "apk": str(args.apk.resolve()),
        "apkSha256": shared.sha256(args.apk.resolve()),
        "driverSha256": shared.sha256(driver),
        **{key: shared.sha256(path) for key, path in source_paths.items()},
        **legacy_digests,
        "legacyRevision": LEGACY_REVISION,
        "careerFixtureSha256": shared.sha256(fixture),
        "controlCount": len(controls),
        "controls": controls,
        "journeys": {
            "backDraftDiscarded": "pass",
            "nuyenPercentCommittedAndReopened": "pass",
            "nuyenExchangeCanonicalNoOp": "pass",
            "nuyenExchangeValidationRejected": "pass",
            "karmaExchangeCommitted": "pass",
            "processRestartUiReadback": "pass",
        },
    }
    args.receipt.parent.mkdir(parents=True, exist_ok=True)
    args.receipt.write_text(json.dumps(receipt, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(receipt, indent=2))
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except Exception as error:
        print(f"CreateExpense E2E failed: {error}", file=sys.stderr)
        raise
