from __future__ import annotations

import importlib.util
import subprocess
import tempfile
import unittest
from pathlib import Path


REPO = Path(__file__).resolve().parents[1]
SCRIPT = REPO / "scripts" / "verify_release_source_graph.py"


def load_module():
    spec = importlib.util.spec_from_file_location("verify_release_source_graph", SCRIPT)
    assert spec is not None and spec.loader is not None
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def initialize_repository(path: Path, remote: str) -> str:
    path.mkdir(parents=True)
    subprocess.run(["git", "init", "-q", str(path)], check=True)
    subprocess.run(["git", "-C", str(path), "remote", "add", "origin", remote], check=True)
    (path / "authority.txt").write_text(remote + "\n", encoding="utf-8")
    subprocess.run(["git", "-C", str(path), "add", "authority.txt"], check=True)
    subprocess.run(
        [
            "git",
            "-C",
            str(path),
            "-c",
            "user.name=Release Test",
            "-c",
            "user.email=release-test@invalid.example",
            "commit",
            "-q",
            "-m",
            "seed",
        ],
        check=True,
    )
    return subprocess.run(
        ["git", "-C", str(path), "rev-parse", "HEAD"],
        check=True,
        capture_output=True,
        text=True,
    ).stdout.strip()


def seed_workspace(tmp_path: Path):
    module = load_module()
    workspace = tmp_path / "coherent"
    revisions: dict[str, str] = {}
    roots: dict[str, Path] = {}
    for name, _, relative_parts, revision_variable, remote in module.REPOSITORY_SPECS:
        root = workspace.joinpath(*relative_parts)
        roots[name] = root
        revisions[revision_variable] = initialize_repository(root, remote)
    return module, workspace, roots, revisions


class ReleaseSourceGraphTests(unittest.TestCase):
    def test_graph_requires_exact_clean_revision_bound_siblings(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            module, workspace, roots, revisions = seed_workspace(Path(temporary))

            graph = module.build_graph(roots["chummer-android"], workspace, revisions)

            self.assertEqual("chummer.android.release-source-graph/v1", graph["contractName"])
            self.assertEqual(
                [
                    revisions[revision_variable]
                    for _, _, _, revision_variable, _ in module.REPOSITORY_SPECS
                ],
                [row["commit"] for row in graph["repositories"]],
            )

            revisions["CHUMMER_CORE_ENGINE_REVISION"] = "0" * 40
            with self.assertRaisesRegex(ValueError, "revision drifted: chummer6-core"):
                module.build_graph(roots["chummer-android"], workspace, revisions)

    def test_graph_rejects_dirty_or_missing_revision_authority(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            module, workspace, roots, revisions = seed_workspace(Path(temporary))
            (roots["chummer6-ui"] / "untracked.txt").write_text("dirty\n", encoding="utf-8")
            with self.assertRaisesRegex(ValueError, "checkout is dirty: chummer6-ui"):
                module.build_graph(roots["chummer-android"], workspace, revisions)

            (roots["chummer6-ui"] / "untracked.txt").unlink()
            revisions.pop("CHUMMER_UI_KIT_REVISION")
            with self.assertRaisesRegex(
                ValueError,
                "expected revision is missing or invalid: chummer6-ui-kit",
            ):
                module.build_graph(roots["chummer-android"], workspace, revisions)

    def test_graph_output_is_exclusive_and_revalidated_without_timestamp_drift(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            module, workspace, roots, revisions = seed_workspace(root)
            graph = module.build_graph(roots["chummer-android"], workspace, revisions)
            output = root / "release-source-graph.json"

            module.write_graph_exclusive(output, graph)
            module.verify_existing_graph(
                output,
                module.build_graph(roots["chummer-android"], workspace, revisions),
            )

            with self.assertRaises(FileExistsError):
                module.write_graph_exclusive(output, graph)

            tampered = output.read_text(encoding="utf-8").replace(
                '  "doesNotAssert": [',
                '  "unexpected": true,\n  "doesNotAssert": [',
            )
            output.write_text(tampered, encoding="utf-8")
            with self.assertRaisesRegex(ValueError, "during packaging: structure"):
                module.verify_existing_graph(output, graph)
            output.unlink()
            module.write_graph_exclusive(output, graph)

            (roots["chummer6-design"] / "authority.txt").write_text(
                "changed\n",
                encoding="utf-8",
            )
            with self.assertRaisesRegex(ValueError, "checkout is dirty: chummer6-design"):
                module.build_graph(roots["chummer-android"], workspace, revisions)


if __name__ == "__main__":
    unittest.main()
