| Metric | Input | Wall time (s) | Limit |
|---|---|---|---|
| M-001 | 8 captures + stomp (synthetic, 50 Hz) | 0.4 | 60 |
| M-002 | 9 captures | 0.4 | 60 |
| M-003 | 12 frames 480x270 internal, 1920x1080 output, + haze_cal | 2.0 | 60 |
| M-004 | one profile-viewpoint set: 6 sequences x 180 frames, 480x270, serial | 27.5 | 60 |
| M-004 | full set: 36 sequences x 180 frames, 480x270, --jobs 8 | 36.9 | 600 |

Machine: 15 cores; kaiju env; headless.
