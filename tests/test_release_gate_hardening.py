from __future__ import annotations

from email.message import Message
import base64
from contextlib import ExitStack, contextmanager
import importlib.util
import hashlib
import json
import os
from pathlib import Path
import re
import stat
import subprocess
import tempfile
import time
import unittest
from unittest import mock
from types import SimpleNamespace
import zipfile


REPO = Path(__file__).resolve().parents[1]


def load(path: Path, name: str):
    spec = importlib.util.spec_from_file_location(name, path)
    assert spec is not None and spec.loader is not None
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


CAPTURE = load(REPO / "scripts/capture_android_release_outputs.py", "release_capture")
KEYS = load(REPO / "scripts/verify_release_private_key_hygiene.py", "key_hygiene")
HYGIENE = load(REPO / "scripts/verify_release_artifact_hygiene.py", "artifact_hygiene")
BUILD_ATTESTATION = load(
    REPO / "scripts/sign_android_release_build_attestation.py",
    "release_build_attestation_hardening",
)
TWO_GREEN_SIGNER = load(
    REPO / "scripts/sign_api36_two_green_release_approval.py",
    "two_green_release_approval_network_hardening",
)


class ReleaseGateHardeningTests(unittest.TestCase):
    def test_source_graph_descriptor_arguments_require_explicit_inheritance(self) -> None:
        snapshot = CAPTURE._sealed_bytes(b"{}", "graph-arguments")
        descriptor = snapshot["descriptor"]
        try:
            self.assertEqual(
                ["--verify-existing-fd", str(descriptor)],
                BUILD_ATTESTATION._source_graph_verification_arguments(
                    CAPTURE._fd_path(snapshot), (descriptor,)
                ),
            )
            for path, inherited in (
                (CAPTURE._fd_path(snapshot), ()),
                (Path(f"/proc/self/fd/0{descriptor}"), (descriptor,)),
                (Path(f"/proc/self/fd/+{descriptor}"), (descriptor,)),
                (Path("/proc/self/fd/1"), (True,)),
                (Path("/proc/self/fd/2147483648"), (2147483648,)),
            ):
                with self.subTest(path=path), self.assertRaises(ValueError):
                    BUILD_ATTESTATION._source_graph_verification_arguments(path, inherited)
            self.assertEqual(
                ["--verify-existing", "/example/graph.json"],
                BUILD_ATTESTATION._source_graph_verification_arguments(Path("/example/graph.json"), ()),
            )
        finally:
            os.close(descriptor)

    def test_unsigned_handoff_runs_real_source_graph_cli_on_captured_descriptor(self) -> None:
        """Real capture, attester wiring, leased script, CLI/Git and promotion.

        Only certificate/SDK/AAB validators are modeled: this is not a signing
        or APK test. The child substitutes the two Presentation fixture pins,
        not verifier logic. A replaced input name must not replace held bytes.
        """
        fixtures = load(REPO / "tests/test_release_source_graph.py", "handoff_graph_fixture")
        run_validator = BUILD_ATTESTATION._run_validator
        run_process = subprocess.run
        for invalid_graph in (False, True):
            with self.subTest(invalid_graph=invalid_graph), tempfile.TemporaryDirectory() as temporary:
                root = Path(temporary).resolve()
                module, workspace, roots, revisions, authority_root, authority = fixtures.seed_workspace(root)
                graph_value = fixtures.build_release_graph(
                    module, roots["chummer-android"], workspace, authority, authority_root, revisions
                )
                graph_value["publicationAuthorized"] = invalid_graph
                graph = root / "graph.json"
                graph_raw = json.dumps(graph_value).encode()
                graph.write_bytes(graph_raw)
                aab = root / "unsigned.aab"
                aab.write_bytes(b"modeled-unsigned-aab")
                package = root / "package.json"
                package.write_text(json.dumps(authority))
                package.chmod(0o600)
                artifacts = root / "artifacts"
                artifacts.mkdir(mode=0o700)
                paths = {}
                for name in ("java", "javac", "jarsigner", "keytool", "dotnet", "bundletool", "certificate"):
                    paths[name] = root / name
                    paths[name].write_bytes(f"public-test-{name}".encode())
                    paths[name].chmod(0o600)
                digest = lambda path: hashlib.sha256(path.read_bytes()).hexdigest()
                trusted = {
                    "tools": {name: paths[name] for name in ("java", "javac", "jarsigner", "keytool")},
                    "toolSha256": {name: digest(paths[name]) for name in ("java", "javac", "jarsigner", "keytool")},
                    "dotnet": paths["dotnet"], "dotnetSha256": digest(paths["dotnet"]),
                    **{name: "1" * 64 for name in ("observationSha256", "javaSdkTreeSha256",
                        "javaVersionOutputSha256", "dotnetVersionOutputSha256", "dotnetSdkTreeSha256")},
                }
                source_calls = []

                def certificate_or_real_process(arguments, **kwargs):
                    if arguments[0] == "/usr/bin/openssl":
                        return SimpleNamespace(returncode=0, stdout="sha256 Fingerprint=" +
                            BUILD_ATTESTATION.EXPECTED_UPLOAD_CERTIFICATE_SHA256 + "\n")
                    return run_process(arguments, **kwargs)

                def validate(arguments, environment, label, timeout, *, pass_fds=()):
                    if label != "canonical clean source graph":
                        graph.write_bytes(b'{"replaced-input-name":true}')
                        return "2" * 64
                    self.assertIn("--verify-existing-fd", arguments)
                    self.assertNotIn("--verify-existing", arguments)
                    descriptor = int(arguments[arguments.index("--verify-existing-fd") + 1])
                    self.assertIn(descriptor, pass_fds)
                    self.assertEqual(graph_raw, os.pread(descriptor, len(graph_raw), 0))
                    # Execute the actual held validator, using real fixture Git
                    # repositories instead of production SHA constants.
                    bootstrap = (
                        "import runpy,sys; m=runpy.run_path(sys.argv[1]); "
                        "g=m['main'].__globals__; "
                        f"g['PRESENTATION_SOURCE_COMMIT']={module.PRESENTATION_SOURCE_COMMIT!r}; "
                        f"g['PRESENTATION_SOURCE_TREE']={module.PRESENTATION_SOURCE_TREE!r}; "
                        "sys.argv=sys.argv[1:]; raise SystemExit(m['main']())"
                    )
                    source_calls.append(descriptor)
                    return run_validator(
                        [*arguments[:4], "-c", bootstrap, *arguments[4:]],
                        environment, label, timeout, pass_fds=pass_fds,
                    )

                with ExitStack() as stack:
                    for name, value in (
                        ("ROOT", roots["chummer-android"]),
                        ("EXPECTED_BUNDLETOOL_SHA256", digest(paths["bundletool"])),
                    ):
                        stack.enter_context(mock.patch.object(BUILD_ATTESTATION, name, value))
                    stack.enter_context(mock.patch.object(BUILD_ATTESTATION, "_load_trusted_java_toolchain", return_value=trusted))
                    stack.enter_context(mock.patch.object(BUILD_ATTESTATION, "_trusted_system_executable", side_effect=lambda path, _: path))
                    stack.enter_context(mock.patch.object(BUILD_ATTESTATION, "_run_validator", side_effect=validate))
                    stack.enter_context(mock.patch.object(BUILD_ATTESTATION.subprocess, "run", side_effect=certificate_or_real_process))

                    def prepare():
                        return BUILD_ATTESTATION.prepare_external_signer_request(
                            aab, graph, artifacts / "release.aab", artifacts / "graph.json",
                            artifacts / "release.aab.sha256", root / "request.json",
                            root / "receipt.json", root / "approval.json", workspace_root=workspace,
                            package_authority=package, authority_root=authority_root,
                            bundletool=paths["bundletool"], upload_certificate=paths["certificate"],
                            java_tool_authority=root / "tool-authority.json",
                        )

                    if invalid_graph:
                        with self.assertRaisesRegex(ValueError, "canonical clean source graph failed"):
                            prepare()
                        self.assertEqual([], list(artifacts.iterdir()))
                        self.assertFalse((root / "request.json").exists())
                    else:
                        self.assertEqual("external-signer-required", prepare()["status"])
                        self.assertEqual(graph_raw, (artifacts / "graph.json").read_bytes())
                        request = json.loads((root / "request.json").read_text())
                        self.assertFalse(request["publicationAuthorized"])
                        self.assertFalse(request["signingAuthorized"])
                    self.assertEqual(1, len(source_calls))

    def test_proof_verifier_requires_every_immutable_descriptor_seal(self) -> None:
        import fcntl
        fixtures = load(REPO / "tests/test_release_aab_proof_exclusion.py", "sealed_proof_fixture")
        with tempfile.TemporaryDirectory() as temporary:
            aab = Path(temporary) / "clean.aab"
            fixtures._write_aab(aab, b"MZ\x00ordinary-release-assembly")
            bits = (fcntl.F_SEAL_SEAL, fcntl.F_SEAL_SHRINK, fcntl.F_SEAL_GROW, fcntl.F_SEAL_WRITE)
            self.assertEqual(CAPTURE.REQUIRED_SEALS, sum(bits))
            for subset in range(16):
                with self.subTest(seals=subset):
                    descriptor = os.memfd_create("proof-seal-test", os.MFD_ALLOW_SEALING)
                    try:
                        os.write(descriptor, aab.read_bytes())
                        fcntl.fcntl(descriptor, fcntl.F_ADD_SEALS, sum(bit for index, bit in enumerate(bits) if subset & (1 << index)))
                        path = Path(f"/proc/self/fd/{descriptor}")
                        if subset == 15:
                            self.assertEqual((1, 1), fixtures.VERIFIER.verify(path, REPO)[:2])
                        else:
                            with self.assertRaisesRegex(fixtures.VERIFIER.VerificationError, "complete immutable seal set"):
                                fixtures.VERIFIER.verify(path, REPO)
                    finally:
                        os.close(descriptor)

    def test_proof_verifier_rejects_unsafe_descriptor_and_ordinary_symlink_inputs(self) -> None:
        fixtures = load(REPO / "tests/test_release_aab_proof_exclusion.py", "unsafe_proof_fixture")
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            aab = root / "clean.aab"
            fixtures._write_aab(aab, b"MZ\x00ordinary-release-assembly")
            snapshot = CAPTURE._sealed_bytes(aab.read_bytes(), "proof-fixture")
            read_pipe, write_pipe = os.pipe()
            regular = os.open(aab, os.O_RDONLY)
            closed = os.dup(regular)
            os.close(closed)
            link = root / "linked.aab"
            link.symlink_to(aab)
            try:
                for path in (Path(f"/proc/self/fd/{read_pipe}"), Path(f"/proc/self/fd/{regular}"),
                             Path(f"/proc/self/fd/{closed}"), Path(f"/proc/self/fd/0{snapshot['descriptor']}"),
                             Path(f"/proc/self/fd/+{snapshot['descriptor']}"), Path("/proc/self/fd/99999999999"),
                             Path(f"/dev/fd/{snapshot['descriptor']}"), link):
                    with self.subTest(path=str(path)), self.assertRaises(fixtures.VERIFIER.VerificationError):
                        fixtures.VERIFIER.verify(path, REPO)
                for alias in (f"/proc/self/fd//{snapshot['descriptor']}", f"/proc/self/fd/./{snapshot['descriptor']}"):
                    result = subprocess.run(["/usr/bin/python3", "-I", "-E", "-S",
                        str(REPO / "scripts/verify_release_aab_excludes_api36_proof.py"), alias],
                        env={"PATH": "/usr/bin:/bin", "CHUMMER_VALIDATOR_REPO_ROOT": str(REPO)},
                        capture_output=True, text=True, timeout=10, pass_fds=(snapshot["descriptor"],))
                    self.assertNotEqual(0, result.returncode)
                    self.assertIn("descriptor path must be canonical", result.stderr)
                fixtures._write_aab(aab, b"MZ\x00Api36ProofStatePublisher")
                hostile = CAPTURE._sealed_bytes(aab.read_bytes(), "proof-forbidden")
                try:
                    with self.assertRaisesRegex(fixtures.VERIFIER.VerificationError, "proof-type"):
                        fixtures.VERIFIER.verify(CAPTURE._fd_path(hostile), REPO)
                finally:
                    os.close(hostile["descriptor"])
            finally:
                for descriptor in (snapshot["descriptor"], read_pipe, write_pipe, regular):
                    os.close(descriptor)

    def test_unsigned_shell_validation_uses_sealed_aab_bytes_and_rejects_signature_metadata(self) -> None:
        """Real sealed transaction + descriptor-held validators; bundletool modeled.

        Signature entries are synthetic ZIP markers, NOT verified signatures.
        No key generation, jarsigner operation, SDK or Android build runs here.
        """
        fixtures = load(REPO / "tests/test_release_aab_proof_exclusion.py", "unsigned_aab_fixture")
        manifest_xml = '''<manifest xmlns:android="http://schemas.android.com/apk/res/android"
 package="com.myexternalbrain.chummer" android:compileSdkVersion="36" android:versionCode="12"
 android:versionName="0.1.0-preview.12">
 <uses-sdk android:minSdkVersion="24" android:targetSdkVersion="36"/>
 <uses-permission android:name="android.permission.ACCESS_NETWORK_STATE"/>
 <uses-permission android:name="android.permission.INTERNET"/>
 <uses-permission android:name="com.myexternalbrain.chummer.DYNAMIC_RECEIVER_NOT_EXPORTED_PERMISSION"/>
 <application android:allowBackup="false" android:usesCleartextTraffic="false">
 <activity android:exported="true" android:enableOnBackInvokedCallback="true">
 <intent-filter><action android:name="android.intent.action.MAIN"/></intent-filter>
 <intent-filter android:autoVerify="true"><data android:scheme="https" android:host="chummer.run"
 android:path="/app/install-link"/></intent-filter></activity></application></manifest>'''
        markers = (None, "META-INF/UPLOAD.SF", "META-INF/UPLOAD.RSA", "META-INF/UPLOAD.DSA",
                   "META-INF/UPLOAD.EC", "META-INF/SIG-CUSTOM", "meta-inf/upload.sf",
                   "MeTa-InF/upLoad.rSa", "META-INF/sig-custom", "./META-INF/UPLOAD.SF",
                   "/META-INF/UPLOAD.SF", "META-INF//UPLOAD.SF", "META-INF/../UPLOAD.SF", "META-INF\\UPLOAD.SF")
        for marker in markers:
            with self.subTest(marker=marker), tempfile.TemporaryDirectory() as temporary:
                root = Path(temporary).resolve()
                artifacts = root / "artifacts"
                artifacts.mkdir(mode=0o700)
                aab = root / "com.myexternalbrain.chummer-Signed.aab"
                fixtures._write_aab(aab, b"MZ\x00ordinary-release-assembly", extra_name=marker)
                with zipfile.ZipFile(aab, "a") as archive:
                    archive.writestr("META-INF/MANIFEST.MF", b"Manifest-Version: 1.0\r\n\r\n")
                aab = aab.rename(root / "com.myexternalbrain.chummer.aab")
                original = aab.read_bytes()
                graph = root / "graph.json"
                graph.write_bytes(b"{}\n")
                manifest = root / "manifest.xml"
                manifest.write_text(manifest_xml, encoding="utf-8")
                bundletool = root / "modeled-bundletool.jar"
                bundletool.write_bytes(b"NOT an actual bundletool jar")
                java = root / "modeled-java"
                java.write_text("#!/usr/bin/python3\nimport os, pathlib, sys\n"
                    "assert sys.argv[1]=='-jar' and any(x.startswith('--bundle=/proc/self/fd/') for x in sys.argv)\n"
                    "assert sys.argv[3] in ('validate','dump')\n"
                    "if sys.argv[3]=='dump': print(pathlib.Path(os.environ['TEST_MANIFEST']).read_text())\n")
                java.chmod(0o700)
                snapshots = [BUILD_ATTESTATION._lease_current(REPO / "scripts" / name, 8 * 1024 * 1024, name)
                             for name in ("validate-aab.sh", "inspect_aab.py", "verify_release_aab_excludes_api36_proof.py")]
                environment = {"PATH": "/usr/bin:/bin", "LANG": "C", "TEST_MANIFEST": str(manifest),
                    "CHUMMER_BUNDLETOOL_JAR": str(bundletool), "CHUMMER_JAVA": str(java),
                    "CHUMMER_PYTHON3": "/usr/bin/python3", "CHUMMER_JARSIGNER": "/must-not-execute",
                    "CHUMMER_KEYTOOL": "/must-not-execute", "CHUMMER_VALIDATOR_REPO_ROOT": str(REPO),
                    "CHUMMER_EXPECTED_VERSION_NAME": "0.1.0-preview.12", "CHUMMER_EXPECTED_VERSION_CODE": "12",
                    "CHUMMER_INSPECT_AAB_SCRIPT": BUILD_ATTESTATION._lease_fd_path(snapshots[1]),
                    "CHUMMER_PROOF_EXCLUSION_SCRIPT": BUILD_ATTESTATION._lease_fd_path(snapshots[2])}
                observed = []
                def validate(aab_fd, _graph_fd, _sidecar_fd, descriptors):
                    self.assertEqual(original, aab_fd.read_bytes())
                    # A same-UID named-path change cannot decide the validation.
                    fixtures._write_aab(aab, b"MZ\x00ordinary-release-assembly",
                                        extra_name="META-INF/OTHER.SF" if marker is None else None)
                    completed = subprocess.run(["/bin/bash", BUILD_ATTESTATION._lease_fd_path(snapshots[0]), str(aab_fd)],
                        env=environment, capture_output=True, text=True, timeout=20,
                        pass_fds=(*descriptors, *(item["descriptor"] for item in snapshots)))
                    observed.append(completed)
                    if completed.returncode:
                        raise ValueError(completed.stderr)
                try:
                    arguments = (aab, graph, artifacts / "raw.aab", artifacts / "graph.json",
                                 artifacts / "raw.aab.sha256", validate)
                    if marker is None:
                        CAPTURE.transaction(*arguments)
                        self.assertEqual(original, (artifacts / "raw.aab").read_bytes())
                        self.assertIn("no JAR signature metadata", observed[0].stdout)
                    else:
                        error = "noncanonical ZIP name" if any(part in marker for part in ("./", "//", "\\")) \
                            or marker.startswith("/") else "JAR signature metadata is forbidden"
                        with self.assertRaisesRegex(ValueError, error):
                            CAPTURE.transaction(*arguments)
                        self.assertEqual([], list(artifacts.iterdir()))
                    self.assertEqual(1, len(observed))
                finally:
                    BUILD_ATTESTATION._close_leases(snapshots, verify=True)

    def test_unsigned_inspection_flag_preserves_signed_branch_and_is_explicit(self) -> None:
        source = (REPO / "scripts/validate-aab.sh").read_text()
        self.assertIn('if [[ -z "$upload_certificate_path" ]]; then\n  inspection_arguments+=(--require-unsigned)', source)
        self.assertIn('"$inspect_aab_script" "$aab_path" "$temporary_dir/manifest.xml" "${inspection_arguments[@]}"', source)
        self.assertIn('if [[ -n "$upload_certificate_path" ]]; then', source)
        completed = subprocess.run(["/usr/bin/python3", "-I", "-E", "-S", str(REPO / "scripts/inspect_aab.py"),
                                    "missing.aab", "missing.xml", "--allow-signed"],
                                   capture_output=True, text=True, timeout=10)
        self.assertNotEqual(0, completed.returncode)
        self.assertIn("usage: inspect_aab.py", completed.stderr)

    def test_release_shell_entry_ignores_hostile_bash_env(self) -> None:
        """Both supported entry forms must keep BASH_ENV from running."""

        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory).resolve()
            marker = root / "bash-env-executed"
            hostile_bash_env = root / "hostile-bash-env.sh"
            hostile_bash_env.write_text(
                f'printf %s hostile > "{marker}"\n',
                encoding="utf-8",
            )
            environment = {
                "PATH": "/usr/bin:/bin",
                "BASH_ENV": os.fspath(hostile_bash_env),
            }
            for script_name in ("build-release.sh", "prepare-release-inputs.sh"):
                script = REPO / "scripts" / script_name
                for invocation in (
                    [os.fspath(script)],
                    ["/bin/bash", "-p", os.fspath(script)],
                ):
                    with self.subTest(script=script_name, invocation=invocation):
                        marker.unlink(missing_ok=True)
                        completed = subprocess.run(
                            invocation,
                            cwd=REPO,
                            check=False,
                            capture_output=True,
                            env=environment,
                            text=True,
                        )
                        self.assertNotEqual(0, completed.returncode)
                        self.assertFalse(
                            marker.exists(),
                            "BASH_ENV executed before the unsigned release lane rejected its incomplete input",
                        )

    def test_every_nested_release_python_validator_is_isolated(self) -> None:
        attester = (
            REPO / "scripts" / "sign_android_release_build_attestation.py"
        ).read_text(encoding="utf-8")
        command_bodies = re.findall(
            r"\[\s*os\.fspath\(python\),(.*?)\]",
            attester,
            flags=re.DOTALL,
        )
        self.assertEqual(2, len(command_bodies), "unexpected protected Python validator inventory")
        for body in command_bodies:
            self.assertRegex(body, r'^\s*"-I", "-E", "-S",')

        validate_aab = (REPO / "scripts" / "validate-aab.sh").read_text(
            encoding="utf-8"
        )
        nested_python_lines = [
            line.strip()
            for line in validate_aab.splitlines()
            if line.lstrip().startswith('"$python_command"')
        ]
        self.assertEqual(2, len(nested_python_lines), "unexpected AAB Python validator inventory")
        for line in nested_python_lines:
            self.assertTrue(
                line.startswith('"$python_command" -I -E -S '),
                f"nested Python validator is not isolated: {line}",
            )

    def test_transaction_promotes_only_validated_sealed_descriptor_bytes(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory).resolve()
            artifact_dir = root / "artifacts"
            artifact_dir.mkdir(mode=0o700)
            aab = root / "input.aab"
            graph = root / "input.json"
            aab.write_bytes(b"signed-aab")
            graph.write_bytes(b'{"publicationAuthorized":false}\n')
            output_aab = artifact_dir / "release.aab"
            output_graph = artifact_dir / "graph.json"
            output_sidecar = artifact_dir / "release.aab.sha256"

            observed: dict[str, bytes] = {}

            def validate(aab_fd: Path, graph_fd: Path, sidecar_fd: Path, descriptors: tuple[int, ...]) -> None:
                self.assertTrue(str(aab_fd).startswith("/proc/self/fd/"))
                self.assertEqual(3, len(descriptors))
                observed["aab"] = aab_fd.read_bytes()
                observed["graph"] = graph_fd.read_bytes()
                observed["sidecar"] = sidecar_fd.read_bytes()

            result = CAPTURE.transaction(
                aab,
                graph,
                output_aab,
                output_graph,
                output_sidecar,
                validate,
            )
            self.assertFalse(result["publicationAuthorized"])
            self.assertEqual(b"signed-aab", observed["aab"])
            self.assertEqual(observed["aab"], output_aab.read_bytes())
            self.assertEqual(observed["graph"], output_graph.read_bytes())
            self.assertEqual(observed["sidecar"], output_sidecar.read_bytes())
            subprocess.run(
                ["/usr/bin/sha256sum", "--check", str(output_sidecar)],
                cwd=root,
                check=True,
                capture_output=True,
            )

    @contextmanager
    def _external_release_hash_fixture(self):
        """Exercise output binding only; synthetic bytes are not a release AAB."""
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory).resolve()
            repo = root / "workspace/chummer-android"
            repo.mkdir(parents=True)
            release_input = root / "private release inputs"
            artifacts = release_input / "artifacts"
            artifacts.mkdir(parents=True, mode=0o700)
            aab, graph = root / "input.aab", root / "input.json"
            aab.write_bytes(b"synthetic unsigned artifact bytes")
            graph.write_bytes(b'{"publicationAuthorized":false}\n')
            outputs = (artifacts / "release.aab", artifacts / "graph.json",
                       artifacts / "release.aab.sha256")

            def validate(aab_fd, graph_fd, sidecar_fd, descriptors):
                self.assertEqual(aab.read_bytes(), aab_fd.read_bytes())
                self.assertEqual(graph.read_bytes(), graph_fd.read_bytes())
                self.assertEqual(3, len(descriptors))
                self.assertEqual(2, len(sidecar_fd.read_bytes().splitlines()))

            result = CAPTURE.transaction(aab, graph, *outputs, validate)
            self.assertFalse(result["publicationAuthorized"])
            yield root, repo, release_input, outputs, result

    def _run_release_hash_gate(self, root, repo, release_input, outputs, result):
        # Execute the production checksum gate and its real success/failure
        # footer, without running restore, source tests, SDKs or any signer.
        source = (REPO / "scripts/build-release.sh").read_text(encoding="utf-8")
        gate_end = source.index('\n  || fail "sealed-hash-verification"\n')
        gate_start = source.rfind("\n(cd ", 0, gate_end) + 1
        self.assertGreater(gate_start, 0)
        fail_start = source.index("fail() {\n")
        fail_end = source.index("\n}\n", fail_start) + len("\n}\n")
        return subprocess.run(
            ["/bin/bash", "-p", "-c", "set -euo pipefail\n"
             + source[fail_start:fail_end] + source[gate_start:]],
            cwd=root, capture_output=True, text=True, timeout=10,
            env={
                "PATH": "/usr/bin:/bin", "LANG": "C", "LC_ALL": "C",
                "repo_dir": str(repo), "release_input_root": str(release_input),
                "output_aab": str(outputs[0]), "output_graph": str(outputs[1]),
                "output_hash": str(outputs[2]), "version_name": "0.1.0-preview.12",
                "version_code": "12", "source_sha256": result["aabSha256"],
                "graph_sha256": result["sourceGraphSha256"],
                "external_signer_request": str(release_input / "synthetic-request.json"),
                "eligibility_sha256": "0" * 64,
            },
        )

    def test_release_hash_gate_uses_external_outputs_not_stale_repository_artifacts(self) -> None:
        for repository_artifacts in ("absent", "mismatching"):
            with self.subTest(repository_artifacts=repository_artifacts), \
                    self._external_release_hash_fixture() as fixture:
                _root, repo, _release_input, outputs, _result = fixture
                if repository_artifacts == "mismatching":
                    (repo / "artifacts").mkdir()
                    for output in outputs[:2]:
                        (repo / "artifacts" / output.name).write_bytes(b"stale repository decoy")
                before = [path.read_bytes() for path in outputs]
                completed = self._run_release_hash_gate(*fixture)
                self.assertEqual(3, completed.returncode, completed.stderr)
                self.assertIn("android_release=external-signer-required", completed.stdout)
                for field in ("signing_authorized", "publication_authorized", "google_play_upload_authorized"):
                    self.assertIn(f"{field}=false", completed.stdout)
                self.assertEqual(before, [path.read_bytes() for path in outputs])

    def test_release_hash_gate_rejects_bad_external_outputs_despite_matching_repository_decoys(self) -> None:
        for output_index in (0, 1, 2):
            for fault in ("missing", "corrupt"):
                with self.subTest(output_index=output_index, fault=fault), \
                        self._external_release_hash_fixture() as fixture:
                    _root, repo, _release_input, outputs, _result = fixture
                    (repo / "artifacts").mkdir()
                    for output in outputs:
                        (repo / "artifacts" / output.name).write_bytes(output.read_bytes())
                    target = outputs[output_index]
                    target.unlink()
                    if fault == "corrupt":
                        target.write_bytes(b"corrupt external output")
                    before = {path: path.read_bytes() if path.exists() else None for path in outputs}
                    completed = self._run_release_hash_gate(*fixture)
                    self.assertEqual(1, completed.returncode, completed.stdout)
                    self.assertIn("android_release=failed stage=sealed-hash-verification", completed.stderr)
                    self.assertNotIn("android_release=external-signer-required", completed.stdout)
                    self.assertEqual(before, {
                        path: path.read_bytes() if path.exists() else None for path in outputs
                    })

    def test_transaction_rejects_source_changed_during_descriptor_snapshot(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory).resolve()
            source = root / "source.aab"
            source.write_bytes(b"a" * (2 * CAPTURE.CHUNK))
            original_read = os.read
            changed = False

            def hostile_read(descriptor: int, size: int) -> bytes:
                nonlocal changed
                result = original_read(descriptor, size)
                if result and not changed:
                    changed = True
                    source.write_bytes(b"b" * (2 * CAPTURE.CHUNK))
                return result

            with mock.patch.object(CAPTURE.os, "read", side_effect=hostile_read):
                with self.assertRaisesRegex(ValueError, "changed while being snapshotted"):
                    CAPTURE._sealed_snapshot(source, label="test", limit=4 * CAPTURE.CHUNK)

    def test_same_uid_hmac_replacement_attack_has_no_authority_seam(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory).resolve()
            artifact_dir = root / "artifacts"
            artifact_dir.mkdir(mode=0o700)
            aab = root / "input.aab"
            graph = root / "graph.json"
            aab.write_bytes(b"signed-aab")
            graph.write_bytes(b'{"publicationAuthorized":false}\n')
            fake_key = root / "readable-hmac.key"
            fake_receipt = root / "capture-receipt.json"

            def validate(aab_fd: Path, _graph_fd: Path, _sidecar_fd: Path, _descriptors: tuple[int, ...]) -> None:
                self.assertEqual(b"signed-aab", aab_fd.read_bytes())
                # Reproduce the old proven attack: same UID reads the key,
                # replaces every named input and recomputes its own receipt.
                fake_key.write_bytes(b"k" * 32)
                aab.write_bytes(b"unvalidated-evil-aab")
                graph.write_bytes(b'{"attacker":true}\n')
                fake_receipt.write_text(json.dumps({
                    "aabSha256": hashlib.sha256(aab.read_bytes()).hexdigest(),
                    "hmac": "attacker-can-recompute-it",
                }))

            CAPTURE.transaction(
                aab,
                graph,
                artifact_dir / "release.aab",
                artifact_dir / "graph.json",
                artifact_dir / "release.aab.sha256",
                validate,
            )
            self.assertEqual(b"signed-aab", (artifact_dir / "release.aab").read_bytes())
            self.assertEqual(b'{"publicationAuthorized":false}\n', (artifact_dir / "graph.json").read_bytes())
            self.assertFalse(hasattr(CAPTURE, "capture"))
            self.assertFalse(hasattr(CAPTURE, "promote"))
            self.assertFalse(hasattr(CAPTURE, "AUTHENTICATION_KEY_BYTES"))

    def test_transaction_failure_promotes_nothing(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory).resolve()
            artifacts = root / "artifacts"
            artifacts.mkdir(mode=0o700)
            aab = root / "input.aab"
            graph = root / "graph.json"
            aab.write_bytes(b"signed-aab")
            graph.write_bytes(b"{}\n")
            with self.assertRaisesRegex(ValueError, "hostile validator"):
                CAPTURE.transaction(
                    aab,
                    graph,
                    artifacts / "release.aab",
                    artifacts / "graph.json",
                    artifacts / "release.aab.sha256",
                    lambda *_: (_ for _ in ()).throw(ValueError("hostile validator")),
                )
            self.assertEqual([], list(artifacts.iterdir()))

    def test_external_signer_request_is_non_authoritative_and_requires_rebuild(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory).resolve()
            root.chmod(0o700)
            artifacts = root / "artifacts"
            artifacts.mkdir(mode=0o700)
            aab = root / "unsigned.aab"
            graph = root / "graph.json"
            aab.write_bytes(b"unsigned-bundle")
            graph.write_text(
                json.dumps(
                    {
                        "contractName": BUILD_ATTESTATION.SOURCE_GRAPH_CONTRACT,
                        "releaseIdentity": {
                            "packageId": "com.myexternalbrain.chummer",
                            "versionName": "9.9.9-candidate",
                            "versionCode": 999,
                        },
                    }
                )
                + "\n",
                encoding="utf-8",
            )
            output_aab = artifacts / "release-unsigned.aab"
            output_graph = artifacts / "release-source-graph.json"
            output_sidecar = artifacts / "release-unsigned.aab.sha256"
            output_request = root / "external-signer-request.json"
            unsigned_validation = {
                "status": "pass",
                "signatureValidated": False,
                "externalSignerRequired": True,
                "publicationAuthorized": False,
            }
            with mock.patch.object(
                BUILD_ATTESTATION,
                "_protected_validation_inputs",
                return_value=unsigned_validation,
            ):
                result = BUILD_ATTESTATION.prepare_external_signer_request(
                    aab,
                    graph,
                    output_aab,
                    output_graph,
                    output_sidecar,
                    output_request,
                    root / "receipt",
                    root / "approval",
                    workspace_root=root,
                    package_authority=root / "package-authority",
                    authority_root=root,
                    bundletool=root / "bundletool",
                    upload_certificate=root / "certificate",
                    java_tool_authority=root / "toolchain-authority",
                )
            request = json.loads(output_request.read_text(encoding="utf-8"))
            self.assertEqual("external-signer-required", result["status"])
            self.assertEqual("none", request["requestAuthority"])
            self.assertFalse(request["signingAuthorized"])
            self.assertFalse(request["publicationAuthorized"])
            self.assertFalse(request["googlePlayUploadAuthorized"])
            self.assertTrue(
                request["requiredExternalSigner"]["mustRebuildAndMatchUnsignedAab"]
            )
            self.assertFalse(
                request["requiredExternalSigner"]["implementedByThisRepository"]
            )
            self.assertTrue(
                request["requiredExternalSigner"][
                    "mustBindFullJdkDotnetAndroidSdkClosure"
                ]
            )
            expected_output = request["expectedExternalSignerOutput"]
            self.assertEqual(
                "chummer.android.external-release-signer-attestation/v1",
                expected_output["contractName"],
            )
            self.assertEqual(
                request["sourceGraph"]["sha256"],
                expected_output["mustBindSourceGraphSha256"],
            )
            self.assertFalse(expected_output["publicationAuthorized"])
            self.assertFalse(expected_output["googlePlayUploadAuthorized"])
            self.assertEqual(
                hashlib.sha256(b"unsigned-bundle").hexdigest(),
                request["unsignedAab"]["sha256"],
            )

    def test_private_key_hygiene_rejects_tracked_and_ignored_key_material(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory).resolve()
            subprocess.run(["git", "-C", str(root), "init", "--quiet"], check=True)
            (root / ".gitignore").write_text("\n".join(KEYS.REQUIRED_IGNORES) + "\n")
            (root / "safe.txt").write_text("safe\n")
            subprocess.run(
                ["git", "-C", str(root), "add", ".gitignore", "safe.txt"], check=True
            )
            KEYS.verify(root)

            tracked = root / "tracked.txt"
            tracked.write_bytes(b"-----BEGIN " + b"PRIVATE KEY-----\nsecret\n")
            subprocess.run(["git", "-C", str(root), "add", "-f", "tracked.txt"], check=True)
            with self.assertRaisesRegex(ValueError, "private key marker"):
                KEYS.verify(root)
            subprocess.run(["git", "-C", str(root), "rm", "--cached", "tracked.txt"], check=True, capture_output=True)
            tracked.unlink()

            ignored = root / "hostile.private.pem"
            ignored.write_text("secret\n")
            with self.assertRaisesRegex(ValueError, "ignored private-key-shaped"):
                KEYS.verify(root)

            ignored.unlink()
            with (root / ".gitignore").open("a", encoding="utf-8") as stream:
                stream.write("ignored/\n")
            ignored_directory = root / "ignored"
            ignored_directory.mkdir()
            generic_ignored = ignored_directory / "secret.txt"
            generic_ignored.write_bytes(
                b"-----BEGIN " + b"PRIVATE KEY-----\nnever-log-this-value\n"
            )
            with self.assertRaisesRegex(ValueError, "private key marker") as failure:
                KEYS.verify(root)
            self.assertNotIn("never-log-this-value", str(failure.exception))

    def test_caller_owned_fake_release_toolchain_is_rejected(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory).resolve()
            java_sdk = root / "java-sdk"
            (java_sdk / "bin").mkdir(parents=True)
            for name in ("java", "javac", "jarsigner", "keytool"):
                tool = java_sdk / "bin" / name
                tool.write_text("#!/bin/sh\nexit 0\n", encoding="utf-8")
                tool.chmod(0o700)
            dotnet = root / "dotnet"
            dotnet.write_text("#!/bin/sh\nexit 0\n", encoding="utf-8")
            dotnet.chmod(0o700)
            if os.getuid() != 0:
                with self.assertRaisesRegex(ValueError, "root-owned"):
                    BUILD_ATTESTATION._java_toolchain_unsigned(java_sdk, dotnet)
            attacker_private = root / "attacker.private.pem"
            subprocess.run(
                ["openssl", "genpkey", "-algorithm", "ED25519", "-out", str(attacker_private)],
                check=True, capture_output=True,
            )
            attacker_private.chmod(0o600)
            authority = root / "attacker-toolchain-authority.json"
            with self.assertRaisesRegex(ValueError, "external-signer-required"):
                BUILD_ATTESTATION.sign_java_toolchain_authority(
                    java_sdk, dotnet, attacker_private, authority
                )
            self.assertFalse(authority.exists())

    def test_local_toolchain_record_is_unsigned_non_authority_and_omits_android_sdk(self) -> None:
        # Schema/routing fixture only: modeled SDK custody is not deployment proof.
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory).resolve()
            java_sdk = root / "jdk"
            (java_sdk / "bin").mkdir(parents=True)
            for name in ("java", "javac", "jarsigner", "keytool"):
                tool = java_sdk / "bin" / name
                tool.write_bytes(f"local-{name}".encode("ascii"))
                tool.chmod(0o700)
            dotnet = root / "dotnet" / "dotnet"
            dotnet.parent.mkdir()
            dotnet.write_bytes(b"local-dotnet")
            dotnet.chmod(0o700)
            output = root / "toolchain-observation.json"
            with mock.patch.object(
                BUILD_ATTESTATION, "_trusted_tool_root", side_effect=lambda path, _label: path
            ), mock.patch.object(
                BUILD_ATTESTATION, "_trusted_tool", side_effect=lambda path, _root, _label: path
            ), mock.patch.object(
                BUILD_ATTESTATION, "_java_version_digest", return_value="1" * 64
            ), mock.patch.object(
                BUILD_ATTESTATION, "_dotnet_version_digest", return_value="2" * 64
            ), mock.patch.object(
                BUILD_ATTESTATION, "_trusted_tree_digest", return_value=("3" * 64, 4, 100)
            ), mock.patch.object(
                BUILD_ATTESTATION, "_trusted_tool_sha256",
                side_effect=lambda path, label, limit: BUILD_ATTESTATION._sha256_file(path, label, limit),
            ) as tool_hash:
                observation = BUILD_ATTESTATION.materialize_java_toolchain_observation(
                    java_sdk, dotnet, output
                )
            # Both construction and readback must route every SDK hash, with its cap.
            expected = [mock.call(dotnet, "trusted dotnet", 256 * 1024 * 1024)] + [
                mock.call(java_sdk / "bin" / name, f"trusted Java {name}", 128 * 1024 * 1024)
                for name in ("java", "javac", "jarsigner", "keytool")
            ]
            self.assertCountEqual(tool_hash.call_args_list, expected * 2)
            self.assertEqual(
                "non_authoritative_local_unsigned_preparation",
                observation["authorityClass"],
            )
            self.assertFalse(observation["androidSdkBound"])
            self.assertFalse(observation["signingAuthorized"])
            self.assertFalse(observation["publicationAuthorized"])
            self.assertNotIn("signatureBase64", observation)
            self.assertNotIn("keyId", observation)
            self.assertTrue(
                observation["externalSignerMustBindFullJdkDotnetAndroidSdkClosure"]
            )

    def test_trusted_executable_real_root_owner_differs_from_caller_reader(self) -> None:
        # Read an existing system executable; never execute it or fake its UID.
        tool = Path("/usr/bin/true")
        if os.getuid() == 0 or not tool.is_file():
            self.skipTest("requires a nonroot caller and a root-owned system executable")
        self.assertEqual(0, tool.stat().st_uid)
        self.assertLessEqual(tool.stat().st_size, 256 * 1024)
        expected = hashlib.sha256(tool.read_bytes()).hexdigest()
        with mock.patch.object(os, "read", wraps=os.read) as reads:
            self.assertEqual(expected, BUILD_ATTESTATION._trusted_tool_sha256(
                tool, "system fixture", 256 * 1024
            ))
        self.assertEqual(4, reads.call_count)  # Bytes + EOF, twice; no subprocess.
        self.assertTrue(all(call.args[1] == 1024 * 1024 for call in reads.call_args_list))
        with self.assertRaisesRegex(ValueError, "bounded owner file"):
            BUILD_ATTESTATION._sha256_file(tool, "ordinary fixture", 256 * 1024)
        with tempfile.TemporaryDirectory() as directory:
            caller_tool = Path(directory) / "caller-tool"
            caller_tool.write_bytes(b"caller bytes")
            caller_tool.chmod(0o700)
            self.assertEqual(os.getuid(), caller_tool.stat().st_uid)
            self.assertEqual(hashlib.sha256(b"caller bytes").hexdigest(),
                             BUILD_ATTESTATION._sha256_file(caller_tool, "ordinary", 128))
            with self.assertRaisesRegex(ValueError, "root-owned"):
                BUILD_ATTESTATION._trusted_tool_sha256(caller_tool, "forged tool", 128)

    def test_readable_owner_only_private_keys_cannot_authorize_local_signing(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory).resolve()
            readable_key = root / "readable.private.pem"
            readable_key.write_text(
                "-----BEGIN PRIVATE KEY-----\nattacker-readable\n-----END PRIVATE KEY-----\n",
                encoding="ascii",
            )
            readable_key.chmod(0o600)
            with mock.patch.object(
                BUILD_ATTESTATION.KEY_HYGIENE, "verify", return_value=None
            ), self.assertRaisesRegex(ValueError, "external-signer-required"):
                BUILD_ATTESTATION._private_key(readable_key)
            with mock.patch.object(
                TWO_GREEN_SIGNER.KEY_HYGIENE, "verify", return_value=None
            ), self.assertRaisesRegex(ValueError, "external-signer-required"):
                TWO_GREEN_SIGNER._private_key(readable_key)

            with mock.patch.object(
                BUILD_ATTESTATION, "_artifact_claims"
            ) as artifact_claims, self.assertRaisesRegex(
                ValueError, "external-signer-required"
            ):
                BUILD_ATTESTATION.sign(
                    readable_key, readable_key, readable_key, readable_key,
                    readable_key, readable_key, root / "output", readable_key,
                )
            artifact_claims.assert_not_called()
            with mock.patch.object(
                TWO_GREEN_SIGNER.VERIFIER, "_stable_bytes"
            ) as stable_bytes, self.assertRaisesRegex(
                ValueError, "external-signer-required"
            ):
                TWO_GREEN_SIGNER.sign(
                    readable_key, readable_key, readable_key, root / "approval"
                )
            stable_bytes.assert_not_called()

            build = (REPO / "scripts/build-release.sh").read_text(encoding="utf-8")
            self.assertIn("external-signer-required-readable-signing-input-rejected", build)
            self.assertIn("-p:AndroidKeyStore=false", build)
            self.assertNotIn("signing-keystore-preflight", build)
            self.assertNotIn("ChummerAndroidSigningStorePass=", build)

            for script_name, marker in (
                ("build-release.sh", "android_release=failed"),
                ("prepare-release-inputs.sh", "android_release_inputs=failed"),
            ):
                for credential_name, credential_value in (
                    ("CHUMMER_ANDROID_BUILD_ATTESTATION_PRIVATE_KEY", str(readable_key)),
                    ("GITHUB_TOKEN", "github-secret"),
                    ("ACTIONS_RUNTIME_TOKEN", "actions-secret"),
                    ("GOOGLE_OAUTH_ACCESS_TOKEN", "play-secret"),
                ):
                    completed = subprocess.run(
                        ["/bin/bash", "-p", str(REPO / "scripts" / script_name)],
                        check=False,
                        capture_output=True,
                        text=True,
                        env={"PATH": "/usr/bin:/bin", credential_name: credential_value},
                    )
                    self.assertNotEqual(0, completed.returncode)
                    self.assertIn(marker, completed.stderr)
                    self.assertIn(
                        "external-signer-required-readable-signing-input-rejected",
                        completed.stderr,
                    )
                    self.assertNotIn("release-version-intent", completed.stderr)
                    self.assertNotIn(credential_value, completed.stderr)

    def test_protected_tool_leases_reject_path_and_metadata_drift(self) -> None:
        for mutation in ("replace", "world-writable", "link-count", "ctime-only"):
            with self.subTest(mutation=mutation), tempfile.TemporaryDirectory() as directory:
                root = Path(directory).resolve()
                tool = root / "tool"
                tool.write_bytes(b"trusted-tool-bytes")
                tool.chmod(0o400)
                digest = hashlib.sha256(tool.read_bytes()).hexdigest()
                lease = BUILD_ATTESTATION._lease(tool, digest, 1024, "test tool")
                if mutation == "replace":
                    raw = tool.read_bytes()
                    tool.unlink()
                    tool.write_bytes(raw)
                    tool.chmod(0o400)
                elif mutation == "world-writable":
                    tool.chmod(0o666)
                elif mutation == "link-count":
                    os.link(tool, root / "second-name")
                else:
                    tool.chmod(0o600)
                    time.sleep(0.002)
                    tool.chmod(0o400)
                with self.assertRaisesRegex(ValueError, "changed during protected validation"):
                    BUILD_ATTESTATION._close_leases([lease], verify=True)

    def test_leased_validator_executes_fd_when_parent_path_is_swapped_and_restored(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory).resolve()
            live = root / "live"
            saved = root / "saved"
            live.mkdir()
            trusted = live / "validator.sh"
            trusted.write_text("#!/bin/bash\nprintf trusted\n", encoding="utf-8")
            trusted.chmod(0o700)
            lease = BUILD_ATTESTATION._lease_current(trusted, 4096, "test validator")
            live.rename(saved)
            live.mkdir()
            marker = root / "malicious-executed"
            hostile = live / "validator.sh"
            hostile.write_text(
                f"#!/bin/bash\nprintf hostile > {marker}\n",
                encoding="utf-8",
            )
            hostile.chmod(0o700)
            try:
                output_digest = BUILD_ATTESTATION._run_validator(
                    ["/usr/bin/bash", BUILD_ATTESTATION._lease_fd_path(lease)],
                    {"PATH": "/usr/bin:/bin", "LANG": "C", "LC_ALL": "C"},
                    "parent-swap hostile validator",
                    10,
                    pass_fds=(lease["descriptor"],),
                )
                self.assertEqual(hashlib.sha256(b"trusted").hexdigest(), output_digest)
                self.assertFalse(marker.exists())
            finally:
                hostile.unlink(missing_ok=True)
                live.rmdir()
                saved.rename(live)
            BUILD_ATTESTATION._close_leases([lease], verify=True)

    def test_github_provenance_ignores_proxy_and_ssl_environment(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory).resolve()
            token = root / "token"
            token.write_text("a" * 32, encoding="ascii")
            token.chmod(0o600)
            observed_handlers: list[object] = []
            sentinel_context = object()

            class Opener:
                pass

            def record_opener(*handlers: object) -> Opener:
                observed_handlers.extend(handlers)
                return Opener()

            hostile = {
                "HTTPS_PROXY": "http://attacker.invalid:8080",
                "ALL_PROXY": "http://attacker.invalid:8081",
                "SSL_CERT_FILE": str(root / "attacker-ca.pem"),
                "SSL_CERT_DIR": str(root / "attacker-ca"),
            }
            with mock.patch.dict(os.environ, hostile, clear=False), mock.patch.object(
                TWO_GREEN_SIGNER.ssl,
                "create_default_context",
                return_value=sentinel_context,
            ) as create_context, mock.patch.object(
                TWO_GREEN_SIGNER,
                "build_opener",
                side_effect=record_opener,
            ):
                TWO_GREEN_SIGNER.GitHubApiClient(token)
            create_context.assert_called_once_with(
                cafile=str(TWO_GREEN_SIGNER.SYSTEM_CA_BUNDLE)
            )
            proxy = next(
                handler for handler in observed_handlers
                if isinstance(handler, TWO_GREEN_SIGNER.ProxyHandler)
            )
            https = next(
                handler for handler in observed_handlers
                if isinstance(handler, TWO_GREEN_SIGNER.HTTPSHandler)
            )
            self.assertEqual({}, proxy.proxies)
            self.assertIs(sentinel_context, https._context)

    def test_github_provenance_rejects_ssl_key_logging_without_creating_log(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory).resolve()
            token = root / "token"
            token.write_text("a" * 32, encoding="ascii")
            token.chmod(0o600)
            key_log = root / "tls-session-keys.log"
            with mock.patch.dict(
                os.environ, {"SSLKEYLOGFILE": str(key_log)}, clear=False
            ), self.assertRaisesRegex(ValueError, "SSLKEYLOGFILE is forbidden"):
                TWO_GREEN_SIGNER.GitHubApiClient(token)
            self.assertFalse(key_log.exists())

    def test_github_provenance_rejects_downgrade_credentials_and_ports(self) -> None:
        for url in (
            "http://api.github.com/repos/example",
            "https://token@api.github.com/repos/example",
            "https://api.github.com:444/repos/example",
        ):
            with self.subTest(url=url), self.assertRaisesRegex(ValueError, "canonical HTTPS"):
                TWO_GREEN_SIGNER._https_origin(url)
        self.assertEqual(
            ("https", "api.github.com", 443),
            TWO_GREEN_SIGNER._https_origin("https://api.github.com/repos/example"),
        )
        self.assertLessEqual(TWO_GREEN_SIGNER._SafeRedirect.max_redirections, 10)
        request = TWO_GREEN_SIGNER.Request(
            "https://api.github.com/repos/example",
            headers={"Authorization": "Bearer must-not-cross-origin"},
        )
        headers = Message()
        headers["Location"] = "https://objects.githubusercontent.com/proof.zip"
        redirected = TWO_GREEN_SIGNER._SafeRedirect().redirect_request(
            request,
            None,
            302,
            "Found",
            headers,
            "https://objects.githubusercontent.com/proof.zip",
        )
        self.assertIsNotNone(redirected)
        self.assertIsNone(redirected.get_header("Authorization"))

    def test_aab_hygiene_rejects_protected_path_and_environment_markers(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory).resolve()
            for marker in (
                b"CHUMMER_ANDROID_BUILD_ATTESTATION_PRIVATE_KEY",
                b"ANDROID_API36_TWO_GREEN_RELEASE_APPROVAL.generated.json",
                b"/protected/two-green-approval.json",
            ):
                with self.subTest(marker=marker):
                    aab = root / "test.aab"
                    with zipfile.ZipFile(aab, "w") as archive:
                        archive.writestr("base/assets/value.bin", b"prefix" + marker + b"suffix")
                    with self.assertRaisesRegex(ValueError, "protected release input"):
                        HYGIENE.verify(aab, [Path("/protected/two-green-approval.json")])

    def test_prepare_scrubs_signing_environment_before_first_child(self) -> None:
        script = (REPO / "scripts/prepare-release-inputs.sh").read_text(encoding="utf-8")
        scrub = script.index("for protected_release_variable in")
        first_child = script.index('release_version_pair="$(python3')
        self.assertLess(scrub, first_child)
        prefix = script[:first_child]
        for name in (
            "AndroidSigningKeyStore",
            "ChummerAndroidSigningStorePass",
            "ChummerAndroidSigningKeyPass",
            "ChummerAndroidSigningKeyAlias",
            "CHUMMER_ANDROID_RELEASE_APPROVER_PRIVATE_KEY",
            "CHUMMER_ANDROID_BUILD_ATTESTATION_PRIVATE_KEY",
            "CHUMMER_ANDROID_GITHUB_PROVENANCE_TOKEN_FILE",
            "CHUMMER_DOTNET",
            "SSLKEYLOGFILE",
        ):
            self.assertIn(name, prefix)
        handoff = script[script.index("{\n  printf 'export CHUMMER_ANDROID_RELEASE_PACKAGE_AUTHORITY") :]
        self.assertNotIn("TWO_GREEN_ELIGIBILITY_RECEIPT=%q", handoff)
        self.assertNotIn("TWO_GREEN_RELEASE_APPROVAL=%q", handoff)
        end_marker = "unset CHUMMER_DOTNET\n"
        scrub_program = script[: script.index(end_marker) + len(end_marker)]
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory).resolve()
            probe = root / "probe.sh"
            observed = root / "observed.env"
            probe.write_text(scrub_program + '\nenv > "$1"\n', encoding="utf-8")
            environment = {"PATH": "/usr/bin:/bin"}
            for name in (
                "CHUMMER_ANDROID_TWO_GREEN_ELIGIBILITY_RECEIPT",
                "CHUMMER_ANDROID_TWO_GREEN_RELEASE_APPROVAL",
                "CHUMMER_DOTNET",
                "SSLKEYLOGFILE",
                "PYTHONPATH",
                "LD_LIBRARY_PATH",
                "JAVA_TOOL_OPTIONS",
                "MSBuildSDKsPath",
            ):
                environment[name] = f"hostile-{name}"
            subprocess.run(
                ["/bin/bash", "-p", str(probe), str(observed)],
                check=True,
                env=environment,
                capture_output=True,
            )
            child = observed.read_text(encoding="utf-8")
            self.assertNotIn("hostile-", child)

            rejected = subprocess.run(
                ["/bin/bash", "-p", str(probe), str(observed)],
                check=False,
                env={
                    "PATH": "/usr/bin:/bin",
                    "CHUMMER_ANDROID_RELEASE_APPROVER_PRIVATE_KEY": "readable-key",
                },
                capture_output=True,
                text=True,
            )
            self.assertNotEqual(0, rejected.returncode)
            self.assertIn(
                "external-signer-required-readable-signing-input-rejected",
                rejected.stderr,
            )

    def test_build_scrubs_protected_signer_environment_before_children(self) -> None:
        script = (REPO / "scripts/build-release.sh").read_text(encoding="utf-8")
        prepare = (REPO / "scripts/prepare-release-inputs.sh").read_text(encoding="utf-8")
        documentation = (REPO / "docs/PLAY_RELEASE.md").read_text(encoding="utf-8")
        self.assertTrue(script.startswith("#!/bin/bash -p\n"))
        self.assertTrue(prepare.startswith("#!/bin/bash -p\n"))
        prefix_end = "unset CHUMMER_DOTNET\n"
        prefix = script[: script.index(prefix_end) + len(prefix_end)]
        for name in (
            "CHUMMER_ANDROID_RELEASE_APPROVER_PRIVATE_KEY",
            "CHUMMER_ANDROID_BUILD_ATTESTATION_PRIVATE_KEY",
            "CHUMMER_ANDROID_GITHUB_PROVENANCE_TOKEN_FILE",
            "CHUMMER_DOTNET",
            "SSLKEYLOGFILE",
        ):
            self.assertIn(name, prefix)
        self.assertIn('unset "$release_secret_variable"', prefix)
        self.assertIn("non-authoritative unsigned builder", prefix)
        self.assertIn("external-signer-required-readable-signing-input-rejected", prefix)
        self.assertNotIn("protected-process-supervisor-required", script)
        self.assertNotIn("ptrace_scope", script)
        self.assertNotIn("PR_SET_DUMPABLE", script)
        self.assertNotIn("run_protected_android_release.py", script)
        self.assertNotIn("run_protected_android_release.py", documentation)
        self.assertIn("separate, not-yet-implemented transaction", documentation)
        self.assertIn("sole secret boundary", documentation)
        self.assertIn("make no impossible claim", documentation)
        self.assertIn("/usr/bin/env -i", documentation)
        self.assertIn("full JDK, .NET SDK/workload/\nMSBuild, and Android SDK closure", documentation)

        # A caller may carry arbitrary non-credential data, but every child is
        # created from clean_exec's explicit allowlist.
        start = script.index('release_child_home=""')
        end = script.index("\n# Every child starts", start)
        clean_exec_program = script[start:end]
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory).resolve()
            probe = root / "clean-child.sh"
            observed = root / "child.env"
            probe.write_text(
                "#!/bin/bash -p\nset -euo pipefail\n"
                + clean_exec_program
                + '\nclean_exec /usr/bin/env > "$1"\n',
                encoding="utf-8",
            )
            subprocess.run(
                ["/bin/bash", "-p", str(probe), str(observed)],
                check=True,
                capture_output=True,
                env={
                    "PATH": "/usr/bin:/bin",
                    "UNRELATED_CALLER_SECRET": "must-not-reach-child",
                    "CHUMMER_ANDROID_REVISION": "a" * 40,
                    "CHUMMER_ANDROID_RELEASE_OFFLINE_NUGET_FEED": "/private/transport-only",
                    "HTTPS_PROXY": "must-not-reach-child",
                    "NUGET_PLUGIN_PATHS": "must-not-reach-child",
                    "RestoreSources": "must-not-reach-child",
                },
            )
            child_environment = observed.read_text(encoding="utf-8")
            self.assertNotIn("UNRELATED_CALLER_SECRET", child_environment)
            self.assertNotIn("must-not-reach-child", child_environment)
            self.assertIn("CHUMMER_ANDROID_REVISION=" + "a" * 40, child_environment)
            for rejected in ("CHUMMER_ANDROID_RELEASE_OFFLINE_NUGET_FEED", "HTTPS_PROXY",
                             "NUGET_PLUGIN_PATHS", "RestoreSources"):
                self.assertNotIn(rejected, child_environment)

        for module_name in (
            "sign_android_release_build_attestation.py",
            "sign_api36_two_green_release_approval.py",
        ):
            module = REPO / "scripts" / module_name
            self.assertFalse(os.access(module, os.X_OK))
            self.assertTrue(module.read_text(encoding="utf-8").startswith("#!/usr/bin/python3\n"))


class TrustedToolchainTreeTests(unittest.TestCase):
    """Real filesystem trees; modeled UID/ancestor custody is NOT deployment proof."""

    def setUp(self) -> None:
        temporary = tempfile.TemporaryDirectory(prefix="android-trusted-tree-")
        self.addCleanup(temporary.cleanup)
        self.root = Path(temporary.name).resolve() / "tree"
        self.root.mkdir(mode=0o755)
        self.overrides: dict[Path, dict] = {}
        self.frozen_times: dict[tuple[int, int], tuple[int, int]] = {}
        real_stat, real_fstat, real_scandir = Path.stat, os.fstat, os.scandir
        real_readlink = os.readlink
        parents = set(self.root.parents)

        def metadata(value, path=None):
            fields = {name: getattr(value, name) for name in dir(value) if name.startswith("st_")}
            fields["st_uid"] = 0
            if path in parents:
                fields["st_mode"] &= ~0o022
            times = self.frozen_times.get((value.st_dev, value.st_ino))
            if times is not None:
                fields["st_mtime_ns"], fields["st_ctime_ns"] = times
            fields.update(self.overrides.get(path, {}))
            return SimpleNamespace(**fields)

        def path_stat(path, *, follow_symlinks=True):
            return metadata(real_stat(path, follow_symlinks=follow_symlinks), path)

        class Entry:
            def __init__(entry_self, value, directory):
                entry_self.value, entry_self.name = value, value.name
                entry_self.path = directory / value.name

            def stat(entry_self, *, follow_symlinks=True):
                return metadata(entry_self.value.stat(follow_symlinks=follow_symlinks), entry_self.path)

        @contextmanager
        def scandir(path):
            directory = Path(real_readlink(f"/proc/self/fd/{path}")) if isinstance(path, int) else Path(path)
            with real_scandir(path) as iterator:
                yield (Entry(entry, directory) for entry in iterator)

        for patch in (mock.patch.object(Path, "stat", path_stat),
                      mock.patch.object(os, "fstat", lambda fd: metadata(real_fstat(fd))),
                      mock.patch.object(os, "scandir", scandir)):
            patch.start()
            self.addCleanup(patch.stop)

    def write(self, name: str, content: bytes = b"payload") -> Path:
        path = self.root / name
        path.parent.mkdir(parents=True, exist_ok=True)
        for parent in path.parents:
            if not parent.is_relative_to(self.root):
                break
            parent.chmod(0o755)
        path.write_bytes(content)
        path.chmod(0o644)
        return path

    def digest(self):
        return BUILD_ATTESTATION._trusted_tree_digest(self.root, "fixture toolchain")

    def test_trusted_executable_admission_bounds_precede_content_reads(self) -> None:
        for case in ("owner", "ancestor-owner", "writable", "ancestor-writable",
                     "symlink", "directory", "fifo", "empty", "large", "nonexec"):
            with self.subTest(case=case):
                target = self.write("tool", b"tool")
                target.chmod(0o755)
                if case in ("owner", "ancestor-owner"):
                    self.overrides[target if case == "owner" else self.root.parent] = {"st_uid": 1000}
                elif case in ("writable", "ancestor-writable"):
                    path = target if case == "writable" else self.root.parent
                    self.overrides[path] = {"st_mode": path.stat().st_mode | 0o020}
                elif case in ("symlink", "directory", "fifo"):
                    target.unlink()
                    if case == "symlink":
                        target.symlink_to(self.write("other"))
                    elif case == "directory":
                        target.mkdir()
                    else:
                        os.mkfifo(target, 0o700)
                elif case == "empty":
                    target.write_bytes(b"")
                elif case == "large":
                    target.write_bytes(b"oversized")
                else:
                    target.chmod(0o644)
                with mock.patch.object(os, "read", side_effect=AssertionError("not admitted")):
                    with self.assertRaises(ValueError):
                        BUILD_ATTESTATION._trusted_tool_sha256(target, "fixture executable", 4)
                self.overrides.clear()
                if case == "directory":
                    target.rmdir()
                else:
                    target.unlink()
        target = self.write("tool", b"tool")
        target.chmod(0o755)
        for limit in (0, -1, True, 4.0):
            with self.subTest(limit=limit), self.assertRaises(ValueError):
                BUILD_ATTESTATION._trusted_tool_sha256(target, "fixture executable", limit)

    def test_trusted_executable_rechecks_metadata_after_admission(self) -> None:
        target = self.write("tool")
        target.chmod(0o755)
        real_tool = BUILD_ATTESTATION._trusted_tool

        def change_after_admission(path, root, label):
            result = real_tool(path, root, label)
            self.overrides[path] = {"st_uid": 1000}
            return result

        with mock.patch.object(BUILD_ATTESTATION, "_trusted_tool", change_after_admission):
            with self.assertRaisesRegex(ValueError, "bounded root-owned"):
                BUILD_ATTESTATION._trusted_tool_sha256(target, "fixture executable", 128)

    def test_trusted_executable_changed_fd_bytes_and_ancestry_rejected(self) -> None:
        for change in ("replace", "grow", "rewrite", "fd", "ancestor"):
            with self.subTest(change=change):
                target = self.write("tool", b"original")
                target.chmod(0o755)
                before = target.stat()
                self.frozen_times[(before.st_dev, before.st_ino)] = (before.st_mtime_ns, before.st_ctime_ns)
                real_read, real_fstat = os.read, os.fstat
                changed = False

                def mutate_after_read(fd, count):
                    nonlocal changed
                    chunk = real_read(fd, count)
                    if chunk and not changed:
                        changed = True
                        if change == "replace":
                            os.replace(self.write("new", b"original"), target)
                        elif change == "grow":
                            target.write_bytes(b"more than original")
                        elif change == "rewrite":
                            target.write_bytes(b"replaced")
                            self.assertEqual(BUILD_ATTESTATION._lease_identity(before),
                                             BUILD_ATTESTATION._lease_identity(target.stat()))
                        elif change == "ancestor":
                            self.overrides[self.root.parent] = {"st_mode": stat.S_IFDIR | 0o777}
                    return chunk

                def changed_fstat(fd):
                    metadata = real_fstat(fd)
                    if change == "fd" and changed:
                        metadata.st_uid = 1000
                    return metadata

                with mock.patch.object(os, "read", mutate_after_read), mock.patch.object(os, "fstat", changed_fstat):
                    with self.assertRaises(ValueError):
                        BUILD_ATTESTATION._trusted_tool_sha256(target, "fixture executable", 128)
                self.assertTrue(changed)
                self.overrides.clear()
                target.unlink()

    def test_trusted_executable_no_follow_rejects_leaf_swap(self) -> None:
        target = self.write("tool")
        target.chmod(0o755)
        other = self.write("other")
        real_open = os.open

        def swap_before_open(path, flags, *args, **kwargs):
            self.assertEqual(target, Path(path))
            self.assertTrue(flags & os.O_NOFOLLOW)
            target.unlink()
            target.symlink_to(other)
            return real_open(path, flags, *args, **kwargs)

        with mock.patch.object(os, "open", swap_before_open), mock.patch.object(
            os, "read", side_effect=AssertionError("swapped bytes must not be read")
        ):
            with self.assertRaisesRegex(ValueError, "changed"):
                BUILD_ATTESTATION._trusted_tool_sha256(target, "fixture executable", 128)

    @staticmethod
    def file_row(name: str, content: bytes, mode=0o644) -> bytes:
        return f"F\0{name}\0{mode:o}\0{len(content)}\0{hashlib.sha256(content).hexdigest()}\n".encode()

    def test_legacy_regular_rows_order_and_empty_file_are_unchanged(self) -> None:
        self.write("a/empty", b"")
        self.write("b/child", b"child")
        self.write("z", b"last")
        rows = (b"D\0a\000755\n" + b"D\0b\000755\n" + self.file_row("z", b"last")
                + self.file_row("b/child", b"child") + self.file_row("a/empty", b""))
        self.assertEqual((hashlib.sha256(rows).hexdigest(), 3, 9), self.digest())

    def test_empty_root_preserves_empty_digest(self) -> None:
        self.assertEqual((hashlib.sha256(b"").hexdigest(), 0, 0), self.digest())

    def test_real_contained_links_bind_text_and_regular_target_bytes(self) -> None:
        target = self.write("data", b"one")
        link = self.root / "link"
        link.symlink_to("data")
        self.assertEqual(0o777, stat.S_IMODE(link.lstat().st_mode))
        rows = self.file_row("data", b"one") + b"L\0link\000777\0data\n"
        first = self.digest()
        self.assertEqual((hashlib.sha256(rows).hexdigest(), 1, 3), first)
        link.unlink()
        link.symlink_to("./data")
        second = self.digest()
        self.assertNotEqual(first[0], second[0])
        target.write_bytes(b"two")
        self.assertNotEqual(second[0], self.digest()[0])

    def test_directory_absolute_and_parent_relative_links_stay_inside(self) -> None:
        self.write("actual/data", b"data")
        (self.root / "directory").symlink_to("actual", target_is_directory=True)
        (self.root / "absolute").symlink_to(self.root / "actual/data")
        (self.root / "actual/back").symlink_to("../actual/data")
        (self.root / "actual/root").symlink_to("..", target_is_directory=True)
        result = self.digest()
        self.assertEqual((1, 4), result[1:])  # Directory links are rows, not recursively expanded.

    def test_unresolved_and_escaping_links_fail_closed(self) -> None:
        self.write("file")
        outside = self.root.parent / "outside"
        outside.write_bytes(b"outside")
        returning = self.root.parent / "returning"
        returning.symlink_to(self.root / "file")
        targets = ("missing", "link", "../outside", str(outside), str(returning),
                   "../tree/file", "file/", "file/.")
        link = self.root / "link"
        for target in targets:
            with self.subTest(target=target):
                link.symlink_to(target)
                with self.assertRaises(ValueError):
                    self.digest()
                link.unlink()

    def test_nested_links_cannot_traverse_outside_then_return(self) -> None:
        self.write("data")
        (self.root.parent / "return").symlink_to(self.root, target_is_directory=True)
        (self.root / "a").symlink_to("../return", target_is_directory=True)
        (self.root / "b").symlink_to("a/data")
        with self.assertRaisesRegex(ValueError, "escapes"):
            self.digest()

    def test_two_node_cycle_and_excessive_hops_are_rejected(self) -> None:
        (self.root / "a").symlink_to("b")
        (self.root / "b").symlink_to("a")
        with self.assertRaises(ValueError):
            self.digest()
        (self.root / "a").unlink()
        (self.root / "b").unlink()
        self.write("last")
        for number in range(67):
            (self.root / f"link{number:02}").symlink_to(f"link{number + 1:02}" if number < 66 else "last")
        with self.assertRaises(ValueError):
            self.digest()

    def test_foreign_owned_file_directory_link_and_target_rejected(self) -> None:
        target = self.write("nested/data")
        link = self.root / "a-link"
        link.symlink_to("nested/data")
        for path in (link, target, target.parent, self.root, self.root.parent):
            with self.subTest(path=path):
                self.overrides[path] = {"st_uid": 1001}
                with self.assertRaises(ValueError):
                    self.digest()
                self.overrides.clear()

    def test_foreign_intermediate_link_is_not_hidden_by_another_link(self) -> None:
        self.write("data")
        (self.root / "a").symlink_to("z")
        (self.root / "z").symlink_to("data")
        self.overrides[self.root / "z"] = {"st_uid": 1001}
        with self.assertRaisesRegex(ValueError, "non-root-owned"):
            self.digest()

    def test_writable_targets_and_directory_ancestors_remain_rejected(self) -> None:
        target = self.write("nested/data")
        (self.root / "a-link").symlink_to("nested/data")
        for path in (target, target.parent, self.root):
            with self.subTest(path=path):
                mode = stat.S_IMODE(path.stat().st_mode)
                path.chmod(mode | 0o020)
                with self.assertRaises(ValueError):
                    self.digest()
                path.chmod(mode)
        self.overrides[self.root.parent] = {"st_mode": stat.S_IFDIR | 0o777}
        with self.assertRaisesRegex(ValueError, "ancestry"):
            self.digest()

    def test_special_file_or_link_target_rejected_without_opening_fifo(self) -> None:
        fifo = self.root / "fifo"
        os.mkfifo(fifo, 0o600)
        (self.root / "a-link").symlink_to("fifo")
        with self.assertRaisesRegex(ValueError, "unsupported"):
            self.digest()
        (self.root / "a-link").unlink()
        with self.assertRaisesRegex(ValueError, "unsupported"):
            self.digest()

    def test_streaming_is_bounded_and_preserves_large_file_formula(self) -> None:
        content = b"x" * (2 * 1024 * 1024 + 19)
        self.write("large", content)
        real_read = os.read
        requests = []

        def bounded_read(fd, count):
            self.assertEqual(1024 * 1024, count)
            requests.append(count)
            return real_read(fd, count)

        with mock.patch.object(os, "read", bounded_read), mock.patch.object(
            Path, "read_bytes", side_effect=AssertionError("whole-file allocation forbidden")
        ):
            observed = self.digest()
        self.assertEqual((hashlib.sha256(self.file_row("large", content)).hexdigest(), 1, len(content)), observed)
        self.assertEqual(8, len(requests))  # Three chunks plus EOF, in each independent pass.

    def test_same_size_persistent_rewrite_with_identical_timestamps_rejected(self) -> None:
        target = self.write("data", b"original")
        before = target.stat()
        self.frozen_times[(before.st_dev, before.st_ino)] = (before.st_mtime_ns, before.st_ctime_ns)
        real_read = os.read
        changed = False

        def rewrite_after_read(fd, count):
            nonlocal changed
            chunk = real_read(fd, count)
            if chunk and not changed:
                changed = True
                target.write_bytes(b"replaced")
                self.assertEqual(BUILD_ATTESTATION._lease_identity(before),
                                 BUILD_ATTESTATION._lease_identity(target.stat()))
            return chunk

        with mock.patch.object(os, "read", rewrite_after_read):
            with self.assertRaisesRegex(ValueError, "changed"):
                self.digest()
        self.assertTrue(changed)
        self.assertEqual(b"replaced", target.read_bytes())

    def test_changed_file_descriptor_or_path_is_rejected(self) -> None:
        for change in ("replace", "truncate", "grow", "unlink", "symlink", "mode", "owner"):
            with self.subTest(change=change):
                target = self.write("data", b"original")
                real_read = os.read
                changed = False

                def mutate_after_read(fd, count):
                    nonlocal changed
                    chunk = real_read(fd, count)
                    if chunk and not changed:
                        changed = True
                        if change == "replace":
                            replacement = self.write("new", b"original")
                            os.replace(replacement, target)
                        elif change == "truncate":
                            target.write_bytes(b"x")
                        elif change == "grow":
                            target.write_bytes(b"more than original")
                        elif change == "unlink":
                            target.unlink()
                        elif change == "symlink":
                            target.unlink()
                            target.symlink_to("missing")
                        elif change == "mode":
                            target.chmod(0o666)
                        else:
                            self.overrides[target] = {"st_uid": 1001}
                    return chunk

                with mock.patch.object(os, "read", mutate_after_read):
                    with self.assertRaisesRegex(ValueError, "changed"):
                        self.digest()
                self.assertTrue(changed)
                self.overrides.clear()
                target.unlink(missing_ok=True)

    def test_no_follow_rejects_leaf_swap_before_open(self) -> None:
        target = self.write("data")
        real_open = os.open
        outside = self.root.parent / "outside"
        outside.write_bytes(b"must not read")
        swapped = False

        def swap_before_open(path, flags, *args, **kwargs):
            nonlocal swapped
            if Path(path) == target:
                self.assertTrue(flags & os.O_NOFOLLOW)
                target.unlink()
                target.symlink_to(outside)
                swapped = True
            return real_open(path, flags, *args, **kwargs)

        with mock.patch.object(os, "open", swap_before_open), mock.patch.object(
            os, "read", side_effect=AssertionError("swapped outside file must not be read")
        ):
            with self.assertRaisesRegex(ValueError, "changed"):
                self.digest()
        self.assertTrue(swapped)

    def test_queued_directory_swap_is_rejected_before_outside_scan(self) -> None:
        self.write("nested/data")
        directory = self.root / "nested"
        outside = self.root.parent / "outside"
        outside.mkdir()
        (outside / "untrusted").write_bytes(b"not closure bytes")
        real_open = os.open
        swapped = False

        def swap_before_open(path, flags, *args, **kwargs):
            nonlocal swapped
            if Path(path) == directory:
                self.assertTrue(flags & os.O_NOFOLLOW)
                directory.rename(self.root / "moved")
                directory.symlink_to(outside, target_is_directory=True)
                swapped = True
            return real_open(path, flags, *args, **kwargs)

        with mock.patch.object(os, "open", swap_before_open):
            with self.assertRaisesRegex(ValueError, "directory changed"):
                self.digest()
        self.assertTrue(swapped)

    def test_all_entries_bound_includes_directories_and_links_before_sorting(self) -> None:
        for number in range(6):
            (self.root / f"dir{number}").mkdir()
        real_scandir = os.scandir
        yielded = 0

        class UnsortableEntry:
            @property
            def name(entry_self):
                raise AssertionError("sorting must follow the admission bound")

        @contextmanager
        def bounded_scandir(path):
            with real_scandir(path) as iterator:
                def entries():
                    nonlocal yielded
                    for _ in iterator:
                        yielded += 1
                        self.assertLessEqual(yielded, 4)
                        yield UnsortableEntry()
                yield entries()

        with mock.patch.object(BUILD_ATTESTATION, "TRUSTED_TREE_MAX_ENTRIES", 3), mock.patch.object(
            os, "scandir", bounded_scandir
        ):
            with self.assertRaisesRegex(ValueError, "entry bound"):
                self.digest()
        self.assertEqual(4, yielded)
        for number in range(6):
            (self.root / f"dir{number}").rmdir()
        self.write("target")
        for number in range(3):
            (self.root / f"link{number}").symlink_to("target")
        with mock.patch.object(BUILD_ATTESTATION, "TRUSTED_TREE_MAX_ENTRIES", 3):
            with self.assertRaisesRegex(ValueError, "entry bound"):
                self.digest()
        with mock.patch.object(BUILD_ATTESTATION, "TRUSTED_TREE_MAX_ENTRIES", 4):
            self.assertEqual((1, 7), self.digest()[1:])


class BuilderOnlyTrustTests(unittest.TestCase):
    """Real synthetic Ed25519 signatures; no operational private keys or SDK."""

    def setUp(self) -> None:
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        self.verify = BUILD_ATTESTATION.VERIFY
        self.builder_id = self.verify.RELEASE_BUILDER_KEY_ID
        self.legacy_id = self.verify.RELEASE_APPROVER_KEY_ID
        self.approver_id = "fleet-release-approver-2026-09"
        self.keys = {}
        for name in (self.builder_id, self.legacy_id, self.approver_id):
            private, public = self.root / f"{name}.private.pem", self.root / f"{name}.public.pem"
            self.openssl("genpkey", "-algorithm", "ED25519", "-out", str(private))
            private.chmod(0o600)
            self.openssl("pkey", "-in", str(private), "-pubout", "-out", str(public))
            self.keys[name] = (private, public, hashlib.sha256(public.read_bytes()).hexdigest())
        for field, value in (
            ("RELEASE_APPROVER_PUBLIC_KEY", self.keys[self.legacy_id][1]),
            ("RELEASE_APPROVER_PUBLIC_KEY_SHA256", self.keys[self.legacy_id][2]),
            ("RELEASE_BUILDER_ONLY_KEYS", {self.builder_id: self.keys[self.builder_id][1:]}),
            ("RELEASE_APPROVAL_ONLY_KEYS", {self.approver_id: self.keys[self.approver_id][1:]}),
        ):
            patcher = mock.patch.object(self.verify, field, value)
            patcher.start()
            self.addCleanup(patcher.stop)

    @staticmethod
    def openssl(*args: str) -> bytes:
        return subprocess.run(["/usr/bin/openssl", *args], check=True, capture_output=True,
            timeout=20, env={"PATH": "/usr/bin:/bin", "LANG": "C", "LC_ALL": "C"}).stdout

    def payload(self, key_id: str, *, external: bool = False) -> dict:
        value = {
            "contractName": "chummer.android.external-release-signer-attestation/v1" if external else BUILD_ATTESTATION.CONTRACT,
            "algorithm": "ed25519", "keyId": key_id, "role": BUILD_ATTESTATION.ROLE,
            "attestationScope": BUILD_ATTESTATION.SCOPE,
            "publicationAuthorized": False, "googlePlayUploadAuthorized": False,
        }
        if not external:
            value["signingAuthorized"] = False
        return value

    def signature(self, value: dict, signing_key_id: str) -> str:
        payload = self.root / "payload.json"
        payload.write_bytes(self.verify._canonical_json_bytes(value))
        raw = self.openssl("pkeyutl", "-sign", "-inkey", str(self.keys[signing_key_id][0]),
                           "-rawin", "-in", str(payload))
        return base64.b64encode(raw).decode("ascii")

    def test_each_builder_format_uses_only_the_selected_key_and_legacy_default_stays_old(self) -> None:
        for external in (False, True):
            for key_id in (self.legacy_id, self.builder_id):
                value = self.payload(key_id, external=external)
                for signer in self.keys:
                    with self.subTest(external=external, key_id=key_id, signer=signer):
                        signature = self.signature(value, signer)
                        if signer == key_id:
                            self.verify._verify_ed25519_signature(value, signature, label="test", builder_key_id=key_id)
                        else:
                            with self.assertRaisesRegex(ValueError, "signature is invalid"):
                                self.verify._verify_ed25519_signature(value, signature, label="test", builder_key_id=key_id)
        value = self.payload(self.legacy_id)
        self.verify._verify_ed25519_signature(value, self.signature(value, self.legacy_id), label="historical")
        with self.assertRaisesRegex(ValueError, "signature is invalid"):
            self.verify._verify_ed25519_signature(value, self.signature(value, self.builder_id), label="historical")

    def test_unknown_ids_algorithms_roles_and_authority_fail_before_key_or_openssl_access(self) -> None:
        cases = []
        for key_id in (None, [], {}, 7, True, "unknown", self.approver_id):
            value = self.payload(key_id)
            cases.append((value, {"builder_key_id": key_id}))
        for field, wrong in (
            ("algorithm", "rsa"), ("contractName", self.verify.RELEASE_APPROVAL_CONTRACT),
            ("role", self.verify.RELEASE_APPROVER_ROLE), ("attestationScope", self.verify.RELEASE_APPROVAL_SCOPE),
            ("keyId", self.legacy_id), ("publicationAuthorized", True),
            ("googlePlayUploadAuthorized", True), ("signingAuthorized", None),
        ):
            value = {**self.payload(self.builder_id), field: wrong}
            cases.append((value, {"builder_key_id": self.builder_id}))
        cases.append(({**self.payload(self.builder_id, external=True), "signingAuthorized": False}, {"builder_key_id": self.builder_id}))
        cases.append((self.payload(self.builder_id), {"builder_key_id": self.builder_id, "approval_key_id": self.approver_id}))
        approval = {
            "contractName": self.verify.RELEASE_APPROVAL_CONTRACT, "algorithm": "ed25519",
            "keyId": self.builder_id, "role": self.verify.RELEASE_APPROVER_ROLE,
            "approvalScope": self.verify.RELEASE_APPROVAL_SCOPE, "signingAuthorized": False,
            "publicationAuthorized": False, "googlePlayUploadAuthorized": False,
        }
        cases.append((approval, {"approval_key_id": self.builder_id}))
        with mock.patch.object(self.verify, "_stable_bytes") as reader, mock.patch.object(self.verify.subprocess, "run") as runner:
            for value, selection in cases:
                with self.subTest(value=value, selection=selection), self.assertRaises(ValueError):
                    self.verify._verify_ed25519_signature(value, "invalid", label="test", **selection)
            reader.assert_not_called()
            runner.assert_not_called()

    def test_builder_pem_capture_is_digest_checked_and_not_reopened(self) -> None:
        value = self.payload(self.builder_id)
        signature = self.signature(value, self.builder_id)
        public = self.keys[self.builder_id][1]
        original = public.read_bytes()
        real_reader = self.verify._stable_bytes
        def replace_after_capture(path, **kwargs):
            raw = real_reader(path, **kwargs)
            if path == public:
                public.write_bytes(self.keys[self.approver_id][1].read_bytes())
            return raw
        with mock.patch.object(self.verify, "_stable_bytes", side_effect=replace_after_capture):
            self.verify._verify_ed25519_signature(value, signature, label="test", builder_key_id=self.builder_id)
        with self.assertRaisesRegex(ValueError, "digest differs"):
            self.verify._verify_ed25519_signature(value, signature, label="test", builder_key_id=self.builder_id)
        public.write_bytes(original)
        link = self.root / "linked-public.pem"
        link.symlink_to(public)
        with mock.patch.dict(self.verify.RELEASE_BUILDER_ONLY_KEYS, {self.builder_id: (link, self.keys[self.builder_id][2])}):
            with self.assertRaises((ValueError, OSError)):
                self.verify._verify_ed25519_signature(value, signature, label="test", builder_key_id=self.builder_id)

    def test_checked_in_builder_public_key_has_admitted_pem_and_spki_and_no_approval_role(self) -> None:
        # Load untouched production pins rather than the synthetic fixture map.
        production = load(REPO / "scripts/verify_api36_two_green_release_eligibility.py", "actual_builder_public_pins")
        self.assertEqual("fleet-release-builder-2026-09", production.RELEASE_BUILDER_KEY_ID)
        self.assertEqual({production.RELEASE_BUILDER_KEY_ID}, set(production.RELEASE_BUILDER_ONLY_KEYS))
        public, digest = production._release_builder_key(production.RELEASE_BUILDER_KEY_ID)
        raw = public.read_bytes()
        self.assertEqual("ef44c5b7fcadaf0f115b5f0e0e7b1a65edb322bb002faf980acb654a5db8caaf", digest)
        self.assertEqual(digest, hashlib.sha256(raw).hexdigest())
        der = self.openssl("pkey", "-pubin", "-in", str(public), "-outform", "DER")
        self.assertEqual("MCowBQYDK2VwAyEAdXOvq6FjTeUUqxBWMCrF+OJGqihEANWatNQ96HmLNEc=", base64.b64encode(der).decode("ascii"))
        self.assertEqual("41b44078d037fafd85b091b967959f77a7a4aa9f160d03749fa49889a8b1b156", hashlib.sha256(der).hexdigest())
        with self.assertRaisesRegex(ValueError, "not admitted"):
            production._release_approval_key(production.RELEASE_BUILDER_KEY_ID)
        self.assertEqual("local-release-builder-2026", production.RELEASE_APPROVER_KEY_ID)
        self.assertEqual({"fleet-release-approver-2026-09"}, set(production.RELEASE_APPROVAL_ONLY_KEYS))


if __name__ == "__main__":
    unittest.main()
