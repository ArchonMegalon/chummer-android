"""Small source-runner regressions; fixture packages are never installed."""

import argparse
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import subprocess
import tempfile
import unittest
from unittest.mock import patch


REPO = Path(__file__).resolve().parents[1]
SPEC = importlib.util.spec_from_file_location("release_source_test_runner", REPO / "scripts/run_release_source_tests.py")
runner = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(runner)


class ReleaseSourceTestRunnerTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name).resolve()
        self.workspace = self.root / "workspace"
        self.repo = self.workspace / "chummer-android"
        self.oracle = self.workspace / "chummer5a"
        self.bootstrap = self.root / "bootstrap"
        self.wheels = self.root / "wheels"
        self.scratch = self.root / "scratch"
        for path in (self.repo / "eng", self.repo / "tests", self.oracle, self.bootstrap, self.wheels, self.scratch):
            path.mkdir(parents=True, mode=0o700)
        self.inputs = {"get-pip.py": b"fixture-only", "pip-26.2.1-py3-none-any.whl": b"pip-fixture-only"}
        self.lock = {"schemaVersion": "chummer.android.release-test-bootstrap/v1", "python": "3.12",
                     "platform": "linux-x86_64", "pipVersion": "26.2.1", "sourceTestsOnly": True,
                     "artifacts": [{"fileName": name, "sha256": hashlib.sha256(raw).hexdigest(),
                                    "sizeBytes": len(raw)} for name, raw in self.inputs.items()]}
        for name, raw in self.inputs.items():
            (self.bootstrap / name).write_bytes(raw)
        (self.repo / "eng/release-test-bootstrap.lock.json").write_text(json.dumps(self.lock))
        lines = []
        for index in range(11):
            raw = f"test-wheel-{index}".encode()
            lines.append(f"fixture{index}==1.0 --hash=sha256:{hashlib.sha256(raw).hexdigest()}")
            (self.wheels / f"fixture{index}-1.0-py3-none-any.whl").write_bytes(raw)
        self.requirements = ("\n".join(lines) + "\n").encode()
        (self.repo / "tests/requirements-ci.txt").write_bytes(self.requirements)
        self.arguments = argparse.Namespace(repo_root=self.repo, workspace_root=self.workspace,
            oracle_root=self.oracle, bootstrap_dir=self.bootstrap, wheelhouse=self.wheels,
            scratch_root=self.scratch, dotnet=Path("/usr/bin/true").resolve())

    def test_captures_exact_bootstrap_and_complete_existing_requirements(self):
        lock, captured, raw = runner.capture_inputs(self.repo, self.bootstrap, self.wheels)
        self.assertEqual("26.2.1", lock["pipVersion"])
        self.assertEqual(13, len(captured))
        self.assertEqual(self.requirements, raw)

    def test_missing_modified_duplicate_or_extra_artifacts_fail_before_subprocess(self):
        target = self.wheels / "fixture0-1.0-py3-none-any.whl"
        original = target.read_bytes()
        for bad in (b"modified", (self.wheels / "fixture1-1.0-py3-none-any.whl").read_bytes()):
            with self.subTest(bad=bad):
                target.write_bytes(bad)
                with self.assertRaises(ValueError):
                    runner.capture_inputs(self.repo, self.bootstrap, self.wheels)
        target.write_bytes(original)
        extra = self.bootstrap / "unknown.whl"
        extra.write_bytes(b"extra")
        with self.assertRaises(ValueError):
            runner.capture_inputs(self.repo, self.bootstrap, self.wheels)
        extra.unlink()
        target.unlink()
        with self.assertRaises(ValueError):
            runner.capture_inputs(self.repo, self.bootstrap, self.wheels)

    def test_bootstrap_hash_mutation_and_symlink_are_rejected(self):
        target = self.bootstrap / "get-pip.py"
        target.write_bytes(b"changed")
        with self.assertRaises(ValueError):
            runner.capture_inputs(self.repo, self.bootstrap, self.wheels)
        target.unlink()
        target.symlink_to(self.bootstrap / "pip-26.2.1-py3-none-any.whl")
        with self.assertRaises(OSError):
            runner.capture_inputs(self.repo, self.bootstrap, self.wheels)

    def test_input_size_and_private_directory_are_bounded(self):
        path = self.root / "large"
        path.write_bytes(b"12345")
        with self.assertRaises(ValueError):
            runner.read_stable(path, limit=4)
        self.scratch.chmod(0o755)
        with self.assertRaises(ValueError):
            runner.directory(self.scratch, private=True)
        with self.assertRaises(ValueError):
            runner.directory(Path("relative"))

    def test_fifo_is_rejected_without_waiting_for_a_writer(self):
        fifo = self.root / "not-a-wheel"
        os.mkfifo(fifo, 0o600)
        with self.assertRaises(ValueError):
            runner.read_stable(fifo)

    def test_environment_ignores_host_secrets_python_hooks_and_pytest_plugins(self):
        with patch.dict(os.environ, {"GH_TOKEN": "sentinel", "PYTHONPATH": "/host",
                                     "PYTEST_ADDOPTS": "--host-option", "PYTEST_PLUGINS": "host_plugin"}):
            environment = runner.test_environment(self.root, self.scratch, self.workspace,
                                                  self.oracle, Path("/opt/dotnet/dotnet"))
        self.assertEqual("/opt/dotnet:/usr/bin:/bin", environment["PATH"])
        self.assertEqual("1", environment["PYTEST_DISABLE_PLUGIN_AUTOLOAD"])
        self.assertEqual(str(self.oracle), environment["CHUMMER5A_ROOT"])
        for name in ("GH_TOKEN", "PYTHONPATH", "PYTEST_ADDOPTS", "PYTEST_PLUGINS", "LD_PRELOAD"):
            self.assertNotIn(name, environment)

    def test_runs_both_suites_with_offline_pinned_bootstrap_and_cleans_up(self):
        with patch.object(runner, "verify_oracle"), patch.object(runner, "run_command") as execute:
            execute.return_value = subprocess.CompletedProcess([], 0)
            runner.execute(self.arguments)
        commands = [call.args[0] for call in execute.call_args_list]
        self.assertIn("--without-pip", commands[0])
        self.assertIn("-S", commands[0])
        bootstrap = next(command for command in commands if any(item.endswith("get-pip.py") for item in command))
        self.assertIn("--no-index", bootstrap)
        self.assertIn("pip==26.2.1", bootstrap)
        install = next(command for command in commands if "install" in command)
        self.assertIn("--require-hashes", install)
        self.assertIn("--only-binary=:all:", install)
        self.assertIn("--no-index", install)
        unit = next(command for command in commands if "unittest" in command)
        pytest = next(command for command in commands if "pytest" in command)
        self.assertIn("discover", unit)
        self.assertNotIn("-t", unit)  # tests is a namespace package, not a regular package.
        self.assertEqual(str(self.repo / runner.PYTEST_MODULE), pytest[-1])
        self.assertIn("no:cacheprovider", pytest)
        self.assertNotIn("-S", unit)
        self.assertNotIn("-S", pytest)
        self.assertIn("-I", unit)
        self.assertIn("-I", pytest)
        self.assertEqual([], list(self.scratch.iterdir()))

    def test_failure_cleans_up_and_does_not_skip_to_second_suite(self):
        def fail_unit(command, **unused):
            if "unittest" in command:
                raise subprocess.CalledProcessError(1, command)
            return subprocess.CompletedProcess(command, 0)
        with patch.object(runner, "verify_oracle"), patch.object(runner, "run_command", side_effect=fail_unit) as execute:
            with self.assertRaises(subprocess.CalledProcessError):
                runner.execute(self.arguments)
        self.assertFalse(any("pytest" in call.args[0] for call in execute.call_args_list))
        self.assertEqual([], list(self.scratch.iterdir()))

    def test_timeout_stops_the_owned_process_group_before_cleanup(self):
        with patch.object(runner.subprocess, "Popen") as popen, patch.object(runner.os, "killpg") as kill:
            child = popen.return_value
            child.pid = 12345
            child.wait.side_effect = [subprocess.TimeoutExpired(["fixture"], 1), -9]
            with self.assertRaises(subprocess.TimeoutExpired):
                runner.run_command(["fixture"], environment={}, cwd=self.repo, timeout=1)
            self.assertTrue(popen.call_args.kwargs["start_new_session"])
            kill.assert_called_once_with(12345, runner.signal.SIGKILL)
            self.assertEqual(2, child.wait.call_count)

    def test_oracle_requires_exact_hosted_commit_and_clean_inventory_roots(self):
        workflow = self.repo / ".github/workflows/api36-editing-e2e.yml"
        workflow.parent.mkdir(parents=True)
        workflow.write_text("repository: ArchonMegalon/chummer5a\n          ref: " + "a" * 40 + "\n")
        for name in ("Chummer", "Plugins/ChummerHub.Client/UI", "Translator", "CrashHandler", "ChummerDataViewer"):
            (self.oracle / name).mkdir(parents=True)
        with patch.object(runner.subprocess, "run") as execute:
            execute.side_effect = [subprocess.CompletedProcess([], 0, "a" * 40 + "\n"),
                                   subprocess.CompletedProcess([], 0, "")]
            runner.verify_oracle(self.repo, self.oracle, {})
            execute.side_effect = [subprocess.CompletedProcess([], 0, "a" * 40 + "\n"),
                                   subprocess.CompletedProcess([], 0, " M altered\n")]
            with self.assertRaises(ValueError):
                runner.verify_oracle(self.repo, self.oracle, {})

    def test_production_python_wrapper_stays_site_free_and_runner_is_mandatory(self):
        script = (REPO / "scripts/build-release.sh").read_text()
        wrapper = script[script.index("python3() {"):script.index("realpath() {")]
        self.assertEqual(2, wrapper.count("/usr/bin/python3 -I -E -S"))
        self.assertIn('"$repo_dir/scripts/run_release_source_tests.py"', script)
        self.assertIn('|| fail "offline-release-source-tests"', script)
        self.assertNotIn('python3 -m unittest discover -s "$repo_dir/tests"', script)


if __name__ == "__main__":
    unittest.main()
