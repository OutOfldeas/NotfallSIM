using System.Collections.Generic;
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
    public static class RagdollBuilder
    {
        // Massenanteile am Körpergewicht (de Leva 1996): Kopf, Rumpf oben/mitte/unten, Oberarm, Unterarm+Hand, Oberschenkel, Unterschenkel+Fuß
        static readonly float[] MaleMass   = { 0.0694f, 0.1596f, 0.1633f, 0.1117f, 0.0271f, 0.0162f + 0.0061f, 0.1416f, 0.0433f + 0.0137f };
        static readonly float[] FemaleMass = { 0.0668f, 0.1545f, 0.1465f, 0.1247f, 0.0255f, 0.0138f + 0.0056f, 0.1478f, 0.0481f + 0.0129f };

        /// <summary>
        /// VERIFY in Unity: Vorzeichen der Kniebeugung im ConfigurableJoint. Wenn die Knie nach hinten durchknicken,
        /// im CrashRig „Invert Knee Flexion“ umschalten.
        /// </summary>
        public static Ragdoll Build(Vector3 hipCenter, Quaternion facing, RagdollPose pose, float height_m, float mass_kg, bool male,
                                    bool invertKneeFlexion = false, float headScale = 1f)
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
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "Sternum";
            go.transform.SetPositionAndRotation(pos, body.rotation);
            go.transform.localScale = new Vector3(0.12f * H, 0.7f * thoraxHeight, 0.02f);
            var rb = Finish(rd, go, "Sternum", "Thorax", SternumMass_kg);
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

        static Rigidbody Finish(Ragdoll rd, GameObject go, string name, string region, float mass)
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
            var r = go.GetComponent<Renderer>();
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
