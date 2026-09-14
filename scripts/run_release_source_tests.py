#!/usr/bin/python3
"""Run source tests in a disposable offline venv, never in validator Python.

Only PyPA's hash-pinned bootstrap installs wheels. This module does not implement
a package installer, change the system interpreter, or authorize a release.
The enclosing build container must remain credential-free and network-disabled.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import os
from pathlib import Path
import platform
import re
import signal
import stat
import subprocess
import sys
import tempfile


PYTHON = "/usr/bin/python3"
FILE_LIMIT = 16 * 1024 * 1024
TOTAL_LIMIT = 64 * 1024 * 1024
PYTEST_MODULE = "tests/test_api36_sr5_life_module_nationality_origin_physical_e2e_driver.py"
REQUIREMENT = re.compile(r"([a-zA-Z0-9_-]+)==([0-9][0-9A-Za-z.]+) --hash=sha256:([0-9a-f]{64})")


def directory(path: Path, *, private: bool = False) -> Path:
    if not path.is_absolute() or path.resolve(strict=True) != path or path.is_symlink():
        raise ValueError("test input directory must be absolute, canonical and not a symlink")
    info = path.stat()
    if not stat.S_ISDIR(info.st_mode) or info.st_uid != os.getuid():
        raise ValueError("test input directory must be owned by the build user")
    if info.st_mode & (0o077 if private else 0o022):
        raise ValueError("test input directory permissions are unsafe")
    return path


def read_stable(path: Path, limit: int = FILE_LIMIT) -> bytes:
    with os.fdopen(os.open(path, os.O_RDONLY | os.O_NOFOLLOW | os.O_NONBLOCK), "rb") as stream:
        before = os.fstat(stream.fileno())
        if not stat.S_ISREG(before.st_mode) or before.st_uid != os.getuid():
            raise ValueError("test input is not an owned regular file")
        if before.st_mode & 0o022 or before.st_size > limit:
            raise ValueError("test input permissions or size are unsafe")
        data = stream.read(limit + 1)
        after = os.fstat(stream.fileno())
    identity = lambda value: (value.st_dev, value.st_ino, value.st_size,
                              value.st_mtime_ns, value.st_ctime_ns)
    if len(data) > limit or len(data) != before.st_size or identity(before) != identity(after):
        raise ValueError("test input changed during bounded capture")
    return data


def requirements(raw: bytes) -> dict[str, tuple[str, str]]:
    result = {}
    for line in raw.decode("utf-8").splitlines():
        if not line or line.startswith("#"):
            continue
        match = REQUIREMENT.fullmatch(line)
        if match is None or match[1] in result:
            raise ValueError("test requirements must be unique exact version/hash pins")
        result[match[1]] = (match[2], match[3])
    if len(result) != 11 or len({value[1] for value in result.values()}) != 11:
        raise ValueError("expected the complete eleven-wheel test dependency closure")
    return result


def capture_inputs(repo: Path, bootstrap: Path, wheelhouse: Path) -> tuple[dict, dict[str, bytes], bytes]:
    lock = json.loads(read_stable(repo / "eng/release-test-bootstrap.lock.json", 16384))
    if (lock.get("schemaVersion") != "chummer.android.release-test-bootstrap/v1"
            or lock.get("python") != "3.12" or lock.get("platform") != "linux-x86_64"
            or lock.get("sourceTestsOnly") is not True):
        raise ValueError("invalid test bootstrap lock")
    version = lock["pipVersion"]
    if re.fullmatch(r"[0-9]+(?:\.[0-9]+){1,2}", version) is None:
        raise ValueError("invalid pinned pip version")
    records = lock["artifacts"]
    expected = {"get-pip.py", f"pip-{version}-py3-none-any.whl"}
    if len(records) != 2 or {row["fileName"] for row in records} != expected:
        raise ValueError("bootstrap must contain only get-pip and the pinned pip wheel")
    if {path.name for path in bootstrap.iterdir()} != expected:
        raise ValueError("unexpected or missing bootstrap artifact")
    captured = {}
    for row in records:
        raw = read_stable(bootstrap / row["fileName"])
        if len(raw) != row["sizeBytes"] or hashlib.sha256(raw).hexdigest() != row["sha256"]:
            raise ValueError("test bootstrap artifact hash/size mismatch")
        captured["bootstrap/" + row["fileName"]] = raw
    raw_requirements = read_stable(repo / "tests/requirements-ci.txt", 16384)
    wanted = {value[1] for value in requirements(raw_requirements).values()}
    paths = list(wheelhouse.iterdir())
    if len(paths) != len(wanted):
        raise ValueError("test wheelhouse must contain exactly the locked closure")
    for path in paths:
        if re.fullmatch(r"[A-Za-z0-9_.+-]+\.whl", path.name) is None:
            raise ValueError("unexpected test wheel filename")
        raw = read_stable(path)
        digest = hashlib.sha256(raw).hexdigest()
        if digest not in wanted:
            raise ValueError("unknown, duplicate or modified test wheel")
        wanted.remove(digest)
        captured["wheels/" + path.name] = raw
    if wanted or sum(map(len, captured.values())) > TOTAL_LIMIT:
        raise ValueError("test dependency closure is incomplete or oversized")
    return lock, captured, raw_requirements


def test_environment(home: Path, temporary: Path, workspace: Path, oracle: Path, dotnet: Path) -> dict[str, str]:
    return {
        "PATH": f"{dotnet.parent}:/usr/bin:/bin", "HOME": str(home),
        "XDG_CONFIG_HOME": str(home), "DOTNET_CLI_HOME": str(home),
        "TMPDIR": str(temporary), "LANG": "C.UTF-8", "LC_ALL": "C.UTF-8",
        "DOTNET_ROOT": str(dotnet.parent), "DOTNET_MULTILEVEL_LOOKUP": "0",
        "DOTNET_CLI_TELEMETRY_OPTOUT": "1", "DOTNET_CLI_USE_MSBUILD_SERVER": "0",
        "MSBUILDDISABLENODEREUSE": "1", "PYTHONDONTWRITEBYTECODE": "1",
        "PYTEST_DISABLE_PLUGIN_AUTOLOAD": "1", "PIP_CONFIG_FILE": "/dev/null",
        "CHUMMER_COMPLETE_ROOT": str(workspace), "CHUMMER5A_ROOT": str(oracle),
        "GIT_CONFIG_NOSYSTEM": "1", "GIT_CONFIG_GLOBAL": "/dev/null",
        "GIT_TERMINAL_PROMPT": "0", "GIT_OPTIONAL_LOCKS": "0",
    }


def verify_oracle(repo: Path, oracle: Path, environment: dict[str, str]) -> None:
    workflow = read_stable(repo / ".github/workflows/api36-editing-e2e.yml").decode("utf-8")
    matches = re.findall(r"repository: ArchonMegalon/chummer5a\n\s+ref: ([0-9a-f]{40})\n", workflow)
    if len(matches) != 1:
        raise ValueError("missing or ambiguous hosted source-test oracle pin")
    def git(*arguments: str) -> str:
        return subprocess.run(["/usr/bin/git", "-C", str(oracle), *arguments],
                              env=environment, check=True, capture_output=True,
                              text=True, timeout=30).stdout.strip()
    if git("rev-parse", "HEAD") != matches[0] or git("status", "--porcelain=v1", "--untracked-files=no"):
        raise ValueError("Chummer5 source-test oracle is not the exact clean hosted pin")
    for name in ("Chummer", "Plugins/ChummerHub.Client/UI", "Translator", "CrashHandler", "ChummerDataViewer"):
        path = oracle / name
        if not path.is_dir() or path.resolve() != path:
            raise ValueError("source-test oracle is missing a hosted inventory root")


def run_command(command: list[str], *, environment: dict[str, str], cwd: Path, timeout: int) -> None:
    child = subprocess.Popen(command, env=environment, cwd=cwd, start_new_session=True)
    try:
        code = child.wait(timeout=timeout)
        if code:
            raise subprocess.CalledProcessError(code, command)
    finally:
        # A timeout or interrupted test must not leave package/bootstrap/test
        # descendants using the temporary directory while it is removed.
        try:
            os.killpg(child.pid, signal.SIGKILL)
        except ProcessLookupError:
            pass
        child.wait(timeout=5)


def execute(arguments: argparse.Namespace) -> None:
    if sys.version_info[:2] != (3, 12) or sys.platform != "linux" or platform.machine() != "x86_64":
        raise ValueError("test closure requires Linux x86_64 CPython 3.12")
    repo, workspace, oracle = (directory(arguments.repo_root), directory(arguments.workspace_root),
                               directory(arguments.oracle_root))
    bootstrap, wheelhouse = directory(arguments.bootstrap_dir), directory(arguments.wheelhouse)
    scratch = directory(arguments.scratch_root, private=True)
    if repo != workspace / "chummer-android" or scratch == workspace or workspace in scratch.parents:
        raise ValueError("test source/scratch boundary is invalid")
    dotnet = arguments.dotnet
    if not dotnet.is_absolute() or dotnet.resolve(strict=True) != dotnet or not os.access(dotnet, os.X_OK):
        raise ValueError("test dotnet must be the explicit canonical trusted executable")
    lock, inputs, raw_requirements = capture_inputs(repo, bootstrap, wheelhouse)
    with tempfile.TemporaryDirectory(prefix=".release-source-tests.", dir=scratch) as temporary:
        root = Path(temporary)
        for name in ("bootstrap", "wheels", "home", "tmp"):
            (root / name).mkdir(mode=0o700)
        for name, raw in inputs.items():
            with (root / name).open("xb") as output:
                os.chmod(output.fileno(), 0o600)
                output.write(raw)
        pinned_requirements = root / "requirements-ci.txt"
        pinned_requirements.write_bytes(raw_requirements)
        environment = test_environment(root / "home", root / "tmp", workspace, oracle, dotnet)
        verify_oracle(repo, oracle, environment)
        def run(command: list[str], *, timeout: int = 180) -> None:
            run_command(command, environment=environment, cwd=repo, timeout=timeout)
        venv = root / "venv"
        run([PYTHON, "-I", "-E", "-S", "-B", "-m", "venv", "--without-pip", "--copies", str(venv)])
        python = str(venv / "bin/python")
        # -S is deliberately NOT used for this fresh test-only venv. It remains
        # mandatory and unchanged on every production validator invocation.
        isolated = [python, "-I", "-E", "-B"]
        run([*isolated, "-c", "import sys; assert sys.prefix != sys.base_prefix; "
             "assert not __import__('site').ENABLE_USER_SITE"])
        run([*isolated, str(root / "bootstrap/get-pip.py"), "--isolated", "--no-index",
             "--find-links", str(root / "bootstrap"), "--only-binary=:all:", "--no-deps",
             "--no-cache-dir", "--disable-pip-version-check", "--no-setuptools", "--no-wheel",
             "pip==" + lock["pipVersion"]])
        run([*isolated, "-m", "pip", "--isolated", "install", "--no-index", "--find-links",
             str(root / "wheels"), "--require-hashes", "--only-binary=:all:", "--no-cache-dir",
             "--disable-pip-version-check", "-r", str(pinned_requirements)])
        run([*isolated, "-m", "pip", "--isolated", "check"])
        # tests is a namespace package, so an explicit repository root is needed
        # under -I. Never re-enable ambient cwd/PYTHONPATH or pass -t to discovery.
        launcher = "import runpy,sys; sys.path.insert(0,sys.argv.pop(1)); " \
                   "module=sys.argv.pop(1); runpy.run_module(module,run_name='__main__',alter_sys=True)"
        run([*isolated, "-c", launcher, str(repo), "unittest", "discover", "-s", str(repo / "tests"), "-v"], timeout=1800)
        run([*isolated, "-c", launcher, str(repo), "pytest", "-q", "-p", "no:cacheprovider", str(repo / PYTEST_MODULE)], timeout=600)
    print("android_release_source_tests=passed scope=source-tests-only signing_authorized=false publication_authorized=false")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ("repo-root", "workspace-root", "oracle-root", "bootstrap-dir", "wheelhouse", "scratch-root", "dotnet"):
        parser.add_argument("--" + name, required=True, type=Path)
    arguments = parser.parse_args()
    try:
        execute(arguments)
    except (OSError, ValueError, KeyError, TypeError, subprocess.SubprocessError) as error:
        print(f"android_release_source_tests=failed reason={type(error).__name__}", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
