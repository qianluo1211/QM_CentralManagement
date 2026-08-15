from pathlib import Path

from PIL import Image, ImageDraw


ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / "assets" / "central_management_source.png"

ACTIVE = {
    "background": (0x00, 0x02, 0x01, 0xFF),
    "dark": (0x10, 0x20, 0x18, 0xFF),
    "dim": (0x1F, 0x3D, 0x2E, 0xFF),
    "main": (0x3A, 0x79, 0x66, 0xFF),
    "bright": (0x81, 0xB5, 0x7A, 0xFF),
    "accent": (0xDD, 0xCC, 0x59, 0xFF),
}

LOCKED = {
    "background": ACTIVE["background"],
    "dark": (0x2C, 0x05, 0x0F, 0xFF),
    "dim": (0x58, 0x07, 0x1C, 0xFF),
    "main": (0x8D, 0x11, 0x31, 0xFF),
    "bright": (0xC2, 0x16, 0x42, 0xFF),
    "accent": (0xE2, 0x45, 0x64, 0xFF),
}


def prepare_source() -> Image.Image:
    source = Image.open(SOURCE).convert("RGBA")
    bbox = source.getchannel("A").getbbox()
    if bbox is None:
        raise RuntimeError("central-management source contains no artwork")
    cropped = source.crop(bbox)
    side = max(cropped.size)
    square = Image.new("RGBA", (side, side), ACTIVE["background"])
    target = (int(side * 0.90), int(side * 0.90))
    cropped.thumbnail(target, Image.Resampling.NEAREST)
    square.alpha_composite(cropped,
                           ((side - cropped.width) // 2,
                            (side - cropped.height) // 2))
    return square


def nearest_active(pixel: tuple[int, ...]) -> tuple[int, ...]:
    if pixel[3] < 64:
        return ACTIVE["background"]
    rgb = pixel[:3]
    return min(ACTIVE.values(), key=lambda color:
               sum((rgb[channel] - color[channel]) ** 2
                   for channel in range(3)))


def build_active(source: Image.Image, size: int) -> Image.Image:
    reduced = source.resize((size, size), Image.Resampling.BOX)
    result = Image.new("RGBA", (size, size))
    result.putdata([nearest_active(pixel) for pixel in reduced.getdata()])
    draw = ImageDraw.Draw(result)
    draw.rectangle((0, 0, size - 1, size - 1), outline=ACTIVE["main"])
    draw.point((size - 2, 1), fill=ACTIVE["bright"])
    return result


def locked(active: Image.Image) -> Image.Image:
    mapping = {ACTIVE[key]: LOCKED[key] for key in ACTIVE}
    result = Image.new("RGBA", active.size)
    result.putdata([mapping.get(pixel, pixel) for pixel in active.getdata()])
    return result


def main() -> None:
    source = prepare_source()
    # Major technologies use the large 38x38 tree emblem.  The previous 20x20
    # output made this project look like a minor branch upgrade.
    tech = build_active(source, 38)
    fast = build_active(source, 30)
    tech.save(ROOT / "assets" / "central_management_tech_active.png")
    locked(tech).save(
        ROOT / "assets" / "central_management_tech_locked.png")
    fast.save(ROOT / "assets" / "central_management_fast.png")


if __name__ == "__main__":
    main()
