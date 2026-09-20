from __future__ import annotations

import copy
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import subprocess
import tempfile
import unittest
from unittest import mock

REPO = Path(__file__).resolve().parents[1]
SPEC = importlib.util.spec_from_file_location("design_policy_under_test", REPO / "scripts/android_design_policy_authority.py")
assert SPEC and SPEC.loader
policy = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(policy)


class DesignPolicyAuthorityTests(unittest.TestCase):
    pin_path = policy.PIN_PATH
    local_internal = False

    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name).resolve()
        self.android = self.root / "android"
        self.design = self.root / "design"
        self.design.mkdir()
        gate = self.android / policy.CONTRACT_RELATIVE_PATH
        gate.parent.mkdir(parents=True)
        gate.write_bytes((REPO / policy.CONTRACT_RELATIVE_PATH).read_bytes())
        self.gate_binding = {"path": policy.CONTRACT_RELATIVE_PATH.as_posix(),
            "schema": policy.CONTRACT_SCHEMA, "sha256": hashlib.sha256(gate.read_bytes()).hexdigest()}
        self.matrix = {
            "schema": policy.MATRIX_SCHEMA, "owner": "chummer6-design",
            "status": "contract_defined_evidence_pending",
            "evidenceAuthority": {
                "implementationRepository": "chummer-android",
                "wizardGateAuthority": "chummer-android/" + self.gate_binding["path"],
                "wizardGateSchema": self.gate_binding["schema"],
                "wizardGateSha256": self.gate_binding["sha256"],
                "wizardAggregateSchema": policy.AGGREGATE_SCHEMA,
                "requiredP0Journeys": [r["matrixJourney"] for r in policy.REQUIRED_JOURNEY_SPECS],
            },
            "claimTiers": {"phone_beta": {"currentEvidenceStatus": "pending", "tabletRequired": False, "rookRequired": False}},
            "capabilities": [{"id": "advanced_editor", "betaPosture": "postponed_non_blocking", "visibility": "not_in_phone_beta"}],
        }
        for relative, data in ((policy.MATRIX_PATH, json.dumps(self.matrix).encode()),
                (policy.VALIDATOR_PATH, b"raise RuntimeError('must never execute Design validator')\n")):
            target = self.design / relative
            target.parent.mkdir(parents=True, exist_ok=True)
            target.write_bytes(data)
        self.git("init", "-q")
        self.git("config", "user.name", "Policy Fixture")
        self.git("config", "user.email", "policy@example.invalid")
        self.git("remote", "add", "origin", "https://github.com/" + policy.DESIGN_REPOSITORY + ".git")
        self.seal()

    def git(self, *args):
        return subprocess.run(["git", "-C", str(self.design), *args], check=True,
            capture_output=True, text=True).stdout.strip()

    def seal(self):
        self.git("add", ".")
        self.git("commit", "-q", "--allow-empty", "-m", "exact policy")
        binding = {"repository": policy.DESIGN_REPOSITORY,
            "commit": self.git("rev-parse", "HEAD"), "tree": self.git("rev-parse", "HEAD^{tree}"),
            "matrixSchema": policy.MATRIX_SCHEMA, "wizardAggregateSchema": policy.AGGREGATE_SCHEMA,
            "wizardGate": self.gate_binding}
        for field, path in (("matrix", policy.MATRIX_PATH), ("validator", policy.VALIDATOR_PATH)):
            binding[field] = {"path": path, "sha256": hashlib.sha256((self.design / path).read_bytes()).hexdigest()}
        self.expected = {"design": binding}
        self.write_pin()

    def write_pin(self):
        (self.android / self.pin_path).write_text(json.dumps({"schema": policy.PIN_SCHEMA, **self.expected}))
        if self.local_internal:
            workflow = self.android / policy.EXTENDED_WORKFLOW
            workflow.parent.mkdir(parents=True, exist_ok=True)
            if not workflow.exists():
                workflow.write_text("on:\n  workflow_dispatch:\n\npermissions:\n  contents: read\n")
        for args in (("init", "-q"), ("config", "user.name", "Android Policy Fixture"),
                     ("config", "user.email", "policy@example.invalid"), ("add", "."),
                     ("commit", "-q", "--allow-empty", "-m", "admitted Design pin")):
            subprocess.run(["git", "-C", str(self.android), *args], check=True, capture_output=True)

    def verify(self):
        return policy.verify_design_checkout(self.design, android_root=self.android,
                                             local_internal=self.local_internal)

    def test_authenticates_actual_git_and_raw_bytes_without_executing_design(self):
        self.assertEqual(self.expected, self.verify())

    def test_rejects_missing_extra_stale_and_substituted_bindings(self):
        # Historical receipt validation deliberately remains on the historical pin.
        if self.local_internal:
            self.assertNotEqual(self.pin_path, policy.PIN_PATH)
            with self.assertRaises(FileNotFoundError):
                policy.load_policy_pin(android_root=self.android)
            return
        for value in (None, {}, {"design": {}}, {**self.expected, "other": {}},
                      {"design": {**self.expected["design"], "commit": "a" * 40}}):
            with self.subTest(value=value), self.assertRaises(ValueError):
                policy.validate_policy_authorities(value, android_root=self.android)

    def test_wrong_commit_tree_remote_and_digest_fail_even_if_fields_look_valid(self):
        original = copy.deepcopy(self.expected)
        for path in (("commit",), ("tree",), ("matrix", "sha256"), ("validator", "sha256")):
            self.expected = copy.deepcopy(original)
            node = self.expected["design"]
            for key in path[:-1]:
                node = node[key]
            node[path[-1]] = "a" * (40 if len(path) == 1 else 64)
            self.write_pin()
            with self.subTest(path=path), self.assertRaises(ValueError):
                self.verify()
        self.expected = original
        self.write_pin()
        self.git("remote", "set-url", "origin", "https://github.com/attacker/chummer6-design.git")
        with self.assertRaises(ValueError):
            self.verify()

    def test_semantically_wrong_policy_rejected_after_honest_reseal(self):
        original = copy.deepcopy(self.matrix)
        cases = [
            ("wizardAggregateSchema", "chummer.android.api36-sr5-wizard-e2e-aggregate/v1"),
            ("requiredP0Journeys", original["evidenceAuthority"]["requiredP0Journeys"][:3]),
            ("requiredP0Journeys", list(reversed(original["evidenceAuthority"]["requiredP0Journeys"]))),
            ("requiredP0Journeys", original["evidenceAuthority"]["requiredP0Journeys"] + ["full-editing"]),
            ("wizardGateSha256", "0" * 64),
            ("rowInventory", "legacy row inventory cannot qualify wizards"),
        ]
        for field, value in cases:
            changed = copy.deepcopy(original)
            changed["evidenceAuthority"][field] = value
            (self.design / policy.MATRIX_PATH).write_text(json.dumps(changed))
            self.seal()
            with self.subTest(field=field, value=value), self.assertRaises(ValueError):
                self.verify()

    def test_full_editing_phone_beta_claim_cannot_be_reintroduced(self):
        self.matrix["capabilities"][0]["visibility"] = "visible_only_when_proven"
        (self.design / policy.MATRIX_PATH).write_text(json.dumps(self.matrix))
        self.seal()
        with self.assertRaisesRegex(ValueError, "Full Editing"):
            self.verify()

    def test_dirty_assume_unchanged_policy_still_compared_against_commit_blob(self):
        self.git("update-index", "--assume-unchanged", policy.VALIDATOR_PATH)
        (self.design / policy.VALIDATOR_PATH).write_bytes(b"# rewritten despite clean status\n")
        self.expected["design"]["validator"]["sha256"] = hashlib.sha256((self.design / policy.VALIDATOR_PATH).read_bytes()).hexdigest()
        self.write_pin()
        self.assertEqual("", self.git("status", "--porcelain=v1"))
        with self.assertRaisesRegex(ValueError, "blob bytes"):
            self.verify()

    def test_android_policy_pin_hidden_by_index_flags_still_fails(self):
        subprocess.run(["git", "-C", str(self.android), "update-index", "--assume-unchanged", self.pin_path.as_posix()],
                       check=True, capture_output=True)
        path = self.android / self.pin_path
        path.write_bytes(path.read_bytes() + b"\n")
        with self.assertRaisesRegex(ValueError, "exact HEAD blob"):
            self.verify()

    def test_cli_rejects_symlink_root_instead_of_resolving_it_away(self):
        link = self.root / "design-alias"
        link.symlink_to(self.design, target_is_directory=True)
        with mock.patch("sys.argv", ["policy", "--android-root", str(self.android), "--design-root", str(link)]), self.assertRaises(ValueError):
            policy.main()

    def test_noncanonical_root_and_symlink_member_rejected(self):
        link = self.root / "design-link"
        link.symlink_to(self.design, target_is_directory=True)
        with self.assertRaises(ValueError):
            policy.verify_design_checkout(link, android_root=self.android)
        target = self.design / policy.VALIDATOR_PATH
        outside = self.root / "validator.py"
        outside.write_bytes(target.read_bytes())
        target.unlink()
        target.symlink_to(outside)
        with self.assertRaises(ValueError):
            self.verify()

    def test_duplicate_keys_and_nonfinite_numbers_fail_closed(self):
        for data in (b'{"schema":"a","schema":"b"}', b'{"number":NaN}'):
            with self.subTest(data=data), self.assertRaises(ValueError):
                policy._json(data)

    def test_fifo_policy_input_fails_without_waiting_for_a_writer(self):
        target = self.android / self.pin_path
        target.unlink()
        os.mkfifo(target)
        with self.assertRaisesRegex(ValueError, "bounded regular file"):
            self.verify()

    def test_gate_byte_drift_and_v1_pin_schema_rejected(self):
        self.expected["design"]["wizardAggregateSchema"] = "chummer.android.api36-sr5-wizard-e2e-aggregate/v1"
        self.write_pin()
        with self.assertRaisesRegex(ValueError, "schema"):
            policy.load_policy_pin(android_root=self.android, local_internal=self.local_internal)

    def test_post_verification_head_drift_fails_closed(self):
        real = policy._git
        calls = 0
        def changing(root, *args):
            nonlocal calls
            if root == self.design and args == ("rev-parse", "HEAD"):
                calls += 1
                if calls == 2:
                    return b"a" * 40 + b"\n"
            return real(root, *args)
        with mock.patch.object(policy, "_git", side_effect=changing), self.assertRaisesRegex(ValueError, "changed"):
            self.verify()


class LocalInternalDesignPolicyTests(DesignPolicyAuthorityTests):
    pin_path = policy.LOCAL_PIN_PATH
    local_internal = True

    def setUp(self):
        super().setUp()
        self.matrix["internalDeliveryPolicy"] = copy.deepcopy(policy.INTERNAL_DELIVERY_POLICY)
        self.matrix["evidenceAuthority"]["qualificationUse"] = "optional_extended_runtime"
        (self.design / policy.MATRIX_PATH).write_text(json.dumps(self.matrix))
        self.seal()

    def test_rejects_obsolete_hosted_requirement_and_weakened_safety_after_reseal(self):
        original = copy.deepcopy(self.matrix)
        for key, value in (
            ("orderedReviewMainRequired", True), ("hostedRuntimeRequired", True),
            ("securityChangesRequireNegativeTests", False),
            ("persistenceChangesRequireProcessRestart", False),
            ("existingUploadKeyRequired", False), ("isolatedSignerRequired", False),
            ("keylessBuildAndVerification", False), ("exactArtifactAndCertificateChecks", False),
            ("playReadbackRequired", False), ("physicalPlayInstallRequiredForBetaClaim", False),
            ("policyAuthorizesUpload", True), ("sourceCheckIsRuntimeEvidence", True),
            ("orderedReviewMainRequired", 0), ("isolatedSignerRequired", 1),
        ):
            matrix = copy.deepcopy(original)
            matrix["internalDeliveryPolicy"][key] = value
            (self.design / policy.MATRIX_PATH).write_text(json.dumps(matrix))
            self.seal()
            with self.subTest(key=key, value=value), self.assertRaisesRegex(ValueError, "delivery policy"):
                self.verify()

    def test_old_matrix_is_not_current_local_delivery_authority(self):
        del self.matrix["internalDeliveryPolicy"]
        (self.design / policy.MATRIX_PATH).write_text(json.dumps(self.matrix))
        self.seal()
        with self.assertRaisesRegex(ValueError, "delivery policy"):
            self.verify()

    def test_automatic_runtime_workflow_conflicts_with_local_policy(self):
        (self.android / policy.EXTENDED_WORKFLOW).write_text(
            "on:\n  pull_request:\n  push:\n    branches: [main]\npermissions:\n  contents: read\n")
        self.write_pin()
        with self.assertRaisesRegex(ValueError, "manual-only"):
            self.verify()

    def test_local_cli_reports_policy_only_not_eligibility(self):
        with mock.patch("sys.argv", ["policy", "--local-internal", "--android-root", str(self.android),
                                     "--design-root", str(self.design)]), mock.patch("builtins.print") as output:
            self.assertEqual(0, policy.main())
        result = json.loads(output.call_args.args[0])
        self.assertFalse(result["publicationAuthorized"])
        self.assertNotIn("eligible", result)
        self.assertNotIn("googlePlayUploadAuthorized", result)


if __name__ == "__main__":
    unittest.main()
