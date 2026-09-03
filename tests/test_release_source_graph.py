from __future__ import annotations

import copy
import hashlib
import importlib.util
import json
import shutil
import subprocess
import tempfile
import unittest
from pathlib import Path


REPO = Path(__file__).resolve().parents[1]
SCRIPT = REPO / "scripts" / "verify_release_source_graph.py"
AUTHORITY_PATHS = (
    "authority/package-authority.receipt.json",
    "authority/package-inventory.json",
    "authority/package-plane.lock.json",
)
NEXT_VERSION_NAME = "0.1.0-preview.11"
NEXT_VERSION_CODE = "11"


def load_module():
    spec = importlib.util.spec_from_file_location("verify_release_source_graph", SCRIPT)
    assert spec is not None and spec.loader is not None
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def initialize_repository(path: Path, remote: str, *, android: bool = False) -> tuple[str, str]:
    path.mkdir(parents=True)
    subprocess.run(["git", "init", "-q", str(path)], check=True)
    subprocess.run(["git", "-C", str(path), "remote", "add", "origin", remote], check=True)
    (path / "authority.txt").write_text(remote + "\n", encoding="utf-8")
    if android:
        destination = path / "scripts" / "verify_release_source_graph.py"
        destination.parent.mkdir(parents=True)
        shutil.copyfile(SCRIPT, destination)
    for relative in AUTHORITY_PATHS:
        target = path / relative
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_text(f"{relative}: exact fixture bytes\n", encoding="utf-8")
    subprocess.run(["git", "-C", str(path), "add", "."], check=True)
    subprocess.run(
        [
            "git", "-C", str(path), "-c", "user.name=Release Test",
            "-c", "user.email=release-test@invalid.example", "commit", "-q", "-m", "seed",
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


def file_binding(root: Path, relative: str) -> dict[str, str]:
    return {
        "path": relative,
        "sha256": hashlib.sha256((root / relative).read_bytes()).hexdigest(),
    }


def seed_workspace(tmp_path: Path):
    module = load_module()
    workspace = tmp_path / "coherent"
    revisions: dict[str, str] = {}
    trees: dict[str, str] = {}
    roots: dict[str, Path] = {}
    for name, _, relative_parts, revision_variable, remote in module.REPOSITORY_SPECS:
        root = workspace.joinpath(*relative_parts)
        roots[name] = root
        revisions[revision_variable], trees[name] = initialize_repository(
            root, remote, android=name == "chummer-android"
        )
    authority_root = tmp_path / "retained-package-cache"
    for relative in AUTHORITY_PATHS:
        target = authority_root / relative
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_text(f"{relative}: exact retained bytes\n", encoding="utf-8")
        target.chmod(0o600)
    module.PRESENTATION_SOURCE_COMMIT = revisions["CHUMMER_PRESENTATION_REVISION"]
    module.PRESENTATION_SOURCE_TREE = trees["chummer6-ui"]
    core_commit = revisions["CHUMMER_CORE_ENGINE_REVISION"]
    package_pins = [
        {
            "package_id": package_id,
            "version": "1.2.3-release",
            "sha256": hashlib.sha256(f"core:{package_id}".encode()).hexdigest(),
            "repository": "chummer6-core",
            "commit": core_commit,
        }
        for package_id in module.RUNTIME_PACKAGE_IDS
    ]
    owner_pins = []
    for index, package_id in enumerate(module.OWNER_PACKAGE_IDS, start=1):
        owner = module.OWNER_REPOSITORY_BY_PACKAGE[package_id]
        owner_pins.append({
            "package_id": package_id,
            "version": f"1.2.{index}-release",
            "sha256": hashlib.sha256(f"owner:{package_id}".encode()).hexdigest(),
            "size_bytes": 1000 + index,
            "owner_repository": owner,
            "source_commit": revisions[
                next(spec[3] for spec in module.REPOSITORY_SPECS if spec[0] == owner)
            ],
            "source_tree": trees[owner],
            "authority_receipt": file_binding(authority_root, AUTHORITY_PATHS[0]),
            "package_inventory": file_binding(authority_root, AUTHORITY_PATHS[1]),
            "package_plane_lock": file_binding(authority_root, AUTHORITY_PATHS[2]),
            "dependency_mode": module.LOCKED_DEPENDENCY_MODE,
        })
    closure = [
        {
            "package_id": package_id,
            "dependencies": (
                ["Chummer.Engine.Contracts", "Chummer.Play.Contracts"]
                if package_id == "Chummer.Run.Contracts" else []
            ),
        }
        for package_id in module.OWNER_PACKAGE_IDS
    ]
    authority = {
        "contractName": module.PACKAGE_AUTHORITY_CONTRACT,
        "packagePins": package_pins,
        "ownerPackagePins": owner_pins,
        "dependencyClosure": closure,
    }
    return module, workspace, roots, revisions, authority_root, authority


def build_release_graph(
    module,
    android_root: Path,
    workspace_root: Path,
    authority: dict[str, object],
    authority_root: Path,
    environment: dict[str, str],
    *,
    version_name: str = NEXT_VERSION_NAME,
    version_code: str = NEXT_VERSION_CODE,
):
    return module.build_graph(
        android_root,
        workspace_root,
        authority,
        authority_root,
        environment,
        expected_version_name=version_name,
        expected_version_code=version_code,
    )


class ReleaseSourceGraphTests(unittest.TestCase):
    def test_v3_graph_binds_release_identity_sources_packages_and_local_review_presentation(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            module, workspace, roots, revisions, authority_root, authority = seed_workspace(Path(temporary))

            graph = build_release_graph(module,
                roots["chummer-android"], workspace, authority, authority_root, revisions
            )

            self.assertEqual(module.SOURCE_GRAPH_CONTRACT, graph["contractName"])
            self.assertEqual("local_review_required", graph["authorityState"])
            self.assertFalse(graph["publicationAuthorized"])
            self.assertEqual(
                {
                    "packageId": "com.myexternalbrain.chummer",
                    "versionName": NEXT_VERSION_NAME,
                    "versionCode": 11,
                    "intentAuthority": "explicit_build_input",
                    "minimumExclusiveVersionCode": 10,
                },
                graph["releaseIdentity"],
            )
            self.assertEqual(list(module.RUNTIME_PACKAGE_IDS), [row["package_id"] for row in graph["packagePins"]])
            self.assertEqual(list(module.OWNER_PACKAGE_IDS), [row["package_id"] for row in graph["ownerPackagePins"]])
            self.assertEqual(
                {
                    "repository": "chummer6-ui",
                    "commit": module.PRESENTATION_SOURCE_COMMIT,
                    "tree": module.PRESENTATION_SOURCE_TREE,
                    "source_path": "chummer-presentation",
                    "authority_state": "local_review_required",
                    "publication_authorized": False,
                    "dependency_mode": module.SOURCE_COMPATIBILITY_MODE,
                },
                graph["presentationSource"],
            )

    def test_release_identity_rejects_missing_ambiguous_stale_and_noncanonical_values(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            module, workspace, roots, revisions, authority_root, authority = seed_workspace(
                Path(temporary)
            )
            cases = (
                ("", "11", "version name"),
                ("0.1.0-preview.11\n0.1.0-preview.12", "11", "version name"),
                ("0.1.0-preview.11", "", "version code"),
                ("0.1.0-preview.11", "010", "version code"),
                ("0.1.0-preview.10", "10", "Preview.10 floor"),
            )
            for version_name, version_code, message in cases:
                with self.subTest(version_name=version_name, version_code=version_code):
                    with self.assertRaisesRegex(ValueError, message):
                        build_release_graph(
                            module,
                            roots["chummer-android"],
                            workspace,
                            authority,
                            authority_root,
                            revisions,
                            version_name=version_name,
                            version_code=version_code,
                        )

    def test_graph_requires_exact_clean_revision_bound_siblings(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            module, workspace, roots, revisions, authority_root, authority = seed_workspace(Path(temporary))
            revisions["CHUMMER_CORE_ENGINE_REVISION"] = "0" * 40
            with self.assertRaisesRegex(ValueError, "revision drifted: chummer6-core"):
                build_release_graph(module,
                    roots["chummer-android"], workspace, authority, authority_root, revisions
                )

            module2, workspace2, roots2, revisions2, authority_root2, authority2 = seed_workspace(
                Path(temporary) / "second"
            )
            (roots2["chummer6-ui"] / "untracked.txt").write_text("dirty\n", encoding="utf-8")
            with self.assertRaisesRegex(ValueError, "checkout is dirty: chummer6-ui"):
                build_release_graph(module2,
                    roots2["chummer-android"], workspace2, authority2, authority_root2, revisions2
                )

    def test_owner_pin_set_rejects_missing_extra_duplicate_misowned_and_noncanonical(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            module, workspace, roots, revisions, authority_root, authority = seed_workspace(Path(temporary))
            mutations = []
            missing = copy.deepcopy(authority)
            missing["ownerPackagePins"].pop()
            mutations.append((missing, "exact five"))
            extra = copy.deepcopy(authority)
            extra["ownerPackagePins"].append(copy.deepcopy(extra["ownerPackagePins"][-1]))
            mutations.append((extra, "exact five"))
            duplicate = copy.deepcopy(authority)
            duplicate["ownerPackagePins"][1] = copy.deepcopy(duplicate["ownerPackagePins"][0])
            mutations.append((duplicate, "missing, duplicated, extra, or noncanonical"))
            reordered = copy.deepcopy(authority)
            reordered["ownerPackagePins"][0], reordered["ownerPackagePins"][1] = (
                reordered["ownerPackagePins"][1], reordered["ownerPackagePins"][0]
            )
            mutations.append((reordered, "missing, duplicated, extra, or noncanonical"))
            misowned = copy.deepcopy(authority)
            misowned["ownerPackagePins"][0]["owner_repository"] = "chummer6-ui-kit"
            mutations.append((misowned, "misowned"))

            for payload, message in mutations:
                with self.subTest(message=message):
                    with self.assertRaisesRegex(ValueError, message):
                        build_release_graph(module,
                            roots["chummer-android"], workspace, payload, authority_root, revisions
                        )

    def test_owner_pin_rejects_hash_version_size_source_and_receipt_tamper(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            module, workspace, roots, revisions, authority_root, authority = seed_workspace(Path(temporary))
            mutations = []
            for field, value, message in (
                ("sha256", "not-a-hash", "sha256"),
                ("version", "not a version", "version is not canonical"),
                ("size_bytes", 0, "positive integer"),
                ("source_commit", "0" * 40, "source commit is unavailable"),
                ("source_tree", "1" * 40, "source authority"),
            ):
                payload = copy.deepcopy(authority)
                payload["ownerPackagePins"][0][field] = value
                mutations.append((payload, message))
            receipt = copy.deepcopy(authority)
            receipt["ownerPackagePins"][0]["authority_receipt"]["sha256"] = "f" * 64
            mutations.append((receipt, "digest does not match"))

            for payload, message in mutations:
                with self.subTest(message=message):
                    with self.assertRaisesRegex(ValueError, message):
                        build_release_graph(module,
                            roots["chummer-android"], workspace, payload, authority_root, revisions
                        )

    def test_historical_owner_source_is_bound_to_exact_tree_and_pinned_head_ancestry(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            module, workspace, roots, revisions, authority_root, authority = seed_workspace(
                Path(temporary)
            )
            hub = roots["chummer6-hub"]
            historical_commit = authority["ownerPackagePins"][0]["source_commit"]
            historical_tree = authority["ownerPackagePins"][0]["source_tree"]
            (hub / "current-authority.txt").write_text("new pinned head\n", encoding="utf-8")
            subprocess.run(["git", "-C", str(hub), "add", "current-authority.txt"], check=True)
            subprocess.run(
                [
                    "git", "-C", str(hub), "-c", "user.name=Release Test",
                    "-c", "user.email=release-test@invalid.example", "commit", "-q",
                    "-m", "advance pinned owner head",
                ],
                check=True,
            )
            revisions["CHUMMER_RUN_SERVICES_REVISION"] = subprocess.run(
                ["git", "-C", str(hub), "rev-parse", "HEAD"],
                check=True,
                capture_output=True,
                text=True,
            ).stdout.strip()

            graph = build_release_graph(module,
                roots["chummer-android"], workspace, authority, authority_root, revisions
            )
            hub_pins = [
                row for row in graph["ownerPackagePins"]
                if row["owner_repository"] == "chummer6-hub"
            ]
            for row in hub_pins:
                self.assertEqual(historical_commit, row["source_commit"])
                self.assertEqual(historical_tree, row["source_tree"])
                self.assertEqual(
                    {
                        "owner_head_commit": revisions["CHUMMER_RUN_SERVICES_REVISION"],
                        "owner_head_tree": subprocess.run(
                            ["git", "-C", str(hub), "rev-parse", "HEAD^{tree}"],
                            check=True,
                            capture_output=True,
                            text=True,
                        ).stdout.strip(),
                        "relationship": "ancestor_or_equal",
                        "verification": "git-merge-base-is-ancestor-without-replace-objects",
                    },
                    row["source_authority"],
                )

    def test_existing_owner_commit_with_exact_tree_but_no_head_ancestry_is_rejected(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            module, workspace, roots, revisions, authority_root, authority = seed_workspace(
                Path(temporary)
            )
            hub = roots["chummer6-hub"]
            tree = subprocess.run(
                ["git", "-C", str(hub), "rev-parse", "HEAD^{tree}"],
                check=True,
                capture_output=True,
                text=True,
            ).stdout.strip()
            orphan = subprocess.run(
                [
                    "git", "-C", str(hub), "-c", "user.name=Release Test",
                    "-c", "user.email=release-test@invalid.example", "commit-tree", tree,
                    "-m", "unreachable owner package source",
                ],
                check=True,
                capture_output=True,
                text=True,
            ).stdout.strip()
            for row in authority["ownerPackagePins"]:
                if row["owner_repository"] == "chummer6-hub":
                    row["source_commit"] = orphan
                    row["source_tree"] = tree

            with self.assertRaisesRegex(
                ValueError, "not an ancestor of the pinned owner repository head"
            ):
                build_release_graph(module,
                    roots["chummer-android"], workspace, authority, authority_root, revisions
                )

    def test_locked_mode_rejects_source_fallback_and_authority_path_escape(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            module, workspace, roots, revisions, authority_root, authority = seed_workspace(Path(temporary))
            fallback = copy.deepcopy(authority)
            fallback["ownerPackagePins"][0]["dependency_mode"] = "source_compatibility"
            with self.assertRaisesRegex(ValueError, "cannot fall back to source"):
                build_release_graph(module,
                    roots["chummer-android"], workspace, fallback, authority_root, revisions
                )

            escape = copy.deepcopy(authority)
            escape["ownerPackagePins"][0]["authority_receipt"]["path"] = "../receipt.json"
            with self.assertRaisesRegex(ValueError, "path escapes"):
                build_release_graph(module,
                    roots["chummer-android"], workspace, escape, authority_root, revisions
                )

    def test_stale_v1_and_missing_transitive_play_fail_closed(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            module, workspace, roots, revisions, authority_root, authority = seed_workspace(Path(temporary))
            stale = copy.deepcopy(authority)
            stale["contractName"] = "chummer.android.release-package-authority/v1"
            with self.assertRaisesRegex(ValueError, "exact v2 schema"):
                build_release_graph(module,
                    roots["chummer-android"], workspace, stale, authority_root, revisions
                )

            missing_play = copy.deepcopy(authority)
            run = next(
                row for row in missing_play["dependencyClosure"]
                if row["package_id"] == "Chummer.Run.Contracts"
            )
            run["dependencies"].remove("Chummer.Play.Contracts")
            with self.assertRaisesRegex(ValueError, "missing transitive Chummer.Play.Contracts"):
                build_release_graph(module,
                    roots["chummer-android"], workspace, missing_play, authority_root, revisions
                )

    def test_graph_output_is_exclusive_and_revalidated_without_timestamp_drift(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            module, workspace, roots, revisions, authority_root, authority = seed_workspace(root)
            graph = build_release_graph(module,
                roots["chummer-android"], workspace, authority, authority_root, revisions
            )
            output = root / "release-source-graph.json"

            module.write_graph_exclusive(output, graph)
            module.verify_existing_graph(
                output,
                build_release_graph(module,
                    roots["chummer-android"], workspace, authority, authority_root, revisions
                ),
            )
            with self.assertRaisesRegex(ValueError, "during packaging: releaseIdentity"):
                module.verify_existing_graph(
                    output,
                    build_release_graph(
                        module,
                        roots["chummer-android"],
                        workspace,
                        authority,
                        authority_root,
                        revisions,
                        version_name="0.1.0-preview.12",
                        version_code="12",
                    ),
                )
            with self.assertRaises(FileExistsError):
                module.write_graph_exclusive(output, graph)
            payload = json.loads(output.read_text(encoding="utf-8"))
            payload["ownerPackagePins"][0]["size_bytes"] += 1
            output.write_text(json.dumps(payload), encoding="utf-8")
            with self.assertRaisesRegex(ValueError, "during packaging: ownerPackagePins"):
                module.verify_existing_graph(output, graph)


if __name__ == "__main__":
    unittest.main()
