from __future__ import annotations

import importlib.util
from pathlib import Path
import re
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
        self.assertRegex(result["sourceVersion"], r"^0\.1\.0-preview\.[1-9][0-9]*$")
        self.assertNotIn("release", result)
        self.assertEqual("en-GB", result["defaultStoreLocale"])
        self.assertEqual("en-US", result["defaultSourceLocale"])
        self.assertTrue(result["copyOnly"])
        self.assertFalse(result["runtimeQualificationAsserted"])
        self.assertEqual("internal_testing_only", result["trackPosture"])
        self.assertEqual("experimental_sr5_phone_wizards_and_origin_reader", result["scope"])
        self.assertEqual(self.module.WIZARD_GATE_SHA256, result["historicalWizardGateSha256"])
        self.assertEqual(
            list(self.module.REQUIRED_GATE_JOURNEYS),
            result["historicalRequiredJourneys"],
        )
        self.assertFalse(result["publicationAuthorized"])
        for locale, fields in result["lengths"].items():
            for name, length in fields.items():
                with self.subTest(locale=locale, name=name):
                    self.assertGreater(length, 0)
                    self.assertLessEqual(length, self.module.LIMITS[name])

    def test_preview10_upgrade_discloses_optional_account_relink_in_each_locale(self) -> None:
        notices = {
            "en-US": ("Link your account again", "Account linking remains optional"),
            "de-DE": ("Konto neu verknüpfen", "Kontoverknüpfung bleibt optional"),
            "es-ES": ("vuelve a vincular tu cuenta", "vinculación sigue siendo opcional"),
        }
        for locale, (release_notice, optional_notice) in notices.items():
            with self.subTest(locale=locale):
                notes = (LISTING / locale / "release-notes-12.txt").read_text(
                    encoding="utf-8"
                )
                description = (LISTING / locale / "full-description.txt").read_text(
                    encoding="utf-8"
                )
                self.assertIn("Preview.10", notes)
                self.assertIn(release_notice, notes)
                self.assertNotIn("PREVIEW.10", description)
                self.assertIn(optional_notice, description)

    def test_cli_reports_listing_validation_without_publication_authority(self) -> None:
        completed = subprocess.run(
            [sys.executable, str(SCRIPT)],
            check=False,
            capture_output=True,
            text=True,
        )
        self.assertEqual(0, completed.returncode, completed.stderr)
        self.assertEqual(
            "play_listing_localizations=pass locales=3 default=en-GB "
            "copy_only=true historical_gate_preserved=true "
            "runtime_qualification_asserted=false "
            "publication_authorized=false\n",
            completed.stdout,
        )

    def test_stale_full_description_release_version_fails_closed(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            for locale in ("en-US", "de-DE", "es-ES"):
                listing = self.copy_listing(root / locale)
                description = listing / locale / "full-description.txt"
                description.write_text(
                    description.read_text(encoding="utf-8").replace(
                        "Chummer", "Chummer Preview.12", 1
                    ),
                    encoding="utf-8",
                )
                with self.subTest(locale=locale), self.assertRaisesRegex(
                    ValueError, "version-neutral"
                ):
                    self.validate(listing)

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
            (
                "en-US",
                "available to invited Internal testers",
                "available for every device",
            ),
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
                path = listing / locale / "full-description.txt"
                path.write_text(
                    path.read_text(encoding="utf-8").rstrip("\n") + claim + "\n",
                    encoding="utf-8",
                )
                with self.subTest(locale=locale, claim=claim), self.assertRaisesRegex(
                    ValueError,
                    "prohibited positive product claim|runtime proof from store copy",
                ):
                    self.validate(listing)

    def test_origin_consent_scope_and_physical_install_disclosures_are_required(self) -> None:
        for locale in self.module.LOCALES:
            for fragment in self.module.REQUIRED_FRAGMENTS[locale]["full-description.txt"]:
                with self.subTest(locale=locale, fragment=fragment), tempfile.TemporaryDirectory() as temporary:
                    listing = self.copy_listing(Path(temporary))
                    path = listing / locale / "full-description.txt"
                    path.write_text(path.read_text(encoding="utf-8").replace(fragment, ""), encoding="utf-8")
                    with self.assertRaisesRegex(ValueError, "missing exact wizard-scope or non-claim"):
                        self.validate(listing)

    def test_broad_wizard_inclusion_claims_fail_closed_in_every_locale(self) -> None:
        cases = (
            ("en-US", " All SR5 wizards are included."),
            ("de-DE", " Alle SR5-Wizards."),
            ("es-ES", " Todos los asistentes de Carrera."),
        )
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            for index, (locale, claim) in enumerate(cases):
                listing = self.copy_listing(root / str(index))
                # Use the full description to isolate scope rejection from the
                # short-description length cap. Historical notes stay immutable.
                path = listing / locale / "full-description.txt"
                path.write_text(
                    path.read_text(encoding="utf-8").rstrip("\n") + claim + "\n",
                    encoding="utf-8",
                )
                with self.subTest(locale=locale), self.assertRaisesRegex(
                    ValueError,
                    "broadens the current experimental wizard scope",
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
                    "0.1.0-preview.",
                    "1.0.0-release.",
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

            for locale in ("en-US", "de-DE", "es-ES"):
                listing = self.copy_listing(root / f"notes-{locale}")
                notes = listing / locale / "release-notes-11.txt"
                notes.write_bytes(notes.read_bytes() + b"\n")
                with self.subTest(locale=locale), self.assertRaisesRegex(
                    ValueError,
                    "Preview.11 historical release notes",
                ):
                    self.validate(listing)

    def test_next_source_version_does_not_become_a_store_publication_claim(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            project = Path(temporary) / "next.csproj"
            source = PROJECT.read_text(encoding="utf-8")
            source = re.sub(r"<ApplicationDisplayVersion>[^<]+</ApplicationDisplayVersion>",
                            "<ApplicationDisplayVersion>0.1.0-preview.999</ApplicationDisplayVersion>", source)
            source = re.sub(r"<ApplicationVersion>[^<]+</ApplicationVersion>",
                            "<ApplicationVersion>999</ApplicationVersion>", source)
            project.write_text(source, encoding="utf-8")
            result = self.validate(project=project)
            self.assertEqual("0.1.0-preview.999", result["sourceVersion"])
            self.assertNotIn("release", result)
            self.assertFalse(result["publicationAuthorized"])
            project.write_text(source.replace("<ApplicationVersion>999", "<ApplicationVersion>998"), encoding="utf-8")
            with self.assertRaisesRegex(ValueError, "version code"):
                self.validate(project=project)

    def test_historical_preview12_notes_are_not_rewritten_as_current_copy(self) -> None:
        for locale in self.module.LOCALES:
            with self.subTest(locale=locale), tempfile.TemporaryDirectory() as temporary:
                listing = self.copy_listing(Path(temporary))
                path = listing / locale / "release-notes-12.txt"
                path.write_bytes(path.read_bytes() + b"\n")
                with self.assertRaisesRegex(ValueError, "Preview.12 historical release notes drifted"):
                    self.validate(listing)

    def test_noncanonical_and_symlink_copy_fail_closed(self) -> None:
        for raw in (b"Chummer", b"Chummer\r\n", b"Chummer\n\n",
                    b"\xef\xbb\xbfChummer\n", b"Chummer\x00\n"):
            with self.subTest(raw=raw), tempfile.TemporaryDirectory() as temporary:
                listing = self.copy_listing(Path(temporary))
                (listing / "en-US" / "title.txt").write_bytes(raw)
                with self.assertRaises(ValueError):
                    self.validate(listing)
        with tempfile.TemporaryDirectory() as temporary:
            listing = self.copy_listing(Path(temporary))
            path = listing / "de-DE" / "title.txt"
            path.unlink()
            path.symlink_to(listing / "en-US" / "title.txt")
            with self.assertRaisesRegex(ValueError, "non-symlink"):
                self.validate(listing)


if __name__ == "__main__":
    unittest.main()
