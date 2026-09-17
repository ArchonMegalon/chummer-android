from __future__ import annotations

import copy
import fcntl
import hashlib
import importlib.util
import json
import os
import shutil
import subprocess
import sys
import tempfile
import unittest
from contextlib import contextmanager
from pathlib import Path


REPO = Path(__file__).resolve().parents[1]
SCRIPT = REPO / "scripts" / "verify_release_source_graph.py"
AUTHORITY_PATHS = (
    "authority/package-authority.receipt.json",
    "authority/package-inventory.json",
    "authority/package-plane.lock.json",
)
NEXT_VERSION_NAME = "0.1.0-preview.12"
NEXT_VERSION_CODE = "12"
IMMUTABLE_GRAPH_SEALS = (
    fcntl.F_SEAL_SEAL | fcntl.F_SEAL_SHRINK | fcntl.F_SEAL_GROW | fcntl.F_SEAL_WRITE
)


@contextmanager
def graph_descriptor(raw: bytes, *, seals: int = IMMUTABLE_GRAPH_SEALS, size: int | None = None):
    descriptor = os.memfd_create("release-source-graph-test", os.MFD_CLOEXEC | os.MFD_ALLOW_SEALING)
    try:
        remaining = memoryview(raw)
        while remaining:
            written = os.write(descriptor, remaining)
            if written <= 0:
                raise OSError("graph fixture write made no progress")
            remaining = remaining[written:]
        if size is not None:
            os.ftruncate(descriptor, size)
        fcntl.fcntl(descriptor, fcntl.F_ADD_SEALS, seals)
        os.lseek(descriptor, 0, os.SEEK_SET)
        yield descriptor
    finally:
        os.close(descriptor)


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
                    "versionCode": 12,
                    "intentAuthority": "explicit_build_input",
                    "minimumExclusiveVersionCode": 11,
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
                ("", "12", "version name"),
                ("0.1.0-preview.12\n0.1.0-preview.13", "12", "version name"),
                ("0.1.0-preview.12", "", "version code"),
                ("0.1.0-preview.12", "012", "version code"),
                ("0.1.0-preview.10", "10", "Preview.11 floor"),
                ("0.1.0-preview.11", "11", "Preview.11 floor"),
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
                        version_name="0.1.0-preview.13",
                        version_code="13",
                    ),
                )
            with self.assertRaises(FileExistsError):
                module.write_graph_exclusive(output, graph)
            payload = json.loads(output.read_text(encoding="utf-8"))
            payload["ownerPackagePins"][0]["size_bytes"] += 1
            output.write_text(json.dumps(payload), encoding="utf-8")
            with self.assertRaisesRegex(ValueError, "during packaging: ownerPackagePins"):
                module.verify_existing_graph(output, graph)


@unittest.skipUnless(hasattr(os, "memfd_create"), "immutable descriptors require Linux memfd")
class ReleaseSourceGraphDescriptorTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        temporary = tempfile.TemporaryDirectory()
        cls.addClassCleanup(temporary.cleanup)
        cls.root = Path(temporary.name)
        (
            cls.module, cls.workspace, cls.roots, cls.revisions,
            cls.authority_root, cls.authority,
        ) = seed_workspace(cls.root)
        cls.graph = build_release_graph(
            cls.module, cls.roots["chummer-android"], cls.workspace,
            cls.authority, cls.authority_root, cls.revisions,
        )
        cls.raw = json.dumps(cls.graph).encode("utf-8")
        cls.authority_path = cls.root / "package-authority.json"
        cls.authority_path.write_text(json.dumps(cls.authority), encoding="utf-8")
        cls.authority_path.chmod(0o600)

    def test_sealed_graph_descriptor_matches_without_consuming_shared_offset(self) -> None:
        with graph_descriptor(self.raw) as descriptor:
            path = Path(f"/proc/self/fd/{descriptor}")
            self.assertTrue(path.is_symlink())
            self.assertTrue(path.is_file())
            self.assertEqual(IMMUTABLE_GRAPH_SEALS, fcntl.fcntl(descriptor, fcntl.F_GET_SEALS))
            os.lseek(descriptor, 7, os.SEEK_SET)
            self.module.verify_existing_graph_fd(descriptor, self.graph)
            self.assertEqual(7, os.lseek(descriptor, 0, os.SEEK_CUR))
            with self.assertRaises(ValueError):
                self.module.verify_existing_graph(path, self.graph)

    def test_graph_descriptor_requires_every_immutable_seal(self) -> None:
        for missing in (
            fcntl.F_SEAL_SEAL, fcntl.F_SEAL_SHRINK, fcntl.F_SEAL_GROW, fcntl.F_SEAL_WRITE,
        ):
            with self.subTest(missing_seal=missing):
                with graph_descriptor(self.raw, seals=IMMUTABLE_GRAPH_SEALS & ~missing) as descriptor:
                    with self.assertRaises(ValueError):
                        self.module.verify_existing_graph_fd(descriptor, self.graph)
        with graph_descriptor(self.raw, seals=0) as descriptor:
            with self.assertRaises(ValueError):
                self.module.verify_existing_graph_fd(descriptor, self.graph)

    def test_graph_descriptor_rejects_ordinary_files_directories_and_pipes(self) -> None:
        with tempfile.TemporaryFile() as ordinary:
            ordinary.write(self.raw)
            ordinary.flush()
            with self.assertRaises(ValueError):
                self.module.verify_existing_graph_fd(ordinary.fileno(), self.graph)
        directory = os.open(self.root, os.O_RDONLY | os.O_DIRECTORY)
        reader, writer = os.pipe()
        try:
            for descriptor in (directory, reader, writer):
                with self.subTest(descriptor=descriptor):
                    with self.assertRaises(ValueError):
                        self.module.verify_existing_graph_fd(descriptor, self.graph)
        finally:
            os.close(directory)
            os.close(reader)
            os.close(writer)

    def test_graph_descriptor_rejects_closed_reserved_and_noninteger_descriptors(self) -> None:
        with graph_descriptor(self.raw) as descriptor:
            closed_descriptor = descriptor
        with self.assertRaises(ValueError):
            self.module.verify_existing_graph_fd(closed_descriptor, self.graph)
        for invalid in (-1, 0, 1, 2, True, False, "3", 3.0, None):
            with self.subTest(descriptor=invalid):
                with self.assertRaises(ValueError):
                    self.module.verify_existing_graph_fd(invalid, self.graph)

    def test_graph_descriptor_rejects_empty_and_oversize_snapshots(self) -> None:
        self.assertEqual(16 * 1024 * 1024, self.module.MAX_SOURCE_GRAPH_BYTES)
        for size in (0, self.module.MAX_SOURCE_GRAPH_BYTES + 1):
            with self.subTest(size=size):
                with graph_descriptor(b"", size=size) as descriptor:
                    with self.assertRaises(ValueError):
                        self.module.verify_existing_graph_fd(descriptor, self.graph)

    def test_graph_descriptor_accepts_exact_size_limit(self) -> None:
        raw = self.raw + b" " * (self.module.MAX_SOURCE_GRAPH_BYTES - len(self.raw))
        with graph_descriptor(raw) as descriptor:
            self.module.verify_existing_graph_fd(descriptor, self.graph)

    @unittest.skipUnless(os.geteuid() == 0, "a real foreign-owner descriptor requires chown privilege")
    def test_graph_descriptor_rejects_foreign_owner(self) -> None:
        with graph_descriptor(self.raw) as descriptor:
            os.fchown(descriptor, os.geteuid() + 1, -1)
            with self.assertRaises(ValueError):
                self.module.verify_existing_graph_fd(descriptor, self.graph)

    def test_graph_descriptor_retains_strict_json_checks(self) -> None:
        malformed = (
            b"{", b"[]", b"\xff",
            self.raw[:-1] + b', "contractName": "duplicate"}',
            self.raw[:-1] + b', "extra": NaN}',
        )
        for index, raw in enumerate(malformed):
            with self.subTest(case=index):
                with graph_descriptor(raw) as descriptor:
                    with self.assertRaises(ValueError):
                        self.module.verify_existing_graph_fd(descriptor, self.graph)

    def test_graph_descriptor_retains_structure_timestamp_and_field_checks(self) -> None:
        missing = copy.deepcopy(self.graph)
        del missing["repositories"]
        extra = {**self.graph, "extra": "unbound"}
        invalid_timestamp = {**self.graph, "generatedAtUtc": "not-utc"}
        changed_identity = copy.deepcopy(self.graph)
        changed_identity["releaseIdentity"]["versionCode"] += 1
        changed_package = copy.deepcopy(self.graph)
        changed_package["ownerPackagePins"][0]["size_bytes"] += 1
        for changed, field in (
            (missing, "structure"), (extra, "structure"),
            (invalid_timestamp, "generatedAtUtc"),
            (changed_identity, "releaseIdentity"), (changed_package, "ownerPackagePins"),
        ):
            with self.subTest(field=field):
                with graph_descriptor(json.dumps(changed).encode("utf-8")) as descriptor:
                    with self.assertRaisesRegex(ValueError, f"during packaging: {field}"):
                        self.module.verify_existing_graph_fd(descriptor, self.graph)
        later = {**self.graph, "generatedAtUtc": "2099-01-01T00:00:00Z"}
        with graph_descriptor(json.dumps(later).encode("utf-8")) as descriptor:
            self.module.verify_existing_graph_fd(descriptor, self.graph)

    def test_ordinary_graph_path_still_rejects_symlinks(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            ordinary = root / "graph.json"
            ordinary.write_bytes(self.raw)
            linked = root / "graph-link.json"
            linked.symlink_to(ordinary)
            self.module.verify_existing_graph(ordinary, self.graph)
            with self.assertRaises(ValueError):
                self.module.verify_existing_graph(linked, self.graph)

    def run_descriptor_cli(self, descriptor: int) -> subprocess.CompletedProcess[str]:
        # The generated Git fixture has its own Presentation commit/tree. Match
        # seed_workspace's fixture pins while exercising the real main(), graph
        # rebuild, argparse, inherited descriptor and comparison in a child.
        launcher = (
            "import importlib.util, sys; "
            "spec = importlib.util.spec_from_file_location('source_graph_cli', sys.argv[1]); "
            "module = importlib.util.module_from_spec(spec); spec.loader.exec_module(module); "
            "module.PRESENTATION_SOURCE_COMMIT = sys.argv[2]; "
            "module.PRESENTATION_SOURCE_TREE = sys.argv[3]; "
            "sys.argv = [sys.argv[1], *sys.argv[4:]]; "
            "raise SystemExit(module.main())"
        )
        return subprocess.run(
            [
                sys.executable, "-I", "-B", "-S", "-c", launcher, str(SCRIPT),
                self.module.PRESENTATION_SOURCE_COMMIT, self.module.PRESENTATION_SOURCE_TREE,
                "--android-root", str(self.roots["chummer-android"]),
                "--workspace-root", str(self.workspace),
                "--package-authority", str(self.authority_path),
                "--authority-root", str(self.authority_root),
                "--expected-version-name", NEXT_VERSION_NAME,
                "--expected-version-code", NEXT_VERSION_CODE,
                "--verify-existing-fd", str(descriptor),
            ],
            env={
                "PATH": os.defpath, "LANG": "C", "LC_ALL": "C",
                "GIT_CONFIG_GLOBAL": "/dev/null", "GIT_CONFIG_NOSYSTEM": "1",
                "GIT_OPTIONAL_LOCKS": "0", **self.revisions,
            },
            pass_fds=(descriptor,), capture_output=True, text=True, timeout=30,
        )

    def test_cli_verifies_real_inherited_graph_descriptor(self) -> None:
        with graph_descriptor(self.raw) as descriptor:
            completed = self.run_descriptor_cli(descriptor)
        self.assertEqual(0, completed.returncode, completed.stdout + completed.stderr)

    def test_cli_rejects_field_drift_in_real_inherited_graph_descriptor(self) -> None:
        changed = copy.deepcopy(self.graph)
        changed["releaseIdentity"]["versionCode"] += 1
        with graph_descriptor(json.dumps(changed).encode("utf-8")) as descriptor:
            completed = self.run_descriptor_cli(descriptor)
        self.assertNotEqual(0, completed.returncode)
        self.assertIn("during packaging: releaseIdentity", completed.stdout + completed.stderr)


if __name__ == "__main__":
    unittest.main()
