#!/usr/bin/env python3
"""Prove one exact SR5 Playtime Short Burst on a physical API 36 ARM64 phone.

Scope is deliberately narrow: this journey proves only the existing typed
direct-weapon ``ShortBurst`` leaf for one bound weapon/clip/ammo identity.  It
does not claim damage, conditions, initiative, modifiers, run state, indirect
weapons, tablet, or unrestricted editing parity.
"""

from __future__ import annotations

from pathlib import Path
import xml.etree.ElementTree as ET

import run_api36_career_weapon_fire_e2e as weapon
import run_api36_sr5_before_run_edge_physical_e2e as lane


RECEIPT_SCHEMA = "chummer.android.sr5-playtime-weapon-physical-e2e/v1"
JOURNEY = "sr5-playtime-weapon-physical"
LANE = "playtime"
LANE_VALUE = 1
ACTION_KIND = 2  # Sr5TableWizardActionKind.FireWeapon
CHECKPOINT_KEY = "chummer.android.sr5-playtime.review.v1"
LANE_ROUTE = "sr5-career/playtime"
REVIEW_ROUTE = "sr5-career/playtime/review"
ACTION_ROUTE = "sr5-career-action-playtime"
FIXTURE_ALIAS = "Sr5PlaytimeWeaponPhysicalE2E"


SPEC = lane.LaneSpec(
    receipt_schema=RECEIPT_SCHEMA,
    journey=JOURNEY,
    lane=LANE,
    lane_value=LANE_VALUE,
    action_kind=ACTION_KIND,
    checkpoint_key=CHECKPOINT_KEY,
    lane_route=LANE_ROUTE,
    review_route=REVIEW_ROUTE,
    action_route=ACTION_ROUTE,
    fixture_alias=FIXTURE_ALIAS,
    fixture=Path(__file__).resolve().parent
    / "fixtures/sr5-playtime-weapon-physical-e2e.chum5",
    representative_action=(
        "Fire one exact three-round Short Burst from direct weapon "
        f"{weapon.WEAPON_ID}, active clip {weapon.AMMO_SLOT} (11 -> 8)"
    ),
    excluded_scope=(
        "damage",
        "conditions",
        "temporary modifiers",
        "initiative",
        "run state",
        "indirect or vehicle weapon fire",
        "tablet",
    ),
    source_paths={},
)


def require_playtime_fixture(root: ET.Element) -> None:
    expected = {
        "alias": FIXTURE_ALIAS,
        "metatype": "Human",
        "buildmethod": "Priority",
        "created": "True",
        "gameedition": "SR5",
        "karma": "19",
        "nuyen": "8765",
    }
    for field, value in expected.items():
        if root.findtext(field) != value:
            raise RuntimeError(f"Playtime fixture <{field}> is not exact")
    target = weapon.target_weapon(root)
    allowed = {
        "mode": "BF",
        "allowsingleshot": "False",
        "allowshortburst": "True",
        "allowlongburst": "False",
        "allowfullburst": "False",
        "allowsuppressive": "False",
        "shortburst": "3",
    }
    for field, value in allowed.items():
        if target.findtext(field) != value:
            raise RuntimeError(f"Playtime fixture Weapon <{field}> is not exact")
    if weapon.active_clip(root).findtext("count") != "11":
        raise RuntimeError("Playtime fixture active clip is not exactly 11 rounds")
    if weapon.linked_ammo(root).findtext("qty") != "11":
        raise RuntimeError("Playtime fixture linked ammo is not exactly 11 rounds")
    if root.findtext("./customstate") != "playtime-unrelated-root-must-survive":
        raise RuntimeError("Playtime fixture unrelated sentinel is missing")


def assert_before_state(root: ET.Element) -> dict[str, str]:
    require_playtime_fixture(root)
    return weapon.unrelated_xml_authority(root)


def assert_after_state(root: ET.Element, preserved: object) -> None:
    if not isinstance(preserved, dict):
        raise RuntimeError("Playtime before-state authority was not captured")
    weapon.assert_after(root, preserved)
    if root.findtext("./customstate") != "playtime-unrelated-root-must-survive":
        raise RuntimeError("Playtime changed unrelated root XML")


def playtime_source_paths(core_root: Path, presentation_root: Path) -> dict[str, Path]:
    return {
        "careerWeaponRequestSha256": presentation_root
        / "Chummer.Presentation/Overview/CareerWeaponFireRequest.cs",
        "careerWeaponRulesSha256": core_root
        / "Chummer.Contracts/Characters/CharacterWeaponFireRules.cs",
        "presenterMutationSha256": presentation_root
        / "Chummer.Presentation/Overview/CharacterOverviewPresenter.WorkspaceMutations.cs",
        "presenterPersistenceSha256": presentation_root
        / "Chummer.Presentation/Overview/CharacterOverviewPresenter.Persistence.cs",
        "weaponFixtureAuthorityHelperSha256": Path(weapon.__file__).resolve(),
    }


def main(argv: list[str] | None = None) -> int:
    return lane.run_main(
        argv,
        spec=SPEC,
        driver=Path(__file__).resolve(),
        fixture_validator=require_playtime_fixture,
        before_validator=assert_before_state,
        after_validator=assert_after_state,
        lane_source_paths=playtime_source_paths,
    )


if __name__ == "__main__":
    raise SystemExit(main())
