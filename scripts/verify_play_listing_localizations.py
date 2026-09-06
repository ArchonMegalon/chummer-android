#!/usr/bin/env python3
"""Verify the exact, truthful Preview.12 Google Play listing localizations."""

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
    "release-notes-12.txt",
)
LIMITS = {
    "title.txt": 30,
    "short-description.txt": 80,
    "full-description.txt": 4000,
    "release-notes-12.txt": 500,
}
PACKAGE_ID = "com.myexternalbrain.chummer"
VERSION_NAME = "0.1.0-preview.12"
VERSION_CODE = "12"
FULL_DESCRIPTION_RELEASE_PREFIX = f"Chummer Preview.{VERSION_CODE} "
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

REQUIRED_FRAGMENTS = {
    "en-US": {
        "short-description.txt": (
            "Seven SR5 phone flows",
            "wizard routes are experimental",
            "Internal test",
        ),
        "full-description.txt": (
            "Internal testing build for phones",
            "covers exactly seven SR5 flows",
            "Additional visible wizard routes remain available for feedback",
            "Experimental — not covered by the current Preview authority",
            "limited to the seven named SR5 phone flows",
            "Full Editing",
            "tablet or foldable support",
            "SR4 or SR6 creation",
            "Rook or live-avatar support",
            "public availability",
            "production release are not included",
        ),
        "release-notes-12.txt": (
            "Internal testing build for phones",
            "Settings respond faster",
            "seven named flows",
            "Additional wizard routes are marked Experimental",
            "not covered by the current Preview authority",
            "remain outside this test",
        ),
    },
    "de-DE": {
        "short-description.txt": (
            "Sieben SR5-Flows",
            "Wizard-Routen sind experimentell",
            "Interner Telefontest",
        ),
        "full-description.txt": (
            "interne Testversion für Telefone",
            "deckt genau sieben SR5-Flows ab",
            "Zusätzliche sichtbare Wizard-Routen bleiben für Feedback verfügbar",
            "Experimentell — nicht durch die aktuelle Preview-Autorität abgedeckt",
            "auf die sieben genannten SR5-Flows für Telefone begrenzt",
            "Vollständige Bearbeitung",
            "Tablet- oder Foldable-Unterstützung",
            "SR4- oder SR6-Erstellung",
            "Rook- oder Live-Avatar-Unterstützung",
            "öffentliche Verfügbarkeit",
            "Produktivveröffentlichung",
        ),
        "release-notes-12.txt": (
            "interner Telefontest",
            "Einstellungen sind klarer und reaktionsschneller",
            "Sieben Flows",
            "Weitere Wizard-Routen: Experimentell",
            "nicht durch die aktuelle Preview-Autorität abgedeckt",
            "nicht Teil dieses Tests",
        ),
    },
    "es-ES": {
        "short-description.txt": (
            "Siete flujos SR5",
            "rutas son experimentales",
            "Prueba interna en móvil",
        ),
        "full-description.txt": (
            "versión de prueba interna para teléfonos",
            "cubre exactamente siete flujos de SR5",
            "rutas adicionales visibles de los asistentes siguen disponibles",
            "Experimental — no cubierta por la autoridad de la vista previa actual",
            "limitada a los siete flujos de SR5 para teléfonos indicados",
            "edición completa",
            "tabletas o los plegables",
            "creación para SR4 o SR6",
            "Rook o los avatares en directo",
            "disponibilidad pública",
            "publicación en producción",
        ),
        "release-notes-12.txt": (
            "prueba interna para teléfonos",
            "Ajustes más ágiles",
            "Siete flujos",
            "Otras rutas: Experimental",
            "no cubiertas por la autoridad de la vista previa actual",
            "no forman parte de esta prueba",
        ),
    },
}

EXACT_FLOW_LABELS = {
    "en-US": {
        "full-description.txt": (
            "Creation Prerequisite",
            "Career Active Skill Advance",
            "Career Weapon Fire",
            "Before Run Edge",
            "Playtime Short Burst",
            "Downtime Calendar",
            "After Run Settlement",
        ),
        "release-notes-12.txt": (
            "Creation Prerequisite",
            "Career Active Skill Advance",
            "Career Weapon Fire",
            "Before Run Edge",
            "Playtime Short Burst",
            "Downtime Calendar",
            "After Run Settlement",
        ),
    },
    "de-DE": {
        "full-description.txt": (
            "Erstellungs-Voraussetzungen",
            "Steigerung einer aktiven Fertigkeit",
            "Karriere-Waffenfeuer",
            "Edge vor dem Run",
            "kurzer Spielzeit-Einsatz",
            "Auszeit-Kalender",
            "Abrechnung nach dem Run",
        ),
        "release-notes-12.txt": (
            "Erstellungs-Voraussetzungen",
            "aktive Fertigkeit steigern",
            "Karriere-Waffenfeuer",
            "Edge vor dem Run",
            "kurzer Spielzeit-Einsatz",
            "Auszeit-Kalender",
            "Abrechnung nach dem Run",
        ),
    },
    "es-ES": {
        "full-description.txt": (
            "Requisitos de creación",
            "Avance de habilidad activa en Carrera",
            "Disparo de arma en Carrera",
            "Edge antes de la misión",
            "Cambio breve durante la partida",
            "Calendario de tiempo libre",
            "Liquidación después de la misión",
        ),
        "release-notes-12.txt": (
            "Requisitos de creación",
            "Avance de habilidad activa",
            "Disparo de arma",
            "Edge antes de la misión",
            "Cambio breve durante la partida",
            "Calendario de tiempo libre",
            "Liquidación después de la misión",
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
    raw = _regular_file(path, label).read_text(encoding="utf-8")
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
    for name in ("short-description.txt", "full-description.txt", "release-notes-12.txt"):
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
            raise ValueError(f"{locale} claims runtime proof before a green aggregate")
    for pattern in FORBIDDEN_BROAD_WIZARD_CLAIMS[locale]:
        if re.search(pattern, candidate, flags=re.IGNORECASE | re.DOTALL):
            raise ValueError(f"{locale} broadens the exact seven-flow wizard scope")


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
    if values["ApplicationDisplayVersion"] != [VERSION_NAME]:
        raise ValueError("Android version name is not exact Preview.12")
    if values["ApplicationVersion"] != [VERSION_CODE]:
        raise ValueError("Android version code is not exact Preview.12")
    return PACKAGE_ID, VERSION_NAME, VERSION_CODE


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
    _project_identity(project)
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
        expected_files.add("release-notes-11.txt")
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
        if (
            not fields["full-description.txt"].startswith(FULL_DESCRIPTION_RELEASE_PREFIX)
            or re.findall(r"\bPreview\.([0-9]+)\b", fields["full-description.txt"])
            != [VERSION_CODE]
        ):
            raise ValueError(
                f"{locale}/full-description.txt must describe the exact Preview.12 candidate"
            )
        for name, fragments in REQUIRED_FRAGMENTS[locale].items():
            for fragment in fragments:
                if fragment not in fields[name]:
                    raise ValueError(
                        f"{locale}/{name} is missing exact wizard-scope or non-claim copy"
                    )
        for name, labels in EXACT_FLOW_LABELS[locale].items():
            for label in labels:
                if label not in fields[name]:
                    raise ValueError(
                        f"{locale}/{name} must name every exact required flow"
                    )
        _reject_positive_or_unproven_claims(locale, fields)
        localized_fields[locale] = fields
        lengths[locale] = locale_lengths

    for name in (
        "short-description.txt",
        "full-description.txt",
        "release-notes-12.txt",
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

    return {
        "packageId": PACKAGE_ID,
        "release": VERSION_NAME,
        "trackPosture": "internal_testing_only",
        "scope": "sr5_phone_wizards_only",
        "wizardGateSha256": wizard_gate_digest,
        "requiredJourneys": list(required_journeys),
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
        f"locales={len(result['locales'])} journeys={len(result['requiredJourneys'])} "
        f"scope={result['scope']} gate_sha256={result['wizardGateSha256']} "
        "publication_authorized=false"
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
