#!/usr/bin/env python3
"""Validate current Internal store copy; preserve historical release evidence.

Copy validation grants neither runtime qualification nor Play publication authority.
The default Play en-GB listing deliberately reuses the en-US English source.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import re
import xml.etree.ElementTree as ET
from pathlib import Path
from typing import Any


LOCALES = ("en-US", "de-DE", "es-ES")
CURRENT_FILES = (
    "title.txt",
    "short-description.txt",
    "full-description.txt",
)
LIMITS = {
    "title.txt": 30,
    "short-description.txt": 80,
    "full-description.txt": 4000,
    "release-notes-12.txt": 500,
}
PACKAGE_ID = "com.myexternalbrain.chummer"
DEFAULT_STORE_LOCALE = "en-GB"
DEFAULT_SOURCE_LOCALE = "en-US"
PREVIEW12_NOTES_SHA256 = {
    "en-US": "c840e9d3c75c0aa2e59b9e96b7d463869d714965b43fa0140a2ffec682263586",
    "de-DE": "020f80bff2d89a425115b0727848fd940db9a8b1798acce6f5666f703f7cb1a1",
    "es-ES": "ebc9e1d7b1cb73178f9993672fc33d4753588822c762880bf28f934a4b611a0d",
}
DATA_SAFETY_SHA256 = "0379209d99ba666ba72a150d88c1855e6b4db17d64199402eb0f9bb80f4fa0f3"
PREVIEW10_EVIDENCE_SHA256 = "8f245fcf6e8fd62d6ed2d7e75170617d3c5430e024ce14ab77535ca1c57fece9"
PREVIEW10_NOTES_SHA256 = "b45905778f70e9c459b37c5a450a75800aca780f8ca4a4c8aa176f685cb39037"
PREVIEW11_NOTES_SHA256 = {
    "en-US": "00aa91900a4090e14140b91626be4443367fa755e73fbe675ea9ff97745bb422",
    "de-DE": "91ae2b99da5315da43e7e02b426058ae78d9e04a3116a56518c65d4ef69c327e",
    "es-ES": "86580a5027a052ceb5291482f87b99eabeffe1b844c5a611e1900e0a4cd8e0c1",
}
WIZARD_GATE_SHA256 = "c867b4fd8c2a771e3ddb4c3e20c0b843ea87510a197b476c7ce75dc013fec7b4"
REQUIRED_GATE_JOURNEYS = (
    "creation-prerequisite",
    "career-active-skill-advance",
    "career-weapon-fire",
    "before-run-edge",
    "playtime-short-burst",
    "downtime-calendar",
    "after-run-settlement",
)

# Current copy is version-neutral: source versions are not distribution evidence.
REQUIRED_FRAGMENTS = {
    "en-US": {
        "short-description.txt": ("SR5", "EPUB", "Internal test"),
        "full-description.txt": (
            "experimental SR5 runner companion for phones",
            "available to invited Internal testers",
            "not every build method or Career action is complete",
            "cumulative rules effects",
            "Confirm your metatype, birth background and childhood",
            "Read accepted chapters", "export them as an EPUB",
            "included offline when available",
            "explicit consent to share character facts", "available provider credits",
            "An entire accepted Origin book and the complete Life Modules-to-Career journey are still being completed",
            "Audiobooks are not included", "Account linking remains optional",
            "a physical Play-managed installation is not yet verified",
            "Visible experimental features do not all have the same test coverage",
        ),
    },
    "de-DE": {
        "short-description.txt": ("SR5", "EPUB", "Interner Test"),
        "full-description.txt": (
            "experimenteller SR5-Runner-Begleiter für Telefone",
            "im internen Test für eingeladene Tester",
            "nicht jede Bauart oder Karriere-Aktion ist fertig",
            "kumulierte Regeleffekte", "Bestätige Metatyp, Geburtsumstände und Kindheit",
            "Lies angenommene Kapitel", "exportiere sie als EPUB",
            "offline enthalten, sofern verfügbar",
            "ausdrückliche Zustimmung zur Weitergabe der Charakterfakten", "verfügbare Anbieter-Credits",
            "Ein vollständig angenommenes Origin-Buch und der gesamte Life-Modules-Weg bis zur Karriere sind noch in Arbeit",
            "Hörbücher sind nicht enthalten", "Kontoverknüpfung bleibt optional",
            "eine physische Installation über Play ist noch nicht verifiziert",
            "Sichtbare experimentelle Funktionen haben nicht alle dieselbe Testabdeckung",
        ),
    },
    "es-ES": {
        "short-description.txt": ("SR5", "EPUB", "Prueba interna"),
        "full-description.txt": (
            "asistente experimental de runners de SR5 para teléfonos",
            "en prueba interna para participantes invitados",
            "no todos los métodos ni las acciones de Carrera están completos",
            "efectos acumulados de las reglas", "Confirma metatipo, circunstancias del nacimiento e infancia",
            "Lee los capítulos aceptados", "expórtalos como EPUB",
            "sin conexión cuando están disponibles",
            "consentimiento explícito para compartir los datos del personaje", "créditos disponibles del proveedor",
            "Un libro de Origin aceptado completo y toda la ruta de Life Modules hasta Carrera siguen en desarrollo",
            "No se incluyen audiolibros", "vinculación sigue siendo opcional",
            "la instalación física mediante Play aún no está verificada",
            "No todas las funciones experimentales visibles tienen la misma cobertura de pruebas",
        ),
    },
}

FORBIDDEN_BROAD_WIZARD_CLAIMS = {
    "en-US": (
        r"\ball (?:sr5 )?(?:creation |career |table )?wizards?\b",
        r"\bincluded (?:sr5 )?career wizards?\b",
        r"\bpriority creation through (?:attributes|skills|qualities)\b",
    ),
    "de-DE": (
        r"\balle (?:sr5-)?(?:erstellungs-|karriere-|tisch-)?wizards?\b",
        r"\benthaltenen (?:sr5-)?karriere-wizards?\b",
        r"\bprioritätserstellung (?:durch|mit) (?:attribute|fertigkeiten|qualitäten)\b",
    ),
    "es-ES": (
        r"\btodos los asistentes? (?:de )?(?:creación|carrera|mesa)\b",
        r"\basistentes? (?:de )?carrera (?:de sr5 )?incluidos?\b",
        r"\bcreación por prioridades (?:con|mediante) (?:atributos|habilidades|cualidades)\b",
    ),
}

NONCLAIM_SENTENCES = {
    "en-US": {
        "full-description.txt": (
            "Full Editing, tablet or foldable support, SR4 or SR6 creation, "
            "Rook or live-avatar support, public availability, and production "
            "release are not included."
        ),
        "release-notes-12.txt": (
            "Full Editing, tablet or foldable support, SR4 or SR6 creation, and "
            "Rook or live-avatar support remain outside this test."
        ),
    },
    "de-DE": {
        "full-description.txt": (
            "Vollständige Bearbeitung, Tablet- oder Foldable-Unterstützung, SR4- "
            "oder SR6-Erstellung, Rook- oder Live-Avatar-Unterstützung, öffentliche "
            "Verfügbarkeit und eine Produktivveröffentlichung sind nicht Teil dieses Tests."
        ),
        "release-notes-12.txt": (
            "Vollständige Bearbeitung, Tablets/Foldables, SR4/SR6 und "
            "Rook/Live-Avatar sind nicht Teil dieses Tests."
        ),
    },
    "es-ES": {
        "full-description.txt": (
            "La edición completa, las tabletas o los plegables, la creación para SR4 "
            "o SR6, Rook o los avatares en directo, la disponibilidad pública y la "
            "publicación en producción no forman parte de esta prueba."
        ),
        "release-notes-12.txt": (
            "La edición completa, tabletas/plegables, SR4/SR6 y "
            "Rook/avatares en directo no forman parte de esta prueba."
        ),
    },
}

FORBIDDEN_SCOPE_TERMS = {
    "en-US": (
        r"\bfull editing\b", r"\btablets?\b", r"\bfoldables?\b", r"\bsr4\b",
        r"\bsr6\b", r"\brook\b", r"\blive[- ]avatars?\b", r"\bpublic(?:ly)?\b",
        r"\bproduction\b",
    ),
    "de-DE": (
        r"\bvollständige bearbeitung\b", r"\btablet", r"\bfoldable", r"\bsr4\b",
        r"\bsr6\b", r"\brook\b", r"\blive-avatar", r"\böffentlich",
        r"\bproduktiv",
    ),
    "es-ES": (
        r"\bedición completa\b", r"\btabletas?\b", r"\bplegables?\b", r"\bsr4\b",
        r"\bsr6\b", r"\brook\b", r"\bavatares? en directo\b", r"\bpúblic",
        r"\bproducción\b",
    ),
}

FORBIDDEN_UNPROVEN_CLAIMS = {
    "en-US": (r"\bdigest-bound\b", r"\bproof\b", r"\bproven\b", r"\baggregate\b.*\bpass"),
    "de-DE": (r"\bdigest-gebunden", r"\bnachweis", r"\bnachgewiesen", r"\baggregat\b.*\bbestanden"),
    "es-ES": (r"vinculad[ao]s? por resumen", r"\bdemostrad[ao]", r"\bagregado\b.*\bsuperad"),
}


def _regular_file(path: Path, label: str) -> Path:
    if path.is_symlink() or not path.is_file():
        raise ValueError(f"{label} must be one regular non-symlink file")
    return path


def _sha256(path: Path, label: str) -> str:
    return hashlib.sha256(_regular_file(path, label).read_bytes()).hexdigest()


def _read_listing(path: Path, label: str) -> str:
    raw = _regular_file(path, label).read_bytes().decode("utf-8")
    if raw.startswith("\ufeff") or "\r" in raw or "\x00" in raw:
        raise ValueError(f"{label} must be canonical UTF-8 text")
    if not raw.endswith("\n") or raw.endswith("\n\n"):
        raise ValueError(f"{label} must have exactly one final newline")
    value = raw[:-1]
    if not value or value != value.strip():
        raise ValueError(f"{label} must contain bounded non-whitespace copy")
    return value


def _read_wizard_gate(path: Path) -> tuple[str, tuple[str, ...]]:
    digest = _sha256(path, "SR5 wizard gate authority")
    if digest != WIZARD_GATE_SHA256:
        raise ValueError("SR5 wizard gate authority drifted from the reviewed exact bytes")
    raw = _regular_file(path, "SR5 wizard gate authority").read_text(encoding="utf-8")
    gate = json.loads(raw)
    if not isinstance(gate, dict):
        raise ValueError("SR5 wizard gate authority must be one object")
    required = gate.get("requiredJourneys")
    journey_ids = tuple(
        item.get("matrixJourney", "")
        for item in required
        if isinstance(item, dict)
    ) if isinstance(required, list) else ()
    expected_exclusion = [
        {
            "matrixJourney": "full-editing",
            "status": "deferred",
            "evidenceClass": "informational_only",
            "maySatisfyRequiredJourney": False,
        }
    ]
    required_nonclaims = {
        "full_editing_pass",
        "exhaustive_chummer5_edit_parity",
        "tablet_readiness",
        "google_play_upload",
        "public_release_readiness",
        "publication_authority",
    }
    if (
        gate.get("schema")
        != "chummer.android.api36-sr5-wizard-gate-authority/v1"
        or gate.get("authorityClass") != "internal_phone_beta_sr5_wizard_only"
        or gate.get("proofScope") != "sr5_wizards_only"
        or gate.get("requiredJourneyCount") != len(REQUIRED_GATE_JOURNEYS)
        or journey_ids != REQUIRED_GATE_JOURNEYS
        or gate.get("excludedFromGate") != expected_exclusion
        or gate.get("publicationAuthorized") is not False
        or set(gate.get("doesNotAssert", [])) != required_nonclaims
    ):
        raise ValueError("SR5 wizard gate authority is not the exact seven-journey scope")
    return digest, journey_ids


def _reject_positive_or_unproven_claims(
    locale: str,
    fields: dict[str, str],
) -> None:
    scrubbed: list[str] = []
    for name in ("short-description.txt", "full-description.txt"):
        value = fields[name]
        permitted = NONCLAIM_SENTENCES.get(locale, {}).get(name)
        if permitted is not None:
            if value.count(permitted) != 1:
                raise ValueError(f"{locale}/{name} must contain the exact bounded non-claim")
            value = value.replace(permitted, "", 1)
        scrubbed.append(value)
    candidate = "\n".join(scrubbed)
    for pattern in FORBIDDEN_SCOPE_TERMS[locale]:
        if re.search(pattern, candidate, flags=re.IGNORECASE):
            raise ValueError(f"{locale} contains a prohibited positive product claim")
    for pattern in FORBIDDEN_UNPROVEN_CLAIMS[locale]:
        if re.search(pattern, candidate, flags=re.IGNORECASE | re.DOTALL):
            raise ValueError(f"{locale} claims runtime proof from store copy")
    for pattern in FORBIDDEN_BROAD_WIZARD_CLAIMS[locale]:
        if re.search(pattern, candidate, flags=re.IGNORECASE | re.DOTALL):
            raise ValueError(f"{locale} broadens the current experimental wizard scope")


def _project_identity(project: Path) -> tuple[str, str, str]:
    root = ET.parse(_regular_file(project, "Android project")).getroot()
    values: dict[str, list[str]] = {
        "ApplicationId": [],
        "ApplicationDisplayVersion": [],
        "ApplicationVersion": [],
    }
    for node in root.iter():
        name = node.tag.rsplit("}", 1)[-1]
        if name in values:
            values[name].append((node.text or "").strip())
    if values["ApplicationId"] != [PACKAGE_ID]:
        raise ValueError("Android package identity is not exact")
    if len(values["ApplicationDisplayVersion"]) != 1:
        raise ValueError("Android version name must be unique")
    version_name = values["ApplicationDisplayVersion"][0]
    match = re.fullmatch(r"0\.1\.0-preview\.([1-9][0-9]*)", version_name)
    if match is None:
        raise ValueError("Android version name is not an Internal preview identity")
    version_code = match.group(1)
    if values["ApplicationVersion"] != [version_code]:
        raise ValueError("Android version code does not match its version name")
    return PACKAGE_ID, version_name, version_code


def _supported_ui_locales(policy: Path) -> tuple[str, ...]:
    source = _regular_file(policy, "phone locale policy").read_text(encoding="utf-8")
    locales = tuple(
        re.findall(
            r'public const string [A-Za-z]+Locale = "([a-z]{2}-[A-Z]{2})";',
            source,
        )
    )
    if set(locales) != set(LOCALES) or len(locales) != len(LOCALES):
        raise ValueError("Play listing locales do not match the exact supported UI languages")
    return locales


def validate_listing(
    listing_root: Path,
    *,
    project: Path,
    phone_locale_policy: Path,
    data_safety: Path,
    preview10_evidence: Path,
    wizard_gate_authority: Path,
) -> dict[str, Any]:
    if listing_root.is_symlink() or not listing_root.is_dir():
        raise ValueError("Play listing root must be one real directory")
    _, source_version, _ = _project_identity(project)
    _supported_ui_locales(phone_locale_policy)
    wizard_gate_digest, required_journeys = _read_wizard_gate(wizard_gate_authority)
    if _sha256(data_safety, "Data safety source") != DATA_SAFETY_SHA256:
        raise ValueError("Data safety source drifted from the reviewed exact bytes")
    if (
        _sha256(preview10_evidence, "Preview.10 publication evidence")
        != PREVIEW10_EVIDENCE_SHA256
    ):
        raise ValueError("Preview.10 historical publication evidence drifted")

    actual_locales = {entry.name for entry in listing_root.iterdir()}
    if actual_locales != set(LOCALES):
        raise ValueError("Play listing locale set does not match supported app languages")

    lengths: dict[str, dict[str, int]] = {}
    localized_fields: dict[str, dict[str, str]] = {}
    for locale in LOCALES:
        locale_root = listing_root / locale
        if locale_root.is_symlink() or not locale_root.is_dir():
            raise ValueError(f"Play listing locale {locale} must be one real directory")
        expected_files = set(CURRENT_FILES)
        expected_files.update(f"release-notes-{version}.txt" for version in (11, 12, 15, 16, 17))
        if locale == "en-US":
            expected_files.update(f"release-notes-{version}.txt" for version in range(1, 11))
        actual_files = {entry.name for entry in locale_root.iterdir()}
        if actual_files != expected_files:
            raise ValueError(f"Play listing files are not exact for {locale}")

        fields: dict[str, str] = {}
        locale_lengths: dict[str, int] = {}
        for name in CURRENT_FILES:
            value = _read_listing(locale_root / name, f"{locale}/{name}")
            if len(value) > LIMITS[name]:
                raise ValueError(f"{locale}/{name} exceeds the Google Play length limit")
            fields[name] = value
            locale_lengths[name] = len(value)
        if fields["title.txt"] != "Chummer":
            raise ValueError(f"Play title is not the exact product identity for {locale}")
        if any(re.search(r"\b(?:Preview[. ]?|version(?:Code)?\s*)[0-9]+", value, re.IGNORECASE)
               for value in fields.values()):
            raise ValueError(f"{locale} current copy must be version-neutral, not a release receipt")
        for name, fragments in REQUIRED_FRAGMENTS[locale].items():
            for fragment in fragments:
                if fragment not in fields[name]:
                    raise ValueError(
                        f"{locale}/{name} is missing exact wizard-scope or non-claim copy"
                    )
        _reject_positive_or_unproven_claims(locale, fields)
        localized_fields[locale] = fields
        lengths[locale] = locale_lengths

    for name in (
        "short-description.txt",
        "full-description.txt",
    ):
        values = {localized_fields[locale][name] for locale in LOCALES}
        if len(values) != len(LOCALES):
            raise ValueError(f"{name} contains an untranslated locale fallback")

    if (
        _sha256(
            listing_root / "en-US" / "release-notes-10.txt",
            "Preview.10 release notes",
        )
        != PREVIEW10_NOTES_SHA256
    ):
        raise ValueError("Preview.10 historical release notes drifted")

    for locale, expected_digest in PREVIEW11_NOTES_SHA256.items():
        if (
            _sha256(
                listing_root / locale / "release-notes-11.txt",
                f"{locale} Preview.11 release notes",
            )
            != expected_digest
        ):
            raise ValueError(f"{locale} Preview.11 historical release notes drifted")

    for locale, expected_digest in PREVIEW12_NOTES_SHA256.items():
        if _sha256(listing_root / locale / "release-notes-12.txt",
                   f"{locale} Preview.12 release notes") != expected_digest:
            raise ValueError(f"{locale} Preview.12 historical release notes drifted")

    return {
        "packageId": PACKAGE_ID,
        "sourceVersion": source_version,
        "defaultStoreLocale": DEFAULT_STORE_LOCALE,
        "defaultSourceLocale": DEFAULT_SOURCE_LOCALE,
        "copyOnly": True,
        "trackPosture": "internal_testing_only",
        "scope": "experimental_sr5_phone_wizards_and_origin_reader",
        "historicalWizardGateSha256": wizard_gate_digest,
        "historicalRequiredJourneys": list(required_journeys),
        "runtimeQualificationAsserted": False,
        "locales": list(LOCALES),
        "lengths": lengths,
        "publicationAuthorized": False,
    }


def main() -> int:
    repo = Path(__file__).resolve().parents[1]
    parser = argparse.ArgumentParser()
    parser.add_argument("--listing-root", type=Path, default=repo / "play" / "listing")
    parser.add_argument(
        "--project",
        type=Path,
        default=repo / "src" / "Chummer.Android" / "Chummer.Android.csproj",
    )
    parser.add_argument(
        "--phone-locale-policy",
        type=Path,
        default=repo / "src" / "Chummer.Android" / "Native" / "PhoneLocalePolicy.cs",
    )
    parser.add_argument("--data-safety", type=Path, default=repo / "play" / "data-safety.md")
    parser.add_argument(
        "--preview10-evidence",
        type=Path,
        default=repo / "play" / "evidence" / "preview10-internal-publication.json",
    )
    parser.add_argument(
        "--wizard-gate-authority",
        type=Path,
        default=repo / "eng" / "api36-sr5-wizard-gate-authority.json",
    )
    arguments = parser.parse_args()
    try:
        result = validate_listing(
            arguments.listing_root.absolute(),
            project=arguments.project.absolute(),
            phone_locale_policy=arguments.phone_locale_policy.absolute(),
            data_safety=arguments.data_safety.absolute(),
            preview10_evidence=arguments.preview10_evidence.absolute(),
            wizard_gate_authority=arguments.wizard_gate_authority.absolute(),
        )
    except (OSError, UnicodeError, ValueError, ET.ParseError) as error:
        raise SystemExit(f"Play listing localization is invalid: {error}") from error
    print(
        "play_listing_localizations=pass "
        f"locales={len(result['locales'])} default={result['defaultStoreLocale']} "
        "copy_only=true historical_gate_preserved=true "
        "runtime_qualification_asserted=false "
        "publication_authorized=false"
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
