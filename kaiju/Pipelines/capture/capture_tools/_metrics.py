"""Locates Tools/Metrics (wrap, never copy) and exposes its colour maths and png reader."""
import os
import sys

_HERE = os.path.dirname(os.path.abspath(__file__))
METRICS_DIR = os.path.abspath(os.path.join(_HERE, "..", "..", "..", "Tools", "Metrics"))
if METRICS_DIR not in sys.path:
    sys.path.insert(0, METRICS_DIR)

from kaiju_metrics.colour import ciede2000, srgb_u8_to_lab  # noqa: E402
from kaiju_metrics.png import read_png  # noqa: E402

METRICS_PY = os.path.join(METRICS_DIR, "metrics.py")
__all__ = ["ciede2000", "srgb_u8_to_lab", "read_png", "METRICS_PY", "METRICS_DIR"]
