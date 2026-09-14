from __future__ import annotations

import copy
import hashlib
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest


REPO = Path(__file__).resolve().parents[1]


def load_consumer():
    specification = importlib.util.spec_from_file_location(
        "release_design_policy_consumer_test",
        REPO / "scripts/verify_api36_two_green_release_eligibility.py",
    )
    assert specification is not None and specification.loader is not None
    module = importlib.util.module_from_spec(specification)
    specification.loader.exec_module(module)
    return module


class ReleaseDesignPolicyGraphTests(unittest.TestCase):
    """Exercise the release graph comparison without signing or external calls.

    The expected policy is the already-validated common authority passed by the
    public consumer.  Checkout authentication and matrix semantics are covered
    by the Design helper's own real-Git tests, not replaced by this fixture.
    """

    def setUp(self) -> None:
        self.consumer = load_consumer()
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.path = Path(self.temporary.name).resolve() / "source-graph.json"
        self.android = self.path.parent / "android"
        design_helper = self.consumer.DESIGN_POLICY
        gate_raw = (REPO / design_helper.CONTRACT_RELATIVE_PATH).read_bytes()
        wizard_gate = {
            "path": design_helper.CONTRACT_RELATIVE_PATH.as_posix(),
            "sha256": hashlib.sha256(gate_raw).hexdigest(),
            "schema": design_helper.CONTRACT_SCHEMA,
        }
        self.policy = {
            "design": {
                "repository": design_helper.DESIGN_REPOSITORY,
                "commit": "a" * 40,
                "tree": "b" * 40,
                "matrix": {"path": design_helper.MATRIX_PATH, "sha256": "c" * 64},
                "validator": {"path": design_helper.VALIDATOR_PATH, "sha256": "d" * 64},
                "matrixSchema": design_helper.MATRIX_SCHEMA,
                "wizardAggregateSchema": design_helper.AGGREGATE_SCHEMA,
                "wizardGate": wizard_gate,
            },
        }
        for relative, raw in (
            (design_helper.CONTRACT_RELATIVE_PATH, gate_raw),
            (design_helper.PIN_PATH, json.dumps({
                "schema": design_helper.PIN_SCHEMA, **self.policy,
            }).encode("utf-8")),
        ):
            path = self.android / relative
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_bytes(raw)
        self.sources = {
            source: {"commit": f"{index:040x}", "tree": f"{index + 20:040x}"}
            for index, source in enumerate(self.consumer.SOURCE_GRAPH_REPOSITORIES, start=1)
        }
        self.repositories = []
        for name, (role, origin) in self.consumer.SOURCE_GRAPH_REPOSITORY_AUTHORITY.items():
            identity = (
                self.policy["design"]
                if name == "chummer6-design"
                else self.sources[next(
                    source for source, repository in self.consumer.SOURCE_GRAPH_REPOSITORIES.items()
                    if repository == name
                )]
            )
            self.repositories.append({
                "name": name, "role": role, "repository": origin,
                "commit": identity["commit"], "tree": identity["tree"], "tree_sha256": "f" * 64,
            })
        self.hub_packages = {
            package: self.sources["hub"]["commit"]
            for package, source in self.consumer.OWNER_PACKAGES.items() if source == "hub"
        }
        self.graph = {
            "contractName": self.consumer.SOURCE_GRAPH_CONTRACT,
            "publicationAuthorized": False,
            "releaseIdentity": {
                "packageId": self.consumer.PACKAGE_ID, "versionName": "0.1.0-preview.12",
                "versionCode": 12, "intentAuthority": "explicit_build_input",
                "minimumExclusiveVersionCode": 11,
            },
            "repositories": copy.deepcopy(self.repositories),
            "ownerPackagePins": [{
                "package_id": package,
                "source_commit": self.sources[source]["commit"],
                "source_tree": self.sources[source]["tree"],
                "source_authority": {
                    "owner_head_commit": self.sources[source]["commit"],
                    "owner_head_tree": self.sources[source]["tree"],
                    "relationship": self.consumer.OWNER_SOURCE_RELATIONSHIP,
                    "verification": self.consumer.OWNER_SOURCE_VERIFICATION,
                },
            } for package, source in self.consumer.OWNER_PACKAGES.items()],
        }

    def verify(self) -> None:
        self.path.write_text(json.dumps(self.graph), encoding="utf-8")
        self.path.chmod(0o600)
        self.consumer._validate_source_graph(
            self.path, source_commit=self.sources["android"]["commit"],
            source_tree=self.sources["android"]["tree"],
            version_name="0.1.0-preview.12", version_code=12,
            sources=self.sources, policy_authorities=self.policy,
            hub_package_sources=self.hub_packages, package_owner_pins=None,
        )

    def test_exact_qualified_design_policy_and_source_row_are_accepted(self) -> None:
        self.assertEqual(self.policy, self.consumer.DESIGN_POLICY.validate_policy_authorities(
            self.policy, android_root=self.android,
        ))
        self.verify()

    def test_missing_policy_is_rejected_despite_present_design_checkout(self) -> None:
        with self.assertRaisesRegex(ValueError, "Design policy authorities are missing"):
            self.consumer.DESIGN_POLICY.validate_policy_authorities(None, android_root=self.android)

    def test_old_or_substituted_policy_fields_are_rejected(self) -> None:
        changes = (
            ("commit", "0" * 40), ("tree", "0" * 40),
            ("repository", "untrusted/chummer6-design"),
            ("matrix", {"path": self.policy["design"]["matrix"]["path"], "sha256": "0" * 64}),
            ("validator", {"path": self.policy["design"]["validator"]["path"], "sha256": "0" * 64}),
            ("wizardAggregateSchema", "chummer.android.api36-sr5-wizard-e2e-aggregate/v1"),
            ("wizardGate", {**self.policy["design"]["wizardGate"], "sha256": "0" * 64}),
        )
        for field, value in changes:
            with self.subTest(field=field):
                changed = copy.deepcopy(self.policy)
                changed["design"][field] = value
                with self.assertRaisesRegex(ValueError, "Design policy authorities are missing, stale or substituted"):
                    self.consumer.DESIGN_POLICY.validate_policy_authorities(changed, android_root=self.android)

    def test_policy_binding_cannot_mask_another_design_checkout(self) -> None:
        for field in ("commit", "tree"):
            with self.subTest(field=field):
                self.graph["repositories"] = copy.deepcopy(self.repositories)
                row = next(row for row in self.graph["repositories"] if row["name"] == "chummer6-design")
                row[field] = "0" * 40
                with self.assertRaisesRegex(ValueError, "Design checkout differs"):
                    self.verify()

    def test_wrong_design_origin_is_rejected_even_with_matching_commit_and_tree(self) -> None:
        row = next(row for row in self.graph["repositories"] if row["name"] == "chummer6-design")
        row["repository"] = "https://github.com/untrusted/chummer6-design.git"
        with self.assertRaisesRegex(ValueError, "role or origin differs"):
            self.verify()

    def test_missing_design_checkout_is_rejected_even_with_policy_binding(self) -> None:
        self.graph["repositories"] = [row for row in self.repositories if row["name"] != "chummer6-design"]
        with self.assertRaisesRegex(ValueError, "repository inventory is not exact"):
            self.verify()


if __name__ == "__main__":
    unittest.main()
