from __future__ import annotations

import ast
import copy
import importlib.util
from pathlib import Path
from types import SimpleNamespace
import subprocess
import tempfile
import unittest
from unittest import mock
import xml.etree.ElementTree as ET
import zipfile


REPO = Path(__file__).resolve().parents[1]
DRIVER = REPO / "tests/run_api36_sr5_career_attribute_wizard_e2e.py"
FIXTURE = REPO / "tests/fixtures/career-attribute-advance-e2e.chum5"
WORKFLOW = REPO / ".github/workflows/api36-editing-e2e.yml"

import sys

sys.path.insert(0, str(DRIVER.parent))
SPEC = importlib.util.spec_from_file_location("staged_attribute_driver", DRIVER)
assert SPEC is not None and SPEC.loader is not None
driver = importlib.util.module_from_spec(SPEC)
sys.modules[SPEC.name] = driver
SPEC.loader.exec_module(driver)


def reviewed_checkpoint() -> dict[str, object]:
    identity = {"Abbreviation": "BOD", "Kind": 0}
    quote: dict[str, object] = {
        "Identity": identity,
        **driver.QUOTE_EXACT_VALUES,
        "Prerequisites": copy.deepcopy(driver.PREREQUISITES_EXACT),
        "LogicalRevision": "1" * 64,
        "SourceRevision": "2" * 64,
        "RuleDigest": "3" * 64,
    }
    plan: dict[str, object] = {
        "Identity": copy.deepcopy(identity),
        **driver.PLAN_EXACT_VALUES,
        "ExpenseDateLocal": "2081-05-02T09:30:00",
        "ExpenseId": "44444444-4444-4444-4444-444444444444",
        "ExpectedLogicalRevision": quote["LogicalRevision"],
        "ExpectedSourceRevision": quote["SourceRevision"],
        "ExpectedRuleDigest": quote["RuleDigest"],
    }
    action_plan: dict[str, object] = {
        "OwnerId": "33333333-3333-3333-3333-333333333333",
        "ActionId": plan["ExpenseId"],
        "IdempotencyKey": "",
        "RouteId": driver.REVIEW_ROUTE,
        "Kind": 1,
        "WorkspaceId": {"Value": "workspace-e2e"},
        "ExpectedContentRevision": 7,
        "DomainIdentity": "BOD:Normal",
        "CostQuote": {
            "KarmaCost": 15,
            "NuyenCost": 0,
            "EssenceCost": 0,
            "Availability": None,
            "ElapsedTime": "00:00:00",
            "RuleDigest": quote["RuleDigest"],
            "LogicalRevision": quote["LogicalRevision"],
            "IsExact": True,
            "Blocker": "",
        },
    }
    checkpoint: dict[str, object] = {
        **driver.CHECKPOINT_EXACT_VALUES,
        "Version": 1,
        "Phase": 0,
        "Draft": {
            "OwnerId": "33333333-3333-3333-3333-333333333333",
            "WorkspaceId": {"Value": "workspace-e2e"},
            "ExpectedContentRevision": 7,
            "Quote": quote,
            "Plan": plan,
            "ActionPlan": action_plan,
        },
        "IdempotencyKey": "",
    }
    checkpoint["IdempotencyKey"] = driver.expected_idempotency_key(checkpoint)
    action_plan["IdempotencyKey"] = checkpoint["IdempotencyKey"]
    return checkpoint


def saved_root(checkpoint: dict[str, object]) -> ET.Element:
    draft = checkpoint["Draft"]
    quote = draft["Quote"]  # type: ignore[index]
    plan = draft["Plan"]  # type: ignore[index]
    action_id = str(plan["ExpenseId"])  # type: ignore[index]
    receipt_digest = driver.expected_receipt_digest(checkpoint)
    receipt_attributes = {
        "transactionId": action_id,
        "target": "BOD",
        "kind": "Normal",
        "repairsBurnedEdge": "false",
        "attributeKarmaBefore": "1",
        "attributeKarmaAfter": "2",
        "characterKarmaBefore": "35",
        "characterKarmaAfter": "20",
        "burnedEdgeBefore": "0",
        "burnedEdgeAfter": "0",
        "expenseId": action_id,
        "expenseAmount": "-15",
        "logicalRevision": str(quote["LogicalRevision"]),  # type: ignore[index]
        "sourceRevision": str(quote["SourceRevision"]),  # type: ignore[index]
        "ruleDigest": str(quote["RuleDigest"]),  # type: ignore[index]
        "receiptDigest": receipt_digest,
        "expenseDigest": "4" * 64,
        "postLogicalRevision": "5" * 64,
        "postSourceRevision": "6" * 64,
        "postRuleDigest": "7" * 64,
    }
    receipt_attributes["projectionDigest"] = driver.hashlib.sha256(
        "\0".join(
            (
                "chummer6.presentation.career-attribute-receipt/v1",
                receipt_digest,
                receipt_attributes["expenseDigest"],
                receipt_attributes["postLogicalRevision"],
                receipt_attributes["postSourceRevision"],
                receipt_attributes["postRuleDigest"],
            )
        ).encode("utf-8")
    ).hexdigest()
    root = ET.fromstring(
        f"""
        <character>
          <alias>CareerAttributeAdvanceE2E</alias>
          <karma>20</karma>
          <nuyen>1000</nuyen>
          <attributes><attribute><name>BOD</name><base>0</base><karma>2</karma><totalvalue>3</totalvalue><notes>target-attribute-must-survive</notes></attribute></attributes>
          <expenses>
            <expense><guid>{driver.ORIGINAL_EXPENSE_ID}</guid><date>2081-05-01T08:00:00</date><amount>-1</amount><reason>Older expense</reason><type>Karma</type><refund>False</refund><forcecareervisible>False</forcecareervisible></expense>
            <expense><guid>{action_id}</guid><date>2081-05-02T09:30:00</date><amount>-15</amount><reason>Attribute BOD 2 -&gt; 3</reason><type>Karma</type><refund>False</refund><forcecareervisible>False</forcecareervisible><undo><karmatype>ImproveAttribute</karmatype><nuyentype>AddCyberware</nuyentype><objectid>BOD</objectid><qty>0</qty><extra /></undo></expense>
          </expenses>
          <careerattributeadvancementreceipts><receipt /></careerattributeadvancementreceipts>
          <customstate><sentinel guid="nested-sentinel">keep-nested-structure</sentinel></customstate>
        </character>
        """
    )
    receipt = root.find("./careerattributeadvancementreceipts/receipt")
    assert receipt is not None
    receipt.attrib.update(receipt_attributes)
    return root


class FakePhysicalDevice:
    def __init__(self, **overrides: str) -> None:
        self.serial = overrides.pop("serial", "R5CT30PHYSICAL")
        self.properties = {
            "ro.build.version.sdk": "36",
            "ro.product.cpu.abi": "arm64-v8a",
            "ro.product.cpu.abilist": "arm64-v8a,armeabi-v7a,armeabi",
            "ro.kernel.qemu": "",
            "ro.product.manufacturer": "Google",
            "ro.product.model": "Pixel 9",
            "ro.hardware": "tensor",
            "ro.build.fingerprint": "google/tokay/tokay:16/BP2A/test:user/release-keys",
            "ro.build.id": "BP2A",
            "ro.build.version.security_patch": "2026-08-05",
            "ro.boot.verifiedbootstate": "green",
        }
        self.properties.update(overrides)

    def run(self, *arguments: str) -> SimpleNamespace:
        if arguments != ("get-state",):
            raise AssertionError(arguments)
        return SimpleNamespace(stdout="device\n")

    def shell(self, *arguments: str) -> str:
        if arguments[:1] != ("getprop",):
            raise AssertionError(arguments)
        return self.properties[arguments[1]]


class FakeMetricDevice:
    def __init__(self, nodes: list[driver.shared.UiNode]) -> None:
        self.nodes = nodes
        self.captures: list[str] = []

    def hierarchy(self) -> list[driver.shared.UiNode]:
        return self.nodes

    def swipe_up(self, **_arguments: object) -> None:
        pass

    def swipe_down(self, **_arguments: object) -> None:
        pass

    def capture(self, name: str) -> None:
        self.captures.append(name)


def ui_node(text: str, bounds: str) -> driver.shared.UiNode:
    return driver.shared.UiNode({"text": text, "bounds": bounds})


def initialize_git_repository(path: Path, payload: bytes) -> str:
    path.mkdir()
    subprocess.run(["git", "init", "-q"], cwd=path, check=True)
    subprocess.run(
        ["git", "config", "user.email", "proof@example.invalid"],
        cwd=path,
        check=True,
    )
    subprocess.run(
        ["git", "config", "user.name", "Proof Test"],
        cwd=path,
        check=True,
    )
    (path / "authority.txt").write_bytes(payload)
    subprocess.run(["git", "add", "authority.txt"], cwd=path, check=True)
    subprocess.run(["git", "commit", "-qm", "authority"], cwd=path, check=True)
    return subprocess.run(
        ["git", "rev-parse", "HEAD"],
        cwd=path,
        check=True,
        capture_output=True,
        text=True,
    ).stdout.strip()


def complete_driver_arguments(root: Path, receipt: Path) -> list[str]:
    return [
        "--adb", str(root / "adb"),
        "--apk", str(root / "app.apk"),
        "--build-provenance-manifest", str(root / "build-provenance.json"),
        "--serial", "R5CT30PHYSICAL",
        "--evidence", str(root / "evidence"),
        "--receipt", str(receipt),
        "--workspace-root", str(root),
        driver.DISPOSABLE_DEVICE_FLAG,
    ]


class Api36Sr5CareerAttributeWizardDriverTests(unittest.TestCase):
    def test_driver_is_syntax_valid_phone_only_physical_arm64_api36(self) -> None:
        source = DRIVER.read_text(encoding="utf-8")
        ast.parse(source)
        self.assertIn('"profile": "phone"', source)
        self.assertIn('api != "36"', source)
        self.assertIn('abi != "arm64-v8a"', source)
        self.assertIn('device.serial.startswith("emulator-")', source)
        self.assertIn('qemu == "1"', source)
        self.assertIn('"classification": "non-emulator-arm64-api36"', source)
        self.assertIn('"evidenceNature": "non-cryptographic getprop and adb serial observations"', source)
        self.assertNotIn('"profile": "tablet"', source)
        self.assertNotIn('abi != "x86_64"', source)

    def test_driver_proves_choose_review_resume_apply_receipt_and_acknowledge(self) -> None:
        source = DRIVER.read_text(encoding="utf-8")
        for marker in (
            '"build-sr5-career-wizard"',
            '"sr5-career/advancement"',
            '"sr5-career-action-attribute"',
            '"sr5-career-attribute-picker"',
            '"sr5-career-attribute-review"',
            '"sr5-career-attribute-resume"',
            '"sr5-career-attribute-apply"',
            '"sr5-career-attribute-receipt-acknowledge"',
            'phase=0',
            'version=1',
            'phase=2',
            'version=3',
            'require_same_action(reviewed.payload, applied.payload)',
            'if recovered_receipt_projection != receipt_projection',
            'if resumed_checkpoint != reviewed',
            'if recovered_applied != applied',
            'read_checkpoint(device, required=False)',
        ):
            with self.subTest(marker=marker):
                self.assertIn(marker, source)
        self.assertIn("Acknowledged checkpoint deletion did not survive process restart", source)
        self.assertEqual(3, source.count("shared.force_stop_and_launch_new_process"))

    def test_reviewed_checkpoint_binds_every_exact_typed_field_and_digest(self) -> None:
        checkpoint = reviewed_checkpoint()
        driver.validate_checkpoint(
            checkpoint,
            workspace_id="workspace-e2e",
            expected_content_revision=7,
            phase=0,
            version=1,
        )
        self.assertEqual(driver.CHECKPOINT_FIELDS, set(checkpoint))
        self.assertEqual(
            driver.expected_idempotency_key(checkpoint),
            checkpoint["IdempotencyKey"],
        )

    def test_checkpoint_validation_rejects_field_drift_and_digest_replay(self) -> None:
        checkpoint = reviewed_checkpoint()
        hostile_values = (
            (("Draft", "Quote", "Identity", "Abbreviation"), "AGI"),
            (("Draft", "Plan", "Identity", "Kind"), 1),
            (("Draft", "ExpectedContentRevision"), 8),
            (("Draft", "Quote", "KarmaCost"), 14),
            (("Draft", "Plan", "ExpenseAmount"), -14),
            (
                ("Draft", "Plan", "ExpenseId"),
                "55555555-5555-5555-5555-555555555555",
            ),
            (("Draft", "Quote", "RuleDigest"), "a" * 64),
            (("Draft", "Plan", "ExpectedRuleDigest"), "a" * 64),
            (
                ("Draft", "ActionPlan", "ActionId"),
                "66666666-6666-6666-6666-666666666666",
            ),
            (("Draft", "ActionPlan", "IdempotencyKey"), "a" * 64),
            (("Draft", "ActionPlan", "CostQuote", "RuleDigest"), "a" * 64),
            (("Draft", "ActionPlan", "CostQuote", "KarmaCost"), 14),
            (("IdempotencyKey",), "0" * 64),
            (("Phase",), 1),
        )
        for path, value in hostile_values:
            with self.subTest(path=path):
                hostile = copy.deepcopy(checkpoint)
                target = hostile
                for field in path[:-1]:
                    target = target[field]  # type: ignore[index,assignment]
                target[path[-1]] = value  # type: ignore[index]
                with self.assertRaises(RuntimeError):
                    driver.validate_checkpoint(
                        hostile,
                        workspace_id="workspace-e2e",
                        expected_content_revision=7,
                        phase=0,
                        version=1,
                    )
        extra = copy.deepcopy(checkpoint)
        extra["UnreviewedField"] = "pass-shaped"
        with self.assertRaisesRegex(RuntimeError, "fields are not exact"):
            driver.validate_checkpoint(
                extra,
                workspace_id="workspace-e2e",
                expected_content_revision=7,
                phase=0,
                version=1,
            )
        nested_extra = copy.deepcopy(checkpoint)
        nested_extra["Draft"]["Quote"]["UnreviewedField"] = "pass-shaped"  # type: ignore[index]
        with self.assertRaisesRegex(RuntimeError, "fields are not exact"):
            driver.validate_checkpoint(
                nested_extra,
                workspace_id="workspace-e2e",
                expected_content_revision=7,
                phase=0,
                version=1,
            )

    def test_applied_checkpoint_may_change_only_cas_version_and_phase(self) -> None:
        reviewed = reviewed_checkpoint()
        applied = {**reviewed, "Version": 3, "Phase": 2}
        driver.validate_checkpoint(
            applied,
            workspace_id="workspace-e2e",
            expected_content_revision=7,
            phase=2,
            version=3,
        )
        driver.require_same_action(reviewed, applied)
        hostile = copy.deepcopy(applied)
        hostile["Draft"]["Plan"]["ExpenseId"] = (  # type: ignore[index]
            "77777777-7777-7777-7777-777777777777"
        )
        with self.assertRaisesRegex(RuntimeError, "exact reviewed action"):
            driver.require_same_action(reviewed, hostile)

    def test_receipt_digest_binds_nested_quote_plan_and_all_three_revisions(self) -> None:
        checkpoint = reviewed_checkpoint()
        expected = driver.expected_receipt_digest(checkpoint)
        self.assertRegex(expected, r"^[0-9a-f]{64}$")
        hostile_paths = (
            ("Draft", "Quote", "LogicalRevision"),
            ("Draft", "Quote", "SourceRevision"),
            ("Draft", "Quote", "RuleDigest"),
            ("Draft", "Plan", "ExpenseId"),
            ("Draft", "Plan", "SavedAttributeKarmaPoints"),
            ("Draft", "Plan", "SavedCharacterKarma"),
        )
        for path in hostile_paths:
            with self.subTest(path=path):
                hostile = copy.deepcopy(checkpoint)
                target = hostile
                for field in path[:-1]:
                    target = target[field]  # type: ignore[index,assignment]
                original = target[path[-1]]  # type: ignore[index]
                target[path[-1]] = (  # type: ignore[index]
                    "f" * 64 if isinstance(original, str) else int(original) + 1
                )
                self.assertNotEqual(expected, driver.expected_receipt_digest(hostile))

    def test_saved_payload_validator_binds_expense_receipt_ledger_and_projection(self) -> None:
        checkpoint = reviewed_checkpoint()
        root = saved_root(checkpoint)
        self.assertEqual(
            "44444444-4444-4444-4444-444444444444",
            driver.assert_after(root, checkpoint),
        )
        hostile_values = (
            ("runner Karma", "./karma", None, "21"),
            ("Attribute total", "./attributes/attribute/totalvalue", None, "4"),
            ("original expense", "./expenses/expense[1]/reason", None, "changed"),
            ("generated expense", "./expenses/expense[2]/amount", None, "-14"),
            (
                "receipt digest",
                "./careerattributeadvancementreceipts/receipt",
                "receiptDigest",
                "0" * 64,
            ),
            (
                "projection digest",
                "./careerattributeadvancementreceipts/receipt",
                "projectionDigest",
                "0" * 64,
            ),
        )
        for label, path, attribute, value in hostile_values:
            with self.subTest(label=label):
                hostile = ET.fromstring(ET.tostring(root, encoding="unicode"))
                target = hostile.find(path)
                self.assertIsNotNone(target)
                if attribute is None:
                    target.text = value  # type: ignore[union-attr]
                else:
                    target.set(attribute, value)  # type: ignore[union-attr]
                with self.assertRaises(RuntimeError):
                    driver.assert_after(hostile, checkpoint)

    def test_device_observation_accepts_arm64_and_rejects_emulator_or_wrong_abi(self) -> None:
        authority = driver.android_device_observation(FakePhysicalDevice())
        self.assertEqual("non-emulator-arm64-api36", authority["classification"])
        self.assertEqual(36, authority["apiLevel"])
        self.assertEqual("arm64-v8a", authority["abi"])

        hostile_devices = (
            FakePhysicalDevice(**{"ro.build.version.sdk": "35"}),
            FakePhysicalDevice(**{"ro.product.cpu.abi": "x86_64"}),
            FakePhysicalDevice(**{"ro.kernel.qemu": "1"}),
            FakePhysicalDevice(serial="emulator-5554"),
            FakePhysicalDevice(**{"ro.hardware": "ranchu"}),
        )
        for hostile in hostile_devices:
            with self.subTest(serial=hostile.serial, properties=hostile.properties):
                with self.assertRaises(RuntimeError):
                    driver.android_device_observation(hostile)

    def test_receipt_metrics_are_label_bound_and_fail_on_cardinality(self) -> None:
        exact = FakeMetricDevice(
            [
                ui_node("Refund", "[10,100][90,140]"),
                ui_node("False", "[200,100][280,140]"),
            ]
        )
        self.assertEqual("False", driver.label_bound_value(exact, "Refund", swipes=0))

        duplicate_label = FakeMetricDevice(
            [
                ui_node("Refund", "[10,100][90,140]"),
                ui_node("Refund", "[10,200][90,240]"),
                ui_node("False", "[200,100][280,140]"),
            ]
        )
        with self.assertRaisesRegex(RuntimeError, "label .* ambiguous"):
            driver.label_bound_value(duplicate_label, "Refund", swipes=0)

        duplicate_value = FakeMetricDevice(
            [
                ui_node("Refund", "[10,100][90,140]"),
                ui_node("False", "[200,100][280,140]"),
                ui_node("pass-shaped", "[300,100][400,140]"),
            ]
        )
        with self.assertRaisesRegex(RuntimeError, "2 bound values"):
            driver.label_bound_value(duplicate_value, "Refund", swipes=0)

    def test_apk_authority_requires_an_arm64_native_payload(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            arm64 = root / "arm64.apk"
            with zipfile.ZipFile(arm64, "w") as archive:
                archive.writestr("lib/arm64-v8a/libmonodroid.so", b"arm64")
                archive.writestr("lib/x86_64/libmonodroid.so", b"x64")
            self.assertEqual(["arm64-v8a", "x86_64"], driver.apk_abis(arm64))

            x64 = root / "x64.apk"
            with zipfile.ZipFile(x64, "w") as archive:
                archive.writestr("lib/x86_64/libmonodroid.so", b"x64")
            with self.assertRaisesRegex(RuntimeError, "no ARM64 native payload"):
                driver.apk_abis(x64)

    def test_receipt_preparse_requires_one_absolute_normalized_explicit_path(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            receipt = Path(temporary) / "receipt.json"
            self.assertEqual(
                receipt,
                driver.locate_explicit_receipt(["--receipt", str(receipt)]),
            )
            self.assertEqual(
                receipt,
                driver.locate_explicit_receipt([f"--receipt={receipt}"]),
            )
            hostile_arguments = (
                [],
                ["--receipt"],
                ["--receipt", "relative.json"],
                ["--receipt", f"{temporary}/../receipt.json"],
                ["--receipt", str(receipt), "--receipt", str(receipt)],
            )
            for arguments in hostile_arguments:
                with self.subTest(arguments=arguments):
                    with self.assertRaises(RuntimeError):
                        driver.locate_explicit_receipt(arguments)

    def test_receipt_and_evidence_symlinks_are_rejected_without_touching_targets(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            target = root / "target.json"
            target.write_text('{"status":"pass","stale":true}\n', encoding="utf-8")
            receipt_link = root / "receipt.json"
            receipt_link.symlink_to(target)
            with self.assertRaisesRegex(RuntimeError, "symlink"):
                driver.prepare_receipt_target(receipt_link)
            self.assertIn("stale", target.read_text(encoding="utf-8"))

            evidence_target = root / "evidence-target"
            evidence_target.mkdir()
            evidence_link = root / "evidence"
            evidence_link.symlink_to(evidence_target, target_is_directory=True)
            with self.assertRaisesRegex(RuntimeError, "symlink"):
                driver.validate_external_output_path(
                    evidence_link,
                    label="Evidence path",
                    repository_roots=(root / "unrelated-source",),
                    expect_directory=True,
                )

    def test_external_output_paths_reject_every_source_worktree(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            repositories = tuple(
                root / name for name in ("android", "core", "presentation")
            )
            for repository in repositories:
                repository.mkdir()
            for repository in repositories:
                with self.subTest(repository=repository):
                    with self.assertRaisesRegex(
                        RuntimeError,
                        "outside every source worktree",
                    ):
                        driver.validate_external_output_path(
                            repository / "proof.json",
                            label="Receipt path",
                            repository_roots=repositories,
                            expect_directory=False,
                        )
            driver.validate_external_output_path(
                root / "external" / "evidence",
                label="Evidence path",
                repository_roots=repositories,
                expect_directory=True,
            )

    def test_receipt_and_evidence_layout_must_be_non_overlapping(self) -> None:
        root = Path("/tmp/staged-attribute-output-layout")
        driver.validate_output_layout(
            receipt=root / "receipt.json",
            evidence=root / "evidence",
        )
        hostile_layouts = (
            (root / "output", root / "output"),
            (root / "receipt.json", root / "receipt.json" / "evidence"),
            (root / "evidence" / "receipt.json", root / "evidence"),
        )
        for receipt, evidence in hostile_layouts:
            with self.subTest(receipt=receipt, evidence=evidence):
                with self.assertRaisesRegex(RuntimeError, "non-overlapping"):
                    driver.validate_output_layout(
                        receipt=receipt,
                        evidence=evidence,
                    )

    def test_overlapping_outputs_fail_before_execute_or_device_mutation(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            receipt = root / "receipt.json"
            arguments = complete_driver_arguments(root, receipt)
            evidence_index = arguments.index("--evidence") + 1
            arguments[evidence_index] = str(receipt / "evidence")
            with (
                mock.patch.object(
                    driver,
                    "source_repository_roots",
                    return_value=(REPO,),
                ),
                mock.patch.object(driver, "execute") as execute,
                mock.patch.object(driver.sys, "stderr"),
            ):
                result = driver.main(arguments)
            self.assertEqual(1, result)
            execute.assert_not_called()
            failed = driver.json.loads(receipt.read_text(encoding="utf-8"))
            self.assertEqual("fail", failed["status"])
            self.assertIn("non-overlapping", failed["failure"]["message"])

    def test_fixture_and_remote_paths_reject_shell_metacharacters_before_adb(self) -> None:
        for name in (
            "runner;rm.chum5",
            "runner space.chum5",
            "runner$(touch).chum5",
            "runneré.chum5",
            ".chum5",
            "runner.xml",
        ):
            with self.subTest(name=name):
                with self.assertRaisesRegex(RuntimeError, "safe ASCII"):
                    driver.safe_fixture_basename(Path(name))
        self.assertEqual(
            "runner-1.chum5",
            driver.safe_fixture_basename(Path("runner-1.chum5")),
        )

        remote_device = mock.Mock()
        with self.assertRaisesRegex(RuntimeError, "safe ASCII"):
            driver.remove_remote_temporary_file(
                remote_device,
                "/sdcard/Download/runner;rm.chum5",
            )
        remote_device.shell.assert_not_called()
        remote_device.run.assert_not_called()

    def test_local_build_manifest_is_honest_about_release_attestation(self) -> None:
        source = DRIVER.read_text(encoding="utf-8")
        self.assertIn('"status": "device-pass-source-bound"', source)
        self.assertIn(
            '"releaseEvidenceStatus": "source-and-apk-bound-local-build-not-release-attested"',
            source,
        )
        parsed = driver.parse_args(
            complete_driver_arguments(Path("/tmp"), Path("/tmp/r.json"))
        )
        self.assertEqual(Path("/tmp/build-provenance.json"), parsed.build_provenance_manifest)

    def test_driver_seals_integrated_sources_commits_fixture_and_saved_payload(self) -> None:
        source = DRIVER.read_text(encoding="utf-8")
        for marker in (
            'load_and_verify_manifest(',
            'expected_core_revision=expected_core_head',
            'expected_presentation_revision=expected_presentation_head',
            '"careerWizardModelSha256"',
            '"careerWizardPageSha256"',
            '"attributeWizardPageSha256"',
            '"attributeWizardModelSha256"',
            '"attributeCoordinatorSha256"',
            '"checkpointStoreSha256"',
            '"careerAttributeRulesSha256"',
            '"careerAttributeMutationSha256"',
            '"workspaceStoreSha256"',
            '"sourceGraphAuthority": source_before',
            '"postRunSourceGraphAuthoritySha256": source_before["authoritySha256"]',
            'if source_after != source_before',
            'expected_android_revision=expected_android_head',
            'expected_apk_sha256=expected_apk_sha256',
            'restored_after_apply.content_revision != imported.content_revision + 1',
            'restored_after_apply.payload_sha256 == imported.payload_sha256',
            'if expense_id != applied_plan["ExpenseId"]',
            '"./careerattributeadvancementreceipts/receipt"',
            'expected_receipt_digest(checkpoint)',
        ):
            with self.subTest(marker=marker):
                self.assertIn(marker, source)
        driver.require_canonical_import_fixture(ET.parse(FIXTURE).getroot())

    def test_git_revision_requires_exact_head_and_clean_worktree(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            repository = Path(temporary) / "repo"
            revision = initialize_git_repository(repository, b"clean")
            self.assertEqual(
                revision,
                driver.git_revision(repository, expected=revision),
            )
            with self.assertRaisesRegex(RuntimeError, "expected .* got"):
                driver.git_revision(repository, expected="0" * 40)
            (repository / "authority.txt").write_bytes(b"dirty")
            with self.assertRaisesRegex(RuntimeError, "worktree is dirty"):
                driver.git_revision(repository, expected=revision)

    def test_source_graph_snapshot_binds_expected_apk_and_rejects_drift(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            android = root / "android"
            core = root / "core"
            presentation = root / "presentation"
            android_revision = initialize_git_repository(android, b"android")
            core_revision = initialize_git_repository(core, b"core")
            presentation_revision = initialize_git_repository(
                presentation,
                b"presentation",
            )
            apk = root / "candidate.apk"
            with zipfile.ZipFile(apk, "w") as archive:
                archive.writestr("lib/arm64-v8a/libmonodroid.so", b"arm64")
            expected_apk = driver.shared.sha256(apk)
            if True:
                snapshot = driver.source_graph_snapshot(
                    android_root=android,
                    core_root=core,
                    presentation_root=presentation,
                    apk=apk,
                    expected_apk_sha256=expected_apk,
                    expected_android_revision=android_revision,
                    expected_core_revision=core_revision,
                    expected_presentation_revision=presentation_revision,
                    source_paths={"androidAuthoritySha256": android / "authority.txt"},
                )
                self.assertEqual(expected_apk, snapshot["apkSha256"])
                self.assertEqual(android_revision, snapshot["androidSourceRevision"])
                self.assertEqual(
                    snapshot["authoritySha256"],
                    driver.canonical_json_sha256(
                        {
                            key: value
                            for key, value in snapshot.items()
                            if key != "authoritySha256"
                        }
                    ),
                )
                with self.assertRaisesRegex(RuntimeError, "APK SHA-256 differs"):
                    driver.source_graph_snapshot(
                        android_root=android,
                        core_root=core,
                        presentation_root=presentation,
                        apk=apk,
                        expected_apk_sha256="0" * 64,
                        expected_android_revision=android_revision,
                        expected_core_revision=core_revision,
                        expected_presentation_revision=presentation_revision,
                        source_paths={
                            "androidAuthoritySha256": android / "authority.txt"
                        },
                    )
                (android / "authority.txt").write_bytes(b"drift")
                with self.assertRaisesRegex(RuntimeError, "worktree is dirty"):
                    driver.source_graph_snapshot(
                        android_root=android,
                        core_root=core,
                        presentation_root=presentation,
                        apk=apk,
                        expected_apk_sha256=expected_apk,
                        expected_android_revision=android_revision,
                        expected_core_revision=core_revision,
                        expected_presentation_revision=presentation_revision,
                        source_paths={
                            "androidAuthoritySha256": android / "authority.txt"
                        },
                    )

    def test_missing_disposable_confirmation_overwrites_stale_pass_with_failure(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            receipt = root / "receipt.json"
            receipt.write_text('{"status":"pass","stale":true}\n', encoding="utf-8")
            with mock.patch.object(
                driver,
                "source_repository_roots",
                return_value=(REPO,),
            ):
                result = driver.main(
                    [
                        "--adb", "/missing/adb",
                        "--apk", "/missing/app.apk",
                        "--build-provenance-manifest", str(root / "build-provenance.json"),
                        "--serial", "DISPOSABLE",
                        "--evidence", str(root / "evidence"),
                        "--receipt", str(receipt),
                        "--workspace-root", str(root),
                    ]
                )
            self.assertEqual(1, result)
            failed = driver.json.loads(receipt.read_text(encoding="utf-8"))
            self.assertEqual("fail", failed["status"])
            self.assertEqual("fail", failed["executionStatus"])
            self.assertNotIn("stale", failed)
            self.assertIn(driver.DISPOSABLE_DEVICE_FLAG, failed["failure"]["message"])

    def test_argparse_failures_replace_stale_pass_with_explicit_failure(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            cases = {
                "missing-required-serial": [
                    argument
                    for pair in (
                        ("--adb", str(root / "adb")),
                        ("--apk", str(root / "app.apk")),
                        ("--build-provenance-manifest", str(root / "build-provenance.json")),
                        ("--evidence", str(root / "evidence")),
                        ("--receipt", str(root / "missing.json")),
                        ("--workspace-root", str(root)),
                    )
                    for argument in pair
                ],
                "unknown-option": complete_driver_arguments(
                    root,
                    root / "unknown.json",
                ) + ["--not-a-real-option"],
            }
            for name, arguments in cases.items():
                with self.subTest(name=name):
                    receipt = Path(arguments[arguments.index("--receipt") + 1])
                    receipt.write_text(
                        '{"status":"pass","stale":true}\n',
                        encoding="utf-8",
                    )
                    with mock.patch.object(driver.sys, "stderr"):
                        result = driver.main(arguments)
                    self.assertEqual(2, result)
                    failed = driver.json.loads(receipt.read_text(encoding="utf-8"))
                    self.assertEqual("fail", failed["status"])
                    self.assertEqual("ArgumentParseError", failed["failure"]["type"])
                    self.assertNotIn("stale", failed)

    def test_missing_build_provenance_manifest_is_an_explicit_argument_failure(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            receipt = root / "receipt.json"
            arguments = complete_driver_arguments(root, receipt)
            index = arguments.index("--build-provenance-manifest")
            del arguments[index:index + 2]
            with mock.patch.object(driver.sys, "stderr"):
                result = driver.main(arguments)
            self.assertEqual(2, result)
            failed = driver.json.loads(receipt.read_text(encoding="utf-8"))
            self.assertEqual("fail", failed["status"])
            self.assertEqual("ArgumentParseError", failed["failure"]["type"])
            self.assertIsNone(failed["buildProvenance"])

    def test_missing_explicit_receipt_does_not_unlink_an_unidentified_stale_file(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            stale = Path(temporary) / "unidentified.json"
            stale_payload = '{"status":"pass","stale":true}\n'
            stale.write_text(stale_payload, encoding="utf-8")
            with mock.patch.object(driver.sys, "stderr"):
                result = driver.main(["--serial", "R5CT30PHYSICAL"])
            self.assertEqual(2, result)
            self.assertEqual(stale_payload, stale.read_text(encoding="utf-8"))

    def test_help_requires_no_receipt_and_performs_no_receipt_preparation(self) -> None:
        with (
            mock.patch.object(driver, "prepare_receipt_target") as prepare,
            mock.patch.object(driver, "preparse_repository_roots") as roots,
            mock.patch.object(driver.sys, "stdout"),
        ):
            result = driver.main(["--help"])
        self.assertEqual(0, result)
        prepare.assert_not_called()
        roots.assert_not_called()

    def test_help_with_explicit_receipt_preserves_existing_bytes(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            receipt = Path(temporary) / "receipt.json"
            original = b'{"status":"pass","mustRemain":true}\n'
            receipt.write_bytes(original)
            with (
                mock.patch.object(driver, "prepare_receipt_target") as prepare,
                mock.patch.object(driver, "preparse_repository_roots") as roots,
                mock.patch.object(driver.sys, "stdout"),
            ):
                result = driver.main(
                    ["--receipt", str(receipt), "--help"]
                )
            self.assertEqual(0, result)
            prepare.assert_not_called()
            roots.assert_not_called()
            self.assertEqual(original, receipt.read_bytes())

    def test_in_worktree_receipt_is_rejected_without_unlinking_it(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            source_root = root / "android"
            source_root.mkdir()
            receipt = source_root / "receipt.json"
            stale_payload = '{"status":"pass","stale":true}\n'
            receipt.write_text(stale_payload, encoding="utf-8")
            arguments = complete_driver_arguments(root, receipt)
            with (
                mock.patch.object(
                    driver,
                    "preparse_repository_roots",
                    return_value=(source_root,),
                ),
                mock.patch.object(driver.sys, "stderr"),
            ):
                result = driver.main(arguments)
            self.assertEqual(2, result)
            self.assertEqual(stale_payload, receipt.read_text(encoding="utf-8"))

    def test_driver_preflights_before_device_mutation_and_cleans_remote_fixture(self) -> None:
        source = DRIVER.read_text(encoding="utf-8")
        execute_source = source[
            source.index("def execute("):source.index("def failure_receipt(")
        ]
        self.assertLess(
            source.index("source_before = source_graph_snapshot("),
            source.index("device = shared.Device("),
        )
        self.assertLess(
            source.index("source_before = source_graph_snapshot("),
            source.index("device.install_verified("),
        )
        self.assertIn('parser.add_argument(DISPOSABLE_DEVICE_FLAG, action="store_true")', source)
        self.assertIn('parser.add_argument("--build-provenance-manifest", type=Path, required=True)', source)
        self.assertIn("validate_output_layout(receipt=receipt_path", source)
        self.assertIn("remove_remote_temporary_file(device, str(remote", source)
        self.assertIn("shared.ADB_FILE_HIERARCHY_REMOTE_PATH", source)
        self.assertIn('"remoteTemporaryFiles": remote_temporary_files', source)
        self.assertIn('"deletedAndVerified": False', source)
        self.assertIn("device.require_transport_stability(expected_api_level=\"36\")", source)
        self.assertIn("authorize_remote_cleanup_once", source)
        self.assertIn('context["adbTransport"] = device.transport_summary()', source)
        self.assertNotIn("visible_texts_across_page", source)
        self.assertIn("label_bound_value(device, label", source)
        self.assertEqual(3, source.count("source_graph_snapshot("))
        self.assertLess(
            execute_source.index("device.require_transport_stability("),
            execute_source.index("device_observation = android_device_observation(device)"),
        )
        self.assertLess(
            execute_source.index("device_observation = android_device_observation(device)"),
            execute_source.index("remove_remote_temporary_file(device, str(remote"),
        )
        self.assertLess(
            execute_source.index("SAFE_ADB_SERIAL.fullmatch(args.serial)"),
            execute_source.index("device = shared.Device("),
        )
        self.assertIsNone(driver.SAFE_ADB_SERIAL.fullmatch("serial;adb"))
        self.assertIn("if device_validated:", execute_source)
        self.assertLess(
            source.index("journey = prove_staged_wizard("),
            source.rindex("source_after = source_graph_snapshot("),
        )

    def test_physical_attribute_driver_is_not_misrepresented_as_hosted_x86(self) -> None:
        workflow = WORKFLOW.read_text(encoding="utf-8")
        runner = (REPO / "scripts/run-api36-editing-e2e-ci.sh").read_text(
            encoding="utf-8"
        )
        self.assertIn("arch: x86_64", workflow)
        self.assertNotIn("career-attribute-advance", workflow)
        self.assertNotIn("run_api36_sr5_career_attribute_wizard_e2e.py", workflow)
        self.assertNotIn("run_api36_sr5_career_attribute_wizard_e2e.py", runner)
        self.assertNotIn(DRIVER.name, workflow)
        self.assertNotIn(DRIVER.name, runner)


if __name__ == "__main__":
    unittest.main()
