// Gibt deterministische Verläufe der C#-Mock-Engine aus (Lehrbuchmodus, ohne Zufall),
// damit web/engine.js dagegen geprüft werden kann. Aufruf über Tests/parity/run_parity.sh.
using System;
using System.Globalization;
using NotfallSIM.Core;
using NotfallSIM.Engines;

static class Parity
{
    static void Run(string name, PatientProfile p, Action<MockPhysiologyEngine, int> plan, int seconds)
    {
        var e = new MockPhysiologyEngine(1, PhysiologyMode.Textbook);
        e.Initialize(p);
        for (int step = 0; step <= seconds * 10; step++)
        {
            plan(e, step);
            if (step % 300 == 0)
            {
                var v = e.Current;
                Console.WriteLine(string.Join(";", name, (step / 10).ToString(CultureInfo.InvariantCulture),
                    N(v.HeartRate_bpm), N(v.SystolicBP_mmHg), N(v.DiastolicBP_mmHg), N(v.RespirationRate_bpm),
                    N(v.SpO2_percent), N(v.EtCO2_mmHg), N(v.BloodVolume_mL), N(v.GCS), N(v.BaseExcess), v.CardiacArrest ? "1" : "0"));
            }
            e.Advance(0.1);
        }
    }

    static string N(double x) => x.ToString("0.###", CultureInfo.InvariantCulture);
    static InjuryEvent Bleed(string c, double s, double r, double m) => new InjuryEvent { Effect = InjuryEffectType.Hemorrhage, Compartment = c, Severity = s, BaseRate_mL_min = r, MaxTotalLoss_mL = m };
    static InterventionEvent Iv(InterventionType t, string target = null, string compound = null, double vol = 0, double rate = 0, double mult = 1) =>
        new InterventionEvent { Type = t, TargetCompartment = target, Compound = compound, Volume_mL = vol, Rate_mL_min = rate, RateMultiplier = mult };

    static void Main()
    {
        var man80 = new PatientProfile { weight_kg = 80, age = 35 };
        Run("becken_unbehandelt", man80, (e, s) => { if (s == 0) { e.ApplyInjury(Bleed("Pelvis", 0.8, 150, 3000)); e.ApplyInjury(Bleed("RightThigh", 0.6, 20, 1000)); } }, 1800);
        Run("becken_behandelt", man80, (e, s) =>
        {
            if (s == 0) { e.ApplyInjury(Bleed("Pelvis", 0.8, 150, 3000)); e.ApplyInjury(Bleed("RightThigh", 0.6, 20, 1000)); }
            if (s == 1800) e.ApplyIntervention(Iv(InterventionType.HemorrhageControl, "Pelvis", mult: 0.5));
            if (s == 3000) e.ApplyIntervention(Iv(InterventionType.FluidInfusion, compound: "Blood", vol: 600, rate: 50));
            if (s == 4000) e.ApplyIntervention(Iv(InterventionType.FluidInfusion, compound: "BalancedCrystalloid", vol: 500, rate: 100));
            if (s == 1200) e.ApplyIntervention(Iv(InterventionType.Oxygen));
        }, 1800);
        var tp = new InjuryEvent { Effect = InjuryEffectType.TensionPneumothorax, Compartment = "LeftLung", Severity = 0.8, ProgressionRate_per_min = 0.06 };
        Run("tp_unbehandelt", new PatientProfile { weight_kg = 85 }, (e, s) => { if (s == 900) e.ApplyInjury(tp); }, 1800);
        Run("tp_drainage", new PatientProfile { weight_kg = 85 }, (e, s) => { if (s == 900) e.ApplyInjury(tp); if (s == 6000) e.ApplyIntervention(Iv(InterventionType.ChestDrain, "LeftLung")); }, 1500);
        Run("tp_beatmet", new PatientProfile { weight_kg = 85 }, (e, s) => { if (s == 0) { e.ApplyInjury(tp); e.ApplyIntervention(Iv(InterventionType.Intubation)); } }, 900);
        var tbi = new InjuryEvent { Effect = InjuryEffectType.TraumaticBrainInjury, Compartment = "Brain", Severity = 0.9, ProgressionRate_per_min = 0.05 };
        Run("sht_unbehandelt", new PatientProfile { weight_kg = 78, age = 24 }, (e, s) => { if (s == 0) e.ApplyInjury(tbi); }, 1500);
        Run("sht_behandelt", new PatientProfile { weight_kg = 78, age = 24 }, (e, s) =>
        {
            if (s == 0) { e.ApplyInjury(tbi); e.ApplyIntervention(Iv(InterventionType.Oxygen)); }
            if (s == 8000) e.ApplyIntervention(Iv(InterventionType.Osmotherapy));
            if (s == 9000) e.ApplyIntervention(Iv(InterventionType.Intubation));
            if (s == 9600) e.ApplyIntervention(Iv(InterventionType.NeurosurgicalDecompression));
        }, 1800);
        Run("betablocker", new PatientProfile { weight_kg = 65, age = 76, sex = "female", betaBlocker = true },
            (e, s) => { if (s == 0) e.ApplyInjury(Bleed("Pelvis", 0.6, 150, 3000)); }, 1800);
    }
}
