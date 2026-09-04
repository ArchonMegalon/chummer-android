#!/usr/bin/env python3
"""Fail closed when an Android Release AAB contains API-36 proof instrumentation.

The .NET Android Release toolchain stores managed assemblies in an ELF-wrapped
assembly store and normally LZ4-compresses every assembly.  Searching only ZIP
member names or the raw AAB therefore cannot establish that proof-only managed
types are absent.  This verifier checks every expanded bundle member and then
parses and expands every managed assembly in every assembly store.
"""

from __future__ import annotations

import re
import struct
import sys
import zipfile
from dataclasses import dataclass
from pathlib import Path, PurePosixPath


ASSEMBLY_STORE_MAGIC = 0x41424158  # XABA, little-endian
LZ4_MAGIC = b"XALZ"
ZSTD_MAGIC = b"XAZS"
MAX_ARCHIVE_ENTRIES = 20_000
MAX_ARCHIVE_ENTRY_BYTES = 128 * 1024 * 1024
MAX_ARCHIVE_EXPANDED_BYTES = 512 * 1024 * 1024
MAX_ASSEMBLIES = 4_096
MAX_ASSEMBLY_BYTES = 128 * 1024 * 1024
MAX_ASSEMBLIES_EXPANDED_BYTES = 512 * 1024 * 1024
ASSEMBLY_STORE_PATH = re.compile(
    r"^base/lib/[^/]+/(?:libassembly-store|libassemblies\.[^/]+\.blob)\.so$"
)
TYPE_DECLARATION = re.compile(
    r"\b(?:class|record(?:\s+(?:class|struct))?|struct|interface|enum)\s+"
    r"([A-Za-z_][A-Za-z0-9_]*)"
)
QUOTED_STRING = re.compile(r'"([^"\r\n]+)"')


class VerificationError(ValueError):
    """A fail-closed Release-AAB verification failure."""


@dataclass(frozen=True)
class Marker:
    category: str
    text: str

    @property
    def label(self) -> str:
        return f"{self.category}:{self.text}"


def require(condition: bool, message: str) -> None:
    if not condition:
        raise VerificationError(message)


def _has_nested_symlink(root: Path, path: Path) -> bool:
    current = path.parent
    while current != root:
        if current.is_symlink():
            return True
        require(root in current.parents, "proof source escaped its source root")
        current = current.parent
    return False


def _proof_sources(repo_root: Path) -> tuple[Path, ...]:
    proof_root = repo_root / "src" / "Chummer.Android" / "Proof"
    require(proof_root.is_dir() and not proof_root.is_symlink(), "proof source root is missing or unsafe")
    sources = tuple(sorted(proof_root.rglob("*.cs")))
    require(sources, "proof source inventory is empty")
    for source in sources:
        relative = source.relative_to(proof_root)
        require(
            source.is_file()
            and not source.is_symlink()
            and not _has_nested_symlink(proof_root, source),
            f"unsafe proof source: {relative.as_posix()}",
        )
    return sources


def load_markers(repo_root: Path) -> tuple[Marker, ...]:
    """Derive the closed marker inventory from the proof-only source owner."""

    types: set[str] = set()
    filenames: set[str] = set()
    contracts: set[str] = set()
    runtime_paths: set[str] = set()
    for source in _proof_sources(repo_root):
        text = source.read_text(encoding="utf-8")
        filenames.add(source.relative_to(repo_root / "src" / "Chummer.Android").as_posix())
        types.update(TYPE_DECLARATION.findall(text))
        for value in QUOTED_STRING.findall(text):
            if value.startswith("chummer.android.api36-"):
                contracts.add(value)
            if value.startswith("api36-proof/"):
                runtime_paths.add(value)

    require(types, "proof type marker inventory is empty")
    require(contracts, "proof contract marker inventory is empty")
    require(runtime_paths, "proof filename marker inventory is empty")

    markers = {
        Marker("namespace", "Chummer.Android.Proof"),
        *(Marker("proof-type", item) for item in types),
        *(Marker("proof-source-filename", item) for item in filenames),
        *(Marker("proof-contract", item) for item in contracts),
        *(Marker("proof-runtime-filename", item) for item in runtime_paths),
    }
    return tuple(sorted(markers, key=lambda item: (item.category, item.text)))


def _matching_marker(data: bytes, markers: tuple[Marker, ...]) -> Marker | None:
    for marker in markers:
        utf8 = marker.text.encode("utf-8")
        if utf8 in data or marker.text.encode("utf-16le") in data:
            return marker
    return None


def _scan(data: bytes, markers: tuple[Marker, ...], location: str) -> None:
    marker = _matching_marker(data, markers)
    if marker is not None:
        raise VerificationError(f"forbidden {marker.label} in {location}")


def _canonical_zip_name(name: str) -> bool:
    if not name or "\\" in name or name.startswith("/") or "\x00" in name:
        return False
    path = PurePosixPath(name)
    return all(part not in ("", ".", "..") for part in path.parts)


def _slice(data: bytes, offset: int, size: int, label: str) -> bytes:
    require(offset >= 0 and size >= 0, f"negative {label} range")
    end = offset + size
    require(end >= offset and end <= len(data), f"out-of-bounds {label} range")
    return data[offset:end]


def _elf_payload(image: bytes, location: str) -> bytes:
    require(len(image) >= 52 and image[:4] == b"\x7fELF", f"{location} is not ELF")
    elf_class = image[4]
    require(image[5] == 1, f"{location} is not little-endian ELF")
    require(image[6] == 1, f"{location} has an unsupported ELF version")

    if elf_class == 2:
        require(len(image) >= 64, f"{location} has a truncated ELF64 header")
        section_offset = struct.unpack_from("<Q", image, 40)[0]
        section_entry_size = struct.unpack_from("<H", image, 58)[0]
        section_count = struct.unpack_from("<H", image, 60)[0]
        string_index = struct.unpack_from("<H", image, 62)[0]
        section_format = "<IIQQQQIIQQ"
    elif elf_class == 1:
        section_offset = struct.unpack_from("<I", image, 32)[0]
        section_entry_size = struct.unpack_from("<H", image, 46)[0]
        section_count = struct.unpack_from("<H", image, 48)[0]
        string_index = struct.unpack_from("<H", image, 50)[0]
        section_format = "<IIIIIIIIII"
    else:
        raise VerificationError(f"{location} has an unsupported ELF class")

    native_section_size = struct.calcsize(section_format)
    require(section_count > 0, f"{location} has no ELF sections")
    require(section_count <= 16_384, f"{location} has too many ELF sections")
    require(section_entry_size >= native_section_size, f"{location} has a short ELF section record")
    require(string_index < section_count, f"{location} has an invalid ELF string-table index")
    require(
        section_offset + section_count * section_entry_size <= len(image),
        f"{location} has an out-of-bounds ELF section table",
    )

    sections: list[tuple[int, int, int]] = []
    for index in range(section_count):
        values = struct.unpack_from(section_format, image, section_offset + index * section_entry_size)
        sections.append((values[0], values[4], values[5]))
    _, string_offset, string_size = sections[string_index]
    strings = _slice(image, string_offset, string_size, f"{location} ELF string table")

    payloads: list[bytes] = []
    for name_offset, data_offset, data_size in sections:
        require(name_offset < len(strings) or name_offset == 0, f"{location} has an invalid ELF section name")
        end = strings.find(b"\x00", name_offset)
        require(end >= 0, f"{location} has an unterminated ELF section name")
        try:
            name = strings[name_offset:end].decode("ascii")
        except UnicodeDecodeError as error:
            raise VerificationError(f"{location} has a non-ASCII ELF section name") from error
        if name == "payload":
            payloads.append(_slice(image, data_offset, data_size, f"{location} payload"))
    require(len(payloads) == 1, f"{location} must contain exactly one payload section")
    return payloads[0]


def _lz4_decompress_block(compressed: bytes, expected_size: int, location: str) -> bytes:
    require(0 <= expected_size <= MAX_ASSEMBLY_BYTES, f"{location} expands beyond the assembly limit")
    output = bytearray()
    cursor = 0
    while cursor < len(compressed):
        token = compressed[cursor]
        cursor += 1
        literal_length = token >> 4
        if literal_length == 15:
            while True:
                require(cursor < len(compressed), f"{location} has a truncated LZ4 literal length")
                extra = compressed[cursor]
                cursor += 1
                literal_length += extra
                if extra != 255:
                    break
        require(cursor + literal_length <= len(compressed), f"{location} has truncated LZ4 literals")
        require(len(output) + literal_length <= expected_size, f"{location} exceeds its LZ4 size")
        output.extend(compressed[cursor : cursor + literal_length])
        cursor += literal_length
        if cursor == len(compressed):
            break

        require(cursor + 2 <= len(compressed), f"{location} has a truncated LZ4 match offset")
        match_offset = compressed[cursor] | (compressed[cursor + 1] << 8)
        cursor += 2
        require(0 < match_offset <= len(output), f"{location} has an invalid LZ4 match offset")
        match_length = token & 0x0F
        if match_length == 15:
            while True:
                require(cursor < len(compressed), f"{location} has a truncated LZ4 match length")
                extra = compressed[cursor]
                cursor += 1
                match_length += extra
                if extra != 255:
                    break
        match_length += 4
        require(len(output) + match_length <= expected_size, f"{location} exceeds its LZ4 size")
        for _ in range(match_length):
            output.append(output[-match_offset])

    require(len(output) == expected_size, f"{location} has an incorrect LZ4 expanded size")
    return bytes(output)


def _managed_image(raw: bytes, location: str) -> bytes:
    require(raw, f"{location} is empty")
    if raw.startswith(LZ4_MAGIC):
        require(len(raw) >= 12, f"{location} has a truncated XALZ header")
        # The compression descriptor index is owned by the Android packer and
        # is not the store entry's descriptor or mapping index.  The official
        # reader consumes but deliberately does not compare it.
        _, expected_size = struct.unpack_from("<II", raw, 4)
        return _lz4_decompress_block(raw[12:], expected_size, location)
    if raw.startswith(ZSTD_MAGIC):
        raise VerificationError(f"{location} uses unsupported XAZS compression")
    require(raw.startswith(b"MZ"), f"{location} is neither PE nor supported XALZ")
    require(len(raw) <= MAX_ASSEMBLY_BYTES, f"{location} exceeds the assembly limit")
    return raw


def _scan_assembly_store(
    elf: bytes,
    markers: tuple[Marker, ...],
    location: str,
) -> tuple[int, int]:
    payload = _elf_payload(elf, location)
    require(len(payload) >= 20, f"{location} has a truncated assembly-store header")
    magic, version, entry_count, index_count, index_size = struct.unpack_from("<IIIII", payload, 0)
    require(magic == ASSEMBLY_STORE_MAGIC, f"{location} has invalid assembly-store magic")
    format_number = version & 0xFFFF
    require(format_number in (2, 3, 4), f"{location} has unsupported assembly-store version")
    is_64_bit = bool(version & 0x80000000)
    header_size = 28 if format_number >= 4 else 20
    require(len(payload) >= header_size, f"{location} has a truncated assembly-store header")
    require(0 < entry_count <= MAX_ASSEMBLIES, f"{location} has an invalid assembly count")
    require(index_count > 0, f"{location} has no assembly-store index")
    require(index_count <= entry_count * 4, f"{location} has an excessive assembly-store index")
    require(index_size % index_count == 0, f"{location} has a corrupt assembly-store index size")
    index_entry_size = index_size // index_count
    expected_index_size = (12 if is_64_bit else 8) + (0 if format_number == 2 else 1)
    require(index_entry_size == expected_index_size, f"{location} has an unsupported index entry size")
    require(header_size + index_size <= len(payload), f"{location} has a truncated assembly-store index")

    cursor = header_size
    descriptor_indexes: set[int] = set()
    for _ in range(index_count):
        if is_64_bit:
            _, descriptor_index = struct.unpack_from("<QI", payload, cursor)
        else:
            _, descriptor_index = struct.unpack_from("<II", payload, cursor)
        require(descriptor_index < entry_count, f"{location} has an invalid descriptor index")
        descriptor_indexes.add(descriptor_index)
        cursor += index_entry_size
    require(cursor == header_size + index_size, f"{location} index cursor drifted")
    require(len(descriptor_indexes) == entry_count, f"{location} index does not cover every assembly")

    descriptor_bytes = entry_count * 28
    require(cursor + descriptor_bytes <= len(payload), f"{location} has truncated descriptors")
    descriptors: list[tuple[int, int]] = []
    for _ in range(entry_count):
        values = struct.unpack_from("<IIIIIII", payload, cursor)
        descriptors.append((values[1], values[2]))
        cursor += 28

    names: list[str] = []
    for _ in range(entry_count):
        require(cursor + 4 <= len(payload), f"{location} has a truncated assembly name length")
        name_size = struct.unpack_from("<I", payload, cursor)[0]
        cursor += 4
        require(0 < name_size <= 4_096, f"{location} has an invalid assembly name length")
        encoded_name = _slice(payload, cursor, name_size, f"{location} assembly name")
        cursor += name_size
        try:
            name = encoded_name.decode("utf-8")
        except UnicodeDecodeError as error:
            raise VerificationError(f"{location} has a non-UTF-8 assembly name") from error
        require("/../" not in f"/{name}/" and "\\" not in name, f"{location} has an unsafe assembly name")
        names.append(name)
        _scan(encoded_name, markers, f"{location} assembly name")
    require(len(set(names)) == len(names), f"{location} has duplicate assembly names")

    expanded_total = 0
    for descriptor_index, (data_offset, data_size) in enumerate(descriptors):
        require(data_offset >= cursor, f"{location} assembly data overlaps metadata")
        require(0 < data_size <= MAX_ARCHIVE_ENTRY_BYTES, f"{location} has an invalid assembly size")
        raw = _slice(payload, data_offset, data_size, f"{location}:{names[descriptor_index]}")
        image = _managed_image(raw, f"{location}:{names[descriptor_index]}")
        expanded_total += len(image)
        require(
            expanded_total <= MAX_ASSEMBLIES_EXPANDED_BYTES,
            f"{location} expanded assemblies exceed the total limit",
        )
        _scan(image, markers, f"{location}:{names[descriptor_index]}")
    return entry_count, expanded_total


def verify(aab_path: Path, repo_root: Path) -> tuple[int, int, int]:
    require(aab_path.is_absolute(), "AAB path must be absolute")
    require(aab_path.is_file() and not aab_path.is_symlink(), "AAB must be a regular non-symlink file")
    require(aab_path.resolve(strict=True) == aab_path, "AAB path must be canonical")
    markers = load_markers(repo_root)

    expanded_total = 0
    assembly_count = 0
    assembly_bytes = 0
    stores: list[tuple[str, bytes]] = []
    try:
        with zipfile.ZipFile(aab_path) as bundle:
            entries = bundle.infolist()
            require(0 < len(entries) <= MAX_ARCHIVE_ENTRIES, "AAB has an invalid entry count")
            names = [entry.filename for entry in entries]
            require(len(names) == len(set(names)), "AAB contains duplicate ZIP member names")
            for entry in entries:
                require(_canonical_zip_name(entry.filename), f"AAB contains unsafe member: {entry.filename}")
                unix_file_type = (entry.external_attr >> 16) & 0o170000
                require(unix_file_type != 0o120000, f"AAB contains symlink member: {entry.filename}")
                _scan(entry.filename.encode("utf-8"), markers, f"ZIP member name:{entry.filename}")
                if entry.is_dir():
                    continue
                require(entry.file_size <= MAX_ARCHIVE_ENTRY_BYTES, f"AAB member is too large: {entry.filename}")
                expanded_total += entry.file_size
                require(expanded_total <= MAX_ARCHIVE_EXPANDED_BYTES, "AAB expanded size exceeds the limit")
                content = bundle.read(entry)
                require(len(content) == entry.file_size, f"AAB member size drift: {entry.filename}")
                _scan(content, markers, f"ZIP member:{entry.filename}")
                if ASSEMBLY_STORE_PATH.fullmatch(entry.filename):
                    stores.append((entry.filename, content))
    except (OSError, zipfile.BadZipFile, RuntimeError) as error:
        raise VerificationError(f"AAB ZIP could not be inspected: {error}") from error

    require(stores, "AAB contains no managed assembly store")
    for name, content in stores:
        count, byte_count = _scan_assembly_store(content, markers, f"ZIP member:{name}")
        assembly_count += count
        assembly_bytes += byte_count
    return len(stores), assembly_count, assembly_bytes


def main() -> None:
    if len(sys.argv) != 2:
        raise SystemExit(f"usage: {Path(sys.argv[0]).name} /absolute/path/to/release.aab")
    repo_root = Path(__file__).resolve().parents[1]
    aab_path = Path(sys.argv[1])
    try:
        stores, assemblies, expanded_bytes = verify(aab_path, repo_root)
    except VerificationError as error:
        raise SystemExit(f"Release AAB proof-exclusion verification failed: {error}") from error
    print(
        "Release AAB proof-exclusion verification passed: "
        f"stores={stores} managed_assemblies={assemblies} expanded_managed_bytes={expanded_bytes}."
    )


if __name__ == "__main__":
    main()
