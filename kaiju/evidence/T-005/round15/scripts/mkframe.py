"""T-005 r15: frame JSON for the 10 x 15 m sole and stance half-width 8 m (mirrors MechConfig.WithExperiments body_r14 + body_r15_stance). Usage: mkframe.py L W half out.json"""
import json, sys
L, W, half, out = float(sys.argv[1]), float(sys.argv[2]), float(sys.argv[3]), sys.argv[4]
J = json.load(open("Assets/Game/Config/Mech/mech_frame_balanced.json")); fs = J["foot_sole"]
fh = -fs["heel_z_m"] / fs["length_m"]; heel = fs["ankle_z_m"] - fh * L; toe = heel + L
fs.update(length_m=L, width_m=W, area_m2=L * W, heel_z_m=heel, toe_z_m=toe)
for s in J["segments"]:
    if s["kind"] == "foot":
        h, m = s["height_y_m"], s["mass_kg"]; v0 = s["depth_z_m"] * s["width_x_m"] * h
        s["depth_z_m"] = s["length_m"] = L; s["width_x_m"] = W; s["distal_end"][2] = toe; s["com"][2] = 0.5 * (heel + toe)
        s["hull_volume_m3"] *= L * W * h / v0; s["inertia_principal_kgm2"] = [m*(h*h+L*L)/12, m*(W*W+L*L)/12, m*(W*W+h*h)/12]
if half > 0:
    sg = lambda x: 1 if x > 0 else -1
    for s in J["segments"]:
        if s["kind"] in ("thigh", "shank", "foot"):
            for k in ("proximal_joint", "distal_end", "com"):
                if s.get(k): s[k][0] = sg(s[k][0]) * half
    for j in J["joints"]:
        if j["id"].split("_")[0] in ("hip", "knee", "ankle"): j["position"][0] = sg(j["position"][0]) * half
    J["stance"]["neutral_stance_width_m"] = 2 * half
json.dump(J, open(out, "w"), indent=1)
print("sole", heel, toe, "area", L * W, "stance", 2 * half)
