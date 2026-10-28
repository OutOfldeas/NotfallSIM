using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace NotfallSIM.Anatomy
{
    // Datenklassen für StreamingAssets/NotfallSIM/tissues.json (JsonUtility bzw. DataContractJsonSerializer in den Tests).
    // Zahlen stehen nur in der JSON-Datei, jede mit Quelle. 0 bedeutet „nicht angegeben“.

    [Serializable]
    public class TissueMeasurement
    {
        public string id;
        public string property;       // E, shearModulus, failureStress, failureStrain, failureStretch, yieldStress, yieldStrain,
                                      // failureStressCompression, failureStrainCompression
        public float value, sd, min, max;
        public string unit;           // Pa, kPa, MPa, GPa oder 1 (Bruchteil)
        public string loading;        // Versuchsart, Probe
        public float strainRate_per_s;
        public string direction;
        public string species;        // human, Schwein, …
        public int n;
        public string sourceId;
        public string note;
    }

    [Serializable]
    public class TissueEntry
    {
        public string id, name, evidence, description;
        public float density_g_cm3;
        public string densitySourceId;
        public string stiffnessFrom, failureStrainFrom, failureStressFrom; // Messung, die als repräsentativer Wert dient
        public string note;
        public TissueMeasurement[] measurements;
    }

    [Serializable]
    public class TissueMapping
    {
        public string tissueId;
        public string layer;      // leer = beliebige Ebene
        public string[] meshes;   // reguläre Ausdrücke auf FMA-Namen; leer = alle Teile der Ebene
    }

    [Serializable]
    public class TissueSource
    {
        public string id, citation, url, note;
    }

    [Serializable]
    public class TissueDatabaseFile
    {
        public string version, note;
        public TissueEntry[] tissues;
        public TissueMapping[] mapping;
        public TissueSource[] sources;
    }

    /// <summary>
    /// Mechanische Gewebeeigenschaften je Anatomieteil: Zuordnung über tissues.json, Umrechnung in SI-Einheiten
    /// und eine kurze Beschreibung für den Röntgenblick. Ohne Unity-Abhängigkeit (wird außerhalb getestet).
    /// </summary>
    public class TissueDatabase
    {
        public readonly TissueDatabaseFile File;
        readonly Dictionary<string, TissueEntry> _byId = new Dictionary<string, TissueEntry>();
        readonly Dictionary<string, TissueSource> _sources = new Dictionary<string, TissueSource>();
        readonly List<KeyValuePair<TissueMapping, Regex[]>> _rules = new List<KeyValuePair<TissueMapping, Regex[]>>();

        public TissueDatabase(TissueDatabaseFile file)
        {
            File = file;
            foreach (var t in file.tissues) _byId[t.id] = t;
            foreach (var s in file.sources) _sources[s.id] = s;
            foreach (var m in file.mapping)
            {
                var pats = m.meshes ?? new string[0];
                var rx = new Regex[pats.Length];
                for (int i = 0; i < pats.Length; i++) rx[i] = new Regex(pats[i], RegexOptions.CultureInvariant);
                _rules.Add(new KeyValuePair<TissueMapping, Regex[]>(m, rx));
            }
        }

        public TissueEntry Get(string id) => id != null && _byId.TryGetValue(id, out var t) ? t : null;
        public TissueSource Source(string id) => id != null && _sources.TryGetValue(id, out var s) ? s : null;
        public IEnumerable<TissueEntry> All => _byId.Values;

        /// <summary>Gewebe eines Anatomieteils (FMA-Name, Ebene). Erste passende Regel gewinnt.</summary>
        public TissueEntry For(string meshName, string layer)
        {
            foreach (var r in _rules)
            {
                var m = r.Key;
                if (!string.IsNullOrEmpty(m.layer) && m.layer != layer) continue;
                if (r.Value.Length == 0)
                {
                    if (!string.IsNullOrEmpty(m.layer)) return Get(m.tissueId); // ganze Ebene
                    continue;
                }
                foreach (var rx in r.Value)
                    if (rx.IsMatch(meshName)) return Get(m.tissueId);
            }
            return null;
        }

        public static TissueMeasurement Measurement(TissueEntry t, string id)
        {
            if (t == null || string.IsNullOrEmpty(id) || t.measurements == null) return null;
            foreach (var m in t.measurements) if (m.id == id) return m;
            return null;
        }

        /// <summary>Spannung/Modul in Pa; NaN bei unbekannter Einheit.</summary>
        public static double ToPascal(double value, string unit)
        {
            switch (unit)
            {
                case "Pa": return value;
                case "kPa": return value * 1e3;
                case "MPa": return value * 1e6;
                case "GPa": return value * 1e9;
                default: return double.NaN;
            }
        }

        /// <summary>
        /// Repräsentativer Elastizitätsmodul in Pa (0 = unbekannt). Ein Schermodul wird für nahezu inkompressibles
        /// Weichgewebe mit E = 3G umgerechnet.
        /// </summary>
        public static double YoungsModulus_Pa(TissueEntry t)
        {
            var m = Measurement(t, t?.stiffnessFrom);
            if (m == null) return 0;
            double v = ToPascal(m.value, m.unit);
            if (double.IsNaN(v)) return 0;
            return m.property == "shearModulus" ? 3 * v : m.property == "E" ? v : 0;
        }

        /// <summary>Repräsentative Versagensdehnung als Bruchteil (0 = unbekannt); Streckung λ wird zu λ − 1.</summary>
        public static double FailureStrain(TissueEntry t)
        {
            var m = Measurement(t, t?.failureStrainFrom);
            if (m == null || m.unit != "1") return 0;
            return m.property == "failureStretch" ? m.value - 1 : m.value;
        }

        /// <summary>Repräsentative Versagens- bzw. Fließspannung in Pa (0 = unbekannt).</summary>
        public static double FailureStress_Pa(TissueEntry t)
        {
            var m = Measurement(t, t?.failureStressFrom);
            if (m == null) return 0;
            double v = ToPascal(m.value, m.unit);
            return double.IsNaN(v) ? 0 : v;
        }

        // deutsches Dezimalkomma ohne Abhängigkeit von installierten Kulturdaten (IL2CPP, invariant globalization)
        static string Num(double v, string format) => v.ToString(format, CultureInfo.InvariantCulture).Replace('.', ',');

        /// <summary>Druck/Spannung lesbar: 13,9 GPa, 52,6 kPa …</summary>
        public static string FormatPa(double pa)
        {
            string u = "Pa"; double v = pa;
            if (pa >= 1e9) { v = pa / 1e9; u = "GPa"; }
            else if (pa >= 1e6) { v = pa / 1e6; u = "MPa"; }
            else if (pa >= 1e3) { v = pa / 1e3; u = "kPa"; }
            string f = v >= 100 ? "0" : v >= 10 ? "0.#" : "0.##";
            return Num(v, f) + " " + u;
        }

        static string Rate(TissueMeasurement m)
        {
            if (m == null || m.strainRate_per_s <= 0) return "";
            return " bei " + Num(m.strainRate_per_s, m.strainRate_per_s >= 1 ? "0" : m.strainRate_per_s >= 0.1 ? "0.#" : "0.###") + "/s";
        }

        /// <summary>
        /// Kurzbeschreibung für den Röntgenblick, z. B. „Leber: E ≈ 6 kPa · reißt ab ≈ 30 % Dehnung (52,6 kPa bei 1/s)“.
        /// </summary>
        public static string Summary(TissueEntry t)
        {
            if (t == null) return "";
            var parts = new List<string>();
            double e = YoungsModulus_Pa(t);
            if (e > 0) parts.Add("E ≈ " + FormatPa(e));
            double eps = FailureStrain(t);
            var sm = Measurement(t, t.failureStressFrom);
            bool yield = sm != null && sm.property == "yieldStress";
            if (eps > 0)
            {
                string s = (yield ? "gibt nach ab ≈ " : "reißt ab ≈ ") + Num(eps * 100, eps < 0.1 ? "0.#" : "0") + " % Dehnung";
                double st = FailureStress_Pa(t);
                if (st > 0) s += " (" + FormatPa(st) + Rate(sm) + ")";
                parts.Add(s);
            }
            else if (FailureStress_Pa(t) > 0) parts.Add("Festigkeit ≈ " + FormatPa(FailureStress_Pa(t)) + Rate(sm));
            string tag = t.evidence == "tierdaten" ? " [Tierdaten]" : t.evidence == "human_eingeschraenkt" ? " [eingeschränkt belegt]" : "";
            if (parts.Count == 0) return t.name + ": keine belegten Werte";
            return t.name + ": " + string.Join(" · ", parts) + tag;
        }
    }
}
