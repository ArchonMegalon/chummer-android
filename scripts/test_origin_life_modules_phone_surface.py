#!/usr/bin/env python3
"""Focused source contract for the phone Origin/Life Modules boundary."""

from pathlib import Path


repo = Path(__file__).resolve().parents[1]
home = (repo / "src/Chummer.Android/Native/HomePage.cs").read_text(encoding="utf-8")
decision = (
    repo / "src/Chummer.Android/Native/OriginDossierLifeModuleDecisionPage.cs"
).read_text(encoding="utf-8")
store = (
    repo / "src/Chummer.Android/Native/OriginDossierLifeModuleDraftStore.cs"
).read_text(encoding="utf-8")

assert 'NativeTheme.SecondaryButton(PhoneStrings.Get("NewRunner", "New runner"))' in home
assert "home-new-runner" in home
assert "home-start-origin" not in home
assert "Start Origin" not in home
assert "Origin starten" not in home

for selector in (
    "origin-life-decision",
    "origin-life-story",
    "origin-life-prompt",
    "origin-life-locale",
    "origin-life-choice-",
    "origin-life-choice-source-",
    "origin-life-effect-",
    "origin-life-ltd-provenance",
    "origin-life-preview",
    "origin-life-confirm",
):
    assert selector in decision, selector

assert "VisibleStoryMarkdown" in decision
assert "DecisionPrompt" in decision
assert "activeAppLocale" in decision
assert "OriginDossierNarrativeLocalePolicy.Resolve(activeAppLocale)" in decision
assert "locale.CanRenderNarrativeLocale(_state.Locale)" in decision
assert "BoundTurnSeedDigest" in decision
assert "Origin story resource language" in decision
assert "English fallback" in decision
assert "choice.Source" in decision
assert "choice.PageReference" in decision
assert "effect.BeforeValue" in decision
assert "effect.AfterValue" in decision
assert "narrative only; mechanics unchanged" in decision
assert "_confirmChoice(selectedChoiceId, previewDigest)" in decision

assert "FileOriginDossierDraftTimelineStore" in store
assert "File.Move(temporaryPath, path, overwrite: true)" in store
assert "OriginDossierSchemas.DraftCheckpointV1" in store
assert "checkpoint.OwnerId, ownerId" in store
assert "checkpoint.WorkspaceId, workspaceId" in store

print("PASS phone Origin remains inside New Runner / SR5 Life Modules boundary")
print("PASS live decision selectors, exact effects, provenance, and atomic draft store")
