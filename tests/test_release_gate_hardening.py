from __future__ import annotations

from email.message import Message
import importlib.util
import hashlib
import json
import os
from pathlib import Path
import re
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
            ):
                observation = BUILD_ATTESTATION.materialize_java_toolchain_observation(
                    java_sdk, dotnet, output
                )
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
                },
            )
            child_environment = observed.read_text(encoding="utf-8")
            self.assertNotIn("UNRELATED_CALLER_SECRET", child_environment)
            self.assertNotIn("must-not-reach-child", child_environment)
            self.assertIn("CHUMMER_ANDROID_REVISION=" + "a" * 40, child_environment)

        for module_name in (
            "sign_android_release_build_attestation.py",
            "sign_api36_two_green_release_approval.py",
        ):
            module = REPO / "scripts" / module_name
            self.assertFalse(os.access(module, os.X_OK))
            self.assertTrue(module.read_text(encoding="utf-8").startswith("#!/usr/bin/python3\n"))


if __name__ == "__main__":
    unittest.main()
