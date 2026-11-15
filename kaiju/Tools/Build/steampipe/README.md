# SteamPipe scaffolding (T-143)

Templates and a renderer for Steam's `steamcmd +run_app_build` upload. **Nothing here uploads, logs in, downloads the
SDK or creates an account.** Every step in the table below is a founder action (account, money, licence acceptance or
publishing, per `plans/M3.md`).

| File | What it is |
|---|---|
| `app_build.vdf.template` | `appbuild` script: app ID, build description, two depots (macOS, Windows). `setlive` is empty on purpose: an upload never goes live by itself. |
| `depot_build.vdf.template` | `DepotBuildConfig` per depot: maps `CONTENT_ROOT/<mac or win>/*` to the depot root, excludes debug symbols. |
| `render_vdf.py` | Fills the `${PLACEHOLDERS}`; refuses to write anything while one is unset, empty, non-numeric (IDs) or contains a quote/newline. |

Placeholders: `APP_ID`, `DEPOT_ID_MAC`, `DEPOT_ID_WIN` (from Steamworks), `BUILD_DESC`, `BUILD_OUTPUT` (steamcmd log/cache
folder), `CONTENT_ROOT` (folder holding `mac/` and `win/` player builds, e.g. the M3 `build.sh` output). The renderer also
derives `DEPOT_ID` and `DEPOT_SUBDIR` per depot. Run it:

```bash
/opt/miniconda3/envs/kaiju/bin/python -I Tools/Build/steampipe/render_vdf.py --out-dir <out> \
  --set APP_ID=<id> --set DEPOT_ID_MAC=<id> --set DEPOT_ID_WIN=<id> \
  --set BUILD_DESC="kaiju 0.x demo" --set BUILD_OUTPUT=<out>/steam_output --set CONTENT_ROOT=<build folder>
```
(Values can instead come from environment variables of the same names.) It writes `app_build_<APP_ID>.vdf` and one
`depot_build_<DEPOT_ID>.vdf` per depot. A fresh clone with no values set fails naming the first unset value and writes nothing.

## Founder actions (in order)

1. **Create the Steamworks partner app and app ID** (partner account, app fee). Create a macOS depot and a Windows depot
   in the app's SteamPipe settings; note the three numbers (app, mac depot, win depot). Windows is optional until the
   build module exists (Q-231): leave its depot unused by deleting its line from `app_build.vdf.template` first.
2. **Accept the Steamworks SDK licence** and download the SDK yourself; place `steamcmd` (from the SDK's
   `tools/ContentBuilder/builder_osx` folder, or the standalone steamcmd) somewhere on your machine. Nothing in this repo
   downloads or vendors it. The SDK's later use (the `IPlatformServices` Steam adapter, T-141 seam) is a separate row.
3. **Produce the player build and its attribution bundle** (T-140 `build.sh`, T-142 `Pipelines/build/attribution.py`
   and `copy_tiles.py`); put the macOS build under `<CONTENT_ROOT>/mac` and Windows under `<CONTENT_ROOT>/win`.
4. **Render the VDFs** with the command above.
5. **Run the upload** yourself (this signs in with your Steam account, so it is yours to type):
   `steamcmd +login <your_builder_account> +run_app_build <out>/app_build_<APP_ID>.vdf +quit`.
   Use `"preview" "1"` in the rendered file first for a dry run that lists what would upload.
6. **Set a branch live** in the Steamworks partner site (publishing is a founder act; the VDF leaves `setlive` empty).

Format reference: Valve's "Uploading to Steam" documentation (partner.steamgames.com/doc/sdk/uploading), SteamPipe
`appbuild` / `DepotBuildConfig` sections.
