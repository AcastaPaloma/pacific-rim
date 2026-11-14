#!/usr/bin/env python
"""Entry point: /opt/miniconda3/envs/kaiju/bin/python Tools/Metrics/metrics.py <command> --help"""
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

from kaiju_metrics.cli import main  # noqa: E402

if __name__ == "__main__":
    sys.exit(main())
