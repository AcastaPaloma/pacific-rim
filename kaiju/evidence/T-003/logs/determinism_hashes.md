file | committed | run A (1 thread) | run B (4 threads, shuffled input) | identical
---|---|---|---|---
terrain.tif | e2f546a142e35579 | e2f546a142e35579 | e2f546a142e35579 | yes
terrain.r16 | 18c1200c8ce07f7c | 18c1200c8ce07f7c | 18c1200c8ce07f7c | yes
water.png | ee4b250c76f3637a | ee4b250c76f3637a | ee4b250c76f3637a | yes
buildings.geojson | d3e21d367ed68002 | d3e21d367ed68002 | d3e21d367ed68002 | yes
roads.geojson | c5e18915462bcc94 | c5e18915462bcc94 | c5e18915462bcc94 | yes
faults.geojson | a25c78d52a778e9e | a25c78d52a778e9e | a25c78d52a778e9e | yes
manifest.json minus build.{built_utc,git,git_dirty_pipeline} (volatile metadata, DEF-T003-arch-1) | 67617582a0ac13b1 | 67617582a0ac13b1 | 67617582a0ac13b1 | yes

build timing lives in build_info.json (not part of geo_tile@v1); a forced re-bake in place that reproduces the content keeps the existing build block.
