from __future__ import annotations

import copy
from datetime import UTC, datetime, timedelta
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
from unittest import mock
import zipfile


REPO = Path(__file__).resolve().parents[1]
SCRIPT = REPO / "scripts/materialize_next_play_internal_publication_receipt.py"
SCHEMA = REPO / "play/next-internal-publication-receipt.schema.json"
BROWSER_SCHEMA = REPO / "play/next-internal-browser-readback.schema.json"
PREVIEW10_SCRIPT = REPO / "scripts/verify_play_internal_publication_receipt.py"
PREVIEW10_RECEIPT = REPO / "play/evidence/preview10-internal-publication.json"
PREVIEW10_SCHEMA = REPO / "play/release-receipt.schema.json"


def load_module(path: Path, name: str):
    spec = importlib.util.spec_from_file_location(name, path)
    assert spec is not None and spec.loader is not None
    module = importlib.util.module_from_spec(spec)
    sys.modules[name] = module
    spec.loader.exec_module(module)
    return module


def write_private(path: Path, value: object) -> Path:
    path.write_text(json.dumps(value, indent=2) + "\n", encoding="utf-8")
    path.chmod(0o600)
    return path


class NextPlayInternalPublicationReceiptTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        cls.module = load_module(SCRIPT, "materialize_next_play_internal_publication_receipt")
        cls.preview10 = load_module(
            PREVIEW10_SCRIPT, "immutable_preview10_internal_publication_verifier"
        )

    def setUp(self) -> None:
        self.temporary = tempfile.TemporaryDirectory()
        self.root = Path(self.temporary.name).resolve()
        self.version_name = "0.1.0-preview.99"
        self.version_code = 99
        self.browser = self.root / "explicit-browser-readback.json"
        self.aab = self.root / f"chummer-android-{self.version_name}-upload.aab"
        self.graph = self.root / f"chummer-android-{self.version_name}-source-graph.json"
        self.receipt = self.root / "next-internal-publication.json"
        self.two_green_receipt = self.root / "two-green-eligibility.json"
        self.two_green_approval = self.root / "two-green-release-approval.json"
        self.build_sidecar = self.root / f"{self.aab.name}.sha256"
        self.build_attestation = self.root / "release-build-attestation.json"
        self.github_token = self.root / "github-provenance.token"
        self.github_token.write_text("a" * 32, encoding="ascii")
        self.github_token.chmod(0o600)
        self.attester_private_key = self.root / "release-attester.private.pem"
        self.attester_public_key = self.root / "release-attester.public.pem"
        subprocess.run(
            ["openssl", "genpkey", "-algorithm", "ED25519", "-out", str(self.attester_private_key)],
            check=True, capture_output=True,
        )
        self.attester_private_key.chmod(0o600)
        subprocess.run(
            ["openssl", "pkey", "-in", str(self.attester_private_key), "-pubout", "-out", str(self.attester_public_key)],
            check=True, capture_output=True,
        )
        self.browser_payload = self.make_browser()
        self.graph_payload = self.make_graph()
        write_private(self.two_green_receipt, {"fixture": True})
        write_private(self.two_green_approval, {"fixture": "protected-approval"})
        self.two_green_receipt_sha256 = hashlib.sha256(
            self.two_green_receipt.read_bytes()
        ).hexdigest()
        self.two_green_binding = {
            "contractName": self.module.QUALIFICATION.TWO_GREEN.CONTRACT,
            "receiptSha256": self.two_green_receipt_sha256,
            "protectedApproval": {
                "contractName": "chummer.android.two-green-release-approval/v1",
                "keyId": "local-release-builder-2026",
                "role": "android_internal_release_approver",
                "approvalScope": "android_internal_release_preparation",
                "approvalSha256": "9" * 64,
                "receiptSha256": self.two_green_receipt_sha256,
                "provenanceValidatorSha256": "8" * 64,
                "provenanceReplaySha256": "7" * 64,
                "generatedAtUtc": "2026-09-05T10:00:00Z",
                "expiresAtUtc": "2026-09-05T16:00:00Z",
            },
            "eligibilitySha256": "a" * 64,
            "sourceCommit": self.graph_payload["repositories"][0]["commit"],
            "sourceTree": self.graph_payload["repositories"][0]["tree"],
            "versionName": self.version_name,
            "versionCode": self.version_code,
            "dependencyGraphSha256": "b" * 64,
            "environmentPolicySha256": "c" * 64,
            "buildEnvironmentCompatibilitySha256": "d" * 64,
            "journeyEnvironmentCompatibilitySha256": "e" * 64,
            "mainRunId": 123,
            "mainRunAttempt": 1,
            "mainRunConclusion": "success",
            "mainAggregateConclusion": "success",
            "environmentCompatibilityStatus": "pass",
            "eligible": True,
            "internalTestingEligible": True,
            "publicationAuthorized": False,
            "googlePlayUploadAuthorized": False,
        }
        self.original_qualification_verifier = (
            self.module.QUALIFICATION.verify_release_eligibility
        )
        self.module.QUALIFICATION.verify_release_eligibility = (
            lambda *_args, **_kwargs: copy.deepcopy(self.two_green_binding)
        )
        write_private(self.browser, self.browser_payload)
        self.write_aab(self.aab)
        write_private(self.graph, self.graph_payload)
        self.expected_source_graph_sha256 = hashlib.sha256(
            self.graph.read_bytes()
        ).hexdigest()
        self.build_sidecar.write_text(
            f"{hashlib.sha256(self.aab.read_bytes()).hexdigest()}  artifacts/{self.aab.name}\n"
            f"{self.expected_source_graph_sha256}  artifacts/{self.graph.name}\n",
            encoding="ascii",
        )
        self.build_sidecar.chmod(0o600)
        write_private(self.build_attestation, {"fixture": "signed"})
        self.build_attestation_binding = {
            "sourceCommit": self.graph_payload["repositories"][0]["commit"],
            "aab": {"sha256": hashlib.sha256(self.aab.read_bytes()).hexdigest()},
            "sourceGraph": {"sha256": self.expected_source_graph_sha256},
            "buildSidecar": {"sha256": hashlib.sha256(self.build_sidecar.read_bytes()).hexdigest()},
        }
        self.original_build_attestation_verifier = self.module.BUILD_ATTESTATION.verify
        self.original_build_attestation_qualification = self.module.BUILD_ATTESTATION.VERIFY.verify_release_eligibility
        self.original_build_attestation_public_key = self.module.BUILD_ATTESTATION.VERIFY.RELEASE_APPROVER_PUBLIC_KEY
        self.original_build_attestation_public_key_sha256 = self.module.BUILD_ATTESTATION.VERIFY.RELEASE_APPROVER_PUBLIC_KEY_SHA256
        self.module.BUILD_ATTESTATION.VERIFY.verify_release_eligibility = (
            lambda *_args, **_kwargs: copy.deepcopy(self.two_green_binding)
        )
        self.module.BUILD_ATTESTATION.VERIFY.RELEASE_APPROVER_PUBLIC_KEY = self.attester_public_key
        self.module.BUILD_ATTESTATION.VERIFY.RELEASE_APPROVER_PUBLIC_KEY_SHA256 = hashlib.sha256(self.attester_public_key.read_bytes()).hexdigest()
        self.module.BUILD_ATTESTATION.verify = (
            lambda *_args, **_kwargs: copy.deepcopy(self.build_attestation_binding)
        )

    def tearDown(self) -> None:
        self.module.QUALIFICATION.verify_release_eligibility = (
            self.original_qualification_verifier
        )
        self.module.BUILD_ATTESTATION.verify = self.original_build_attestation_verifier
        self.module.BUILD_ATTESTATION.VERIFY.verify_release_eligibility = self.original_build_attestation_qualification
        self.module.BUILD_ATTESTATION.VERIFY.RELEASE_APPROVER_PUBLIC_KEY = self.original_build_attestation_public_key
        self.module.BUILD_ATTESTATION.VERIFY.RELEASE_APPROVER_PUBLIC_KEY_SHA256 = self.original_build_attestation_public_key_sha256
        self.temporary.cleanup()

    def make_browser(self) -> dict[str, object]:
        observed = (datetime.now(UTC) - timedelta(minutes=1)).replace(
            microsecond=0
        ).isoformat().replace("+00:00", "Z")
        return {
            "contractName": self.module.BROWSER_READBACK_CONTRACT,
            "observedAtUtc": observed,
            "surface": "google_play_console_internal_testing",
            "status": "observed",
            "application": {
                "name": "Chummer",
                "packageId": self.module.PACKAGE_ID,
                "playApplicationId": "4975957268242186974",
            },
            "track": {
                "name": "internal",
                "consoleName": "Internal testing",
                "trackId": "4700678198570024687",
                "active": True,
                "joinUrl": "https://play.google.com/apps/internaltest/4700678198570024687",
            },
            "release": {
                "name": f"{self.version_code} ({self.version_name})",
                "versionCode": self.version_code,
                "versionName": self.version_name,
                "status": "Available to internal testers",
                "releasedAt": {
                    "consoleDisplay": "3 Sept 01:04",
                    "normalizedUtc": None,
                    "precision": "console_minute",
                    "timeZoneAuthority": "not_exposed_by_browser_readback",
                },
                "supportedAndroidDevices": 13550,
            },
            "fieldsObserved": list(self.module.EXPECTED_OBSERVED_FIELDS),
            "credentialOrSessionDataRecorded": False,
        }

    def make_graph(self) -> dict[str, object]:
        generator = REPO / "scripts/verify_release_source_graph.py"
        generator_raw = generator.read_bytes()
        repositories = [
            {
                "name": name,
                "role": role,
                "commit": f"{index + 1:x}" * 40,
                "tree": f"{index + 2:x}" * 40,
                "tree_sha256": f"{index + 3:x}" * 64,
                "repository": repository,
            }
            for index, (name, (role, repository)) in enumerate(
                self.module.EXPECTED_SOURCE_REPOSITORIES.items()
            )
        ]
        runtime_pins = [
            {
                "package_id": package_id,
                "version": "1.2.3-test.1",
                "sha256": f"{index + 1:x}" * 64,
                "repository": "chummer6-core",
                "commit": "3" * 40,
            }
            for index, package_id in enumerate(self.module.EXPECTED_RUNTIME_PACKAGES)
        ]
        owner_pins = []
        for index, package_id in enumerate(self.module.EXPECTED_OWNER_PACKAGES):
            owner_repository = self.module.OWNER_REPOSITORY_BY_PACKAGE[package_id]
            owner_source = next(
                row for row in repositories if row["name"] == owner_repository
            )
            owner_pins.append(
                {
                    "package_id": package_id,
                    "version": "1.2.3-test.1",
                    "sha256": f"{index + 8:x}" * 64,
                    "size_bytes": 1000 + index,
                    "owner_repository": owner_repository,
                    "source_commit": "4" * 40,
                    "source_tree": "5" * 40,
                    "source_authority": {
                        "owner_head_commit": owner_source["commit"],
                        "owner_head_tree": owner_source["tree"],
                        "relationship": "ancestor_or_equal",
                        "verification": "git-merge-base-is-ancestor-without-replace-objects",
                    },
                    "authority_receipt_sha256": "a" * 64,
                    "package_inventory_sha256": "b" * 64,
                    "package_plane_lock_sha256": "c" * 64,
                    "dependency_mode": "locked_package",
                }
            )
        closure = [
            {
                "package_id": package_id,
                "dependencies": (
                    ["Chummer.Engine.Contracts", "Chummer.Play.Contracts"]
                    if package_id == "Chummer.Run.Contracts"
                    else []
                ),
            }
            for package_id in self.module.EXPECTED_OWNER_PACKAGES
        ]
        return {
            "contractName": self.module.SOURCE_GRAPH_CONTRACT,
            "generatedAtUtc": self.browser_payload["observedAtUtc"],
            "authorityState": "local_review_required",
            "publicationAuthorized": False,
            "releaseIdentity": {
                "packageId": self.module.PACKAGE_ID,
                "versionName": self.version_name,
                "versionCode": self.version_code,
                "intentAuthority": "explicit_build_input",
                "minimumExclusiveVersionCode": 10,
            },
            "generator": {
                "path": "scripts/verify_release_source_graph.py",
                "sha256": hashlib.sha256(generator_raw).hexdigest(),
                "size_bytes": len(generator_raw),
            },
            "repositories": repositories,
            "packagePins": runtime_pins,
            "ownerPackagePins": owner_pins,
            "dependencyClosure": closure,
            "presentationSource": {
                "repository": "chummer6-ui",
                "commit": "2" * 40,
                "tree": "3" * 40,
                "source_path": "chummer-presentation",
                "authority_state": "local_review_required",
                "publication_authorized": False,
                "dependency_mode": "source_compatibility",
            },
            "doesNotAssert": list(self.module.SOURCE_GRAPH_NONCLAIMS),
        }

    @staticmethod
    def write_aab(path: Path, marker: bytes = b"bundle") -> None:
        with zipfile.ZipFile(path, "w") as archive:
            archive.writestr("BundleConfig.pb", marker)
            archive.writestr("base/manifest/AndroidManifest.xml", b"binary-manifest")
            archive.writestr("base/lib/arm64-v8a/libmonodroid.so", b"arm64")
        path.chmod(0o644)

    def materialize(self) -> dict[str, object]:
        return self.module.materialize(
            self.browser,
            self.aab,
            self.graph,
            self.receipt,
            build_sidecar_path=self.build_sidecar,
            build_attestation_path=self.build_attestation,
            two_green_receipt_path=self.two_green_receipt,
            two_green_approval_path=self.two_green_approval,
        )

    def protected_build_validation(self) -> dict[str, object]:
        build = self.module.BUILD_ATTESTATION
        validator_names = (
            "validate-aab.sh",
            "inspect_aab.py",
            "verify_release_aab_excludes_api36_proof.py",
            "verify_release_artifact_hygiene.py",
            "verify_release_source_graph.py",
        )
        return {
            "contractName": build.VALIDATION_CONTRACT,
            "status": "pass",
            "bundletoolSha256": build.EXPECTED_BUNDLETOOL_SHA256,
            "uploadCertificateSha256": build.EXPECTED_UPLOAD_CERTIFICATE_SHA256,
            "uploadCertificateFileSha256": "0" * 64,
            "javaToolAuthoritySha256": "4" * 64,
            "javaVersionOutputSha256": "5" * 64,
            "javaToolSha256": {
                "java": "6" * 64,
                "javac": "b" * 64,
                "jarsigner": "7" * 64,
                "keytool": "8" * 64,
            },
            "dotnetSha256": "9" * 64,
            "dotnetVersionOutputSha256": "a" * 64,
            "aabValidationOutputSha256": "1" * 64,
            "artifactHygieneOutputSha256": "2" * 64,
            "sourceGraphValidationOutputSha256": "3" * 64,
            "validatorSha256": {
                name: hashlib.sha256((REPO / "scripts" / name).read_bytes()).hexdigest()
                for name in validator_names
            },
            "publicationAuthorized": False,
        }

    def verify(self) -> dict[str, object]:
        return self.module.verify(
            self.receipt,
            self.aab,
            self.graph,
            build_sidecar_path=self.build_sidecar,
            build_attestation_path=self.build_attestation,
            two_green_receipt_path=self.two_green_receipt,
            two_green_approval_path=self.two_green_approval,
        )

    def test_round_trip_binds_explicit_readback_and_exact_local_outputs(self) -> None:
        result = self.materialize()
        self.assertEqual("pass", result["status"], result["failures"])
        self.assertFalse(result["publicationAuthorized"])
        self.assertEqual("none", result["authorizationScope"])
        self.assertEqual(
            "google_play_internal_testing_observation_only", result["evidenceScope"]
        )
        self.assertFalse(result["productionAuthorized"])
        self.assertEqual(0o600, self.receipt.stat().st_mode & 0o777)

        payload = json.loads(self.receipt.read_text(encoding="utf-8"))
        self.assertEqual(self.module.CONTRACT, payload["contractName"])
        self.assertFalse(payload["publicationAuthorized"])
        self.assertEqual(
            "explicit_internal_browser_readback_plus_exact_qualified_release_outputs",
            payload["evidenceClass"],
        )
        self.assertEqual(self.two_green_binding, payload["twoGreenEligibility"])
        self.assertFalse(payload["authorization"]["publicationAuthorized"])
        self.assertFalse(payload["authorization"]["productionAuthorized"])
        self.assertFalse(payload["authorization"]["uploadActionAuthorized"])
        self.assertFalse(payload["authorization"]["testerRosterMutationAuthorized"])
        self.assertEqual(
            hashlib.sha256(self.aab.read_bytes()).hexdigest(),
            payload["artifact"]["aabSha256"],
        )
        self.assertEqual(
            hashlib.sha256(self.graph.read_bytes()).hexdigest(),
            payload["artifact"]["sourceGraphSha256"],
        )
        self.assertNotIn(str(self.root), self.receipt.read_text(encoding="utf-8"))

        verified = self.verify()
        self.assertEqual("pass", verified["status"], verified["failures"])
        self.assertTrue(verified["localArtifactBytesVerified"])

    def test_signed_build_attestation_rejects_substituted_graph_and_sidecar(self) -> None:
        build = self.module.BUILD_ATTESTATION
        self.build_attestation.unlink()
        with mock.patch.object(
            build,
            "_protected_validation",
            return_value=self.protected_build_validation(),
        ), mock.patch.object(
            build.APPROVAL_SIGNER,
            "_authenticated_github_replay",
            return_value=("8" * 64, "7" * 64),
        ):
            attestation = build.sign(
                self.aab, self.graph, self.build_sidecar,
                self.two_green_receipt, self.two_green_approval,
                self.attester_private_key, self.build_attestation,
                self.github_token,
                workspace_root=self.root,
                package_authority=self.graph,
                authority_root=self.root,
                bundletool=self.graph,
                upload_certificate=self.graph,
                java_tool_authority=self.graph,
            )
        self.assertFalse(attestation["publicationAuthorized"])
        self.assertEqual(
            build.VALIDATION_CONTRACT,
            attestation["protectedValidation"]["contractName"],
        )
        verified = self.original_build_attestation_verifier(
            self.build_attestation, self.aab, self.graph, self.build_sidecar,
            self.two_green_receipt, self.two_green_approval,
        )
        self.assertEqual(self.expected_source_graph_sha256, verified["sourceGraph"]["sha256"])

        unvalidated_output = self.root / "unvalidated-build-attestation.json"
        with mock.patch.object(
            build.APPROVAL_SIGNER,
            "_authenticated_github_replay",
            return_value=("8" * 64, "7" * 64),
        ):
            with self.assertRaisesRegex(ValueError, "validation inputs are incomplete"):
                build.sign(
                    self.aab,
                    self.graph,
                    self.build_sidecar,
                    self.two_green_receipt,
                    self.two_green_approval,
                    self.attester_private_key,
                    unvalidated_output,
                    self.github_token,
                )
        self.assertFalse(unvalidated_output.exists())

        mismatched_provenance_output = self.root / "mismatched-provenance-attestation.json"
        with mock.patch.object(
            build.APPROVAL_SIGNER,
            "_authenticated_github_replay",
            return_value=("0" * 64, "7" * 64),
        ):
            with self.assertRaisesRegex(ValueError, "authenticated provenance differs"):
                build.sign(
                    self.aab,
                    self.graph,
                    self.build_sidecar,
                    self.two_green_receipt,
                    self.two_green_approval,
                    self.attester_private_key,
                    mismatched_provenance_output,
                    self.github_token,
                )
        self.assertFalse(mismatched_provenance_output.exists())

        fabricated_validation = self.protected_build_validation()
        fabricated_validation["validatorSha256"]["validate-aab.sh"] = "0" * 64
        with self.assertRaisesRegex(ValueError, "validator source changed"):
            build._validate_validation_claims(fabricated_validation)

        graph = copy.deepcopy(self.graph_payload)
        next(row for row in graph["repositories"] if row["name"] == "chummer6-design").update(
            {"tree": "f" * 40, "tree_sha256": "e" * 64}
        )
        write_private(self.graph, graph)
        substituted_graph_sha = hashlib.sha256(self.graph.read_bytes()).hexdigest()
        self.build_sidecar.write_text(
            f"{hashlib.sha256(self.aab.read_bytes()).hexdigest()}  artifacts/{self.aab.name}\n"
            f"{substituted_graph_sha}  artifacts/{self.graph.name}\n",
            encoding="ascii",
        )
        with self.assertRaisesRegex(ValueError, "differs from exact protected outputs"):
            self.original_build_attestation_verifier(
                self.build_attestation, self.aab, self.graph, self.build_sidecar,
                self.two_green_receipt, self.two_green_approval,
            )

    def test_preview10_verifier_receipt_and_schema_are_immutable(self) -> None:
        expected = {
            PREVIEW10_SCRIPT: "b5774d06d00ea0f0bf99a1eac17a21119d1111da5b105fa86ce4cac619d62f52",
            PREVIEW10_RECEIPT: "8f245fcf6e8fd62d6ed2d7e75170617d3c5430e024ce14ab77535ca1c57fece9",
            PREVIEW10_SCHEMA: "c6da97a4b9afbfb83d93f180a34ffde5dc5a9f9443bbc00755f74b36250ecd9f",
        }
        for path, digest in expected.items():
            with self.subTest(path=path.name):
                self.assertEqual(digest, hashlib.sha256(path.read_bytes()).hexdigest())
        historical = self.preview10.verify(PREVIEW10_RECEIPT)
        self.assertEqual("pass", historical["status"], historical["failures"])
        self.assertFalse(historical["authorization"]["productionAuthorized"])
        self.assertFalse((REPO / "play/evidence/preview11-internal-publication.json").exists())

    def test_browser_input_is_closed_and_rejects_secret_or_session_fields(self) -> None:
        cases = (
            (lambda value: value.update({"cookie": "secret"}), "fields are not exact"),
            (
                lambda value: value.update({"credentialOrSessionDataRecorded": True}),
                "credential or session data",
            ),
            (
                lambda value: value["track"].update({"name": "production"}),
                "not exact Internal testing",
            ),
            (
                lambda value: value["application"].update({"playApplicationId": "1"}),
                "not exact for Chummer",
            ),
            (
                lambda value: value["track"].update({"trackId": "1"}),
                "not exact Internal testing",
            ),
            (
                lambda value: value["release"].update({"status": "In review"}),
                "not available to Internal testers",
            ),
            (
                lambda value: value["release"]["releasedAt"].update(
                    {"normalizedUtc": "2026-09-03T01:04:00Z"}
                ),
                "invents unavailable authority",
            ),
            (
                lambda value: value.update({"fieldsObserved": ["application", "release"]}),
                "field inventory",
            ),
        )
        for mutation, message in cases:
            with self.subTest(message=message):
                candidate = copy.deepcopy(self.browser_payload)
                mutation(candidate)
                write_private(self.browser, candidate)
                with self.assertRaisesRegex(ValueError, message):
                    self.materialize()
                self.assertFalse(self.receipt.exists())

    def test_duplicate_browser_key_and_unknown_cli_secret_option_fail_closed(self) -> None:
        raw = self.browser.read_text(encoding="utf-8")
        raw = raw.replace(
            '"contractName": "chummer.android.play-internal-browser-readback/v1",',
            '"contractName": "chummer.android.play-internal-browser-readback/v1",\n'
            '  "contractName": "chummer.android.play-internal-browser-readback/v1",',
            1,
        )
        self.browser.write_text(raw, encoding="utf-8")
        self.browser.chmod(0o600)
        with self.assertRaisesRegex(ValueError, "duplicate key"):
            self.materialize()

        completed = subprocess.run(
            [
                sys.executable,
                str(SCRIPT),
                "materialize",
                "--browser-readback",
                str(self.browser),
                "--aab",
                str(self.aab),
                "--source-graph",
                str(self.graph),
                "--output",
                str(self.receipt),
                "--build-sidecar",
                str(self.build_sidecar),
                "--build-attestation",
                str(self.build_attestation),
                "--two-green-receipt",
                str(self.two_green_receipt),
                "--two-green-approval",
                str(self.two_green_approval),
                "--token",
                "do-not-record",
            ],
            check=False,
            capture_output=True,
            text=True,
        )
        self.assertNotEqual(0, completed.returncode)
        self.assertIn("unrecognized arguments", completed.stderr)

    def test_graph_contract_identity_inventory_and_local_posture_fail_closed(self) -> None:
        cases = (
            (
                lambda value: value.update(
                    {"contractName": "chummer.android.release-source-graph/v2"}
                ),
                "v3 local evidence",
            ),
            (lambda value: value.update({"publicationAuthorized": True}), "v3 local evidence"),
            (
                lambda value: value["releaseIdentity"].update({"versionCode": 100}),
                "does not match browser readback",
            ),
            (
                lambda value: value.update(
                    {
                        "generatedAtUtc": (
                            datetime.now(UTC) + timedelta(minutes=4)
                        ).replace(microsecond=0).isoformat().replace("+00:00", "Z")
                    }
                ),
                "generated after the browser publication observation",
            ),
            (lambda value: value["repositories"].pop(), "repository inventory"),
            (
                lambda value: value["repositories"].reverse(),
                "repository order",
            ),
            (
                lambda value: value["generator"].update({"sha256": "0" * 64}),
                "generator binding",
            ),
            (lambda value: value["ownerPackagePins"].pop(), "owner package inventory"),
            (
                lambda value: value["presentationSource"].update(
                    {"publication_authorized": True}
                ),
                "Presentation authority",
            ),
        )
        for mutation, message in cases:
            with self.subTest(message=message):
                candidate = copy.deepcopy(self.graph_payload)
                mutation(candidate)
                write_private(self.graph, candidate)
                with self.assertRaisesRegex(ValueError, message):
                    self.materialize()
                self.assertFalse(self.receipt.exists())

    def test_approved_head_digest_and_fresh_readback_are_required(self) -> None:
        expected_head = self.graph_payload["repositories"][0]["commit"]
        expected_digest = hashlib.sha256(self.aab.read_bytes()).hexdigest()
        cases = (
            (
                {"sourceCommit": "f" * 40, "aab": {"sha256": expected_digest}},
                "does not match approved source head",
            ),
            (
                {"sourceCommit": expected_head, "aab": {"sha256": "f" * 64}},
                "AAB bytes do not match approved AAB sha256",
            ),
        )
        for bindings, message in cases:
            with self.subTest(message=message):
                original = copy.deepcopy(self.build_attestation_binding)
                self.build_attestation_binding.update(bindings)
                with self.assertRaisesRegex(ValueError, message):
                    self.materialize()
                self.build_attestation_binding = original
                self.assertFalse(self.receipt.exists())

        stale = copy.deepcopy(self.browser_payload)
        stale["observedAtUtc"] = (
            datetime.now(UTC) - timedelta(hours=25)
        ).replace(microsecond=0).isoformat().replace("+00:00", "Z")
        self.graph_payload["generatedAtUtc"] = stale["observedAtUtc"]
        write_private(self.browser, stale)
        write_private(self.graph, self.graph_payload)
        with self.assertRaisesRegex(ValueError, "too old"):
            self.materialize()
        self.assertFalse(self.receipt.exists())

    def test_noncanonical_or_changed_aab_and_source_graph_bytes_fail_closed(self) -> None:
        self.materialize()
        self.write_aab(self.aab, marker=b"different-exact-bytes")
        result = self.verify()
        self.assertEqual("fail", result["status"])
        self.assertFalse(result["publicationAuthorized"])
        self.assertFalse(result["productionAuthorized"])

        self.receipt.unlink()
        self.write_aab(self.aab)
        self.materialize()
        graph = copy.deepcopy(self.graph_payload)
        graph["repositories"][0]["tree"] = "f" * 40
        write_private(self.graph, graph)
        result = self.verify()
        self.assertEqual("fail", result["status"])
        self.assertFalse(result["publicationAuthorized"])

    def test_protected_source_graph_digest_rejects_design_and_tree_substitution(self) -> None:
        for label, mutate in (
            (
                "design",
                lambda graph: next(
                    row for row in graph["repositories"]
                    if row["name"] == "chummer6-design"
                ).update({"commit": "f" * 40, "tree": "e" * 40}),
            ),
            (
                "repository tree bytes",
                lambda graph: graph["repositories"][1].update(
                    {"tree_sha256": "0" * 64}
                ),
            ),
        ):
            with self.subTest(label=label):
                graph = copy.deepcopy(self.graph_payload)
                mutate(graph)
                write_private(self.graph, graph)
                with self.assertRaisesRegex(
                    ValueError, "source graph bytes do not match approved build-sidecar sha256"
                ):
                    self.materialize()
                self.assertFalse(self.receipt.exists())

    def test_receipt_claim_escalation_and_noncanonical_bytes_fail_closed(self) -> None:
        self.materialize()
        original = json.loads(self.receipt.read_text(encoding="utf-8"))
        cases = (
            (lambda value: value["authorization"].update({"productionAuthorized": True})),
            (lambda value: value["authorization"].update({"uploadActionAuthorized": True})),
            (
                lambda value: value["twoGreenEligibility"].update(
                    {"googlePlayUploadAuthorized": True}
                )
            ),
            (lambda value: value["doesNotClaim"].remove("tester_installation")),
            (lambda value: value.update({"credential": "secret"})),
        )
        for mutation in cases:
            with self.subTest(mutation=mutation):
                candidate = copy.deepcopy(original)
                mutation(candidate)
                write_private(self.receipt, candidate)
                result = self.verify()
                self.assertEqual("fail", result["status"])
                self.assertFalse(result["publicationAuthorized"])
                self.assertFalse(result["productionAuthorized"])
        self.receipt.write_text(json.dumps(original, indent=2) + "\n", encoding="utf-8")
        self.receipt.chmod(0o600)
        result = self.verify()
        self.assertEqual("fail", result["status"])
        self.assertTrue(any("not canonical" in failure for failure in result["failures"]))

    def test_input_permissions_output_collision_and_archive_shape_fail_closed(self) -> None:
        self.browser.chmod(0o644)
        with self.assertRaisesRegex(ValueError, "owner file"):
            self.materialize()
        self.assertFalse(self.receipt.exists())
        self.browser.chmod(0o600)

        self.aab.unlink()
        self.aab.write_bytes(b"not-a-bundle")
        self.aab.chmod(0o644)
        with self.assertRaisesRegex(ValueError, "readable bundle archive"):
            self.materialize()
        self.write_aab(self.aab)

        self.receipt.write_text("occupied\n", encoding="utf-8")
        self.receipt.chmod(0o600)
        with self.assertRaisesRegex(ValueError, "new file"):
            self.materialize()

    def test_schema_is_closed_internal_only_and_never_grants_production(self) -> None:
        schema = json.loads(SCHEMA.read_text(encoding="utf-8"))
        browser_schema = json.loads(BROWSER_SCHEMA.read_text(encoding="utf-8"))
        self.assertFalse(schema["additionalProperties"])
        self.assertEqual(self.module.CONTRACT, schema["properties"]["contractName"]["const"])
        self.assertFalse(schema["properties"]["publicationAuthorized"]["const"])
        qualification = schema["properties"]["twoGreenEligibility"]
        self.assertFalse(qualification["additionalProperties"])
        protected_approval = qualification["properties"]["protectedApproval"]
        self.assertFalse(protected_approval["additionalProperties"])
        self.assertEqual(
            "android_internal_release_preparation",
            protected_approval["properties"]["approvalScope"]["const"],
        )
        self.assertTrue(
            qualification["properties"]["internalTestingEligible"]["const"]
        )
        self.assertFalse(
            qualification["properties"]["publicationAuthorized"]["const"]
        )
        self.assertFalse(
            qualification["properties"]["googlePlayUploadAuthorized"]["const"]
        )
        authorization = schema["properties"]["authorization"]
        self.assertFalse(authorization["additionalProperties"])
        self.assertEqual(
            "google_play_internal_testing_evidence_only",
            authorization["properties"]["scope"]["const"],
        )
        self.assertFalse(authorization["properties"]["publicationAuthorized"]["const"])
        self.assertFalse(authorization["properties"]["productionAuthorized"]["const"])
        self.assertFalse(authorization["properties"]["uploadActionAuthorized"]["const"])
        self.assertFalse(
            authorization["properties"]["testerRosterMutationAuthorized"]["const"]
        )
        self.assertFalse(browser_schema["additionalProperties"])
        self.assertEqual(
            self.module.BROWSER_READBACK_CONTRACT,
            browser_schema["properties"]["contractName"]["const"],
        )
        self.assertFalse(
            browser_schema["properties"]["credentialOrSessionDataRecorded"]["const"]
        )
        self.assertNotIn("credentials", browser_schema["properties"])
        self.assertNotIn("session", browser_schema["properties"])
        self.assertNotIn("screenshots", browser_schema["properties"])
        self.assertNotIn("notes", browser_schema["properties"])

    def test_materializer_has_no_network_browser_or_play_mutation_surface(self) -> None:
        source = SCRIPT.read_text(encoding="utf-8")
        for forbidden in (
            "import requests",
            "import subprocess",
            "urllib.request",
            "selenium",
            "playwright",
            "browser-act",
            "service-account",
        ):
            self.assertNotIn(forbidden, source)
        self.assertIn("never opens Play", source)
        self.assertIn('"uploadActionAuthorized": False', source)
        self.assertIn('"publicationAuthorized": False', source)
        self.assertIn('"productionAuthorized": False', source)


if __name__ == "__main__":
    unittest.main()
