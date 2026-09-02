from __future__ import annotations

import base64
import copy
import hashlib
import importlib.util
import json
import os
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
    path.mkdir(parents=True, exist_ok=True)
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
    intermediate = private_directory(workspace / "chummer-android/src/Chummer.Android/obj")
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
    lock = private_file(root / "packages.lock.json", b'{"version":2}')
    return (
        module, workspace, input_root, authority_path, feed, selected_feed,
        packages, routed_locks, intermediate, output, lock,
    )


class ReleaseRestoreConsumptionTests(unittest.TestCase):
    def test_snapshot_binds_exact_twelve_package_closure_and_engine(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            (
                module, _workspace, _input_root, authority, feed, selected,
                _packages, _routed_locks, _intermediate, _output, _lock,
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
                    _packages, _routed_locks, _intermediate, _output, _lock,
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
            )
            self.assertEqual(module.CONTRACT, payload["contractName"])
            self.assertFalse(payload["publicationAuthorized"])
            self.assertTrue(payload["projectAssets"]["path"].endswith("Chummer.Android/obj/project.assets.json"))
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
            )

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
                    )

    def test_materialize_rejects_workspace_input_nonempty_output_missing_dgspec_and_run_play_drift(self) -> None:
        mutations = (
            ("inside-workspace", "outside the coherent workspace"),
            ("nonempty-output", "bin outputs must remain absent before publish"),
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
                    )


if __name__ == "__main__":
    unittest.main()
