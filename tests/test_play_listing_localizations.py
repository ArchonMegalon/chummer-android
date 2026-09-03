from __future__ import annotations

import importlib.util
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile
import unittest


REPO = Path(__file__).resolve().parents[1]
SCRIPT = REPO / "scripts" / "verify_play_listing_localizations.py"
LISTING = REPO / "play" / "listing"
PROJECT = REPO / "src" / "Chummer.Android" / "Chummer.Android.csproj"
PHONE_LOCALE_POLICY = REPO / "src" / "Chummer.Android" / "Native" / "PhoneLocalePolicy.cs"
DATA_SAFETY = REPO / "play" / "data-safety.md"
PREVIEW10_EVIDENCE = REPO / "play" / "evidence" / "preview10-internal-publication.json"
WIZARD_GATE = REPO / "eng" / "api36-sr5-wizard-gate-authority.json"


def load_module():
    spec = importlib.util.spec_from_file_location("verify_play_listing_localizations", SCRIPT)
    assert spec is not None and spec.loader is not None
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


class PlayListingLocalizationTests(unittest.TestCase):
    def setUp(self) -> None:
        self.module = load_module()

    def validate(self, listing: Path = LISTING, **overrides):
        arguments = {
            "project": PROJECT,
            "phone_locale_policy": PHONE_LOCALE_POLICY,
            "data_safety": DATA_SAFETY,
            "preview10_evidence": PREVIEW10_EVIDENCE,
            "wizard_gate_authority": WIZARD_GATE,
        }
        arguments.update(overrides)
        return self.module.validate_listing(listing, **arguments)

    @staticmethod
    def copy_listing(root: Path) -> Path:
        target = root / "listing"
        shutil.copytree(LISTING, target)
        return target

    def test_exact_supported_locales_are_truthful_bounded_and_internal_only(self) -> None:
        result = self.validate()
        self.assertEqual(["en-US", "de-DE", "es-ES"], result["locales"])
        self.assertEqual("com.myexternalbrain.chummer", result["packageId"])
        self.assertEqual("0.1.0-preview.11", result["release"])
        self.assertEqual("internal_testing_only", result["trackPosture"])
        self.assertEqual("sr5_phone_wizards_only", result["scope"])
        self.assertEqual(self.module.WIZARD_GATE_SHA256, result["wizardGateSha256"])
        self.assertEqual(
            list(self.module.REQUIRED_GATE_JOURNEYS),
            result["requiredJourneys"],
        )
        self.assertFalse(result["publicationAuthorized"])
        for locale, fields in result["lengths"].items():
            for name, length in fields.items():
                with self.subTest(locale=locale, name=name):
                    self.assertGreater(length, 0)
                    self.assertLessEqual(length, self.module.LIMITS[name])

    def test_cli_reports_listing_validation_without_publication_authority(self) -> None:
        completed = subprocess.run(
            [sys.executable, str(SCRIPT)],
            check=False,
            capture_output=True,
            text=True,
        )
        self.assertEqual(0, completed.returncode, completed.stderr)
        self.assertEqual(
            "play_listing_localizations=pass locales=3 journeys=7 "
            "scope=sr5_phone_wizards_only "
            f"gate_sha256={self.module.WIZARD_GATE_SHA256} "
            "publication_authorized=false\n",
            completed.stdout,
        )

    def test_missing_extra_untranslated_and_overlong_locales_fail_closed(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            missing = self.copy_listing(root / "missing")
            shutil.rmtree(missing / "de-DE")
            with self.assertRaisesRegex(ValueError, "locale set"):
                self.validate(missing)

            extra = self.copy_listing(root / "extra")
            shutil.copytree(extra / "en-US", extra / "fr-FR")
            with self.assertRaisesRegex(ValueError, "locale set"):
                self.validate(extra)

            untranslated = self.copy_listing(root / "untranslated")
            shutil.copyfile(
                untranslated / "en-US" / "short-description.txt",
                untranslated / "de-DE" / "short-description.txt",
            )
            with self.assertRaisesRegex(ValueError, "wizard-scope or non-claim|untranslated"):
                self.validate(untranslated)

            overlong = self.copy_listing(root / "overlong")
            (overlong / "es-ES" / "short-description.txt").write_text(
                "x" * 81 + "\n",
                encoding="utf-8",
            )
            with self.assertRaisesRegex(ValueError, "length limit"):
                self.validate(overlong)

    def test_scope_internal_posture_and_nonclaims_fail_closed(self) -> None:
        replacements = (
            ("en-US", "limited to SR5 phone wizards", "available for every device"),
            (
                "de-DE",
                "sind nicht Teil dieses Tests",
                "sind vollständig verfügbar",
            ),
            (
                "es-ES",
                "no forman parte de esta prueba",
                "están disponibles",
            ),
        )
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            for index, (locale, original, replacement) in enumerate(replacements):
                listing = self.copy_listing(root / str(index))
                path = listing / locale / "full-description.txt"
                path.write_text(
                    path.read_text(encoding="utf-8").replace(original, replacement),
                    encoding="utf-8",
                )
                with self.subTest(locale=locale), self.assertRaisesRegex(
                    ValueError,
                    "wizard-scope|non-claim",
                ):
                    self.validate(listing)

    def test_positive_scope_and_premature_proof_claims_fail_closed(self) -> None:
        cases = (
            ("en-US", " Full Editing and tablet support are available."),
            ("de-DE", " Rook und SR6 sind verfügbar."),
            ("es-ES", " La versión pública de producción está disponible."),
            ("en-US", " The seven-journey aggregate passed with digest-bound proof."),
            ("de-DE", " Das Aggregat hat bestanden und ist nachgewiesen."),
            ("es-ES", " El agregado fue superado y quedó demostrado."),
        )
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            for index, (locale, claim) in enumerate(cases):
                listing = self.copy_listing(root / str(index))
                path = listing / locale / "release-notes-11.txt"
                path.write_text(
                    path.read_text(encoding="utf-8").rstrip("\n") + claim + "\n",
                    encoding="utf-8",
                )
                with self.subTest(locale=locale, claim=claim), self.assertRaisesRegex(
                    ValueError,
                    "prohibited positive product claim|runtime proof before a green aggregate",
                ):
                    self.validate(listing)

    def test_package_supported_languages_and_historical_bytes_fail_closed(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)

            project = root / "Chummer.Android.csproj"
            project.write_text(
                PROJECT.read_text(encoding="utf-8").replace(
                    "com.myexternalbrain.chummer",
                    "com.example.chummer",
                ),
                encoding="utf-8",
            )
            with self.assertRaisesRegex(ValueError, "package identity"):
                self.validate(project=project)

            versioned_project = root / "Versioned.Chummer.Android.csproj"
            versioned_project.write_text(
                PROJECT.read_text(encoding="utf-8").replace(
                    "0.1.0-preview.11",
                    "0.1.0-preview.12",
                ),
                encoding="utf-8",
            )
            with self.assertRaisesRegex(ValueError, "version name"):
                self.validate(project=versioned_project)

            policy = root / "PhoneLocalePolicy.cs"
            policy.write_text(
                PHONE_LOCALE_POLICY.read_text(encoding="utf-8").replace("es-ES", "fr-FR"),
                encoding="utf-8",
            )
            with self.assertRaisesRegex(ValueError, "supported UI languages"):
                self.validate(phone_locale_policy=policy)

            data_safety = root / "data-safety.md"
            data_safety.write_bytes(DATA_SAFETY.read_bytes() + b"\n")
            with self.assertRaisesRegex(ValueError, "Data safety source drifted"):
                self.validate(data_safety=data_safety)

            evidence = root / "preview10-internal-publication.json"
            evidence.write_bytes(PREVIEW10_EVIDENCE.read_bytes() + b"\n")
            with self.assertRaisesRegex(ValueError, "Preview.10 historical publication"):
                self.validate(preview10_evidence=evidence)

            wizard_gate = root / "api36-sr5-wizard-gate-authority.json"
            wizard_gate.write_bytes(WIZARD_GATE.read_bytes() + b"\n")
            with self.assertRaisesRegex(ValueError, "wizard gate authority drifted"):
                self.validate(wizard_gate_authority=wizard_gate)

            listing = self.copy_listing(root / "notes")
            notes = listing / "en-US" / "release-notes-10.txt"
            notes.write_bytes(notes.read_bytes() + b"\n")
            with self.assertRaisesRegex(ValueError, "Preview.10 historical release notes"):
                self.validate(listing)


if __name__ == "__main__":
    unittest.main()
