import importlib.util
import struct
import subprocess
import sys
import tempfile
import unittest
import zipfile
from pathlib import Path


REPO = Path(__file__).resolve().parents[1]
VERIFIER_PATH = REPO / "scripts" / "verify_release_aab_excludes_api36_proof.py"
SPEC = importlib.util.spec_from_file_location("release_aab_proof_exclusion", VERIFIER_PATH)
assert SPEC is not None and SPEC.loader is not None
VERIFIER = importlib.util.module_from_spec(SPEC)
sys.modules[SPEC.name] = VERIFIER
SPEC.loader.exec_module(VERIFIER)


def _lz4_literals(data: bytes) -> bytes:
    literal_length = len(data)
    token_length = min(literal_length, 15)
    encoded = bytearray([token_length << 4])
    remainder = literal_length - token_length
    if token_length == 15:
        while remainder >= 255:
            encoded.append(255)
            remainder -= 255
        encoded.append(remainder)
    encoded.extend(data)
    return bytes(encoded)


def _assembly_store_payload(image: bytes, compressed: bool = True) -> bytes:
    name = b"Chummer.Android.dll"
    if compressed:
        image = b"XALZ" + struct.pack("<II", 0, len(image)) + _lz4_literals(image)
    header_size = 20
    index_size = 13
    descriptor_size = 28
    names_size = 4 + len(name)
    data_offset = header_size + index_size + descriptor_size + names_size
    return b"".join(
        (
            struct.pack("<IIIII", 0x41424158, 0x80010003, 1, 1, index_size),
            struct.pack("<QIB", 0x1234, 0, 0),
            struct.pack("<IIIIIII", 0, data_offset, len(image), 0, 0, 0, 0),
            struct.pack("<I", len(name)),
            name,
            image,
        )
    )


def _elf(payload: bytes) -> bytes:
    string_table = b"\x00.shstrtab\x00payload\x00"
    payload_offset = 128
    section_offset = (payload_offset + len(payload) + 7) & ~7
    image = bytearray(section_offset + 3 * 64)
    struct.pack_into(
        "<16sHHIQQQIHHHHHH",
        image,
        0,
        b"\x7fELF\x02\x01\x01" + b"\x00" * 9,
        3,
        183,
        1,
        0,
        0,
        section_offset,
        0,
        64,
        0,
        0,
        64,
        3,
        1,
    )
    image[64 : 64 + len(string_table)] = string_table
    image[payload_offset : payload_offset + len(payload)] = payload
    struct.pack_into("<IIQQQQIIQQ", image, section_offset + 64, 1, 3, 0, 0, 64, len(string_table), 0, 0, 1, 0)
    struct.pack_into(
        "<IIQQQQIIQQ",
        image,
        section_offset + 128,
        11,
        1,
        0,
        0,
        payload_offset,
        len(payload),
        0,
        0,
        8,
        0,
    )
    return bytes(image)


def _write_aab(path: Path, managed_image: bytes, *, compressed: bool = True, extra_name: str | None = None) -> None:
    payload = _assembly_store_payload(managed_image, compressed=compressed)
    with zipfile.ZipFile(path, "w", compression=zipfile.ZIP_DEFLATED) as bundle:
        bundle.writestr("base/lib/arm64-v8a/libassembly-store.so", _elf(payload))
        bundle.writestr("base/manifest/AndroidManifest.xml", b"clean")
        if extra_name is not None:
            bundle.writestr(extra_name, b"clean")


class ReleaseAabProofExclusionTests(unittest.TestCase):
    def test_source_derived_inventory_covers_every_current_proof_type(self) -> None:
        markers = {(marker.category, marker.text) for marker in VERIFIER.load_markers(REPO)}
        expected_types = {
            "Api36ProofBuildIdentity",
            "Api36ProofSurfaceState",
            "Api36ProofWorkspaceState",
            "Api36ProofCreationResourcesState",
            "Api36ProofTransactionState",
            "Api36ProofState",
            "Api36ImportPickerState",
            "Api36ImportStreamState",
            "Api36ImportWorkspaceState",
            "Api36ImportProofState",
            "Api36ImportProofStateContract",
            "Api36ProofStateContract",
            "Api36ProofStatePublisher",
        }
        self.assertTrue(
            {("proof-type", name) for name in expected_types}.issubset(markers),
            markers,
        )

    def test_clean_compressed_managed_assembly_passes(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            aab = Path(temporary) / "clean.aab"
            _write_aab(aab, b"MZ\x00ordinary-release-assembly")
            stores, assemblies, expanded = VERIFIER.verify(aab.resolve(), REPO)
        self.assertEqual((1, 1), (stores, assemblies))
        self.assertGreater(expanded, 0)

    def test_proof_type_in_compressed_managed_metadata_fails(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            aab = Path(temporary) / "proof-type.aab"
            _write_aab(aab, b"MZ\x00Api36ProofStatePublisher")
            with self.assertRaisesRegex(
                VERIFIER.VerificationError,
                r"proof-type:Api36ProofState",
            ):
                VERIFIER.verify(aab.resolve(), REPO)

    def test_xalz_managed_image_is_actually_expanded_before_inspection(self) -> None:
        managed = b"MZ\x00Api36ProofStatePublisher"
        compressed = b"XALZ" + struct.pack("<II", 7, len(managed)) + _lz4_literals(managed)
        self.assertEqual(
            managed,
            VERIFIER._managed_image(compressed, "test assembly"),
        )

    def test_proof_contract_in_uncompressed_managed_metadata_fails(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            aab = Path(temporary) / "proof-contract.aab"
            _write_aab(
                aab,
                b"MZ\x00chummer.android.api36-proof-state/v2",
                compressed=False,
            )
            with self.assertRaisesRegex(VERIFIER.VerificationError, r"proof-contract"):
                VERIFIER.verify(aab.resolve(), REPO)

    def test_proof_filename_in_zip_path_fails(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            aab = Path(temporary) / "proof-filename.aab"
            _write_aab(
                aab,
                b"MZ\x00ordinary-release-assembly",
                extra_name="base/assets/api36-proof/state.v2.json",
            )
            with self.assertRaisesRegex(VERIFIER.VerificationError, r"proof-runtime-filename"):
                VERIFIER.verify(aab.resolve(), REPO)

    def test_unsupported_managed_compression_fails_closed(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            aab = Path(temporary) / "unsupported.aab"
            payload = _assembly_store_payload(b"MZ\x00clean", compressed=False)
            payload = payload[:-8] + b"XAZS" + payload[-4:]
            with zipfile.ZipFile(aab, "w") as bundle:
                bundle.writestr("base/lib/arm64-v8a/libassembly-store.so", _elf(payload))
            with self.assertRaisesRegex(VERIFIER.VerificationError, r"unsupported XAZS"):
                VERIFIER.verify(aab.resolve(), REPO)

    def test_validate_aab_pipeline_invokes_binary_proof_verifier(self) -> None:
        validate = (REPO / "scripts" / "validate-aab.sh").read_text(encoding="utf-8")
        inspect_index = validate.index('"$inspect_aab_script" "$aab_path"')
        verifier_index = validate.index('"$proof_exclusion_script" "$aab_path"')
        signer_index = validate.index('if [[ -n "$upload_certificate_path" ]]')
        self.assertLess(inspect_index, verifier_index)
        self.assertLess(verifier_index, signer_index)

    def test_cli_rejects_relative_aab_path(self) -> None:
        result = subprocess.run(
            [sys.executable, str(VERIFIER_PATH), "relative.aab"],
            cwd=REPO,
            check=False,
            capture_output=True,
            text=True,
        )
        self.assertNotEqual(0, result.returncode)
        self.assertIn("AAB path must be absolute", result.stderr)


if __name__ == "__main__":
    unittest.main()
