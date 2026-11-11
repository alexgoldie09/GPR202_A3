"""
Generates the terrain's tiling sand textures: Resource/Terrain/SandColor.png and SandNormal.png.

Both are made from noise filtered in frequency space (FFT), which is periodic by construction,
so the textures tile with no seams. Not part of the game build: run once with Python 3, numpy and Pillow:
    python Tools/make_sand_textures.py
"""
import numpy as np
from PIL import Image

SIZE = 1024  # power of two, so mipmaps halve cleanly
rng = np.random.default_rng(7)  # fixed seed: the same textures every run


def periodic_noise(n, falloff):
    """White noise with its high frequencies turned down (1 / frequency^falloff). Larger falloff = smoother."""
    spectrum = np.fft.fft2(rng.standard_normal((n, n)))
    ky = np.fft.fftfreq(n)[:, None]
    kx = np.fft.fftfreq(n)[None, :]
    k = np.sqrt(kx ** 2 + ky ** 2)
    k[0, 0] = 1
    spectrum /= k ** falloff
    spectrum[0, 0] = 0
    noise = np.real(np.fft.ifft2(spectrum))
    return (noise - noise.mean()) / noise.std()


# --- Colour: sand base colour with fine grain, soft blotches and a few darker/lighter specks ---
grain = rng.standard_normal((SIZE, SIZE))
blotch = periodic_noise(SIZE, 2.2)
mid = periodic_noise(SIZE, 1.4)
base = np.array([0.87, 0.72, 0.50])
colour = base[None, None, :] * (1 + 0.06 * grain + 0.05 * blotch + 0.03 * mid)[..., None]
specks = rng.random((SIZE, SIZE))
colour[specks > 0.995] *= 0.7
colour[specks < 0.004] *= 1.15
Image.fromarray((np.clip(colour, 0, 1) * 255).astype(np.uint8)).save("Resource/Terrain/SandColor.png", optimize=True)

# --- Normal map: wind ripples (a whole number of cycles across the tile so they wrap), bent by noise ---
y, x = np.mgrid[0:SIZE, 0:SIZE] / SIZE
cycles = 14
warp = periodic_noise(SIZE, 2.5) * 0.04
phase = x * cycles + y * 3 + warp * cycles
t = phase - np.floor(phase)
ripple = np.where(t < 0.7, t / 0.7, (1 - t) / 0.3)  # gentle windward side, steep lee side (like the dunes)
ripple = ripple * (0.8 + 0.2 * periodic_noise(SIZE, 2))
height = ripple + 0.15 * periodic_noise(SIZE, 1.0)  # plus fine grain bumps

# Slopes by central differences (np.roll wraps around, keeping the tile seamless), then normals
strength = 6.0
dx = (np.roll(height, -1, 1) - np.roll(height, 1, 1)) * 0.5 * strength
dy = (np.roll(height, -1, 0) - np.roll(height, 1, 0)) * 0.5 * strength
# Image rows run down, but the game flips images on load so +V runs up the image: hence +dy for green
normal = np.stack([-dx, dy, np.ones_like(height)], -1)
normal /= np.linalg.norm(normal, axis=-1, keepdims=True)
Image.fromarray(((normal * 0.5 + 0.5) * 255).astype(np.uint8)).save("Resource/Terrain/SandNormal.png", optimize=True)
