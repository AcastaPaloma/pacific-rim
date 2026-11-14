"""R-002: print the markdown tables used in research/R-002.md from the JSON outputs."""
import json, os
from common import OUT

p = json.load(open(os.path.join(OUT, "panels.json")))["panels"]
pal = json.load(open(os.path.join(OUT, "palettes.json")))
m3 = json.load(open(os.path.join(OUT, "m003_validation.json")))
m4 = json.load(open(os.path.join(OUT, "m004_validation.json")))

print("| Panel | Box x0,y0,x1,y1 | Grid px (R) | Native WxH | Uniq raw | Knee k | Pal (dE3) | L* bands / levels | Hue fam. | Accent (share) | Self-adh. dn @1/2/3/5 | raw @2/@5 | dE95 dn |")
print("|---|---|---|---|---|---|---|---|---|---|---|---|---|")
for k, v in p.items():
    g = v["grid"]; a = v["accent"]; r = m3["references"][k]
    gs = ("%.2f" % g["p"] if g["p"] else "none") + " (%.2f%s)" % (max(g["rx"], g["ry"]), ", weak" if g["weak"] else "")
    print(f"| {k} | {','.join(map(str, v['box_xyxy']))} | {gs} | {'x'.join(map(str, g['native'])) if g['native'] else '—'} | "
          f"{v['unique_colours_raw']} | {v['kmeans_k_knee']} | {v['palette_size']} | {v['value_band_count']} / {r['value_levels']} | "
          f"{r['hue_families']} | {(a['hex'] + ' (%.1f%%)' % (100 * a['share'])) if a else '—'} | "
          f"{' / '.join('%.2f' % v['adherence_denoised'][t] for t in '1235')} | {v['adherence_raw']['2']:.2f} / {v['adherence_raw']['5']:.2f} | {v['de95_denoised']:.1f} |")
print()
print("| Panel | DIG | O | interleaved | banded-gradient | sky share | horizon (from bottom) | dL* P10-P90 |")
print("|---|---|---|---|---|---|---|---|")
for k, r in m3["references"].items():
    d = r["dither"]; c = r["composition"]
    print(f"| {k} | {d['DIG']:.2f} | {d['O']:.2f} | {d['interleaved_share']:.2f} | {d['banded_gradient_share']:.2f} | {c['sky_share']:.2f} | {c['horizon_from_bottom']:.2f} | {c['silhouette_dL_10_90']:.0f} |")
print()
print("| Profile | Members | Size | Ramp (dark->light) | Accent | Member adherence @2 / @3 / @5 |")
print("|---|---|---|---|---|---|")
for n, pr in pal["profiles"].items():
    ramp = " ".join(c["hex"] for c in pr["colours"] if c["role"].startswith("ramp"))
    acc = [c["hex"] for c in pr["colours"] if c["role"] == "accent"]
    ad = "; ".join(f"{m}: {a[0]['2']:.2f}/{a[0]['3']:.2f}/{a[0]['5']:.2f}" for m, a in pr["member_adherence"].items())
    print(f"| {n} | {', '.join(pr['members'])} | {pr['size']} | {ramp} | {acc[0] if acc else '—'} | {ad} |")
print()
print("cross-profile @5:")
for k, v in pal["cross_profile_adherence_at_5"].items():
    print(k, {a: round(b, 2) for a, b in v.items()})
print()
for k, v in m3["synthetic"].items():
    print(k, v)
print()
for k, v in m4["results"].items():
    print(k, {a: b for a, b in v.items() if a in ("lag", "shimmer_per_px", "crawl_per_px", "shimmer", "shimmer_p95", "flicker_reversal")})
