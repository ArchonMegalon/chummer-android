from __future__ import annotations

import copy
import hashlib
import importlib.util
import json
import os
import subprocess
import sys
import tempfile
import time
import unittest
from pathlib import Path


REPO = Path(__file__).resolve().parents[1]
AUTHORITY_SCRIPT = REPO / "scripts/verify_internal_phone_beta_package_authority.py"
GRAPH_SCRIPT = REPO / "scripts/verify_internal_phone_beta_compile_graph.py"
RUNNER = REPO / "scripts/run_internal_phone_beta_bounded.py"
RECEIPT_VERIFIER = REPO / "scripts/verify_internal_phone_beta_compile_receipt.py"
BUILD = REPO / "scripts/build-internal-phone-beta-native-compile.sh"
WORKFLOW = REPO / ".github/workflows/internal-phone-beta-package-compile.yml"
AUTHORITY_MANIFEST = REPO / "eng/internal-phone-beta-package-authority.json"


def load_module(path: Path, name: str):
    spec = importlib.util.spec_from_file_location(name, path)
    assert spec is not None and spec.loader is not None
    module = importlib.util.module_from_spec(spec)
    sys.modules[name] = module
    spec.loader.exec_module(module)
    return module


class InternalPhoneBetaBuildContractTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        cls.authority = load_module(AUTHORITY_SCRIPT, "verify_internal_phone_beta_package_authority")
        cls.graph = load_module(GRAPH_SCRIPT, "verify_internal_phone_beta_compile_graph")
        cls.receipt = load_module(RECEIPT_VERIFIER, "verify_internal_phone_beta_compile_receipt")

    def test_build_is_exact_locked_serialized_and_bounded(self) -> None:
        text = BUILD.read_text(encoding="utf-8")
        self.assertIn("--locked-mode", text)
        self.assertIn('"-p:ChummerUseLocalCompatibilityTree=false"', text)
        self.assertIn('"-p:ChummerUseLockedOwnerContractPackages=true"', text)
        self.assertIn('"-p:RestoreLockedMode=true"', text)
        self.assertIn('"-p:RestorePackagesWithLockFile=true"', text)
        self.assertIn("-m:1", text)
        self.assertIn("-p:BuildInParallel=false", text)
        self.assertIn("--no-restore", text)
        self.assertEqual(5, text.count("run_bounded "))
        self.assertIn("processGroupTermination: true", text)
        self.assertIn("totalSeconds: 3600", text)
        self.assertIn('proofScope: "Native.CompileCheck_dependency_only"', text)
        self.assertIn("fullMauiBuild: false", text)
        self.assertIn("coreDataLangContentVerified: false", text)
        self.assertIn(
            'dependencyMode: "locked_package_closure_with_pinned_presentation_source"',
            text,
        )
        self.assertIn("packageOnly: false", text)
        self.assertIn("sourceCheckoutsPresent: true", text)
        self.assertIn("ambientSiblingRootsAllowed: false", text)
        self.assertNotIn("locked_package_no_siblings", text)
        self.assertNotIn('>"$restore_log"', text)
        self.assertNotIn('>"$build_log"', text)
        self.assertIn("persist_evidence", text)
        self.assertIn("verify_internal_phone_beta_compile_receipt.py", text)
        for field in self.receipt.PASS_ALLOWED_KEYS:
            self.assertIn(f"{field}:", text, field)

    def test_source_sibling_inputs_are_rejected_not_forwarded(self) -> None:
        text = BUILD.read_text(encoding="utf-8")
        self.assertIn("require_canonical_directory CHUMMER_PRESENTATION_ROOT", text)
        self.assertIn('"-p:ChummerPresentationRoot=$CHUMMER_PRESENTATION_ROOT"', text)
        for variable in (
            "CHUMMER_CORE_ENGINE_ROOT", "CHUMMER_RUN_SERVICES_ROOT",
            "CHUMMER_HUB_REGISTRY_ROOT", "CHUMMER_UI_KIT_ROOT",
            "CHUMMER_MEDIA_FACTORY_ROOT",
        ):
            self.assertIn(variable, text)
        for forbidden in (
            "ChummerLocalContractsProject=", "ChummerLocalCampaignContractsProject=",
            "ChummerLocalRunContractsProject=", "ChummerLocalUiKitProject=",
        ):
            self.assertNotIn(forbidden, text)

    def test_workflow_is_inactive_internal_only_and_exactly_pinned(self) -> None:
        text = WORKFLOW.read_text(encoding="utf-8")
        self.assertIn('on:\n  push:\n    branches-ignore:\n      - "**"', text)
        self.assertNotIn("\non: []\n", text)
        self.assertIn("if: ${{ false }}", text)
        self.assertIn("732a33cb8d3c704b8a86e1249eab46508339a105", text)
        final_receipt_name = "UI_CURRENT_MAIN_PACKAGE_PLANE.generated.json"
        self.assertEqual(1, text.count(final_receipt_name))
        self.assertIn(
            "CHUMMER_CURRENT_UI_PACKAGE_AUTHORITY_RECEIPT: "
            "${{ runner.temp }}/current-ui-input/" + final_receipt_name,
            text,
        )
        self.assertNotIn("ui-current-authority-1438978f-main-cache-hit.receipt.json", text)
        self.assertNotIn("ui-current-authority-", text)
        self.assertIn("Chummer.Desktop.Runtime", text)
        self.assertIn("Chummer.Presentation", text)
        self.assertIn("dotnet-version: 10.0.111", text)
        self.assertIn("build-internal-phone-beta-native-compile.sh", text)
        self.assertIn("pinned-source locked-package", text)
        self.assertNotIn("package-only", text)
        self.assertNotIn("upload-artifact", text)
        self.assertNotIn("android-emulator", text)
        self.assertNotIn("Google Play", text)

    def test_compile_check_uses_the_same_two_android_project_references(self) -> None:
        android = (REPO / "src/Chummer.Android/Chummer.Android.csproj").read_text(encoding="utf-8")
        compile_check = (
            REPO / "tests/Chummer.Android.Native.CompileCheck/Chummer.Android.Native.CompileCheck.csproj"
        ).read_text(encoding="utf-8")
        references = (
            "$(ChummerPresentationRoot)/Chummer.Desktop.Runtime/Chummer.Desktop.Runtime.csproj",
            "$(ChummerPresentationRoot)/Chummer.Presentation/Chummer.Presentation.csproj",
        )
        for reference in references:
            self.assertIn(reference, android)
            self.assertIn(reference, compile_check)
        self.assertEqual(2, android.count("<ProjectReference Include="))
        self.assertEqual(2, compile_check.count("<ProjectReference Include="))
        self.assertIn("<RestorePackagesWithLockFile>true</RestorePackagesWithLockFile>", compile_check)
        lock = REPO / "tests/Chummer.Android.Native.CompileCheck/packages.lock.json"
        self.assertTrue(lock.is_file())
        self.assertEqual(
            "f421578231b43f5bd81eebedb5b82fd4b9345dc91bc2af005cbefcaab117b00b",
            self.authority.sha256(lock),
        )

    def test_compile_check_pass_cannot_satisfy_api36_or_play_beta_gates(self) -> None:
        compile_contract = "chummer.android.internal-phone-beta-native-compile/v2"
        aggregate = (REPO / "scripts/verify-api36-editing-e2e-aggregate.py").read_text(encoding="utf-8")
        finalizer = (REPO / "scripts/finalize-api36-e2e-journey-receipt.py").read_text(encoding="utf-8")
        active_workflow = (REPO / ".github/workflows/api36-editing-e2e.yml").read_text(encoding="utf-8")
        play_schema = (REPO / "play/release-receipt.schema.json").read_text(encoding="utf-8")
        for gate in (aggregate, finalizer, active_workflow, play_schema):
            self.assertNotIn(compile_contract, gate)
            self.assertNotIn("internal-phone-beta-native-compile.json", gate)
        self.assertIn("fullMauiBuild: false", BUILD.read_text(encoding="utf-8"))
        self.assertIn("physical_api36_execution", BUILD.read_text(encoding="utf-8"))

    def test_bounded_runner_journals_success_and_timeout_without_retry(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            journal = root / "journal.jsonl"
            success = root / "success.log"
            side_effect = root / "side-effect.txt"
            completed = subprocess.run(
                [
                    sys.executable, str(RUNNER), "--journal", str(journal),
                    "--output", str(success), "--phase", "success",
                    "--timeout-seconds", "2", "--deadline-epoch", str(time.time() + 10),
                    "--", sys.executable, "-c",
                    "from pathlib import Path; import sys; "
                    "Path(sys.argv[1]).write_text('executed'); print('green')",
                    str(side_effect),
                ],
                check=False, capture_output=True, text=True,
            )
            self.assertEqual(0, completed.returncode, completed.stderr)
            self.assertEqual("executed", side_effect.read_text(encoding="utf-8"))
            self.assertEqual("green\n", success.read_text(encoding="utf-8"))
            timed = root / "timed.log"
            completed = subprocess.run(
                [
                    sys.executable, str(RUNNER), "--journal", str(journal),
                    "--output", str(timed), "--phase", "timeout",
                    "--timeout-seconds", "0.05", "--deadline-epoch", str(time.time() + 10),
                    "--", sys.executable, "-c", "import time; time.sleep(5)",
                ],
                check=False, capture_output=True, text=True,
            )
            self.assertEqual(124, completed.returncode)
            rows = [json.loads(line) for line in journal.read_text(encoding="utf-8").splitlines()]
            self.assertEqual(4, len(rows))
            self.assertTrue(rows[-1]["timedOut"])
            self.assertTrue(all(row["publicationAuthorized"] is False for row in rows))
            self.assertTrue(all(row.get("processGroupTermination", True) for row in rows))

    def test_bounded_runner_kills_sigterm_resistant_descendant(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            child_pid_path = root / "child.pid"
            child_code = (
                "import os,signal,time; from pathlib import Path; "
                "signal.signal(signal.SIGTERM, signal.SIG_IGN); "
                f"Path({str(child_pid_path)!r}).write_text(str(os.getpid())); "
                "time.sleep(30)"
            )
            parent_code = (
                "import subprocess,sys,time; "
                f"subprocess.Popen([sys.executable, '-c', {child_code!r}]); "
                "time.sleep(30)"
            )
            journal = root / "journal.jsonl"
            output = root / "timeout.log"
            completed = subprocess.run(
                [
                    sys.executable, str(RUNNER), "--journal", str(journal),
                    "--output", str(output), "--phase", "resistant-child",
                    "--timeout-seconds", "0.4", "--term-grace-seconds", "0.1",
                    "--deadline-epoch", str(time.time() + 10),
                    "--", sys.executable, "-c", parent_code,
                ],
                check=False, capture_output=True, text=True, timeout=5,
            )
            self.assertEqual(124, completed.returncode, completed.stderr)
            child_pid = int(child_pid_path.read_text(encoding="utf-8"))
            with self.assertRaises(ProcessLookupError):
                os.kill(child_pid, 0)
            rows = [json.loads(line) for line in journal.read_text(encoding="utf-8").splitlines()]
            self.assertEqual({
                "sigtermSent": True,
                "sigkillSent": True,
                "groupAbsent": True,
            }, rows[-1]["termination"])

    def seed_compile_receipt(self, root: Path) -> tuple[Path, Path, dict[str, object]]:
        receipt = root / "compile-receipt.json"
        evidence = Path(f"{receipt}.evidence")
        evidence.mkdir(mode=0o700)
        evidence_payloads = {
            "authority-intake.log": json.dumps({
                "authorityClass": self.receipt.AUTHORITY_CLASS,
                "contractName": "chummer.android.internal-phone-beta-package-authority/v2",
                "doesNotAssert": [
                    "api36_device_execution", "google_play_upload", "public_release_readiness",
                    "publication_authority", "tablet_readiness",
                ],
                "ownerPackagePinCount": 6,
                "packagePinCount": 18,
                "publicationAuthorized": False,
                "receiptSha256": self.receipt.AUTHORITY_RECEIPT_SHA256,
                "status": "pass",
            }, sort_keys=True) + "\n",
            "restore.log": "Restored compile-check dependencies.\n",
            "owned-compile-graph.log": json.dumps({
                "compileProject": str(
                    root / "tests/Chummer.Android.Native.CompileCheck/Chummer.Android.Native.CompileCheck.csproj"
                ),
                "compiledOwnedSourceCount": 215,
                "generatedProjectReferenceCount": 3,
                "issues": [],
                "repoRoot": str(root),
                "schema": "chummer.android.native-compile-graph/v1",
                "status": "pass",
                "workspaceRoot": str(root.parent),
            }, sort_keys=True) + "\n",
            "compile-graph.json": json.dumps({
                "ambientSiblingRootsAllowed": False,
                "chummerPackageCount": 12,
                "contractName": "chummer.android.internal-phone-beta-compile-graph/v2",
                "dependencyMode": "locked_package_closure_with_pinned_presentation_source",
                "doesNotAssert": ["api36_device_execution", "public_release_readiness"],
                "packageOnly": False,
                "projectCount": 3,
                "presentationSourceProjectLibraries": [
                    "Chummer.Desktop.Runtime/1.0.0", "Chummer.Presentation/1.0.0",
                ],
                "publicationAuthorized": False,
                "restoreLockedMode": True,
                "sourceCheckoutsPresent": True,
                "status": "pass",
            }, sort_keys=True) + "\n",
            "build.log": "Build succeeded.\n    0 Warning(s)\n    0 Error(s)\n",
        }
        for name, text in evidence_payloads.items():
            (evidence / name).write_text(text, encoding="utf-8")
        (evidence / "authority-binding.json").write_bytes(AUTHORITY_MANIFEST.read_bytes())

        presentation_root = str(root / "pinned-presentation")
        phase_commands = {
            "authority-intake": [
                "python3", "verify_internal_phone_beta_package_authority.py",
                "--presentation-root", presentation_root,
            ],
            "locked-restore": [
                "dotnet", "restore", "--locked-mode", "--disable-parallel",
                f"-p:ChummerPresentationRoot={presentation_root}",
                "-p:ChummerUseLocalCompatibilityTree=false",
                "-p:ChummerUseLockedOwnerContractPackages=true",
                "-p:RestoreLockedMode=true", "-p:RestorePackagesWithLockFile=true",
            ],
            "owned-compile-graph": [
                "python3", "verify_native_compile_graph.py", "--require-assets",
            ],
            "package-compile-graph": [
                "python3", "verify_internal_phone_beta_compile_graph.py",
                "--presentation-root", presentation_root,
            ],
            "serialized-native-compile": [
                "dotnet", "build", "--no-restore", "--warnaserror", "-m:1",
                f"-p:ChummerPresentationRoot={presentation_root}",
                "-p:BuildInParallel=false",
                "-p:ChummerUseLocalCompatibilityTree=false",
                "-p:ChummerUseLockedOwnerContractPackages=true",
                "-p:RestoreLockedMode=true", "-p:RestorePackagesWithLockFile=true",
            ],
        }
        journal_rows = []
        for phase, evidence_name in self.receipt.PHASES:
            journal_rows.extend(({
                "command": phase_commands[phase],
                "contractName": "chummer.android.internal-phone-beta-command-journal/v1",
                "event": "started",
                "phase": phase,
                "processGroupTermination": True,
                "publicationAuthorized": False,
                "timeoutSeconds": 900.0,
            }, {
                "contractName": "chummer.android.internal-phone-beta-command-journal/v1",
                "elapsedSeconds": 1.0,
                "event": "finished",
                "exitCode": 0,
                "outputSha256": hashlib.sha256((evidence / evidence_name).read_bytes()).hexdigest(),
                "phase": phase,
                "processGroupTermination": True,
                "publicationAuthorized": False,
                "termination": {
                    "groupAbsent": True, "sigkillSent": False, "sigtermSent": False,
                },
                "timedOut": False,
            }))
        (evidence / "command-journal.jsonl").write_text(
            "".join(json.dumps(row, sort_keys=True) + "\n" for row in journal_rows),
            encoding="utf-8",
        )

        rows = []
        digests = {}
        sizes = {}
        for name in self.receipt.EXPECTED_EVIDENCE:
            path = evidence / name
            digest = hashlib.sha256(path.read_bytes()).hexdigest()
            size = path.stat().st_size
            rows.append({"path": name, "sha256": digest, "sizeBytes": size})
            digests[name] = digest
            sizes[name] = size
        payload = {
            "contractName": self.receipt.CONTRACT,
            "schema": self.receipt.CONTRACT,
            "status": "pass",
            "authorityClass": self.receipt.AUTHORITY_CLASS,
            "publicationAuthorized": False,
            "proofScope": self.receipt.PROOF_SCOPE,
            "dependencyMode": "locked_package_closure_with_pinned_presentation_source",
            "packageOnly": False,
            "restoreLockedMode": True,
            "sourceCheckoutsPresent": True,
            "ambientSiblingRootsAllowed": False,
            "presentationSourceProjectLibraries": [
                "Chummer.Desktop.Runtime/1.0.0", "Chummer.Presentation/1.0.0",
            ],
            "serializedBuild": True,
            "sdkVersion": self.receipt.CONSUMER_SDK_VERSION,
            "producerSdkVersion": self.receipt.PRODUCER_SDK_VERSION,
            "androidCommit": "a" * 40,
            "androidTree": "b" * 40,
            "androidWorktreeClean": True,
            "presentationCommit": self.receipt.PRESENTATION_COMMIT,
            "presentationTree": self.receipt.PRESENTATION_TREE,
            "authorityReceiptSha256": self.receipt.AUTHORITY_RECEIPT_SHA256,
            "authorityCacheManifestSha256": self.receipt.AUTHORITY_CACHE_MANIFEST_SHA256,
            "packageAuthoritySha256": self.receipt.PACKAGE_AUTHORITY_SHA256,
            "evidenceDirectory": str(evidence),
            "evidence": rows,
            "evidenceBindings": {
                row["path"]: {"sha256": row["sha256"], "sizeBytes": row["sizeBytes"]}
                for row in rows
            },
            "authorityBindingSha256": digests["authority-binding.json"],
            "restoreOutputSha256": digests["restore.log"],
            "compileGraphSha256": digests["compile-graph.json"],
            "buildOutputSha256": digests["build.log"],
            "journalSha256": digests["command-journal.jsonl"],
            "journalSizeBytes": sizes["command-journal.jsonl"],
            "lockSha256": self.receipt.ANDROID_LOCK_SHA256,
            "lockSizeBytes": self.receipt.ANDROID_LOCK_SIZE,
            "assetsSha256": "c" * 64,
            "artifact": {
                "path": (
                    "tests/Chummer.Android.Native.CompileCheck/bin/Release/net10.0/"
                    "Chummer.Android.Native.CompileCheck.dll"
                ),
                "kind": "native_compile_check_dependency_dll",
                "scope": self.receipt.PROOF_SCOPE,
                "sha256": "d" * 64,
                "sizeBytes": 123,
                "fullMauiArtifact": False,
            },
            "phaseResults": copy.deepcopy(self.receipt.EXPECTED_PHASE_RESULTS),
            "executionBounds": {
                "perCommandSeconds": 900,
                "totalSeconds": 3600,
                "processGroupTermination": True,
            },
            "fullMauiBuild": False,
            "coreDataLangContentVerified": False,
            "laterDeviceGateRequirements": list(self.receipt.LATER_DEVICE_REQUIREMENTS),
            "doesNotAssert": list(self.receipt.PASS_DOES_NOT_ASSERT),
        }
        receipt.write_text(json.dumps(payload), encoding="utf-8")
        return receipt, evidence, payload

    def seed_blocked_compile_receipt(
        self,
        root: Path,
    ) -> tuple[Path, Path, dict[str, object]]:
        receipt, evidence, payload = self.seed_compile_receipt(root)
        (evidence / "build.log").write_text(
            "Build FAILED.\n    0 Warning(s)\n    1 Error(s)\n", encoding="utf-8"
        )
        self.refresh_evidence_binding(
            receipt, evidence, payload, "build.log", "serialized-native-compile"
        )
        journal = evidence / "command-journal.jsonl"
        journal_rows = [
            json.loads(line) for line in journal.read_text(encoding="utf-8").splitlines()
        ]
        journal_rows[-1]["exitCode"] = 1
        journal.write_text(
            "".join(json.dumps(row, sort_keys=True) + "\n" for row in journal_rows),
            encoding="utf-8",
        )
        self.refresh_evidence_binding(
            receipt, evidence, payload, "command-journal.jsonl"
        )
        payload.update({
            "status": "blocked",
            "failureStage": "serialized-native-compile",
            "retryPerformed": False,
            "doesNotAssert": list(self.receipt.BLOCKED_DOES_NOT_ASSERT),
        })
        for field in self.receipt.PASS_ONLY_FIELDS:
            payload.pop(field, None)
        journal = next(
            row for row in payload["evidence"]
            if row["path"] == "command-journal.jsonl"
        )
        payload["journalSha256"] = journal["sha256"]
        payload["journalSizeBytes"] = journal["sizeBytes"]
        receipt.write_text(json.dumps(payload), encoding="utf-8")
        return receipt, evidence, payload

    def refresh_evidence_binding(
        self,
        receipt: Path,
        evidence: Path,
        payload: dict[str, object],
        name: str,
        phase: str | None = None,
    ) -> None:
        top_level = {
            "authority-binding.json": "authorityBindingSha256",
            "restore.log": "restoreOutputSha256",
            "compile-graph.json": "compileGraphSha256",
            "build.log": "buildOutputSha256",
            "command-journal.jsonl": "journalSha256",
        }

        def bind(bound_name: str) -> str:
            path = evidence / bound_name
            digest = hashlib.sha256(path.read_bytes()).hexdigest()
            size = path.stat().st_size
            row = next(row for row in payload["evidence"] if row["path"] == bound_name)
            row.update({"sha256": digest, "sizeBytes": size})
            payload["evidenceBindings"][bound_name] = {
                "sha256": digest,
                "sizeBytes": size,
            }
            if bound_name in top_level:
                payload[top_level[bound_name]] = digest
            if bound_name == "command-journal.jsonl":
                payload["journalSizeBytes"] = size
            return digest

        digest = bind(name)
        if phase is not None:
            journal = evidence / "command-journal.jsonl"
            rows = [json.loads(line) for line in journal.read_text(encoding="utf-8").splitlines()]
            finished = next(
                row for row in rows
                if row["phase"] == phase and row["event"] == "finished"
            )
            finished["outputSha256"] = digest
            journal.write_text(
                "".join(json.dumps(row, sort_keys=True) + "\n" for row in rows),
                encoding="utf-8",
            )
            bind("command-journal.jsonl")
        receipt.write_text(json.dumps(payload), encoding="utf-8")

    def refresh_blocked_journal_binding(
        self,
        receipt: Path,
        evidence: Path,
        payload: dict[str, object],
    ) -> None:
        journal = evidence / "command-journal.jsonl"
        digest = hashlib.sha256(journal.read_bytes()).hexdigest()
        size = journal.stat().st_size
        row = next(
            row for row in payload["evidence"]
            if row["path"] == "command-journal.jsonl"
        )
        row.update({"sha256": digest, "sizeBytes": size})
        payload["journalSha256"] = digest
        payload["journalSizeBytes"] = size
        receipt.write_text(json.dumps(payload), encoding="utf-8")

    def test_compile_receipt_verifies_persisted_evidence_bytes(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            receipt, _evidence, _payload = self.seed_compile_receipt(Path(temporary))
            result = self.receipt.verify_receipt(receipt)
            self.assertEqual("pass", result["status"])
            self.assertEqual(7, len(result["evidence"]))

    def test_pass_receipt_rejects_every_missing_authoritative_field(self) -> None:
        for field in sorted(self.receipt.PASS_ALLOWED_KEYS):
            with self.subTest(field=field), tempfile.TemporaryDirectory() as temporary:
                receipt, _evidence, payload = self.seed_compile_receipt(Path(temporary))
                payload.pop(field)
                receipt.write_text(json.dumps(payload), encoding="utf-8")
                with self.assertRaises(ValueError):
                    self.receipt.verify_receipt(receipt)

    def test_pass_receipt_rejects_orphaned_desktop_runtime_lock_claim(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            receipt, _evidence, payload = self.seed_compile_receipt(Path(temporary))
            payload["desktopRuntimeLockSha256"] = "0" * 64
            receipt.write_text(json.dumps(payload), encoding="utf-8")
            with self.assertRaisesRegex(
                ValueError,
                "extra=.*desktopRuntimeLockSha256",
            ):
                self.receipt.verify_receipt(receipt)

    def test_pass_receipt_rejects_wrong_values_in_every_authority_group(self) -> None:
        cases = (
            ("schema", "schema", "forged/v1"),
            ("android", "androidWorktreeClean", False),
            ("lock", "lockSha256", "0" * 64),
            ("presentation", "presentationCommit", "0" * 40),
            ("current-ui-receipt", "authorityReceiptSha256", "0" * 64),
            ("v2-package-authority", "packageAuthoritySha256", "0" * 64),
            ("producer-sdk", "producerSdkVersion", "10.0.111"),
            ("consumer-sdk", "sdkVersion", "10.0.103"),
            ("dependency-mode", "dependencyMode", "locked_package_no_siblings"),
            ("package-only", "packageOnly", True),
            ("source-checkout", "sourceCheckoutsPresent", False),
            ("ambient-root", "ambientSiblingRootsAllowed", True),
            (
                "presentation-projects",
                "presentationSourceProjectLibraries",
                ["Chummer.Presentation/1.0.0"],
            ),
            ("serialization", "serializedBuild", False),
            ("phase-results", "phaseResults", {}),
            ("bounds", "executionBounds", {"perCommandSeconds": 0}),
            ("evidence", "evidenceBindings", {}),
            ("artifact", "artifact", {"path": "forged"}),
            ("maui-boundary", "fullMauiBuild", True),
            ("non-claims", "doesNotAssert", []),
        )
        for label, field, value in cases:
            with self.subTest(group=label), tempfile.TemporaryDirectory() as temporary:
                receipt, _evidence, payload = self.seed_compile_receipt(Path(temporary))
                payload[field] = value
                receipt.write_text(json.dumps(payload), encoding="utf-8")
                with self.assertRaises(ValueError):
                    self.receipt.verify_receipt(receipt)

    def test_journal_cross_binds_one_canonical_presentation_root(self) -> None:
        cases = (
            ("missing-intake", "authority-intake"),
            ("duplicate-graph", "package-compile-graph"),
            ("relative-restore", "locked-restore"),
            ("mismatched-build", "serialized-native-compile"),
        )
        for label, phase in cases:
            with self.subTest(case=label), tempfile.TemporaryDirectory() as temporary:
                root = Path(temporary)
                receipt, evidence, payload = self.seed_compile_receipt(root)
                journal = evidence / "command-journal.jsonl"
                rows = [
                    json.loads(line)
                    for line in journal.read_text(encoding="utf-8").splitlines()
                ]
                started = next(
                    row for row in rows
                    if row["phase"] == phase and row["event"] == "started"
                )
                command = started["command"]
                if label == "missing-intake":
                    index = command.index("--presentation-root")
                    del command[index:index + 2]
                elif label == "duplicate-graph":
                    command.extend(("--presentation-root", str(root / "pinned-presentation")))
                elif label == "relative-restore":
                    index = next(
                        index for index, argument in enumerate(command)
                        if argument.startswith("-p:ChummerPresentationRoot=")
                    )
                    command[index] = "-p:ChummerPresentationRoot=../ambient-presentation"
                else:
                    index = next(
                        index for index, argument in enumerate(command)
                        if argument.startswith("-p:ChummerPresentationRoot=")
                    )
                    command[index] = f"-p:ChummerPresentationRoot={root / 'other-presentation'}"
                journal.write_text(
                    "".join(json.dumps(row, sort_keys=True) + "\n" for row in rows),
                    encoding="utf-8",
                )
                self.refresh_evidence_binding(
                    receipt, evidence, payload, "command-journal.jsonl"
                )
                with self.assertRaisesRegex(ValueError, "journal Presentation root"):
                    self.receipt.verify_receipt(receipt)

    def test_pass_receipt_binds_current_clean_android_tree_lock_assets_and_artifact(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            proof = root / "proof"
            proof.mkdir()
            receipt, _evidence, payload = self.seed_compile_receipt(proof)
            android = root / "android"
            lock = android / "tests/Chummer.Android.Native.CompileCheck/packages.lock.json"
            assets = android / "tests/Chummer.Android.Native.CompileCheck/obj/project.assets.json"
            artifact = (
                android
                / "tests/Chummer.Android.Native.CompileCheck/bin/Release/net10.0/"
                / "Chummer.Android.Native.CompileCheck.dll"
            )
            for path in (lock, assets, artifact):
                path.parent.mkdir(parents=True, exist_ok=True)
            lock.write_bytes(
                (REPO / "tests/Chummer.Android.Native.CompileCheck/packages.lock.json").read_bytes()
            )
            assets.write_bytes(b"locked-assets\n")
            artifact.write_bytes(b"compile-only-artifact\n")
            subprocess.run(["git", "init", "-q", str(android)], check=True)
            subprocess.run(["git", "-C", str(android), "add", "."], check=True)
            subprocess.run([
                "git", "-C", str(android), "-c", "user.name=W5 test",
                "-c", "user.email=w5@example.invalid", "commit", "-q", "-m", "fixture",
            ], check=True)
            payload["androidCommit"] = subprocess.run(
                ["git", "-C", str(android), "rev-parse", "HEAD"],
                check=True, capture_output=True, text=True,
            ).stdout.strip()
            payload["androidTree"] = subprocess.run(
                ["git", "-C", str(android), "rev-parse", "HEAD^{tree}"],
                check=True, capture_output=True, text=True,
            ).stdout.strip()
            payload["assetsSha256"] = hashlib.sha256(assets.read_bytes()).hexdigest()
            payload["artifact"]["sha256"] = hashlib.sha256(artifact.read_bytes()).hexdigest()
            payload["artifact"]["sizeBytes"] = artifact.stat().st_size
            receipt.write_text(json.dumps(payload), encoding="utf-8")
            self.assertEqual(
                "pass",
                self.receipt.verify_receipt(receipt, android_root=android)["status"],
            )

            payload["androidTree"] = "f" * 40
            receipt.write_text(json.dumps(payload), encoding="utf-8")
            with self.assertRaisesRegex(ValueError, "clean commit/tree mismatch"):
                self.receipt.verify_receipt(receipt, android_root=android)

    def test_receipt_rejects_extra_and_duplicate_json_keys(self) -> None:
        for claim in ("publicReleaseReady", "googlePlayUploadAuthorized"):
            with self.subTest(claim=claim), tempfile.TemporaryDirectory() as temporary:
                receipt, _evidence, payload = self.seed_compile_receipt(Path(temporary))
                payload[claim] = True
                receipt.write_text(json.dumps(payload), encoding="utf-8")
                with self.assertRaisesRegex(ValueError, "keys mismatch"):
                    self.receipt.verify_receipt(receipt)

        with tempfile.TemporaryDirectory() as temporary:
            receipt, _evidence, _payload = self.seed_compile_receipt(Path(temporary))
            text = receipt.read_text(encoding="utf-8")
            text = text.replace('"status": "pass"', '"status": "pass", "status": "pass"', 1)
            receipt.write_text(text, encoding="utf-8")
            with self.assertRaisesRegex(ValueError, "duplicate JSON key: status"):
                self.receipt.verify_receipt(receipt)

    def test_relevant_evidence_json_rejects_extra_and_duplicate_keys(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            receipt, evidence, payload = self.seed_compile_receipt(Path(temporary))
            graph = json.loads((evidence / "compile-graph.json").read_text(encoding="utf-8"))
            graph["publicReleaseReady"] = True
            (evidence / "compile-graph.json").write_text(json.dumps(graph), encoding="utf-8")
            self.refresh_evidence_binding(
                receipt, evidence, payload, "compile-graph.json", "package-compile-graph"
            )
            with self.assertRaisesRegex(ValueError, "keys mismatch"):
                self.receipt.verify_receipt(receipt)

        with tempfile.TemporaryDirectory() as temporary:
            receipt, evidence, payload = self.seed_compile_receipt(Path(temporary))
            (evidence / "compile-graph.json").write_text(
                '{"status":"pass","status":"pass"}\n', encoding="utf-8"
            )
            self.refresh_evidence_binding(
                receipt, evidence, payload, "compile-graph.json", "package-compile-graph"
            )
            with self.assertRaisesRegex(ValueError, "duplicate JSON key: status"):
                self.receipt.verify_receipt(receipt)

    def test_pass_receipt_rejects_stale_owned_source_inventory_count(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            receipt, evidence, payload = self.seed_compile_receipt(Path(temporary))
            graph_path = evidence / "owned-compile-graph.log"
            graph = json.loads(graph_path.read_text(encoding="utf-8"))
            graph["compiledOwnedSourceCount"] = 212
            graph_path.write_text(json.dumps(graph), encoding="utf-8")
            self.refresh_evidence_binding(
                receipt, evidence, payload, "owned-compile-graph.log", "owned-compile-graph"
            )
            with self.assertRaisesRegex(ValueError, "owned compile graph evidence facts mismatch"):
                self.receipt.verify_receipt(receipt)

    def test_pass_phase_evidence_rejects_warning_and_journal_bound_drift(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            receipt, evidence, payload = self.seed_compile_receipt(Path(temporary))
            (evidence / "build.log").write_text(
                "Build succeeded.\n    1 Warning(s)\n    0 Error(s)\n", encoding="utf-8"
            )
            self.refresh_evidence_binding(
                receipt, evidence, payload, "build.log", "serialized-native-compile"
            )
            with self.assertRaisesRegex(ValueError, "warnings=0/errors=0"):
                self.receipt.verify_receipt(receipt)

        with tempfile.TemporaryDirectory() as temporary:
            receipt, evidence, payload = self.seed_compile_receipt(Path(temporary))
            journal = evidence / "command-journal.jsonl"
            rows = [json.loads(line) for line in journal.read_text(encoding="utf-8").splitlines()]
            rows[2]["timeoutSeconds"] = 901.0
            journal.write_text(
                "".join(json.dumps(row, sort_keys=True) + "\n" for row in rows),
                encoding="utf-8",
            )
            self.refresh_evidence_binding(receipt, evidence, payload, "command-journal.jsonl")
            with self.assertRaisesRegex(ValueError, "timeout bound mismatch"):
                self.receipt.verify_receipt(receipt)

    def test_real_blocked_receipt_shape_cross_binds_all_available_evidence(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            receipt, _evidence, _payload = self.seed_blocked_compile_receipt(
                Path(temporary)
            )
            result = self.receipt.verify_receipt(receipt)
            self.assertEqual("blocked", result["verifiedReceiptStatus"])
            self.assertEqual(7, len(result["evidence"]))

    def test_real_f17_shape_rejects_deleted_successful_authority_binding(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            receipt, evidence, payload = self.seed_blocked_compile_receipt(Path(temporary))
            (evidence / "authority-binding.json").unlink()
            payload["evidence"] = [
                row for row in payload["evidence"]
                if row["path"] != "authority-binding.json"
            ]
            receipt.write_text(json.dumps(payload), encoding="utf-8")
            with self.assertRaisesRegex(ValueError, "authenticated phase prefix"):
                self.receipt.verify_receipt(receipt)

    def test_blocked_receipt_requires_every_causal_phase_side_effect(self) -> None:
        for missing in self.receipt.EXPECTED_EVIDENCE:
            with self.subTest(missing=missing), tempfile.TemporaryDirectory() as temporary:
                receipt, evidence, payload = self.seed_blocked_compile_receipt(
                    Path(temporary)
                )
                (evidence / missing).unlink()
                payload["evidence"] = [
                    row for row in payload["evidence"] if row["path"] != missing
                ]
                if missing == "command-journal.jsonl":
                    payload["journalSha256"] = ""
                    payload["journalSizeBytes"] = 0
                receipt.write_text(json.dumps(payload), encoding="utf-8")
                with self.assertRaises(ValueError):
                    self.receipt.verify_receipt(receipt)

    def test_earlier_failure_rejects_later_phase_evidence(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            receipt, evidence, payload = self.seed_blocked_compile_receipt(Path(temporary))
            payload["failureStage"] = "locked-restore"
            journal = evidence / "command-journal.jsonl"
            rows = [json.loads(line) for line in journal.read_text(encoding="utf-8").splitlines()]
            rows = rows[:4]
            rows[-1]["exitCode"] = 1
            journal.write_text(
                "".join(json.dumps(row, sort_keys=True) + "\n" for row in rows),
                encoding="utf-8",
            )
            self.refresh_blocked_journal_binding(receipt, evidence, payload)
            with self.assertRaisesRegex(ValueError, "authenticated phase prefix"):
                self.receipt.verify_receipt(receipt)

    def test_journal_rejects_reordered_and_duplicate_phase_rows(self) -> None:
        for mutation in ("reordered", "duplicate"):
            with self.subTest(mutation=mutation), tempfile.TemporaryDirectory() as temporary:
                receipt, evidence, payload = self.seed_blocked_compile_receipt(
                    Path(temporary)
                )
                journal = evidence / "command-journal.jsonl"
                rows = [
                    json.loads(line) for line in journal.read_text(encoding="utf-8").splitlines()
                ]
                if mutation == "reordered":
                    rows[:4] = rows[2:4] + rows[:2]
                else:
                    rows.insert(2, copy.deepcopy(rows[1]))
                journal.write_text(
                    "".join(json.dumps(row, sort_keys=True) + "\n" for row in rows),
                    encoding="utf-8",
                )
                self.refresh_blocked_journal_binding(receipt, evidence, payload)
                with self.assertRaises(ValueError):
                    self.receipt.verify_receipt(receipt)

    def test_graph_status_must_match_authenticated_phase_result(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            receipt, evidence, payload = self.seed_compile_receipt(Path(temporary))
            graph_path = evidence / "compile-graph.json"
            graph = json.loads(graph_path.read_text(encoding="utf-8"))
            graph["status"] = "blocked"
            graph_path.write_text(json.dumps(graph), encoding="utf-8")
            self.refresh_evidence_binding(
                receipt, evidence, payload, "compile-graph.json", "package-compile-graph"
            )
            with self.assertRaisesRegex(ValueError, "facts mismatch"):
                self.receipt.verify_receipt(receipt)

        with tempfile.TemporaryDirectory() as temporary:
            receipt, evidence, payload = self.seed_blocked_compile_receipt(Path(temporary))
            payload["failureStage"] = "package-compile-graph"
            journal = evidence / "command-journal.jsonl"
            rows = [json.loads(line) for line in journal.read_text(encoding="utf-8").splitlines()]
            rows = rows[:8]
            rows[-1]["exitCode"] = 1
            journal.write_text(
                "".join(json.dumps(row, sort_keys=True) + "\n" for row in rows),
                encoding="utf-8",
            )
            (evidence / "build.log").unlink()
            payload["evidence"] = [
                row for row in payload["evidence"] if row["path"] != "build.log"
            ]
            self.refresh_blocked_journal_binding(receipt, evidence, payload)
            with self.assertRaisesRegex(ValueError, "evidence claims pass"):
                self.receipt.verify_receipt(receipt)

    def test_blocked_failure_stage_is_derived_from_the_first_failing_phase(self) -> None:
        for forged_stage in (
            "google-play-upload",
            "api36-device-execution",
            "full-maui-build",
            "locked-restore",
            "post-compile-seal",
        ):
            with self.subTest(stage=forged_stage), tempfile.TemporaryDirectory() as temporary:
                receipt, _evidence, payload = self.seed_blocked_compile_receipt(
                    Path(temporary)
                )
                payload["failureStage"] = forged_stage
                receipt.write_text(json.dumps(payload), encoding="utf-8")
                with self.assertRaisesRegex(
                    ValueError,
                    "outside compile-only scope|does not match authenticated journal",
                ):
                    self.receipt.verify_receipt(receipt)

    def test_blocked_journal_rejects_zero_exit_multiple_failure_and_phase_mismatch(self) -> None:
        mutations = (
            "zero-exit", "multiple-failure", "phase-mismatch", "sigkill-string",
        )
        for mutation in mutations:
            with self.subTest(mutation=mutation), tempfile.TemporaryDirectory() as temporary:
                receipt, evidence, payload = self.seed_blocked_compile_receipt(
                    Path(temporary)
                )
                journal = evidence / "command-journal.jsonl"
                rows = [
                    json.loads(line) for line in journal.read_text(encoding="utf-8").splitlines()
                ]
                if mutation == "zero-exit":
                    rows[-1]["exitCode"] = 0
                elif mutation == "multiple-failure":
                    rows[3]["exitCode"] = 2
                elif mutation == "sigkill-string":
                    rows[-1]["termination"]["sigkillSent"] = "false"
                else:
                    rows[-1]["phase"] = "package-compile-graph"
                journal.write_text(
                    "".join(json.dumps(row, sort_keys=True) + "\n" for row in rows),
                    encoding="utf-8",
                )
                self.refresh_blocked_journal_binding(receipt, evidence, payload)
                with self.assertRaises(ValueError):
                    self.receipt.verify_receipt(receipt)

    def test_blocked_timeout_requires_exit_124_boolean_timeout_and_legal_termination(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            receipt, evidence, payload = self.seed_blocked_compile_receipt(Path(temporary))
            journal = evidence / "command-journal.jsonl"
            rows = [json.loads(line) for line in journal.read_text(encoding="utf-8").splitlines()]
            rows[-1].update({
                "exitCode": 124,
                "timedOut": True,
                "termination": {
                    "groupAbsent": True, "sigtermSent": True, "sigkillSent": True,
                },
            })
            journal.write_text(
                "".join(json.dumps(row, sort_keys=True) + "\n" for row in rows),
                encoding="utf-8",
            )
            self.refresh_blocked_journal_binding(receipt, evidence, payload)
            self.assertEqual(
                "blocked", self.receipt.verify_receipt(receipt)["verifiedReceiptStatus"]
            )

        for field, value in (
            ("exitCode", 0),
            ("timedOut", "true"),
            ("sigkillSent", "true"),
        ):
            with self.subTest(field=field), tempfile.TemporaryDirectory() as temporary:
                receipt, evidence, payload = self.seed_blocked_compile_receipt(
                    Path(temporary)
                )
                journal = evidence / "command-journal.jsonl"
                rows = [
                    json.loads(line) for line in journal.read_text(encoding="utf-8").splitlines()
                ]
                rows[-1].update({
                    "exitCode": 124,
                    "timedOut": True,
                    "termination": {
                        "groupAbsent": True, "sigtermSent": True, "sigkillSent": True,
                    },
                })
                if field == "sigkillSent":
                    rows[-1]["termination"][field] = value
                else:
                    rows[-1][field] = value
                journal.write_text(
                    "".join(json.dumps(row, sort_keys=True) + "\n" for row in rows),
                    encoding="utf-8",
                )
                self.refresh_blocked_journal_binding(receipt, evidence, payload)
                with self.assertRaises(ValueError):
                    self.receipt.verify_receipt(receipt)

    def test_total_deadline_block_binds_phase_without_later_output(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            receipt, evidence, payload = self.seed_blocked_compile_receipt(Path(temporary))
            journal = evidence / "command-journal.jsonl"
            rows = [json.loads(line) for line in journal.read_text(encoding="utf-8").splitlines()]
            rows[-2:] = [{
                "contractName": "chummer.android.internal-phone-beta-command-journal/v1",
                "phase": "serialized-native-compile",
                "event": "blocked",
                "reason": "total-deadline-expired",
                "publicationAuthorized": False,
            }]
            journal.write_text(
                "".join(json.dumps(row, sort_keys=True) + "\n" for row in rows),
                encoding="utf-8",
            )
            (evidence / "build.log").unlink()
            payload["evidence"] = [
                row for row in payload["evidence"] if row["path"] != "build.log"
            ]
            self.refresh_blocked_journal_binding(receipt, evidence, payload)
            self.assertEqual(
                "blocked", self.receipt.verify_receipt(receipt)["verifiedReceiptStatus"]
            )

    def test_post_compile_seal_failure_requires_all_five_passes_and_full_evidence(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            receipt, _evidence, payload = self.seed_compile_receipt(Path(temporary))
            payload.update({
                "status": "blocked",
                "failureStage": "post-compile-seal",
                "retryPerformed": False,
                "doesNotAssert": list(self.receipt.BLOCKED_DOES_NOT_ASSERT),
            })
            for field in self.receipt.PASS_ONLY_FIELDS:
                payload.pop(field, None)
            journal = next(
                row for row in payload["evidence"]
                if row["path"] == "command-journal.jsonl"
            )
            payload["journalSha256"] = journal["sha256"]
            payload["journalSizeBytes"] = journal["sizeBytes"]
            receipt.write_text(json.dumps(payload), encoding="utf-8")
            self.assertEqual(
                "blocked", self.receipt.verify_receipt(receipt)["verifiedReceiptStatus"]
            )

    def test_mutated_f17c_blocked_shape_rejects_tamper_missing_and_forged_pass(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            receipt, evidence, payload = self.seed_blocked_compile_receipt(
                Path(temporary)
            )
            (evidence / "build.log").write_text("tampered\n", encoding="utf-8")
            with self.assertRaisesRegex(ValueError, "digest/size mismatch"):
                self.receipt.verify_receipt(receipt)

        with tempfile.TemporaryDirectory() as temporary:
            receipt, evidence, _payload = self.seed_blocked_compile_receipt(
                Path(temporary)
            )
            (evidence / "authority-binding.json").unlink()
            with self.assertRaisesRegex(ValueError, "authority-binding.json is missing"):
                self.receipt.verify_receipt(receipt)

        with tempfile.TemporaryDirectory() as temporary:
            receipt, _evidence, payload = self.seed_blocked_compile_receipt(
                Path(temporary)
            )
            payload["status"] = "pass"
            receipt.write_text(json.dumps(payload), encoding="utf-8")
            with self.assertRaisesRegex(ValueError, "blocked-result claims"):
                self.receipt.verify_receipt(receipt)

    def test_blocked_receipt_rejects_missing_failure_facts_and_success_claims(self) -> None:
        for field, value, message in (
            ("failureStage", None, "requires failureStage"),
            ("retryPerformed", True, "retryPerformed=false"),
            ("publicationAuthorized", True, "publication false"),
            ("artifact", {"sha256": "forged"}, "success-only fields"),
        ):
            with self.subTest(field=field), tempfile.TemporaryDirectory() as temporary:
                receipt, _evidence, payload = self.seed_blocked_compile_receipt(
                    Path(temporary)
                )
                if value is None:
                    payload.pop(field)
                else:
                    payload[field] = value
                receipt.write_text(json.dumps(payload), encoding="utf-8")
                with self.assertRaisesRegex(ValueError, message):
                    self.receipt.verify_receipt(receipt)

        for claim in ("publicReleaseReady", "googlePlayUploadAuthorized"):
            with self.subTest(claim=claim), tempfile.TemporaryDirectory() as temporary:
                receipt, _evidence, payload = self.seed_blocked_compile_receipt(
                    Path(temporary)
                )
                payload[claim] = True
                receipt.write_text(json.dumps(payload), encoding="utf-8")
                with self.assertRaisesRegex(ValueError, "keys mismatch"):
                    self.receipt.verify_receipt(receipt)

    def test_build_failure_persists_a_verifiable_blocked_receipt(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            presentation = root / "presentation"
            feed = root / "feed"
            packages = root / "packages"
            for directory in (presentation, feed, packages):
                directory.mkdir()
            authority_receipt = root / "authority.json"
            authority_journal = root / "authority.journal.json"
            authority_receipt.write_text("{}\n", encoding="utf-8")
            authority_journal.write_text("{}\n", encoding="utf-8")
            fake_dotnet = root / "dotnet"
            fake_dotnet.write_text("#!/usr/bin/env bash\nprintf '0.0.0\\n'\n", encoding="utf-8")
            fake_dotnet.chmod(0o700)
            build_receipt = root / "blocked.json"
            environment = os.environ.copy()
            for forbidden in (
                "CHUMMER_CORE_ENGINE_ROOT", "CHUMMER_RUN_SERVICES_ROOT",
                "CHUMMER_HUB_REGISTRY_ROOT", "CHUMMER_UI_KIT_ROOT",
                "CHUMMER_MEDIA_FACTORY_ROOT",
            ):
                environment.pop(forbidden, None)
            environment.update({
                "CHUMMER_DOTNET": str(fake_dotnet),
                "CHUMMER_PRESENTATION_ROOT": str(presentation),
                "CHUMMER_INTERNAL_PHONE_BETA_PACKAGE_FEED": str(feed),
                "CHUMMER_INTERNAL_PHONE_BETA_NUGET_PACKAGES": str(packages),
                "CHUMMER_CURRENT_UI_PACKAGE_AUTHORITY_RECEIPT": str(authority_receipt),
                "CHUMMER_INTERNAL_PHONE_BETA_BUILD_RECEIPT": str(build_receipt),
            })
            completed = subprocess.run(
                [str(BUILD)], env=environment, check=False,
                capture_output=True, text=True, timeout=10,
            )
            self.assertEqual(2, completed.returncode, completed.stderr)
            payload = json.loads(build_receipt.read_text(encoding="utf-8"))
            self.assertEqual("blocked", payload["status"])
            self.assertEqual("preflight", payload["failureStage"])
            result = self.receipt.verify_receipt(build_receipt)
            self.assertEqual("blocked", result["verifiedReceiptStatus"])
            self.assertTrue(Path(f"{build_receipt}.evidence").is_dir())

    def test_compile_receipt_rejects_tamper_missing_and_inventory_deletion(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            receipt, evidence, payload = self.seed_compile_receipt(root)
            (evidence / "build.log").write_text("tampered\n", encoding="utf-8")
            with self.assertRaisesRegex(ValueError, "digest/size mismatch"):
                self.receipt.verify_receipt(receipt)

            receipt.unlink()
            for child in evidence.iterdir():
                child.unlink()
            evidence.rmdir()
            receipt, evidence, payload = self.seed_compile_receipt(root)
            (evidence / "restore.log").unlink()
            with self.assertRaisesRegex(ValueError, "evidence restore.log is missing"):
                self.receipt.verify_receipt(receipt)

            (evidence / "restore.log").write_text("replacement\n", encoding="utf-8")
            payload["evidence"] = [
                row for row in payload["evidence"] if row["path"] != "restore.log"
            ]
            receipt.write_text(json.dumps(payload), encoding="utf-8")
            with self.assertRaisesRegex(ValueError, "missing or extra files"):
                self.receipt.verify_receipt(receipt)

    def seed_compile_graph(self, root: Path):
        android = root / "android"
        presentation = root / "presentation"
        project = android / "tests/Chummer.Android.Native.CompileCheck/Chummer.Android.Native.CompileCheck.csproj"
        project.parent.mkdir(parents=True)
        project.write_text("<Project />\n", encoding="utf-8")
        for relative in (
            "Chummer.Presentation/Chummer.Presentation.csproj",
            "Chummer.Desktop.Runtime/Chummer.Desktop.Runtime.csproj",
        ):
            path = presentation / relative
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_text("<Project />\n", encoding="utf-8")
        expected_versions = self.authority.EXPECTED_COMPILE_PACKAGES
        package_ids = self.graph.EXPECTED_CHUMMER_IDS
        libraries = {
            f"{package_id}/{expected_versions[package_id]}": {"type": "package"}
            for package_id in package_ids
        }
        libraries.update({
            "Chummer.Desktop.Runtime/1.0.0": {"type": "project"},
            "Chummer.Presentation/1.0.0": {"type": "project"},
        })
        obj = project.parent / "obj"
        obj.mkdir()
        assets = {"libraries": libraries}
        (obj / "project.assets.json").write_text(json.dumps(assets), encoding="utf-8")
        allowed = [
            project,
            presentation / "Chummer.Presentation/Chummer.Presentation.csproj",
            presentation / "Chummer.Desktop.Runtime/Chummer.Desktop.Runtime.csproj",
        ]
        dgspec = {"projects": {str(path): {"frameworks": {"net10.0": {"projectReferences": {}}}} for path in allowed}}
        (obj / "fixture.nuget.dgspec.json").write_text(json.dumps(dgspec), encoding="utf-8")
        lock = {
            "version": 1,
            "dependencies": {
                "net10.0": {
                    package_id: {
                        "type": "Transitive",
                        "resolved": expected_versions[package_id],
                        "contentHash": "fixture",
                    }
                    for package_id in package_ids
                }
            },
        }
        (project.parent / "packages.lock.json").write_text(json.dumps(lock), encoding="utf-8")
        return android, presentation, project, assets, dgspec

    def test_compile_graph_rejects_sibling_project_and_missing_package(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            android, presentation, project, assets, dgspec = self.seed_compile_graph(root)
            payload = self.graph.validate_compile_graph(project, android, presentation)
            self.assertEqual("pass", payload["status"])
            self.assertEqual(
                "locked_package_closure_with_pinned_presentation_source",
                payload["dependencyMode"],
            )
            self.assertFalse(payload["packageOnly"])
            self.assertTrue(payload["sourceCheckoutsPresent"])
            self.assertFalse(payload["ambientSiblingRootsAllowed"])
            self.assertEqual(
                ["Chummer.Desktop.Runtime/1.0.0", "Chummer.Presentation/1.0.0"],
                payload["presentationSourceProjectLibraries"],
            )
            extra = root / "chummer-core-engine/Chummer.Contracts/Chummer.Contracts.csproj"
            extra.parent.mkdir(parents=True)
            extra.write_text("<Project />\n", encoding="utf-8")
            tampered = copy.deepcopy(dgspec)
            tampered["projects"][str(extra)] = {"frameworks": {"net10.0": {"projectReferences": {}}}}
            (project.parent / "obj/fixture.nuget.dgspec.json").write_text(json.dumps(tampered), encoding="utf-8")
            with self.assertRaisesRegex(ValueError, "sibling project reference"):
                self.graph.validate_compile_graph(project, android, presentation)
            (project.parent / "obj/fixture.nuget.dgspec.json").write_text(json.dumps(dgspec), encoding="utf-8")
            missing = copy.deepcopy(assets)
            key = next(key for key in missing["libraries"] if key.startswith("Chummer.Play.Contracts/"))
            del missing["libraries"][key]
            (project.parent / "obj/project.assets.json").write_text(json.dumps(missing), encoding="utf-8")
            with self.assertRaisesRegex(ValueError, "exact current compile closure"):
                self.graph.validate_compile_graph(project, android, presentation)


if __name__ == "__main__":
    unittest.main()
