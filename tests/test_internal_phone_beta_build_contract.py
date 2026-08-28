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
        self.assertNotIn('>"$restore_log"', text)
        self.assertNotIn('>"$build_log"', text)
        self.assertIn("persist_evidence", text)
        self.assertIn("verify_internal_phone_beta_compile_receipt.py", text)

    def test_source_sibling_inputs_are_rejected_not_forwarded(self) -> None:
        text = BUILD.read_text(encoding="utf-8")
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
        self.assertIn("on: []", text)
        self.assertIn("if: ${{ false }}", text)
        self.assertIn("a8a317aff534dc5fd47f2db1bc39466799021990", text)
        self.assertIn("Chummer.Desktop.Runtime", text)
        self.assertIn("Chummer.Presentation", text)
        self.assertIn("dotnet-version: 10.0.111", text)
        self.assertIn("build-internal-phone-beta-native-compile.sh", text)
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
            "64454d5420e2a5430a046d392c6eea2ca41d9105c1667f2b8a66e1f61064cccc",
            self.authority.sha256(lock),
        )

    def test_compile_check_pass_cannot_satisfy_api36_or_play_beta_gates(self) -> None:
        compile_contract = "chummer.android.internal-phone-beta-native-compile/v1"
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
        rows = []
        digests = {}
        sizes = {}
        for index, name in enumerate(self.receipt.EXPECTED_EVIDENCE):
            path = evidence / name
            path.write_text(f"evidence-{index}\n", encoding="utf-8")
            digest = hashlib.sha256(path.read_bytes()).hexdigest()
            size = path.stat().st_size
            rows.append({"path": name, "sha256": digest, "sizeBytes": size})
            digests[name] = digest
            sizes[name] = size
        payload = {
            "contractName": self.receipt.CONTRACT,
            "status": "pass",
            "authorityClass": self.receipt.AUTHORITY_CLASS,
            "publicationAuthorized": False,
            "proofScope": self.receipt.PROOF_SCOPE,
            "evidenceDirectory": str(evidence),
            "evidence": rows,
            "authorityBindingSha256": digests["authority-binding.json"],
            "restoreOutputSha256": digests["restore.log"],
            "compileGraphSha256": digests["compile-graph.json"],
            "buildOutputSha256": digests["build.log"],
            "journalSha256": digests["command-journal.jsonl"],
            "journalSizeBytes": sizes["command-journal.jsonl"],
        }
        receipt.write_text(json.dumps(payload), encoding="utf-8")
        return receipt, evidence, payload

    def seed_blocked_compile_receipt(
        self,
        root: Path,
    ) -> tuple[Path, Path, dict[str, object]]:
        receipt, evidence, payload = self.seed_compile_receipt(root)
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

    def test_compile_receipt_verifies_persisted_evidence_bytes(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            receipt, _evidence, _payload = self.seed_compile_receipt(Path(temporary))
            result = self.receipt.verify_receipt(receipt)
            self.assertEqual("pass", result["status"])
            self.assertEqual(7, len(result["evidence"]))

    def test_real_blocked_receipt_shape_cross_binds_all_available_evidence(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            receipt, _evidence, _payload = self.seed_blocked_compile_receipt(
                Path(temporary)
            )
            result = self.receipt.verify_receipt(receipt)
            self.assertEqual("blocked", result["verifiedReceiptStatus"])
            self.assertEqual(7, len(result["evidence"]))

    def test_blocked_receipt_rejects_tamper_missing_and_forged_status(self) -> None:
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
                "CHUMMER_W41_PACKAGE_AUTHORITY_RECEIPT": str(authority_receipt),
                "CHUMMER_W41_PACKAGE_AUTHORITY_JOURNAL": str(authority_journal),
                "CHUMMER_INTERNAL_PHONE_BETA_BUILD_RECEIPT": str(build_receipt),
            })
            completed = subprocess.run(
                [str(BUILD)], env=environment, check=False,
                capture_output=True, text=True, timeout=10,
            )
            self.assertEqual(2, completed.returncode, completed.stderr)
            payload = json.loads(build_receipt.read_text(encoding="utf-8"))
            self.assertEqual("blocked", payload["status"])
            self.assertEqual("dotnet-sdk-not-10.0.111", payload["failureStage"])
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
        expected_rows = {row[0]: row for row in self.authority.EXPECTED_PACKAGES}
        package_ids = self.graph.EXPECTED_CHUMMER_IDS
        libraries = {
            f"{package_id}/{expected_rows[package_id][1]}": {"type": "package"}
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
                        "resolved": expected_rows[package_id][1],
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
            with self.assertRaisesRegex(ValueError, "exact fourteen-package"):
                self.graph.validate_compile_graph(project, android, presentation)


if __name__ == "__main__":
    unittest.main()
