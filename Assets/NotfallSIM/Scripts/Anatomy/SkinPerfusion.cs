using System;
using NotfallSIM.Core;

namespace NotfallSIM.Anatomy
{
    [Serializable]
    public class SkinSource { public string id, citation, url, note; }

    /// <summary>Datenmodell skin.json.</summary>
    [Serializable]
    public class SkinParams
    {
        public string version, note;
        public float[] toneColor, paleColor, cyanoticColor, mottleColor;
        public double pallorFromLoss, pallorFullLoss; public string pallorSourceId;
        public double sweatFromLoss, sweatFullLoss; public string sweatSourceId;
        public double mottlingFromLoss, mottlingFullLoss; public string mottlingSourceId, mottlingNote;
        public double cyanosisDeoxyHb_g_dL, cyanosisFullDeoxyHb_g_dL; public string cyanosisSourceId;
        public double oxygenExtraction; public string extractionSourceId, cyanosisNote;
        public float skinF0; public float[] roughness; public string reflectanceSourceId;
        public float[] wrap; public string wrapSourceId;
        public float poreScale, poreStrength;
        public SkinSource[] sources;
    }

    /// <summary>Hautzeichen 0..1 für den Shader.</summary>
    public struct SkinState
    {
        public float Pallor, Sweat, Mottling, Cyanosis;
    }

    /// <summary>
    /// Hautfarbe aus der Physiologie (ohne Unity, getestet): Blässe, Schweiß und Marmorierung aus dem Blutverlust,
    /// Zyanose aus dem kapillären desoxygenierten Hämoglobin (hängt von Sättigung UND Hb ab).
    /// </summary>
    public static class SkinPerfusion
    {
        static float Ramp(double x, double from, double full) =>
            full > from ? (float)Math.Max(0, Math.Min(1, (x - from) / (full - from))) : (x >= from ? 1f : 0f);

        /// <summary>Kapilläres desoxygeniertes Hb (g/dL): Mittel aus arteriellem und venösem Ende.</summary>
        public static double CapillaryDeoxyHb(double hb_g_dL, double spo2_percent, double extraction)
        {
            double sa = Math.Max(0, Math.Min(1, spo2_percent / 100));
            double sv = Math.Max(0, sa - extraction);
            return hb_g_dL * (1 - (sa + sv) / 2);
        }

        public static SkinState Evaluate(SkinParams p, VitalSigns v)
        {
            double loss = v.BloodLossFraction;
            var s = new SkinState
            {
                Pallor = Ramp(loss, p.pallorFromLoss, p.pallorFullLoss),
                Sweat = Ramp(loss, p.sweatFromLoss, p.sweatFullLoss),
                Mottling = Ramp(loss, p.mottlingFromLoss, p.mottlingFullLoss),
                Cyanosis = Ramp(CapillaryDeoxyHb(v.Hb, v.SpO2_percent, p.oxygenExtraction), p.cyanosisDeoxyHb_g_dL, p.cyanosisFullDeoxyHb_g_dL)
            };
            if (v.CardiacArrest) { s.Pallor = 1; s.Mottling = Math.Max(s.Mottling, 0.7f); }
            return s;
        }
    }
}
