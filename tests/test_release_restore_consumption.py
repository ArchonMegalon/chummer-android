from __future__ import annotations

import base64
import copy
import hashlib
import importlib.util
import json
import os
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path


REPO = Path(__file__).resolve().parents[1]
SCRIPT = REPO / "scripts" / "seal_release_restore_consumption.py"


def load_module():
    spec = importlib.util.spec_from_file_location("seal_release_restore_consumption", SCRIPT)
    assert spec is not None and spec.loader is not None
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


PACKAGE_IDS = (
    "Chummer.Engine.Contracts", "Chummer.Application", "Chummer.Infrastructure",
    "Chummer.Rulesets.Hosting", "Chummer.Rulesets.Sr4", "Chummer.Rulesets.Sr5",
    "Chummer.Rulesets.Sr6", "Chummer.Campaign.Contracts", "Chummer.Play.Contracts",
    "Chummer.Run.Contracts", "Chummer.Hub.Registry.Contracts", "Chummer.Ui.Kit",
)


def private_directory(path: Path) -> Path:
    # mkdir(parents=True) applies mode only to the leaf. Every cache ancestor
    # must be private even on hosted runners whose ambient umask is 0022.
    if not path.parent.exists():
        private_directory(path.parent)
    path.mkdir(mode=0o700, exist_ok=True)
    path.chmod(0o700)
    return path


def private_file(path: Path, data: bytes) -> Path:
    private_directory(path.parent)
    path.write_bytes(data)
    path.chmod(0o600)
    return path


def fixture(root: Path):
    module = load_module()
    workspace = private_directory(root / "workspace")
    input_root = private_directory(root / "release-input")
    feed = private_directory(root / "retained-feed")
    selected_feed = private_directory(input_root / "selected-feed")
    packages = private_directory(input_root / "packages")
    routed_locks = private_directory(input_root / "project-locks")
    for name in (
        "Chummer.Android.packages.lock.json",
        "Chummer.Desktop.Runtime.packages.lock.json",
        "Chummer.Presentation.packages.lock.json",
    ):
        private_file(routed_locks / name, f"sealed:{name}".encode())
    intermediate = private_directory(input_root / "intermediate" / "Chummer.Android")
    primary_intermediate = intermediate
    android_project = private_file(
        workspace / "chummer-android/src/Chummer.Android/Chummer.Android.csproj",
        b"<Project />",
    )
    desktop_project = private_file(
        workspace / "chummer-presentation/Chummer.Desktop.Runtime/Chummer.Desktop.Runtime.csproj",
        b"<Project />",
    )
    presentation_project = private_file(
        workspace / "chummer-presentation/Chummer.Presentation/Chummer.Presentation.csproj",
        b"<Project />",
    )
    output = workspace / "chummer-android/src/Chummer.Android/bin"
    core = []
    owners = []
    for index, package_id in enumerate(PACKAGE_IDS):
        version = "1.2.3"
        nupkg = f"sealed:{package_id}".encode()
        private_file(feed / f"{package_id}.{version}.nupkg", nupkg)
        row = {
            "package_id": package_id,
            "version": version,
            "sha256": hashlib.sha256(nupkg).hexdigest(),
        }
        (core if index < 7 else owners).append(row)
        package_dir = private_directory(packages / package_id.lower() / version)
        content_hash = base64.b64encode(hashlib.sha512(nupkg).digest()).decode()
        private_file(package_dir / f"{package_id.lower()}.{version}.nupkg.sha512", content_hash.encode())
        private_file(package_dir / "lib" / "payload.dll", nupkg)
    authority = {
        "contractName": module.AUTHORITY_CONTRACT,
        "packagePins": core,
        "ownerPackagePins": owners,
    }
    authority_path = private_file(
        root / "authority.json", json.dumps(authority).encode(),
    )
    module.snapshot_feed(authority_path, feed, selected_feed)
    libraries = {}
    target = {}
    for package_id in PACKAGE_IDS:
        version = "1.2.3"
        nupkg = f"sealed:{package_id}".encode()
        content_hash = base64.b64encode(hashlib.sha512(nupkg).digest()).decode()
        libraries[f"{package_id}/{version}"] = {
            "type": "package",
            "sha512": content_hash,
            "path": f"{package_id.lower()}/{version}",
        }
        target[f"{package_id}/{version}"] = {
            "dependencies": (
                {"Chummer.Play.Contracts": version}
                if package_id == "Chummer.Run.Contracts" else {}
            ),
        }
    for project_id in ("Chummer.Desktop.Runtime", "Chummer.Presentation"):
        absolute_project = (
            desktop_project if project_id == "Chummer.Desktop.Runtime" else presentation_project
        )
        project_path = os.path.relpath(absolute_project, android_project.parent)
        libraries[f"{project_id}/1.0.0"] = {
            "type": "project",
            "path": project_path,
            "msbuildProject": project_path,
        }
        target[f"{project_id}/1.0.0"] = {"type": "project"}
    assets = {
        "packageFolders": {os.fspath(packages) + os.sep: {}},
        "libraries": libraries,
        "targets": {
            "net10.0-android36.0": target,
            "net10.0-android36.0/android-arm64": copy.deepcopy(target),
        },
        "project": {"restore": {"projectPath": os.fspath(android_project)}},
    }
    private_file(primary_intermediate / "project.assets.json", json.dumps(assets).encode())
    def dgspec_project(path: Path, framework: str, references: tuple[Path, ...]):
        return {
            "restore": {
                "projectPath": os.fspath(path),
                "projectUniqueName": os.fspath(path),
                "frameworks": {
                    framework: {
                        "projectReferences": {
                            os.fspath(reference): {"projectPath": os.fspath(reference)}
                            for reference in references
                        }
                    }
                },
            }
        }
    dgspec = {
        "projects": {
            os.fspath(android_project): dgspec_project(
                android_project, "net10.0-android36.0",
                (desktop_project, presentation_project),
            ),
            os.fspath(desktop_project): dgspec_project(
                desktop_project, "net10.0", (presentation_project,),
            ),
            os.fspath(presentation_project): dgspec_project(
                presentation_project, "net10.0", (),
            ),
        }
    }
    private_file(
        primary_intermediate / "Chummer.Android.csproj.nuget.dgspec.json",
        json.dumps(dgspec).encode(),
    )
    private_file(primary_intermediate / "Chummer.Android.csproj.nuget.g.props", b"<Project />")
    for project_id, path in (("Chummer.Desktop.Runtime", desktop_project), ("Chummer.Presentation", presentation_project)):
        project_intermediate = private_directory(intermediate.parent / project_id)
        private_file(project_intermediate / "project.assets.json", json.dumps({
            "project": {"restore": {"projectPath": os.fspath(path)}}
        }).encode())
        private_file(project_intermediate / f"{project_id}.csproj.nuget.dgspec.json", json.dumps({
            "projects": {os.fspath(path): dgspec["projects"][os.fspath(path)]}
        }).encode())
        private_file(project_intermediate / f"{project_id}.csproj.nuget.g.props", b"<Project />")
    lock = private_file(root / "packages.lock.json", b'{"version":2}')
    return (
        module, workspace, input_root, authority_path, feed, selected_feed,
        packages, routed_locks, intermediate, output, lock,
    )


def sealed_fixture(root: Path):
    values = fixture(root)
    module, workspace, input_root, authority, _feed, selected, packages, routed_locks, intermediate, _output, lock = values
    arguments = dict(
        input_root=input_root, workspace_root=workspace, authority_path=authority,
        owner_feed=selected, packages_root=packages, routed_lock_root=routed_locks,
        project_lock=lock, intermediate_root=intermediate.parent,
    )
    payload = module.materialize_payload(**arguments)
    verify_arguments = {key: value for key, value in arguments.items() if key not in {"input_root", "authority_path"}}
    return values, payload, arguments, verify_arguments


class ReleaseRestoreConsumptionTests(unittest.TestCase):
    def test_v2_external_layout_has_no_ambient_workspace_restore(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            values, payload, _arguments, verify = sealed_fixture(Path(temporary))
            module, workspace, _input, _authority, _feed, _selected, _packages, _locks, intermediate, _output, _lock = values
            self.assertEqual("chummer.android.release-restore-consumption/v2", payload["contractName"])
            self.assertEqual([], module._workspace_build_state(workspace)[0])
            self.assertEqual(os.fspath(intermediate.parent), payload["restoreIntermediates"]["root"])
            self.assertEqual(9, len(payload["restoreIntermediates"]["files"]))
            module.verify_post_publish(payload, **verify, phase="pre-publish")
            module.verify_post_publish(payload, **verify, phase="post-publish")

    def test_wrong_swapped_symlink_and_unsafe_intermediate_roots_fail(self) -> None:
        for mutation in ("wrong", "swapped", "symlink", "public", "outside", "equal-input", "overlap-packages", "nested-symlink"):
            with self.subTest(mutation=mutation), tempfile.TemporaryDirectory() as temporary:
                root = Path(temporary)
                values, payload, arguments, verify = sealed_fixture(root)
                module, _workspace, input_root, _authority, _feed, _selected, packages, _locks, intermediate, _output, _lock = values
                if mutation == "wrong":
                    verify["intermediate_root"] = private_directory(input_root / "wrong")
                elif mutation == "swapped":
                    original = intermediate.parent
                    original.rename(input_root / "retired-intermediate")
                    private_directory(original)
                elif mutation == "symlink":
                    link = input_root / "linked-intermediate"
                    link.symlink_to(intermediate.parent, target_is_directory=True)
                    verify["intermediate_root"] = link
                elif mutation == "public":
                    intermediate.parent.chmod(0o755)
                elif mutation == "outside":
                    verify["intermediate_root"] = private_directory(root / "outside")
                elif mutation == "equal-input":
                    verify["intermediate_root"] = input_root
                elif mutation == "overlap-packages":
                    verify["intermediate_root"] = packages
                else:
                    (intermediate / "escape").symlink_to(packages, target_is_directory=True)
                with self.assertRaises(ValueError):
                    module.verify_post_publish(payload, **verify, phase="post-publish")
                # Root scope checks apply at initial capture as well as replay.
                if mutation in {"public", "outside", "equal-input", "overlap-packages", "symlink", "nested-symlink"}:
                    arguments["intermediate_root"] = verify["intermediate_root"]
                    with self.assertRaises(ValueError):
                        module.materialize_payload(**arguments)

    def test_root_overlap_in_both_directions_rejected_before_capture(self) -> None:
        for nested in (False, True):
            with self.subTest(nested=nested), tempfile.TemporaryDirectory() as temporary:
                values, _payload, arguments, _verify = sealed_fixture(Path(temporary))
                module, _workspace, _input, _authority, _feed, _selected, packages, _locks, intermediate, _output, _lock = values
                if nested:
                    arguments["intermediate_root"] = private_directory(packages / "restore")
                else:
                    arguments["packages_root"] = private_directory(intermediate.parent / "packages")
                with self.assertRaisesRegex(ValueError, "overlap"):
                    module.materialize_payload(**arguments)

    def test_input_workspace_overlap_rejected_at_capture_context_and_each_phase(self) -> None:
        for layout in ("ancestor", "ancestor-with-workspace-intermediates", "equal", "descendant"):
            with self.subTest(layout=layout), tempfile.TemporaryDirectory() as temporary:
                root = Path(temporary)
                values, payload, arguments, verify = sealed_fixture(root)
                module, workspace, _input, _authority, _feed, _selected, _packages, _locks, intermediate, _output, _lock = values
                input_root = root
                if layout == "ancestor-with-workspace-intermediates":
                    # This is neither bin nor obj: the independent workspace
                    # output scan cannot substitute for root disjointness.
                    in_workspace = workspace / "restore-intermediates"
                    intermediate.parent.rename(in_workspace)
                    arguments["intermediate_root"] = in_workspace
                    verify["intermediate_root"] = in_workspace
                    payload["restoreIntermediates"]["root"] = os.fspath(in_workspace)
                elif layout == "equal":
                    input_root = workspace
                elif layout == "descendant":
                    input_root = private_directory(workspace / "release-input")
                arguments["input_root"] = input_root
                payload["inputRoot"] = os.fspath(input_root)
                context = {key: value for key, value in arguments.items() if key != "project_lock"}
                error = "outside the coherent workspace|must be disjoint"
                with self.subTest(entrypoint="materialize"), self.assertRaisesRegex(ValueError, error):
                    module.materialize_payload(**arguments)
                with self.subTest(entrypoint="context"), self.assertRaisesRegex(ValueError, error):
                    module.verify_context(payload, **context)
                for phase in ("pre-publish", "post-publish"):
                    with self.subTest(entrypoint=phase), self.assertRaisesRegex(ValueError, error):
                        module.verify_post_publish(payload, **verify, phase=phase)

    def test_changed_removed_replaced_or_linked_restore_bytes_fail_in_both_phases(self) -> None:
        for phase in ("pre-publish", "post-publish"):
            for mutation in ("changed", "removed", "replaced", "symlink", "hardlink"):
                with self.subTest(phase=phase, mutation=mutation), tempfile.TemporaryDirectory() as temporary:
                    values, payload, _arguments, verify = sealed_fixture(Path(temporary))
                    module, _workspace, _input, _authority, _feed, _selected, _packages, _locks, intermediate, _output, _lock = values
                    path = intermediate / "Chummer.Android.csproj.nuget.g.props"
                    if mutation == "changed":
                        private_file(path, b"modified restore byte")
                    else:
                        path.unlink()
                        if mutation == "replaced":
                            private_file(path, b"replacement restore byte")
                        elif mutation == "symlink":
                            path.symlink_to(intermediate / "project.assets.json")
                        elif mutation == "hardlink":
                            os.link(intermediate / "project.assets.json", path)
                    with self.assertRaises(ValueError):
                        module.verify_post_publish(payload, **verify, phase=phase)

    def test_pre_rejects_new_output_and_post_allows_new_compiler_and_workspace_bin_outputs(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            values, payload, _arguments, verify = sealed_fixture(Path(temporary))
            module, workspace, _input, _authority, _feed, _selected, _packages, _locks, intermediate, output, _lock = values
            private_file(intermediate / "Release/net10.0-android36.0/compiled.dll", b"compiler output")
            with self.assertRaisesRegex(ValueError, "changed before publish"):
                module.verify_post_publish(payload, **verify, phase="pre-publish")
            module.verify_post_publish(payload, **verify, phase="post-publish")
            private_file(output / "Release/net10.0-android36.0/app.dll", b"app output")
            private_file(workspace / "chummer-presentation/Chummer.Presentation/bin/Release/net10.0/presentation.dll", b"UI output")
            module.verify_post_publish(payload, **verify, phase="post-publish")
            with self.assertRaisesRegex(ValueError, "stale bin/obj"):
                module.verify_post_publish(payload, **verify, phase="pre-publish")

    def test_post_rejects_duplicate_or_unsealed_restore_metadata_and_wrong_project_outputs(self) -> None:
        for relative in (
            "Chummer.Android/Release/project.assets.json",
            "Chummer.Android/other.csproj.nuget.dgspec.json",
            "Chummer.Android/Release/Chummer.Android.csproj.nuget.g.props",
            "Chummer.Android/project.nuget.cache",
            "Unexpected.Project/Release/payload.dll",
        ):
            with self.subTest(relative=relative), tempfile.TemporaryDirectory() as temporary:
                values, payload, _arguments, verify = sealed_fixture(Path(temporary))
                module, _workspace, _input, _authority, _feed, _selected, _packages, _locks, intermediate, _output, _lock = values
                private_file(intermediate.parent / relative, b"{}")
                with self.assertRaises(ValueError):
                    module.verify_post_publish(payload, **verify, phase="post-publish")

    def test_post_rejects_ambient_obj_and_unrelated_workspace_bin(self) -> None:
        for relative in ("chummer-android/src/Chummer.Android/obj/project.assets.json", "unrelated/bin/payload.dll"):
            with self.subTest(relative=relative), tempfile.TemporaryDirectory() as temporary:
                values, payload, _arguments, verify = sealed_fixture(Path(temporary))
                module, workspace, *_rest = values
                private_file(workspace / relative, b"not an admitted output")
                with self.assertRaisesRegex(ValueError, "exact source-project bin roots"):
                    module.verify_post_publish(payload, **verify, phase="post-publish")

    def test_materialize_rejects_duplicate_assets_and_malformed_primary_json(self) -> None:
        for mutation in ("duplicate-assets", "duplicate-dgspec", "malformed", "duplicate-json-key"):
            with self.subTest(mutation=mutation), tempfile.TemporaryDirectory() as temporary:
                values, _payload, arguments, _verify = sealed_fixture(Path(temporary))
                module, _workspace, _input, _authority, _feed, _selected, _packages, _locks, intermediate, _output, _lock = values
                if mutation == "duplicate-assets":
                    private_file(intermediate / "extra/project.assets.json", b"{}")
                elif mutation == "duplicate-dgspec":
                    private_file(intermediate / "extra/Chummer.Android.csproj.nuget.dgspec.json", b"{}")
                else:
                    private_file(intermediate / "project.assets.json", b"{" if mutation == "malformed" else b'{"project":{},"project":{}}')
                with self.assertRaises(ValueError):
                    module.materialize_payload(**arguments)

    def test_v1_missing_root_malformed_rows_and_manifest_digest_drift_fail(self) -> None:
        for mutation in ("v1", "missing-root", "wrong-root", "bad-inventory", "escape-row", "missing-primary"):
            with self.subTest(mutation=mutation), tempfile.TemporaryDirectory() as temporary:
                values, payload, _arguments, verify = sealed_fixture(Path(temporary))
                module = values[0]
                if mutation == "v1":
                    payload["contractName"] = "chummer.android.release-restore-consumption/v1"
                elif mutation == "missing-root":
                    del payload["restoreIntermediates"]
                elif mutation == "wrong-root":
                    payload["restoreIntermediates"]["root"] = "/unbound"
                elif mutation == "bad-inventory":
                    payload["restoreIntermediates"]["inventorySha256"] = "0" * 64
                elif mutation == "escape-row":
                    payload["restoreIntermediates"]["files"][0]["path"] = "../outside"
                else:
                    payload["restoreIntermediates"]["files"] = []
                    payload["restoreIntermediates"]["inventorySha256"] = module._inventory_digest([])
                with self.assertRaises(ValueError):
                    module.verify_post_publish(payload, **verify, phase="post-publish")

    def test_verify_cli_requires_explicit_phase_and_intermediate_root(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            values, payload, arguments, _verify = sealed_fixture(Path(temporary))
            input_root = values[2]
            manifest = private_file(input_root / "restore-consumption.json", json.dumps(payload).encode())
            names = {"authority_path": "authority", "project_lock": "project-lock"}
            command = [sys.executable, os.fspath(SCRIPT), "verify", "--manifest", os.fspath(manifest), "--phase", "pre-publish"]
            for key, value in arguments.items():
                command.extend(("--" + names.get(key, key.replace("_", "-")), os.fspath(value)))
            completed = subprocess.run(command, capture_output=True, text=True, check=False)
            self.assertEqual(0, completed.returncode, completed.stdout + completed.stderr)
            for missing in ("--phase", "--intermediate-root"):
                with self.subTest(missing=missing):
                    reduced = command.copy()
                    index = reduced.index(missing)
                    del reduced[index:index + 2]
                    rejected = subprocess.run(reduced, capture_output=True, text=True, check=False)
                    self.assertNotEqual(0, rejected.returncode)
                    self.assertIn(missing, rejected.stderr)

    def test_private_fixture_parents_are_safe_under_hosted_umask(self) -> None:
        previous_umask = os.umask(0o022)
        try:
            with tempfile.TemporaryDirectory() as temporary:
                root = Path(temporary)
                private_directory(root / "packages" / "example" / "1.0.0")
                for path in root.rglob("*"):
                    self.assertEqual(0o700, path.stat().st_mode & 0o777, str(path))
            self.test_manifest_binds_assets_dgspec_lock_cache_and_complete_closure()
        finally:
            os.umask(previous_umask)

    def test_snapshot_binds_exact_twelve_package_closure_and_engine(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            (
                module, _workspace, _input_root, authority, feed, selected,
                _packages, _routed_locks, intermediate, _output, _lock,
            ) = fixture(Path(temporary))
            second = private_directory(Path(temporary) / "second-feed")
            result = module.snapshot_feed(authority, feed, second)
            self.assertEqual(12, result["selectedPackageCount"])
            self.assertIn("Chummer.Engine.Contracts", {
                row["packageId"] for row in result["selectedPackages"]
            })
            self.assertEqual(12, len(result["inventory"]))
            self.assertFalse(result["publicationAuthorized"])
            self.assertTrue(all((second / row["path"]).stat().st_mode & 0o077 == 0 for row in result["inventory"]))

    def test_snapshot_rejects_missing_engine_tamper_symlink_and_nonempty_destination(self) -> None:
        for mutation, message in (
            ("missing-engine", "exact twelve-package"),
            ("tamper", "digest drifted"),
            ("symlink", "without following links"),
            ("extra", "must be empty"),
        ):
            with self.subTest(mutation=mutation), tempfile.TemporaryDirectory() as temporary:
                (
                    module, _workspace, _input_root, authority, feed, _selected,
                    _packages, _routed_locks, intermediate, _output, _lock,
                ) = fixture(Path(temporary))
                destination = private_directory(Path(temporary) / "snapshot")
                if mutation == "missing-engine":
                    payload = json.loads(authority.read_text())
                    payload["packagePins"] = [
                        row for row in payload["packagePins"]
                        if row["package_id"] != "Chummer.Engine.Contracts"
                    ]
                    private_file(authority, json.dumps(payload).encode())
                elif mutation == "tamper":
                    private_file(feed / "Chummer.Engine.Contracts.1.2.3.nupkg", b"tampered")
                elif mutation == "symlink":
                    target = feed / "Chummer.Engine.Contracts.1.2.3.nupkg"
                    target.unlink()
                    target.symlink_to(feed / "Chummer.Application.1.2.3.nupkg")
                else:
                    private_file(destination / "unexpected", b"x")
                with self.assertRaisesRegex(ValueError, message):
                    module.snapshot_feed(authority, feed, destination)

    def test_manifest_binds_assets_dgspec_lock_cache_and_complete_closure(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            values = fixture(Path(temporary))
            module, workspace, input_root, authority, _feed, selected, packages, routed_locks, intermediate, output, lock = values
            payload = module.materialize_payload(
                input_root=input_root, workspace_root=workspace, authority_path=authority,
                owner_feed=selected, packages_root=packages,
                routed_lock_root=routed_locks, project_lock=lock,
                intermediate_root=intermediate.parent,
            )
            self.assertEqual(module.CONTRACT, payload["contractName"])
            self.assertFalse(payload["publicationAuthorized"])
            self.assertTrue(payload["projectAssets"]["path"].endswith("Chummer.Android/project.assets.json"))
            self.assertTrue(payload["dependencyGraphSpec"]["path"].endswith(".nuget.dgspec.json"))
            self.assertEqual(12, len(payload["chummerClosure"]))
            self.assertIn("Chummer.Engine.Contracts", {
                row["packageId"] for row in payload["chummerClosure"]
            })
            self.assertEqual(
                {"Chummer.Desktop.Runtime", "Chummer.Presentation"},
                {row["projectId"] for row in payload["sourceProjectReferences"]},
            )
            module.verify_post_publish(
                payload, packages_root=packages, workspace_root=workspace,
                owner_feed=selected, routed_lock_root=routed_locks, project_lock=lock,
                intermediate_root=intermediate.parent, phase="post-publish",
            )

    def test_inventory_drift_diagnostic_is_sorted_exact_and_byte_free(self) -> None:
        module = load_module()
        expected = [
            {"path": "changed", "sizeBytes": 3, "sha256": "a" * 64},
            {"path": "removed", "sizeBytes": 4, "sha256": "b" * 64},
        ]
        actual = [
            {"path": "z-added", "sizeBytes": 5, "sha256": "c" * 64},
            {"path": "changed", "sizeBytes": 6, "sha256": "d" * 64},
            {"path": "a-added", "sizeBytes": 7, "sha256": "e" * 64},
        ]

        drift = module._inventory_drift(actual, expected, label="test cache")

        self.assertIsNotNone(drift)
        assert drift is not None
        self.assertEqual(2, drift["addedCount"])
        self.assertEqual(1, drift["removedCount"])
        self.assertEqual(1, drift["changedCount"])
        self.assertTrue(drift["exact"])
        self.assertEqual(
            ["a-added", "z-added"],
            [row["path"] for row in drift["added"]],
        )
        self.assertEqual(["removed"], [row["path"] for row in drift["removed"]])
        self.assertEqual(["changed"], [row["path"] for row in drift["changed"]])
        for row in [*drift["added"], *drift["removed"]]:
            self.assertEqual({"path", "sizeBytes", "sha256"}, set(row))
        for row in drift["changed"]:
            self.assertEqual({"path", "sealed", "actual"}, set(row))
            self.assertEqual({"path", "sizeBytes", "sha256"}, set(row["sealed"]))
            self.assertEqual({"path", "sizeBytes", "sha256"}, set(row["actual"]))

        many = [
            {
                "path": f"added-{index:03d}",
                "sizeBytes": index + 1,
                "sha256": f"{index:064x}",
            }
            for index in range(68)
        ]
        exact = module._inventory_drift(many, [], label="exact cache")
        self.assertIsNotNone(exact)
        assert exact is not None
        self.assertTrue(exact["exact"])
        self.assertEqual(len(many), exact["addedCount"])
        self.assertEqual(len(many), len(exact["added"]))

    def test_inventory_drift_receipt_survives_release_input_cleanup_boundary(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            values = fixture(Path(temporary))
            (
                module, workspace, input_root, authority, _feed, selected,
                packages, routed_locks, intermediate, _output, lock,
            ) = values
            payload = module.materialize_payload(
                input_root=input_root,
                workspace_root=workspace,
                authority_path=authority,
                owner_feed=selected,
                packages_root=packages,
                routed_lock_root=routed_locks,
                project_lock=lock,
                intermediate_root=intermediate.parent,
            )
            manifest = private_file(
                input_root / "restore-consumption.json",
                json.dumps(payload, sort_keys=True).encode(),
            )
            private_file(
                packages
                / "chummer.engine.contracts"
                / "1.2.3"
                / "lib"
                / "payload.dll",
                b"changed after restore",
            )

            with self.assertRaises(module.InventoryDriftError) as raised:
                module.verify_post_publish(
                    payload,
                    packages_root=packages,
                    workspace_root=workspace,
                    owner_feed=selected,
                    routed_lock_root=routed_locks,
                    project_lock=lock,
                    intermediate_root=intermediate.parent, phase="post-publish",
                )

            validated_manifest_sha256 = hashlib.sha256(manifest.read_bytes()).hexdigest()
            private_file(manifest, b'{"hostileReplacement":true}')
            diagnostic = input_root.parent / "restore-drift.json"
            module._write_drift_diagnostic(
                diagnostic,
                input_root=input_root,
                workspace_root=workspace,
                manifest_sha256=validated_manifest_sha256,
                error=raised.exception,
            )
            receipt = json.loads(diagnostic.read_text(encoding="utf-8"))
            self.assertEqual(module.DRIFT_DIAGNOSTIC_CONTRACT, receipt["contractName"])
            self.assertEqual("blocked", receipt["status"])
            self.assertFalse(receipt["publicationAuthorized"])
            self.assertEqual(0, receipt["drift"]["addedCount"])
            self.assertEqual(0, receipt["drift"]["removedCount"])
            self.assertEqual(1, receipt["drift"]["changedCount"])
            self.assertEqual(
                validated_manifest_sha256,
                receipt["restoreConsumptionManifestSha256"],
            )
            self.assertNotEqual(
                hashlib.sha256(manifest.read_bytes()).hexdigest(),
                receipt["restoreConsumptionManifestSha256"],
            )
            self.assertEqual(
                "chummer.engine.contracts/1.2.3/lib/payload.dll",
                receipt["drift"]["changed"][0]["path"],
            )
            self.assertEqual(0, diagnostic.stat().st_mode & 0o077)
            self.assertFalse(diagnostic.is_relative_to(input_root))

    def test_verify_cli_emits_drift_receipt_and_remains_blocked(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            values = fixture(Path(temporary))
            (
                module, workspace, input_root, authority, _feed, selected,
                packages, routed_locks, intermediate, _output, lock,
            ) = values
            payload = module.materialize_payload(
                input_root=input_root,
                workspace_root=workspace,
                authority_path=authority,
                owner_feed=selected,
                packages_root=packages,
                routed_lock_root=routed_locks,
                project_lock=lock,
                intermediate_root=intermediate.parent,
            )
            manifest = private_file(
                input_root / "restore-consumption.json",
                json.dumps(payload, sort_keys=True).encode(),
            )
            private_file(
                packages
                / "chummer.engine.contracts"
                / "1.2.3"
                / "lib"
                / "payload.dll",
                b"changed after restore",
            )
            diagnostic = input_root.parent / "restore-drift.json"

            completed = subprocess.run(
                [
                    sys.executable,
                    os.fspath(SCRIPT),
                    "verify",
                    "--input-root",
                    os.fspath(input_root),
                    "--workspace-root",
                    os.fspath(workspace),
                    "--intermediate-root",
                    os.fspath(intermediate.parent),
                    "--phase",
                    "post-publish",
                    "--authority",
                    os.fspath(authority),
                    "--owner-feed",
                    os.fspath(selected),
                    "--packages-root",
                    os.fspath(packages),
                    "--routed-lock-root",
                    os.fspath(routed_locks),
                    "--project-lock",
                    os.fspath(lock),
                    "--manifest",
                    os.fspath(manifest),
                    "--drift-diagnostic",
                    os.fspath(diagnostic),
                ],
                check=False,
                capture_output=True,
                text=True,
            )

            self.assertEqual(2, completed.returncode)
            blocked = json.loads(completed.stdout)
            self.assertEqual("blocked", blocked["status"])
            self.assertFalse(blocked["publicationAuthorized"])
            self.assertEqual(os.fspath(diagnostic), blocked["driftDiagnostic"])
            receipt = json.loads(diagnostic.read_text(encoding="utf-8"))
            self.assertEqual(module.DRIFT_DIAGNOSTIC_CONTRACT, receipt["contractName"])
            self.assertEqual(1, receipt["drift"]["changedCount"])

    def test_tamper_extra_missing_symlink_cache_asset_and_lock_fail_closed(self) -> None:
        mutations = (
            ("package-tamper", "global-packages cache changed"),
            ("package-extra", "global-packages cache changed"),
            ("package-missing", "global-packages cache changed"),
            ("asset-tamper", "sealed restore intermediate changed"),
            ("lock-tamper", "packages.lock.json changed"),
            ("routed-lock-tamper", "routed project locks changed"),
            ("routed-lock-extra", "exactly three approved locks"),
            ("routed-lock-missing", "exactly three approved locks"),
            ("routed-lock-symlink", "unsafe lock"),
            ("output-symlink", "unsafe directory|without following links|singly-linked"),
        )
        for mutation, message in mutations:
            with self.subTest(mutation=mutation), tempfile.TemporaryDirectory() as temporary:
                values = fixture(Path(temporary))
                module, workspace, input_root, authority, _feed, selected, packages, routed_locks, intermediate, output, lock = values
                payload = module.materialize_payload(
                    input_root=input_root, workspace_root=workspace, authority_path=authority,
                    owner_feed=selected, packages_root=packages,
                    routed_lock_root=routed_locks, project_lock=lock,
                    intermediate_root=intermediate.parent,
                )
                engine_dir = packages / "chummer.engine.contracts" / "1.2.3"
                if mutation == "package-tamper":
                    private_file(engine_dir / "lib" / "payload.dll", b"tampered")
                elif mutation == "package-extra":
                    private_file(engine_dir / "extra", b"extra")
                elif mutation == "package-missing":
                    (engine_dir / "lib" / "payload.dll").unlink()
                elif mutation == "asset-tamper":
                    private_file(intermediate / "project.assets.json", b"{}")
                elif mutation == "lock-tamper":
                    private_file(lock, b"tampered")
                elif mutation == "routed-lock-tamper":
                    private_file(
                        routed_locks / "Chummer.Presentation.packages.lock.json", b"tampered"
                    )
                elif mutation == "routed-lock-extra":
                    private_file(routed_locks / "Unexpected.Project.packages.lock.json", b"extra")
                elif mutation == "routed-lock-missing":
                    (routed_locks / "Chummer.Presentation.packages.lock.json").unlink()
                elif mutation == "routed-lock-symlink":
                    presentation_lock = routed_locks / "Chummer.Presentation.packages.lock.json"
                    presentation_lock.unlink()
                    presentation_lock.symlink_to(
                        routed_locks / "Chummer.Android.packages.lock.json"
                    )
                else:
                    private_directory(output)
                    (output / "escape").symlink_to(lock)
                with self.assertRaisesRegex(ValueError, message):
                    module.verify_post_publish(
                        payload, packages_root=packages, workspace_root=workspace,
                        owner_feed=selected, routed_lock_root=routed_locks,
                        project_lock=lock,
                        intermediate_root=intermediate.parent, phase="post-publish",
                    )

    def test_materialize_rejects_workspace_input_nonempty_output_missing_dgspec_and_run_play_drift(self) -> None:
        mutations = (
            ("inside-workspace", "outside the coherent workspace"),
            ("nonempty-output", "stale bin/obj build state"),
            ("missing-dgspec", "exactly one"),
            ("run-play", "does not bind exact Play.Contracts"),
        )
        for mutation, message in mutations:
            with self.subTest(mutation=mutation), tempfile.TemporaryDirectory() as temporary:
                values = fixture(Path(temporary))
                module, workspace, input_root, authority, _feed, selected, packages, routed_locks, intermediate, output, lock = values
                if mutation == "inside-workspace":
                    workspace = private_directory(Path(temporary))
                elif mutation == "nonempty-output":
                    private_file(output / "stale.dll", b"stale")
                elif mutation == "missing-dgspec":
                    (intermediate / "Chummer.Android.csproj.nuget.dgspec.json").unlink()
                else:
                    assets_path = intermediate / "project.assets.json"
                    assets = json.loads(assets_path.read_text())
                    assets["targets"]["net10.0-android36.0"]["Chummer.Run.Contracts/1.2.3"]["dependencies"] = {}
                    private_file(assets_path, json.dumps(assets).encode())
                with self.assertRaisesRegex(ValueError, message):
                    module.materialize_payload(
                        input_root=input_root, workspace_root=workspace,
                        authority_path=authority, owner_feed=selected,
                        packages_root=packages, routed_lock_root=routed_locks,
                        project_lock=lock,
                        intermediate_root=intermediate.parent,
                    )

    def test_source_projects_are_separate_from_packages_and_fail_closed(self) -> None:
        for mutation, message in (
            ("extra-project", "source project references are not exact"),
            ("missing-project", "source project references are not exact"),
            ("duplicate-project-version", "duplicates source project identity"),
            ("duplicate-package-version", "duplicates Chummer package identity"),
            ("bad-project-type", "Chummer library type is invalid"),
        ):
            with self.subTest(mutation=mutation), tempfile.TemporaryDirectory() as temporary:
                values = fixture(Path(temporary))
                module, workspace, input_root, authority, _feed, selected, packages, routed_locks, intermediate, _output, lock = values
                assets_path = intermediate / "project.assets.json"
                assets = json.loads(assets_path.read_text())
                libraries = assets["libraries"]
                if mutation == "extra-project":
                    libraries["Vendor.Unexpected/1.0.0"] = {
                        "type": "project",
                        "path": "../unexpected.csproj",
                        "msbuildProject": "../unexpected.csproj",
                    }
                elif mutation == "missing-project":
                    del libraries["Chummer.Presentation/1.0.0"]
                elif mutation == "duplicate-project-version":
                    duplicate = dict(libraries["Chummer.Presentation/1.0.0"])
                    libraries["Chummer.Presentation/9.9.9"] = duplicate
                elif mutation == "duplicate-package-version":
                    duplicate = dict(libraries["Chummer.Application/1.2.3"])
                    duplicate["path"] = "chummer.application/9.9.9"
                    libraries = {
                        "Chummer.Application/9.9.9": duplicate,
                        **libraries,
                    }
                    assets["libraries"] = libraries
                else:
                    libraries["Chummer.Presentation/1.0.0"]["type"] = "unknown"
                private_file(assets_path, json.dumps(assets).encode())
                with self.assertRaisesRegex(ValueError, message):
                    module.materialize_payload(
                        input_root=input_root, workspace_root=workspace,
                        authority_path=authority, owner_feed=selected,
                        packages_root=packages, routed_lock_root=routed_locks,
                        project_lock=lock,
                        intermediate_root=intermediate.parent,
                    )

    def test_assets_and_dgspec_bind_the_exact_three_project_graph(self) -> None:
        for mutation, message in (
            ("malformed-chummer", "malformed Chummer library identity"),
            ("wrong-project-version", "source project version is not exact"),
            ("spoof-project-path", "source project path is not exact"),
            ("wrong-assets-root", "exact Android project"),
            ("missing-rid-project", "target project identities are not exact"),
            ("dgspec-extra-project", "exact three-project source graph"),
            ("dgspec-root-edge", "project references are not exact"),
            ("dgspec-desktop-edge", "project references are not exact"),
        ):
            with self.subTest(mutation=mutation), tempfile.TemporaryDirectory() as temporary:
                values = fixture(Path(temporary))
                module, workspace, input_root, authority, _feed, selected, packages, routed_locks, intermediate, _output, lock = values
                assets_path = intermediate / "project.assets.json"
                dgspec_path = intermediate / "Chummer.Android.csproj.nuget.dgspec.json"
                assets = json.loads(assets_path.read_text())
                dgspec = json.loads(dgspec_path.read_text())
                android_project = workspace / "chummer-android/src/Chummer.Android/Chummer.Android.csproj"
                desktop_project = workspace / "chummer-presentation/Chummer.Desktop.Runtime/Chummer.Desktop.Runtime.csproj"
                presentation_project = workspace / "chummer-presentation/Chummer.Presentation/Chummer.Presentation.csproj"
                if mutation == "malformed-chummer":
                    assets["libraries"]["Chummer.Malformed"] = {"type": "package"}
                elif mutation == "wrong-project-version":
                    row = assets["libraries"].pop("Chummer.Presentation/1.0.0")
                    assets["libraries"]["Chummer.Presentation/9.9.9"] = row
                elif mutation == "spoof-project-path":
                    spoof = private_file(workspace / "spoof/Chummer.Presentation.csproj", b"<Project />")
                    relative = os.path.relpath(spoof, android_project.parent)
                    assets["libraries"]["Chummer.Presentation/1.0.0"].update({
                        "path": relative,
                        "msbuildProject": relative,
                    })
                elif mutation == "wrong-assets-root":
                    assets["project"]["restore"]["projectPath"] = os.fspath(presentation_project)
                elif mutation == "missing-rid-project":
                    del assets["targets"]["net10.0-android36.0/android-arm64"][
                        "Chummer.Presentation/1.0.0"
                    ]
                elif mutation == "dgspec-extra-project":
                    extra = private_file(workspace / "extra/Extra.csproj", b"<Project />")
                    dgspec["projects"][os.fspath(extra)] = {
                        "restore": {
                            "projectPath": os.fspath(extra),
                            "projectUniqueName": os.fspath(extra),
                            "frameworks": {"net10.0": {"projectReferences": {}}},
                        }
                    }
                else:
                    owner = android_project if mutation == "dgspec-root-edge" else desktop_project
                    framework = (
                        "net10.0-android36.0" if mutation == "dgspec-root-edge" else "net10.0"
                    )
                    del dgspec["projects"][os.fspath(owner)]["restore"]["frameworks"][framework][
                        "projectReferences"
                    ][os.fspath(presentation_project)]
                private_file(assets_path, json.dumps(assets).encode())
                private_file(dgspec_path, json.dumps(dgspec).encode())
                with self.assertRaisesRegex(ValueError, message):
                    module.materialize_payload(
                        input_root=input_root, workspace_root=workspace,
                        authority_path=authority, owner_feed=selected,
                        packages_root=packages, routed_lock_root=routed_locks,
                        project_lock=lock,
                        intermediate_root=intermediate.parent,
                    )


if __name__ == "__main__":
    unittest.main()
