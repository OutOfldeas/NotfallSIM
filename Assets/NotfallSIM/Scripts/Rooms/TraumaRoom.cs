using UnityEngine;
using UnityEngine.Rendering;

namespace NotfallSIM.Rooms
{
    /// <summary>
    /// Baut den Schockraum aus Grundkörpern mit PBR-Materialien: Raum, Trage, OP-Leuchte, Deckenversorgungseinheit
    /// mit Monitor, Beatmungsgerät, Infusionsständer, Notfallwagen, Sonografie, Schränke und Beleuchtung.
    /// Trage: Mitte im Ursprung, Kopfende Richtung +z, Liegefläche auf <see cref="MattressTop"/>.
    /// </summary>
    public static partial class TraumaRoom
    {
        public const float MattressTop = 0.84f;
        /// <summary>Gehäuse des Patientenmonitors (für die World-Space-Anzeige); Bildschirm auf der lokalen −z-Seite.</summary>
        public static Transform MonitorBody { get; private set; }
        const float RoomX = 3.2f, RoomZ = 3.8f, RoomH = 3.0f;

        public static Transform BuildGeometry()
        {
            var root = new GameObject("Schockraum").transform;

            // ---- Materialien
            var floorMat = Pbr("Boden", new Color(0.58f, 0.60f, 0.60f), 0, 0.45f, FloorTexture());
            floorMat.mainTextureScale = new Vector2(RoomX * 2 / 0.6f, RoomZ * 2 / 0.6f);
            var wall = Pbr("Wand", new Color(0.80f, 0.85f, 0.83f), 0, 0.25f);
            var wallLow = Pbr("Wandschutz", new Color(0.55f, 0.66f, 0.66f), 0, 0.35f);
            var ceiling = Pbr("Decke", new Color(0.92f, 0.93f, 0.93f), 0, 0.1f);
            var steel = Pbr("Edelstahl", new Color(0.78f, 0.79f, 0.80f), 0.9f, 0.62f);
            var dark = Pbr("Kunststoff dunkel", new Color(0.12f, 0.13f, 0.14f), 0, 0.45f);
            var white = Pbr("Kunststoff weiß", new Color(0.90f, 0.91f, 0.90f), 0, 0.55f);
            var mattress = Pbr("Matratze", new Color(0.13f, 0.20f, 0.30f), 0, 0.35f);
            var red = Pbr("Notfallwagen", new Color(0.70f, 0.08f, 0.08f), 0.1f, 0.5f);
            var screen = Pbr("Bildschirm", new Color(0.02f, 0.03f, 0.03f), 0, 0.85f);
            var lampGlow = Pbr("Leuchtfläche", Color.white, 0, 0.5f, null, new Color(1.6f, 1.55f, 1.45f));
            var panelGlow = Pbr("Deckenleuchte", Color.white, 0, 0.5f, null, new Color(1.2f, 1.25f, 1.3f));
            var o2 = Pbr("Anschluss O2", new Color(0.95f, 0.95f, 0.95f), 0, 0.5f);
            var o2ring = Pbr("Ring O2", new Color(0.1f, 0.55f, 0.25f), 0, 0.5f);
            var airRing = Pbr("Ring Luft", new Color(0.1f, 0.1f, 0.1f), 0, 0.5f);
            var bag = Pbr("Infusionsbeutel", new Color(0.85f, 0.92f, 0.95f), 0, 0.9f);
            var door = Pbr("Tür", new Color(0.66f, 0.70f, 0.72f), 0.2f, 0.4f);

            // ---- Raum
            Box(root, "Boden", new Vector3(0, -0.05f, 0), new Vector3(RoomX * 2, 0.1f, RoomZ * 2), floorMat);
            Box(root, "Decke", new Vector3(0, RoomH + 0.05f, 0), new Vector3(RoomX * 2, 0.1f, RoomZ * 2), ceiling);
            Wall(root, new Vector3(0, 0, RoomZ), new Vector3(RoomX * 2, 0, 0.1f), wall, wallLow);
            Wall(root, new Vector3(0, 0, -RoomZ), new Vector3(RoomX * 2, 0, 0.1f), wall, wallLow);
            Wall(root, new Vector3(RoomX, 0, 0), new Vector3(0.1f, 0, RoomZ * 2), wall, wallLow);
            Wall(root, new Vector3(-RoomX, 0, 0), new Vector3(0.1f, 0, RoomZ * 2), wall, wallLow);
            Box(root, "Schiebetür links", new Vector3(-0.62f, 1.1f, -RoomZ + 0.06f), new Vector3(1.2f, 2.2f, 0.04f), door);
            Box(root, "Schiebetür rechts", new Vector3(0.62f, 1.1f, -RoomZ + 0.06f), new Vector3(1.2f, 2.2f, 0.04f), door);
            Box(root, "Türfenster", new Vector3(-0.62f, 1.55f, -RoomZ + 0.085f), new Vector3(0.5f, 0.6f, 0.01f), screen);

            // ---- Trage
            var t = new GameObject("Trage").transform; t.SetParent(root, false);
            Box(t, "Liegefläche", new Vector3(0, MattressTop - 0.05f, 0), new Vector3(0.68f, 0.10f, 2.05f), mattress);
            Box(t, "Rahmen", new Vector3(0, MattressTop - 0.14f, 0), new Vector3(0.72f, 0.08f, 2.0f), steel);
            Box(t, "Seitengitter rechts", new Vector3(-0.38f, MattressTop + 0.06f, 0.05f), new Vector3(0.025f, 0.04f, 1.2f), steel);
            Box(t, "Seitengitter links", new Vector3(0.38f, MattressTop + 0.06f, 0.05f), new Vector3(0.025f, 0.04f, 1.2f), steel);
            Box(t, "Säule", new Vector3(0, 0.38f, 0), new Vector3(0.28f, 0.55f, 0.4f), dark);
            Box(t, "Fahrgestell", new Vector3(0, 0.12f, 0), new Vector3(0.6f, 0.08f, 1.7f), dark);
            foreach (var p in new[] { new Vector3(-0.26f, 0.06f, 0.78f), new Vector3(0.26f, 0.06f, 0.78f), new Vector3(-0.26f, 0.06f, -0.78f), new Vector3(0.26f, 0.06f, -0.78f) })
                Cyl(t, "Rolle", p, new Vector3(0.1f, 0.02f, 0.1f), dark, new Vector3(0, 0, 90));

            // ---- OP-Leuchte über der Trage
            var lamp = new GameObject("OP-Leuchte").transform; lamp.SetParent(root, false);
            Cyl(lamp, "Deckenhalter", new Vector3(0.6f, RoomH - 0.1f, 0.2f), new Vector3(0.2f, 0.1f, 0.2f), white);
            Box(lamp, "Arm", new Vector3(0.45f, RoomH - 0.35f, 0.2f), new Vector3(0.06f, 0.5f, 0.06f), white);
            Box(lamp, "Arm quer", new Vector3(0.25f, RoomH - 0.6f, 0.2f), new Vector3(0.45f, 0.06f, 0.06f), white);
            Cyl(lamp, "Leuchtenkopf", new Vector3(0.05f, RoomH - 0.72f, 0.2f), new Vector3(0.7f, 0.06f, 0.7f), white);
            Cyl(lamp, "Leuchtfläche", new Vector3(0.05f, RoomH - 0.765f, 0.2f), new Vector3(0.6f, 0.01f, 0.6f), lampGlow);
            var spot = new GameObject("Licht OP-Leuchte").AddComponent<Light>();
            spot.transform.SetParent(lamp, false);
            spot.transform.position = new Vector3(0.05f, RoomH - 0.8f, 0.2f);
            spot.transform.rotation = Quaternion.LookRotation(Vector3.down, Vector3.forward);
            spot.type = LightType.Spot; spot.spotAngle = 70; spot.innerSpotAngle = 35; spot.range = 4.5f;
            spot.intensity = 6f; spot.color = new Color(1f, 0.97f, 0.92f);
            spot.shadows = LightShadows.Soft; spot.shadowStrength = 0.6f;

            // ---- Deckenleuchten
            for (int ix = -1; ix <= 1; ix += 2)
                for (int iz = -1; iz <= 1; iz += 2)
                {
                    var pos = new Vector3(ix * 1.5f, RoomH - 0.01f, iz * 1.7f);
                    Box(root, "Deckenleuchte", pos, new Vector3(0.6f, 0.02f, 1.2f), panelGlow);
                    var l = new GameObject("Licht Decke").AddComponent<Light>();
                    l.transform.SetParent(root, false);
                    l.transform.position = pos + Vector3.down * 0.15f;
                    l.type = LightType.Point; l.range = 6f; l.intensity = 0.9f; l.color = new Color(0.95f, 0.98f, 1f);
                    l.shadows = LightShadows.None;
                }

            // ---- Deckenversorgungseinheit mit Monitor (Kopfende rechts vom Patienten = −x)
            var pendant = new GameObject("Versorgungseinheit").transform; pendant.SetParent(root, false);
            Box(pendant, "Säule", new Vector3(-0.95f, 1.75f, 1.15f), new Vector3(0.32f, 1.1f, 0.32f), white);
            Box(pendant, "Deckenarm", new Vector3(-0.95f, RoomH - 0.2f, 1.15f), new Vector3(0.12f, 0.4f, 0.12f), white);
            for (int i = 0; i < 3; i++)
            {
                var y = 1.45f + i * 0.12f;
                Cyl(pendant, "Gasanschluss", new Vector3(-0.78f, y, 1.15f), new Vector3(0.06f, 0.02f, 0.06f), o2, new Vector3(0, 0, 90));
                Cyl(pendant, "Gasring", new Vector3(-0.775f, y, 1.15f), new Vector3(0.075f, 0.005f, 0.075f), i == 0 ? o2ring : airRing, new Vector3(0, 0, 90));
            }
            Box(pendant, "Monitorarm", new Vector3(-0.8f, 2.05f, 1.0f), new Vector3(0.3f, 0.05f, 0.05f), steel);
            var monitorBody = Box(pendant, "Monitor", new Vector3(-0.62f, 2.0f, 0.95f), new Vector3(0.56f, 0.40f, 0.06f), dark);
            // Bildschirm (lokal −z) zeigt Richtung Fußende/Trage und ist leicht nach unten geneigt
            monitorBody.transform.rotation = Quaternion.Euler(-4, -15, 0);
            MonitorBody = monitorBody.transform;
            var monitorScreen = Box(monitorBody.transform, "Bildschirm", new Vector3(0, 0, -0.55f), new Vector3(0.92f, 0.88f, 0.1f), screen);
            monitorScreen.name = "Monitor-Bildschirm";

            // ---- Beatmungsgerät (Kopfende links = +x)
            var vent = new GameObject("Beatmungsgerät").transform; vent.SetParent(root, false);
            Box(vent, "Wagen", new Vector3(1.0f, 0.55f, 1.35f), new Vector3(0.45f, 0.9f, 0.45f), white);
            Box(vent, "Display", new Vector3(1.0f, 1.25f, 1.25f), new Vector3(0.38f, 0.3f, 0.04f), screen).transform.rotation = Quaternion.Euler(-12, 200, 0);
            Box(vent, "Schlauchhalter", new Vector3(0.8f, 1.15f, 1.3f), new Vector3(0.03f, 0.5f, 0.03f), steel);

            // ---- Infusionsständer
            var iv = new GameObject("Infusionsständer").transform; iv.SetParent(root, false);
            Cyl(iv, "Fuß", new Vector3(-0.6f, 0.03f, 0.5f), new Vector3(0.45f, 0.03f, 0.45f), dark);
            Cyl(iv, "Stange", new Vector3(-0.6f, 1.0f, 0.5f), new Vector3(0.025f, 1.0f, 0.025f), steel);
            Box(iv, "Haken", new Vector3(-0.6f, 1.98f, 0.5f), new Vector3(0.3f, 0.015f, 0.015f), steel);
            Box(iv, "Beutel", new Vector3(-0.72f, 1.8f, 0.5f), new Vector3(0.12f, 0.2f, 0.03f), bag);

            // ---- Notfallwagen, Sonografie, Schränke
            var cart = Box(root, "Notfallwagen", new Vector3(2.4f, 0.5f, -1.6f), new Vector3(0.65f, 1.0f, 0.5f), red);
            for (int i = 0; i < 5; i++) Box(cart.transform, "Schublade", new Vector3(0, -0.35f + i * 0.18f, -0.51f), new Vector3(0.9f, 0.01f, 0.02f), dark);
            var us = new GameObject("Sonografie").transform; us.SetParent(root, false);
            Box(us, "Wagen", new Vector3(0.95f, 0.45f, -0.5f), new Vector3(0.45f, 0.8f, 0.5f), white);
            Box(us, "Bildschirm", new Vector3(0.95f, 1.15f, -0.45f), new Vector3(0.45f, 0.32f, 0.03f), screen).transform.rotation = Quaternion.Euler(-10, 250, 0);
            for (int i = 0; i < 4; i++)
            {
                var c = Box(root, "Schrank", new Vector3(-RoomX + 0.3f, 1.0f, -2.4f + i * 0.85f), new Vector3(0.55f, 2.0f, 0.82f), white);
                Box(c.transform, "Griff", new Vector3(0.52f, 0.0f, 0.3f), new Vector3(0.05f, 0.08f, 0.02f), steel);
            }
            Box(root, "Arbeitsfläche", new Vector3(RoomX - 0.3f, 0.9f, 1.2f), new Vector3(0.6f, 0.05f, 1.8f), steel);
            Box(root, "Unterschrank", new Vector3(RoomX - 0.3f, 0.44f, 1.2f), new Vector3(0.58f, 0.86f, 1.78f), white);

            // ---- Umgebungslicht
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.55f, 0.58f, 0.60f);
            RenderSettings.ambientEquatorColor = new Color(0.40f, 0.42f, 0.42f);
            RenderSettings.ambientGroundColor = new Color(0.22f, 0.22f, 0.22f);
            return root;
        }

        // ------------------------------------------------------------ Bausteine

        static void Wall(Transform root, Vector3 at, Vector3 size, Material upper, Material lower)
        {
            Box(root, "Wand", at + Vector3.up * RoomH / 2, new Vector3(Mathf.Max(size.x, 0.1f), RoomH, Mathf.Max(size.z, 0.1f)), upper);
            Vector3 inward = -new Vector3(Mathf.Sign(at.x) * (size.x < 0.5f ? 1 : 0), 0, Mathf.Sign(at.z) * (size.z < 0.5f ? 1 : 0)) * 0.03f;
            Box(root, "Wandschutz", at + inward + Vector3.up * 0.55f, new Vector3(Mathf.Max(size.x, 0.12f), 0.2f, Mathf.Max(size.z, 0.12f)), lower);
            Box(root, "Sockel", at + inward + Vector3.up * 0.05f, new Vector3(Mathf.Max(size.x, 0.12f), 0.1f, Mathf.Max(size.z, 0.12f)), lower);
        }

        public static GameObject Box(Transform parent, string name, Vector3 pos, Vector3 size, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = size;
            go.GetComponent<Renderer>().sharedMaterial = mat;
            return go;
        }

        static GameObject Cyl(Transform parent, string name, Vector3 pos, Vector3 size, Material mat, Vector3 euler = default(Vector3))
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localEulerAngles = euler;
            go.transform.localScale = size;
            go.GetComponent<Renderer>().sharedMaterial = mat;
            return go;
        }

        /// <summary>URP-Lit-Material zur Laufzeit (Metallic/Glätte, optional Textur und Leuchten).</summary>
        public static Material Pbr(string name, Color color, float metallic, float smoothness, Texture2D tex = null, Color? emission = null)
        {
            var sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var m = new Material(sh) { name = name, color = color };
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", metallic);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smoothness);
            if (tex != null) m.mainTexture = tex;
            if (emission.HasValue)
            {
                m.EnableKeyword("_EMISSION");
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                if (m.HasProperty("_EmissionColor")) m.SetColor("_EmissionColor", emission.Value);
            }
            return m;
        }

        /// <summary>Fliesen-/Vinylmuster als Textur (zur Laufzeit erzeugt).</summary>
        static Texture2D FloorTexture()
        {
            const int n = 256;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, true) { name = "Boden", wrapMode = TextureWrapMode.Repeat };
            var rnd = new System.Random(3);
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    bool grout = x < 2 || y < 2;
                    float speck = (float)rnd.NextDouble() * 0.06f;
                    float v = grout ? 0.62f : 0.9f + speck - 0.03f * Mathf.PerlinNoise(x * 0.05f, y * 0.05f);
                    tex.SetPixel(x, y, new Color(v, v, v * 0.99f));
                }
            tex.Apply();
            return tex;
        }
    }
}
