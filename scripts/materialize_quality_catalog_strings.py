#!/usr/bin/env python3
"""Emit display-only quality names from exact pinned Core, never rules/prose.

Translations require both the source ID and neutral name. --check detects stale
resources after a content repin; normal output is a patch, not a direct write.
"""
import argparse
import hashlib
import uuid
from pathlib import Path
import xml.etree.ElementTree as ET

from materialize_skill_catalog_strings import render

# Short reviewed labels for a renamed row and a duplicate German/English row
# with case-variant IDs in the legacy German catalog. Never last-row-wins.
GERMAN_NAMES = {"Shoot First, Don't Ask Questions": "Erst schießen, keine Fragen",
                "Tattoo Magic": "Tätowierungsmagie"}


def resources(core):
    raw = {name: (core / name).read_bytes()
           for name in ("data/qualities.xml", "lang/de-de_data.xml")}
    source = ET.fromstring(raw["data/qualities.xml"])
    german = ET.fromstring(raw["lang/de-de_data.xml"]).find("chummer[@file='qualities.xml']")
    if german is None:
        raise ValueError("Missing German qualities catalog")
    localized = {}
    for row in german.findall("qualities/quality"):
        identity = str(uuid.UUID(row.findtext("id")))
        localized.setdefault(identity, []).append(row)
    english, translated = {}, {}
    for row in source.findall("qualities/quality"):
        identity, name = str(uuid.UUID(row.findtext("id"))), row.findtext("name")
        key = f"Quality.{identity}.Name"
        if not identity or not name or key in english:
            raise ValueError("Missing or duplicate quality identity/name")
        labels = {other.findtext("translate") for other in localized.get(identity, [])
                  if other.findtext("name") == name and other.findtext("translate")}
        label = GERMAN_NAMES.get(name)
        if not label and len(labels) > 1:
            raise ValueError("Ambiguous German quality name: " + name)
        label = label or next(iter(labels), None)
        if not label:
            raise ValueError("Missing reviewed German quality name: " + name)
        english[key], translated[key] = name, label
    stamps = " ".join(f"{name} sha256={hashlib.sha256(data).hexdigest()}" for name, data in raw.items())
    return {suffix: render(values, stamps).replace("materialize_skill_catalog_strings.py", "materialize_quality_catalog_strings.py")
            for suffix, values in (("", english), (".de", translated))}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--core-content-root", type=Path, required=True)
    parser.add_argument("--check", action="store_true")
    args = parser.parse_args()
    repo = Path(__file__).resolve().parents[1]
    patches = []
    for suffix, text in resources(args.core_content_root).items():
        path = Path(f"src/Chummer.Android/Resources/Localization/QualityCatalogStrings{suffix}.resx")
        target = repo / path
        old = target.read_text() if target.exists() else None
        if old == text:
            continue
        if args.check:
            raise SystemExit(f"Stale or missing display resource: {path}")
        if old is None:
            patches.append(f"*** Add File: {path}\n" + "\n".join("+" + line for line in text.splitlines()))
        else:
            patches.append(f"*** Update File: {path}\n@@\n" + "\n".join("-" + line for line in old.splitlines())
                           + "\n" + "\n".join("+" + line for line in text.splitlines()))
    if args.check:
        print("PASS quality display resources match exact Core inputs and reviewed German labels")
    elif patches:
        print("*** Begin Patch\n" + "\n".join(patches) + "\n*** End Patch")


if __name__ == "__main__":
    main()
