# Regression: T-030 step 2 + T-031 merge (task/T-030b, fac600e)
- Merge into the integration branch: clean, pushed.
- `CoreConfigAsset.CreateOrUpdate`: ran, no errors (`T-030b-merge.coreconfig.log`).
- Unity EditMode, whole project: 453 total, 451 passed, 0 failed, 2 skipped (ignored) (`T-030b-merge.editmode.log`). Includes the 14 ControlBridge tests.
- dotnet `Net.ControlBridge.Tests`: 12/12 before the merge.
- New behaviour is behind `net.seatBridge` (default off).
