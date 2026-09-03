import xml.etree.ElementTree as ET
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
PAGE = ROOT / "src/Chummer.Android/Native/Sr5DowntimeCalendarWizardPage.cs"
LOCALIZATION = ROOT / "src/Chummer.Android/Resources/Localization"

CONTROL_SEMANTICS = {
    "_operation": "Calendar action",
    "_weekPicker": "Exact saved week",
    "_year": "First year (empty calendar only)",
    "_week": "First ISO week",
    "_notes": "Downtime notes",
    "_notesColor": "Notes color (edit only)",
    "_review": "Create exact preview",
    "_confirm": "Confirm reviewed preview",
    "_apply": "Save confirmed change",
    "_clear": "Start another calendar change",
}


def load_catalog(name: str) -> dict[str, str]:
    root = ET.parse(LOCALIZATION / name).getroot()
    return {
        entry.attrib["name"]: (entry.findtext("value") or "").strip()
        for entry in root.findall("data")
    }


def test_downtime_controls_have_localized_semantic_descriptions() -> None:
    source = PAGE.read_text(encoding="utf-8")
    for control, resource_key in CONTROL_SEMANTICS.items():
        assert (
            f'SemanticProperties.SetDescription({control}, Text("{resource_key}"));'
            in source
        ), control


def test_downtime_semantic_resource_keys_have_de_en_es_values() -> None:
    catalogs = {
        "en": load_catalog("Sr5CareerFlowStrings.resx"),
        "de": load_catalog("Sr5CareerFlowStrings.de.resx"),
        "es": load_catalog("Sr5CareerFlowStrings.es.resx"),
    }
    for resource_key in CONTROL_SEMANTICS.values():
        values = [catalogs[language].get(resource_key, "") for language in ("en", "de", "es")]
        assert all(values), resource_key
        assert len(set(values)) == 3, resource_key
