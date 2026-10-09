#!/usr/bin/env python3
"""Emit an apply_patch patch for display-only SR5 magic names from pinned Core.

Names must match both source identity and neutral name. No rules, descriptions,
costs, mutations or runtime XML loading; --check detects a stale content repin.
"""
import argparse
import hashlib
from pathlib import Path
import xml.etree.ElementTree as ET

from materialize_skill_catalog_strings import render, token

CATALOGS = (
    ("traditions.xml", "traditions", "tradition"),
    ("streams.xml", "traditions", "stream"),
    ("powers.xml", "powers", "adept-power"),
    ("spells.xml", "spells", "spell"),
    ("complexforms.xml", "complexforms", "complex-form"),
)

# Short independent labels only for missing/renamed legacy translation rows.
# In particular Disrupt [Focus] must not inherit Disrupt [Object]'s meaning.
GERMAN_NAMES = {
    "Traditionalist Shaman": "Traditionalistischer Schamane",
    "Ancestor Shaman": "Ahnenschamane", "Faustian [Alt]": "Faustisch [Alternativ]",
    "Brocken Witches [Alt], Cauldron": "Brockenhexen [Alternativ], Kessel",
    "Brocken Witches [Alt], Helsmägde": "Brockenhexen [Alternativ], Helsmägde",
    "Frisian Magic [Alt], Frijskwart": "Friesische Magie [Alternativ], Frijskwart",
    "Frisian Magic [Alt], Sea Monk": "Friesische Magie [Alternativ], Seemönch",
    "Freudian Tradition": "Freudianische Tradition", "Guardian Order": "Wächterorden",
    "Nerve Strike": "Nervenschlag", "Nimble Fingers": "Flinke Finger",
    "Disrupt [Focus]": "Fokus stören", "Fashion": "Mode", "Interference": "Störung",
    "Sound Barrier": "Schallbarriere", "Vehicle Mask": "Fahrzeugmaske",
    "Geopathic Connection": "Geomantische Verbindung", "Yang Zhai": "Yang Zhai",
    "Krigama Carpet": "Krigama-Teppich",
}


def resources(core):
    files = ["lang/de-de_data.xml", "data/priorities.xml", "data/metatypes.xml"]
    files.extend("data/" + file for file, _, _ in CATALOGS)
    raw = {name: (core / name).read_bytes() for name in files}
    german = ET.fromstring(raw["lang/de-de_data.xml"])
    english, translated = {}, {}

    def add(key, name, label):
        if not name or key in english and english[key] != name:
            raise ValueError("Missing or ambiguous display name")
        if label and key in translated and translated[key] != label:
            raise ValueError("Ambiguous German display name")
        english[key] = name
        if label:
            translated[key] = label

    for file, section, kind in CATALOGS:
        source = ET.fromstring(raw["data/" + file])
        local = german.find(f"chummer[@file='{file}']")
        if local is None:
            raise ValueError("Missing German catalog: " + file)
        localized = {row.findtext("id"): row for row in local.find(section)}
        for row in source.find(section):
            identity, name = row.findtext("id"), row.findtext("name")
            if not identity or f"Option.{kind}.{identity}.Name" in english:
                raise ValueError("Missing or duplicate catalog identity")
            other = localized.get(identity)
            exact = other is not None and other.findtext("name") == name
            label = (other.findtext("translate") if exact else None) or GERMAN_NAMES.get(name)
            add(f"Option.{kind}.{identity}.Name", name, label)
        labels = {row.text: row.get("translate") for row in local.findall("categories/category")}
        for row in source.findall("categories/category"):
            add(f"Category.{kind}." + token(row.text), row.text, labels.get(row.text))

    # Priority talent captions are full exact display labels, never parsed for
    # numbers/selection identity. Custom captions remain untouched at runtime.
    for file, path, prefix in (("priorities.xml", ".//talents/talent", "Talent"),
                              ("metatypes.xml", "metatypes/metatype", "Metatype")):
        source = ET.fromstring(raw["data/" + file])
        local = german.find(f"chummer[@file='{file}']")
        labels = {}
        for row in local.findall(path):
            name, label = row.findtext("name"), row.findtext("translate")
            if name in labels and labels[name] != label:
                raise ValueError("Ambiguous translation: " + name)
            labels[name] = label
        for row in source.findall(path):
            name = row.findtext("name")
            add(prefix + "." + token(name), name, labels.get(name))
        if file == "metatypes.xml":
            labels = {row.text: row.get("translate") for row in local.findall("categories/category")}
            for row in source.findall("categories/category"):
                add("MetatypeCategory." + token(row.text), row.text, labels.get(row.text))

    stamps = " ".join(f"{name} sha256={hashlib.sha256(data).hexdigest()}" for name, data in raw.items())
    return {suffix: render(values, stamps).replace("materialize_skill_catalog_strings.py", "materialize_magic_catalog_strings.py")
            for suffix, values in (("", english), (".de", translated))}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--core-content-root", type=Path, required=True)
    parser.add_argument("--check", action="store_true")
    args = parser.parse_args()
    repo = Path(__file__).resolve().parents[1]
    patches = []
    for suffix, text in resources(args.core_content_root).items():
        path = Path(f"src/Chummer.Android/Resources/Localization/MagicCatalogStrings{suffix}.resx")
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
        print("PASS magic display resources match exact Core inputs and reviewed German labels")
    elif patches:
        print("*** Begin Patch\n" + "\n".join(patches) + "\n*** End Patch")


if __name__ == "__main__":
    main()
