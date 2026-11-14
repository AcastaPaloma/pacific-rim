import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
METRICS = os.path.dirname(HERE)
sys.path.insert(0, METRICS)
os.environ.setdefault("MPLBACKEND", "Agg")

import pytest  # noqa: E402

EXAMPLES = os.path.join(METRICS, "examples")
PROJECT = os.path.dirname(os.path.dirname(METRICS))
PALETTES = os.path.join(PROJECT, "research", "R-002", "palettes.json")


@pytest.fixture(scope="session")
def palettes():
    from kaiju_metrics.contracts.palette_set import load_palette_set
    return load_palette_set(PALETTES)
