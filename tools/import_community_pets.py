"""Build Mosaic PNG atlases from the two credited community pet repositories.

Examples:
  python tools/import_community_pets.py --fy-zip chatgpt-chibi-pets-main.zip --crosspet-dir crosspet/characters
  python tools/import_community_pets.py --fy-zip chatgpt-chibi-pets-main.zip --crosspet-bundles <browser-use/assets>

The browser bundle option is only a way to collect original files; the normal
input is a checkout of CrossPet. Original files are not needed at runtime.
"""

from __future__ import annotations

import argparse
import hashlib
import io
import json
import re
import zipfile
from pathlib import Path

from PIL import Image, ImageOps


ROOT = Path(__file__).resolve().parents[1]
OUTPUT = ROOT / "src/Mosaic.Host/Assets/Pet"

FY_CHARACTERS = {
    "GrokCommunity": "grok-gothic-chibi-v1",
    "KimiCommunity": "kimi-moon-maid-chibi-v2",
    "ZhipuCommunity": "zhipu-chibi-tech-v2",
}
CROSS_CHARACTERS = {
    "CrossPetGpt": "gpt",
    "CrossPetClaude": "claude",
    "CrossPetDeepSeek": "deepseek",
    "CrossPetGemini": "gemini",
    "CrossPetQwen": "qwen",
    "CrossPetGlm": "glm",
}
CROSS_FILES = (
    "idle", "idle-blink", "listening", "thinking", "reading", "writing",
    "running", "searching", "drawing", "happy", "proud", "oops",
    "surprised", "pat", "sleeping", "tired", "yawn", "stretch",
    "hum", "bye", "asking",
)

# PetPose values retained by Mosaic's original artwork, plus the seven new actions.
FY_POSES = {
    "Idle": (0, 0), "LookLeft": (10, 4), "LookRight": (9, 4),
    "LookUp": (9, 0), "LookDown": (10, 0), "Blink": (0, 2),
    "Pat": (4, 0), "Cheer": (4, 2), "Wave": (3, 1),
    "Goodbye": (3, 3), "Sleep": (6, 5), "Wake": (4, 0),
    "Eat": (6, 2), "Drag": (4, 3), "Surprise": (5, 1),
    "Pout": (5, 6), "TypingLeft": (7, 0), "TypingRight": (7, 3),
    "RiceHat": (8, 0), "RiceHatTilt": (8, 2),
    "GreetA": (3, 0), "GreetB": (3, 1), "Tired": (5, 7),
    "BegTokens": (6, 1), "TokenDazed": (5, 3),
    "TiredYawn": (5, 5), "BegReach": (6, 3),
    "Think": (6, 0), "Read": (8, 0), "Search": (8, 3),
    "Draw": (7, 3), "Stretch": (4, 0), "Run": (1, 2),
    "Hum": (0, 4),
}

CROSS_POSES = {
    "Idle": ("idle",), "LookLeft": ("idle",),
    "LookRight": ("idle",), "LookUp": ("idle",),
    "LookDown": ("idle",), "Blink": ("idle-blink", "idle"),
    "Pat": ("pat", "happy", "idle"), "Cheer": ("happy", "proud"),
    "Wave": ("happy", "proud"), "Goodbye": ("bye",),
    "Sleep": ("sleeping", "tired"), "Wake": ("stretch",),
    "Eat": ("happy", "proud"), "Drag": ("surprised",),
    "Surprise": ("surprised",), "Pout": ("oops",),
    "TypingLeft": ("writing",), "TypingRight": ("writing",),
    "RiceHat": ("proud",), "RiceHatTilt": ("proud",),
    "GreetA": ("listening",), "GreetB": ("idle",),
    "Tired": ("tired",), "BegTokens": ("asking",),
    "TokenDazed": ("oops",), "TiredYawn": ("yawn",),
    "BegReach": ("asking",), "Think": ("thinking",),
    "Read": ("reading", "thinking"), "Search": ("searching",),
    "Draw": ("drawing", "writing"), "Stretch": ("stretch",),
    "Run": ("running",), "Hum": ("hum",),
}


def sha256(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def build_atlas(target: Path, images: dict[str, Image.Image], pose_images: dict[str, str],
                cell: tuple[int, int], provenance: dict[str, object]) -> None:
    target.mkdir(parents=True, exist_ok=True)
    used = list(dict.fromkeys(pose_images.values()))
    cols = 5
    atlas = Image.new("RGBA", (cols * cell[0], ((len(used) + cols - 1) // cols) * cell[1]))
    rects = {}
    for index, key in enumerate(used):
        box = (index % cols * cell[0], index // cols * cell[1])
        picture = images[key].convert("RGBA")
        if picture.size != cell:
            picture = ImageOps.contain(picture, cell, Image.Resampling.LANCZOS)
        atlas.paste(picture, (box[0] + (cell[0] - picture.width) // 2,
                              box[1] + cell[1] - picture.height))
        rects[key] = [*box, *cell]
    atlas.save(target / "atlas.png", optimize=True)
    frames = [{"pose": pose, "image": f"{target.name}/atlas.png", "rect": rects[key]}
              for pose, key in pose_images.items()]
    (target / "frames.json").write_text(json.dumps(frames, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    (target / "provenance.json").write_text(json.dumps(provenance, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(f"{target.name}: {len(used)} original frames, {len(frames)} Mosaic poses")


def fy_assets(archive: Path) -> None:
    with zipfile.ZipFile(archive) as bundle:
        paths = {name.split("/dist/codex-native/", 1)[1]: name for name in bundle.namelist()
                 if "/dist/codex-native/" in name and name.endswith("/spritesheet.webp")}
        for character, source in FY_CHARACTERS.items():
            raw = bundle.read(paths[f"{source}/spritesheet.webp"])
            sheet = Image.open(io.BytesIO(raw)).convert("RGBA")
            if sheet.size != (1536, 2288):
                raise ValueError(f"Wrong v2 sheet size for {source}: {sheet.size}")
            locations = list(dict.fromkeys(FY_POSES.values()))
            images = {f"{r}-{c}": sheet.crop((c * 192, r * 208, (c + 1) * 192, (r + 1) * 208))
                      for r, c in locations}
            build_atlas(OUTPUT / character, images,
                        {pose: f"{r}-{c}" for pose, (r, c) in FY_POSES.items()},
                        (192, 208), {
                            "source": "https://github.com/fywdy/chatgpt-chibi-pets",
                            "file": f"dist/codex-native/{source}/spritesheet.webp",
                            "sha256": sha256(raw), "license": "CC BY-NC 4.0",
                            "change": "Cropped selected v2 sprite cells and repacked them as a PNG atlas; pose mapping in this script.",
                        })


def bundled_crosspet_files(folder: Path) -> dict[tuple[str, str], bytes]:
    files = {}
    for manifest in folder.glob("*/manifest.json"):
        for item in json.loads(manifest.read_text(encoding="utf-8")).get("assets", []):
            match = re.search(r"/characters/([^/]+)/([^/]+)\.webp\?raw=true$", item["url"])
            if not match:
                continue
            key = match.group(1), match.group(2)
            raw = Path(item["path"]).read_bytes()
            if key in files and files[key] != raw:
                raise ValueError(f"Conflicting browser assets for {key}")
            files[key] = raw
    return files


def crosspet_assets(directory: Path | None, bundle_folder: Path | None) -> None:
    bundle_files = bundled_crosspet_files(bundle_folder) if bundle_folder else {}
    for character, source in CROSS_CHARACTERS.items():
        raw_files = {}
        for name in CROSS_FILES:
            path = directory / source / f"{name}.webp" if directory else None
            raw = path.read_bytes() if path and path.exists() else bundle_files.get((source, name))
            if raw:
                raw_files[name] = raw
        if "idle" not in raw_files:
            raise ValueError(f"Missing CrossPet idle art for {source}")
        images = {name: Image.open(io.BytesIO(raw)).convert("RGBA") for name, raw in raw_files.items()}
        if any(image.height != 640 or image.width > 704 for image in images.values()):
            raise ValueError(f"Unexpected CrossPet source size for {source}")
        poses = {}
        for pose, choices in CROSS_POSES.items():
            selected = next((name for name in choices if name in images), None)
            if not selected:
                raise ValueError(f"Missing CrossPet artwork for {source}/{pose}: {choices}")
            poses[pose] = selected
        build_atlas(OUTPUT / character, images, poses, (352, 320), {
            "source": "https://github.com/lokicorvus/crosspet",
            "directory": f"characters/{source}",
            "files": {f"{name}.webp": sha256(raw) for name, raw in raw_files.items()},
            "license": "CC BY-NC-SA 4.0",
            "change": "Resized source poses to half size and repacked them as a PNG atlas; pose mapping in this script.",
        })


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--fy-zip", type=Path, required=True)
    source = parser.add_mutually_exclusive_group(required=True)
    source.add_argument("--crosspet-dir", type=Path)
    source.add_argument("--crosspet-bundles", type=Path)
    args = parser.parse_args()
    fy_assets(args.fy_zip)
    crosspet_assets(args.crosspet_dir, args.crosspet_bundles)


if __name__ == "__main__":
    main()
