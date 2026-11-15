# StyleCapture

Editor tool that renders the T-004 style evidence (capture sets of PNG frames, sidecar JSON and logs) from the pixel pipeline in batch mode. Entry point: `Editor/StyleCaptureCli.cs`. Job list: `Editor/StyleCaptureJobs.cs`.

## Run a capture

From `kaiju/`:

```
unity run . --no-tail --log-file <log> --timeout 900 -- -executeMethod Game.Tools.StyleCapture.StyleCaptureCli.Run -captureOut <dir> [-captureJobs <a,b>]
```

Arguments parsed by `Run` and its helpers:

- `-captureOut <dir>`: output root. Default `evidence/T-004/captures`.
- `-captureJobs <a,b>`: comma-separated job names. Default `stills,hazecal,m004,m004ab,subpixel,controls,stages,cut,orbit,dolly`.
- `-captureProfiles <a,b>`: comma-separated profile ids. Default: all five (`warm_rust_sepia`, `warm_amber_haze`, `slate_teal`, `lavender_peach`, `slate_crimson`).
- `-captureC11Phases`, `-captureC11Frames`, `-captureC11Configs`: see `c11slab` below.
- `-captureCommit <hash>`: git commit recorded in the sidecars. Default `git rev-parse HEAD`.

Other entry points: `StyleCaptureCli.Preview` (`-executeMethod ...StyleCaptureCli.Preview`, with `-captureOut`, default `Temp/pixel_preview`) writes one PNG set per profile for a quick look.

## Jobs

- `stills`: twelve fixed stations per profile at the street, eye70, elevated300 and eye70_tower viewpoints, plus detail frames (M-003).
- `hazecal`: haze calibration scene, one frame per profile.
- `m004`: M-004 motion sequences (static, translate, yaw) on the main viewpoint.
- `m004direction`: the M-004 sets with direction anchoring. Not in the default list; request it explicitly.
- `m004ab`: A/B variants of anchoring, snap and rotation-quantisation, plus a control noise frame.
- `subpixel`: sub-pixel motion sequences from the eye70_tower viewpoint.
- `controls`: unquantised, dither-off, haze-off, bilinear and reversed-haze controls.
- `stages`: single frames with each pipeline stage switched off or on, and flag combinations.
- `cut`: moving-then-cut check and a static check at the new pose.
- `orbit`: 20 s orbit follow-mode run per profile, with one camera-shake impulse.
- `dolly`: 180-frame dolly toward the 10 km layer per profile.
- `c11slab` (T-182, on request, not in the default list): the T1 translation through the shipped present stage in eight configurations (single snap, `depthShift`, `layerSnap` at main depth 663.0 and 664.874 m, plus `layerSnap` at the class points' geometric depths) from many start offsets, one `c11_slab@v1` sequence each (`sequence.json` and `c11_frames.json.gz`: per frame the log-coded eye depth of the presented output along the row of each class point, near 124 m, reference 663 m, far 10.4 km). Arguments: `-captureC11Phases` (group B starts, default 48; group A gets half), `-captureC11Frames` (180), `-captureC11Configs` (name filters). Analysis: `Tools/Rendering/Pixel/c11_slab.py <captureOut>`.
- `layercheck` (T-181d acceptance, on request): the layered render against the single render on cut frames from three viewpoints (shares of identical texels, palette exactness of the output), written to `layercheck.json`.

## Output layout

```
<captureOut>/
  capture_log_summary.txt          # overwritten on every run: job list, duration, error and warning counts, first 8 messages
  capture_log_warnings.txt         # overwritten on every run: every warning and error line logged during the run (empty = clean)
  control_continuous_tone/         # `controls` job: continuous-tone colour render of the eye70 view (negative-anchor counterpart)
  <set>/<sequence>/                # one folder per sequence (set and sequence names come from the jobs)
    <stem>_color_internal.png
    <stem>_color_output.png        # only for sequences that write output
    <stem>_index.png
    <stem>_depth.png
    <stem>_mask_animated.png       # where a mask is produced
    <stem>.json                    # per-frame sidecar
    sequence.json                  # sequence manifest
```

The run throws at the end if the log contained any error, so check `capture_log_summary.txt` as well as the exit status.

## Traps

- Never run `StyleCaptureCli.Setup` unless the scene changes. It installs the pipeline assets and rebuilds both `.unity` test scenes.
- A run overwrites `capture_log_summary.txt` in `<captureOut>`. Copy it first if you need the previous one.
- Check the Unity queue before starting. Expect a count below 2:
  `ps ax -o command | grep -i "[U]nity.app/Contents/MacOS/Unity .*-batchmode" | grep -vc AssetImportWorker`
- Default output goes to `evidence/T-004/captures`. Pass `-captureOut` to use another folder.
