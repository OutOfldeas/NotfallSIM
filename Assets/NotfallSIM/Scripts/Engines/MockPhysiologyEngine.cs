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
            { "Saline", 0.3 }, { "RingerAcetate", 0.3 }, { "Blood", 1.0 }
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

        readonly Dictionary<string, Bleed> _bleeds = new Dictionary<string, Bleed>();
        readonly List<Infusion> _infusions = new List<Infusion>();
        double _baselineVolume, _volume, _time, _baselineMap;
        bool _oxygen, _arrest;

        public bool Initialize(PatientProfile patient)
        {
            _baselineVolume = _volume = patient.EstimatedBloodVolume_mL;
            _time = 0;
            _bleeds.Clear();
            _infusions.Clear();
            _oxygen = _arrest = false;
            _baselineMap = DbpTable[0] + (SbpTable[0] - DbpTable[0]) / 3.0;
            UpdateVitals();
            return true;
        }

        public void ApplyInjury(InjuryEvent injury)
        {
            if (injury.Effect != InjuryEffectType.Hemorrhage)
            {
                Console.WriteLine($"[Mock] Effekt {injury.Effect} wird vom Mock nicht simuliert ({injury.InjuryId}).");
                return;
            }
            _bleeds[injury.Compartment] = new Bleed { Injury = injury };
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
            if (f >= ArrestThreshold) _arrest = true; // ohne Reanimationsmodell: irreversibel

            if (_arrest)
            {
                Current = new VitalSigns
                {
                    SimTime_s = _time, BloodVolume_mL = _volume, BloodLossFraction = f,
                    EtCO2_mmHg = 5, CardiacArrest = true
                };
                return;
            }

            double sbp = Interp(f, SbpTable), dbp = Interp(f, DbpTable);
            double spo2 = _oxygen ? Math.Min(100, Interp(f, SpO2Air) + 3) : Interp(f, SpO2Air);
            Current = new VitalSigns
            {
                SimTime_s = _time,
                HeartRate_bpm = Interp(f, HrTable),
                SystolicBP_mmHg = sbp,
                DiastolicBP_mmHg = dbp,
                MeanArterialPressure_mmHg = dbp + (sbp - dbp) / 3.0,
                RespirationRate_bpm = Interp(f, RrTable),
                SpO2_percent = spo2,
                EtCO2_mmHg = Interp(f, EtCo2Table),
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
