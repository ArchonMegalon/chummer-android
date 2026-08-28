from __future__ import annotations

import copy
import importlib.util
import json
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
            completed = subprocess.run(
                [
                    sys.executable, str(RUNNER), "--journal", str(journal),
                    "--output", str(success), "--phase", "success",
                    "--timeout-seconds", "2", "--deadline-epoch", str(time.time() + 10),
                    "--", sys.executable, "-c", "print('green')",
                ],
                check=False, capture_output=True, text=True,
            )
            self.assertEqual(0, completed.returncode, completed.stderr)
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
