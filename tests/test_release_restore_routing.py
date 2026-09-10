"""Execute the release scripts' actual graph-check commands, never a build/signing lane."""

import json
import os
from pathlib import Path
import subprocess
import tempfile
import unittest


REPO = Path(__file__).resolve().parents[1]


def commands(script_name, tool):
    lines = (REPO / "scripts" / script_name).read_text(encoding="utf-8").splitlines(keepends=True)
    result = []
    prefix = 'python3 "$repo_dir/scripts/' + tool + '"'
    for index, line in enumerate(lines):
        if not line.startswith(prefix):
            continue
        block = [line]
        while block[-1].rstrip().endswith("\\"):
            index += 1
            block.append(lines[index])
        result.append("".join(block))
    return result


class ReleaseRestoreRoutingTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory(prefix="release routing ")
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        self.workspace = self.root / "workspace"
        self.repo = self.workspace / "chummer-android"
        self.project = self.repo / "src/Chummer.Android/Chummer.Android.csproj"
        self.dependency = self.workspace / "chummer-presentation/Chummer.Presentation/Chummer.Presentation.csproj"
        for project in (self.project, self.dependency):
            project.parent.mkdir(parents=True)
            project.write_text("<Project />", encoding="utf-8")
        scripts = self.repo / "scripts"
        scripts.mkdir()
        (scripts / "verify_native_compile_graph.py").write_bytes(
            (REPO / "scripts/verify_native_compile_graph.py").read_bytes()
        )
        self.intermediate = self.root / "private release/intermediate"
        self.assets_root = self.intermediate / "Chummer.Android"
        self.write_restore(self.assets_root)

    def write_restore(self, directory, project=None):
        project = project or self.project
        directory.mkdir(parents=True)
        assets = {
            "project": {"restore": {"projectPath": str(project)}},
            "libraries": {"Presentation/1.0": {
                "type": "project", "path": str(self.dependency),
                "msbuildProject": str(self.dependency),
            }},
        }
        dgspec = {"projects": {
            str(project): {
                "restore": {"projectPath": str(project), "projectUniqueName": str(project)},
                "frameworks": {"net10.0": {"projectReferences": {str(self.dependency): {}}}},
            },
            str(self.dependency): {
                "restore": {"projectPath": str(self.dependency), "projectUniqueName": str(self.dependency)},
                "frameworks": {},
            },
        }}
        (directory / "project.assets.json").write_text(json.dumps(assets), encoding="utf-8")
        (directory / "Chummer.Android.csproj.nuget.dgspec.json").write_text(json.dumps(dgspec), encoding="utf-8")

    def invoke_graph_command(self, script_name, intermediate=None):
        blocks = commands(script_name, "verify_native_compile_graph.py")
        self.assertEqual(1, len(blocks))
        environment = {
            "PATH": os.environ["PATH"], "LANG": "C.UTF-8", "PYTHONDONTWRITEBYTECODE": "1",
            "repo_dir": str(self.repo), "workspace_root": str(self.workspace),
            "project_path": str(self.project),
            "release_intermediate": str(intermediate or self.intermediate),
            "preparation_obj": str(intermediate or self.intermediate),
        }
        result = subprocess.run(["bash", "-euc", blocks[0]], env=environment,
                                capture_output=True, text=True, timeout=10, check=False)
        return result, json.loads(result.stdout)

    def test_both_release_commands_consume_external_restore_without_workspace_obj(self):
        self.assertFalse((self.project.parent / "obj").exists())
        for script in ("prepare-release-inputs.sh", "build-release.sh"):
            with self.subTest(script=script):
                result, receipt = self.invoke_graph_command(script)
                self.assertEqual(0, result.returncode, result.stdout + result.stderr)
                self.assertEqual("pass", receipt["status"])
                self.assertEqual(2, receipt["generatedProjectReferenceCount"])
                self.assertEqual(str(self.project), receipt["compileProject"])

    def test_missing_external_restore_never_falls_back_to_valid_ambient_obj(self):
        self.write_restore(self.project.parent / "obj")
        result, receipt = self.invoke_graph_command("build-release.sh", self.root / "missing")
        self.assertEqual(2, result.returncode)
        self.assertTrue(any("generated-assets-missing" in issue for issue in receipt["issues"]))

    def test_malformed_external_restore_never_falls_back_to_valid_ambient_obj(self):
        self.write_restore(self.project.parent / "obj")
        (self.assets_root / "project.assets.json").write_text("not json", encoding="utf-8")
        result, receipt = self.invoke_graph_command("build-release.sh")
        self.assertEqual(2, result.returncode)
        self.assertTrue(any("generated-assets-invalid" in issue for issue in receipt["issues"]))

    def test_linked_external_root_is_rejected(self):
        alias = self.root / "alias"
        alias.symlink_to(self.intermediate, target_is_directory=True)
        result, receipt = self.invoke_graph_command("build-release.sh", alias)
        self.assertEqual(2, result.returncode)
        self.assertTrue(any("generated-assets-root-symlink" in issue for issue in receipt["issues"]))

    def test_external_restore_must_bind_the_exact_android_project(self):
        other = self.workspace / "old-android/Other.csproj"
        other.parent.mkdir()
        other.write_text("<Project />", encoding="utf-8")
        payload = json.loads((self.assets_root / "project.assets.json").read_text())
        payload["project"]["restore"]["projectPath"] = str(other)
        (self.assets_root / "project.assets.json").write_text(json.dumps(payload), encoding="utf-8")
        result, receipt = self.invoke_graph_command("build-release.sh")
        self.assertEqual(2, result.returncode)
        self.assertTrue(any("different-project" in issue for issue in receipt["issues"]))

    def test_every_sealer_call_binds_the_same_root_and_exact_publish_phase(self):
        blocks = commands("build-release.sh", "seal_release_restore_consumption.py")
        materialize = [block for block in blocks if block.splitlines()[0].endswith(' materialize \\')]
        verify = [block for block in blocks if block.splitlines()[0].endswith(' verify \\')]
        self.assertEqual(1, len(materialize))
        self.assertEqual(2, len(verify))
        for block in [*materialize, *verify]:
            self.assertEqual(1, block.count('--intermediate-root "$release_intermediate"'))
        self.assertNotIn("--phase", materialize[0])
        self.assertIn("--phase pre-publish", verify[0])
        self.assertNotIn("--phase post-publish", verify[0])
        self.assertIn("--phase post-publish", verify[1])
        self.assertNotIn("--phase pre-publish", verify[1])
        script = (REPO / "scripts/build-release.sh").read_text()
        restore = script.index('"$dotnet_command" restore')
        publish = script.index('"$dotnet_command" publish')
        self.assertLess(restore, script.index(materialize[0]))
        self.assertLess(script.index(verify[0]), publish)
        self.assertLess(publish, script.index(verify[1]))
        self.assertEqual(2, script.count('-p:ChummerReleaseIntermediateRoot="$release_intermediate"'))


if __name__ == "__main__":
    unittest.main()
