from __future__ import annotations

import copy
import hashlib
import importlib.util
import json
import subprocess
import sys
import tempfile
import unittest
import zipfile
from pathlib import Path


REPO = Path(__file__).resolve().parents[1]
SCRIPTS = REPO / "scripts"
SCRIPT = SCRIPTS / "materialize_release_package_authority.py"
sys.path.insert(0, str(SCRIPTS))


def load_module():
    spec = importlib.util.spec_from_file_location("materialize_release_package_authority", SCRIPT)
    assert spec is not None and spec.loader is not None
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def initialize_repository(path: Path, remote: str) -> tuple[str, str]:
    path.mkdir(parents=True)
    subprocess.run(["git", "init", "-q", str(path)], check=True)
    subprocess.run(["git", "-C", str(path), "remote", "add", "origin", remote], check=True)
    (path / "source.txt").write_text(remote + "\n", encoding="utf-8")
    subprocess.run(["git", "-C", str(path), "add", "."], check=True)
    subprocess.run(
        [
            "git", "-C", str(path), "-c", "user.name=Authority Test",
            "-c", "user.email=authority-test@invalid.example", "commit", "-q", "-m", "seed",
        ],
        check=True,
    )
    commit = subprocess.run(
        ["git", "-C", str(path), "rev-parse", "HEAD"],
        check=True, capture_output=True, text=True,
    ).stdout.strip()
    tree = subprocess.run(
        ["git", "-C", str(path), "rev-parse", "HEAD^{tree}"],
        check=True, capture_output=True, text=True,
    ).stdout.strip()
    return commit, tree


def write_package(path: Path, package_id: str, version: str, dependencies: list[tuple[str, str]]) -> None:
    dependency_xml = "".join(
        f'<dependency id="{package}" version="{dependency_version}" />'
        for package, dependency_version in dependencies
    )
    nuspec = (
        '<?xml version="1.0" encoding="utf-8"?>'
        '<package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd">'
        f'<metadata><id>{package_id}</id><version>{version}</version>'
        f'<dependencies><group targetFramework="net10.0">{dependency_xml}</group></dependencies>'
        '</metadata></package>'
    )
    with zipfile.ZipFile(path, "w", compression=zipfile.ZIP_DEFLATED) as archive:
        archive.writestr(f"{package_id}.nuspec", nuspec)


def fixture(root: Path):
    module = load_module()
    workspace = root / "workspace"
    repository_roots: dict[str, Path] = {}
    commits: dict[str, str] = {}
    for name, (parts, remote, _) in module.REPOSITORIES.items():
        repository_root = workspace.joinpath(*parts)
        repository_roots[name] = repository_root
        commits[name], _ = initialize_repository(repository_root, remote)
    source_graph = {
        "coreRuntimeSourceCommit": commits["chummer6-core"],
        "hubProducerCommit": commits["chummer6-hub"],
        "registryCommit": commits["chummer6-hub-registry"],
        "uiKitCommit": commits["chummer6-ui-kit"],
    }

    cache_root = root / "retained-cache"
    package_feed = cache_root / "packages"
    authority_root = cache_root / "authority"
    package_feed.mkdir(parents=True)
    authority_root.mkdir()
    artifact_names = {
        name
        for spec in module.OWNER_PACKAGE_SPECS
        for name in spec[3:]
    }
    authority_artifacts = []
    for name in sorted(artifact_names):
        path = authority_root / name
        path.write_text(f"sealed {name}\n", encoding="utf-8")
        path.chmod(0o600)
        authority_artifacts.append({
            "fileName": name,
            "sha256": hashlib.sha256(path.read_bytes()).hexdigest(),
        })

    core_version = "1.2.3-core"
    owner_version = "1.2.3-owner"
    package_specs: list[tuple[str, str, str, str, str, list[tuple[str, str]]]] = []
    for package_id in module.CORE_PACKAGE_IDS:
        package_specs.append((
            package_id, core_version, "core-runtime", module.REPOSITORIES["chummer6-core"][1],
            commits["chummer6-core"], [],
        ))
    for package_id, owner, plane, *_ in module.OWNER_PACKAGE_SPECS:
        dependencies: list[tuple[str, str]] = []
        if package_id == "Chummer.Campaign.Contracts":
            dependencies = [("Chummer.Engine.Contracts", core_version)]
        elif package_id == "Chummer.Run.Contracts":
            dependencies = [
                ("Chummer.Engine.Contracts", core_version),
                ("Chummer.Hub.Registry.Contracts", owner_version),
                ("Chummer.Play.Contracts", owner_version),
            ]
        package_specs.append((
            package_id, owner_version, plane, module.REPOSITORIES[owner][1], commits[owner], dependencies,
        ))

    package_rows = []
    for package_id, version, plane, repository, commit, dependencies in package_specs:
        file_name = f"{package_id}.{version}.nupkg"
        package_path = package_feed / file_name
        write_package(package_path, package_id, version, dependencies)
        package_path.chmod(0o600)
        package_rows.append({
            "commit": commit,
            "fileName": file_name,
            "packageId": package_id,
            "plane": plane,
            "repository": repository,
            "sha256": hashlib.sha256(package_path.read_bytes()).hexdigest(),
            "sizeBytes": package_path.stat().st_size,
            "version": version,
        })
    cache = {"packages": package_rows, "authorityArtifacts": authority_artifacts}
    return module, workspace, package_feed, cache, source_graph


class ReleasePackageAuthorityMaterializerTests(unittest.TestCase):
    def test_derives_exact_pins_bindings_and_nuspec_dependency_closure(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            module, workspace, package_feed, cache, source_graph = fixture(Path(temporary))

            authority = module.derive_authority(
                workspace_root=workspace,
                package_feed=package_feed,
                cache=cache,
                source_graph=source_graph,
            )

            self.assertEqual(module.CONTRACT, authority["contractName"])
            self.assertEqual(list(module.CORE_PACKAGE_IDS), [row["package_id"] for row in authority["packagePins"]])
            self.assertEqual(
                [spec[0] for spec in module.OWNER_PACKAGE_SPECS],
                [row["package_id"] for row in authority["ownerPackagePins"]],
            )
            run = next(
                row for row in authority["dependencyClosure"]
                if row["package_id"] == "Chummer.Run.Contracts"
            )
            self.assertEqual(
                [
                    "Chummer.Engine.Contracts",
                    "Chummer.Hub.Registry.Contracts",
                    "Chummer.Play.Contracts",
                ],
                run["dependencies"],
            )
            self.assertTrue(all(
                row["authority_receipt"]["path"].startswith("authority/")
                for row in authority["ownerPackagePins"]
            ))

    def test_missing_handwritten_pin_and_tampered_package_fail_closed(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            module, workspace, package_feed, cache, source_graph = fixture(Path(temporary))
            missing = copy.deepcopy(cache)
            missing["packages"] = [
                row for row in missing["packages"]
                if row["packageId"] != "Chummer.Rulesets.Sr6"
            ]
            with self.assertRaisesRegex(ValueError, "Core runtime package authority is missing"):
                module.derive_authority(
                    workspace_root=workspace, package_feed=package_feed,
                    cache=missing, source_graph=source_graph,
                )

            target = next(package_feed.glob("Chummer.Run.Contracts.*.nupkg"))
            target.write_bytes(target.read_bytes() + b"tamper")
            with self.assertRaisesRegex(ValueError, "retained package bytes drifted"):
                module.derive_authority(
                    workspace_root=workspace, package_feed=package_feed,
                    cache=cache, source_graph=source_graph,
                )

    def test_unknown_nuspec_dependency_and_authority_artifact_tamper_fail_closed(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            module, workspace, package_feed, cache, source_graph = fixture(Path(temporary))
            run_row = next(row for row in cache["packages"] if row["packageId"] == "Chummer.Run.Contracts")
            run_path = package_feed / run_row["fileName"]
            write_package(
                run_path,
                "Chummer.Run.Contracts",
                run_row["version"],
                [("Chummer.Unretained.Contracts", "9.9.9")],
            )
            run_row["sha256"] = hashlib.sha256(run_path.read_bytes()).hexdigest()
            run_row["sizeBytes"] = run_path.stat().st_size
            with self.assertRaisesRegex(ValueError, "dependency is absent from the retained exact cache"):
                module.derive_authority(
                    workspace_root=workspace, package_feed=package_feed,
                    cache=cache, source_graph=source_graph,
                )

        with tempfile.TemporaryDirectory() as temporary:
            module, workspace, package_feed, cache, source_graph = fixture(Path(temporary))
            receipt = package_feed.parent / "authority" / "hub-receipt.json"
            receipt.write_text("tampered\n", encoding="utf-8")
            with self.assertRaisesRegex(ValueError, "authority artifact drifted"):
                module.derive_authority(
                    workspace_root=workspace, package_feed=package_feed,
                    cache=cache, source_graph=source_graph,
                )

    def test_source_commit_must_exist_and_output_is_owner_only_exclusive(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            module, workspace, package_feed, cache, source_graph = fixture(root)
            play = next(row for row in cache["packages"] if row["packageId"] == "Chummer.Play.Contracts")
            play["commit"] = "0" * 40
            with self.assertRaisesRegex(ValueError, "source commit is unavailable"):
                module.derive_authority(
                    workspace_root=workspace, package_feed=package_feed,
                    cache=cache, source_graph=source_graph,
                )

            module, workspace, package_feed, cache, source_graph = fixture(root / "second")
            authority = module.derive_authority(
                workspace_root=workspace, package_feed=package_feed,
                cache=cache, source_graph=source_graph,
            )
            output = root / "authority.json"
            module.write_exclusive(output, authority)
            self.assertEqual(0o600, output.stat().st_mode & 0o777)
            module.verify_existing(output, authority)
            with self.assertRaises(FileExistsError):
                module.write_exclusive(output, authority)


if __name__ == "__main__":
    unittest.main()
