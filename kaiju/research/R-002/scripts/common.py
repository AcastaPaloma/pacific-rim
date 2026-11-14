"""R-002 research notes: shared helpers (panel inventory, colour conversions, CIEDE2000).
Run with the kaiju env: /opt/miniconda3/envs/kaiju/bin/python. Not production code."""
import os
import numpy as np
from PIL import Image
from skimage import color

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "..", ".."))
REFS = os.path.join(ROOT, "art", "style_refs")
OUT = os.path.join(ROOT, "research", "R-002")
import tempfile
CACHE = os.environ.get("R002_CACHE", os.path.join(tempfile.gettempdir(), "r002_cache"))
os.makedirs(CACHE, exist_ok=True)

# Panel inventory: id -> (file, box x0,y0,x1,y1 exclusive, exclusion boxes [captions/logos], neutral label)
# ref_02 is byte-identical to ref_01 and is not analysed separately.
PANELS = {
    "ref_01A": ("ref_01.png", (0, 0, 1024, 1014), [(900, 930, 1024, 1024)], "warm rust/sepia, giant armoured figure"),
    "ref_01B": ("ref_01.png", (1024, 0, 2048, 1024), [], "slate teal, giant silhouette in mist"),
    "ref_01C": ("ref_01.png", (0, 1024, 2048, 2048), [], "lavender-blue with peach fire, light shafts"),
    "ref_03A": ("ref_03.png", (0, 0, 1024, 1024), [(0, 0, 440, 115), (0, 900, 110, 1024)], "warm red/orange fire, giant silhouette"),
    "ref_03B": ("ref_03.png", (1024, 0, 2048, 1024), [(1024, 0, 1380, 90)], "slate blue with white cloud masses, giant silhouette"),
    "ref_03C": ("ref_03.png", (0, 1024, 1024, 2048), [], "sepia monochrome, large animal (posterised photo)"),
    "ref_03D": ("ref_03.png", (1024, 1024, 2048, 2048), [], "slate with crimson accent, armoured figure"),
    "ref_04A": ("ref_04.png", (0, 0, 1670, 2048), [], "slate with crimson accent, posterised, no dither"),
    "ref_04B": ("ref_04.png", (1676, 0, 2048, 2048), [], "warm sunset sky strip, crimson accent"),
    "ref_05A": ("ref_05.png", (0, 0, 443, 744), [(100, 375, 345, 420)], "warm orange haze, giant rock creature, tiny figures"),
    "ref_06A": ("ref_06.png", (78, 62, 642, 381), [], "saturated blue/cream, heavy ordered dither, building facade"),
}


def load_panel(pid):
    """Return (rgb uint8 HxWx3 cropped, mask bool HxW of analysable pixels)."""
    fn, (x0, y0, x1, y1), excl, _ = PANELS[pid]
    im = Image.open(os.path.join(REFS, fn))
    a = np.array(im.convert("RGBA"))
    rgb = a[y0:y1, x0:x1, :3].copy()
    mask = a[y0:y1, x0:x1, 3] == 255
    for (ex0, ey0, ex1, ey1) in excl:
        mask[max(ey0 - y0, 0):max(ey1 - y0, 0), max(ex0 - x0, 0):max(ex1 - x0, 0)] = False
    if pid == "ref_06A":  # erode the alpha edge (soft matte) by 4 px
        from scipy.ndimage import binary_erosion
        mask = binary_erosion(mask, iterations=4)
    return rgb, mask


def srgb_to_lab(rgb_u8):
    return color.rgb2lab(np.asarray(rgb_u8, dtype=np.float64) / 255.0)


def lab_to_srgb_u8(lab):
    rgb = color.lab2rgb(np.asarray(lab, dtype=np.float64))
    return np.clip(np.round(rgb * 255), 0, 255).astype(np.uint8)


def hexs(rgb_u8):
    return ["#%02x%02x%02x" % tuple(int(v) for v in c) for c in np.asarray(rgb_u8).reshape(-1, 3)]


def de00(lab1, lab2):
    """CIEDE2000 (Sharma, Wu, Dalal 2005 implementation in scikit-image), broadcasting."""
    return color.deltaE_ciede2000(lab1, lab2)


def nearest_de00(lab_pixels, palette_lab, chunk=200000):
    """For N x 3 Lab pixels return (min dE00, argmin index) to a K x 3 palette."""
    lab_pixels = np.asarray(lab_pixels).reshape(-1, 3)
    out_d = np.empty(len(lab_pixels)); out_i = np.empty(len(lab_pixels), dtype=np.int32)
    P = np.asarray(palette_lab)
    for s in range(0, len(lab_pixels), chunk):
        x = lab_pixels[s:s + chunk]
        d = de00(x[:, None, :], P[None, :, :])
        out_i[s:s + chunk] = d.argmin(1)
        out_d[s:s + chunk] = d.min(1)
    return out_d, out_i


def bayer(n):
    """Bayer (1973) ordered-dither threshold matrix of size n (power of 2), values in [0,1)."""
    m = np.array([[0, 2], [3, 1]])
    while m.shape[0] < n:
        m = np.block([[4 * m, 4 * m + 2], [4 * m + 3, 4 * m + 1]])
    return (m + 0.5) / m.size
