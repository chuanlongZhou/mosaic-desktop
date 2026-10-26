"""Verify imported community pet atlases and, when available, source blobs."""

from __future__ import annotations

import argparse
import hashlib
import json
import zipfile
from pathlib import Path

from PIL import Image

from import_community_pets import (
    CROSS_CHARACTERS, FY_CHARACTERS, OUTPUT, bundled_crosspet_files,
)


def git_blob_hash(data: bytes) -> str:
    return hashlib.sha1(f"blob {len(data)}\0".encode() + data).hexdigest()


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--fy-zip", type=Path)
    parser.add_argument("--crosspet-bundles", type=Path)
    parser.add_argument("--crosspet-tree", type=Path)
    args = parser.parse_args()
    expected_poses = 34  # 29 original poses minus 2 generated token frames, plus 7 new actions
    original = bundled_crosspet_files(args.crosspet_bundles) if args.crosspet_bundles else {}
    tree = json.loads(args.crosspet_tree.read_text(encoding="utf-8")) if args.crosspet_tree else {}
    fy_archive = zipfile.ZipFile(args.fy_zip) if args.fy_zip else None
    total = 0
    for character in [*FY_CHARACTERS, *CROSS_CHARACTERS]:
        folder = OUTPUT / character
        atlas = Image.open(folder / "atlas.png").convert("RGBA")
        frames = json.loads((folder / "frames.json").read_text(encoding="utf-8"))
        provenance = json.loads((folder / "provenance.json").read_text(encoding="utf-8"))
        if len(frames) != expected_poses or len({entry["pose"] for entry in frames}) != expected_poses:
            raise AssertionError(f"{character}: missing or duplicate poses")
        for entry in frames:
            if entry["image"] != f"{character}/atlas.png":
                raise AssertionError(f"{character}: wrong WPF resource URI")
            x, y, width, height = entry["rect"]
            if min(x, y) < 0 or min(width, height) <= 0 or x + width > atlas.width or y + height > atlas.height:
                raise AssertionError(f"{character}/{entry['pose']}: out-of-bounds frame")
            if not atlas.crop((x, y, x + width, y + height)).getchannel("A").getbbox():
                raise AssertionError(f"{character}/{entry['pose']}: empty frame")
            total += 1
        if character in CROSS_CHARACTERS and original:
            for filename, sha in provenance["files"].items():
                source = CROSS_CHARACTERS[character]
                data = original[(source, Path(filename).stem)]
                if hashlib.sha256(data).hexdigest() != sha:
                    raise AssertionError(f"{character}/{filename}: source checksum changed")
                path = f"characters/{source}/{filename}"
                if tree and git_blob_hash(data) != tree[path]:
                    raise AssertionError(f"{path}: source differs from GitHub tree")
        if character in FY_CHARACTERS and fy_archive:
            path = provenance["file"]
            matching = [name for name in fy_archive.namelist() if name.endswith("/" + path)]
            if len(matching) != 1 or hashlib.sha256(fy_archive.read(matching[0])).hexdigest() != provenance["sha256"]:
                raise AssertionError(f"{character}: source checksum changed")
    if fy_archive:
        fy_archive.close()
    print(f"Verified 9 atlases and {total} nonempty pose mappings; source checksums match.")


if __name__ == "__main__":
    main()
