using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Game.Core;
using Game.Physics.Body.Contracts;
using UnityEngine;
using EntityId = Game.Core.EntityId;

namespace Game.Physics.Body.Unity
{
    /// <summary>
    /// Passive physics truth scenes for M-006@v1 (research/R-005 section 8): unit sanity, free drop, knee pendulum, two-body impact, toppling fall.
    /// No controller runs here. Initial conditions (a release height, a tilt, a pendulum angle, an approach velocity) are set once before the first step; after that only PhysX moves anything.
    /// Each scene writes physics_log@v1 and returns scalars; thresholds are applied by Tools/PhysicsMetrics/m006.py, not here.
    /// </summary>
    public static class TruthScenes
    {
        public sealed class Result { public string Scene; public readonly List<KeyValuePair<string, double>> Values = new List<KeyValuePair<string, double>>(); public void Add(string k, double v) => Values.Add(new KeyValuePair<string, double>(k, v)); }

        static string F(double v) => v.ToString("R", CultureInfo.InvariantCulture);
        public static string ToJson(IEnumerable<Result> results)
        {
            var sb = new StringBuilder("{");
            bool first = true;
            foreach (var r in results)
            {
                if (!first) sb.Append(','); first = false;
                sb.Append('"').Append(r.Scene).Append("\":{");
                for (int i = 0; i < r.Values.Count; i++) { if (i > 0) sb.Append(','); sb.Append('"').Append(r.Values[i].Key).Append("\":").Append(double.IsNaN(r.Values[i].Value) ? "null" : F(r.Values[i].Value)); }
                sb.Append('}');
            }
            return sb.Append('}').ToString();
        }

        sealed class Scene : IDisposable
        {
            public BodyWorld World; public ArticulatedBody Body; public GameObject Ground; public long Step; public double Dt;
            public Scene(MechFrameData frame, BodyTuning tuning, double dt, BodyBuildOptions opt, bool ground, string logPath, string scene, string commit, string engineInfo)
            {
                Dt = dt; World = new BodyWorld(tuning, dt, true, null); if (engineInfo != null) World.EngineInfoJson = engineInfo;
                if (ground) Ground = BodyWorld.CreateGround(tuning);
                Body = new ArticulatedBody(World, frame, tuning, EntityId.FromValue(1), null, opt);
                if (logPath != null) World.StartPhysicsLog(Body, logPath, scene, commit);
            }
            public void Advance() { Step++; World.Simulate(Step); }
            public void Dispose() { World.DestroyBody(Body); if (Ground != null) UnityEngine.Object.DestroyImmediate(Ground); World.Dispose(); }
        }

        /// <summary>M-006.1: what the engine actually holds, read back after the build.</summary>
        public static Result UnitSanity(MechFrameData frame, BodyTuning tuning, double dt, string dir, string commit, string engineInfo, string name = "unit_sanity")
        {
            var r = new Result { Scene = name };
            using (var s = new Scene(frame, tuning, dt, new BodyBuildOptions { Visuals = false }, true, Path.Combine(dir, "physics", name + ".physics.jsonl"), name, commit, engineInfo))
            {
                double sum = 0, worst = 0, volume = 0, maxDamp = 0;
                for (int i = 0; i < s.Body.LinkCount; i++)
                {
                    var ab = s.Body.EngineLink(i); double m = ab.mass * tuning.sim_mass_unit_kg; sum += m;
                    worst = Math.Max(worst, Math.Abs(m - frame.segments[i].mass_kg) / frame.segments[i].mass_kg);
                    var box = ab.GetComponent<BoxCollider>(); volume += box.size.x * box.size.y * box.size.z;
                    maxDamp = Math.Max(maxDamp, Math.Max(ab.linearDamping, Math.Max(ab.angularDamping, ab.jointFriction)));
                }
                r.Add("mass_sum_kg", sum); r.Add("mass_config_kg", frame.total_mass_kg); r.Add("mass_sum_rel_error", Math.Abs(sum - frame.total_mass_kg) / frame.total_mass_kg); r.Add("link_mass_worst_rel_error", worst);
                r.Add("gravity_x", UnityEngine.Physics.gravity.x); r.Add("gravity_y", UnityEngine.Physics.gravity.y); r.Add("gravity_z", UnityEngine.Physics.gravity.z); r.Add("fixed_dt", dt);
                r.Add("density_sec4_kg_m3", sum / (0.07 * Math.Pow(frame.total_height_m / 1.8, 3))); r.Add("hull_density_config_kg_m3", sum / frame.hull_volume_m3); r.Add("collider_box_density_kg_m3", sum / volume);
                r.Add("max_damping_readback", maxDamp); r.Add("sim_mass_unit_kg", tuning.sim_mass_unit_kg); r.Add("dof_count", s.Body.DofCount); r.Add("link_count", s.Body.LinkCount); r.Add("foot_count", s.Body.FootCount);
            }
            return r;
        }

        /// <summary>M-006.2 (drop): rigid mech released from a height; energy at release against energy at the last step before any contact.</summary>
        public static Result Drop(MechFrameData frame, BodyTuning tuning, double dt, string dir, string commit, string engineInfo, float height = 20f, float x = 0f, string name = "drop")
        {
            var r = new Result { Scene = name };
            using (var s = new Scene(frame, tuning, dt, new BodyBuildOptions { LockAllJoints = true, Visuals = false, Position = new Vector3(x, height, 0) }, true, Path.Combine(dir, "physics", name + ".physics.jsonl"), name, commit, engineInfo))
            {
                var e0 = BodyEnergetics.Compute(s.Body); var last = e0; double worstGain = 0; int steps = 0;
                for (int i = 0; i < 600; i++)
                {
                    s.Advance();
                    bool touched = false; for (int f = 0; f < s.Body.FootCount; f++) { s.Body.GetFoot(f, out var fs); if (fs.NormalForceN > 0) touched = true; }
                    if (touched || s.Body.NonFootContacts > 0) break;
                    last = BodyEnergetics.Compute(s.Body); steps++; worstGain = Math.Max(worstGain, (last.Total - e0.Total));
                }
                double released = tuning.gravity_mps2 * s.Body.TotalMassKg * height;
                r.Add("release_height_m", height); r.Add("steps_before_contact", steps); r.Add("fall_time_s", steps * dt); r.Add("e_release_J", e0.Total); r.Add("e_before_contact_J", last.Total);
                r.Add("energy_loss_rel_of_released", (e0.Total - last.Total) / (last.KeLin + last.KeRot + (e0.Total - last.Total))); r.Add("energy_gain_worst_rel", worstGain / released); r.Add("threshold_loss", dt / (steps * dt) + 0.005);
                r.Add("speed_expected_mps", Math.Sqrt(2 * tuning.gravity_mps2 * height)); r.Add("x_offset_m", x);
            }
            return r;
        }

        /// <summary>M-006.2 (pendulum): pelvis fixed, every joint locked except one knee with zero stiffness and damping; shank and foot released at 30 degrees.</summary>
        public static Result KneePendulum(MechFrameData frame, BodyTuning tuning, double dt, string dir, string commit, string engineInfo, string name = "pendulum")
        {
            var r = new Result { Scene = name };
            string knee = null; foreach (var j in frame.joints) if (j.id.StartsWith("knee", StringComparison.Ordinal)) { knee = j.id; break; }
            using (var s = new Scene(frame, tuning, dt, new BodyBuildOptions { LockAllJoints = true, ImmovableRoot = true, FreeJoints = new[] { knee }, Visuals = false, Position = new Vector3(0, 30, 0) }, false, Path.Combine(dir, "physics", name + ".physics.jsonl"), name, commit, engineInfo))
            {
                int dof = s.Body.FindDof(knee, "flexion"); var info = s.Body.GetDof(dof);
                var link = s.Body.EngineLink(info.ChildLink);
                link.jointPosition = new ArticulationReducedSpace(info.Sign * 30f * Mathf.Deg2Rad); link.jointVelocity = new ArticulationReducedSpace(0f);
                s.Advance();
                var e0 = BodyEnergetics.Compute(s.Body); double eMin = e0.Total, eMax = e0.Total; int crossings = 0; double prev = s.Body.DofPosition(dof), firstCross = -1, lastCross = -1, amplitudeEnd = 0;
                // small-oscillation estimate of the period from the frame: distal inertia about the knee and the distal centre of mass
                var joint = MechFrameOps.JointOfChild(frame, frame.segments[info.ChildLink].id);
                double inertia = MechFrameOps.DistalInertia(frame, joint, 0), m = 0, my = 0;
                for (int i = 0; i < frame.segments.Length; i++) if (MechFrameOps.IsDescendant(frame, i, info.ChildLink)) { m += frame.segments[i].mass_kg; my += frame.segments[i].mass_kg * (joint.position[1] - frame.segments[i].com[1]); }
                double period = 2 * Math.PI * Math.Sqrt(inertia / (tuning.gravity_mps2 * my));
                int total = (int)Math.Round(10 * period * 1.02 / dt);
                for (int i = 0; i < total; i++)
                {
                    s.Advance();
                    var e = BodyEnergetics.Compute(s.Body); eMin = Math.Min(eMin, e.Total); eMax = Math.Max(eMax, e.Total);
                    double q = s.Body.DofPosition(dof);
                    if (prev < 0 && q >= 0) { crossings++; if (firstCross < 0) firstCross = s.Step * dt; lastCross = s.Step * dt; }
                    if (i > total - (int)(period / dt)) amplitudeEnd = Math.Max(amplitudeEnd, Math.Abs(q));
                    prev = q;
                }
                var e1 = BodyEnergetics.Compute(s.Body);
                // swing energy scale: m g r (1 - cos 30 deg)
                double scale = tuning.gravity_mps2 * my * (1 - Math.Cos(30 * Math.PI / 180));
                r.Add("period_small_angle_s", period); r.Add("period_measured_s", crossings > 1 ? (lastCross - firstCross) / (crossings - 1) : double.NaN); r.Add("periods_run", total * dt / period);
                r.Add("energy_drift_rel_of_swing", (e1.Total - e0.Total) / scale); r.Add("energy_span_rel_of_swing", (eMax - eMin) / scale); r.Add("amplitude_end_deg", amplitudeEnd * 180 / Math.PI); r.Add("omega_dt", 2 * Math.PI / period * dt);
            }
            return r;
        }

        /// <summary>M-006.3: gravity off, friction 0; the rigid mech at 5 m/s meets a 1e6 kg box at rest. Momentum and angular momentum about the combined centre of mass, 1 s before against 2 s after.</summary>
        public static Result Impact(MechFrameData frame, BodyTuning tuning, double dt, string dir, string commit, string engineInfo, double restitution, double offsetM, string name)
        {
            var r = new Result { Scene = name };
            var t = JsonUtility.FromJson<BodyTuning>(JsonUtility.ToJson(tuning)); t.gravity_mps2 = 0; t.friction_static = 0; t.friction_dynamic = 0; t.restitution = restitution;
            using (var s = new Scene(frame, t, dt, new BodyBuildOptions { LockAllJoints = true, Visuals = false, Position = new Vector3(0, 100, 0) }, false, Path.Combine(dir, "physics", name + ".physics.jsonl"), name, commit, engineInfo))
            {
                const double boxMass = 1e6; const float speed = 5f;
                float comY = 100 + (float)frame.com_rest[1], comZ = (float)frame.com_rest[2];
                var go = new GameObject("impact_box"); go.transform.position = new Vector3((float)offsetM, comY, comZ + 5 + 5 + speed * 2f); // contact after about 2 s
                var bc = go.AddComponent<BoxCollider>(); bc.size = new Vector3(10, 10, 10); bc.material = BodyWorld.FootMaterial(t); bc.contactOffset = (float)t.contact_offset_m;
                var rb = go.AddComponent<Rigidbody>(); rb.mass = (float)(boxMass / t.sim_mass_unit_kg); rb.useGravity = false; rb.linearDamping = 0; rb.angularDamping = 0; rb.sleepThreshold = 0; rb.maxDepenetrationVelocity = (float)t.max_depenetration_mps;
                rb.solverIterations = t.solver_iterations; rb.solverVelocityIterations = t.solver_velocity_iterations; rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative; rb.maxAngularVelocity = 50;
                s.Body.EngineLink(0).linearVelocity = new Vector3(0, 0, speed); // initial condition: approach velocity of the rigid mech
                UnityEngine.Physics.SyncTransforms();
                Func<double[]> measure = () =>
                {
                    var e = BodyEnergetics.Compute(s.Body);
                    double m1 = s.Body.TotalMassKg, m2 = boxMass;
                    Vector3 c1 = new Vector3(s.Body.Com.X, s.Body.Com.Y, s.Body.Com.Z), v1 = new Vector3(s.Body.ComVelocity.X, s.Body.ComVelocity.Y, s.Body.ComVelocity.Z), c2 = rb.worldCenterOfMass, v2 = rb.linearVelocity;
                    Vector3 R = (c1 * (float)m1 + c2 * (float)m2) / (float)(m1 + m2), V = (v1 * (float)m1 + v2 * (float)m2) / (float)(m1 + m2);
                    Vector3 p = v1 * (float)(m1 / 1e6) + v2 * (float)(m2 / 1e6); // in 1e6 kg m/s
                    Vector3 w2 = rb.angularVelocity, wl = Quaternion.Inverse(rb.rotation * rb.inertiaTensorRotation) * w2, it = rb.inertiaTensor * (float)(t.sim_mass_unit_kg / 1e6);
                    Vector3 l2 = rb.rotation * rb.inertiaTensorRotation * new Vector3(it.x * wl.x, it.y * wl.y, it.z * wl.z);
                    Vector3 l1 = new Vector3(e.AngularMomentumCom.X, e.AngularMomentumCom.Y, e.AngularMomentumCom.Z) / 1e6f;
                    Vector3 L = l1 + l2 + Vector3.Cross(c1 - R, v1 - V) * (float)(m1 / 1e6) + Vector3.Cross(c2 - R, v2 - V) * (float)(m2 / 1e6);
                    double ke2 = 0.5 * m2 * v2.sqrMagnitude + 0.5e6 * (it.x * wl.x * wl.x + it.y * wl.y * wl.y + it.z * wl.z * wl.z);
                    return new double[] { p.x, p.y, p.z, L.x, L.y, L.z, e.KeLin + e.KeRot + ke2, e.MaxLinkSpeed, v2.magnitude };
                };
                int contactStep = -1; double[] before = null, after = null; var history = new List<double[]>();
                for (int i = 0; i < (int)Math.Round(8.0 / dt); i++)
                {
                    history.Add(measure());
                    s.Advance();
                    if (contactStep < 0 && rb.linearVelocity.sqrMagnitude > 1e-6f) { contactStep = i; before = history[Math.Max(0, i - (int)Math.Round(1.0 / dt))]; }
                    if (contactStep >= 0 && i == contactStep + (int)Math.Round(2.0 / dt)) { after = measure(); break; }
                }
                if (before == null || after == null) { r.Add("contact_found", 0); UnityEngine.Object.DestroyImmediate(go); return r; }
                double p0 = Math.Sqrt(before[0] * before[0] + before[1] * before[1] + before[2] * before[2]), dp = Math.Sqrt(Math.Pow(after[0] - before[0], 2) + Math.Pow(after[1] - before[1], 2) + Math.Pow(after[2] - before[2], 2));
                double l0 = Math.Sqrt(before[3] * before[3] + before[4] * before[4] + before[5] * before[5]), dl = Math.Sqrt(Math.Pow(after[3] - before[3], 2) + Math.Pow(after[4] - before[4], 2) + Math.Pow(after[5] - before[5], 2));
                // reference angular momentum for the relative error when the pair starts with none about its own centre of mass: reduced mass x speed x offset
                double lRef = (s.Body.TotalMassKg * boxMass / (s.Body.TotalMassKg + boxMass)) / 1e6 * speed * Math.Max(Math.Abs(offsetM), 1.0);
                r.Add("contact_found", 1); r.Add("restitution", restitution); r.Add("offset_m", offsetM); r.Add("contact_time_s", contactStep * dt);
                r.Add("p_before_1e6kgmps", p0); r.Add("dp_rel", dp / p0); r.Add("L_before_1e6", l0); r.Add("dL_abs_1e6", dl); r.Add("dL_rel", dl / Math.Max(l0, lRef));
                r.Add("ke_before_J", before[6]); r.Add("ke_after_J", after[6]); r.Add("ke_ratio", after[6] / before[6]); r.Add("ke_ratio_expected_e0_headon", s.Body.TotalMassKg / (s.Body.TotalMassKg + boxMass)); r.Add("box_speed_after_mps", after[8]);
                UnityEngine.Object.DestroyImmediate(go);
            }
            return r;
        }

        /// <summary>M-006.5: mech tilted 5 degrees forward about the toe line and released. Locked (rigid) or limp (zero stiffness, declared drive damping).</summary>
        public static Result Topple(MechFrameData frame, BodyTuning tuning, double dt, string dir, string commit, string engineInfo, bool limp, double limpDampingRatio, string name)
        {
            var r = new Result { Scene = name };
            // R-005 section 7: the line from the toe pivot to the centre of mass starts 5 degrees past vertical
            float toe = (float)frame.foot_sole.toe_z_m; float poise = Mathf.Atan2(toe - (float)frame.com_rest[2], (float)frame.com_rest[1]) * Mathf.Rad2Deg;
            var rot = Quaternion.Euler(poise + 5f, 0, 0); Vector3 pivot = new Vector3(0, 0, toe);
            var opt = new BodyBuildOptions { LockAllJoints = !limp, Visuals = false, Rotation = rot, Position = pivot + rot * (Vector3.zero - pivot) + new Vector3(0, 0.02f, 0) };
            using (var s = new Scene(frame, tuning, dt, opt, true, Path.Combine(dir, "physics", name + ".physics.jsonl"), name, commit, engineInfo))
            {
                if (limp) for (int d = 0; d < s.Body.DofCount; d++) { s.Body.GetDofGains(d, false, out _, out double damping); s.Body.SetDofDrive(d, 0, 0, 0, damping * limpDampingRatio); }
                var e0 = BodyEnergetics.Compute(s.Body); double dissipated = 0; int impactStep = -1; BodyEnergetics atImpact = e0; double wImpact = 0, vImpact = 0, maxLink = 0, maxJoint = 0; double restTime = 0; int endStep = -1;
                double eJumpMax = 0, ePrev = e0.Total; var prevState = BodyEnergetics.Compute(s.Body); double wPrev = 0, vPrev = 0, dissPrev = 0;
                for (int i = 0; i < (int)Math.Round(60.0 / dt); i++)
                {
                    s.Advance();
                    var e = BodyEnergetics.Compute(s.Body);
                    for (int d = 0; d < s.Body.DofCount; d++) { dissipated -= s.Body.DofDriveTorque(d) * s.Body.DofVelocity(d) * dt; maxJoint = Math.Max(maxJoint, Math.Abs(s.Body.DofVelocity(d))); }
                    maxLink = Math.Max(maxLink, e.MaxLinkSpeed); eJumpMax = Math.Max(eJumpMax, e.Total - ePrev); ePrev = e.Total;
                    s.Body.GetLink(0, out var root);
                    if (impactStep < 0 && s.Body.NonFootContacts > 0) { impactStep = i; atImpact = prevState; wImpact = wPrev; vImpact = vPrev; r.Add("dissipated_to_impact_J", dissPrev); }
                    prevState = e; wPrev = root.AngularVelocity.Length(); vPrev = s.Body.ComVelocity.Length(); dissPrev = dissipated;
                    if (impactStep >= 0) { restTime = e.KeLin + e.KeRot < 1e-4 * e0.Pe ? restTime + dt : 0; if (restTime > 2.0) { endStep = i; break; } }
                }
                var e1 = BodyEnergetics.Compute(s.Body);
                r.Add("impact_found", impactStep >= 0 ? 1 : 0); r.Add("t_fall_s", impactStep * dt); r.Add("omega_impact_rps", wImpact); r.Add("com_speed_impact_mps", vImpact);
                r.Add("dpe_to_impact_J", e0.Pe - atImpact.Pe); r.Add("ke_impact_J", atImpact.KeLin + atImpact.KeRot); r.Add("ke_over_dpe_impact", (atImpact.KeLin + atImpact.KeRot) / (e0.Pe - atImpact.Pe));
                r.Add("released_to_rest_J", e0.Pe - e1.Pe); r.Add("came_to_rest", endStep >= 0 ? 1 : 0); r.Add("t_rest_s", endStep * dt); r.Add("com_height_start_m", e0.Pe / (s.Body.TotalMassKg * tuning.gravity_mps2)); r.Add("com_height_end_m", e1.Pe / (s.Body.TotalMassKg * tuning.gravity_mps2));
                r.Add("mgh_J", e0.Pe); r.Add("body_tilt_deg", poise + 5f); r.Add("mgh_standing_J", s.Body.TotalMassKg * tuning.gravity_mps2 * frame.com_rest[1]); r.Add("drive_dissipation_total_J", dissipated); r.Add("max_link_speed_mps", maxLink); r.Add("max_joint_speed_rps", maxJoint); r.Add("max_energy_gain_step_J", eJumpMax); r.Add("limp_damping_ratio", limp ? limpDampingRatio : 0);
            }
            return r;
        }
    }
}
