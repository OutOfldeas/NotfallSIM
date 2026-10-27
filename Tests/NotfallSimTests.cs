// Testsuite außerhalb von Unity. Prüft Datenkonsistenz, Mock-Physiologie und Bewertung.
// Ausführen: Tests/run_tests.sh (braucht mono bzw. mcs). Läuft auch in GitHub Actions.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Text;
using NotfallSIM.Core;
using NotfallSIM.Data;
using NotfallSIM.Engines;

static class Tests
{
    static string DataRoot;
    static int _passed, _failed;

    static int Main(string[] args)
    {
        DataRoot = args.Length > 0 ? args[0] : "Assets/StreamingAssets/NotfallSIM";

        Run("JSON: alle Dateien lesbar", JsonLoads);
        Run("Szenarien: Verletzungen existieren in der Datenbank", ScenarioInjuriesExist);
        Run("Datenbank: Effekttypen und Maßnahmentypen gültig", TypesValid);
        Run("Bewertung: jedes Kriterium ist im Spiel erreichbar", CriteriaReachable);
        Run("Datenbank: jede Verletzung hat Quelle und Status", SourcesPresent);
        Run("Mock Becken: unbehandelt Klasse III vor 30 min", PelvisUntreated);
        Run("Mock Becken: Gurt + Blut verbessern den Verlauf", PelvisTreatedBetter);
        Run("Mock Spannungspneu: Hypoxie vor Hypotonie, Stillstand 15–30 min", TensionCourse);
        Run("Mock Spannungspneu: Drainage entlastet vollständig", TensionDrain);
        Run("Mock Nadel: Versagerrate ca. 43 %", NeedleFailureRate);
        Run("Bewertung: relatives Zeitziel und Status", AssessmentLogic);
        Run("Mock SHT: luzides Intervall, Anisokorie, Einklemmung 15–30 min", TbiUntreated);
        Run("Mock SHT: frühe Kraniotomie verhindert Einklemmung", TbiEarlySurgery);
        Run("Mock SHT: ungesicherter Atemweg beschleunigt den Verlauf", TbiAirwayMatters);
        Run("Mock: Beatmung verschärft unentlasteten Spannungspneu", VentilationWorsensTension);
        Run("Zustandsereignisse: Episoden mit Hysterese, Schwellen einmalig", StateWatcherLogic);
        Run("Maßnahmen: Verzögerung nur bei Kraniotomie, nicht negativ", InterventionDelays);
        Run("Realitätsmodus: deutlich weniger Tachykardie als Lehrbuch", RegistryLessTachycardia);
        Run("Base Excess: Grenzen der ATLS-Klassen", BaseExcessClasses);
        Run("Realitätsmodus: Streuung zwischen Patienten, Lehrbuch identisch", RegistryVariability);
        Run("Betablocker: Tachykardie stark gedämpft", BetaBlockerDampens);
        Run("Debriefing: Modusvergleich ab Klasse II", DebriefComparison);
        Run("Bildgebung: jeder Befund gehört zu einem Verfahren", ImagingFindingsCovered);
        Run("Bildgebung: Normalbefund, Verletzungsbefund, Befund nach Therapie", ImagingReportLogic);
        Run("Maßnahmen: Beckengurt und Not-OP haben unterscheidbare Tags", DistinctPelvisTags);
        Run("BGA: Normalwerte beim unverletzten Patienten", BgaBaseline);
        Run("BGA: Hb bleibt bei akuter Blutung gleich, Kristalloide verdünnen, EK hebt", BgaHemoglobin);
        Run("BGA: metabolische Azidose im Schock, Hyperkapnie bei ungeschütztem Atemweg", BgaAcidBase);
        Run("Noradrenalin: hebt den MAD, erhöht aber den Blutverlust", VasopressorTradeOff);
        Run("Polytrauma: präklinischer Blutverlust und akutes SDH bei Ankunft", PolytraumaArrival);
        Run("Zustandsereignis: MAD < 80 nur bei schwerem SHT", MapLowTbiState);
        Run("Laparotomie: stillt Milz- und Leberblutung (mehrere Ziele)", MultiTargetControl);
        Run("Generator: gleicher Seed gleiches Szenario, Ausschlussgruppen, nie leer", GeneratorConsistency);
        Run("Generator: Häufigkeiten folgen den Wahrscheinlichkeiten", GeneratorDistribution);
        Run("Generator: gute Versorgung überlebt, keine Versorgung oft nicht", GeneratorPlayable);
        Run("Untersuchung: Normalbefund ohne Verletzung, Verletzungsbefund erst ab Beginn", ExaminationFindings);
        Run("Normalbefunde: jede Region der Datenbank abgedeckt", NormalFindingsComplete);
        Run("Diagnosen: erkannt, übersehen, falscher Verdacht, verworfen", DiagnosisLogic);
        Run("Dynamische Befunde: Spätzeichen, Hypovolämie, nach Drainage und OP", DynamicFindings);
        Run("Dynamische Befunde: Bedingungen gültig, Tags erreichbar", DynamicFindingsValid);
        Run("Differenzialdiagnosen: eindeutige IDs, Argumente gültig, spezifisches Argument bevorzugt", Differentials);

        Console.WriteLine($"\n{_passed} bestanden, {_failed} fehlgeschlagen");
        return _failed == 0 ? 0 : 1;
    }

    // ------------------------------------------------------------ Daten

    static T Load<T>(string relPath)
    {
        var ser = new DataContractJsonSerializer(typeof(T));
        using (var s = new MemoryStream(Encoding.UTF8.GetBytes(File.ReadAllText(Path.Combine(DataRoot, relPath)))))
            return (T)ser.ReadObject(s);
    }

    static InjuryDatabaseFile Injuries => Load<InjuryDatabaseFile>("injuries.json");
    static InterventionDatabaseFile Interventions => Load<InterventionDatabaseFile>("interventions.json");
    static ImagingDatabaseFile Imaging => Load<ImagingDatabaseFile>("imaging.json");
    static MechanismDatabaseFile Mechanisms => Load<MechanismDatabaseFile>("mechanisms.json");
    static IEnumerable<ScenarioDefinition> Scenarios =>
        Directory.GetFiles(Path.Combine(DataRoot, "scenarios"), "*.json")
                 .Select(f => Load<ScenarioDefinition>(Path.Combine("scenarios", Path.GetFileName(f))));

    static void JsonLoads()
    {
        Check(Injuries.injuries.Length > 0, "keine Verletzungen");
        Check(Interventions.interventions.Length > 0, "keine Maßnahmen");
        Check(Scenarios.Any(), "keine Szenarien");
    }

    static void ScenarioInjuriesExist()
    {
        var ids = new HashSet<string>(Injuries.injuries.Select(i => i.id));
        foreach (var s in Scenarios)
            foreach (var si in s.injuries)
                Check(ids.Contains(si.injuryId), $"{s.id}: unbekannte Verletzung {si.injuryId}");
    }

    static void TypesValid()
    {
        foreach (var inj in Injuries.injuries)
            foreach (var e in inj.effects)
                Check(Enum.TryParse(e.effectType, out InjuryEffectType _), $"{inj.id}: Effekttyp {e.effectType}");
        foreach (var iv in Interventions.interventions)
            Check(iv.ToEvent() != null, $"{iv.id}: Maßnahmentyp {iv.type}");
        var ids = Interventions.interventions.Select(i => i.id).ToList();
        Check(ids.Count == ids.Distinct().Count(), "doppelte Maßnahmen-IDs");
    }

    /// <summary>Jeder Tag eines Kriteriums muss von einer Maßnahme, Untersuchung oder Verletzung erzeugt werden können.</summary>
    static void CriteriaReachable()
    {
        var possible = new HashSet<string>();
        foreach (var iv in Interventions.interventions)
        {
            var ev = iv.ToEvent();
            possible.Add($"intervention:{ev.Type}:{ev.TargetCompartment ?? ev.Compound ?? ""}:{ev.Id}");
            possible.Add("effect:" + ev.Type);
        }
        foreach (var inj in Injuries.injuries)
        {
            possible.Add("injury:" + inj.id);
            possible.Add("exam:" + inj.region);
        }
        foreach (var st in StateTags.All) possible.Add(st);
        foreach (var m in Imaging.modalities) { possible.Add("imaging:" + m.id); possible.Add("result:Imaging:" + m.id); }
        possible.Add("warning:unstable-transport");
        var lists = Scenarios.Select(x => new { id = x.id, crit = x.assessment ?? new AssessmentCriterion[0] })
            .Concat(Injuries.injuries.Select(x => new { id = "injury/" + x.id, crit = x.criteria ?? new AssessmentCriterion[0] }))
            .Concat(new[] { new { id = "mechanisms/base", crit = Mechanisms.baseCriteria } });
        foreach (var s in lists)
            foreach (var c in s.crit)
            {
                Check(c.type == "deadline" || c.type == "maxCount", $"{s.id}/{c.id}: Typ {c.type}");
                Check(c.tags.Any(t => possible.Any(p => p.StartsWith(t, StringComparison.Ordinal))),
                      $"{s.id}/{c.id}: kein erreichbarer Tag ({string.Join(", ", c.tags)})");
                if (!string.IsNullOrEmpty(c.relativeTo))
                    Check(possible.Any(p => p.StartsWith(c.relativeTo, StringComparison.Ordinal)),
                          $"{s.id}/{c.id}: relativeTo {c.relativeTo} nie erzeugt");
            }
    }

    static void SourcesPresent()
    {
        foreach (var inj in Injuries.injuries)
        {
            Check(inj.sources != null && inj.sources.Length > 0, $"{inj.id}: keine Quelle");
            Check(new[] { "draft", "reviewed", "validated" }.Contains(inj.evidenceStatus), $"{inj.id}: Status {inj.evidenceStatus}");
        }
    }

    // ------------------------------------------------------------ Mock

    static InjuryEvent Bleed(string cmpt, double sev, double rate, double max) => new InjuryEvent
    { Effect = InjuryEffectType.Hemorrhage, Compartment = cmpt, Severity = sev, BaseRate_mL_min = rate, MaxTotalLoss_mL = max };

    static InjuryEvent Tension(double sev) => new InjuryEvent
    { Effect = InjuryEffectType.TensionPneumothorax, Compartment = "LeftLung", Severity = sev, ProgressionRate_per_min = 0.06 };

    static MockPhysiologyEngine PelvisPatient()
    {
        var e = new MockPhysiologyEngine(1);
        e.Initialize(new PatientProfile { weight_kg = 80 });
        e.ApplyInjury(Bleed("Pelvis", 0.8, 150, 3000));
        e.ApplyInjury(Bleed("RightThigh", 0.6, 20, 1000));
        return e;
    }

    static void Advance(IPhysiologyEngine e, double seconds) { for (double t = 0; t < seconds; t += 0.1) e.Advance(0.1); }

    static void PelvisUntreated()
    {
        var e = PelvisPatient();
        Advance(e, 30 * 60);
        Check(e.Current.BloodLossFraction >= 0.30, $"Verlust nach 30 min nur {e.Current.BloodLossFraction:P0}");
    }

    static void PelvisTreatedBetter()
    {
        var untreated = PelvisPatient(); Advance(untreated, 30 * 60);
        var treated = PelvisPatient();
        Advance(treated, 180);
        treated.ApplyIntervention(new InterventionEvent { Type = InterventionType.HemorrhageControl, TargetCompartment = "Pelvis", RateMultiplier = 0.5 });
        Advance(treated, 120);
        treated.ApplyIntervention(new InterventionEvent { Type = InterventionType.FluidInfusion, Compound = "Blood", Volume_mL = 600, Rate_mL_min = 50 });
        Advance(treated, 30 * 60 - 300);
        Check(treated.Current.BloodLossFraction < untreated.Current.BloodLossFraction - 0.1,
              $"behandelt {treated.Current.BloodLossFraction:P0} vs. unbehandelt {untreated.Current.BloodLossFraction:P0}");
        Check(treated.Current.SystolicBP_mmHg > untreated.Current.SystolicBP_mmHg, "Blutdruck nicht besser");
    }

    static void TensionCourse()
    {
        var e = new MockPhysiologyEngine(1);
        e.Initialize(new PatientProfile { weight_kg = 85 });
        e.ApplyInjury(Tension(0.8));
        double? hypoxia = null, hypotension = null, arrest = null;
        for (double t = 0; t < 40 * 60 && arrest == null; t += 0.1)
        {
            e.Advance(0.1);
            var v = e.Current;
            if (v.CardiacArrest) arrest = v.SimTime_s;
            else
            {
                if (hypoxia == null && v.SpO2_percent < 90) hypoxia = v.SimTime_s;
                if (hypotension == null && v.SystolicBP_mmHg < 90) hypotension = v.SimTime_s;
            }
        }
        Check(hypoxia != null && hypotension != null && hypoxia < hypotension, $"Hypoxie {hypoxia}s, Hypotonie {hypotension}s");
        Check(arrest != null && arrest > 15 * 60 && arrest < 30 * 60, $"Stillstand bei {arrest}s");
    }

    static void TensionDrain()
    {
        var e = new MockPhysiologyEngine(1);
        e.Initialize(new PatientProfile());
        e.ApplyInjury(Tension(0.8));
        Advance(e, 600);
        Check(e.Current.SpO2_percent < 90, "vor Drainage keine Hypoxie");
        e.ApplyIntervention(new InterventionEvent { Type = InterventionType.ChestDrain, TargetCompartment = "LeftLung" });
        Advance(e, 600);
        Check(e.Current.SpO2_percent >= 96 && !e.Current.CardiacArrest, $"nach Drainage SpO2 {e.Current.SpO2_percent:F0}");
    }

    static void NeedleFailureRate()
    {
        int fails = 0, n = 2000;
        for (int seed = 0; seed < n; seed++)
        {
            var e = new MockPhysiologyEngine(seed);
            e.Initialize(new PatientProfile());
            e.ApplyInjury(Tension(1));
            e.Advance(300);
            e.ApplyIntervention(new InterventionEvent { Type = InterventionType.NeedleDecompression, TargetCompartment = "LeftLung", SuccessProbability = 0.57 });
            e.Advance(0.1);
            if (e.Current.SpO2_percent < 90) fails++;
        }
        double rate = fails / (double)n;
        Check(Math.Abs(rate - 0.43) < 0.04, $"Versagerrate {rate:P1}");
    }

    // ------------------------------------------------------------ Bewertung

    static void AssessmentLogic()
    {
        var criteria = new[]
        {
            new AssessmentCriterion { id = "a", type = "deadline", tags = new[] { "intervention:ChestDrain" }, limit = 300, relativeTo = "injury:tp" },
            new AssessmentCriterion { id = "b", type = "deadline", tags = new[] { "exam:Thorax" }, limit = 60 },
            new AssessmentCriterion { id = "c", type = "maxCount", tags = new[] { "intervention:FluidInfusion:BalancedCrystalloid" }, limit = 2 },
            new AssessmentCriterion { id = "d", type = "deadline", tags = new[] { "x" }, limit = 10, relativeTo = "injury:nie" },
            new AssessmentCriterion { id = "e", type = "deadline", tags = new[] { "exam:Thorax" }, limit = 60, relativeTo = "injury:tp", onlyAfterReference = true },
            new AssessmentCriterion { id = "f", type = "deadline", tags = new[] { "exam:Thorax" }, limit = 60, relativeTo = "injury:tp" },
        };
        var events = new List<LogEntry>
        {
            new LogEntry { Time_s = 100, Tag = "injury:tp" },
            new LogEntry { Time_s = 50, Tag = "exam:Thorax" },
            new LogEntry { Time_s = 200, Tag = "exam:Thorax" },
            new LogEntry { Time_s = 350, Tag = "intervention:ChestDrain:LeftLung" },
            new LogEntry { Time_s = 10, Tag = "intervention:FluidInfusion:BalancedCrystalloid" },
            new LogEntry { Time_s = 20, Tag = "intervention:FluidInfusion:BalancedCrystalloid" },
            new LogEntry { Time_s = 30, Tag = "intervention:FluidInfusion:BalancedCrystalloid" },
        };
        var r = Assessment.Evaluate(criteria, events).ToDictionary(x => x.Criterion.id);
        Check(r["a"].Outcome == CriterionOutcome.Met && r["a"].Deadline_s == 400, $"a: {r["a"].Outcome}, Ziel {r["a"].Deadline_s}");
        Check(r["b"].Outcome == CriterionOutcome.Met, $"b: {r["b"].Outcome}");
        Check(r["e"].Outcome == CriterionOutcome.Late && r["e"].Time_s == 200, $"e: {r["e"].Outcome} bei {r["e"].Time_s}");
        Check(r["f"].Outcome == CriterionOutcome.Met && r["f"].Time_s == 50, $"f: {r["f"].Outcome} bei {r["f"].Time_s}");
        Check(r["c"].Outcome == CriterionOutcome.Exceeded && r["c"].Count == 3, $"c: {r["c"].Outcome}");
        Check(r["d"].Outcome == CriterionOutcome.NotApplicable, $"d: {r["d"].Outcome}");
    }

    // ------------------------------------------------------------ SHT und Zustände

    static InjuryEvent Tbi(double sev) => new InjuryEvent
    { Effect = InjuryEffectType.TraumaticBrainInjury, Compartment = "Brain", Severity = sev, ProgressionRate_per_min = 0.05 };

    static MockPhysiologyEngine TbiPatient()
    {
        var e = new MockPhysiologyEngine(1);
        e.Initialize(new PatientProfile { weight_kg = 78 });
        e.ApplyInjury(Tbi(0.9));
        return e;
    }

    /// <summary>Simuliert bis zum Stillstand oder Zeitende; optional Intubation automatisch bei GCS ≤ 8.</summary>
    static double? RunUntilArrest(MockPhysiologyEngine e, double maxSeconds, bool intubateAtGcs8 = false)
    {
        bool done = false;
        for (double t = 0; t < maxSeconds; t += 0.1)
        {
            e.Advance(0.1);
            if (intubateAtGcs8 && !done && e.Current.GCS <= 8)
            {
                e.ApplyIntervention(new InterventionEvent { Type = InterventionType.Intubation });
                done = true;
            }
            if (e.Current.CardiacArrest) return e.Current.SimTime_s;
        }
        return null;
    }

    static void TbiUntreated()
    {
        var e = TbiPatient();
        Advance(e, 300);
        Check(e.Current.GCS >= 14, $"GCS nach 5 min {e.Current.GCS}");
        bool anisoBeforeArrest = false;
        double? arrest = null;
        for (double t = 0; t < 40 * 60; t += 0.1)
        {
            e.Advance(0.1);
            if (e.Current.CardiacArrest) { arrest = e.Current.SimTime_s; break; }
            if (e.Current.Anisocoria) anisoBeforeArrest = true;
        }
        Check(anisoBeforeArrest, "keine Anisokorie vor der Einklemmung");
        Check(arrest != null && arrest > 15 * 60 && arrest < 30 * 60, $"Einklemmung bei {arrest}s");
    }

    static void TbiEarlySurgery()
    {
        var e = TbiPatient();
        e.ApplyIntervention(new InterventionEvent { Type = InterventionType.Oxygen });
        Advance(e, 13 * 60); // Anordnung nach 3 min + 10 min Verzögerung
        e.ApplyIntervention(new InterventionEvent { Type = InterventionType.NeurosurgicalDecompression });
        var arrest = RunUntilArrest(e, 40 * 60, intubateAtGcs8: true);
        Check(arrest == null, $"trotz Kraniotomie Stillstand bei {arrest}s");
        Check(e.Current.GCS >= 9, $"GCS nach OP {e.Current.GCS}");
    }

    static void TbiAirwayMatters()
    {
        var without = RunUntilArrest(TbiPatient(), 60 * 60);
        var with = RunUntilArrest(TbiPatient(), 60 * 60, intubateAtGcs8: true);
        Check(without != null && with != null && without < with - 60,
              $"ohne Atemwegssicherung {without}s, mit {with}s");
    }

    static void VentilationWorsensTension()
    {
        Func<bool, MockPhysiologyEngine> Make = intubate =>
        {
            var e = new MockPhysiologyEngine(1);
            e.Initialize(new PatientProfile());
            e.ApplyInjury(Tension(0.8));
            if (intubate) e.ApplyIntervention(new InterventionEvent { Type = InterventionType.Intubation });
            return e;
        };
        var spont = RunUntilArrest(Make(false), 60 * 60);
        var vent = RunUntilArrest(Make(true), 60 * 60);
        Check(spont != null && vent != null && vent < spont / 2, $"spontan {spont}s, beatmet {vent}s");
    }

    static void StateWatcherLogic()
    {
        var w = new StateWatcher();
        var tags = new List<string>();
        Action<double, double, double, bool> Step = (sbp, spo2, gcs, aniso) =>
            tags.AddRange(w.Check(new VitalSigns { SystolicBP_mmHg = sbp, SpO2_percent = spo2, GCS = gcs, Anisocoria = aniso }).Select(x => x.tag));
        Step(120, 97, 15, false);
        Step(85, 97, 15, false);    // Hypotonie 1
        Step(92, 97, 15, false);    // noch keine Erholung (Hysterese)
        Step(80, 97, 13, false);    // keine neue Episode, aber GCS <= 13
        Step(100, 97, 12, false);   // erholt
        Step(85, 88, 8, true);      // Hypotonie 2, Hypoxie, GCS 8, Anisokorie
        Step(85, 88, 7, true);
        Check(tags.Count(t => t == StateTags.Hypotension) == 2, "Hypotonie-Episoden: " + tags.Count(t => t == StateTags.Hypotension));
        Check(tags.Count(t => t == StateTags.Hypoxia) == 1, "Hypoxie-Episoden");
        Check(tags.Count(t => t == StateTags.GcsDrop) == 1 && tags.Count(t => t == StateTags.Gcs8) == 1, "GCS-Schwellen");
        Check(tags.Count(t => t == StateTags.Anisocoria) == 1, "Anisokorie");
    }

    static void InterventionDelays()
    {
        foreach (var iv in Interventions.interventions)
        {
            Check(iv.effectDelay_s >= 0, $"{iv.id}: negative Verzögerung");
            if (iv.type == "NeurosurgicalDecompression") Check(iv.effectDelay_s > 0, "Kraniotomie ohne Verzögerung");
        }
    }

    // ------------------------------------------------------------ Realitätsmodus

    static MockPhysiologyEngine Bleeding(PhysiologyMode mode, int seed, bool betaBlocker = false)
    {
        var e = new MockPhysiologyEngine(seed, mode);
        e.Initialize(new PatientProfile { weight_kg = 80, age = 40, betaBlocker = betaBlocker });
        e.ApplyInjury(Bleed("Pelvis", 1.0, 150, 0));
        return e;
    }

    static VitalSigns AtLoss(MockPhysiologyEngine e, double loss)
    {
        while (e.Current.BloodLossFraction < loss && !e.Current.CardiacArrest) e.Advance(0.5);
        return e.Current;
    }

    static void RegistryLessTachycardia()
    {
        var tb = AtLoss(Bleeding(PhysiologyMode.Textbook, 1), 0.35);
        double mean = Enumerable.Range(0, 50).Select(s => AtLoss(Bleeding(PhysiologyMode.Registry, s), 0.35).HeartRate_bpm).Average();
        Check(tb.HeartRate_bpm - mean > 25, $"Lehrbuch HF {tb.HeartRate_bpm:F0}, Register Ø {mean:F0}");
        Check(mean < 100, $"Register Ø HF {mean:F0} bei 35 % Verlust");
    }

    static void BaseExcessClasses()
    {
        Func<double, double> be = f => ShockTables.Interp(f, ShockTables.BaseExcess);
        Check(be(0.10) > -2, "Klasse I");
        Check(be(0.20) <= -2 && be(0.20) > -6, "Klasse II");
        Check(be(0.35) <= -6 && be(0.35) > -10, "Klasse III");
        Check(be(0.45) <= -10, "Klasse IV");
        var v = AtLoss(Bleeding(PhysiologyMode.Textbook, 1), 0.35);
        Check(v.BaseExcess <= -6 && v.Lactate > 2, $"Engine: BE {v.BaseExcess:F1}, Laktat {v.Lactate:F1}");
    }

    static void RegistryVariability()
    {
        var reg = Enumerable.Range(0, 20).Select(s => Bleeding(PhysiologyMode.Registry, s).Current.HeartRate_bpm).Distinct().Count();
        var tb = Enumerable.Range(0, 20).Select(s => Bleeding(PhysiologyMode.Textbook, s).Current.HeartRate_bpm).Distinct().Count();
        Check(reg > 10, $"Register: nur {reg} verschiedene Ausgangs-HF");
        Check(tb == 1, $"Lehrbuch: {tb} verschiedene Ausgangs-HF");
        var a = Bleeding(PhysiologyMode.Registry, 7).Current.HeartRate_bpm;
        var b = Bleeding(PhysiologyMode.Registry, 7).Current.HeartRate_bpm;
        Check(a == b, "gleicher Seed, unterschiedliche Werte");
    }

    static void BetaBlockerDampens()
    {
        var normal = Bleeding(PhysiologyMode.Textbook, 1);
        double hr0 = normal.Current.HeartRate_bpm, rise = AtLoss(normal, 0.30).HeartRate_bpm - hr0;
        var bb = Bleeding(PhysiologyMode.Textbook, 1, betaBlocker: true);
        double hrB0 = bb.Current.HeartRate_bpm, riseB = AtLoss(bb, 0.30).HeartRate_bpm - hrB0;
        Check(riseB < rise * 0.3, $"Anstieg ohne {rise:F0}, mit Betablocker {riseB:F0}");
        Check(bb.Current.BaseExcess <= -5, "BE trotz Betablocker nicht pathologisch");
    }

    static void DebriefComparison()
    {
        var log = new SessionLog { Mode = PhysiologyMode.Registry };
        log.AddSample(new VitalSigns { SimTime_s = 600, BloodLossFraction = 0.35, HeartRate_bpm = 95, SystolicBP_mmHg = 112, GCS = 15 });
        string text = log.BuildDebrief("Test");
        Check(text.Contains("Lehrbuch vs. Realität") && text.Contains("Modus: Realität"), "Vergleich fehlt");
        var small = new SessionLog();
        small.AddSample(new VitalSigns { SimTime_s = 60, BloodLossFraction = 0.05, GCS = 15 });
        Check(!small.BuildDebrief("Test").Contains("Lehrbuch vs. Realität"), "Vergleich trotz Klasse I");
    }

    // ------------------------------------------------------------ Bildgebung

    static void ImagingFindingsCovered()
    {
        var covered = new HashSet<string>(Imaging.modalities.SelectMany(m => m.covers));
        foreach (var inj in Injuries.injuries)
            foreach (var f in inj.imaging)
            {
                Check(covered.Contains(f.modality), $"{inj.id}: Kategorie {f.modality} von keinem Verfahren abgedeckt");
                Check(!string.IsNullOrEmpty(f.finding), $"{inj.id}/{f.modality}: leerer Befund");
            }
        foreach (var m in Imaging.modalities)
            Check(!string.IsNullOrEmpty(m.normalFinding) && m.duration_s > 0 && m.transport_s >= 0, $"{m.id}: unvollständig");
    }

    static void ImagingReportLogic()
    {
        var mods = Imaging.modalities.ToDictionary(m => m.id);
        var inj = Injuries.injuries.ToDictionary(i => i.id);
        var none = ImagingReport.Build(mods["rx_thorax"], new InjuryDefinition[0], new string[0]);
        Check(none.Count == 1 && none[0] == mods["rx_thorax"].normalFinding, "Normalbefund");
        var tp = ImagingReport.Build(mods["rx_thorax"], new[] { inj["spannungspneu_links"] }, new string[0]);
        Check(tp.Count == 1 && tp[0].Contains("Pneumothorax links"), "Spannungspneu im Röntgen: " + string.Join(" ", tp));
        var after = ImagingReport.Build(mods["rx_thorax"], new[] { inj["spannungspneu_links"] }, new[] { "intervention:ChestDrain:LeftLung:drainage_li" });
        Check(after[0].Contains("Thoraxdrainage"), "nach Drainage: " + after[0]);
        var ct = ImagingReport.Build(mods["ct_ganzkoerper"], new[] { inj["becken_instabil"], inj["epiduralhaematom_rechts"] }, new string[0]);
        Check(ct.Count == 2, "Ganzkörper-CT sollte Becken und Kopf zeigen: " + ct.Count);
        var cct = ImagingReport.Build(mods["cct"], new[] { inj["becken_instabil"] }, new string[0]);
        Check(cct[0] == mods["cct"].normalFinding, "CCT zeigt keine Beckenverletzung");
    }

    static void DistinctPelvisTags()
    {
        var tags = Interventions.interventions.Select(d => d.ToEvent()).Where(e => e.TargetCompartment == "Pelvis")
            .Select(e => $"intervention:{e.Type}:{e.TargetCompartment}:{e.Id}").ToList();
        var crit = Scenarios.SelectMany(s => s.assessment).Where(c => c.id == "beckengurt").SelectMany(c => c.tags).Distinct().ToList();
        foreach (var c in crit)
            Check(tags.Count(x => x.StartsWith(c, StringComparison.Ordinal)) == 1, $"Tag {c} trifft {tags.Count(x => x.StartsWith(c, StringComparison.Ordinal))} Maßnahmen");
    }

    // ------------------------------------------------------------ BGA

    static void BgaBaseline()
    {
        var e = new MockPhysiologyEngine(1); e.Initialize(new PatientProfile { weight_kg = 80 });
        var v = e.Current;
        Check(Math.Abs(v.pH - 7.40) < 0.01 && Math.Abs(v.PaCO2 - 40) < 0.5 && Math.Abs(v.HCO3 - 24) < 0.1, $"pH {v.pH:F2}, pCO2 {v.PaCO2:F0}, HCO3 {v.HCO3:F1}");
        Check(Math.Abs(v.Hb - 14.5) < 0.05 && Math.Abs(v.IonizedCalcium - 1.20) < 0.001 && Math.Abs(v.Potassium - 4.0) < 0.05, $"Hb {v.Hb:F1}, Ca {v.IonizedCalcium:F2}, K {v.Potassium:F1}");
        var w = new MockPhysiologyEngine(1); w.Initialize(new PatientProfile { weight_kg = 60, sex = "female" });
        Check(Math.Abs(w.Current.Hb - 13.0) < 0.05, $"Hb Frau {w.Current.Hb:F1}");
        Check(BloodGas.Report(v).Contains("pH 7,40") && BloodGas.Report(v).Contains("Hb 14,5"), BloodGas.Report(v));
    }

    static void BgaHemoglobin()
    {
        var e = Bleeding(PhysiologyMode.Textbook, 1);
        double hb0 = e.Current.Hb;
        var v = AtLoss(e, 0.35);
        Check(Math.Abs(v.Hb - hb0) < 0.1, $"Hb nach 35 % Verlust {v.Hb:F1} statt {hb0:F1}");
        var c = new MockPhysiologyEngine(1); c.Initialize(new PatientProfile { weight_kg = 80 });
        c.ApplyIntervention(new InterventionEvent { Type = InterventionType.FluidInfusion, Compound = "BalancedCrystalloid", Volume_mL = 1000, Rate_mL_min = 100 });
        Advance(c, 900);
        double diluted = c.Current.Hb;
        Check(diluted < hb0 - 0.3, $"Verdünnung: {diluted:F1}");
        c.ApplyIntervention(new InterventionEvent { Type = InterventionType.FluidInfusion, Compound = "Blood", Volume_mL = 600, Rate_mL_min = 50 });
        Advance(c, 900);
        Check(c.Current.Hb > diluted, $"nach 2 EK {c.Current.Hb:F1} (vorher {diluted:F1})");
        Check(c.Current.IonizedCalcium < 1.10, $"Ca++ nach 2 EK {c.Current.IonizedCalcium:F2}");
    }

    static void BgaAcidBase()
    {
        var v = AtLoss(Bleeding(PhysiologyMode.Textbook, 1), 0.35);
        Check(v.pH < 7.35 && v.HCO3 < 20 && v.PaCO2 < 40, $"Schock: pH {v.pH:F2}, HCO3 {v.HCO3:F1}, pCO2 {v.PaCO2:F0}");
        Check(v.Potassium > 4.2, $"K {v.Potassium:F1}");
        var t = TbiPatient();
        while (t.Current.GCS > 6 && !t.Current.CardiacArrest) t.Advance(0.5);
        Check(t.Current.PaCO2 > 45, $"SHT ohne Atemwegssicherung: pCO2 {t.Current.PaCO2:F0}");
        t.ApplyIntervention(new InterventionEvent { Type = InterventionType.Intubation });
        t.Advance(0.1);
        Check(Math.Abs(t.Current.PaCO2 - 38) < 0.5 && t.Current.PaO2 > 200, $"beatmet: pCO2 {t.Current.PaCO2:F0}, pO2 {t.Current.PaO2:F0}");
    }

    // ------------------------------------------------------------ Polytrauma

    static void VasopressorTradeOff()
    {
        Func<bool, MockPhysiologyEngine> make = na =>
        {
            var e = Bleeding(PhysiologyMode.Textbook, 1);
            Advance(e, 300);
            if (na) e.ApplyIntervention(new InterventionEvent { Type = InterventionType.Vasopressor });
            e.Advance(0.1);
            return e;
        };
        var without = make(false); var with = make(true);
        Check(with.Current.MeanArterialPressure_mmHg > without.Current.MeanArterialPressure_mmHg + 10, "MAD steigt nicht");
        Advance(without, 300); Advance(with, 300);
        Check(with.Current.BloodLossFraction > without.Current.BloodLossFraction, $"Verlust mit {with.Current.BloodLossFraction:P1}, ohne {without.Current.BloodLossFraction:P1}");
    }

    static void PolytraumaArrival()
    {
        var e = new MockPhysiologyEngine(1);
        e.Initialize(new PatientProfile { weight_kg = 85, age = 42, initialBloodLoss_mL = 1500 });
        Check(e.Current.BloodLossFraction > 0.24 && e.Current.BloodLossFraction < 0.26, $"Verlust bei Ankunft {e.Current.BloodLossFraction:P0}");
        Check(Math.Abs(e.Current.Hb - 14.5) < 0.05 && e.Current.BaseExcess < -3, $"Hb {e.Current.Hb:F1}, BE {e.Current.BaseExcess:F1}");
        e.ApplyInjury(new InjuryEvent { Effect = InjuryEffectType.TraumaticBrainInjury, Compartment = "Brain", Severity = 1, ProgressionRate_per_min = 0.018, InitialLevel = 0.45 });
        e.Advance(0.1);
        Check(e.Current.GCS == 12, $"GCS bei Ankunft {e.Current.GCS}");
        var s = Scenarios.First(x => x.id == "pkw_polytrauma");
        Check(s.patient.initialBloodLoss_mL > 0 && s.injuries.Length >= 3, "Szenario unvollständig");
    }

    static void MapLowTbiState()
    {
        var w = new StateWatcher();
        Func<double, double, int> count = (map, gcs) => w.Check(new VitalSigns { SystolicBP_mmHg = 120, MeanArterialPressure_mmHg = map, SpO2_percent = 97, GCS = gcs })
            .Count(x => x.tag == StateTags.MapLowTbi);
        Check(count(70, 12) == 0, "ohne schweres SHT ausgelöst");
        Check(count(70, 7) == 1, "bei GCS 7 und MAD 70 nicht ausgelöst");
        Check(count(78, 7) == 0, "doppelt ausgelöst");
        Check(count(86, 7) == 0 && count(75, 7) == 1, "Hysterese");
    }

    // ------------------------------------------------------------ Unfallmechanismen

    static void MultiTargetControl()
    {
        var e = new MockPhysiologyEngine(1);
        e.Initialize(new PatientProfile { weight_kg = 80 });
        e.ApplyInjury(new InjuryEvent { Effect = InjuryEffectType.Hemorrhage, Compartment = "Spleen", Severity = 1, BaseRate_mL_min = 100, MaxTotalLoss_mL = 2500 });
        e.ApplyInjury(new InjuryEvent { Effect = InjuryEffectType.Hemorrhage, Compartment = "Liver", Severity = 1, BaseRate_mL_min = 90, MaxTotalLoss_mL = 2500 });
        Advance(e, 60);
        var lap = Interventions.interventions.First(i => i.id == "laparotomie").ToEvent();
        e.ApplyIntervention(lap);
        double before = e.Current.BloodVolume_mL;
        Advance(e, 60);
        Check(before - e.Current.BloodVolume_mL < 15, $"Verlust nach Laparotomie {before - e.Current.BloodVolume_mL:F0} mL/min");
    }

    static void GeneratorConsistency()
    {
        var db = Mechanisms; var inj = Injuries.injuries;
        var ids = new HashSet<string>(inj.Select(i => i.id));
        foreach (var m in db.mechanisms)
        {
            foreach (var c in m.injuries) Check(ids.Contains(c.injuryId), $"{m.id}: unbekannte Verletzung {c.injuryId}");
            Check(m.sources.Length > 0, $"{m.id}: keine Quelle");
            for (uint seed = 1; seed <= 300; seed++)
            {
                var a = ScenarioGenerator.Generate(db, m.id, inj, seed);
                var b = ScenarioGenerator.Generate(db, m.id, inj, seed);
                Check(a.description == b.description && a.injuries.Length == b.injuries.Length, $"{m.id}/{seed}: nicht deterministisch");
                Check(a.injuries.Any(si => inj.First(d => d.id == si.injuryId).effects.Length > 0), $"{m.id}/{seed}: ohne relevante Verletzung");
                foreach (var g in db.exclusiveGroups)
                    Check(a.injuries.Count(si => g.injuryIds.Contains(si.injuryId)) <= 1, $"{m.id}/{seed}: Ausschlussgruppe verletzt");
                Check(a.assessment.Select(c => c.id).Distinct().Count() == a.assessment.Length, $"{m.id}/{seed}: doppelte Kriterien");
                Check(a.patient.initialBloodLoss_mL >= 0 && a.patient.initialBloodLoss_mL <= db.prehospital.maxLoss_mL, $"{m.id}/{seed}: Verlust {a.patient.initialBloodLoss_mL}");
                Check(!a.description.Contains("{"), $"{m.id}: Platzhalter übrig: {a.description}");
            }
        }
    }

    static void GeneratorDistribution()
    {
        var db = Mechanisms; var inj = Injuries.injuries;
        var m = db.mechanisms.First(x => x.id == "motorrad"); // keine Gurt-Multiplikatoren
        const int n = 2000;
        var count = new Dictionary<string, int>();
        for (uint seed = 1; seed <= n; seed++)
            foreach (var si in ScenarioGenerator.Generate(db, m.id, inj, seed).injuries)
                count[si.injuryId] = (count.TryGetValue(si.injuryId, out var c) ? c : 0) + 1;
        // Szenarien ohne relevante Verletzung werden neu gezogen: P(X | nicht leer) = p / (1 − P(leer)).
        Func<string, bool> relevant = id => inj.First(d => d.id == id).effects.Length > 0;
        double pEmpty = m.injuries.Where(x => relevant(x.injuryId)).Aggregate(1.0, (acc, x) => acc * (1 - x.probability));
        foreach (var c in m.injuries.Where(x => !db.exclusiveGroups.Any(g => g.injuryIds.Contains(x.injuryId))))
        {
            double f = (count.TryGetValue(c.injuryId, out var k) ? k : 0) / (double)n;
            double expected = relevant(c.injuryId) ? c.probability / (1 - pEmpty) : c.probability;
            Check(Math.Abs(f - expected) < 0.04, $"{c.injuryId}: {f:P0} statt {expected:P0}");
        }
    }

    /// <summary>Spielt ein generiertes Szenario mit einer Musterversorgung (oder ohne) durch. Rückgabe: überlebt?</summary>
    static bool PlayGenerated(ScenarioDefinition s, bool treat, out string why)
    {
        var ivs = Interventions.interventions.ToDictionary(i => i.id);
        var e = new MockPhysiologyEngine(7);
        e.Initialize(s.patient);
        var has = new HashSet<string>(s.injuries.Select(i => i.injuryId));
        var pending = s.injuries.OrderBy(i => i.onsetTime_s).ToList();
        var defs = Injuries.injuries.ToDictionary(d => d.id);
        var plan = new List<KeyValuePair<double, string>>();
        Action<double, string> at = (t, id) => plan.Add(new KeyValuePair<double, string>(t, id));
        bool bleed = s.injuries.Any(si => defs[si.injuryId].effects.Any(x => x.effectType == "Hemorrhage"));
        bool tbi = has.Contains("sdh_akut_links") || has.Contains("epiduralhaematom_rechts");
        if (treat)
        {
            if (has.Contains("unterschenkel_offen_rechts")) at(20, "tourniquet");
            at(30, "o2");
            if (has.Contains("becken_instabil")) { at(60, "beckengurt"); at(600, "angio_op"); }
            if (has.Contains("femur_schaft_rechts")) at(90, "femur_schiene");
            if (bleed) { at(90, "txa"); for (int k = 0; k < 5; k++) at(90 + 360 * k, "ek"); }
            if (has.Contains("milzruptur") || has.Contains("leberlazeration")) at(300, "laparotomie");
            var tp = s.injuries.FirstOrDefault(i => i.injuryId == "spannungspneu_links");
            if (tp != null) at(tp.onsetTime_s + 90, "drainage_li");
            if (tbi) at(480, "kraniotomie");
        }
        plan.Sort((x, y) => x.Key.CompareTo(y.Key));
        var delayed = new List<KeyValuePair<double, InterventionEvent>>();
        Action<string, double> apply = (id, now) =>
        {
            var d = ivs[id];
            if (d.effectDelay_s > 0) delayed.Add(new KeyValuePair<double, InterventionEvent>(now + d.effectDelay_s, d.ToEvent()));
            else e.ApplyIntervention(d.ToEvent());
        };
        bool intubated = false, pressor = false;
        int next = 0;
        for (double t = 0; t < 2700; t += 0.1)
        {
            while (pending.Count > 0 && pending[0].onsetTime_s <= t)
            {
                var si = pending[0]; pending.RemoveAt(0);
                foreach (var ev in ToEvents(defs[si.injuryId], si)) e.ApplyInjury(ev);
            }
            while (next < plan.Count && plan[next].Key <= t) apply(plan[next++].Value, t);
            for (int k = delayed.Count - 1; k >= 0; k--)
                if (delayed[k].Key <= t) { e.ApplyIntervention(delayed[k].Value); delayed.RemoveAt(k); }
            if (treat && !intubated && e.Current.GCS <= 8 && t > 1) { apply("intubation", t); intubated = true; }
            if (treat && intubated && !pressor && e.Current.MeanArterialPressure_mmHg < 80) { apply("noradrenalin", t); pressor = true; }
            e.Advance(0.1);
            if (e.Current.CardiacArrest)
            {
                why = $"Stillstand nach {t / 60:F0} min, Verlust {e.Current.BloodLossFraction:P0}, GCS {e.Current.GCS}";
                return false;
            }
        }
        why = "";
        return true;
    }

    static IEnumerable<InjuryEvent> ToEvents(InjuryDefinition def, ScenarioInjury si)
    {
        foreach (var x in def.effects)
            yield return new InjuryEvent
            {
                InjuryId = def.id, Effect = (InjuryEffectType)Enum.Parse(typeof(InjuryEffectType), x.effectType),
                Compartment = x.compartment, Severity = si.severity, BaseRate_mL_min = x.baseRate_mL_min,
                MaxTotalLoss_mL = x.maxTotalLoss_mL, ProgressionRate_per_min = x.progressionRate_per_min, InitialLevel = x.initialLevel
            };
    }

    static void GeneratorPlayable()
    {
        var db = Mechanisms; var inj = Injuries.injuries;
        foreach (var m in db.mechanisms)
        {
            int n = 40, survivedTreated = 0, survivedUntreated = 0;
            var fails = new List<string>();
            for (uint seed = 1; seed <= n; seed++)
            {
                var s = ScenarioGenerator.Generate(db, m.id, inj, seed);
                string why;
                if (PlayGenerated(s, true, out why)) survivedTreated++;
                else fails.Add($"{seed} [{string.Join(",", s.injuries.Select(i => i.injuryId + ":" + i.severity))}; {s.patient.initialBloodLoss_mL} mL] {why}");
                if (PlayGenerated(s, false, out why)) survivedUntreated++;
            }
            Console.WriteLine($"        {m.id}: behandelt {survivedTreated}/{n}, unbehandelt {survivedUntreated}/{n} überleben" + (fails.Count > 0 ? " — z. B. " + fails[0] : ""));
            Check(survivedTreated >= n * 0.9, $"{m.id}: nur {survivedTreated}/{n} überleben mit Versorgung. " + string.Join(" | ", fails.Take(3)));
            Check(survivedUntreated <= n * 0.7, $"{m.id}: {survivedUntreated}/{n} überleben ohne Versorgung — zu harmlos");
        }
    }

    // ------------------------------------------------------------ Diagnose-Modus

    static FindingsDatabaseFile Findings => Load<FindingsDatabaseFile>("findings.json");

    static void ExaminationFindings()
    {
        var defs = Injuries.injuries.ToDictionary(d => d.id);
        var sc = new ScenarioDefinition { injuries = new[] {
            new ScenarioInjury { injuryId = "becken_instabil", severity = 1 },
            new ScenarioInjury { injuryId = "spannungspneu_links", severity = 1, onsetTime_s = 300 } } };
        var normal = Findings;
        Func<double, ExamContext> ctx = t => new ExamContext { Time_s = t, Events = new List<LogEntry>() };
        var becken = Examination.Findings("Becken", sc, id => defs[id], normal, ctx(0));
        Check(becken.Contains("Beinlängendifferenz und Außenrotation") && becken.Any(x => x.StartsWith("Instabilität")), "Becken zeigt nicht die Verletzungsbefunde");
        var thoraxEarly = Examination.Findings("Thorax", sc, id => defs[id], normal, ctx(100));
        Check(thoraxEarly.SequenceEqual(normal.regions.First(r => r.region == "Thorax").normal), "Thorax vor Beginn nicht normal");
        Check(!Examination.HasInjury("Thorax", sc, 100, id => defs[id]) && Examination.HasInjury("Thorax", sc, 300, id => defs[id]), "HasInjury");
        Check(Examination.Findings("Thorax", sc, id => defs[id], normal, ctx(300)).Contains("Hypersonorer Klopfschall links"), "Thorax nach Beginn ohne Befund");
    }

    /// <summary>Spannungspneu: Spätzeichen erst spät, Halsvenen bei Hypovolämie flach, nach Drainage andere Befunde.</summary>
    static void DynamicFindings()
    {
        var defs = Injuries.injuries.ToDictionary(d => d.id);
        var tpDef = defs["spannungspneu_links"];
        var sc = new ScenarioDefinition { injuries = new[] { new ScenarioInjury { injuryId = tpDef.id, severity = 0.8 } } };
        var e = new MockPhysiologyEngine(1);
        e.Initialize(new PatientProfile { weight_kg = 80 });
        foreach (var ev in ToEvents(tpDef, sc.injuries[0])) e.ApplyInjury(ev);
        var log = new List<LogEntry>();
        Func<List<string>> exam = () => Examination.Findings("Thorax", sc, id => defs[id], Findings, new ExamContext
        {
            Time_s = e.Current.SimTime_s, BodyLossFraction = e.Current.BloodLossFraction,
            Level = e.InjuryLevel, Lost_mL = e.InjuryBloodLoss_mL, Events = log
        });
        e.Advance(0.1);
        var early = exam();
        Check(early.Contains("Hypersonorer Klopfschall links") && !early.Any(x => x.StartsWith("Trachea")) && !early.Contains("Gestaute Halsvenen"), "früh: " + string.Join(" | ", early));
        Advance(e, 1200);
        var late = exam();
        Check(e.InjuryLevel(tpDef.id) > 0.8 && late.Any(x => x.StartsWith("Trachea")) && late.Contains("Gestaute Halsvenen"), $"spät (Level {e.InjuryLevel(tpDef.id):F2}): " + string.Join(" | ", late));
        log.Add(new LogEntry { Time_s = e.Current.SimTime_s, Tag = "intervention:ChestDrain:LeftLung:drainage_li" });
        e.ApplyIntervention(Interventions.interventions.First(i => i.id == "drainage_li").ToEvent());
        e.Advance(0.1);
        var drained = exam();
        Check(drained.Count == 1 && drained[0].StartsWith("Thoraxdrainage links liegt"), "nach Drainage: " + string.Join(" | ", drained));

        // Hypovolämie: Halsvenen flach statt gestaut
        var flat = Examination.Findings("Thorax", sc, id => defs[id], Findings, new ExamContext
            { Time_s = 1200, BodyLossFraction = 0.3, Level = id => 0.9, Events = new List<LogEntry>() });
        Check(flat.Contains("Halsvenen flach") && !flat.Contains("Gestaute Halsvenen"), "Hypovolämie");
        // Blutung: Distension erst ab Verlust, OP-Befund erst nach Wirkzeit
        var mz = new ScenarioDefinition { injuries = new[] { new ScenarioInjury { injuryId = "milzruptur", severity = 1 } } };
        var lap = new List<LogEntry> { new LogEntry { Time_s = 100, Tag = "intervention:HemorrhageControl:Spleen,Liver:laparotomie" } };
        Func<double, double, List<string>> abd = (t, lost) => Examination.Findings("Abdomen", mz, id => defs[id], Findings,
            new ExamContext { Time_s = t, Lost_mL = id => lost, Events = lap });
        Check(!abd(200, 300).Contains("Zunehmende Distension des Abdomens") && abd(200, 900).Contains("Zunehmende Distension des Abdomens"), "Distension");
        Check(!abd(500, 0).Any(x => x.StartsWith("Z. n.")) && abd(1000, 0).Any(x => x.StartsWith("Z. n.")), "OP-Befund");
    }

    static void DynamicFindingsValid()
    {
        var tags = new HashSet<string>();
        foreach (var iv in Interventions.interventions)
        {
            var ev = iv.ToEvent();
            tags.Add($"intervention:{ev.Type}:{ev.TargetCompartment ?? ev.Compound ?? ""}:{ev.Id}");
        }
        foreach (var inj in Injuries.injuries)
            foreach (var d in inj.dynamicFindings)
            {
                Check(!string.IsNullOrEmpty(d.text) && d.minLevel <= d.maxLevel && d.minBodyLoss <= d.maxBodyLoss, $"{inj.id}: ungültige Bedingung '{d.text}'");
                foreach (var t in new[] { d.requiresTag, d.untilTag }.Where(x => !string.IsNullOrEmpty(x)))
                    Check(tags.Any(x => x.StartsWith(t, StringComparison.Ordinal)), $"{inj.id}: Tag {t} wird nie erzeugt");
            }
    }

    static void NormalFindingsComplete()
    {
        var regions = new HashSet<string>(Findings.regions.Select(r => r.region));
        foreach (var inj in Injuries.injuries) Check(regions.Contains(inj.region), $"kein Normalbefund für {inj.region}");
        foreach (var r in new[] { "Kopf", "Thorax", "Abdomen", "Becken", "Oberschenkel rechts", "Oberschenkel links", "Unterschenkel rechts", "Unterschenkel links", "Arm rechts", "Arm links" })
            Check(regions.Contains(r), $"kein Normalbefund für {r}");
        Check(Findings.regions.All(r => r.normal.Length > 0), "leerer Normalbefund");
    }

    static void DiagnosisLogic()
    {
        var defs = Injuries.injuries.ToDictionary(d => d.id);
        var actual = new[] { new ScenarioInjury { injuryId = "milzruptur" }, new ScenarioInjury { injuryId = "becken_instabil" }, new ScenarioInjury { injuryId = "unterschenkel_offen_rechts" } };
        Func<double, string, LogEntry> ev = (t, tag) => new LogEntry { Time_s = t, Message = tag, Tag = tag };
        var events = new List<LogEntry> {
            ev(10, "exam:Abdomen"), ev(20, "diagnosis:milzruptur"),
            ev(30, "diagnosis:leberlazeration"),
            ev(40, "diagnosis:becken_instabil"), ev(50, "diagnosis-retracted:becken_instabil"),
            ev(60, "diagnosis:spannungspneu_links"), ev(70, "diagnosis-retracted:spannungspneu_links"),
            ev(80, "result:Imaging:efast") };
        var r = DiagnosisEvaluation.Evaluate(actual, events, id => defs[id], Imaging.modalities).ToDictionary(x => x.InjuryId);
        Check(r.Count == 4, $"{r.Count} Ergebnisse statt 4");
        Check(r["milzruptur"].Outcome == DiagnosisOutcome.Correct && r["milzruptur"].Time_s == 20 && r["milzruptur"].Examined && r["milzruptur"].ImagingShown, "Milz");
        Check(r["becken_instabil"].Outcome == DiagnosisOutcome.Missed && !r["becken_instabil"].Examined && r["becken_instabil"].ImagingShown, "Becken (verworfen = übersehen, eFAST zeigt es)");
        Check(r["unterschenkel_offen_rechts"].Outcome == DiagnosisOutcome.Missed && r["unterschenkel_offen_rechts"].ClinicalOnly, "Unterschenkel nur klinisch");
        Check(r["leberlazeration"].Outcome == DiagnosisOutcome.FalsePositive, "Leber falscher Verdacht");
        Check(!r.ContainsKey("spannungspneu_links"), "verworfener Fehlverdacht zählt");
    }

    static void Differentials()
    {
        var diffs = Load<DifferentialDatabaseFile>("differentials.json");
        var defs = Injuries.injuries.ToDictionary(d => d.id);
        var ids = diffs.differentials.Select(d => d.id).Concat(defs.Keys).ToList();
        Check(ids.Count == ids.Distinct().Count(), "ID doppelt zwischen Verletzungen und Differenzialdiagnosen");
        foreach (var d in diffs.differentials)
        {
            Check(d.against.Any(a => string.IsNullOrEmpty(a.injuryId)), $"{d.id}: kein allgemeines Gegenargument");
            foreach (var a in d.against.Where(a => !string.IsNullOrEmpty(a.injuryId)))
                Check(defs.ContainsKey(a.injuryId), $"{d.id}: unbekannte Verletzung {a.injuryId}");
            Check(d.sources.Length > 0, $"{d.id}: keine Quelle");
        }
        var actual = new[] { new ScenarioInjury { injuryId = "spannungspneu_links" } };
        var events = new List<LogEntry> { new LogEntry { Time_s = 5, Tag = "diagnosis:haematothorax_links" }, new LogEntry { Time_s = 6, Tag = "diagnosis:hohlorganverletzung" },
                                          new LogEntry { Time_s = 7, Tag = "diagnosis:milzruptur" } };
        var r = DiagnosisEvaluation.Evaluate(actual, events, id => defs[id], Imaging.modalities, diffs).ToDictionary(x => x.InjuryId);
        Check(r["haematothorax_links"].IsDifferential && r["haematothorax_links"].Against.Length == 1 && r["haematothorax_links"].Against[0].Contains("hypersonor"), "spezifisches Argument fehlt");
        Check(r["hohlorganverletzung"].Against[0].Contains("Gurtzeichen"), "allgemeines Argument fehlt");
        Check(!r["milzruptur"].IsDifferential && r["milzruptur"].Against[0].StartsWith("Typische Befunde fehlten"), "Fehlverdacht auf Verletzung ohne Hinweis");
    }

    // ------------------------------------------------------------ Helfer

    class CheckFailed : Exception { public CheckFailed(string m) : base(m) { } }

    static void Check(bool ok, string message) { if (!ok) throw new CheckFailed(message); }

    static void Run(string name, Action test)
    {
        try { test(); _passed++; Console.WriteLine($"  ok    {name}"); }
        catch (CheckFailed e) { _failed++; Console.WriteLine($"  FAIL  {name}: {e.Message}"); }
        catch (Exception e) { _failed++; Console.WriteLine($"  FAIL  {name}: {e.GetType().Name}: {e.Message}"); }
    }
}
