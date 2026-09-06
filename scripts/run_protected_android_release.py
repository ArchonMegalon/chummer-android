#!/usr/bin/env python3
"""Start build-release.sh with a sealed script image and non-dumpable process."""

from __future__ import annotations

import ctypes
import fcntl
import os
from pathlib import Path
import stat
import sys


PR_SET_DUMPABLE = 4
PR_GET_DUMPABLE = 3
PR_SET_NO_NEW_PRIVS = 38
SEALS = fcntl.F_SEAL_SEAL | fcntl.F_SEAL_SHRINK | fcntl.F_SEAL_GROW | fcntl.F_SEAL_WRITE
MAX_SCRIPT_BYTES = 2 * 1024 * 1024


def fail(message: str) -> None:
    raise SystemExit(f"protected_android_release=failed error={message}")


def main() -> int:
    root = Path(__file__).resolve().parents[1]
    requested = "build-release.sh"
    forwarded = list(sys.argv[1:])
    if len(forwarded) >= 2 and forwarded[0] == "--protected-script":
        requested = forwarded[1]
        forwarded = forwarded[2:]
    allowed = {
        "build-release.sh": "/usr/bin/bash",
        "sign_api36_two_green_release_approval.py": "/usr/bin/python3",
        "sign_android_release_build_attestation.py": "/usr/bin/python3",
    }
    interpreter = allowed.get(requested)
    if interpreter is None:
        fail("requested protected script is not allowlisted")
    source = root / "scripts" / requested
    if source.is_symlink() or not source.is_file():
        fail("release script is not a regular file")
    source_fd = os.open(source, os.O_RDONLY | getattr(os, "O_NOFOLLOW", 0))
    try:
        before = os.fstat(source_fd)
        path_before = os.stat(source, follow_symlinks=False)
        if (
            not stat.S_ISREG(before.st_mode)
            or before.st_dev != path_before.st_dev
            or before.st_ino != path_before.st_ino
            or before.st_size <= 0
            or before.st_size > MAX_SCRIPT_BYTES
        ):
            fail("release script identity is unstable")
        raw = b""
        while chunk := os.read(source_fd, 1024 * 1024):
            raw += chunk
            if len(raw) > MAX_SCRIPT_BYTES:
                fail("release script is oversized")
        after = os.fstat(source_fd)
        path_after = os.stat(source, follow_symlinks=False)
        if (
            len(raw) != before.st_size
            or (before.st_dev, before.st_ino, before.st_size, before.st_mtime_ns, before.st_ctime_ns)
            != (after.st_dev, after.st_ino, after.st_size, after.st_mtime_ns, after.st_ctime_ns)
            or (after.st_dev, after.st_ino) != (path_after.st_dev, path_after.st_ino)
        ):
            fail("release script changed while being captured")
    finally:
        os.close(source_fd)

    yama = Path("/proc/sys/kernel/yama/ptrace_scope")
    try:
        ptrace_scope = int(yama.read_text(encoding="ascii").strip())
    except (OSError, ValueError) as error:
        fail(f"cannot establish Yama ptrace posture: {error}")
    if ptrace_scope < 2:
        fail("Yama ptrace_scope must be at least 2")
    docker_socket = Path("/var/run/docker.sock")
    if docker_socket.exists() and (
        os.access(docker_socket, os.R_OK) or os.access(docker_socket, os.W_OK)
    ):
        fail("release identity has access to the rootful Docker socket")

    libc = ctypes.CDLL(None, use_errno=True)
    if libc.prctl(PR_SET_DUMPABLE, 0, 0, 0, 0) != 0:
        fail("cannot disable process dumpability")
    if libc.prctl(PR_SET_NO_NEW_PRIVS, 1, 0, 0, 0) != 0:
        fail("cannot enable no_new_privs")
    if libc.prctl(PR_GET_DUMPABLE, 0, 0, 0, 0) != 0:
        fail("release supervisor remains dumpable")

    script_fd = os.memfd_create(
        "chummer-build-release",
        getattr(os, "MFD_ALLOW_SEALING", 0x0002),
    )
    view = memoryview(raw)
    while view:
        written = os.write(script_fd, view)
        if written <= 0:
            fail("cannot materialize sealed release script")
        view = view[written:]
    fcntl.fcntl(script_fd, fcntl.F_ADD_SEALS, SEALS)
    if fcntl.fcntl(script_fd, fcntl.F_GET_SEALS) != SEALS:
        fail("release script memfd is not immutable")
    os.set_inheritable(script_fd, True)
    environment = dict(os.environ)
    environment["CHUMMER_RELEASE_PROCESS_ISOLATED"] = "v1"
    environment["CHUMMER_RELEASE_REPO_ROOT"] = os.fspath(root)
    environment.pop("SSLKEYLOGFILE", None)
    os.execve(
        interpreter,
        [interpreter, f"/proc/self/fd/{script_fd}", *forwarded],
        environment,
    )
    return 127


if __name__ == "__main__":
    raise SystemExit(main())
