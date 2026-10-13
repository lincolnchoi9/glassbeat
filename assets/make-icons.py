"""Builds assets/icon.png (1024², the tile edge to edge) and src/PrismPlay/PrismPlay.ico from the artwork.
Usage: python assets/make-icons.py <source.webp>   (Pillow + NumPy)"""
import sys
from pathlib import Path
import numpy as np
from PIL import Image, ImageDraw, ImageFilter

ROOT = Path(__file__).resolve().parent.parent
src = Image.open(sys.argv[1]).convert("RGBA")
alpha = np.asarray(src)[..., 3].astype(float)

def crossing(profile):  # sub-pixel index where alpha first rises through 50 %
    i = int(np.argmax(profile > 128))
    return i - 1 + (128 - profile[i - 1]) / (profile[i] - profile[i - 1])

n, mid = alpha.shape[0], range(450, 800)
left = np.mean([crossing(alpha[y]) for y in mid]); right = n - np.mean([crossing(alpha[y, ::-1]) for y in mid])
top = np.mean([crossing(alpha[:, x]) for x in mid]); bottom = n - np.mean([crossing(alpha[::-1, x]) for x in mid])
# The tile's corner radius at its outer edge, and per-side insets (left, top, right, bottom) that drop the pale glow band
# outside the lavender rim; the band is wider on the right.
RADIUS, INSET = 289, (9, 9, 12, 9)
box = (left + INSET[0], top + INSET[1], right - INSET[2], bottom - INSET[3])
scale = 1024 / (box[2] - box[0])

# RGB only: the tile is opaque in the artwork (alpha ~253), and the mask below supplies the edge.
rgb = src.convert("RGB").transform((1024, 1024), Image.Transform.EXTENT, box, Image.Resampling.BICUBIC)
big = Image.new("L", (4096, 4096))  # 4x supersampled, antialiased rounded square
ImageDraw.Draw(big).rounded_rectangle((0, 0, 4095, 4095), radius=round((RADIUS - 9) * scale * 4), fill=255)
icon = rgb.convert("RGBA")
icon.putalpha(big.resize((1024, 1024), Image.Resampling.LANCZOS))
icon.save(ROOT / "assets" / "icon.png")

# Windows: the tile fills ~94 % of each size. At 24 px and below the detailed glass turns to noise, so those sizes use a hinted
# variant: the softened tile colours with a solid prism ring traced over the artwork's triangle.
hinted = icon.convert("RGB").filter(ImageFilter.GaussianBlur(48)).convert("RGBA")
prism = [(309, 283), (314, 765), (764, 496)]
ImageDraw.Draw(hinted).line(prism + prism[:2], fill=(60, 68, 94, 255), width=140, joint="curve")
hinted.putalpha(icon.getchannel("A"))
frames = []
for size in (16, 20, 24, 32, 40, 48, 64, 96, 128, 256):
    tile = round(size * 0.94)
    image = (hinted if size <= 24 else icon).resize((tile, tile), Image.Resampling.LANCZOS)
    if 24 < size <= 48:
        image = image.filter(ImageFilter.UnsharpMask(radius=0.6, percent=80, threshold=1))
    frame = Image.new("RGBA", (size, size))
    frame.alpha_composite(image, ((size - tile) // 2, (size - tile) // 2))
    frames.append(frame)
frames[-1].save(ROOT / "src" / "PrismPlay" / "PrismPlay.ico", sizes=[f.size for f in frames], append_images=frames[:-1])
