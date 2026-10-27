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
    /// ACHTUNG Lehrbuch vs. Realität: Registerdaten (TraumaRegister DGU, Mutschler et al.
    /// Crit Care 2013;17:R42) zeigen, dass echte Traumapatienten deutlich weniger Tachykardie
    /// entwickeln als ATLS vorhersagt (mittlere HF nur ca. 86 -> 97/min über die Schockklassen).
    /// Der Mock bildet bewusst das ATLS-Lehrbuchbild ab. Siehe docs/LITERATUR.md.
    ///
    /// GRENZEN (absichtlich): keine Gerinnung, keine Hämodilution, keine Azidose,
    /// keine Hypothermie, kristalloide Volumenwirkung nur als fester Anteil.
    /// Für echten Realismus -> PulsePhysiologyAdapter. Der Mock dient als
    /// Referenz/Plausibilitätscheck und als Fallback.
    /// Keine Abhängigkeit von UnityEngine -> außerhalb von Unity testbar.
    /// </summary>
    public class MockPhysiologyEngine : IPhysiologyEngine
    {
        public string Name => "Mock (ATLS-Lehrmodell)";
        public VitalSigns Current { get; private set; }

        // Stützstellen: relativer Blutverlust -> Vitalwert.
        // QUELLE: ATLS Student Course Manual, 10th ed. (ACS 2018), Tabelle der Schockklassen.
        // Konkrete Zwischenwerte sind eigene Interpolation (Status: Entwurf, zu validieren).
        static readonly double[] LossPoints = { 0.00, 0.15, 0.30, 0.40, 0.50, 0.58 };
        static readonly double[] HrTable    = { 75,   98,   118,  138,  150,  110 }; // präterminal Bradykardie
        static readonly double[] SbpTable   = { 120,  120,  112,  88,   70,   50  };
        static readonly double[] DbpTable   = { 78,   84,   82,   62,   45,   30  }; // Klasse II: kleine Amplitude
        static readonly double[] RrTable    = { 14,   18,   26,   34,   40,   40  };
        static readonly double[] EtCo2Table = { 38,   37,   33,   27,   20,   14  };
        static readonly double[] SpO2Air    = { 97,   97,   96,   95,   92,   88  };
        const double ArrestThreshold = 0.60;

        // Anteil eines Infusionsvolumens, der (vereinfachend) intravasal wirksam bleibt.
        static readonly Dictionary<string, double> Retention = new Dictionary<string, double>
        {
            { "BalancedCrystalloid", 0.3 }, { "Saline", 0.3 }, { "Blood", 1.0 }
        };

        class Bleed
        {
            public InjuryEvent Injury;
            public double Multiplier = 1;
            public double Lost_mL;
        }

        class Infusion
        {
            public string Compound;
            public double Remaining_mL;
            public double Rate_mL_min;
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

        readonly Dictionary<string, Bleed> _bleeds = new Dictionary<string, Bleed>();
        readonly Dictionary<string, Tension> _tension = new Dictionary<string, Tension>();
        readonly List<Infusion> _infusions = new List<Infusion>();
        readonly Random _rng;
        double _baselineVolume, _volume, _time, _baselineMap;
        bool _oxygen, _arrest;

        /// <param name="seed">Fester Seed macht Zufallsereignisse (z. B. Versagen der Punktion) reproduzierbar.</param>
        public MockPhysiologyEngine(int? seed = null)
        {
            _rng = seed.HasValue ? new Random(seed.Value) : new Random();
        }

        public bool Initialize(PatientProfile patient)
        {
            _baselineVolume = _volume = patient.EstimatedBloodVolume_mL;
            _time = 0;
            _bleeds.Clear();
            _tension.Clear();
            _infusions.Clear();
            _oxygen = _arrest = false;
            _baselineMap = DbpTable[0] + (SbpTable[0] - DbpTable[0]) / 3.0;
            UpdateVitals();
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
                    if (_bleeds.TryGetValue(iv.TargetCompartment, out var b))
                        b.Multiplier = Math.Min(b.Multiplier, iv.RateMultiplier);
                    break;
                case InterventionType.FluidInfusion:
                    _infusions.Add(new Infusion { Compound = iv.Compound, Remaining_mL = iv.Volume_mL, Rate_mL_min = iv.Rate_mL_min });
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
                // Blutung: druckabhängig und mit Obergrenze (Tamponade).
                // Fluss durch eine Gefäßläsion ~ Wurzel des Drucks (Bernoulli-Näherung).
                double pressureFactor = Math.Sqrt(Math.Max(0, Current.MeanArterialPressure_mmHg / _baselineMap));
                foreach (var b in _bleeds.Values)
                {
                    var inj = b.Injury;
                    double rate = inj.BaseRate_mL_min * inj.Severity * b.Multiplier * pressureFactor;
                    if (inj.MaxTotalLoss_mL > 0)
                        rate *= Math.Max(0, 1 - b.Lost_mL / inj.MaxTotalLoss_mL);
                    double loss = rate * minutes;
                    b.Lost_mL += loss;
                    _volume -= loss;
                }

                foreach (var t in _tension.Values)
                {
                    if (t.Drained) continue;
                    double rate = t.Injury.ProgressionRate_per_min * t.Injury.Severity * t.RateFactor;
                    t.Level = Math.Min(1, t.Level + rate * minutes);
                }
            }

            foreach (var inf in _infusions)
            {
                double given = Math.Min(inf.Rate_mL_min * minutes, inf.Remaining_mL);
                inf.Remaining_mL -= given;
                double retention = Retention.TryGetValue(inf.Compound ?? "", out var r) ? r : 0.3;
                _volume += given * retention;
            }
            _infusions.RemoveAll(i => i.Remaining_mL <= 0);

            _volume = Math.Max(0, Math.Min(_volume, _baselineVolume * 1.1));
            UpdateVitals();
        }

        void UpdateVitals()
        {
            double f = Math.Max(0, 1 - _volume / _baselineVolume);
            double tp = 0;
            foreach (var t in _tension.Values) tp = Math.Max(tp, t.Level);

            // Ohne Reanimationsmodell irreversibel. Voller Spannungspneu = obstruktiver Stillstand (PEA).
            if (f >= ArrestThreshold || tp >= 1) _arrest = true;

            if (_arrest)
            {
                Current = new VitalSigns
                {
                    SimTime_s = _time, BloodVolume_mL = _volume, BloodLossFraction = f,
                    EtCO2_mmHg = 5, CardiacArrest = true
                };
                return;
            }

            // Spannungspneu wirkt additiv auf das Blutungsbild: früh Hypoxie, Tachypnoe, Tachykardie;
            // erst ab höherer Spannung obstruktiver Blutdruckabfall.
            double sbpDrop = tp > TpHypotensionOnset ? (tp - TpHypotensionOnset) / (1 - TpHypotensionOnset) * TpMaxSbpDrop : 0;
            double sbp = Interp(f, SbpTable) - sbpDrop;
            double dbp = Interp(f, DbpTable) - sbpDrop * 0.6;
            double spo2 = Interp(f, SpO2Air) - TpMaxSpO2Drop * tp + (_oxygen ? 3 : 0);
            spo2 = Math.Max(50, Math.Min(100, spo2));
            Current = new VitalSigns
            {
                SimTime_s = _time,
                HeartRate_bpm = Interp(f, HrTable) + TpMaxHrAdd * tp,
                SystolicBP_mmHg = sbp,
                DiastolicBP_mmHg = dbp,
                MeanArterialPressure_mmHg = dbp + (sbp - dbp) / 3.0,
                RespirationRate_bpm = Interp(f, RrTable) + TpMaxRrAdd * tp,
                SpO2_percent = spo2,
                EtCO2_mmHg = Interp(f, EtCo2Table) - TpMaxEtCo2Drop * tp,
                BloodVolume_mL = _volume,
                BloodLossFraction = f
            };
        }

        static double Interp(double x, double[] table)
        {
            if (x <= LossPoints[0]) return table[0];
            for (int i = 1; i < LossPoints.Length; i++)
            {
                if (x <= LossPoints[i])
                {
                    double t = (x - LossPoints[i - 1]) / (LossPoints[i] - LossPoints[i - 1]);
                    return table[i - 1] + t * (table[i] - table[i - 1]);
                }
            }
            return table[table.Length - 1];
        }
    }
}
