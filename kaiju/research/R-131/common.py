"""Shared loaders for the R-131 offline models (read-only on evidence/T-004 and research/R-002)."""
import sys, os, json, math
import numpy as np
ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
sys.path.insert(0, os.path.join(ROOT, "Tools", "Rendering", "Pixel"))
import pixel_checks as pc   # committed T-004 metric code (C7 block_contrast, C6 layers, C11 edge series)
CAP = os.path.join(ROOT, "evidence", "T-004", "captures")
PROFILES = ["warm_rust_sepia", "warm_amber_haze", "slate_teal", "lavender_peach", "slate_crimson"]
def stills(profile, vp="eye70"):
    return os.path.join(CAP, "m003_stills", f"{profile}_{vp}")
