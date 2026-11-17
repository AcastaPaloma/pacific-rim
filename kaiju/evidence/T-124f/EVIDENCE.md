# T-124f evidence
- alloc_before.csv: `KAIJU_LOD_ROOT=<pyramid> KAIJU_T124F_OUT=... dotnet test --filter FullyQualifiedName~StreamingAllocBenchTests` in DotnetTests/WorldStreaming.Host.Tests at ac63335 (bench + golden tests, no optimisation).
- alloc_after.csv (= alloc_after1.csv): same command at 61ca43c.
- real_golden_before.txt: hashes of the real L1..L4 tiles before the change (TileBuildEqualityTests, KAIJU_T124F_REAL_OUT); the after run compared against it with KAIJU_T124F_REAL_GOLDEN: pass.
- dotnet_*.log: test results of WorldGeo.Tests, WorldStreaming.Tests, WorldStreaming.Host.Tests.
