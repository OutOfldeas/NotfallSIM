using System;
using System.Collections.Generic;
using System.Linq;
using NotfallSIM.Anatomy;

namespace NotfallSIM.Crash
{
    // ---------- Datenmodell (organ_model.json) ----------

    [Serializable]
    public class AbdomenModel
    {
        public double stiffness_N_per_m;   // dynamische Steifigkeit des Bauchs (Leichenversuche)
        public string stiffnessSourceId;
        public double maxCompression;      // Begrenzung (der Bauch lässt sich nicht beliebig zusammendrücken)
        public double modelHeight_m;       // Körperhöhe des Anatomiemodells, an dem Tiefe/Breite gemessen sind
        public double depthFrontal_m, widthLateral_m;
        public string geometrySourceId;
        public double lateralThreshold;    // |Abdomen.lateral| darüber: seitlicher Anprall (Kompression über die Breite)
        public double loadStartFraction;   // Belastungsbeginn für die Dehnrate: Anteil der Spitzenkompression
    }

    [Serializable]
    public class OrganModelEntry
    {
        public string id, label, tissueId;
        public int side;          // −1 links, +1 rechts (Patientenseite)
        public string criterion;  // compression: Stauchung gegen Kompressions-Versagensdehnung (wahre Dehnung)
                                  // tension_incompressible: Querdehnung λ = (1 − C)^−½ gegen Zug-Versagensdehnung
        public string cvFrom;     // Messung (tissues.json), deren SD/Mittel die Streuung zwischen Spendern angibt
        public string metric;     // Messwert-ID für injury_risk.json
    }

    [Serializable]
    public class OrganValidationCase
    {
        public string description;
        public double compression, lateral;
        public string organ;
        public double minProbability, maxProbability;
        public string sourceId;
    }

    [Serializable]
    public class OrganModelSource
    {
        public string id, citation, url, note;
    }

    [Serializable]
    public class OrganModelFile
    {
        public string version, note;
        public AbdomenModel abdomen;
        public OrganModelEntry[] organs;
        public OrganValidationCase[] validation;
        public OrganModelSource[] sources;
    }

    /// <summary>Belastung eines Organs beim Anprall.</summary>
    public class OrganLoad
    {
        public OrganModelEntry Organ;
        public double Exposure;          // 0…1: liegt das Organ im Lastpfad?
        public double Strain;            // maßgebliche Dehnung (wahre Stauchung bzw. Querdehnung)
        public double StrainRate_per_s;
        public double FailureStrain;     // Versagensdehnung des Gewebes bei dieser Dehnrate
        public double Cv;                // Streuung der Versagensdehnung (Variationskoeffizient)
        public double Utilization => FailureStrain > 0 ? Strain / FailureStrain : 0;
        public double Probability;       // Anteil der Spender, deren Gewebe bei dieser Dehnung versagt hätte
    }

    public class AbdomenLoad
    {
        public bool Lateral;
        public double PeakDeflection_m, PeakCompression, StrainRate_per_s, ReferenceLength_m;
        public List<OrganLoad> Organs = new List<OrganLoad>();
    }

    /// <summary>
    /// Stufe 2 des Gewebemodells: Organdehnung beim Bauchanprall aus der gemessenen Kontaktkraft und den
    /// Materialdaten (tissues.json). Ohne Unity-Abhängigkeit, damit außerhalb testbar.
    /// </summary>
    public static class OrganMechanics
    {
        /// <summary>
        /// Wertet einen Kraftverlauf am Abdomen aus. <paramref name="force_N"/>: Kontaktkraft (gefiltert), gleichmäßig
        /// abgetastet mit <paramref name="dt_s"/>; <paramref name="lateral"/>: −1 Anprall von links … +1 von rechts.
        /// </summary>
        public static AbdomenLoad Evaluate(OrganModelFile model, TissueDatabase tissues, IList<double> force_N, double dt_s,
                                           double lateral, double height_m)
        {
            var a = model.abdomen;
            double scale = height_m > 0 && a.modelHeight_m > 0 ? height_m / a.modelHeight_m : 1;
            bool side = Math.Abs(lateral) > a.lateralThreshold;
            double length = (side ? a.widthLateral_m : a.depthFrontal_m) * scale;
            var compression = force_N.Select(f => Math.Min(a.maxCompression, Math.Max(0, f) / a.stiffness_N_per_m / length)).ToList();
            return FromCompression(model, tissues, compression, dt_s, lateral, length);
        }

        /// <summary>Wie <see cref="Evaluate"/>, aber direkt aus einem Kompressionsverlauf (0…1).</summary>
        public static AbdomenLoad FromCompression(OrganModelFile model, TissueDatabase tissues, IList<double> compression, double dt_s,
                                                  double lateral, double referenceLength_m)
        {
            var a = model.abdomen;
            var result = new AbdomenLoad { Lateral = Math.Abs(lateral) > a.lateralThreshold, ReferenceLength_m = referenceLength_m };
            if (compression.Count == 0) return result;
            int iPeak = 0;
            for (int i = 1; i < compression.Count; i++) if (compression[i] > compression[iPeak]) iPeak = i;
            double cPeak = compression[iPeak];
            result.PeakCompression = cPeak;
            result.PeakDeflection_m = cPeak * referenceLength_m;
            // Dehnrate: mittlere Rate vom Belastungsbeginn bis zur Spitze (wahre Dehnung)
            int iStart = iPeak;
            while (iStart > 0 && compression[iStart - 1] > a.loadStartFraction * cPeak) iStart--;
            double rise = Math.Max(dt_s, (iPeak - iStart) * dt_s);
            result.StrainRate_per_s = cPeak > 0 ? TrueStrain(cPeak) / rise : 0;

            foreach (var o in model.organs)
            {
                var tissue = tissues.Get(o.tissueId);
                double e = Math.Max(0, Math.Min(1, 1 + o.side * lateral));
                double c = Math.Min(a.maxCompression, cPeak * e);
                var load = new OrganLoad { Organ = o, Exposure = e, StrainRate_per_s = result.StrainRate_per_s };
                if (o.criterion == "compression")
                {
                    load.Strain = TrueStrain(c);
                    load.FailureStrain = FailureStrainAtRate(tissue, "failureStrainCompression", result.StrainRate_per_s);
                }
                else if (o.criterion == "tension_incompressible")
                {
                    load.Strain = c > 0 ? Math.Pow(1 - c, -0.5) - 1 : 0; // inkompressibel: Querstreckung = (Längsstreckung)^−½
                    load.FailureStrain = FailureStrainAtRate(tissue, "failureStrain", result.StrainRate_per_s);
                }
                else throw new ArgumentException("Unbekanntes Kriterium " + o.criterion);
                load.Cv = Cv(tissue, o.cvFrom, tissues);
                load.Probability = load.FailureStrain > 0 && load.Cv > 0 && load.Strain > 0
                    ? Biomechanics.NormalCdf((load.Utilization - 1) / load.Cv) : 0;
                result.Organs.Add(load);
            }
            return result;
        }

        /// <summary>Messwerte für injury_risk.json: Ausnutzung in Prozent je Organ, dazu Spitzenkompression des Bauchs.</summary>
        public static List<CrashMeasurement> Measurements(AbdomenLoad load)
        {
            var list = new List<CrashMeasurement> { new CrashMeasurement { metric = "Abdomen.C", value = load.PeakCompression * 100 } };
            foreach (var o in load.Organs) list.Add(new CrashMeasurement { metric = o.Organ.metric, value = o.Utilization * 100 });
            return list;
        }

        /// <summary>Wahre (logarithmische) Stauchung aus der technischen Kompression C.</summary>
        public static double TrueStrain(double c) => c <= 0 ? 0 : -Math.Log(1 - Math.Min(c, 0.999));

        /// <summary>
        /// Versagensdehnung bei gegebener Dehnrate: lineare Interpolation über log10(Rate) zwischen den Messungen
        /// (menschliches Gewebe, Messgröße <paramref name="property"/>); außerhalb des Messbereichs der nächste Wert.
        /// Gibt es mehrere Messungen ohne Rate, zählt der Mittelwert; nur Parenchym ohne Kapsel.
        /// </summary>
        public static double FailureStrainAtRate(TissueEntry t, string property, double rate)
        {
            if (t == null || t.measurements == null) return 0;
            var pts = t.measurements.Where(m => m.property == property && m.species == "human" && m.unit == "1"
                                                && (m.loading == null || !m.loading.Contains("Kapsel")))
                                    .Where(m => m.strainRate_per_s > 0)
                                    .GroupBy(m => m.strainRate_per_s).Select(g => new { r = (double)g.Key, v = g.Average(m => (double)m.value) })
                                    .OrderBy(p => p.r).ToList();
            if (pts.Count == 0) return 0;
            if (rate <= pts[0].r) return pts[0].v;
            if (rate >= pts[pts.Count - 1].r) return pts[pts.Count - 1].v;
            double lr = Math.Log10(rate);
            for (int i = 1; i < pts.Count; i++)
            {
                if (rate > pts[i].r) continue;
                double l0 = Math.Log10(pts[i - 1].r), l1 = Math.Log10(pts[i].r);
                return pts[i - 1].v + (pts[i].v - pts[i - 1].v) * (lr - l0) / (l1 - l0);
            }
            return pts[pts.Count - 1].v;
        }

        /// <summary>Variationskoeffizient SD / Mittel der angegebenen Messung.</summary>
        public static double Cv(TissueEntry t, string measurementId)
        {
            var m = TissueDatabase.Measurement(t, measurementId);
            return m != null && m.value > 0 ? m.sd / m.value : 0;
        }

        /// <summary>Gewebe-Datenbank zur Messung <paramref name="measurementId"/> suchen (für cvFrom über Gewebegrenzen).</summary>
        static double Cv(TissueDatabase db, string measurementId)
        {
            foreach (var t in db.All)
            {
                var m = TissueDatabase.Measurement(t, measurementId);
                if (m != null) return m.value > 0 ? m.sd / m.value : 0;
            }
            return 0;
        }

        // cvFrom darf auf ein anderes Gewebe verweisen (z. B. Milz nutzt die Streuung der Leber, weil für die Milz
        // je Dehnrate nur Mittelwerte veröffentlicht sind) — daher Suche über alle Gewebe.
        static double Cv(TissueEntry t, string measurementId, TissueDatabase db)
        {
            double cv = Cv(t, measurementId);
            return cv > 0 ? cv : Cv(db, measurementId);
        }
    }
}
