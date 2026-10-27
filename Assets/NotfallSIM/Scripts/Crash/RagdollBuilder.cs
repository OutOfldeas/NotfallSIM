using System.Collections.Generic;
using System.Linq;
using NotfallSIM.Anatomy;
using UnityEngine;

namespace NotfallSIM.Crash
{
    public enum RagdollPose { Standing, Seated, Straddling }

    /// <summary>Ein fertiges Ragdoll: Segmente nach Namen, jedes mit Rigidbody und CrashSensor.</summary>
    public class Ragdoll
    {
        public GameObject Root;
        public readonly Dictionary<string, Rigidbody> Segments = new Dictionary<string, Rigidbody>();
        public readonly List<CrashSensor> Sensors = new List<CrashSensor>();
        public Rigidbody Pelvis => Segments["Becken"];
        public Rigidbody Thorax => Segments["Thorax"];
        public Rigidbody Head => Segments["Kopf"];
        /// <summary>Brustbein-Platte vor dem Thorax, federnd gelagert (Brustkorbdeflexion).</summary>
        public Rigidbody Sternum;
        public float SternumRestOffset; // Abstand Sternum–Thorax entlang „vorn“ in Ruhe
        public ConfigurableJoint NeckJoint, HipJointRight;
        public Transform Body => Root.transform;
        /// <summary>Für die Risiko-Heatmap: je Segment ein einfärbbarer Körper (beim anatomischen Ragdoll anfangs unsichtbar).</summary>
        public readonly Dictionary<string, Renderer> Heat = new Dictionary<string, Renderer>();
        public SkinnedMeshRenderer Skin;
        /// <summary>Fingerspitzen in der Ausgangshaltung (Welt) — daran werden Lenkrad und Lenker ausgerichtet.</summary>
        public Vector3 HandTipRight, HandTipLeft;

        public void ShowHeat(string segment, Color c)
        {
            if (!Heat.TryGetValue(segment, out var r) || r == null) return;
            r.enabled = true;
            r.material.color = c;
        }

        public void SetVelocity(Vector3 v)
        {
            foreach (var rb in Segments.Values) CrashPhysics.SetVelocity(rb, v);
        }
    }

    /// <summary>
    /// Baut ein Ragdoll aus Grundkörpern zur Laufzeit — ohne Modell-Import, damit die Sandbox sofort läuft.
    /// Segmentmassen nach de Leva (1996), Segmentlängen als Anteil der Körpergröße (Drillis/Contini, gerundet).
    /// Später kann ein echtes Modell mit Skelett dieselben Segmentnamen bekommen.
    ///
    /// Koordinaten: Blick in +z, rechts = +x (aus Sicht des Patienten), oben = +y. Ursprung: Mitte zwischen den Hüftgelenken.
    /// </summary>
    /// <summary>Körpermaße für den Aufbau der Unfallszenen (Sitzhöhe, Armaturenbrett, Lenkrad …).</summary>
    public struct RagdollDims
    {
        public float HipToKnee, KneeToSole, ThighRadius, ShoulderAboveHip, HeadTopAboveHip;
    }

    public static class RagdollBuilder
    {
        public static RagdollDims Dims(float H)
        {
            if (!AnatomyBuilder.Available)
                return new RagdollDims { HipToKnee = 0.245f * H, KneeToSole = 0.30f * H, ThighRadius = 0.042f * H, ShoulderAboveHip = 0.27f * H, HeadTopAboveHip = 0.42f * H };
            var h = AnatomyBuilder.Load().Header;
            float k = H / h.modelHeight;
            System.Func<string, Vector3> J = n => AnatomyBuilder.V(h.joints.First(j => j.name == n).pos) * k;
            var thigh = h.segments.First(x => x.name == "Oberschenkel rechts");
            return new RagdollDims
            {
                HipToKnee = (J("Knie rechts") - J("Hüfte rechts")).magnitude,
                KneeToSole = J("Knie rechts").y - h.floorY * k,
                ThighRadius = thigh.radius * k,
                ShoulderAboveHip = J("Schulter rechts").y,
                HeadTopAboveHip = J("Scheitel").y,
            };
        }

        /// <summary>
        /// Ragdoll mit anatomischem Skelett und Haut (aus anatomy/body.bin), sonst aus Grundkörpern.
        /// <paramref name="hipCenter"/> = Mitte zwischen den Hüftgelenken.
        /// </summary>
        public static Ragdoll Build(Vector3 hipCenter, Quaternion facing, RagdollPose pose, float height_m, float mass_kg, bool male,
                                    bool invertKneeFlexion = false, float headScale = 1f)
        {
            if (AnatomyBuilder.Available)
            {
                try { return BuildAnatomical(hipCenter, facing, pose, height_m, mass_kg, male, invertKneeFlexion, headScale); }
                catch (System.Exception e) { Debug.LogWarning("[Ragdoll] Anatomisches Modell fehlgeschlagen, nutze Grundkörper: " + e.Message); }
            }
            return BuildPrimitive(hipCenter, facing, pose, height_m, mass_kg, male, invertKneeFlexion, headScale);
        }

        /// <summary>Gelenkstellungen je Haltung relativ zur Ruhelage (aufrecht, Arme hängend). Positiver x-Winkel = Vorderseite nach unten.</summary>
        static Quaternion PoseRotation(RagdollPose pose, string seg)
        {
            bool right = seg.EndsWith("rechts");
            float side = right ? 1 : -1;
            switch (pose)
            {
                case RagdollPose.Seated:
                    if (seg.StartsWith("Oberschenkel")) return Quaternion.Euler(-90, 0, 0);
                    if (seg.StartsWith("Unterschenkel")) return Quaternion.Euler(90, 0, 0);
                    if (seg.StartsWith("Oberarm")) return Quaternion.Euler(-40, 0, side * 6);
                    // Hände am Lenkrad: Ellbogen gebeugt, Unterarm proniert (Handflächen zum Lenkrad)
                    if (seg.StartsWith("Unterarm")) return Quaternion.Euler(-55, 0, 0) * Quaternion.Euler(0, -side * 80, 0);
                    return Quaternion.identity;
                case RagdollPose.Straddling:
                    if (seg.StartsWith("Oberschenkel")) return Quaternion.Euler(-75, 0, side * 14);
                    if (seg.StartsWith("Unterschenkel")) return Quaternion.Euler(70, 0, -side * 6);
                    if (seg.StartsWith("Oberarm")) return Quaternion.Euler(-62, 0, side * 7);
                    if (seg.StartsWith("Unterarm")) return Quaternion.Euler(-18, 0, 0) * Quaternion.Euler(0, -side * 80, 0);
                    if (seg == "Abdomen") return Quaternion.Euler(14, 0, 0);
                    if (seg == "Thorax") return Quaternion.Euler(10, 0, 0);
                    return Quaternion.identity;
                default:
                    if (seg.StartsWith("Oberarm")) return Quaternion.Euler(0, 0, side * 4);
                    return Quaternion.identity;
            }
        }

        static Ragdoll BuildAnatomical(Vector3 hipCenter, Quaternion facing, RagdollPose pose, float H, float mass_kg, bool male,
                                       bool invertKneeFlexion, float headScale)
        {
            var data = AnatomyBuilder.Load();
            var h = data.Header;
            float k = H / h.modelHeight;
            var rd = new Ragdoll { Root = new GameObject("Ragdoll") };
            rd.Root.transform.SetPositionAndRotation(hipCenter, facing);
            var T = rd.Root.transform;
            var heatMat = CrashPhysics.Mat(new Color(0.62f, 0.69f, 0.78f));

            int n = h.segments.Length;
            var rbs = new Rigidbody[n];
            var worldRot = new Quaternion[n];
            var bones = new Transform[n];
            for (int i = 0; i < n; i++)
            {
                var s = h.segments[i];
                int p = string.IsNullOrEmpty(s.parent) ? -1 : data.SegmentIndex(s.parent);
                Vector3 joint = AnatomyBuilder.V(s.joint) * k, end = AnatomyBuilder.V(s.end) * k;
                Vector3 pos; Quaternion rot;
                if (p < 0) { pos = hipCenter; rot = facing * PoseRotation(pose, s.name); }
                else
                {
                    Vector3 parentJoint = AnatomyBuilder.V(h.segments[p].joint) * k;
                    pos = rbs[p].position + worldRot[p] * (joint - parentJoint);
                    rot = worldRot[p] * PoseRotation(pose, s.name);
                }
                worldRot[i] = rot;
                var go = new GameObject(s.name);
                go.transform.SetParent(T, true);
                go.transform.SetPositionAndRotation(pos, rot);
                bones[i] = go.transform;

                // Kollisionsform in Ruhelage-Achsen (Segmentursprung = proximales Gelenk)
                Vector3 axis = end - joint;
                GameObject vis;
                if (s.capsule)
                {
                    float r = s.radius * k;
                    float len = axis.magnitude;
                    if (s.name.StartsWith("Unterschenkel")) { axis += new Vector3(0, -(AnatomyBuilder.V(s.end).y - h.floorY) * k * 0.8f, 0); len = axis.magnitude; }
                    var c = go.AddComponent<CapsuleCollider>();
                    c.direction = 1;
                    c.center = axis / 2;
                    c.radius = r;
                    c.height = len + 2 * r * 0.5f;
                    vis = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                    vis.transform.SetParent(go.transform, false);
                    vis.transform.localPosition = axis / 2;
                    vis.transform.localRotation = Quaternion.FromToRotation(Vector3.up, axis.normalized);
                    vis.transform.localScale = new Vector3(2 * r, len / 2 + r * 0.5f, 2 * r);
                }
                else if (s.name == "Kopf")
                {
                    var c = go.AddComponent<SphereCollider>();
                    c.center = AnatomyBuilder.V(s.boxCenter) * k;
                    c.radius = Mathf.Max(s.boxSize[0], s.boxSize[2]) * 0.5f * k * 0.9f * headScale;
                    vis = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                    vis.transform.SetParent(go.transform, false);
                    vis.transform.localPosition = c.center;
                    vis.transform.localScale = Vector3.one * c.radius * 2;
                }
                else
                {
                    var c = go.AddComponent<BoxCollider>();
                    c.center = AnatomyBuilder.V(s.boxCenter) * k;
                    c.size = AnatomyBuilder.V(s.boxSize) * k * 0.9f;
                    vis = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    vis.transform.SetParent(go.transform, false);
                    vis.transform.localPosition = c.center;
                    vis.transform.localScale = c.size;
                }
                Object.Destroy(vis.GetComponent<Collider>());
                var vr = vis.GetComponent<Renderer>();
                vr.sharedMaterial = heatMat;
                vr.enabled = false;
                rd.Heat[s.name] = vr;

                float frac = male ? s.massMale : s.massFemale;
                rbs[i] = Finish(rd, go, s.name, s.region, frac * mass_kg, keepRenderer: true);
            }

            int fr = data.SegmentIndex("Unterarm rechts"), fl = data.SegmentIndex("Unterarm links");
            rd.HandTipRight = bones[fr].TransformPoint((AnatomyBuilder.V(h.segments[fr].end) - AnatomyBuilder.V(h.segments[fr].joint)) * k);
            rd.HandTipLeft = bones[fl].TransformPoint((AnatomyBuilder.V(h.segments[fl].end) - AnatomyBuilder.V(h.segments[fl].joint)) * k);

            // Gelenke: Anker am Segmentursprung, Beugeachse = Querachse des Körpers
            for (int i = 0; i < n; i++)
            {
                var s = h.segments[i];
                if (string.IsNullOrEmpty(s.parent)) continue;
                var parent = rbs[data.SegmentIndex(s.parent)];
                ConfigurableJoint j;
                if (s.name == "Kopf") j = rd.NeckJoint = Joint(rbs[i], parent, rbs[i].position, T, 45, 45, 40, 30, 60);
                else if (s.name == "Abdomen" || s.name == "Thorax") j = Joint(rbs[i], parent, rbs[i].position, T, 25, 25, 20, 20, 300);
                else if (s.name.StartsWith("Oberarm")) j = Joint(rbs[i], parent, rbs[i].position, T, 100, 100, 80, 80, 20);
                else if (s.name.StartsWith("Unterarm")) j = Joint(rbs[i], parent, rbs[i].position, T, 130, 130, 10, 10, 10);
                else if (s.name.StartsWith("Oberschenkel")) j = Joint(rbs[i], parent, rbs[i].position, T, 100, 100, 30, 40, 40);
                else j = Joint(rbs[i], parent, rbs[i].position, T, invertKneeFlexion ? 140 : 5, invertKneeFlexion ? 5 : 140, 5, 5, 30);
                if (s.name == "Oberschenkel rechts") rd.HipJointRight = j;
            }

            // Brustbein vor dem Thorax (Brustkorbdeflexion)
            var thoraxSeg = h.segments[data.SegmentIndex("Thorax")];
            var thorax = rbs[data.SegmentIndex("Thorax")];
            Vector3 bc = AnatomyBuilder.V(thoraxSeg.boxCenter) * k, bs = AnatomyBuilder.V(thoraxSeg.boxSize) * k * 0.9f;
            AddSternumAt(rd, thorax, thorax.transform.TransformPoint(bc + new Vector3(0, 0.05f * bs.y, bs.z / 2 + 0.012f)), T, 0.32f * bs.x, 0.6f * bs.y);

            // Selbstkollisionen im Ragdoll ausschalten (Startüberlappungen würden es sonst sprengen)
            var cols = rd.Root.GetComponentsInChildren<Collider>();
            for (int a = 0; a < cols.Length; a++)
                for (int b = a + 1; b < cols.Length; b++)
                    Physics.IgnoreCollision(cols[a], cols[b]);

            // Haut an die Segmente binden
            rd.Skin = AnatomyBuilder.BuildSkinForBones(T, bones, k, CrashPhysics.Mat(new Color(0.86f, 0.67f, 0.55f)), CrashPhysics.Mat(new Color(0.12f, 0.15f, 0.22f)));
            return rd;
        }

        /// <summary>Ragdoll aus Grundkörpern (ohne anatomy/body.bin).</summary>
        public static Ragdoll BuildPrimitive(Vector3 hipCenter, Quaternion facing, RagdollPose pose, float height_m, float mass_kg, bool male,
                                             bool invertKneeFlexion = false, float headScale = 1f)
        {
            var rdp = BuildPrimitiveCore(hipCenter, facing, pose, height_m, mass_kg, male, invertKneeFlexion, headScale);
            foreach (var kv in rdp.Segments) rdp.Heat[kv.Key] = kv.Value.GetComponent<Renderer>();
            return rdp;
        }

        // Massenanteile am Körpergewicht (de Leva 1996): Kopf, Rumpf oben/mitte/unten, Oberarm, Unterarm+Hand, Oberschenkel, Unterschenkel+Fuß
        static readonly float[] MaleMass   = { 0.0694f, 0.1596f, 0.1633f, 0.1117f, 0.0271f, 0.0162f + 0.0061f, 0.1416f, 0.0433f + 0.0137f };
        static readonly float[] FemaleMass = { 0.0668f, 0.1545f, 0.1465f, 0.1247f, 0.0255f, 0.0138f + 0.0056f, 0.1478f, 0.0481f + 0.0129f };

        /// <summary>
        /// VERIFY in Unity: Vorzeichen der Kniebeugung im ConfigurableJoint. Wenn die Knie nach hinten durchknicken,
        /// im CrashRig „Invert Knee Flexion“ umschalten.
        /// </summary>
        static Ragdoll BuildPrimitiveCore(Vector3 hipCenter, Quaternion facing, RagdollPose pose, float height_m, float mass_kg, bool male,
                                          bool invertKneeFlexion, float headScale)
        {
            float H = height_m;
            var m = male ? MaleMass : FemaleMass;
            var rd = new Ragdoll { Root = new GameObject("Ragdoll") };
            rd.Root.transform.SetPositionAndRotation(hipCenter, facing);
            var T = rd.Root.transform;
            System.Func<Vector3, Vector3> P = local => T.TransformPoint(local);

            // ---- Schlüsselpunkte (lokal, relativ zur Hüftmitte)
            float hipX = 0.055f * H;
            float shoulderY = 0.27f * H, shoulderX = 0.13f * H;
            float thigh = 0.245f * H, shank = 0.27f * H;
            Vector3 hipR = new Vector3(hipX, 0, 0), hipL = new Vector3(-hipX, 0, 0);
            Vector3 kneeR, kneeL, footR, footL, elbowR, elbowL, handR, handL;
            Vector3 shR = new Vector3(shoulderX, shoulderY, 0), shL = new Vector3(-shoulderX, shoulderY, 0);
            switch (pose)
            {
                case RagdollPose.Standing:
                    kneeR = new Vector3(hipX, -thigh, 0); footR = new Vector3(hipX, -thigh - shank, 0);
                    elbowR = new Vector3(shoulderX + 0.01f * H, shoulderY - 0.186f * H, 0); handR = new Vector3(shoulderX + 0.015f * H, shoulderY - 0.42f * H, 0);
                    break;
                case RagdollPose.Straddling: // Motorrad: Knie nach außen, Hände am Lenker
                    kneeR = new Vector3(hipX + 0.06f * H, -0.03f * H, thigh * 0.95f); footR = new Vector3(hipX + 0.07f * H, -0.03f * H - shank * 0.9f, thigh * 0.95f - 0.05f * H);
                    elbowR = new Vector3(shoulderX + 0.02f * H, shoulderY - 0.12f * H, 0.12f * H); handR = new Vector3(shoulderX + 0.06f * H, shoulderY - 0.13f * H, 0.33f * H);
                    break;
                default: // Sitzend: Oberschenkel waagerecht, Unterschenkel senkrecht, Hände Richtung Lenkrad
                    kneeR = new Vector3(hipX, 0, thigh); footR = new Vector3(hipX, -shank, thigh + 0.02f * H);
                    elbowR = new Vector3(shoulderX + 0.01f * H, shoulderY - 0.15f * H, 0.07f * H); handR = new Vector3(shoulderX - 0.01f * H, shoulderY - 0.17f * H, 0.22f * H);
                    break;
            }
            kneeL = Mirror(kneeR); footL = Mirror(footR); elbowL = Mirror(elbowR); handL = Mirror(handR);
            rd.HandTipRight = P(handR); rd.HandTipLeft = P(handL);

            // ---- Rumpf (aufrecht, Boxen) und Kopf
            float pelvisTop = 0.07f * H, abdomenTop = 0.17f * H;
            var becken  = Box(rd, "Becken", "Becken", P(new Vector3(0, (pelvisTop - 0.02f * H) / 2, 0)), T.rotation, new Vector3(0.19f * H, pelvisTop + 0.02f * H, 0.11f * H), m[3] * mass_kg);
            var abdomen = Box(rd, "Abdomen", "Abdomen", P(new Vector3(0, (pelvisTop + abdomenTop) / 2, 0)), T.rotation, new Vector3(0.16f * H, abdomenTop - pelvisTop, 0.10f * H), m[2] * mass_kg);
            var thorax  = Box(rd, "Thorax", "Thorax", P(new Vector3(0, (abdomenTop + shoulderY + 0.015f * H) / 2, 0)), T.rotation, new Vector3(0.18f * H, shoulderY + 0.015f * H - abdomenTop, 0.11f * H), m[1] * mass_kg);
            var kopf    = Sphere(rd, "Kopf", "Kopf", P(new Vector3(0, shoulderY + 0.09f * H, 0)), 0.062f * H * headScale, m[0] * mass_kg);

            // ---- Gliedmaßen (Kapseln)
            var oaR = Capsule(rd, "Oberarm rechts", "Arm rechts", P(shR), P(elbowR), 0.026f * H, m[4] * mass_kg);
            var oaL = Capsule(rd, "Oberarm links", "Arm links", P(shL), P(elbowL), 0.026f * H, m[4] * mass_kg);
            var uaR = Capsule(rd, "Unterarm rechts", "Arm rechts", P(elbowR), P(handR), 0.021f * H, m[5] * mass_kg);
            var uaL = Capsule(rd, "Unterarm links", "Arm links", P(elbowL), P(handL), 0.021f * H, m[5] * mass_kg);
            var osR = Capsule(rd, "Oberschenkel rechts", "Oberschenkel rechts", P(hipR), P(kneeR), 0.042f * H, m[6] * mass_kg);
            var osL = Capsule(rd, "Oberschenkel links", "Oberschenkel links", P(hipL), P(kneeL), 0.042f * H, m[6] * mass_kg);
            var usR = Capsule(rd, "Unterschenkel rechts", "Unterschenkel rechts", P(kneeR), P(footR), 0.03f * H, m[7] * mass_kg);
            var usL = Capsule(rd, "Unterschenkel links", "Unterschenkel links", P(kneeL), P(footL), 0.03f * H, m[7] * mass_kg);

            // ---- Gelenke (Winkel in Grad; X = Beugung um die Querachse des Körpers)
            Joint(abdomen, becken, P(new Vector3(0, pelvisTop, 0)), T, 25, 25, 20, 20, 300);
            Joint(thorax, abdomen, P(new Vector3(0, abdomenTop, 0)), T, 25, 25, 20, 20, 300);
            rd.NeckJoint = Joint(kopf, thorax, P(new Vector3(0, shoulderY + 0.02f * H, 0)), T, 45, 45, 40, 30, 60);
            Joint(oaR, thorax, P(shR), T, 100, 100, 80, 80, 20);
            Joint(oaL, thorax, P(shL), T, 100, 100, 80, 80, 20);
            Joint(uaR, oaR, P(elbowR), T, 130, 130, 10, 10, 10);
            Joint(uaL, oaL, P(elbowL), T, 130, 130, 10, 10, 10);
            rd.HipJointRight = Joint(osR, becken, P(hipR), T, 100, 100, 30, 40, 40);
            Joint(osL, becken, P(hipL), T, 100, 100, 30, 40, 40);
            float kneeLow = invertKneeFlexion ? 140 : 5, kneeHigh = invertKneeFlexion ? 5 : 140;
            Joint(usR, osR, P(kneeR), T, kneeLow, kneeHigh, 5, 5, 30);
            Joint(usL, osL, P(kneeL), T, kneeLow, kneeHigh, 5, 5, 30);
            AddSternum(rd, thorax, T, H, (shoulderY + 0.015f * H - abdomenTop));
            return rd;
        }

        // Brustkorb als Feder-Dämpfer zwischen Sternum und Wirbelsäule, angelehnt an das Thoraxmodell von Lobdell (1973).
        // Steifigkeit und Dämpfung sind Größenordnungen, keine validierten Werte (VERIFY gegen Lobdell 1973 / Kroell 1974).
        public const float ChestStiffness_N_per_m = 26000f, ChestDamping_Ns_per_m = 520f, SternumMass_kg = 0.45f;
        public const float MaxChestDeflection_m = 0.09f;

        static void AddSternum(Ragdoll rd, Rigidbody thorax, Transform body, float H, float thoraxHeight)
        {
            float depth = 0.11f * H;
            Vector3 pos = thorax.position + body.forward * (depth / 2 + 0.012f) + body.up * 0.05f * thoraxHeight;
            AddSternumAt(rd, thorax, pos, body, 0.12f * H, 0.7f * thoraxHeight);
        }

        static void AddSternumAt(Ragdoll rd, Rigidbody thorax, Vector3 pos, Transform body, float width, float height)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "Sternum";
            go.transform.SetPositionAndRotation(pos, body.rotation);
            go.transform.localScale = new Vector3(width, height, 0.02f);
            var rb = Finish(rd, go, "Sternum", "Thorax", SternumMass_kg);
            if (AnatomyBuilder.Available) go.GetComponent<Renderer>().enabled = false; // unter der Haut
            rd.Sternum = rb;
            rd.SternumRestOffset = Vector3.Dot(pos - thorax.position, body.forward);

            var j = go.AddComponent<ConfigurableJoint>();
            j.connectedBody = thorax;
            j.autoConfigureConnectedAnchor = true;
            j.anchor = Vector3.zero;
            j.axis = go.transform.InverseTransformDirection(body.right);
            j.secondaryAxis = go.transform.InverseTransformDirection(body.up); // Gelenk-z = vorn/hinten
            j.xMotion = j.yMotion = ConfigurableJointMotion.Locked;
            j.zMotion = ConfigurableJointMotion.Limited;
            j.linearLimit = new SoftJointLimit { limit = MaxChestDeflection_m }; // harter Anschlag: Rippen bis zur Wirbelsäule
            j.zDrive = new JointDrive { positionSpring = ChestStiffness_N_per_m, positionDamper = ChestDamping_Ns_per_m, maximumForce = float.MaxValue };
            j.angularXMotion = j.angularYMotion = j.angularZMotion = ConfigurableJointMotion.Locked;
            j.enablePreprocessing = false;
        }

        static Vector3 Mirror(Vector3 v) => new Vector3(-v.x, v.y, v.z);

        static Rigidbody Box(Ragdoll rd, string name, string region, Vector3 center, Quaternion rot, Vector3 size, float mass)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.transform.SetPositionAndRotation(center, rot);
            go.transform.localScale = size;
            return Finish(rd, go, name, region, mass);
        }

        static Rigidbody Sphere(Ragdoll rd, string name, string region, Vector3 center, float radius, float mass)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.transform.position = center;
            go.transform.localScale = Vector3.one * radius * 2;
            return Finish(rd, go, name, region, mass);
        }

        static Rigidbody Capsule(Ragdoll rd, string name, string region, Vector3 a, Vector3 b, float radius, float mass)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            Vector3 dir = b - a;
            go.transform.SetPositionAndRotation((a + b) / 2, Quaternion.FromToRotation(Vector3.up, dir.normalized));
            go.transform.localScale = new Vector3(radius * 2, dir.magnitude / 2, radius * 2);
            return Finish(rd, go, name, region, mass);
        }

        static Rigidbody Finish(Ragdoll rd, GameObject go, string name, string region, float mass, bool keepRenderer = false)
        {
            go.name = name;
            go.transform.SetParent(rd.Root.transform, true);
            var rb = go.AddComponent<Rigidbody>();
            rb.mass = mass;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            rb.solverIterations = 30;
            rb.solverVelocityIterations = 15;
            rb.maxAngularVelocity = 60;
            var sensor = go.AddComponent<CrashSensor>();
            sensor.Configure(region, rd.Root.transform);
            rd.Segments[name] = rb;
            rd.Sensors.Add(sensor);
            var r = keepRenderer ? null : go.GetComponent<Renderer>();
            if (r) r.material = CrashPhysics.Mat(new Color(0.62f, 0.69f, 0.78f));
            return rb;
        }

        /// <summary>ConfigurableJoint mit Grenzen in Grad und leichtem „Muskeltonus“ (hält die Ausgangshaltung).</summary>
        static ConfigurableJoint Joint(Rigidbody child, Rigidbody parent, Vector3 worldAnchor, Transform body,
                          float lowX, float highX, float swingY, float swingZ, float tone)
        {
            var j = child.gameObject.AddComponent<ConfigurableJoint>();
            j.connectedBody = parent;
            j.autoConfigureConnectedAnchor = true;
            j.anchor = child.transform.InverseTransformPoint(worldAnchor);
            j.axis = child.transform.InverseTransformDirection(body.right);
            j.secondaryAxis = child.transform.InverseTransformDirection(body.forward);
            j.xMotion = j.yMotion = j.zMotion = ConfigurableJointMotion.Locked;
            j.angularXMotion = j.angularYMotion = j.angularZMotion = ConfigurableJointMotion.Limited;
            j.lowAngularXLimit = new SoftJointLimit { limit = -lowX };
            j.highAngularXLimit = new SoftJointLimit { limit = highX };
            j.angularYLimit = new SoftJointLimit { limit = swingY };
            j.angularZLimit = new SoftJointLimit { limit = swingZ };
            j.rotationDriveMode = RotationDriveMode.Slerp;
            j.slerpDrive = new JointDrive { positionSpring = tone, positionDamper = tone * 0.1f, maximumForce = float.MaxValue };
            j.projectionMode = JointProjectionMode.PositionAndRotation;
            j.projectionDistance = 0.02f;
            j.projectionAngle = 5;
            j.enablePreprocessing = false;
            return j;
        }
    }
}
