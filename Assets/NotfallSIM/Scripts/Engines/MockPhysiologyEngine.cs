using System;
using System.Collections.Generic;
using NotfallSIM.Core;

namespace NotfallSIM.Engines
{
    /// <summary>
    /// Bewusst einfaches Lehrmodell für hämorrhagischen Schock — läuft ohne Pulse,
    /// damit das Projekt sofort spielbar ist. Vitalwerte werden aus dem relativen
    /// Blutverlust über Stützstellen interpoliert, die sich an den ATLS-Schockklassen
    /// orientieren (Klasse I &lt;15 %, II 15–30 %, III 30–40 %, IV &gt;40 %).
    ///
    /// Zwei Modi (siehe <see cref="ShockTables"/>): Lehrbuch (ATLS-Stufen) und Register
    /// (an TraumaRegister-DGU-Daten angelehnt: kaum Tachykardie, individuelle Streuung,
    /// Alter und Betablocker wirken sich aus). Siehe docs/LITERATUR.md.
    ///
    /// GRENZEN (absichtlich): Gerinnung nur als Gerinnsel an der Läsion (keine Koagulopathie), keine Hämodilution, keine Azidose,
    /// keine Hypothermie, kristalloide Volumenwirkung nur als fester Anteil.
    /// Für echten Realismus -> PulsePhysiologyAdapter. Der Mock dient als
    /// Referenz/Plausibilitätscheck und als Fallback.
    /// Keine Abhängigkeit von UnityEngine -> außerhalb von Unity testbar.
    /// </summary>
    public class MockPhysiologyEngine : IPhysiologyEngine
    {
        public string Name => Mode == PhysiologyMode.Registry ? "Mock (Realitätsmodus)" : "Mock (Lehrbuchmodus)";
        public PhysiologyMode Mode { get; }
        public VitalSigns Current { get; private set; }

        // Stützstellen und Werte je Modus: Core/ShockTables.cs
        static readonly double[] LossPoints = ShockTables.LossPoints;
        const double ArrestThreshold = 0.60;

        // Anteil eines Infusionsvolumens, der (vereinfachend) intravasal wirksam bleibt.
        static readonly Dictionary<string, double> Retention = new Dictionary<string, double>
        {
            { "BalancedCrystalloid", 0.3 }, { "Saline", 0.3 }, { "Blood", 1.0 }, { "Plasma", 1.0 }
        };

        class Bleed
        {
            public InjuryEvent Injury;
            public double Multiplier = 1;
            public double Lost_mL;
            public double ClotTimer_s;   // Zeit unter der Nachblutungsschwelle
            public bool Clotted;
        }

        // Blutungsphysik (docs/LITERATUR.md, Abschnitt Blutungsphysik):
        // Fluss durch die Läsion ~ √(Druckdifferenz) (Bernoulli/Ausflussformel), Druckdifferenz = MAD − Gegendruck des Raums.
        // Bauchraum (Milz, Leber, Mesenterium teilen sich ihn): IAP = IAP0 · exp(V / (IAP0 · C0)), Anfangscompliance
        // 400 mL/mmHg bei IAP 7 mmHg, danach steiler (Malbrain 2016: 250–450 mL/mmHg normal, 60–133 bei hohem IAP).
        // Geschlossene Räume (Becken, Oberschenkel): linear, Gegendruck erreicht den Ausgangs-MAD beim Tamponadevolumen
        // (maxTotalLoss_mL). Außen (Extremität, Arm): kein Gegendruck.
        const double IapBaseline_mmHg = 7, AbdominalCompliance_mL_per_mmHg = 400;
        // Gerinnsel: bilden sich bei Läsionen, die sich verschließen können (Durchtrennung mit Retraktion, Parenchym,
        // venös), wenn der MAD unter der Nachblutungsschwelle bleibt; darüber lösen sie sich (Sondeen 2003: 64 ± 2 mmHg).
        // Bildungszeit und Restfluss: Annahmen.
        const double RebleedMap_mmHg = 64, ClotFormation_s = 300, ClotFlowFactor = 0.1;

        // Hydrostatik: Druck am Blutungsort = Druck auf Herzhöhe − ρ·g·Δh (Δh = Höhe über dem Herzen).
        // ρ = 1060 kg/m³ (Kenner 1989), g = 9,81 m/s² → 78,0 mmHg je Meter.
        public const double HydrostaticMmHgPerM = 1060 * 9.81 / 133.322;
        // Venöse Läsionen bluten mit dem venösen Druck, nicht mit dem MAD: mittlerer Füllungsdruck ≈ 7 mmHg (Guyton & Hall),
        // skaliert mit dem MAD relativ zum Ausgangswert (fällt im Schock mit). Deshalb wirkt Hochlagern bei venöser Blutung stark.
        public const double VenousPressure_mmHg = 7;

        // Lage der Blutungsorte als Anteil der Körpergröße vom Boden im Stehen (Winter 2009: Hüfte 0,530, Knie 0,285,
        // Sprunggelenk 0,039, Schulter 0,818, Ellbogen 0,630, Handgelenk 0,485). Herz auf Höhe 4. ICR: 0,72 (Annahme).
        const double HipLevel = 0.530, ShoulderLevel = 0.818, HeartLevel = 0.72;
        static readonly Dictionary<string, KeyValuePair<double, string>> Sites = new Dictionary<string, KeyValuePair<double, string>>
        {
            { "Extremity", new KeyValuePair<double, string>((0.285 + 0.039) / 2, "legR") },   // Unterschenkel rechts
            { "RightThigh", new KeyValuePair<double, string>((0.530 + 0.285) / 2, "legR") },
            { "LeftThigh", new KeyValuePair<double, string>((0.530 + 0.285) / 2, "legL") },
            { "LeftArm", new KeyValuePair<double, string>((0.630 + 0.485) / 2, "armL") },     // Unterarm links
            { "RightArm", new KeyValuePair<double, string>((0.630 + 0.485) / 2, "armR") },
            { "Pelvis", new KeyValuePair<double, string>(0.53, "trunk") },
            { "Abdomen", new KeyValuePair<double, string>(0.62, "trunk") },
            { "Liver", new KeyValuePair<double, string>(0.66, "trunk") },
            { "Spleen", new KeyValuePair<double, string>(0.66, "trunk") },
            { "RightLeg", new KeyValuePair<double, string>((0.285 + 0.039) / 2, "legR") },   // Unterschenkel (geschlossene Fraktur)
            { "LeftLeg", new KeyValuePair<double, string>((0.285 + 0.039) / 2, "legL") },
            { "RightThighArtery", new KeyValuePair<double, string>((0.530 + 0.285) / 2, "legR") },
            { "LeftThighArtery", new KeyValuePair<double, string>((0.530 + 0.285) / 2, "legL") },
            { "RightChest", new KeyValuePair<double, string>(0.72, "trunk") },           // Pleurahöhle (Hämatothorax)
            { "LeftChest", new KeyValuePair<double, string>(0.72, "trunk") },
        };

        /// <summary>Lagerungen: Oberkörper (Kopf hoch, Grad), Beine rechts/links, Arme rechts/links (Grad gegen die Liegefläche),
        /// Autotransfusion (mL, zentral verschobenes Volumen). Beine 45° aus flacher Rückenlage: halbes Volumen des klassischen
        /// Passive Leg Raising aus halbsitzender Lage (≈ 300 mL, Monnet 2015) — Annahme.</summary>
        public static readonly Dictionary<string, double[]> Positions = new Dictionary<string, double[]>
        {
            //                               Oberkörper, Bein re, Bein li, Arm re, Arm li, Autotransfusion
            { "flach",            new double[] { 0,  0,  0,  0,  0,   0 } },
            { "beine_hoch",       new double[] { 0, 45, 45,  0,  0, 150 } },
            { "bein_rechts_hoch", new double[] { 0, 45,  0,  0,  0,  75 } },
            { "oberkoerper_30",   new double[] { 30, 0,  0,  0,  0,   0 } },
            { "arm_links_hoch",   new double[] { 0,  0,  0,  0, 90,   0 } },
        };
        double[] _position = Positions["flach"];
        double _height_m = 1.8;

        public string PositionId { get; private set; } = "flach";

        /// <summary>Höhe eines Blutungsorts über dem Herzen (m) in der aktuellen Lagerung.</summary>
        public double SiteHeight_m(string compartment)
        {
            if (!Sites.TryGetValue(compartment ?? "", out var site)) return 0;
            return SiteHeight(site.Key, site.Value, _position, _height_m);
        }

        public static double SiteHeight(double level, string limb, double[] pos, double height_m)
        {
            double rad = Math.PI / 180, trunk = pos[0] * rad;
            double heart = (HeartLevel - HipLevel) * height_m * Math.Sin(trunk);
            double h;
            switch (limb)
            {
                case "legR": h = (HipLevel - level) * height_m * Math.Sin(pos[1] * rad); break;
                case "legL": h = (HipLevel - level) * height_m * Math.Sin(pos[2] * rad); break;
                case "armR": case "armL":
                    double arm = (limb == "armR" ? pos[3] : pos[4]) * rad;
                    double shoulder = (ShoulderLevel - HipLevel) * height_m * Math.Sin(trunk);
                    h = shoulder + (ShoulderLevel - level) * height_m * Math.Sin(arm - trunk);
                    break;
                default: h = (level - HipLevel) * height_m * Math.Sin(trunk); break;
            }
            return h - heart;
        }

        static string SpaceOf(string compartment)
        {
            switch (compartment)
            {
                case "Spleen": case "Liver": case "Abdomen": return "Peritoneum";
                case "Extremity": case "LeftArm": case "RightArm": case "RightThighArtery": case "LeftThighArtery": return "";
                default: return compartment;
            }
        }

        /// <summary>Intraabdomineller Druck bei zusätzlichem Volumen im Bauchraum.</summary>
        public static double AbdominalPressure_mmHg(double addedVolume_mL) =>
            IapBaseline_mmHg * Math.Exp(addedVolume_mL / (IapBaseline_mmHg * AbdominalCompliance_mL_per_mmHg));

        static bool CanClot(string lesion) => lesion == "transection" || lesion == "parenchymal" || lesion == "venous";

        /// <summary>Gegendruck des Raums (mmHg) bei dem darin angesammelten Blut.</summary>
        double BackPressure(Bleed b, double spaceVolume_mL)
        {
            string space = SpaceOf(b.Injury.Compartment);
            if (space == "") return 0;
            if (space == "Peritoneum") return AbdominalPressure_mmHg(spaceVolume_mL);
            return b.Injury.MaxTotalLoss_mL > 0 ? spaceVolume_mL / b.Injury.MaxTotalLoss_mL * ReferencePressure(b) : 0;
        }

        /// <summary>Treibender Druck auf Herzhöhe im Ausgangszustand: MAD, bei venösen Läsionen der venöse Druck.</summary>
        double ReferencePressure(Bleed b) => b.Injury.Lesion == "venous" ? VenousPressure_mmHg : _baselineMap;

        /// <summary>Hat sich an der Blutung dieser Verletzung ein Gerinnsel gebildet?</summary>
        public bool InjuryClotted(string injuryId)
        {
            foreach (var b in _bleeds.Values) if (b.Injury.InjuryId == injuryId && b.Clotted) return true;
            return false;
        }

        class Infusion
        {
            public string Compound;
            public double Remaining_mL;
            public double Rate_mL_min;
            public bool Warmed;
        }

        // Spannungspneumothorax, spontan atmender Patient. Level 0..1 = Schwere der Spannung.
        // Didaktische Annahmen nach Roberts et al. (Ann Surg 2015): bei Spontanatmung ist Hypoxie
        // häufig und früh, Hypotonie und Stillstand deutlich seltener und spät.
        // Zahlenwerte sind eigene Setzungen (Status: Entwurf), siehe docs/LITERATUR.md.
        const double TpMaxHrAdd = 30, TpMaxRrAdd = 12, TpMaxSpO2Drop = 25, TpMaxEtCo2Drop = 6;
        const double TpHypotensionOnset = 0.6, TpMaxSbpDrop = 50;
        const double TpLevelAfterNeedle = 0.15, TpRateAfterNeedle = 0.2;

        class Tension
        {
            public InjuryEvent Injury;
            public double Level;
            public double RateFactor = 1;
            public bool Drained;
        }

        // Schädel-Hirn-Trauma mit raumforderndem Hämatom. Level 0..1 = intrakranielle Raumforderung.
        // Verlauf: luzides Intervall (GCS lange 14–15), dann Eintrübung, ab 0,7 Anisokorie (Herniation),
        // ab 0,8 Cushing-Reflex, bei 1 Einklemmung. Hypotonie oder Hypoxie beschleunigen den Verlauf
        // (sekundäre Hirnschädigung, vgl. Chesnut et al. 1993). Zahlen: didaktische Setzungen, Entwurf.
        static readonly double[] TbiPoints = { 0.0, 0.3, 0.5, 0.7, 0.85, 1.0 };
        static readonly double[] TbiGcs    = { 15,  14,  11,  8,   5,    3   };
        // GCS-Abfall im hämorrhagischen Schock (ATLS: Klasse III verwirrt, Klasse IV lethargisch).
        static readonly double[] ShockGcs  = { 15,  15,  15,  14,  12,   9   };
        const double TbiAnisocoriaLevel = 0.7, TbiCushingLevel = 0.8;
        const double TbiSecondaryInsultFactor = 2.0;     // Beschleunigung bei RRsys < 90 oder SpO2 < 90
        const double TbiOsmotherapyReduction = 0.15, TbiLevelAfterSurgery = 0.4;
        const double AirwayMaxSpO2Drop = 15, AirwayMaxEtCo2Rise = 10; // ungeschützter Atemweg bei GCS <= 8
        const double TpFactorWhenVentilated = 3.0;       // Überdruckbeatmung verschärft Spannungspneu (Roberts 2015)
        // Noradrenalin: Blutdruckanstieg (didaktische Setzung). Über den höheren MAD steigt auch der Blutfluss aus Läsionen.
        const double VasopressorSbpRise = 20, VasopressorDbpRise = 12;

        class BrainInjury
        {
            public InjuryEvent Injury;
            public double Level;
            public bool Stopped;
        }

        // ---------------- Temperatur (docs/LITERATUR.md, Abschnitt Letale Trias)
        // Spezifische Wärme des Körpers 2,98 kJ/(kg·K) (Xu 2022; früher üblich 3,47); Infusionen bringen ihre Temperatur mit:
        // ΔT = m_Infusion · c_Infusion · (T_Infusion − T) / (m_Körper · c_Körper).
        public const double BodyHeatCapacity_kJ_kgK = 2.98;
        static readonly Dictionary<string, double[]> FluidThermal = new Dictionary<string, double[]>
        {   // Temperatur ungewärmt (°C), spezifische Wärme (kJ/(kg·K)), Dichte (kg/L)
            { "BalancedCrystalloid", new[] { 20.0, 4.18, 1.0 } }, { "Saline", new[] { 20.0, 4.18, 1.0 } },
            { "Blood", new[] { 4.0, 3.6, 1.06 } }, { "Plasma", new[] { 4.0, 3.9, 1.03 } },
        };
        public const double FluidWarmerTemp_C = 38;
        // Auskühlung entkleidet im Schockraum (Annahme), in Narkose doppelt (Umverteilung, Sessler 2008), aktive Wärme
        public const double ExposureCooling_C_per_h = 0.5, AnesthesiaCoolingFactor = 2, ActiveWarming_C_per_h = 0.5;

        // ---------------- Gerinnung
        // Konzentrationen relativ zum Normalwert im Plasma; Verdünnung nach Massenbilanz im Plasmavolumen (Blutvolumen −
        // Erythrozytenvolumen). Fibrinogen normal 2,5 g/L; kritisch < 1,5 g/L (Rossaint 2023).
        public const double FibrinogenNormal_g_L = 2.5, FibrinogenConcentrate_g = 2;
        // Hyperfibrinolyse im Schock (akute traumatische Koagulopathie, Brohi 2003) ohne TXA: Abbau je Minute bei 40 % Verlust
        public const double LysisPerMinAt40 = 0.02;
        // Wirkung der wirksamen Gerinnung C (0..1) auf die Blutung (Annahme): diffus/venös/Parenchym bis ×2, arteriell bis ×1,3;
        // Gerinnsel bilden sich nur bei C ≥ 0,5, unter 0,3 lösen sie sich.
        public const double CoagDiffuseGain = 1.0, CoagArterialGain = 0.3, CoagClotMin = 0.5, CoagClotLost = 0.3;
        public const double CalciumPerGram_mmol_L = 0.2;   // 1 g CaCl2 (6,8 mmol) in ~15 L Extrazellulärraum, ionisiert ≈ +0,2 (Annahme)

        // ---------------- Medikamente: Wirkung über die Zeit (voll bis full_min, abklingend bis end_min)
        //                                        SBP-Anteil, Anteil je Blutverlust (Verstärkung im Schock), HF, Atemdepression, voll, Ende (min)
        public static readonly Dictionary<string, double[]> Drugs = new Dictionary<string, double[]>
        {
            { "Fentanyl", new[] { -0.10, -0.33,  -8, 0.3, 20, 40 } },  // Sympathikolyse, Atemdepression (spontan atmend)
            { "Ketamin",  new[] {  0.10, -0.29, 12, 0.0, 10, 20 } },  // sympathomimetisch; im tiefen Schock (≥ 35 %) kein Anstieg
            { "Propofol", new[] { -0.20, -0.67, -5, 0.6,  5, 15 } },  // Induktion: MAD −16 bis −23 % (normovoläm), im Schock stärker
        };
        readonly List<double[]> _drugDoses = new List<double[]>();   // je Gabe: Zeit, Dosisfaktor (parallel zu _drugs)
        readonly List<KeyValuePair<string, double>> _drugs = new List<KeyValuePair<string, double>>();
        double _weight_kg = 75, _temp = 36.5, _factors = 1, _fib = 1, _calciumGiven_g;
        bool _warming, _fluidWarmer, _txa;

        /// <summary>Noradrenalin: sättigende Dosis-Wirkung (Emax), 1 bei Referenzrate, höchstens 2.</summary>
        public static double PressorEffect(double f) => f <= 0 ? 0 : 2 * f / (1 + f);

        /// <summary>Wirkdauer wächst mit dem Logarithmus der Dosis (Bolus: Umverteilung), mindestens halb so lang.</summary>
        public static double DurationFactor(double f) => Math.Max(0.5, 1 + Math.Log(Math.Max(1e-6, f)));

        /// <summary>Wirkstärke 0..1 eines Medikaments t Minuten nach der Gabe (Dosisfaktor f verlängert die Wirkung).</summary>
        public static double DrugLevel(double[] d, double t_min, double f = 1)
        {
            double g = DurationFactor(f), t1 = d[4] * g, t2 = d[5] * g;
            return t_min < 0 ? 0 : t_min <= t1 ? 1 : t_min >= t2 ? 0 : 1 - (t_min - t1) / (t2 - t1);
        }

        /// <summary>Summe der Medikamentenwirkungen: SBP-Faktor, HF-Änderung, Atemdepression 0..1.</summary>
        void DrugEffects(double lossFraction, out double sbpFactor, out double hrAdd, out double resp)
        {
            sbpFactor = 1; hrAdd = 0; resp = 0;
            for (int i = 0; i < _drugs.Count; i++)
            {
                var kv = _drugs[i];
                var d = Drugs[kv.Key];
                double f = _drugDoses[i][1];
                double w = DrugLevel(d, (_time - kv.Value) / 60, f);
                if (w <= 0) continue;
                double pct = (d[0] + d[1] * lossFraction) * f;
                if (kv.Key == "Ketamin") pct = Math.Max(0, pct);       // erschöpfte Katecholamine: kein Anstieg mehr
                sbpFactor *= Math.Max(0.3, 1 + pct * w);
                hrAdd += d[2] * f * w;
                resp = Math.Max(resp, Math.Min(1, d[3] * f * w));
            }
        }

        /// <summary>Wirksame Gerinnung 0..1 aus Faktoren, Fibrinogen, Temperatur, pH und ionisiertem Calcium.</summary>
        public static double Coagulation(double factors, double fibRel, double temp_C, double pH, double ica)
        {
            double cf = Clamp01((factors - 0.2) / 0.3);                                   // Faktoren < 20 % → keine Gerinnung, ≥ 50 % → voll
            double cfib = Clamp01((fibRel * FibrinogenNormal_g_L - 0.8) / 0.7);           // Fibrinogen 0,8 … 1,5 g/L
            double t = Math.Max(0.3, Math.Min(1, 1 - 0.075 * Math.Max(0, 35 - temp_C))); // Thrombozyten unter 35 °C (Wolberg 2004)
            double ph = Math.Max(0.2, Math.Min(1, 1 - (7.35 - pH) / 0.35 * 0.6));          // FVIIa/TF −60 % bei pH 7,0 (Meng 2003)
            double ca = Math.Max(0.5, Math.Min(1, 0.5 + 0.5 * (ica - 0.6) / 0.4));         // Cofaktor Calcium, < 1,0 mmol/L
            return Math.Min(cf, cfib) * t * ph * ca;
        }

        static double Clamp01(double x) => Math.Max(0, Math.Min(1, x));

        double IonizedCa => Math.Min(1.3, BloodGas.IonizedCalcium(_transfused_mL) + CalciumPerGram_mmol_L * _calciumGiven_g);

        readonly Dictionary<string, Bleed> _bleeds = new Dictionary<string, Bleed>();
        readonly Dictionary<string, Tension> _tension = new Dictionary<string, Tension>();
        readonly Dictionary<string, BrainInjury> _brain = new Dictionary<string, BrainInjury>();
        readonly List<Infusion> _infusions = new List<Infusion>();
        readonly Random _rng;
        double _baselineVolume, _volume, _time, _baselineMap;
        // Individuelle Reaktion (Realitätsmodus) und Patientenfaktoren
        double _hrBaseOffset, _hrResponse = 1, _sbpOffset;
        // Erythrozytenmasse für das Hb: Blutung entfernt Vollblut (Hb bleibt akut gleich),
        // Kristalloide verdünnen, EKs bringen Erythrozyten. Transkapilläre Rückfüllung ist nicht modelliert.
        const double EkHematocrit = 0.6;
        double _redCellVolume, _transfused_mL;
        bool _oxygen, _arrest, _intubated;
        double _pressor;   // Noradrenalin-Laufrate relativ zur Referenz (0,1 µg/kg/min); 0 = aus

        /// <param name="seed">Fester Seed macht Zufallsereignisse (z. B. Versagen der Punktion) reproduzierbar.</param>
        public MockPhysiologyEngine(int? seed = null, PhysiologyMode mode = PhysiologyMode.Textbook)
        {
            _rng = seed.HasValue ? new Random(seed.Value) : new Random();
            Mode = mode;
        }

        double Uniform(double min, double max) => min + _rng.NextDouble() * (max - min);

        public double InjuryLevel(string injuryId)
        {
            double level = 0;
            foreach (var t in _tension.Values) if (t.Injury.InjuryId == injuryId) level = Math.Max(level, t.Level);
            foreach (var b in _brain.Values) if (b.Injury.InjuryId == injuryId) level = Math.Max(level, b.Level);
            return level;
        }

        public double InjuryBloodLoss_mL(string injuryId)
        {
            double lost = 0;
            foreach (var b in _bleeds.Values) if (b.Injury.InjuryId == injuryId) lost += b.Lost_mL;
            return lost;
        }

        public bool Initialize(PatientProfile patient)
        {
            _baselineVolume = _volume = patient.EstimatedBloodVolume_mL;
            _time = 0;
            _bleeds.Clear();
            _tension.Clear();
            _brain.Clear();
            _infusions.Clear();
            _oxygen = _arrest = _intubated = false; _pressor = 0;
            _position = Positions["flach"]; PositionId = "flach";
            _drugs.Clear(); _drugDoses.Clear(); _warming = _fluidWarmer = _txa = false;
            _weight_kg = patient.weight_kg > 0 ? patient.weight_kg : 75;
            _temp = patient.initialTemperature_C > 0 ? patient.initialTemperature_C : 36.5;
            _factors = 1; _fib = 1; _calciumGiven_g = 0;
            _height_m = patient.height_cm > 100 ? patient.height_cm / 100 : 1.8;
            // Lehrbuch: idealisierter Patient. Register: Ausgangswerte und Reaktionsstärke streuen.
            _hrBaseOffset = 0; _hrResponse = 1; _sbpOffset = 0;
            if (Mode == PhysiologyMode.Registry)
            {
                _hrBaseOffset = Uniform(-12, 12);
                _hrResponse = Uniform(0.5, 1.5);
                _sbpOffset = Uniform(-10, 10) + (patient.age >= 65 ? 15 : 0); // Ältere: oft vorbestehende Hypertonie
            }
            // Betablocker dämpfen die Tachykardie in beiden Modi (didaktischer Faktor).
            if (patient.betaBlocker) _hrResponse *= 0.25;
            double hb0 = patient.IsMale ? 14.5 : 13.0; // g/dL
            _redCellVolume = _volume * hb0 * 3 / 100;   // Hkt ≈ 3 × Hb
            _transfused_mL = 0;
            UpdateVitals();
            _baselineMap = Current.MeanArterialPressure_mmHg; // Bezug: gesunder Ausgangszustand
            // Präklinischer Blutverlust: Vollblut, das Hb bleibt dabei gleich.
            if (patient.initialBloodLoss_mL > 0)
            {
                double lost = Math.Min(patient.initialBloodLoss_mL, _volume * 0.5);
                _redCellVolume -= lost * _redCellVolume / _volume;
                _volume -= lost;
                UpdateVitals();
            }
            return true;
        }

        public void ApplyInjury(InjuryEvent injury)
        {
            switch (injury.Effect)
            {
                case InjuryEffectType.Hemorrhage:
                    _bleeds[injury.Compartment] = new Bleed { Injury = injury };
                    break;
                case InjuryEffectType.TensionPneumothorax:
                    _tension[injury.Compartment] = new Tension { Injury = injury };
                    break;
                case InjuryEffectType.TraumaticBrainInjury:
                    _brain[injury.Compartment] = new BrainInjury { Injury = injury, Level = Math.Max(0, Math.Min(1, injury.InitialLevel)) };
                    break;
                default:
                    Console.WriteLine($"[Mock] Effekt {injury.Effect} wird vom Mock nicht simuliert ({injury.InjuryId}).");
                    break;
            }
        }

        public void ApplyIntervention(InterventionEvent iv)
        {
            switch (iv.Type)
            {
                case InterventionType.HemorrhageControl:
                    // Mehrere Ziele kommagetrennt möglich (z. B. Laparotomie: "Spleen,Liver").
                    foreach (var target in (iv.TargetCompartment ?? "").Split(','))
                        if (_bleeds.TryGetValue(target.Trim(), out var b))
                            b.Multiplier = Math.Min(b.Multiplier, iv.RateMultiplier);
                    break;
                case InterventionType.FluidInfusion:
                    _infusions.Add(new Infusion { Compound = iv.Compound, Remaining_mL = iv.Volume_mL, Rate_mL_min = iv.Rate_mL_min, Warmed = _fluidWarmer });
                    break;
                case InterventionType.Medication:
                    switch (iv.Compound)
                    {
                        case "TXA": if (iv.DoseFactor >= 0.5) _txa = true; break;   // ≥ 0,5 g hemmt die Fibrinolyse
                        case "Fibrinogen":
                            _fib += FibrinogenConcentrate_g * iv.DoseFactor / (Math.Max(500, _volume - _redCellVolume) / 1000 * FibrinogenNormal_g_L);
                            break;
                        case "Calcium": _calciumGiven_g += iv.DoseFactor; break;
                        default:
                            if (iv.Compound != null && Drugs.ContainsKey(iv.Compound) && iv.DoseFactor > 0)
                            {
                                _drugs.Add(new KeyValuePair<string, double>(iv.Compound, _time));
                                _drugDoses.Add(new[] { _time, iv.DoseFactor });
                            }
                            break;
                    }
                    UpdateVitals();
                    break;
                case InterventionType.Supportive:
                    if (iv.Compound == "Warming") _warming = true;
                    if (iv.Compound == "FluidWarmer") _fluidWarmer = true;
                    break;
                case InterventionType.Oxygen:
                    _oxygen = true;
                    break;
                case InterventionType.NeedleDecompression:
                    // Kann versagen (Nadel zu kurz, abgeknickt) — dann ändert sich nichts.
                    if (_tension.TryGetValue(iv.TargetCompartment, out var tn) && !tn.Drained
                        && _rng.NextDouble() < iv.SuccessProbability)
                    {
                        tn.Level = Math.Min(tn.Level, TpLevelAfterNeedle);
                        tn.RateFactor = Math.Min(tn.RateFactor, TpRateAfterNeedle); // Re-Spannung möglich
                    }
                    break;
                case InterventionType.Intubation:
                    _intubated = true;
                    break;
                case InterventionType.Vasopressor:
                    _pressor = Math.Max(0, iv.DoseFactor);
                    break;
                case InterventionType.Positioning:
                    if (Positions.TryGetValue(iv.TargetCompartment ?? "", out var pos)) { _position = pos; PositionId = iv.TargetCompartment; UpdateVitals(); }
                    break;
                case InterventionType.Osmotherapy:
                    foreach (var bi in _brain.Values) bi.Level = Math.Max(0, bi.Level - TbiOsmotherapyReduction);
                    break;
                case InterventionType.NeurosurgicalDecompression:
                    foreach (var bi in _brain.Values) { bi.Level = Math.Min(bi.Level, TbiLevelAfterSurgery); bi.Stopped = true; }
                    break;
                case InterventionType.ChestDrain:
                    if (_tension.TryGetValue(iv.TargetCompartment, out var td))
                    {
                        td.Level = 0;
                        td.Drained = true;
                    }
                    break;
            }
        }

        public void Advance(double dt_s)
        {
            if (dt_s <= 0) return;
            _time += dt_s;
            double minutes = dt_s / 60.0;

            if (!_arrest)
            {
                // Blutung: Fluss ~ √(MAD − Gegendruck), bezogen auf den Ausgangszustand; Gerinnsel unter der Nachblutungsschwelle.
                double map = Current.MeanArterialPressure_mmHg;
                var spaceVolume = new Dictionary<string, double>();
                foreach (var b in _bleeds.Values)
                {
                    string sp = SpaceOf(b.Injury.Compartment);
                    spaceVolume[sp] = (spaceVolume.TryGetValue(sp, out var v0) ? v0 : 0) + b.Lost_mL;
                }
                foreach (var b in _bleeds.Values)
                {
                    var inj = b.Injury;
                    double coag = Current.Coagulation;
                    if (CanClot(inj.Lesion))
                    {
                        if (map < RebleedMap_mmHg && coag >= CoagClotMin) { b.ClotTimer_s += dt_s; if (b.ClotTimer_s >= ClotFormation_s) b.Clotted = true; }
                        else if (map >= RebleedMap_mmHg || coag < CoagClotLost) { b.ClotTimer_s = 0; b.Clotted = false; }
                    }
                    double back = BackPressure(b, spaceVolume[SpaceOf(inj.Compartment)]);
                    double back0 = BackPressure(b, 0);
                    double pref = ReferencePressure(b);
                    double p = (inj.Lesion == "venous" ? VenousPressure_mmHg * map / _baselineMap : map)
                               - HydrostaticMmHgPerM * SiteHeight_m(inj.Compartment);
                    double drive = Math.Max(0, p - back) / Math.Max(0.5, pref - back0);
                    bool arterial = inj.Lesion == "partial" || inj.Lesion == "transection";
                    double coagGain = 1 + (arterial ? CoagArterialGain : CoagDiffuseGain) * (1 - coag);
                    double rate = inj.BaseRate_mL_min * inj.Severity * b.Multiplier * Math.Sqrt(drive) * (b.Clotted ? ClotFlowFactor : 1) * coagGain;
                    double loss = rate * minutes;
                    b.Lost_mL += loss;
                    if (_volume > 0) _redCellVolume -= loss * _redCellVolume / _volume;
                    _volume -= loss;
                }

                foreach (var t in _tension.Values)
                {
                    if (t.Drained) continue;
                    double rate = t.Injury.ProgressionRate_per_min * t.Injury.Severity * t.RateFactor
                                  * (_intubated ? TpFactorWhenVentilated : 1);
                    t.Level = Math.Min(1, t.Level + rate * minutes);
                }

                bool secondaryInsult = Current.SystolicBP_mmHg < 90 || Current.SpO2_percent < 90;
                foreach (var bi in _brain.Values)
                {
                    if (bi.Stopped) continue;
                    double rate = bi.Injury.ProgressionRate_per_min * bi.Injury.Severity
                                  * (secondaryInsult ? TbiSecondaryInsultFactor : 1);
                    bi.Level = Math.Min(1, bi.Level + rate * minutes);
                }
            }

            foreach (var inf in _infusions)
            {
                double given = Math.Min(inf.Rate_mL_min * minutes, inf.Remaining_mL);
                inf.Remaining_mL -= given;
                if (given <= 0) continue;
                double retention = Retention.TryGetValue(inf.Compound ?? "", out var r) ? r : 0.3;
                // Gerinnung: Mischung im Plasma (Kristalloid und EK ohne Gerinnungsfaktoren, Plasma mit normalen Spiegeln)
                double plasma = Math.Max(500, _volume - _redCellVolume);
                double plasmaIn = inf.Compound == "Blood" ? given * (1 - EkHematocrit) : given * retention;
                double conc = inf.Compound == "Plasma" ? 1 : 0;
                _factors = (_factors * plasma + conc * plasmaIn) / (plasma + plasmaIn);
                _fib = (_fib * plasma + conc * plasmaIn) / (plasma + plasmaIn);
                // Wärme: Infusion mit ihrer Temperatur (ungewärmt: Raum bzw. Kühlschrank)
                if (FluidThermal.TryGetValue(inf.Compound ?? "", out var th))
                {
                    double tIn = inf.Warmed ? FluidWarmerTemp_C : th[0];
                    _temp += given / 1000 * th[2] * th[1] * (tIn - _temp) / (_weight_kg * BodyHeatCapacity_kJ_kgK);
                }
                _volume += given * retention;
                if (inf.Compound == "Blood") { _redCellVolume += given * EkHematocrit; _transfused_mL += given; }
                if (inf.Compound == "Plasma") _transfused_mL += given;   // Citrat auch im Plasma
            }
            // Hyperfibrinolyse im Schock, solange keine Tranexamsäure gegeben wurde
            double lossNow = Math.Max(0, 1 - _volume / _baselineVolume);
            if (!_txa && !_arrest && lossNow > 0.15) _fib = Math.Max(0, _fib - LysisPerMinAt40 * (lossNow / 0.4) * minutes);
            // Temperatur: entkleidet kühlt aus (in Narkose schneller), Wärmeerhalt wärmt bis 37 °C
            if (_warming) _temp = Math.Min(37, _temp + ActiveWarming_C_per_h * dt_s / 3600);
            else _temp -= ExposureCooling_C_per_h * (_intubated ? AnesthesiaCoolingFactor : 1) * dt_s / 3600;
            _infusions.RemoveAll(i => i.Remaining_mL <= 0);

            _redCellVolume = Math.Max(0, Math.Min(_redCellVolume, _volume));
            _volume = Math.Max(0, Math.Min(_volume, _baselineVolume * 1.1));
            UpdateVitals();
        }

        void UpdateVitals()
        {
            double f = Math.Max(0, 1 - _volume / _baselineVolume);
            double tp = 0;
            foreach (var t in _tension.Values) tp = Math.Max(tp, t.Level);
            double tbi = 0;
            foreach (var bi in _brain.Values) tbi = Math.Max(tbi, bi.Level);

            // Ohne Reanimationsmodell irreversibel. Voller Spannungspneu = obstruktiver Stillstand (PEA),
            // volle Raumforderung = Einklemmung.
            if (f >= ArrestThreshold || tp >= 1 || tbi >= 1) _arrest = true;

            double hb = _volume > 0 ? _redCellVolume / _volume * 100 / 3 : 0;
            if (_arrest)
            {
                Current = new VitalSigns
                {
                    Hb = hb, pH = 6.90, PaCO2 = 70, PaO2 = 30, HCO3 = 4, Potassium = 6.5,
                    IonizedCalcium = IonizedCa, Glucose = BloodGas.Glucose(f),
                    Temperature_C = _temp, FactorActivity = _factors, Fibrinogen_g_L = _fib * FibrinogenNormal_g_L, Coagulation = 0,
                    SimTime_s = _time, BloodVolume_mL = _volume, BloodLossFraction = f,
                    EtCO2_mmHg = 5, CardiacArrest = true, GCS = 3, BaseExcess = -20, Lactate = 12, Anisocoria = tbi >= TbiAnisocoriaLevel,
                    Intubated = _intubated
                };
                return;
            }

            // Kreislauf: zentral verfügbares Volumen inkl. Autotransfusion durch Lagerung (nur Hämodynamik, nicht Hb/Laktat)
            double fh = Math.Max(0, 1 - (_volume + _position[5]) / _baselineVolume);
            double gcs = Math.Min(Interp(tbi, TbiPoints, TbiGcs), Interp(f, LossPoints, ShockGcs));
            // Ungeschützter Atemweg bei tiefer Bewusstlosigkeit: Hypoventilation, Hypoxie, Hyperkapnie.
            double airway = !_intubated && gcs <= 8 ? Math.Min(1, (8 - gcs) / 5.0 + 0.2) : 0;
            // Cushing-Reflex: Hypertonie, Bradykardie, unregelmäßige Atmung.
            double cushing = tbi > TbiCushingLevel ? (tbi - TbiCushingLevel) / (1 - TbiCushingLevel) : 0;

            // Spannungspneu wirkt additiv auf das Blutungsbild: früh Hypoxie, Tachypnoe, Tachykardie;
            // erst ab höherer Spannung obstruktiver Blutdruckabfall.
            double sbpDrop = tp > TpHypotensionOnset ? (tp - TpHypotensionOnset) / (1 - TpHypotensionOnset) * TpMaxSbpDrop : 0;
            double hr0 = ShockTables.Hr(Mode, 0);
            double hr = hr0 + _hrBaseOffset + (ShockTables.Hr(Mode, fh) - hr0) * _hrResponse;
            DrugEffects(f, out double drugSbp, out double drugHr, out double drugResp);
            double hypoCa = Math.Max(0, Math.Min(1, (1.0 - IonizedCa) / 0.4));       // ionisiertes Ca < 1,0: schwächere Kontraktion (Annahme)
            double sbp = (ShockTables.Sbp(Mode, fh) + _sbpOffset - sbpDrop + 50 * cushing + VasopressorSbpRise * PressorEffect(_pressor)) * drugSbp - 15 * hypoCa;
            double dbp = (ShockTables.Dbp(Mode, fh) + _sbpOffset * 0.5 - sbpDrop * 0.6 + 25 * cushing + VasopressorDbpRise * PressorEffect(_pressor)) * drugSbp - 8 * hypoCa;
            hr += drugHr;
            // Atemdepression durch Opioid/Hypnotikum wirkt wie ein ungeschützter Atemweg (nur spontan atmend)
            double respDep = _intubated ? 0 : drugResp;
            double rrFactor = 1 - 0.5 * respDep;
            airway = Math.Max(airway, respDep);
            double spo2 = Interp(f, ShockTables.SpO2Air) - TpMaxSpO2Drop * tp - AirwayMaxSpO2Drop * airway
                          + (_oxygen ? 3 : 0) + (_intubated ? 3 : 0);
            spo2 = Math.Max(50, Math.Min(100, spo2));
            double be = Interp(f, ShockTables.BaseExcess);
            double hco3 = BloodGas.Hco3(be);
            double paco2 = BloodGas.PaCO2(hco3, _intubated, airway, tp);
            double ph = BloodGas.PH(hco3, paco2);
            Current = new VitalSigns
            {
                SimTime_s = _time,
                HeartRate_bpm = hr + TpMaxHrAdd * tp - 35 * cushing,
                SystolicBP_mmHg = sbp,
                DiastolicBP_mmHg = dbp,
                MeanArterialPressure_mmHg = dbp + (sbp - dbp) / 3.0,
                RespirationRate_bpm = _intubated ? 14 : (ShockTables.Rr(Mode, f) + TpMaxRrAdd * tp - 6 * airway - 6 * cushing) * rrFactor,
                SpO2_percent = spo2,
                EtCO2_mmHg = Interp(f, ShockTables.EtCo2) - TpMaxEtCo2Drop * tp + AirwayMaxEtCo2Rise * airway,
                BaseExcess = be,
                Lactate = Interp(f, ShockTables.Lactate),
                Hb = hb,
                HCO3 = hco3,
                PaCO2 = paco2,
                pH = ph,
                PaO2 = BloodGas.PaO2(spo2, _oxygen, _intubated),
                Potassium = BloodGas.Potassium(ph),
                IonizedCalcium = IonizedCa,
                Glucose = BloodGas.Glucose(f),
                Temperature_C = _temp,
                FactorActivity = _factors,
                Fibrinogen_g_L = _fib * FibrinogenNormal_g_L,
                Coagulation = Coagulation(_factors, _fib, _temp, ph, IonizedCa),
                GCS = Math.Round(gcs),
                Anisocoria = tbi >= TbiAnisocoriaLevel,
                Intubated = _intubated,
                BloodVolume_mL = _volume,
                BloodLossFraction = f
            };
        }

        static double Interp(double x, double[] table) => ShockTables.Interp(x, table);
        static double Interp(double x, double[] points, double[] table) => ShockTables.Interp(x, points, table);
    }
}
