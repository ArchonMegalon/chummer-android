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
copy = (
    repo / "src/Chummer.Android/Native/AndroidSurfaceStrings.cs"
).read_text(encoding="utf-8")

assert 'NativeTheme.SecondaryButton(PhoneStrings.Get("NewRunner", "New runner"))' in home
assert "home-new-runner" in home
assert "home-start-origin" not in home
assert "Start Origin" not in home
assert "Origin starten" not in home

for selector in (
    "origin-life-decision",
    "origin-life-prompt",
    "origin-life-locale",
    "origin-life-choice-",
    "origin-life-confirm",
):
    assert selector in decision, selector

assert "OriginStoryDecisionText.Prompt(_state)" in decision
assert "OriginStoryDecisionText.Choice(_state, choice.ChoiceId)" in decision
assert "NativeTheme.BookProse(_state.VisibleStoryMarkdown)" not in decision
assert "activeAppLocale" in decision
assert "OriginDossierNarrativeLocalePolicy.Resolve(activeAppLocale)" in decision
assert "locale.CanRenderNarrativeLocale(_state.Locale)" in decision
assert "BoundTurnSeedDigest" in decision
assert '"Origin.LocaleSemantic"' in decision
assert '("Origin.LocaleSemantic", "Origin story resource language {0}; formatting locale {1}; English fallback {2}")' in copy
assert "LineBreakMode = LineBreakMode.WordWrap" in decision
assert "choice.KarmaCost >= 0 && choice.KarmaCost <= _budget.Remaining" in decision
assert "_state.CanConfirm && _storyCheckpoint?.PendingPreview?.EffectReview is not null" in decision
assert "_prepareChoice(choiceId, null)" in decision
assert "string choiceId = choice.ChoiceId" in decision
assert "generation != _renderGeneration" in decision
assert "BoundContentDigest" in decision and "BoundSourceDigest" in decision
assert "Origin.StoryChoiceReviewRequired" in decision
assert "_confirmChoice(selectedChoiceId, previewDigest)" in decision

assert "FileOriginDossierDraftTimelineStore" in store
assert "File.Move(temporaryPath, path, overwrite: true)" in store
assert "OriginDossierSchemas.DraftCheckpointV1" in store
assert "checkpoint.OwnerId, ownerId" in store
assert "checkpoint.WorkspaceId, workspaceId" in store

print("PASS phone Origin remains inside New Runner / SR5 Life Modules boundary")
print("PASS narrative copy, exact choice/budget/review binding, and atomic draft store")
