"""Synthetic ZIP fixtures only: no download, SDK, extracted cache, or origin proof."""
import copy
import hashlib
import io
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
from unittest import mock
import zipfile

REPO = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(REPO / "scripts"))
import prepare_android_aar_cache as aar


class AndroidAarCacheTests(unittest.TestCase):
    def setUp(self):
        temporary = tempfile.TemporaryDirectory(prefix="AAR input ")
        self.addCleanup(temporary.cleanup)
        self.root = Path(temporary.name)
        self.source = self.root / "source"
        self.source.mkdir(mode=0o700)
        self.cache, self.spec, self.lock = (self.root / name for name in ("cache", "aar.json", "packages.lock.json"))
        self.document = json.loads((REPO / "eng/android-aar-inputs.lock.json").read_bytes())
        for row in self.document["archives"]:
            buffer = io.BytesIO()
            with zipfile.ZipFile(buffer, "w", zipfile.ZIP_DEFLATED) as archive:
                archive.writestr("AndroidManifest.xml", b"synthetic manifest")
                archive.writestr("classes.jar", row["packageId"].encode())
            data = buffer.getvalue()
            path = self.source / row["fileName"]
            path.write_bytes(data); path.chmod(0o600)
            row.update(sizeBytes=len(data), sha256=hashlib.sha256(data).hexdigest())
        self.graph = {"version": 1, "dependencies": {"net10.0-android36.0": {
            row["packageId"]: {"type": "Transitive", "resolved": row["packageVersion"]}
            for row in self.document["archives"]}, "net10.0-android36.0/android-arm64": {}}}
        self.save()

    def save(self):
        for path, value in ((self.spec, self.document), (self.lock, self.graph)):
            path.write_text(json.dumps(value)); path.chmod(0o600)

    def run_helper(self, operation, **kwargs):
        return aar.prepare(operation, source=self.source, cache=None if operation == "validate" else self.cache,
                           aar_lock=self.spec, project_lock=self.lock, **kwargs)

    def consume(self, rows=None, root=None):
        # Model only the exact 0.11.4 DLL's Uncompressed move/marker/lock shape;
        # this is not task execution or proof of a successful Android publish.
        root = self.cache if root is None else root
        for row in self.document["archives"] if rows is None else rows:
            name, stem = row["fileName"], Path(row["fileName"]).stem
            nested = root / stem
            nested.mkdir(mode=0o700)
            (root / name).rename(nested / name)
            for suffix, data in ((".unpacked", aar.UNPACKED_MARKER), (".locked", b"")):
                sidecar = root / (stem + suffix)
                sidecar.write_bytes(data); sidecar.chmod(0o600)

    def test_seed_is_flat_fresh_and_verify_accepts_exact_consumption_without_reseeding(self):
        self.assertEqual(b"This marks that the extraction completed successfully", aar.UNPACKED_MARKER)
        self.assertEqual(5, self.run_helper("validate")["archiveCount"])
        result = self.run_helper("seed")
        self.assertFalse(result["originAuthenticated"])
        self.assertEqual(sorted(p.name for p in self.source.iterdir()), sorted(p.name for p in self.cache.iterdir()))
        for path in self.cache.iterdir():
            self.assertEqual(0o600, path.stat().st_mode & 0o777)
            self.assertEqual((self.source / path.name).read_bytes(), path.read_bytes())
        self.assertEqual(5, self.run_helper("verify")["archiveCount"])
        self.consume(self.document["archives"][:1])
        self.assertEqual(5, self.run_helper("verify")["archiveCount"])
        self.consume(self.document["archives"][1:])
        before = {str(p.relative_to(self.cache)): p.read_bytes() for p in self.cache.rglob("*") if p.is_file()}
        self.assertEqual(5, self.run_helper("verify")["archiveCount"])
        self.assertEqual(before, {str(p.relative_to(self.cache)): p.read_bytes()
                                 for p in self.cache.rglob("*") if p.is_file()})
        self.assertFalse(any((self.cache / row["fileName"]).exists() for row in self.document["archives"]))
        row = self.document["archives"][0]
        (self.cache / Path(row["fileName"]).stem / row["fileName"]).write_bytes(b"changed")
        with self.assertRaises(ValueError):
            self.run_helper("verify")

    def test_consumed_source_or_preseeded_cache_is_never_admitted(self):
        self.consume(root=self.source)
        for operation in ("validate", "seed"):
            with self.subTest(operation=operation), self.assertRaisesRegex(ValueError, "exactly five originals"):
                self.run_helper(operation)
        self.assertFalse(self.cache.exists())

    def test_cache_rejects_missing_ambiguous_partial_casefold_and_extra_layouts(self):
        faults = ("missing-payload", "missing-marker", "missing-lock", "both", "extra-root",
                  "extra-nested", "nested-directory", "uppercase-payload", "uppercase-stem",
                  "duplicate-case", "flat-with-marker", "flat-with-directory", "extra-sha256")
        for index, fault in enumerate(faults):
            with self.subTest(fault=fault):
                self.cache = self.root / ("cache-" + str(index))
                self.run_helper("seed")
                row = self.document["archives"][0]
                name, stem = row["fileName"], Path(row["fileName"]).stem
                if not fault.startswith("flat-"):
                    self.consume()
                original = self.cache / stem / name
                if fault == "missing-payload": original.unlink()
                elif fault == "missing-marker": (self.cache / (stem + ".unpacked")).unlink()
                elif fault == "missing-lock": (self.cache / (stem + ".locked")).unlink()
                elif fault == "both": (self.cache / name).write_bytes(original.read_bytes())
                elif fault == "extra-root": (self.cache / "unexpected").write_bytes(b"fixture")
                elif fault == "extra-nested": (original.parent / "classes.jar").write_bytes(b"fixture")
                elif fault == "nested-directory": (original.parent / "extracted").mkdir()
                elif fault == "uppercase-payload": original.rename(original.with_name(name.upper()))
                elif fault == "uppercase-stem": original.parent.rename(self.cache / stem.upper())
                elif fault == "duplicate-case": (original.parent / name.upper()).write_bytes(b"fixture")
                elif fault == "flat-with-marker": (self.cache / (stem + ".unpacked")).write_bytes(aar.UNPACKED_MARKER)
                elif fault == "flat-with-directory": (self.cache / stem).mkdir()
                elif fault == "extra-sha256": (self.cache / (stem + ".sha256")).write_bytes(b"0" * 64)
                with self.assertRaises((ValueError, OSError)):
                    self.run_helper("verify")

    def test_consumed_files_and_sidecars_reject_links_special_modes_and_wrong_bytes(self):
        faults = ("wrong-payload", "wrong-marker", "oversized-marker", "empty-marker", "nonempty-lock",
                  "payload-link", "marker-link", "lock-link", "payload-hardlink", "marker-hardlink",
                  "lock-hardlink", "payload-fifo", "marker-fifo", "lock-fifo", "payload-directory",
                  "marker-directory", "lock-directory", "public-payload", "public-marker", "public-lock",
                  "public-directory", "directory-link")
        for index, fault in enumerate(faults):
            with self.subTest(fault=fault):
                self.cache = self.root / ("cache-" + str(index))
                self.run_helper("seed"); self.consume()
                row = self.document["archives"][0]
                name, stem = row["fileName"], Path(row["fileName"]).stem
                paths = {"payload": self.cache / stem / name,
                         "marker": self.cache / (stem + ".unpacked"),
                         "lock": self.cache / (stem + ".locked")}
                if fault == "wrong-payload": paths["payload"].write_bytes(b"changed")
                elif fault == "wrong-marker": paths["marker"].write_bytes(b"X" * len(aar.UNPACKED_MARKER))
                elif fault == "oversized-marker": paths["marker"].write_bytes(aar.UNPACKED_MARKER + b"\n")
                elif fault == "empty-marker": paths["marker"].write_bytes(b"")
                elif fault == "nonempty-lock": paths["lock"].write_bytes(b"X")
                elif fault == "public-directory": (self.cache / stem).chmod(0o755)
                elif fault == "directory-link":
                    outside = self.root / ("outside-" + str(index))
                    (self.cache / stem).rename(outside)
                    (self.cache / stem).symlink_to(outside, target_is_directory=True)
                elif fault.startswith("public-"): paths[fault.removeprefix("public-")].chmod(0o644)
                else:
                    target, kind = fault.split("-")
                    path = paths[target]
                    if kind == "hardlink": os.link(path, self.root / ("hardlink-" + str(index)))
                    else:
                        path.unlink()
                        if kind == "link": path.symlink_to(self.source / name)
                        elif kind == "fifo": os.mkfifo(path, 0o600)
                        elif kind == "directory": path.mkdir(mode=0o700)
                with self.assertRaises((ValueError, OSError)):
                    self.run_helper("verify")

    def test_consumed_directory_replacement_during_snapshot_is_rejected(self):
        self.run_helper("seed"); self.consume()
        row = self.document["archives"][0]
        name, stem = row["fileName"], Path(row["fileName"]).stem
        original_read = aar._feed_read
        def replace(directory, filename, limit):
            data = original_read(directory, filename, limit)
            if filename == stem + ".unpacked":
                (self.cache / stem).rename(self.root / "retired-fixture")
                (self.cache / stem).mkdir(mode=0o700)
            return data
        with mock.patch.object(aar, "_feed_read", side_effect=replace), self.assertRaisesRegex(ValueError, "directory changed"):
            self.run_helper("verify")

    def test_empty_lock_growth_during_snapshot_is_rejected(self):
        self.run_helper("seed"); self.consume()
        stem = Path(self.document["archives"][0]["fileName"]).stem
        descriptor = os.open(self.cache, os.O_RDONLY | os.O_DIRECTORY)
        original_read = os.read
        def grow(fd, limit):
            data = original_read(fd, limit)
            (self.cache / (stem + ".locked")).write_bytes(b"changed")
            return data
        try:
            with mock.patch.object(aar.os, "read", side_effect=grow), self.assertRaisesRegex(ValueError, "lock changed"):
                aar.empty_cache_lock(descriptor, stem + ".locked")
        finally:
            os.close(descriptor)

    def test_consumed_directory_and_lock_reject_foreign_owner(self):
        self.run_helper("seed"); self.consume()
        row = self.document["archives"][0]
        descriptor = os.open(self.cache, os.O_RDONLY | os.O_DIRECTORY)
        foreign_uid = os.getuid() + 1
        try:
            with mock.patch.object(aar.os, "getuid", return_value=foreign_uid):
                with self.assertRaisesRegex(ValueError, "owner-owned"):
                    aar.consumed_archive(descriptor, row)
                with self.assertRaisesRegex(ValueError, "private"):
                    aar.empty_cache_lock(descriptor, Path(row["fileName"]).stem + ".locked")
        finally:
            os.close(descriptor)

    def test_layout_diagnostic_is_names_only_bounded_and_escaped(self):
        unexpected = "\n" + "x" * 1000
        with self.assertRaises(ValueError) as error:
            aar.cache_layout({unexpected}, self.document["archives"])
        self.assertIn("expected=", str(error.exception))
        self.assertIn("observed=", str(error.exception))
        self.assertNotIn("\n", str(error.exception))
        self.assertNotIn("x" * 97, str(error.exception))

    def test_extra_missing_link_special_wrong_bytes_and_extraction_preseed_fail(self):
        path = self.source / self.document["archives"][0]["fileName"]
        original = path.read_bytes()
        for fault in ("missing", "link", "fifo", "wrong", "extra", "preseed"):
            with self.subTest(fault=fault):
                if fault in ("missing", "link", "fifo"):
                    path.unlink()
                if fault == "link":
                    path.symlink_to(self.lock)
                elif fault == "fifo":
                    os.mkfifo(path, 0o600)
                elif fault == "wrong":
                    path.write_bytes(b"changed")
                extra = self.source / "extra"
                if fault == "extra":
                    extra.write_text("extra")
                elif fault == "preseed":
                    extra.mkdir()
                with self.assertRaises((ValueError, OSError)):
                    self.run_helper("seed")
                self.assertFalse(self.cache.exists())
                if extra.exists():
                    extra.rmdir() if extra.is_dir() else extra.unlink()
                if path.exists() or path.is_symlink():
                    path.unlink()
                path.write_bytes(original); path.chmod(0o600)

    def test_bound_lock_versions_targets_duplicates_and_pin_identity(self):
        original = copy.deepcopy(self.graph)
        row = self.document["archives"][0]
        for fault in ("version", "missing", "rid-conflict", "duplicate-case", "target"):
            with self.subTest(fault=fault):
                self.graph = copy.deepcopy(original)
                base = self.graph["dependencies"]["net10.0-android36.0"]
                if fault == "version":
                    base[row["packageId"]]["resolved"] = "99.0.0"
                elif fault == "missing":
                    del base[row["packageId"]]
                elif fault == "rid-conflict":
                    self.graph["dependencies"]["net10.0-android36.0/android-arm64"][row["packageId"]] = {"type": "Transitive", "resolved": "99.0.0"}
                elif fault == "duplicate-case":
                    base[row["packageId"].lower()] = base[row["packageId"]]
                else:
                    self.graph["dependencies"]["wrong-target"] = {}
                self.save()
                with self.assertRaises(ValueError):
                    self.run_helper("validate")
        self.graph = original; self.save()
        self.spec.write_text('{"archives":[],"archives":[]}')
        with self.assertRaises(ValueError):
            self.run_helper("validate")

    def test_paths_overlap_freshness_and_unset_online_behavior(self):
        with self.assertRaises(ValueError):
            self.run_helper("seed", exclude_roots=(self.root,))
        alias = self.root / "protected-alias"; alias.symlink_to(self.root, target_is_directory=True)
        for excluded in (self.source, alias):
            with self.assertRaises(ValueError):
                self.run_helper("seed", exclude_roots=(excluded,))
        self.cache = self.source / "cache"
        with self.assertRaises(ValueError):
            self.run_helper("seed")
        self.cache = self.root / "cache"
        result = aar.prepare("seed", cache=self.cache, aar_lock=self.root / "absent", project_lock=self.root / "absent")
        self.assertFalse(result["offline"])
        self.assertEqual([], list(self.cache.iterdir()))
        with self.assertRaises(ValueError):
            self.run_helper("seed")
        for value in ("", "relative", str(self.source) + "/../source"):
            command = [sys.executable, "-B", str(REPO / "scripts/prepare_android_aar_cache.py"), "validate",
                       "--source", value, "--aar-lock", str(self.spec), "--project-lock", str(self.lock)]
            self.assertNotEqual(0, subprocess.run(command, capture_output=True, timeout=5).returncode)

    def test_direct_cli_validate_and_seed(self):
        for operation in ("validate", "seed", "verify"):
            command = [sys.executable, "-B", str(REPO / "scripts/prepare_android_aar_cache.py"), operation,
                       "--source", str(self.source), "--aar-lock", str(self.spec), "--project-lock", str(self.lock)]
            if operation != "validate":
                command += ["--cache", str(self.cache)]
            result = subprocess.run(command, capture_output=True, timeout=5)
            self.assertEqual(0, result.returncode, result.stderr)
            self.assertEqual(5, json.loads(result.stdout)["archiveCount"])

    def test_unsafe_zip_and_crc_are_not_admitted(self):
        for name in ("../escape", "/absolute", "classes.jar/child"):
            buffer = io.BytesIO()
            with zipfile.ZipFile(buffer, "w") as archive:
                archive.writestr("AndroidManifest.xml", b"fixture")
                archive.writestr("classes.jar", b"fixture")
                archive.writestr(name, b"bad")
            with self.subTest(name=name), self.assertRaises(ValueError):
                aar.admit_archive(buffer.getvalue())
        buffer = io.BytesIO()
        with zipfile.ZipFile(buffer, "w") as archive:
            archive.writestr("AndroidManifest.xml", b"payload")
            archive.writestr("classes.jar", b"payload")
        with self.assertRaises(zipfile.BadZipFile):
            aar.admit_archive(buffer.getvalue().replace(b"payload", b"PAYLOAD", 1))


if __name__ == "__main__":
    unittest.main()
