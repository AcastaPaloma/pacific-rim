| Metric | PASS (0) | FAIL (1) | INVALID_INPUT (2) | INCOMPLETE (3) |
|---|---|---|---|---|
| m001 | 0: M-001@v1 PASS (provisional: 7 assumed) (exit 0) | 1: M-001@v1 FAIL (exit 1) | 2: M-001@v1 INVALID_INPUT (exit 2) | 3: M-001@v1 INCOMPLETE (exit 3) |
| m002 | 0: M-002@v1 PASS (exit 0) | 1: M-002@v1 FAIL (exit 1) | 2: M-002@v1 INVALID_INPUT (exit 2) | 3: M-002@v1 INCOMPLETE (exit 3) |
| m003 | 0: M-003@v1 PASS (provisional: 2 assumed) (exit 0) | 1: M-003@v1 FAIL (exit 1) | 2: M-003@v1 INVALID_INPUT (exit 2) | 3: M-003@v1 INCOMPLETE (exit 3) |
| m004 | 0: M-004@v1 PASS (provisional: 9 assumed) [fixture mode: not a gating run | 1: M-004@v1 FAIL (exit 1) | 2: M-004@v1 INVALID_INPUT (exit 2) | 3: M-004@v1 INCOMPLETE (exit 3) |

INCOMPLETE examples: m001 without ratings (M-001.13 PENDING), m002 with captures missing, m003 without ratings (M-003 f PENDING), m004 60-frame sequences without --fixture.
