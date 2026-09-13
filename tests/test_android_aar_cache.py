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

    def test_seed_is_flat_fresh_and_verify_allows_only_generated_directories(self):
        self.assertEqual(5, self.run_helper("validate")["archiveCount"])
        result = self.run_helper("seed")
        self.assertFalse(result["originAuthenticated"])
        self.assertEqual(sorted(p.name for p in self.source.iterdir()), sorted(p.name for p in self.cache.iterdir()))
        for path in self.cache.iterdir():
            self.assertEqual(0o600, path.stat().st_mode & 0o777)
            self.assertEqual((self.source / path.name).read_bytes(), path.read_bytes())
        row = self.document["archives"][0]
        extracted = self.cache / Path(row["fileName"]).stem
        extracted.mkdir(); (extracted / "task-generated").write_text("not preseeded")
        self.assertEqual(5, self.run_helper("verify")["archiveCount"])
        (self.cache / row["fileName"]).write_bytes(b"changed")
        with self.assertRaises(ValueError):
            self.run_helper("verify")

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
