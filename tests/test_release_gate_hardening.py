from __future__ import annotations

from email.message import Message
import importlib.util
import hashlib
import json
import os
from pathlib import Path
import subprocess
import tempfile
import time
import unittest
from unittest import mock
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
    @staticmethod
    def authentication_key(root: Path) -> Path:
        key = root / "capture-authentication.key"
        key.write_bytes(b"a" * CAPTURE.AUTHENTICATION_KEY_BYTES)
        key.chmod(0o600)
        return key

    @staticmethod
    def authentication_digest(key: Path) -> str:
        return hashlib.sha256(key.read_bytes()).hexdigest()

    def test_capture_promotes_exact_descriptor_bytes_and_sidecar(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory).resolve()
            artifact_dir = root / "artifacts"
            capture_dir = artifact_dir / ".capture"
            artifact_dir.mkdir(mode=0o700)
            capture_dir.mkdir(mode=0o700)
            aab = root / "input.aab"
            graph = root / "input.json"
            key = self.authentication_key(root)
            aab.write_bytes(b"signed-aab")
            graph.write_bytes(b'{"publicationAuthorized":false}\n')
            CAPTURE.capture(
                aab, graph, capture_dir, "release.aab", "graph.json",
                key, self.authentication_digest(key),
            )
            output_aab = artifact_dir / "release.aab"
            output_graph = artifact_dir / "graph.json"
            output_sidecar = artifact_dir / "release.aab.sha256"
            CAPTURE.promote(
                capture_dir,
                output_aab,
                output_graph,
                output_sidecar,
                key,
                self.authentication_digest(key),
            )
            self.assertNotEqual((capture_dir / "captured.aab").stat().st_ino, output_aab.stat().st_ino)
            self.assertEqual((capture_dir / "captured.aab").read_bytes(), output_aab.read_bytes())
            subprocess.run(
                ["sha256sum", "--check", str(output_sidecar)],
                cwd=root,
                check=True,
                capture_output=True,
            )

    def test_capture_rejects_source_changed_during_descriptor_copy(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory).resolve()
            source = root / "source.aab"
            target = root / "target.aab"
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
                with self.assertRaisesRegex(ValueError, "changed while being captured"):
                    CAPTURE._stable_capture(
                        source,
                        target,
                        limit=4 * CAPTURE.CHUNK,
                        mode=0o400,
                    )
            self.assertFalse(target.exists())

    def test_capture_rejects_captured_inode_substitution_before_promotion(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory).resolve()
            artifact_dir = root / "artifacts"
            capture_dir = artifact_dir / ".capture"
            artifact_dir.mkdir(mode=0o700)
            capture_dir.mkdir(mode=0o700)
            aab = root / "input.aab"
            graph = root / "graph.json"
            key = self.authentication_key(root)
            aab.write_bytes(b"signed-aab")
            graph.write_bytes(b'{"publicationAuthorized":false}\n')
            CAPTURE.capture(
                aab, graph, capture_dir, "release.aab", "graph.json",
                key, self.authentication_digest(key),
            )
            captured = capture_dir / "captured.aab"
            captured.unlink()
            captured.write_bytes(b"substituted")
            with self.assertRaisesRegex(ValueError, "identity changed"):
                CAPTURE.promote(
                    capture_dir,
                    artifact_dir / "release.aab",
                    artifact_dir / "graph.json",
                    artifact_dir / "release.aab.sha256",
                    key,
                    self.authentication_digest(key),
                )
            self.assertFalse((artifact_dir / "release.aab").exists())

    def test_capture_rejects_self_authorized_replacement_receipt(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory).resolve()
            artifact_dir = root / "artifacts"
            capture_dir = artifact_dir / ".capture"
            artifact_dir.mkdir(mode=0o700)
            capture_dir.mkdir(mode=0o700)
            key = self.authentication_key(root)
            aab = root / "input.aab"
            graph = root / "graph.json"
            aab.write_bytes(b"signed-aab")
            graph.write_bytes(b'{"publicationAuthorized":false}\n')
            CAPTURE.capture(
                aab, graph, capture_dir, "release.aab", "graph.json",
                key, self.authentication_digest(key),
            )
            receipt_path = capture_dir / "capture-receipt.json"
            receipt = json.loads(receipt_path.read_text(encoding="utf-8"))
            evil = capture_dir / "evil.aab"
            evil.write_bytes(b"unvalidated-evil-aab")
            evil.chmod(0o444)
            receipt["outputs"]["aab"].update({
                "capturedName": evil.name,
                "capturedIdentity": CAPTURE._identity(evil.stat()),
                "sha256": hashlib.sha256(evil.read_bytes()).hexdigest(),
                "sizeBytes": evil.stat().st_size,
            })
            receipt["authentication"]["tagHex"] = "0" * 64
            receipt_path.chmod(0o600)
            receipt_path.write_bytes(CAPTURE._canonical_json(receipt))
            receipt_path.chmod(0o400)
            with self.assertRaisesRegex(ValueError, "authentication failed"):
                CAPTURE.promote(
                    capture_dir,
                    artifact_dir / "release.aab",
                    artifact_dir / "graph.json",
                    artifact_dir / "release.aab.sha256",
                    key,
                    self.authentication_digest(key),
                )

    def test_capture_rejects_replaced_authentication_key_even_with_matching_receipt(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory).resolve()
            artifact_dir = root / "artifacts"
            capture_dir = artifact_dir / ".capture"
            artifact_dir.mkdir(mode=0o700)
            capture_dir.mkdir(mode=0o700)
            key = self.authentication_key(root)
            expected_key_digest = self.authentication_digest(key)
            aab = root / "input.aab"
            graph = root / "graph.json"
            aab.write_bytes(b"signed-aab")
            graph.write_bytes(b'{"publicationAuthorized":false}\n')
            CAPTURE.capture(
                aab, graph, capture_dir, "release.aab", "graph.json",
                key, expected_key_digest,
            )
            receipt_path = capture_dir / "capture-receipt.json"
            receipt = json.loads(receipt_path.read_text(encoding="utf-8"))
            key.write_bytes(b"b" * CAPTURE.AUTHENTICATION_KEY_BYTES)
            key.chmod(0o600)
            unsigned = {
                name: value for name, value in receipt.items()
                if name != "authentication"
            }
            receipt["authentication"] = CAPTURE._authentication(unsigned, key.read_bytes())
            receipt_path.chmod(0o600)
            receipt_path.write_bytes(CAPTURE._canonical_json(receipt))
            receipt_path.chmod(0o400)
            with self.assertRaisesRegex(ValueError, "not one stable 256-bit key"):
                CAPTURE.promote(
                    capture_dir,
                    artifact_dir / "release.aab",
                    artifact_dir / "graph.json",
                    artifact_dir / "release.aab.sha256",
                    key,
                    expected_key_digest,
                )

    def test_capture_rejects_aab_sidecar_and_metadata_drift(self) -> None:
        for target_name, mutation in (
            ("captured.aab", "replace"),
            ("captured-output.sha256", "replace"),
            ("captured.aab", "world-writable"),
            ("captured.aab", "link-count"),
            ("captured.aab", "ctime-only"),
        ):
            with self.subTest(target_name=target_name, mutation=mutation), tempfile.TemporaryDirectory() as directory:
                root = Path(directory).resolve()
                artifact_dir = root / "artifacts"
                capture_dir = artifact_dir / ".capture"
                artifact_dir.mkdir(mode=0o700)
                capture_dir.mkdir(mode=0o700)
                key = self.authentication_key(root)
                aab = root / "input.aab"
                graph = root / "graph.json"
                aab.write_bytes(b"signed-aab")
                graph.write_bytes(b'{"publicationAuthorized":false}\n')
                CAPTURE.capture(
                    aab, graph, capture_dir, "release.aab", "graph.json",
                    key, self.authentication_digest(key),
                )
                target = capture_dir / target_name
                if mutation == "replace":
                    raw = target.read_bytes()
                    target.unlink()
                    target.write_bytes(raw)
                elif mutation == "world-writable":
                    target.chmod(0o666)
                elif mutation == "link-count":
                    os.link(target, root / "extra-link")
                else:
                    original_mode = target.stat().st_mode & 0o777
                    target.chmod(0o600)
                    target.chmod(original_mode)
                with self.assertRaisesRegex(ValueError, "identity changed"):
                    CAPTURE.promote(
                        capture_dir,
                        artifact_dir / "release.aab",
                        artifact_dir / "graph.json",
                        artifact_dir / "release.aab.sha256",
                        key,
                        self.authentication_digest(key),
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
            attacker_public = root / "attacker.public.pem"
            subprocess.run(
                ["openssl", "genpkey", "-algorithm", "ED25519", "-out", str(attacker_private)],
                check=True,
                capture_output=True,
            )
            attacker_private.chmod(0o600)
            subprocess.run(
                ["openssl", "pkey", "-in", str(attacker_private), "-pubout", "-out", str(attacker_public)],
                check=True,
                capture_output=True,
            )
            authority = root / "attacker-toolchain-authority.json"
            with mock.patch.object(
                BUILD_ATTESTATION, "_trusted_tool_root", side_effect=lambda path, _label: path
            ), mock.patch.object(
                BUILD_ATTESTATION, "_trusted_tool", side_effect=lambda path, _root, _label: path
            ), mock.patch.object(
                BUILD_ATTESTATION, "_java_version_digest", return_value="1" * 64
            ), mock.patch.object(
                BUILD_ATTESTATION, "_dotnet_version_digest", return_value="2" * 64
            ), mock.patch.object(
                BUILD_ATTESTATION.VERIFY, "RELEASE_APPROVER_PUBLIC_KEY", attacker_public
            ), mock.patch.object(
                BUILD_ATTESTATION.VERIFY,
                "RELEASE_APPROVER_PUBLIC_KEY_SHA256",
                hashlib.sha256(attacker_public.read_bytes()).hexdigest(),
            ):
                BUILD_ATTESTATION.sign_java_toolchain_authority(
                    java_sdk, dotnet, attacker_private, authority
                )
            with self.assertRaisesRegex(ValueError, "signature is invalid"):
                BUILD_ATTESTATION._load_trusted_java_toolchain(authority)

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
            environment = dict(os.environ)
            for name in (
                "AndroidSigningKeyStore",
                "ChummerAndroidSigningStorePass",
                "ChummerAndroidSigningKeyPass",
                "ChummerAndroidSigningKeyAlias",
                "CHUMMER_ANDROID_RELEASE_APPROVER_PRIVATE_KEY",
                "CHUMMER_ANDROID_BUILD_ATTESTATION_PRIVATE_KEY",
                "CHUMMER_ANDROID_GITHUB_PROVENANCE_TOKEN_FILE",
                "CHUMMER_ANDROID_TWO_GREEN_ELIGIBILITY_RECEIPT",
                "CHUMMER_ANDROID_TWO_GREEN_RELEASE_APPROVAL",
                "CHUMMER_DOTNET",
            ):
                environment[name] = f"hostile-{name}"
            subprocess.run(
                ["bash", str(probe), str(observed)],
                check=True,
                env=environment,
                capture_output=True,
            )
            child = observed.read_text(encoding="utf-8")
            self.assertNotIn("hostile-", child)

    def test_build_scrubs_protected_signer_environment_before_children(self) -> None:
        script = (REPO / "scripts/build-release.sh").read_text(encoding="utf-8")
        prefix_end = "unset CHUMMER_DOTNET\n"
        prefix = script[: script.index(prefix_end) + len(prefix_end)]
        for name in (
            "CHUMMER_ANDROID_RELEASE_APPROVER_PRIVATE_KEY",
            "CHUMMER_ANDROID_BUILD_ATTESTATION_PRIVATE_KEY",
            "CHUMMER_ANDROID_GITHUB_PROVENANCE_TOKEN_FILE",
            "CHUMMER_DOTNET",
        ):
            self.assertIn(f"unset {name}", prefix)
            self.assertIn(f"-u {name}", prefix)
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory).resolve()
            probe = root / "probe.sh"
            observed = root / "observed.env"
            probe.write_text(prefix + '\nenv > "$1"\n', encoding="utf-8")
            environment = dict(os.environ)
            for name in (
                "CHUMMER_ANDROID_RELEASE_APPROVER_PRIVATE_KEY",
                "CHUMMER_ANDROID_BUILD_ATTESTATION_PRIVATE_KEY",
                "CHUMMER_ANDROID_GITHUB_PROVENANCE_TOKEN_FILE",
                "CHUMMER_DOTNET",
            ):
                environment[name] = f"hostile-{name}"
            subprocess.run(
                ["bash", str(probe), str(observed)],
                check=True,
                env=environment,
                capture_output=True,
            )
            self.assertNotIn("hostile-", observed.read_text(encoding="utf-8"))


if __name__ == "__main__":
    unittest.main()
