using System;
using System.Collections.Generic;
using System.Linq;

namespace NotfallSIM.Crash
{
    // ---------- impact_model.json ----------
    [Serializable] public class AreaStiffness { public double area_cm2, k_N_m; }

    /// <summary>Kontaktparameter einer Körperregion (nachgiebiger Kontakt statt starrer Kollision).</summary>
    [Serializable]
    public class RegionContact
    {
        public string region;            // "Kopf", "Thorax", "Abdomen", "Becken", "Oberschenkel", "Unterschenkel", "Arm"
        public double k1_N_m, k2_N_m, knee1_m, knee2_m;
        public AreaStiffness[] areaStiffness;
        public double restitution, effectiveMass_kg, depth_m, maxArea_cm2;
        public string evidence, sourceId, note;
    }

    [Serializable] public class SkullPoint { public double area_cm2, force50_N; }
    [Serializable] public class SkullFracture { public SkullPoint[] points; public double cv; public string injuryId, sourceId, note; }
    [Serializable] public class LongBone { public string segment; public double moment50_Nm, cv; public string injuryRight, injuryLeft, sourceId, note; }
    [Serializable] public class RibFracture { public double aisIntercept, aisSlope; public string injuryRight, injuryLeft, sourceId, note; }
    [Serializable] public class FractureModel { public SkullFracture skull; public LongBone[] longBones; public RibFracture ribs; }

    [Serializable] public class SkinPenetration { public double intercept_N, perTipRadius_um, perTipAngle_deg, perCuttingAngle_deg, minimum_N, clothingFactor; public string sourceId, note; }
    [Serializable] public class TissueResistance { public string id; public double resistance_N; public string evidence, note; }
    [Serializable] public class PenetrationRegion { public string region; public double wall_m, boneHitProbability; public string deep, deepRight, deepLeft; public double probability; public string note; }
    [Serializable] public class PenetrationModel { public SkinPenetration skin; public TissueResistance[] tissues; public PenetrationRegion[] regions; }

    [Serializable] public class ImpactSource { public string id, citation, url, note; }

    [Serializable]
    public class ImpactModelFile
    {
        public string version, note;
        public RegionContact[] regions;
        public FractureModel fractures;
        public PenetrationModel penetration;
        public ImpactSource[] sources;
    }

    // ---------- objects.json ----------
    [Serializable]
    public class SandboxObject
    {
        public string id, label, kind, mode, shape;   // kind: stumpf | spitz; mode: werfen | gefuehrt | statisch
        public float[] size_m;
        public double mass_kg, contactArea_cm2, stiffness_N_m, tipRadius_um, tipAngle_deg, cuttingAngle_deg, bladeLength_m;
        public double defaultSpeed_m_s, minSpeed_m_s, maxSpeed_m_s;
        public float[] color;
        public string evidence, sourceId, note;
        public bool Sharp => kind == "spitz";
    }

    [Serializable] public class SandboxObjectFile { public string version, note; public SandboxObject[] objects; public ImpactSource[] sources; }

    /// <summary>Verletzung, die direkt aus einem Aufprall folgt (Fraktur, Stichkanal), mit Wahrscheinlichkeit und Schweregrad.</summary>
    public class DirectInjury
    {
        public string InjuryId;
        public double Probability, Severity;
        public string Reason;
        public override string ToString() => $"{InjuryId} {Probability:P0} ({Reason})";
    }

    /// <summary>Stärkster stumpfer Kontakt einer Region (aus den Messaufnehmern).</summary>
    public class BluntContact
    {
        public string Region;          // Region wie in der Anatomie ("Kopf", "Oberschenkel rechts" …)
        public string Segment;         // Segmentname ("Oberschenkel rechts", "Unterarm links" …)
        public double PeakForce_N, Area_cm2, MaxIndentation_m, SegmentLength_m;
        public int Side;               // −1 links, +1 rechts, 0 Mitte
        public string Surface;         // Fläche ("ground", "object:hammer" …), nur zur Anzeige
    }

    /// <summary>Ergebnis eines eingedrungenen spitzen Gegenstands.</summary>
    public class PenetrationResult
    {
        public string Region, Segment;
        public int Side;
        public double Depth_m;
        public bool HitBone;
        public string ObjectId;
        public bool Embedded;          // steckt noch (Pfählung, Messer nicht zurückgezogen)
        public float[] ModelPoint, ModelInside;   // Einstich im Modellraum (für die Wundmarke), sonst null
    }

    /// <summary>
    /// Kontaktgesetz und Verletzungsschwellen für stumpfe und spitze Gewalt. Reines C#, testbar.
    /// </summary>
    public static class ImpactModel
    {
        /// <summary>Region des Kontaktmodells zu einer Körperregion bzw. einem Segment.</summary>
        public static string ContactRegion(string region)
        {
            if (string.IsNullOrEmpty(region)) return "Thorax";
            if (region.StartsWith("Oberschenkel")) return "Oberschenkel";
            if (region.StartsWith("Unterschenkel")) return "Unterschenkel";
            if (region.StartsWith("Arm") || region.StartsWith("Oberarm") || region.StartsWith("Unterarm")) return "Arm";
            return region;
        }

        public static RegionContact Region(ImpactModelFile m, string region)
        {
            string key = ContactRegion(region);
            return m.regions.FirstOrDefault(r => r.region == key) ?? m.regions.First(r => r.region == "Thorax");
        }

        static double LogInterp(double x, double x0, double y0, double x1, double y1)
        {
            x = Math.Max(x0, Math.Min(x1, x));
            double t = Math.Log(x / x0) / Math.Log(x1 / x0);
            return y0 * Math.Pow(y1 / y0, t);
        }

        /// <summary>Steifigkeit der Region bei Eindrücktiefe δ (stückweise linear) und Kontaktfläche (nur Kopf).</summary>
        public static double Stiffness(RegionContact r, double delta_m, double area_cm2)
        {
            double k1 = r.k1_N_m, k2 = r.k2_N_m;
            if (r.areaStiffness != null && r.areaStiffness.Length >= 2)
            {
                // ohne LINQ: läuft pro Kontaktpunkt auf den Physik-Threads (Stützstellen aufsteigend nach Fläche, im Test geprüft)
                var a = r.areaStiffness;
                double area = Math.Min(area_cm2 > 0 ? area_cm2 : r.maxArea_cm2, r.maxArea_cm2);
                double k = LogInterp(area, a[0].area_cm2, a[0].k_N_m, a[a.Length - 1].area_cm2, a[a.Length - 1].k_N_m);
                k2 *= k / k1; k1 = k;
            }
            if (delta_m <= r.knee1_m) return k1;
            if (delta_m >= r.knee2_m) return k2;
            return k1 + (k2 - k1) * (delta_m - r.knee1_m) / (r.knee2_m - r.knee1_m);
        }

        /// <summary>Dämpfungsgrad aus der Restitution (Feder-Dämpfer-Stoß): ζ = −ln e / √(π² + ln² e).</summary>
        public static double DampingRatio(double restitution)
        {
            double e = Math.Max(0.01, Math.Min(0.99, restitution)), l = Math.Log(e);
            return -l / Math.Sqrt(Math.PI * Math.PI + l * l);
        }

        /// <summary>
        /// Kontaktkraft (nur Druck): F = k·δ + c·δ̇, k = Körper in Reihe mit dem Gegenstand (0 = starr), c = 2ζ√(k·m).
        /// m = reduzierte Masse aus Region und Gegenstand (0 = unendlich schwer, z. B. Boden).
        /// </summary>
        public static double Force(RegionContact r, double delta_m, double closing_m_s, double area_cm2, double objectStiffness_N_m, double objectMass_kg)
        {
            if (delta_m <= 0 && closing_m_s <= 0) return 0;
            double kb = Stiffness(r, Math.Max(0, delta_m), area_cm2);
            double k = objectStiffness_N_m > 0 ? 1 / (1 / kb + 1 / objectStiffness_N_m) : kb;
            double mb = r.effectiveMass_kg, m = objectMass_kg > 0 ? mb * objectMass_kg / (mb + objectMass_kg) : mb;
            double c = 2 * DampingRatio(r.restitution) * Math.Sqrt(k * m);
            return Math.Max(0, k * Math.Max(0, delta_m) + c * closing_m_s);
        }

        /// <summary>
        /// Stoß eines Gegenstands (Masse m, Geschwindigkeit v) auf die Region, eindimensional, Körperseite mit der wirksamen
        /// Masse der Region (frei beweglich). Liefert Spitzenkraft, größte Eindrückung und Kontaktdauer. Für Tests und Doku.
        /// </summary>
        public static (double peakForce_N, double maxDelta_m, double duration_s) Simulate(RegionContact r, double objectMass_kg, double v0, double area_cm2, double objectStiffness_N_m = 0, double dt = 1e-6)
        {
            double mo = objectMass_kg, mb = r.effectiveMass_kg;
            double xo = 0, vo = v0, xb = 0, vb = 0, peak = 0, maxD = 0, t = 0;
            bool started = false;
            while (t < 0.5)
            {
                double d = xo - xb, closing = vo - vb;
                double f = d > 0 || closing > 0 ? Force(r, d, closing, area_cm2, objectStiffness_N_m, mo) : 0;
                if (d <= 0 && started && closing <= 0) break;
                if (d > 0) started = true;
                peak = Math.Max(peak, f); maxD = Math.Max(maxD, d);
                vo -= f / mo * dt; vb += f / mb * dt;
                xo += vo * dt; xb += vb * dt;
                t += dt;
            }
            return (peak, maxD, t);
        }

        // ---------- Frakturen

        static double Phi(double z) => Biomechanics.NormalCdf(z);

        /// <summary>Schädelfraktur: Bruchkraft wächst mit der Kontaktfläche (Allsop 1991), normalverteilt mit CV.</summary>
        public static double SkullFractureProbability(ImpactModelFile m, double force_N, double area_cm2)
        {
            var s = m.fractures.skull;
            var p = s.points.OrderBy(x => x.area_cm2).ToArray();
            double f50 = LogInterp(Math.Max(1, area_cm2), p[0].area_cm2, p[0].force50_N, p[p.Length - 1].area_cm2, p[p.Length - 1].force50_N);
            return force_N <= 0 ? 0 : Phi((force_N - f50) / (s.cv * f50));
        }

        /// <summary>Biegebruch eines Röhrenknochens bei Querkraft F mittig: M = F·L/4 gegen das 50-%-Moment (Kerrigan 2004).</summary>
        public static double LongBoneProbability(LongBone b, double force_N, double length_m)
        {
            double moment = force_N * length_m / 4;
            return force_N <= 0 ? 0 : Phi((moment - b.moment50_Nm) / (b.cv * b.moment50_Nm));
        }

        /// <summary>AIS des Thorax aus der Kompression (Kroell).</summary>
        public static double RibAis(ImpactModelFile m, double compression) => m.fractures.ribs.aisIntercept + m.fractures.ribs.aisSlope * compression;

        // ---------- Spitze Gewalt

        /// <summary>Kraft zum Durchstechen der Haut aus der Klingengeometrie (Ní Annaidh 2015), mit Kleidung höher.</summary>
        public static double SkinPenetrationForce(ImpactModelFile m, SandboxObject o, bool clothed)
        {
            var s = m.penetration.skin;
            double f = s.intercept_N + s.perTipRadius_um * o.tipRadius_um + s.perTipAngle_deg * o.tipAngle_deg + s.perCuttingAngle_deg * o.cuttingAngle_deg;
            f = Math.Max(s.minimum_N, f);
            return clothed ? f * s.clothingFactor : f;
        }

        public static double TissueResistance(ImpactModelFile m, string tissue) =>
            m.penetration.tissues.FirstOrDefault(t => t.id == tissue)?.resistance_N ?? 25;

        public static PenetrationRegion PenetrationRule(ImpactModelFile m, string region)
        {
            string key = ContactRegion(region);
            return m.penetration.regions.FirstOrDefault(r => r.region == key);
        }

        /// <summary>
        /// Widerstand gegen die Klinge in Tiefe d (nach dem Hautdurchstich): Weichteil bis zur Wand, dort ggf. Knochen
        /// (wenn <paramref name="boneHit"/>), dahinter Organ.
        /// </summary>
        public static double ResistanceAt(ImpactModelFile m, string region, double depth_m, bool boneHit)
        {
            var rule = PenetrationRule(m, region);
            double wall = rule != null ? rule.wall_m : 0.03;
            if (depth_m < wall - 0.005) return TissueResistance(m, "weichteil");
            if (depth_m < wall && boneHit) return TissueResistance(m, "knochen");
            return depth_m < wall ? TissueResistance(m, "weichteil") : TissueResistance(m, "organ");
        }

        // ---------- Verletzungen aus den Aufprallen

        /// <summary>
        /// Direkte Verletzungen: Schädelfraktur (Kraft und Fläche), Röhrenknochen (Biegemoment), Rippen (Kompression nach Kroell),
        /// Stichkanäle (Tiefe hinter der Körperwand). <paramref name="draw"/> liefert Zufallszahlen 0..1 (reproduzierbar).
        /// </summary>
        public static List<DirectInjury> Evaluate(ImpactModelFile m, IEnumerable<BluntContact> blunt, IEnumerable<PenetrationResult> penetrations, Func<double> draw)
        {
            var result = new List<DirectInjury>();
            Action<string, double, double, string> add = (id, p, sev, why) =>
            {
                if (string.IsNullOrEmpty(id) || p <= 0.005) return;
                var old = result.FirstOrDefault(x => x.InjuryId == id);
                if (old == null) result.Add(new DirectInjury { InjuryId = id, Probability = Math.Min(1, p), Severity = sev, Reason = why });
                else { old.Probability = 1 - (1 - old.Probability) * (1 - Math.Min(1, p)); old.Severity = Math.Max(old.Severity, sev); old.Reason += "; " + why; }
            };
            foreach (var b in blunt ?? new BluntContact[0])
            {
                string key = ContactRegion(b.Region);
                if (key == "Kopf")
                {
                    double p = SkullFractureProbability(m, b.PeakForce_N, b.Area_cm2);
                    add(m.fractures.skull.injuryId, p, 0.4 + 0.5 * p, $"Kopf {b.PeakForce_N / 1000:0.0} kN auf {b.Area_cm2:0} cm²");
                }
                else if (key == "Thorax")
                {
                    var reg = Region(m, "Thorax");
                    double comp = b.MaxIndentation_m / reg.depth_m, ais = RibAis(m, comp);
                    double p = Math.Max(0, Math.Min(1, (ais - 2) / 2));   // AIS 2 → 0, AIS 4 → 1 (linear, Annahme)
                    add(b.Side > 0 ? m.fractures.ribs.injuryRight : m.fractures.ribs.injuryLeft, p, 0.5 + 0.4 * p, $"Thoraxkompression {comp:P0}");
                }
                else
                {
                    var bone = m.fractures.longBones.FirstOrDefault(x => x.segment == key && b.Segment != null && b.Segment.StartsWith(key));
                    if (bone == null) continue;
                    double p = LongBoneProbability(bone, b.PeakForce_N, b.SegmentLength_m);
                    add(b.Side > 0 ? bone.injuryRight : bone.injuryLeft, p, 0.5 + 0.4 * p, $"{b.Segment} {b.PeakForce_N / 1000:0.0} kN quer");
                }
            }
            foreach (var pen in penetrations ?? new PenetrationResult[0])
            {
                var rule = PenetrationRule(m, pen.Region);
                if (rule == null || pen.HitBone || pen.Depth_m < rule.wall_m) continue;
                if (ContactRegion(pen.Region) == "Arm" && (pen.Segment == null || !pen.Segment.StartsWith("Unterarm"))) continue;
                string id = !string.IsNullOrEmpty(rule.deep) ? rule.deep : pen.Side > 0 ? rule.deepRight : rule.deepLeft;
                double p = draw() < rule.probability ? 1 : 0;
                double extra = Math.Min(1, (pen.Depth_m - rule.wall_m) / 0.08);
                add(id, p, 0.5 + 0.4 * extra, $"Stichkanal {pen.Region} {pen.Depth_m * 100:0} cm tief" + (pen.Embedded ? " (Fremdkörper steckt)" : ""));
            }
            return result;
        }
    }
}
