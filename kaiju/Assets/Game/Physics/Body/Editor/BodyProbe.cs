using System;
using System.IO;
using System.Text;
using Unity.Collections;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Game.Physics.Body.Editor
{
    /// <summary>R-005 risk probes (R1 drive units/sign, spherical DOF frames, contact impulses, defaults). Prints measured facts; no pass/fail.</summary>
    public static class BodyProbe
    {
        static readonly StringBuilder Sb = new StringBuilder();
        const float Dt = 1f / 60f;
        static void L(string s) { Sb.AppendLine(s); }
        static string V(Vector3 v) => $"({v.x:G6},{v.y:G6},{v.z:G6})";

        public static void Run()
        {
            string outPath = "probe.txt";
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++) if (args[i] == "-probeOut") outPath = args[i + 1];
            UnityEngine.Physics.simulationMode = SimulationMode.Script;
            foreach (var step in new Action[] { P4m })
            {
                try { EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single); UnityEngine.Physics.gravity = new Vector3(0, -9.81f, 0); step(); }
                catch (Exception e) { L("EXC in " + step.Method.Name + ": " + e); }
            }
            File.WriteAllText(outPath, Sb.ToString());
        }

        static ArticulationBody Root(string name, Vector3 pos, bool immovable, float mass = 1e6f)
        {
            var go = new GameObject(name); go.transform.position = pos;
            var ab = go.AddComponent<ArticulationBody>(); ab.immovable = immovable; ab.mass = mass; ab.useGravity = true;
            ab.automaticCenterOfMass = false; ab.automaticInertiaTensor = false; ab.centerOfMass = Vector3.zero; ab.inertiaTensor = Vector3.one * 1e7f; ab.inertiaTensorRotation = Quaternion.identity;
            return ab;
        }
        static ArticulationBody Child(ArticulationBody parent, ArticulationJointType type, float mass, Vector3 com, float inertia)
        {
            var go = new GameObject("child"); go.transform.SetParent(parent.transform, false);
            var ab = go.AddComponent<ArticulationBody>(); ab.jointType = type; ab.anchorPosition = Vector3.zero; ab.anchorRotation = Quaternion.identity; ab.matchAnchors = true;
            ab.mass = mass; ab.automaticCenterOfMass = false; ab.automaticInertiaTensor = false; ab.centerOfMass = com; ab.inertiaTensor = Vector3.one * inertia; ab.inertiaTensorRotation = Quaternion.identity;
            ab.linearDamping = 0; ab.angularDamping = 0; ab.jointFriction = 0;
            if (type == ArticulationJointType.RevoluteJoint) ab.twistLock = ArticulationDofLock.FreeMotion;
            if (type == ArticulationJointType.SphericalJoint) { ab.twistLock = ArticulationDofLock.FreeMotion; ab.swingYLock = ArticulationDofLock.FreeMotion; ab.swingZLock = ArticulationDofLock.FreeMotion; }
            return ab;
        }
        static void Sim(int n) { for (int i = 0; i < n; i++) UnityEngine.Physics.Simulate(Dt); }
        static string Rel(ArticulationBody c)
        {
            c.transform.localRotation.ToAngleAxis(out float ang, out Vector3 ax);
            if (ang > 180) { ang = 360 - ang; ax = -ax; }
            return $"angle={ang:F3}deg axis={V(ax)}";
        }

        static void P1()
        {
            var ab = Root("free", new Vector3(0, 100, 0), false);
            L($"P1 defaults: linDamp={ab.linearDamping} angDamp={ab.angularDamping} jointFriction={ab.jointFriction} maxDepen={ab.maxDepenetrationVelocity} maxAngVel={ab.maxAngularVelocity} maxLinVel={ab.maxLinearVelocity} maxJointVel={ab.maxJointVelocity} sleep={ab.sleepThreshold} solverIt={ab.solverIterations}/{ab.solverVelocityIterations} cdm={ab.collisionDetectionMode}");
            L($"P1 physics: defaultContactOffset={UnityEngine.Physics.defaultContactOffset} bounce={UnityEngine.Physics.bounceThreshold} defDepen={UnityEngine.Physics.defaultMaxDepenetrationVelocity} sleep={UnityEngine.Physics.sleepThreshold} improvedPatch={UnityEngine.Physics.improvedPatchFriction}");
            ab.linearDamping = 0; ab.angularDamping = 0;
            Sim(60);
            L($"P1 free fall 60 steps: y={ab.transform.position.y:F5} (analytic semi-implicit 100-0.5*9.81*(1+1/60)={100 - 0.5 * 9.81 * (1 + 1.0 / 60):F5}) vy={ab.linearVelocity.y:F5}");
        }

        static void P2()
        {
            UnityEngine.Physics.gravity = Vector3.zero;
            const float m = 1e6f, len = 10f, ic = 1e7f; float I = ic + m * len * len;
            // (a) period: k = I (2 pi 0.5)^2 -> T = 2 s if k is per radian
            var root = Root("root", Vector3.zero, true); var c = Child(root, ArticulationJointType.RevoluteJoint, m, new Vector3(0, -len, 0), ic);
            float k = I * Mathf.Pow(2 * Mathf.PI * 0.5f, 2);
            var d = c.xDrive; d.driveType = ArticulationDriveType.Force; d.stiffness = k; d.damping = 0; d.forceLimit = float.MaxValue; d.target = 10f; d.targetVelocity = 0; c.xDrive = d;
            float maxPos = -1e9f; int maxStep = -1; float df1 = 0;
            for (int i = 1; i <= 240; i++) { Sim(1); float q = c.jointPosition[0]; if (i == 1) df1 = c.driveForce[0]; if (q > maxPos) { maxPos = q; maxStep = i; } if (i == maxStep) { } }
            L($"P2a revolute k={k:G5} I={I:G5}: first/abs max jointPosition={maxPos:F5} (rad? 20deg=0.349) at t={maxStep * Dt:F3}s (half period 1.0 s if k per rad, 0.13 s if per deg); driveForce step1={df1:G5} (k*10deg in rad={k * 10 * Mathf.Deg2Rad:G5})");
            // sign: hold with damping, read transform
            d.damping = 2 * I * 2 * Mathf.PI * 0.5f; c.xDrive = d; Sim(600);
            L($"P2b target +10deg on xDrive, anchor identity: jointPosition={c.jointPosition[0]:F5} child local rot {Rel(c)} localEulerX={c.transform.localEulerAngles.x:F3}");
            // (c) damping decay
            d.stiffness = 0; d.damping = I * 1f; d.target = 0; c.xDrive = d; c.jointVelocity = new ArticulationReducedSpace(1f); Sim(60);
            L($"P2c damping d=I: jointVelocity after 1 s from 1 rad/s = {c.jointVelocity[0]:F5} (expect 0.371 if N m s/rad)");
            // (d) jointForce: persistence, sign, units
            d.damping = 0; c.xDrive = d; c.jointVelocity = new ArticulationReducedSpace(0f); c.jointForce = new ArticulationReducedSpace(I * 0.1f); Sim(60);
            float w1 = c.jointVelocity[0]; Vector3 wv = c.angularVelocity;
            L($"P2d jointForce=I*0.1 set once, 60 steps: jointVelocity={w1:F6} (0.1 persistent, 0.00167 one-shot) worldAngVel={V(wv)} jointForceReadback={c.jointForce[0]:G5}");
            c.jointForce = new ArticulationReducedSpace(0f); c.jointVelocity = new ArticulationReducedSpace(0f);
            for (int i = 0; i < 60; i++) { c.jointForce = new ArticulationReducedSpace(I * 0.1f); Sim(1); }
            L($"P2e jointForce set every step: jointVelocity delta={c.jointVelocity[0]:F6}");
            // (f) targetVelocity units: stiffness 0, damping d, targetVelocity 0.5
            d.stiffness = 0; d.damping = I * 5f; d.targetVelocity = 0.5f; c.xDrive = d; c.jointForce = new ArticulationReducedSpace(0f); c.jointVelocity = new ArticulationReducedSpace(0f); Sim(300);
            L($"P2f targetVelocity=0.5: steady jointVelocity={c.jointVelocity[0]:F5} rad/s (0.5 if rad/s, 0.00873 if deg/s)");
            // (g) force limit clamp
            d.stiffness = k; d.damping = 0; d.targetVelocity = 0; d.target = c.jointPosition[0] * Mathf.Rad2Deg + 90f; d.forceLimit = 1e6f; c.xDrive = d; Sim(1);
            L($"P2g forceLimit=1e6 with large error: driveForce={c.driveForce[0]:G6}");
            var idx = new System.Collections.Generic.List<int>(); root.GetDofStartIndices(idx); var forces = new System.Collections.Generic.List<float>(); root.GetDriveForces(forces);
            L($"P2h dofStartIndices=[{string.Join(",", idx)}] driveForces=[{string.Join(",", forces)}] index root={root.index} child={c.index} dofCount={c.dofCount}");
        }

        static void P3()
        {
            UnityEngine.Physics.gravity = Vector3.zero;
            const float m = 1e6f, ic = 1e7f;
            for (int dof = 0; dof < 3; dof++)
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                var root = Root("root", Vector3.zero, true); var c = Child(root, ArticulationJointType.SphericalJoint, m, Vector3.zero, ic);
                Sim(1);
                var f = new ArticulationReducedSpace(0f, 0f, 0f); f[dof] = ic * 0.1f;
                for (int i = 0; i < 30; i++) { c.jointForce = f; Sim(1); }
                L($"P3a spherical jointForce on dof {dof} at zero pose: worldAngVel={V(c.angularVelocity)} jointVel=({c.jointVelocity[0]:F4},{c.jointVelocity[1]:F4},{c.jointVelocity[2]:F4}) dofCount={c.dofCount}");
            }
            for (int dof = 1; dof < 3; dof++)
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                var root = Root("root", Vector3.zero, true); var c = Child(root, ArticulationJointType.SphericalJoint, m, Vector3.zero, ic);
                // hold twist at 45 deg with a stiff drive, then push on a swing dof
                var d = c.xDrive; d.driveType = ArticulationDriveType.Force; d.stiffness = ic * 400; d.damping = ic * 40; d.forceLimit = float.MaxValue; d.target = 45; c.xDrive = d;
                Sim(600);
                string pose = Rel(c);
                var f = new ArticulationReducedSpace(0f, 0f, 0f); f[dof] = ic * 0.1f;
                for (int i = 0; i < 6; i++) { c.jointForce = f; Sim(1); }
                Vector3 w = c.angularVelocity; Vector3 cy = c.transform.TransformDirection(dof == 1 ? Vector3.up : Vector3.forward);
                L($"P3b twist 45 ({pose}) then jointForce dof {dof}: worldAngVel={V(w.normalized)} |w|={w.magnitude:G4}; child-frame axis would be {V(cy)}, parent-frame axis {V(dof == 1 ? Vector3.up : Vector3.forward)}; jointPos=({c.jointPosition[0]:F4},{c.jointPosition[1]:F4},{c.jointPosition[2]:F4})");
            }
            // drive targets on each axis and combined
            float[][] targets = { new float[] { 0, 10, 0 }, new float[] { 0, 0, 10 }, new float[] { 30, 0, 10 }, new float[] { 30, 10, 0 }, new float[] { 30, 10, 10 } };
            foreach (var t in targets)
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                var root = Root("root", Vector3.zero, true); var c = Child(root, ArticulationJointType.SphericalJoint, m, Vector3.zero, ic);
                for (int a = 0; a < 3; a++)
                {
                    var d = a == 0 ? c.xDrive : a == 1 ? c.yDrive : c.zDrive; d.driveType = ArticulationDriveType.Force; d.stiffness = ic * 400; d.damping = ic * 40; d.forceLimit = float.MaxValue; d.target = t[a];
                    if (a == 0) c.xDrive = d; else if (a == 1) c.yDrive = d; else c.zDrive = d;
                }
                Sim(900);
                Quaternion q = c.transform.localRotation;
                L($"P3c targets(x,y,z)=({t[0]},{t[1]},{t[2]}): localRot quat=({q.x:F5},{q.y:F5},{q.z:F5},{q.w:F5}) euler={V(c.transform.localEulerAngles)} {Rel(c)} jointPos=({c.jointPosition[0]:F4},{c.jointPosition[1]:F4},{c.jointPosition[2]:F4})");
            }
        }

        static Vector3 s_sum; static int s_points, s_events; static float s_minSep; static string s_firstPoint;
        static void OnContact(PhysicsScene scene, NativeArray<ContactPairHeader>.ReadOnly headers)
        {
            s_events++; s_sum = Vector3.zero; s_points = 0; s_minSep = 1e9f;
            for (int h = 0; h < headers.Length; h++)
            {
                var hd = headers[h];
                for (int p = 0; p < hd.pairCount; p++)
                {
                    ref readonly var pair = ref hd.GetContactPair(p);
                    for (int k = 0; k < pair.contactCount; k++)
                    {
                        ref readonly var pt = ref pair.GetContactPoint(k);
                        s_sum += pt.impulse; s_points++; if (pt.separation < s_minSep) s_minSep = pt.separation;
                        if (k == 0) s_firstPoint = $"pos={V(pt.position)} n={V(pt.normal)} sep={pt.separation:G4} imp={V(pt.impulse)} colliderIsFoot={(pair.collider != null ? pair.collider.name : "null")}/{(pair.otherCollider != null ? pair.otherCollider.name : "null")} impulseSum={V(pair.impulseSum)}";
                    }
                }
            }
        }
        static void P4()
        {
            var ground = new GameObject("ground"); var gc = ground.AddComponent<BoxCollider>(); gc.size = new Vector3(1000, 10, 1000); ground.transform.position = new Vector3(0, -5, 0);
            var mat = new PhysicsMaterial("foot") { staticFriction = 0.8f, dynamicFriction = 0.6f, bounciness = 0, frictionCombine = PhysicsMaterialCombine.Minimum, bounceCombine = PhysicsMaterialCombine.Minimum };
            gc.material = mat;
            const float m = 1e7f;
            var foot = Root("foot", new Vector3(0, 2.0f, 0), false, m); foot.linearDamping = 0; foot.angularDamping = 0; foot.inertiaTensor = Vector3.one * 2e8f;
            var bc = foot.gameObject.AddComponent<BoxCollider>(); bc.size = new Vector3(10, 4, 15); bc.material = mat; bc.providesContacts = true; bc.contactOffset = 0.1f;
            UnityEngine.Physics.ContactEvent += OnContact;
            try
            {
                Sim(180);
                L($"P4a ContactEvent fired {s_events} times in 180 steps (edit mode, Script sim). last step: points={s_points} sumImpulse={V(s_sum)} expected normal m g dt={m * 9.81f * Dt:G6} minSep={s_minSep:G4} y={foot.transform.position.y:F5}");
                L($"P4a first point: {s_firstPoint}");
                for (int i = 0; i < 60; i++) { foot.AddForce(new Vector3(0.3f * m * 9.81f, 0, 0)); Sim(1); }
                L($"P4b with +x force 0.3 m g: sumImpulse={V(s_sum)} (x=-{0.3f * m * 9.81f * Dt:G6} if friction is included) vx={foot.linearVelocity.x:G4} x={foot.transform.position.x:G5}");
                L($"P4b first point: {s_firstPoint}");
            }
            finally { UnityEngine.Physics.ContactEvent -= OnContact; }
        }

        static void P6()
        {
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube); cube.transform.localScale = new Vector3(10, 40, 10); cube.transform.position = new Vector3(0, 20, 0);
            var lightGo = new GameObject("sun"); var li = lightGo.AddComponent<Light>(); li.type = LightType.Directional; lightGo.transform.rotation = Quaternion.Euler(50, -30, 0);
            var camGo = new GameObject("cam"); var cam = camGo.AddComponent<Camera>(); camGo.transform.position = new Vector3(0, 20, -120); cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.6f, 0.7f, 0.8f); cam.farClipPlane = 2000;
            var rt = new RenderTexture(640, 360, 24); cam.targetTexture = rt; cam.Render();
            RenderTexture.active = rt; var tex = new Texture2D(640, 360, TextureFormat.RGB24, false); tex.ReadPixels(new Rect(0, 0, 640, 360), 0, 0); tex.Apply();
            var px = tex.GetPixels32(); long r = 0, g = 0, b = 0; int distinct = 0; Color32 first = px[0]; foreach (var p in px) { r += p.r; g += p.g; b += p.b; if (p.r != first.r || p.g != first.g) distinct++; }
            string path = Path.Combine(Path.GetDirectoryName(Application.dataPath), "Temp", "probe_capture.png"); File.WriteAllBytes(path, tex.EncodeToPNG());
            L($"P6 capture: mean rgb=({r / px.Length},{g / px.Length},{b / px.Length}) pixelsDifferentFromCorner={distinct} of {px.Length} graphicsDevice={SystemInfo.graphicsDeviceType} file={path}");
            RenderTexture.active = null;
        }

        static void P7()
        {
            UnityEngine.Physics.gravity = Vector3.zero;
            var root = Root("root", Vector3.zero, false); root.gameObject.AddComponent<BoxCollider>().size = Vector3.one * 10;
            var c = Child(root, ArticulationJointType.RevoluteJoint, 1e6f, Vector3.zero, 1e7f); c.gameObject.AddComponent<BoxCollider>().size = Vector3.one * 10;
            var gc = new GameObject("grand"); gc.transform.SetParent(c.transform, false); var g = gc.AddComponent<ArticulationBody>(); g.jointType = ArticulationJointType.RevoluteJoint; g.twistLock = ArticulationDofLock.FreeMotion; g.mass = 1e6f; gc.AddComponent<BoxCollider>().size = Vector3.one * 10;
            Sim(60);
            L($"P7 overlapping parent/child/grandchild colliders after 60 steps: rootV={V(root.linearVelocity)} childJointVel={c.jointVelocity[0]:G4} grandJointVel={g.jointVelocity[0]:G4} (all 0 => adjacent and grandparent pairs filtered)");
        }

        static string s_log;
        static void OnContact2(PhysicsScene scene, NativeArray<ContactPairHeader>.ReadOnly headers)
        {
            s_sum = Vector3.zero; s_points = 0; s_minSep = 1e9f;
            for (int h = 0; h < headers.Length; h++) { var hd = headers[h]; for (int p = 0; p < hd.pairCount; p++) { ref readonly var pair = ref hd.GetContactPair(p); for (int k = 0; k < pair.contactCount; k++) { ref readonly var pt = ref pair.GetContactPoint(k); s_sum += pt.impulse; s_points++; if (pt.separation < s_minSep) s_minSep = pt.separation; } } }
        }
        static void P4v()
        {
            for (int variant = 0; variant < 6; variant++)
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                var ground = new GameObject("ground"); ground.transform.position = new Vector3(0, -5, 0); var gc = ground.AddComponent<BoxCollider>(); gc.size = new Vector3(1000, 10, 1000);
                float m = variant == 4 ? 1e3f : 1e7f; string desc; Func<float> y; Func<Vector3> v; Action<Vector3> push;
                var go = new GameObject("foot"); go.transform.position = new Vector3(0, variant == 3 ? 3f : 2f, 0);
                BoxCollider bc = null;
                if (variant == 1 || variant == 5) { bc = go.AddComponent<BoxCollider>(); bc.size = new Vector3(10, 4, 15); }
                if (variant == 2)
                {
                    bc = go.AddComponent<BoxCollider>(); bc.size = new Vector3(10, 4, 15);
                    var rb = go.AddComponent<Rigidbody>(); rb.mass = m; rb.linearDamping = 0; desc = "Rigidbody"; y = () => rb.position.y; v = () => rb.linearVelocity; push = f => rb.AddForce(f);
                }
                else
                {
                    var ab = go.AddComponent<ArticulationBody>(); ab.mass = m; ab.linearDamping = 0; ab.angularDamping = 0;
                    if (variant == 5) { ab.automaticCenterOfMass = false; ab.automaticInertiaTensor = false; ab.centerOfMass = Vector3.zero; ab.inertiaTensor = Vector3.one * 2e8f; ab.inertiaTensorRotation = Quaternion.identity; }
                    if (bc == null) { bc = go.AddComponent<BoxCollider>(); bc.size = new Vector3(10, 4, 15); }
                    desc = variant == 0 ? "AB collider after, auto inertia" : variant == 1 ? "AB collider before, auto inertia" : variant == 3 ? "AB dropped from 1 m" : variant == 4 ? "AB mass 1e3" : "AB collider before, manual inertia 2e8";
                    y = () => ab.transform.position.y; v = () => ab.linearVelocity; push = f => ab.AddForce(f);
                }
                bc.providesContacts = true;
                UnityEngine.Physics.ContactEvent += OnContact2;
                try
                {
                    Sim(1); string s1 = $"step1 pts={s_points} sep={s_minSep:G4} imp={V(s_sum)}";
                    Sim(179);
                    L($"P4v{variant} {desc}: {s1}; after 180: y={y():F4} pts={s_points} sep={s_minSep:G4} sumImpulse={V(s_sum)} (m g dt={m * 9.81f * Dt:G6})");
                    for (int i = 0; i < 60; i++) { push(new Vector3(0.3f * m * 9.81f, 0, 0)); Sim(1); }
                    L($"   push 0.3 m g: sumImpulse={V(s_sum)} v={V(v())}");
                }
                finally { UnityEngine.Physics.ContactEvent -= OnContact2; }
            }
            var so = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/DynamicsManager.asset")[0]); var it = so.GetIterator(); var names = new StringBuilder();
            while (it.Next(true)) if (it.depth == 0) names.Append(it.name + "=" + (it.propertyType == SerializedPropertyType.Integer ? it.intValue.ToString() : it.propertyType == SerializedPropertyType.Boolean ? it.boolValue.ToString() : it.propertyType == SerializedPropertyType.Float ? it.floatValue.ToString() : it.propertyType == SerializedPropertyType.Enum ? it.enumValueIndex.ToString() : "?") + " ");
            L("DynamicsManager props: " + names);
        }

        static void P4m()
        {
            L("settings: " + PhysicsProjectSettings.Describe());
            foreach (float m in new[] { 1f, 10f, 100f, 1e3f, 1e4f, 1e5f, 1e6f, 1e7f })
            foreach (int it in new[] { 6, 24 })
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                var ground = new GameObject("ground"); ground.transform.position = new Vector3(0, -5, 0); var gc = ground.AddComponent<BoxCollider>(); gc.size = new Vector3(1000, 10, 1000);
                var go = new GameObject("foot"); go.transform.position = new Vector3(0, 2.5f, 0);
                var bc = go.AddComponent<BoxCollider>(); bc.size = new Vector3(10, 4, 15); bc.providesContacts = true;
                var ab = go.AddComponent<ArticulationBody>(); ab.mass = m; ab.linearDamping = 0; ab.angularDamping = 0; ab.solverIterations = it; ab.solverVelocityIterations = it / 6; ab.maxDepenetrationVelocity = 1f; ab.sleepThreshold = 0;
                UnityEngine.Physics.ContactEvent += OnContact2;
                float peak = 0; float minY = 1e9f;
                try { for (int i = 0; i < 300; i++) { Sim(1); peak = Mathf.Max(peak, s_sum.y); minY = Mathf.Min(minY, ab.transform.position.y); } }
                finally { UnityEngine.Physics.ContactEvent -= OnContact2; }
                L($"P4m mass={m:G3} iters={it}: drop 0.5 m then rest: y={ab.transform.position.y:F5} (2.0 = on ground) minY={minY:F5} vy={ab.linearVelocity.y:G3} Fn/mg={s_sum.y / (m * 9.81f * Dt):F4} peakFn/mg={peak / (m * 9.81f * Dt):F2}");
            }
        }
    }
}
