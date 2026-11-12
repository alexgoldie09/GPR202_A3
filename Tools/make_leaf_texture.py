"""
Builds the palm leaf's RGBA texture (Resource/Props/PalmLeaf.png) from the leaf colour image.

The colour image has a grey/white checkerboard baked in where it should be transparent, and the separate
alpha image doesn't line up with it. So the alpha is made from the colour image itself: leaf pixels are green,
checkerboard pixels are grey or white (no colour), so "how green is this pixel" is the alpha.
Transparent pixels then get the colour of the nearest leaf pixel, so mipmaps don't blend grey into the leaf edges.

Run once with Python 3, numpy and Pillow:
    python Tools/make_leaf_texture.py <leaf colour image> Resource/Props/PalmLeaf.png
"""
import sys
import numpy as np
from PIL import Image, ImageFilter

SIZE = 1024  # power of two, so mipmaps halve cleanly

# How green a pixel must be (green minus the larger of red and blue, 0..1) to count as leaf.
# Below GREENSTART = fully transparent, above GREENFULL = fully opaque, smooth in between.
GREENSTART = 0.012
GREENFULL = 0.045

# How many times to spread leaf colour outwards into the transparent area (each pass grows it ~1 pixel)
BLEEDPASSES = 16

source, destination = sys.argv[1], sys.argv[2]
colour = np.asarray(Image.open(source).convert("RGB").resize((SIZE, SIZE), Image.LANCZOS)).astype(np.float32) / 255

# --- Alpha from greenness ---
greenness = colour[..., 1] - np.maximum(colour[..., 0], colour[..., 2])
t = np.clip((greenness - GREENSTART) / (GREENFULL - GREENSTART), 0, 1)
alpha = t * t * (3 - 2 * t)  # smoothstep

# Remove lone specks (a 3x3 median keeps the fronds but drops single stray pixels)
alpha_img = Image.fromarray((alpha * 255).astype(np.uint8)).filter(ImageFilter.MedianFilter(3))
alpha = np.asarray(alpha_img).astype(np.float32) / 255

# --- Bleed leaf colour into the transparent area ---
# Average each transparent pixel's leaf-coloured neighbours, repeatedly, so the edges fade into leaf green, not grey
known = alpha > 0.5
rgb = np.where(known[..., None], colour, 0)
for _ in range(BLEEDPASSES):
    total = np.zeros_like(rgb)
    count = np.zeros(known.shape, np.float32)
    for dy in (-1, 0, 1):
        for dx in (-1, 0, 1):
            total += np.roll(np.roll(rgb * known[..., None], dy, 0), dx, 1)
            count += np.roll(np.roll(known, dy, 0), dx, 1)
    grow = (~known) & (count > 0)
    rgb[grow] = total[grow] / count[grow][:, None]
    known = known | grow
rgb[~known] = rgb[known].mean(axis=0)  # anything still unreached gets the average leaf colour

rgba = np.dstack([rgb, alpha])
Image.fromarray((np.clip(rgba, 0, 1) * 255).astype(np.uint8), "RGBA").save(destination, optimize=True)
