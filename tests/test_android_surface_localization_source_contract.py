from __future__ import annotations

import re
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
NATIVE = ROOT / "src" / "Chummer.Android" / "Native"
HELPER = NATIVE / "AndroidSurfaceStrings.cs"
OWNED_PAGES = (
    NATIVE / "CreationResourcesPage.cs",
    NATIVE / "CreationGearPage.cs",
    NATIVE / "OriginDossierLifeModuleDecisionPage.cs",
    NATIVE / "ShadowArchivePage.cs",
)


def _text(path: Path) -> str:
    return path.read_text(encoding="utf-8")


def test_catalogs_are_explicit_complete_and_regional_fallback_is_whole_pack() -> None:
    source = _text(HELPER)
    assert source.count("private static readonly IReadOnlyDictionary<string, string>") == 3
    assert '"de" => new(requested, "de", false, German)' in source
    assert '"en" => new(requested, "en", false, English)' in source
    assert '"es" => new(requested, "es", false, Spanish)' in source
    assert 'CultureInfo.GetCultureInfo("en-US")' in source
    assert "Every Android surface resource needs an explicit translation." in source
    assert "Android surface localization placeholder mismatch" in source
    assert "var result = source.ToDictionary" not in source

    catalog_keys = re.findall(r'\("([A-Za-z][A-Za-z0-9.]+)",\s*"', source)
    assert catalog_keys
    assert all(catalog_keys.count(key) == 3 for key in set(catalog_keys))


def test_owned_pages_resolve_one_copy_pack_and_use_resource_backed_chrome() -> None:
    for page in OWNED_PAGES:
        source = _text(page)
        assert "AndroidSurfaceCopy _copy" in source, page.name
        assert "AndroidSurfaceStrings.Resolve(" in source, page.name

        forbidden = (
            r'Title\s*=\s*"[A-Za-z]',
            r'NativeTheme\.(?:Eyebrow|Title|Body|Metric|PrimaryButton|NavigationRow)\(\s*"[A-Za-z]',
            r'Placeholder\s*=\s*"[A-Za-z]',
            r'DisplayAlertAsync\(\s*"[A-Za-z]',
            r'SemanticProperties\.SetDescription\([^;]*,\s*"[A-Za-z]',
        )
        for pattern in forbidden:
            assert re.search(pattern, source, re.DOTALL) is None, f"{page.name}: {pattern}"


def test_public_stories_filters_reader_and_accessibility_copy_are_resource_backed() -> None:
    helper = _text(HELPER)
    page = _text(NATIVE / "ShadowArchivePage.cs")
    required_keys = {
        "Stories.LanguageEdition",
        "Stories.AllLanguages",
        "Stories.Archetype",
        "Stories.AllArchetypes",
        "Stories.LanguageFilterSemantic",
        "Stories.ArchetypeFilterSemantic",
        "Stories.MetadataSemantic",
        "Stories.Read",
        "Stories.Chapter",
        "Stories.PublicDownloads",
        "Stories.SignalStory",
    }
    for key in required_keys:
        assert helper.count(f'(\"{key}\",') == 3, key
        assert any(
            token in page
            for token in (
                f'_copy[\"{key}\"]',
                f'_copy.Format(\"{key}\"',
                f'copy[\"{key}\"]',
                f'copy.Format(\"{key}\"',
            )
        ), key


def test_scope_does_not_replace_stable_ids_or_dynamic_story_content() -> None:
    origin = _text(NATIVE / "OriginDossierLifeModuleDecisionPage.cs")
    archive = _text(NATIVE / "ShadowArchivePage.cs")
    assert "_state.VisibleStoryMarkdown" in origin
    assert "_state.DecisionPrompt" in origin
    assert "choice.Label" in origin
    assert "story.Title" in archive
    assert "chapter.BodyMarkdown" in archive
    assert 'AutomationId = "origin-life-decision"' in origin
    assert 'AutomationId = "phone-archive"' in archive


def test_every_literal_resource_reference_exists_in_all_three_catalogs() -> None:
    helper = _text(HELPER)
    catalog_keys = set(re.findall(r'\("([A-Za-z][A-Za-z0-9.]+)",\s*"', helper))
    references: set[str] = set()
    for page in OWNED_PAGES:
        source = _text(page)
        references.update(re.findall(r'(?:_copy|copy)\["([A-Za-z][A-Za-z0-9.]+)"\]', source))
        references.update(re.findall(r'(?:_copy|copy)\.Format\("([A-Za-z][A-Za-z0-9.]+)"', source))
    assert references
    assert references <= catalog_keys, sorted(references - catalog_keys)
