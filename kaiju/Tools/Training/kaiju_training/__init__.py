"""Kaiju RL tactical policy training (T-041, research/R-028.md).

Python drives the headless C# bridge (`Tools/Training/bridge`, the unmodified 2D harness + the C# policy codec) over a
binary lockstep pipe. The C# encoder/decoder are the single source of truth for the observation/action layout."""
