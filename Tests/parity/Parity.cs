// Gibt deterministische Verläufe der C#-Mock-Engine aus (Lehrbuchmodus, ohne Zufall),
// damit web/engine.js dagegen geprüft werden kann. Aufruf über Tests/parity/run_parity.sh.
using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Text;
using NotfallSIM.Data;
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
                    N(v.SpO2_percent), N(v.EtCO2_mmHg), N(v.BloodVolume_mL), N(v.GCS), N(v.BaseExcess), v.CardiacArrest ? "1" : "0",
                    N(v.Hb), N(v.pH), N(v.PaCO2), N(v.PaO2), N(v.Potassium), N(v.IonizedCalcium), N(v.Glucose),
                    N(v.Temperature_C), N(v.FactorActivity), N(v.Fibrinogen_g_L), N(v.Coagulation)));
            }
            e.Advance(0.1);
        }
    }

    static string N(double x) => x.ToString("0.###", CultureInfo.InvariantCulture);
    static InjuryEvent Bleed(string c, double s, double r, double m) => new InjuryEvent { Effect = InjuryEffectType.Hemorrhage, Compartment = c, Severity = s, BaseRate_mL_min = r, MaxTotalLoss_mL = m };
    static InterventionEvent Iv(InterventionType t, string target = null, string compound = null, double vol = 0, double rate = 0, double mult = 1, double dose = 1) =>
        new InterventionEvent { Type = t, TargetCompartment = target, Compound = compound, Volume_mL = vol, Rate_mL_min = rate, RateMultiplier = mult, DoseFactor = dose };

    static T Load<T>(string path)
    {
        var ser = new DataContractJsonSerializer(typeof(T));
        using (var st = new MemoryStream(Encoding.UTF8.GetBytes(File.ReadAllText(path)))) return (T)ser.ReadObject(st);
    }

    /// <summary>Befundverlauf: Spannungspneu, Milz und Becken mit Nadel, Gurt, Drainage und Laparotomie.</summary>
    static void ExamCourse(System.Collections.Generic.Dictionary<string, InjuryDefinition> defs, FindingsDatabaseFile normal)
    {
        var sc = new ScenarioDefinition { injuries = new[] {
            new ScenarioInjury { injuryId = "spannungspneu_links", severity = 0.8, onsetTime_s = 60 },
            new ScenarioInjury { injuryId = "milzruptur", severity = 0.9 },
            new ScenarioInjury { injuryId = "becken_instabil", severity = 0.7 } } };
        var e = new MockPhysiologyEngine(3, PhysiologyMode.Textbook);
        e.Initialize(new PatientProfile { weight_kg = 80, age = 40 });
        var log = new System.Collections.Generic.List<LogEntry>();
        var plan = new System.Collections.Generic.Dictionary<int, string[]> {
            { 3000, new[] { "beckengurt" } }, { 6000, new[] { "nadel_li" } }, { 9000, new[] { "laparotomie", "drainage_li" } } };
        var ivs = new System.Collections.Generic.Dictionary<string, InterventionEvent> {
            { "beckengurt", new InterventionEvent { Id = "beckengurt", Type = InterventionType.HemorrhageControl, TargetCompartment = "Pelvis", RateMultiplier = 0.5 } },
            { "nadel_li", new InterventionEvent { Id = "nadel_li", Type = InterventionType.NeedleDecompression, TargetCompartment = "LeftLung", SuccessProbability = 1 } },
            { "laparotomie", new InterventionEvent { Id = "laparotomie", Type = InterventionType.HemorrhageControl, TargetCompartment = "Spleen,Liver", RateMultiplier = 0.05 } },
            { "drainage_li", new InterventionEvent { Id = "drainage_li", Type = InterventionType.ChestDrain, TargetCompartment = "LeftLung" } } };
        bool tpOn = false;
        for (int step = 0; step <= 21000; step++)
        {
            double t = step / 10.0;
            foreach (var si in sc.injuries)
                if (si.injuryId == "spannungspneu_links" ? (!tpOn && t >= si.onsetTime_s) : step == 0)
                {
                    if (si.injuryId == "spannungspneu_links") tpOn = true;
                    var def = defs[si.injuryId];
                    foreach (var x in def.effects)
                        e.ApplyInjury(new InjuryEvent { InjuryId = def.id, Effect = (InjuryEffectType)Enum.Parse(typeof(InjuryEffectType), x.effectType),
                            Compartment = x.compartment, Severity = si.severity, BaseRate_mL_min = x.baseRate_mL_min, MaxTotalLoss_mL = x.maxTotalLoss_mL,
                            ProgressionRate_per_min = x.progressionRate_per_min, InitialLevel = x.initialLevel });
                }
            string[] ids;
            if (plan.TryGetValue(step, out ids))
                foreach (var id in ids)
                {
                    var iv = ivs[id];
                    log.Add(new LogEntry { Time_s = t, Tag = $"intervention:{iv.Type}:{iv.TargetCompartment}:{iv.Id}" });
                    if (id == "laparotomie") continue; // Wirkung im Befund über requiresTagDelay
                    e.ApplyIntervention(iv);
                }
            if (step % 600 == 0)
            {
                var ctx = new ExamContext { Time_s = t, BodyLossFraction = e.Current.BloodLossFraction, Level = e.InjuryLevel, Lost_mL = e.InjuryBloodLoss_mL, Events = log };
                foreach (var region in new[] { "Thorax", "Abdomen", "Becken" })
                    Console.WriteLine($"exam|{N(t)}|{region}|" + string.Join(" / ", Examination.Findings(region, sc, id => defs[id], normal, ctx)));
            }
            e.Advance(0.1);
        }
    }

    /// <summary>Feste Ereignisfolge je Fall: erste Verletzung untersucht und vermutet, Leber vermutet, eFAST, Milz verworfen.</summary>
    static string DiagnosisLine(ScenarioDefinition s, System.Collections.Generic.Dictionary<string, InjuryDefinition> defs, ImagingModality[] mods, DifferentialDatabaseFile diffs)
    {
        var first = defs[s.injuries[0].injuryId];
        var events = new System.Collections.Generic.List<LogEntry> {
            new LogEntry { Time_s = 5, Tag = "exam:" + first.region }, new LogEntry { Time_s = 10, Tag = "diagnosis:" + first.id },
            new LogEntry { Time_s = 20, Tag = "diagnosis:leberlazeration" }, new LogEntry { Time_s = 25, Tag = "diagnosis:milzruptur" }, new LogEntry { Time_s = 26, Tag = "diagnosis:aortenverletzung" },
            new LogEntry { Time_s = 30, Tag = "result:Imaging:efast" }, new LogEntry { Time_s = 35, Tag = "diagnosis-retracted:milzruptur" } };
        return string.Join(",", DiagnosisEvaluation.Evaluate(s.injuries, events, id => defs[id], mods, diffs)
            .Select(r => $"{r.InjuryId}:{r.Outcome}:{(r.Time_s.HasValue ? N(r.Time_s.Value) : "-")}:{(r.Examined ? 1 : 0)}{(r.ImagingShown ? 1 : 0)}{(r.ClinicalOnly ? 1 : 0)}{(r.IsDifferential ? 1 : 0)}:{string.Join(" ", r.Against)}"));
    }

    /// <summary>Zufallsszenarien: gleicher Seed muss in C# und JS dasselbe ergeben.</summary>
    static void Generator(string root)
    {
        var mech = Load<MechanismDatabaseFile>(Path.Combine(root, "mechanisms.json"));
        var inj = Load<InjuryDatabaseFile>(Path.Combine(root, "injuries.json")).injuries;
        var mods = Load<ImagingDatabaseFile>(Path.Combine(root, "imaging.json")).modalities;
        var byId = inj.ToDictionary(d => d.id);
        var diffs = Load<DifferentialDatabaseFile>(Path.Combine(root, "differentials.json"));
        foreach (var m in mech.mechanisms)
            for (uint seed = 1; seed <= 60; seed++)
            {
                var s = ScenarioGenerator.Generate(mech, m.id, inj, seed * 7919u);
                var p = s.patient;
                Console.WriteLine(string.Join("|", s.id, s.description, p.sex, p.age, N(p.weight_kg), N(p.height_cm), p.betaBlocker ? "1" : "0", N(p.initialBloodLoss_mL),
                    string.Join(",", s.injuries.Select(i => i.injuryId + ":" + N(i.severity) + ":" + N(i.onsetTime_s))),
                    string.Join(",", s.assessment.Select(c => c.id)),
                    DiagnosisLine(s, byId, mods, diffs)));
            }
        ExamCourse(byId, Load<FindingsDatabaseFile>(Path.Combine(root, "findings.json")));
        // Coach: Tipp und Strategie entlang einer festen Ereignisfolge, wechselnde Vitalwerte (stabil, Schock, Koma)
        var ivDefs = Load<InterventionDatabaseFile>(Path.Combine(root, "interventions.json")).interventions;
        var cases = Directory.GetFiles(Path.Combine(root, "scenarios"), "*.json").OrderBy(f => Path.GetFileName(f), StringComparer.Ordinal)
            .Select(f => Load<ScenarioDefinition>(f)).ToList();
        foreach (var m in mech.mechanisms) for (uint seed = 1; seed <= 5; seed++) cases.Add(ScenarioGenerator.Generate(mech, m.id, inj, seed * 7919u));
        string[] script = { "intervention:Oxygen::o2", "exam:Thorax", "exam:Becken", "exam:Abdomen", "result:Imaging:efast", "intervention:FluidInfusion:Blood::ek",
                            "intervention:FluidInfusion:Blood::ek", "intervention:HemorrhageControl:Pelvis:beckengurt", "exam:Kopf", "exam:Oberschenkel rechts",
                            "exam:Oberschenkel links", "diagnosis:sdh_akut_links", "result:Imaging:ct_ganzkoerper" };
        foreach (var s in cases)
        {
            var log = new System.Collections.Generic.List<LogEntry>();
            var tips = new System.Collections.Generic.List<string>();
            CoachContext ctx = null;
            for (int k = 0; k <= script.Length; k++)
            {
                double t = 15 + 20 * k;
                var v = new VitalSigns { SimTime_s = t, HeartRate_bpm = k % 3 == 1 ? 128 : 84, SystolicBP_mmHg = k % 3 == 1 ? 82 : 124, DiastolicBP_mmHg = 70,
                                         GCS = k % 3 == 2 ? 7 : 14, BaseExcess = k % 3 == 1 ? -8 : -1, IonizedCalcium = k > 6 ? 1.02 : 1.18, Temperature_C = 36.2 };
                ctx = new CoachContext { Scenario = s, Injury = id => byId.ContainsKey(id) ? byId[id] : null, Interventions = ivDefs, Imaging = mods, Events = log, Vitals = v };
                var tip = Coach.Tip(ctx);
                tips.Add($"{tip.Key}:{tip.Phase}:{(tip.Deadline_s.HasValue ? N(tip.Deadline_s.Value) : "-")}");
                if (k < script.Length) log.Add(new LogEntry { Time_s = t + 1, Tag = script[k] });
            }
            var strat = Coach.Strategy(ctx);
            Console.WriteLine($"coach|{s.id}|" + string.Join(",", tips) + "|" + string.Join(",", strat.Select(i =>
                $"{i.Key}:{i.Phase}:{(i.Done_s.HasValue ? N(i.Done_s.Value) : "-")}:{(i.Deadline_s.HasValue ? N(i.Deadline_s.Value) : "-")}:{(i.Missed ? 1 : 0)}")) +
                "|" + string.Join(",", Coach.Cautions(ctx)));
        }
    }

    static void Main(string[] args)
    {
        Console.OutputEncoding = new UTF8Encoding(false);
        if (args.Length == 2 && args[0] == "gen") { Generator(args[1]); return; }
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
        var sdh = new InjuryEvent { Effect = InjuryEffectType.TraumaticBrainInjury, Compartment = "Brain", Severity = 1.0, ProgressionRate_per_min = 0.018, InitialLevel = 0.45 };
        Run("polytrauma_noradrenalin", new PatientProfile { weight_kg = 85, age = 42, initialBloodLoss_mL = 1500 }, (e, s) =>
        {
            if (s == 0) { e.ApplyInjury(sdh); e.ApplyInjury(Bleed("Pelvis", 0.7, 150, 3000)); e.ApplyInjury(Bleed("RightThigh", 0.6, 20, 1000)); }
            if (s == 6000) e.ApplyIntervention(Iv(InterventionType.Intubation));
            if (s == 7000) e.ApplyIntervention(Iv(InterventionType.Vasopressor));
        }, 1800);
        Run("laparotomie_mehrfach", man80, (e, s) =>
        {
            if (s == 0) { e.ApplyInjury(Bleed("Spleen", 0.8, 100, 2500)); e.ApplyInjury(Bleed("Liver", 0.7, 90, 2500)); e.ApplyInjury(Bleed("Extremity", 0.8, 120, 0)); }
            if (s == 300) e.ApplyIntervention(Iv(InterventionType.HemorrhageControl, "Extremity", mult: 0));
            if (s == 3000) e.ApplyIntervention(Iv(InterventionType.HemorrhageControl, "Spleen,Liver", mult: 0.05));
        }, 1200);
        Run("betablocker", new PatientProfile { weight_kg = 65, age = 76, sex = "female", betaBlocker = true },
            (e, s) => { if (s == 0) e.ApplyInjury(Bleed("Pelvis", 0.6, 150, 3000)); }, 1800);
        // Gerinnsel unter der Nachblutungsschwelle, Nachblutung nach Volumengabe; Bauchraum geteilt; Durchtrennung außen
        Run("gerinnsel_nachblutung", new PatientProfile { weight_kg = 80, age = 35, sex = "male" }, (e, s) =>
        {
            if (s == 0)
            {
                var sp = Bleed("Spleen", 0.9, 100, 2500); sp.Lesion = "parenchymal"; e.ApplyInjury(sp);
                var ab = Bleed("Abdomen", 0.6, 110, 3000); ab.Lesion = "partial"; e.ApplyInjury(ab);
                var am = Bleed("Extremity", 0.8, 100, 0); am.Lesion = "transection"; e.ApplyInjury(am);
                var pv = Bleed("Pelvis", 0.7, 150, 3000); pv.Lesion = "venous"; e.ApplyInjury(pv);
            }
            if (s == 6000) e.ApplyIntervention(Iv(InterventionType.HemorrhageControl, "Abdomen", mult: 0));
            if (s == 9000) e.ApplyIntervention(Iv(InterventionType.FluidInfusion, "", "Blood", 1500, 150));
        }, 1800);
        // Lagerung: Hydrostatik an Bein, Arm und Rumpf, Autotransfusion
        Run("lagerung", new PatientProfile { weight_kg = 80, age = 35, sex = "male", height_cm = 175, initialBloodLoss_mL = 800 }, (e, s) =>
        {
            if (s == 0)
            {
                var a = Bleed("Extremity", 0.8, 120, 0); a.Lesion = "partial"; e.ApplyInjury(a);
                var t = Bleed("RightThigh", 0.7, 20, 1000); t.Lesion = "venous"; e.ApplyInjury(t);
                var l = Bleed("LeftArm", 0.8, 40, 0); l.Lesion = "partial"; e.ApplyInjury(l);
                var v = Bleed("Liver", 0.6, 90, 2500); v.Lesion = "parenchymal"; e.ApplyInjury(v);
            }
            if (s == 1500) e.ApplyIntervention(Iv(InterventionType.Positioning, "beine_hoch"));
            if (s == 4000) e.ApplyIntervention(Iv(InterventionType.Positioning, "oberkoerper_30"));
            if (s == 6000) e.ApplyIntervention(Iv(InterventionType.Positioning, "arm_links_hoch"));
            if (s == 8000) e.ApplyIntervention(Iv(InterventionType.Positioning, "flach"));
        }, 1200);
        // Letale Trias: Verdünnung, kalte Infusion, Plasma, TXA, Fibrinogen, Calcium, Wärme, Medikamente
        Run("trias", new PatientProfile { weight_kg = 70, age = 40, sex = "male", initialBloodLoss_mL = 1200, initialTemperature_C = 35.5 }, (e, s) =>
        {
            if (s == 0)
            {
                var l = Bleed("Liver", 0.7, 90, 2500); l.Lesion = "parenchymal"; e.ApplyInjury(l);
                var a = Bleed("Extremity", 0.6, 60, 0); a.Lesion = "partial"; e.ApplyInjury(a);
            }
            if (s == 300) e.ApplyIntervention(Iv(InterventionType.FluidInfusion, "", "BalancedCrystalloid", 2000, 200));
            if (s == 600) e.ApplyIntervention(Iv(InterventionType.Medication, compound: "Fentanyl"));
            if (s == 1500) e.ApplyIntervention(Iv(InterventionType.Medication, compound: "Ketamin"));
            if (s == 2400) e.ApplyIntervention(Iv(InterventionType.FluidInfusion, "", "Blood", 1000, 100));
            if (s == 3000) e.ApplyIntervention(Iv(InterventionType.Medication, compound: "Propofol"));
            if (s == 3010) e.ApplyIntervention(Iv(InterventionType.Intubation));
            if (s == 4000) e.ApplyIntervention(Iv(InterventionType.Supportive, compound: "FluidWarmer"));
            if (s == 4200) e.ApplyIntervention(Iv(InterventionType.FluidInfusion, "", "Plasma", 1000, 50));
            if (s == 5000) e.ApplyIntervention(Iv(InterventionType.Medication, compound: "TXA"));
            if (s == 5200) e.ApplyIntervention(Iv(InterventionType.Medication, compound: "Fibrinogen"));
            if (s == 5400) e.ApplyIntervention(Iv(InterventionType.Medication, compound: "Calcium"));
            if (s == 6000) e.ApplyIntervention(Iv(InterventionType.Supportive, compound: "Warming"));
            if (s == 7000) e.ApplyIntervention(Iv(InterventionType.HemorrhageControl, "Extremity,Liver", mult: 0.1));
        }, 1800);
        // Dosierung: Bolus über und unter der Standarddosis, Perfusor verstellen und stoppen
        Run("dosis", new PatientProfile { weight_kg = 80, age = 50, sex = "female", initialBloodLoss_mL = 1000 }, (e, s) =>
        {
            if (s == 0) { var l = Bleed("Spleen", 0.5, 40, 2000); l.Lesion = "parenchymal"; e.ApplyInjury(l); }
            if (s == 300) e.ApplyIntervention(Iv(InterventionType.Medication, compound: "Fentanyl", dose: 3));
            if (s == 1200) e.ApplyIntervention(Iv(InterventionType.Medication, compound: "Propofol", dose: 0.5));
            if (s == 1500) e.ApplyIntervention(Iv(InterventionType.Medication, compound: "Ketamin", dose: 0.25));
            if (s == 2000) e.ApplyIntervention(Iv(InterventionType.Vasopressor, dose: 3));
            if (s == 3000) e.ApplyIntervention(Iv(InterventionType.Medication, compound: "Fibrinogen", dose: 2));
            if (s == 3200) e.ApplyIntervention(Iv(InterventionType.Medication, compound: "TXA", dose: 0.5));
            if (s == 3400) e.ApplyIntervention(Iv(InterventionType.Medication, compound: "Calcium", dose: 1.5));
            if (s == 5000) e.ApplyIntervention(Iv(InterventionType.Vasopressor, dose: 0.5));
            if (s == 8000) e.ApplyIntervention(Iv(InterventionType.Vasopressor, dose: 0));
        }, 1500);
    }
}
