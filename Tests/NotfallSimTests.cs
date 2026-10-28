// Testsuite außerhalb von Unity. Prüft Datenkonsistenz, Mock-Physiologie und Bewertung.
// Ausführen: Tests/run_tests.sh (braucht mono bzw. mcs). Läuft auch in GitHub Actions.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Text;
using NotfallSIM.Anatomy;
using NotfallSIM.Core;
using NotfallSIM.Crash;
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
        Run("Anatomie: body.bin vollständig lesbar, Skelett plausibel", AnatomyFileReads);
        Run("Gewebe: jedes Anatomieteil zugeordnet, jede Zahl mit Quelle, Werte plausibel", TissueData);
        Run("Biomechanik: HIC und 3-ms-Clip an bekannten Pulsen", BiomechanicsMath);
        Run("Risikokurven: HIC 700/1000, log-logistisch bei x50", RiskCurves);
        Run("Crash: Risikodaten konsistent", RiskDataConsistent);
        Run("Biomechanik: CFC-Filter (SAE J211), Viscous Criterion, Nij", FiltersAndCriteria);
        Run("Kalibrierung: Faktoren aus Referenz, begrenzt, angewandt", CalibrationLogic);
        Run("Organmodell: Versagensdehnung je Dehnrate, Seite, Kontrollfälle Viano/Hardy, Daten konsistent", OrganModel);
        Run("Crash: harter Aufprall verletzt, leichter nicht, Seite bestimmt Milz/Leber", CrashScenario);
        Run("Muskeltonus: Daten mit Quellen, Aktivierung je Zustand, Kniemoment plausibel", MuscleToneData);
        Run("Verletzungszeichen: Ort an der Haut, Region, Befund, Schwellungsvolumen, Amputation, Blutlache", InjuryVisualsData);

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
            if (has.Contains("unterschenkel_offen_rechts") || has.Contains("amputation_unterschenkel_rechts")) at(20, "tourniquet");
            if (has.Contains("schnittverletzung_unterarm_links")) at(40, "druckverband_arm_li");
            at(30, "o2");
            if (has.Contains("becken_instabil")) { at(60, "beckengurt"); at(600, "angio_op"); }
            if (has.Contains("femur_schaft_rechts")) at(90, "femur_schiene");
            if (bleed) { at(90, "txa"); for (int k = 0; k < 5; k++) at(90 + 360 * k, "ek"); }
            if (has.Contains("milzruptur") || has.Contains("leberlazeration") || has.Contains("stichverletzung_abdomen_links")) at(300, "laparotomie");
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

    // ------------------------------------------------------------ Unfall-Sandbox

    static InjuryRiskDatabaseFile Risk => Load<InjuryRiskDatabaseFile>("injury_risk.json");

    static void AnatomyFileReads()
    {
        var bytes = File.ReadAllBytes(Path.Combine(DataRoot, "anatomy", "body.bin"));
        var data = AnatomyFile.Read(bytes, json =>
        {
            var ser = new DataContractJsonSerializer(typeof(AnatomyHeader));
            using (var st = new MemoryStream(Encoding.UTF8.GetBytes(json))) return (AnatomyHeader)ser.ReadObject(st);
        });
        Check(data.Meshes.Count > 300, $"{data.Meshes.Count} Meshes");
        var skin = data.Meshes[0];
        Check(skin.Info.layer == "skin" && skin.Bones != null && skin.BlendShapes.Count == 3, "Haut ohne Gewichte/Blendshapes");
        Check(skin.Info.submeshes.Sum() == skin.Indices.Length, "Teilmeshes passen nicht");
        foreach (var m in data.Meshes)
        {
            Check(m.Indices.All(i => i >= 0 && i < m.Info.vertexCount), m.Info.name + ": Index außerhalb");
            Check(m.Info.segment < data.Header.segments.Length, m.Info.name + ": Segment");
        }
        Check(skin.Bones.All(b => b < data.Header.segments.Length), "Knochenindex außerhalb");
        Check(data.Header.modelHeight > 1.5f && data.Header.modelHeight < 1.9f, $"Modellhöhe {data.Header.modelHeight}");
        var names = new HashSet<string>(data.Header.segments.Select(x => x.name));
        foreach (var r in new[] { "Becken", "Abdomen", "Thorax", "Kopf", "Oberschenkel rechts", "Unterschenkel links", "Oberarm rechts", "Unterarm links" })
            Check(names.Contains(r), "Segment fehlt: " + r);
        foreach (var sg in data.Header.segments)
            Check(sg.parent == "" || names.Contains(sg.parent), sg.name + ": Elternsegment");

        // Umlaufrichtung wie Unity sie erwartet: (b−a)×(c−a) zeigt nach außen ⇒ eingeschlossenes Volumen positiv.
        // (Die Abbildung BodyParts3D → Unity spiegelt; ohne Umdrehen sähe man in Unity die Innenseiten.)
        foreach (var m in data.Meshes)
        {
            double cx = 0, cy = 0, cz = 0; int n = m.Info.vertexCount;
            for (int i = 0; i < n; i++) { cx += m.Positions[3 * i]; cy += m.Positions[3 * i + 1]; cz += m.Positions[3 * i + 2]; }
            cx /= n; cy /= n; cz /= n;
            double vol = 0;
            for (int t = 0; t < m.Indices.Length; t += 3)
            {
                int a = 3 * m.Indices[t], b = 3 * m.Indices[t + 1], c = 3 * m.Indices[t + 2];
                double ax = m.Positions[a] - cx, ay = m.Positions[a + 1] - cy, az = m.Positions[a + 2] - cz;
                double bx = m.Positions[b] - cx, by = m.Positions[b + 1] - cy, bz = m.Positions[b + 2] - cz;
                double qx = m.Positions[c] - cx, qy = m.Positions[c + 1] - cy, qz = m.Positions[c + 2] - cz;
                vol += ax * (by * qz - bz * qy) - ay * (bx * qz - bz * qx) + az * (bx * qy - by * qx);
            }
            Check(vol > 0, m.Info.name + ": Dreiecke nach innen orientiert");
        }
        // Hautgewichte nur zwischen benachbarten Segmenten (sonst hängt z. B. die Hand am Becken)
        var segIdx = data.Header.segments.Select((x, i) => new { x.name, i }).ToDictionary(x => x.name, x => x.i);
        Func<int, int, bool> adjacent = (a, b) => a == b
            || data.Header.segments[a].parent != "" && segIdx[data.Header.segments[a].parent] == b
            || data.Header.segments[b].parent != "" && segIdx[data.Header.segments[b].parent] == a;
        int foreign = 0;
        for (int i = 0; i < skin.Info.vertexCount; i++)
            if (skin.Weight0[i] < 250 && !adjacent(skin.Bones[2 * i], skin.Bones[2 * i + 1])) foreign++;
        Check(foreign == 0, $"{foreign} Hautecken mischen nicht benachbarte Segmente");
        // Muskeln (BodyParts3D): gewichtet wie die Haut, nur zwischen benachbarten Segmenten
        var muscles = data.Meshes.Where(m => m.Info.layer == "muscle").ToList();
        Check(muscles.Count >= 200, $"nur {muscles.Count} Muskeln");
        foreach (var mu in muscles)
        {
            Check(mu.Info.weights && mu.Bones != null && mu.Weight0 != null, mu.Info.name + ": ohne Gewichte");
            int bad = 0;
            for (int i = 0; i < mu.Info.vertexCount; i++)
                if (mu.Weight0[i] < 250 && !adjacent(mu.Bones[2 * i], mu.Bones[2 * i + 1])) bad++;
            Check(bad == 0, $"{mu.Info.name}: {bad} Ecken mischen nicht benachbarte Segmente");
            Check(mu.Info.label.StartsWith("M. ") || mu.Info.label.StartsWith("Mm. "), "Muskelname: " + mu.Info.label);
        }
        Check(muscles.Any(m => m.Bones.Where((b, i) => i % 2 == 0).Distinct().Count() > 1), "kein Muskel über ein Gelenk gewichtet");
        // Einatmung hebt die Vorderseite (Blendshape-Deltas entlang der äußeren Normalen)
        for (int s = 0; s < skin.BlendShapes.Count; s++)
        {
            double dz = 0; var d = skin.BlendShapes[s];
            for (int i = 0; i < skin.Info.vertexCount; i++) dz += d[3 * i + 2];
            Check(dz > 0, skin.Info.blendShapes[s] + ": bewegt die Haut nach innen");
        }
        // Deutsche Bezeichnungen für den Röntgenblick
        foreach (var m in data.Meshes)
            Check(!System.Text.RegularExpressions.Regex.IsMatch(m.Info.label, @"\b(of|left|right|bone|phalanx|metacarpal|metatarsal|nsn)\b"), "englische Bezeichnung: " + m.Info.label);

        // Anatomie-Zuordnung der Verletzungen: jedes Muster trifft mindestens ein Mesh
        var map = Load<AnatomyInjuryMapFile>("anatomy/anatomy.json");
        foreach (var e in map.injuries)
            foreach (var pat in e.meshes)
                Check(data.Meshes.Any(m => System.Text.RegularExpressions.Regex.IsMatch(m.Info.name, pat)), $"{e.injuryId}: kein Mesh für '{pat}'");
        var ids = new HashSet<string>(Injuries.injuries.Select(i => i.id));
        foreach (var e in map.injuries) Check(ids.Contains(e.injuryId), "unbekannte Verletzung " + e.injuryId);
    }

    static void TissueData()
    {
        var file = Load<TissueDatabaseFile>("tissues.json");
        var db = new TissueDatabase(file);
        var sources = new HashSet<string>(file.sources.Select(x => x.id));
        Check(file.sources.All(x => !string.IsNullOrEmpty(x.citation)), "Quelle ohne Zitat");
        Check(file.tissues.Select(x => x.id).Distinct().Count() == file.tissues.Length, "doppelte Gewebe-ID");
        var evidence = new[] { "human", "human_eingeschraenkt", "tierdaten", "keine_daten" };
        var props = new[] { "E", "shearModulus", "failureStress", "failureStrain", "failureStretch", "yieldStress", "yieldStrain", "failureStressCompression", "failureStrainCompression" };
        var stressUnits = new[] { "Pa", "kPa", "MPa", "GPa" };
        foreach (var t in file.tissues)
        {
            Check(evidence.Contains(t.evidence), t.id + ": evidence " + t.evidence);
            Check(!string.IsNullOrEmpty(t.name) && !string.IsNullOrEmpty(t.description), t.id + ": Name/Beschreibung");
            Check(t.density_g_cm3 == 0 || sources.Contains(t.densitySourceId), t.id + ": Dichte ohne Quelle");
            Check(t.density_g_cm3 == 0 || t.density_g_cm3 > 0.2f && t.density_g_cm3 < 2.1f, t.id + ": Dichte");
            Check(t.measurements.Select(x => x.id).Distinct().Count() == t.measurements.Length, t.id + ": doppelte Mess-ID");
            foreach (var m in t.measurements)
            {
                string w = t.id + "/" + m.id;
                Check(sources.Contains(m.sourceId), w + ": unbekannte Quelle " + m.sourceId);
                Check(props.Contains(m.property), w + ": Größe " + m.property);
                bool strain = m.property.Contains("Strain") || m.property == "failureStretch";
                Check(strain ? m.unit == "1" : stressUnits.Contains(m.unit), w + ": Einheit " + m.unit);
                Check(m.value > 0 && m.sd >= 0, w + ": Wert/SD");
                Check(m.min == 0 && m.max == 0 || m.min <= m.value && m.value <= m.max, w + ": Wert außerhalb der Spanne");
                Check(m.property != "failureStretch" || m.value > 1, w + ": Streckung ≤ 1");
                Check(!strain || m.property == "failureStretch" || m.value < 1.5, w + ": Dehnung als Prozent statt Bruchteil?");
                Check(!string.IsNullOrEmpty(m.species), w + ": Spezies");
                Check(m.species == "human" || t.evidence == "tierdaten", w + ": Tierdaten bei evidence " + t.evidence);
            }
            foreach (var r in new[] { t.stiffnessFrom, t.failureStrainFrom, t.failureStressFrom })
                Check(string.IsNullOrEmpty(r) || TissueDatabase.Measurement(t, r) != null, t.id + ": Verweis auf fehlende Messung " + r);
            Check(string.IsNullOrEmpty(t.stiffnessFrom) || TissueDatabase.YoungsModulus_Pa(t) > 0, t.id + ": Steifigkeit nicht umrechenbar");
            Check(string.IsNullOrEmpty(t.failureStrainFrom) || TissueDatabase.FailureStrain(t) > 0, t.id + ": Dehnung nicht umrechenbar");
            Check(string.IsNullOrEmpty(t.failureStressFrom) || TissueDatabase.FailureStress_Pa(t) > 0, t.id + ": Spannung nicht umrechenbar");
            if (t.evidence == "keine_daten")
                Check(t.measurements.Length == 0 && string.IsNullOrEmpty(t.stiffnessFrom + t.failureStrainFrom + t.failureStressFrom), t.id + ": keine_daten, aber Werte");
            else
                Check(t.measurements.Length > 0 && !string.IsNullOrEmpty(t.stiffnessFrom + t.failureStrainFrom + t.failureStressFrom), t.id + ": ohne repräsentativen Wert");
            Check(TissueDatabase.Summary(t).StartsWith(t.name), t.id + ": Kurzbeschreibung");
        }

        // Zuordnung: jede Regel trifft etwas, jedes Anatomieteil bekommt ein Gewebe
        var bytes = File.ReadAllBytes(Path.Combine(DataRoot, "anatomy", "body.bin"));
        var data = AnatomyFile.Read(bytes, json =>
        {
            var ser = new DataContractJsonSerializer(typeof(AnatomyHeader));
            using (var st = new MemoryStream(Encoding.UTF8.GetBytes(json))) return (AnatomyHeader)ser.ReadObject(st);
        });
        foreach (var r in file.mapping)
        {
            Check(db.Get(r.tissueId) != null, "Zuordnung auf unbekanntes Gewebe " + r.tissueId);
            Check(r.meshes.Length > 0 || !string.IsNullOrEmpty(r.layer), r.tissueId + ": Regel ohne Muster und Ebene");
            foreach (var pat in r.meshes)
                Check(data.Meshes.Any(m => (string.IsNullOrEmpty(r.layer) || m.Info.layer == r.layer)
                                           && System.Text.RegularExpressions.Regex.IsMatch(m.Info.name, pat)), $"{r.tissueId}: kein Mesh für '{pat}'");
        }
        foreach (var m in data.Meshes) Check(db.For(m.Info.name, m.Info.layer) != null, "ohne Gewebe: " + m.Info.name);
        Func<string, string> of = n => db.For(n, data.Meshes.First(x => x.Info.name == n).Info.layer).id;
        Check(of("liver") == "leber" && of("spleen") == "milz" && of("left kidney") == "niere", "Bauchorgane");
        Check(of("left femur") == "knochen_kortikal" && of("right fifth rib") == "knochen_kortikal", "Kortikalis");
        Check(of("third lumbar vertebra") == "knochen_spongios" && of("right hip bone") == "knochen_spongios", "Spongiosa");
        Check(of("left costal cartilage") == "knorpel" && of("Rückenmark") == "rueckenmark" && of("Haut") == "haut", "Knorpel/Rückenmark/Haut");
        Check(of("descending aorta") == "arterie" && of("inferior vena cava") == "vene" && of("middle lobe of lung") == "lunge", "Gefäße/Lunge");
        if (data.Meshes.Any(m => m.Info.layer == "muscle"))
            Check(of("right gluteus maximus") == "skelettmuskel" && of("long head of left biceps femoris") == "skelettmuskel", "Muskeln → Skelettmuskel");

        // Plausibilität (Größenordnungen, die in allen Quellen gleich liegen)
        Func<string, TissueEntry> T = db.Get;
        Check(TissueDatabase.YoungsModulus_Pa(T("knochen_kortikal")) > 10 * TissueDatabase.YoungsModulus_Pa(T("knochen_spongios")), "Kortikalis nicht steifer als Spongiosa");
        Check(TissueDatabase.YoungsModulus_Pa(T("knochen_spongios")) > 10 * TissueDatabase.YoungsModulus_Pa(T("knorpel")), "Spongiosa nicht steifer als Knorpel");
        Check(TissueDatabase.YoungsModulus_Pa(T("haut")) > 1000 * TissueDatabase.YoungsModulus_Pa(T("leber")), "Haut nicht viel steifer als Leber");
        Check(TissueDatabase.FailureStrain(T("knochen_kortikal")) < 0.05, "Knochen bricht nicht spröde");
        foreach (var soft in new[] { "haut", "leber", "milz", "niere", "arterie", "vene", "gi_wand", "zwerchfell" })
            Check(TissueDatabase.FailureStrain(T(soft)) > 0.1, soft + ": Bruchdehnung unter 10 %");
        Check(TissueDatabase.FailureStress_Pa(T("leber")) > TissueDatabase.FailureStress_Pa(T("milz")), "Milz nicht weniger fest als Leber");
        Check(T("lunge").density_g_cm3 < 0.5f && T("knochen_kortikal").density_g_cm3 > 1.8f, "Dichten");
        // Dehnrate: Leber und Milz werden bei schneller Belastung fester, reißen aber früher (Kemper 2010/2012)
        foreach (var id in new[] { "leber", "milz" })
        {
            var tens = T(id).measurements.Where(x => x.loading == "Zug, Parenchym").OrderBy(x => x.strainRate_per_s).ToList();
            var s = tens.Where(x => x.property == "failureStress").Select(x => x.value).ToList();
            var e = tens.Where(x => x.property == "failureStrain").Select(x => x.value).ToList();
            Check(s.Count == 4 && e.Count == 4, id + ": vier Dehnraten");
            for (int i = 1; i < 4; i++) Check(s[i] > s[i - 1] && e[i] <= e[i - 1], id + ": Dehnrateneffekt");
        }
        Check(TissueDatabase.Summary(T("leber")) == "Leber: E ≈ 6 kPa · reißt ab ≈ 30 % Dehnung (52,6 kPa bei 1/s)", "Text: " + TissueDatabase.Summary(T("leber")));
        Check(TissueDatabase.Summary(T("myokard")) == "Herzmuskel: keine belegten Werte", "Text ohne Daten");
    }

    static void BiomechanicsMath()
    {
        const double dt = 0.0005;
        Func<double, double, List<double>> pulse = (g, ms) =>
        {
            var l = new List<double>();
            for (int i = 0; i < 40; i++) l.Add(0);
            for (int i = 0; i < (int)Math.Round(ms / 1000 / dt); i++) l.Add(g);
            for (int i = 0; i < 40; i++) l.Add(0);
            return l;
        };
        double h10 = Biomechanics.Hic(pulse(100, 10), dt), h20 = Biomechanics.Hic(pulse(100, 20), dt);
        Check(Math.Abs(h10 - 1000) < 10, $"HIC 100 g/10 ms = {h10:F0} statt 1000");
        Check(Math.Abs(h20 - 1500) < 15, $"HIC15 100 g/20 ms = {h20:F0} statt 1500 (Fenster 15 ms)");
        var plateau = pulse(50, 10);
        for (int i = 50; i < 52; i++) plateau[i] = 200; // 1-ms-Spitze
        Check(Math.Abs(Biomechanics.Clip(plateau, dt) - 50) < 1e-9, $"3-ms-Clip {Biomechanics.Clip(plateau, dt)} statt 50");
        Check(Math.Abs(Biomechanics.NormalCdf(1.96) - 0.975) < 0.001 && Math.Abs(Biomechanics.NormalCdf(-1) - 0.1587) < 0.001, "Normalverteilung");
    }

    static void RiskCurves()
    {
        var head = Risk.metrics.First(m => m.id == "HIC15").curve;
        double p700 = InjuryRisk.Probability(head, 700), p1000 = InjuryRisk.Probability(head, 1000);
        Check(Math.Abs(p700 - 0.112) < 0.01 && Math.Abs(p1000 - 0.231) < 0.01, $"HIC 700 → {p700:P1}, 1000 → {p1000:P1}");
        Check(InjuryRisk.Probability(head, 0) == 0, "HIC 0");
        foreach (var m in Risk.metrics.Where(x => x.curve.type == "loglogistic"))
            Check(Math.Abs(InjuryRisk.Probability(m.curve, m.curve.x50) - 0.5) < 1e-9, m.id);
    }

    static void FiltersAndCriteria()
    {
        const double dt = 0.0001;
        Func<double, double, double> amp = (f, cfc) =>
        {
            var x = new List<double>();
            for (int i = 0; i < 20000; i++) x.Add(Math.Sin(2 * Math.PI * f * i * dt));
            var y = Biomechanics.Cfc(x, dt, cfc);
            double m = 0;
            for (int i = 5000; i < 15000; i++) m = Math.Max(m, Math.Abs(y[i]));
            return m;
        };
        Check(Math.Abs(amp(100, 1000) - 1) < 0.01, "CFC 1000 dämpft 100 Hz");
        Check(Math.Abs(amp(1650, 1000) - 0.75) < 0.05, $"CFC 1000 bei 1650 Hz: {amp(1650, 1000):F2} statt ≈ 0,71–0,76 (−3 dB)");
        Check(amp(3000, 600) < 0.05, "CFC 600 lässt 3 kHz durch");
        var step = new List<double>(); for (int i = 0; i < 2000; i++) step.Add(5);
        Check(Biomechanics.Cfc(step, dt, 180).All(v => Math.Abs(v - 5) < 1e-9), "Gleichanteil verändert");

        // VC: lineare Eindrückung 0 → 50 mm in 25 ms bei 0,229 m Tiefe: V = 2 m/s, C max ≈ 0,218 → VC ≈ 0,437 m/s
        var d = new List<double>(); for (int i = 0; i <= 250; i++) d.Add(0.05 * i / 250.0); for (int i = 0; i < 50; i++) d.Add(0.05);
        double vc = Biomechanics.ViscousCriterion(d, dt, 0.229);
        Check(Math.Abs(vc - 2 * 0.05 / 0.229) < 0.01, $"VC {vc:F3} statt 0,437");

        // Nij: Zug an der Grenze allein = 1; Extension 67,5 Nm allein = 0,5; Kombination
        Check(Math.Abs(Biomechanics.Nij(new[] { 6806.0 }, new[] { 0.0 }) - 1) < 1e-9, "Nij Zug");
        Check(Math.Abs(Biomechanics.Nij(new[] { 0.0 }, new[] { -67.5 }) - 0.5) < 1e-9, "Nij Extension");
        Check(Math.Abs(Biomechanics.Nij(new[] { -3080.0, 0 }, new[] { 155.0, 0 }) - 1.0) < 1e-9, "Nij Druck + Flexion");
    }

    static void CalibrationLogic()
    {
        var cal = Load<CalibrationFile>("calibration.json");
        Check(cal.references.Length > 0 && cal.references.All(r => r.values.Length > 0 && r.sources.Length > 0), "Referenzen unvollständig");
        var metrics = new HashSet<string>(Risk.metrics.Select(m => m.id));
        foreach (var r in cal.references) foreach (var v in r.values) Check(metrics.Contains(v.metric), $"Referenz {r.id}: unbekannter Messwert {v.metric}");
        var refTest = cal.references[0];
        var runs = new List<IList<CrashMeasurement>> {
            new List<CrashMeasurement> { new CrashMeasurement { metric = "HIC15", value = 1000 }, new CrashMeasurement { metric = "Thorax.D", value = 1 } },
            new List<CrashMeasurement> { new CrashMeasurement { metric = "HIC15", value = 1300 }, new CrashMeasurement { metric = "Thorax.D", value = 1 } } };
        var f = InjuryRisk.ComputeFactors(cal, refTest, runs).ToDictionary(x => x.metric);
        double hicRef = refTest.values.First(v => v.metric == "HIC15").value;
        Check(Math.Abs(f["HIC15"].factor - Math.Round(hicRef / 1150, 3)) < 1e-9, $"HIC-Faktor {f["HIC15"].factor}");
        Check(f["Thorax.D"].factor == cal.maxFactor, "Faktor nicht begrenzt");
        Check(!f.ContainsKey("Nij"), "Faktor ohne Simulationswert");
        cal.factors = f.Values.ToArray();
        var c = InjuryRisk.Calibrate(new[] { new CrashMeasurement { metric = "HIC15", value = 1150 }, new CrashMeasurement { metric = "Becken.F", value = 3 } }, cal);
        Check(Math.Abs(c[0].value - hicRef) < 1 && c[1].value == 3, "Anwendung der Faktoren");
    }

    static void RiskDataConsistent()
    {
        var r = Risk; var ids = new HashSet<string>(Injuries.injuries.Select(i => i.id));
        var metrics = new HashSet<string>(r.metrics.Select(m => m.id));
        foreach (var m in r.metrics)
        {
            Check(m.curve.type == "lognormal" || m.curve.type == "loglogistic" || m.curve.type == "normal" && m.curve.sigma > 0 || m.curve.type == "none", $"{m.id}: Kurve {m.curve.type}");
            Check(m.sources.Length > 0, $"{m.id}: keine Quelle");
        }
        foreach (var l in r.injuries)
        {
            Check(ids.Contains(l.injuryId), $"unbekannte Verletzung {l.injuryId}");
            Check(metrics.Contains(l.metric), $"unbekannter Messwert {l.metric}");
            Check(r.metrics.First(m => m.id == l.metric).curve.type != "none", $"{l.injuryId}: Messwert {l.metric} ist nur Anzeige");
            Check(l.share > 0 && l.share <= 1 && l.severityMin <= l.severityMax, $"{l.injuryId}/{l.metric}: Werte");
        }
    }

    static OrganModelFile Organs => Load<OrganModelFile>("organ_model.json");
    static TissueDatabase Tissues => new TissueDatabase(Load<TissueDatabaseFile>("tissues.json"));

    /// <summary>Kompressionsverlauf: Rampe in 10 ms auf <paramref name="c"/>, 10 ms halten, 10 ms zurück (dt 0,1 ms).</summary>
    static List<double> Ramp(double c)
    {
        var l = new List<double>();
        for (int i = 0; i < 100; i++) l.Add(c * i / 100.0);
        for (int i = 0; i < 100; i++) l.Add(c);
        for (int i = 100; i >= 0; i--) l.Add(c * i / 100.0);
        return l;
    }

    static void OrganModel()
    {
        var model = Organs; var tissues = Tissues; var risk = Risk;
        var sources = new HashSet<string>(model.sources.Select(x => x.id));
        Check(sources.Contains(model.abdomen.stiffnessSourceId) && sources.Contains(model.abdomen.geometrySourceId), "Quelle Bauchmodell");
        Check(model.abdomen.stiffness_N_per_m > 1e4 && model.abdomen.depthFrontal_m > 0.15 && model.abdomen.widthLateral_m > model.abdomen.depthFrontal_m, "Bauchmaße/Steifigkeit");
        foreach (var o in model.organs)
        {
            var t = tissues.Get(o.tissueId);
            Check(t != null, o.id + ": Gewebe " + o.tissueId);
            string prop = o.criterion == "compression" ? "failureStrainCompression" : "failureStrain";
            Check(OrganMechanics.FailureStrainAtRate(t, prop, 1) > 0, o.id + ": keine Versagensdehnung");
            Check(o.side == -1 || o.side == 1, o.id + ": Seite");
            var metric = risk.metrics.FirstOrDefault(m => m.id == o.metric);
            Check(metric != null && metric.curve.type == "normal" && metric.curve.mu == 100, o.id + ": Messwert/Kurve in injury_risk.json");
            var load = OrganMechanics.FromCompression(model, tissues, Ramp(0.3), 1e-4, 0, 0.25).Organs.First(x => x.Organ.id == o.id);
            Check(load.Cv > 0.1 && load.Cv < 0.6, o.id + $": CV {load.Cv}");
            Check(Math.Abs(metric.curve.sigma - Math.Round(load.Cv * 100, 1)) < 0.051, o.id + $": sigma {metric.curve.sigma} passt nicht zu CV {load.Cv:F3}");
        }
        // Dehnrate: Mittelwerte bei 0,01/0,1/1/10 s⁻¹, dazwischen log-linear, außerhalb der nächste Wert
        var liver = tissues.Get("leber");
        Func<double, double> ef = r => OrganMechanics.FailureStrainAtRate(liver, "failureStrainCompression", r);
        Check(Math.Abs(ef(1) - 0.61) < 1e-6 && Math.Abs(ef(10) - 0.60) < 1e-6 && Math.Abs(ef(500) - 0.60) < 1e-6 && Math.Abs(ef(0.001) - 0.94) < 1e-6, "Stützstellen Leber");
        Check(Math.Abs(ef(Math.Sqrt(0.1)) - 0.67) < 1e-6, $"Interpolation {ef(Math.Sqrt(0.1))}");
        Check(Math.Abs(OrganMechanics.FailureStrainAtRate(tissues.Get("milz"), "failureStrain", 1) - 0.19) < 1e-6, "Milz Zug: Kapselproben dürfen nicht mitzählen");
        Check(Math.Abs(OrganMechanics.TrueStrain(0.5) - Math.Log(2)) < 1e-12, "wahre Dehnung");

        // Kontrollfälle aus Leichenversuchen
        foreach (var v in model.validation)
        {
            Check(sources.Contains(v.sourceId), v.description + ": Quelle");
            var load = OrganMechanics.FromCompression(model, tissues, Ramp(v.compression), 1e-4, v.lateral, 0.289);
            double pr = load.Organs.First(o => o.Organ.id == v.organ).Probability;
            Check(pr >= v.minProbability && pr <= v.maxProbability, $"{v.description} → {pr:P0}");
        }
        // Seite: Anprall von links trifft die Milz, nicht die Leber; frontal beide
        var left = OrganMechanics.FromCompression(model, tissues, Ramp(0.45), 1e-4, -1, 0.289);
        Func<AbdomenLoad, string, OrganLoad> org = (l, id) => l.Organs.First(o => o.Organ.id == id);
        Check(org(left, "leber").Probability == 0 && org(left, "milz").Probability > 0.5 && org(left, "niere_links").Exposure == 1, "links");
        var front = OrganMechanics.FromCompression(model, tissues, Ramp(0.45), 1e-4, 0, 0.226);
        Check(org(front, "leber").Exposure == 1 && org(front, "milz").Exposure == 1, "frontal");
        // Milz versagt früher als Leber (Kemper 2011/2013), Risiko wächst mit der Kompression
        Check(org(front, "milz").Probability > org(front, "leber").Probability, "Milz nicht empfindlicher als Leber");
        double prev = -1;
        foreach (var c in new[] { 0.05, 0.15, 0.25, 0.35, 0.45, 0.55 })
        {
            double pr = org(OrganMechanics.FromCompression(model, tissues, Ramp(c), 1e-4, 0, 0.226), "leber").Probability;
            Check(pr > prev || pr == 1, $"nicht monoton bei {c}"); prev = pr;
        }
        // Belastungsbeginn bei 5 % der Spitze (Probe 6), Spitze bei Probe 100
        Check(Math.Abs(front.StrainRate_per_s - OrganMechanics.TrueStrain(0.45) / (94 * 1e-4)) < 0.01, $"Dehnrate {front.StrainRate_per_s}");
        // Kraft → Kompression: 3,5 kN seitlich am Modellkörper = 3500 / 27000 / 0,289
        var force = Ramp(3500);
        var ev = OrganMechanics.Evaluate(model, tissues, force, 1e-4, -1, model.abdomen.modelHeight_m);
        Check(ev.Lateral && Math.Abs(ev.PeakCompression - 3500 / 27000.0 / 0.289) < 1e-6, $"Kompression {ev.PeakCompression}");
        var big = OrganMechanics.Evaluate(model, tissues, Ramp(1e5), 1e-4, 0, 1.8);
        Check(big.PeakCompression == model.abdomen.maxCompression, "Begrenzung");
        var ms = OrganMechanics.Measurements(ev);
        Check(ms.Any(m => m.metric == "Abdomen.C") && model.organs.All(o => ms.Any(m => m.metric == o.metric)), "Messwerte");
        // Über injury_risk.json: Kurve liefert dasselbe Risiko wie das Organmodell
        var sp = org(ev, "milz");
        double viaCurve = InjuryRisk.Probability(risk.metrics.First(m => m.id == "Milz.Kompression").curve, sp.Utilization * 100);
        Check(Math.Abs(viaCurve - sp.Probability) < 0.002, $"Kurve {viaCurve:P1} vs Modell {sp.Probability:P1}");
    }

    static void MuscleToneData()
    {
        var f = Load<MuscleToneFile>("muscle_tone.json");
        var src = new HashSet<string>(f.sources.Select(x => x.id));
        var a = f.activation;
        foreach (var id in new[] { a.baselineSourceId, a.reflexLatencySourceId, a.reflexLevelSourceId, a.bracedLevelSourceId })
            Check(src.Contains(id), "Quelle fehlt: " + id);
        Check(a.baseline >= 0 && a.baseline < a.reflexLevel && a.reflexLevel <= 1 && a.bracedLevel <= 1, "Aktivierungswerte");
        Check(a.reflexLatency_s >= 0.05 && a.reflexLatency_s <= 0.3, "Reflexlatenz außerhalb 50–300 ms");
        var bytes = File.ReadAllBytes(Path.Combine(DataRoot, "anatomy", "body.bin"));
        var data = AnatomyFile.Read(bytes, json =>
        {
            var ser = new DataContractJsonSerializer(typeof(AnatomyHeader));
            using (var st = new MemoryStream(Encoding.UTF8.GetBytes(json))) return (AnatomyHeader)ser.ReadObject(st);
        });
        var segs = new HashSet<string>(data.Header.segments.Select(x => x.name));
        foreach (var j in f.joints)
        {
            Check(segs.Contains(j.segment), "unbekanntes Segment " + j.segment);
            Check(src.Contains(j.sourceId), j.segment + ": Quelle");
            Check(j.male_Nm > j.female_Nm && j.female_Nm > 0, j.segment + ": Werte");
        }
        foreach (var sg in data.Header.segments.Where(x => x.parent != ""))
            Check(f.joints.Any(j => j.segment == sg.name), "Gelenk ohne Muskeltonus: " + sg.name);
        // Aktivierung
        Check(MuscleTone.Activation(a, OccupantState.Bewusstlos, 0.3) == 0, "bewusstlos");
        Check(MuscleTone.Activation(a, OccupantState.Angespannt, 0) == a.bracedLevel, "angespannt von Beginn an");
        Check(MuscleTone.Activation(a, OccupantState.Entspannt, 0.05) == a.baseline, "entspannt vor dem Reflex");
        double mid = MuscleTone.Activation(a, OccupantState.Entspannt, a.reflexLatency_s + a.reflexRise_s / 2);
        Check(mid > a.baseline && mid < a.reflexLevel, "Reflexanstieg");
        Check(Math.Abs(MuscleTone.Activation(a, OccupantState.Entspannt, 1) - a.reflexLevel) < 1e-12, "Reflexniveau");
        // Kniestrecker: Harbo 2012 gegen Anderson 2007 (0,163 × Körpergewicht × Größe, Männer) — gleiche Größenordnung
        double harbo = MuscleTone.MaxTorque(f, "Unterschenkel rechts", true), anderson = 0.163 * 80 * 9.81 * 1.80;
        Check(Math.Abs(harbo / anderson - 1) < 0.3, $"Knie: Harbo {harbo:F0} Nm vs. Anderson {anderson:F0} Nm");
        var d = MuscleTone.Drive(f, "Unterschenkel rechts", true, a.bracedLevel);
        Check(Math.Abs(d.Item2 * f.angleAtMaxTorque_deg * Math.PI / 180 - d.Item1) < 1e-9, "Steifigkeit × Winkel = Moment");
    }

    static void InjuryVisualsData()
    {
        var f = Load<InjuryVisualsFile>("injury_visuals.json");
        var inj = Injuries.injuries.ToDictionary(x => x.id);
        var src = new HashSet<string>(f.sources.Select(x => x.id));
        foreach (var k in f.kinds)
        {
            Check(string.IsNullOrEmpty(k.sourceId) || src.Contains(k.sourceId), k.id + ": Quelle " + k.sourceId);
            Check(k.colorCenter.Length == 3 && k.colorEdge.Length == 3 && k.opacity > 0 && k.opacity <= 1, k.id + ": Farbe");
        }
        foreach (var id in new[] { "schnitt", "riss_quetschwunde", "schuerfung", "stichwunde", "prellmarke", "haematom", "platzwunde", "offene_fraktur", "amputation" })
            Check(f.kinds.Any(k => k.id == id), "Art fehlt: " + id);
        var bp = f.bloodPool;
        foreach (var id in new[] { bp.surfaceTensionSourceId, bp.densitySourceId, bp.formulaSourceId, f.spurt.sourceId }) Check(src.Contains(id), "Quelle fehlt: " + id);

        var data = AnatomyFile.Read(File.ReadAllBytes(Path.Combine(DataRoot, "anatomy", "body.bin")), json =>
        {
            var ser = new DataContractJsonSerializer(typeof(AnatomyHeader));
            using (var st = new MemoryStream(Encoding.UTF8.GetBytes(json))) return (AnatomyHeader)ser.ReadObject(st);
        });
        var skin = data.Meshes[0];
        int tc = skin.Info.submeshes[0] / 3, n = skin.Info.vertexCount;
        float scale = 1.75f / data.Header.modelHeight;
        var nrm = SkinMarks.VertexNormals(skin.Positions, skin.Indices, skin.Indices.Length / 3);
        var area = SkinMarks.VertexAreas(skin.Positions, skin.Indices, skin.Indices.Length / 3);
        var adj = SkinMarks.Adjacency(n, skin.Indices, tc);
        foreach (var m in f.marks)
        {
            Check(inj.ContainsKey(m.injuryId), m.id + ": unbekannte Verletzung " + m.injuryId);
            var def = inj[m.injuryId];
            var texts = (def.clinicalFindings ?? new string[0]).Concat((def.dynamicFindings ?? new DynamicFinding[0]).Select(x => x.text));
            Check(texts.Contains(m.finding), m.id + ": Befund nicht in der Verletzung: " + m.finding);
            bool swelling = m.swellingFromLoss || m.swelling_mL > 0;
            Check(f.kinds.Any(k => k.id == m.kind) || (m.kind == "" && swelling), m.id + ": Art " + m.kind);
            if (m.amputation) continue;
            var p = SkinMarks.Select(data, m, scale, tc, nrm, adj);
            Check(p != null && !p.HiddenByShorts, m.id + ": kein sichtbarer Hautort");
            Check(p.HitRegion == (string.IsNullOrEmpty(m.region) ? def.region : m.region), $"{m.id}: Region {p.HitRegion} statt {def.region}");
            if (m.kind != "") Check(p.Triangles.Length >= 3 * 20, $"{m.id}: zu wenige Dreiecke ({p.Triangles.Length / 3})");
            if (swelling)
            {
                var d = SkinMarks.SwellingDeltas(p, nrm, area, n, 500, scale);
                Check(Math.Abs(SkinMarks.DisplacedVolume_mL(d, nrm, area, scale) - 500) < 1, m.id + ": Schwellungsvolumen");
            }
            if (m.swellingFromLoss) Check(def.effects.Any(e => e.maxTotalLoss_mL > 0), m.id + ": Schwellung aus Verlust ohne Blutung");
            if (m.bleeding) Check(def.effects.Any(e => e.effectType == "Hemorrhage"), m.id + ": äußere Blutung ohne Blutverlust");
        }

        // Wachstum mit dem Blutverlust und Bedingung
        var grow = f.marks.First(x => x.growToLost_mL > x.growFromLost_mL && x.when != null && x.when.minLost_mL > 0);
        Func<double, ExamContext> ctx = lost => new ExamContext { Time_s = 100, Lost_mL = _ => lost, Level = _ => 0, Events = new List<LogEntry>() };
        Check(SkinMarks.Amount(grow, ctx(grow.when.minLost_mL - 1)) == 0, "Hämatom vor der Schwelle sichtbar");
        double half = SkinMarks.Amount(grow, ctx((grow.growFromLost_mL + grow.growToLost_mL) / 2));
        Check(Math.Abs(half - 0.5) < 1e-9, "Wachstum zur Hälfte");

        // Schmale Zeichen werden unterteilt (Stich: wenige mm)
        var stab = new SkinMark { id = "t", anchor = new MarkAnchor { meshes = new[] { "left rectus abdominis" }, offset = new float[] { -0.02f, 0.03f, 0 }, direction = new float[] { 0, 0, 1 } },
                                  segments = new[] { "Abdomen" }, radius_m = 0.007f, stretch = 2.5f, stretchAxis = new float[] { 1, 0.2f, 0 } };
        var sp = SkinMarks.Select(data, stab, scale, tc, nrm, adj);
        Check(sp.Subdivisions >= 2 && sp.RFalloff.Count(x => x > 0.3f) >= 6, $"Stich zu grob ({sp.RFalloff.Count(x => x > 0.3f)} Vertices)");
        for (int i = 0; i < sp.W.Length; i += 3) Check(Math.Abs(sp.W[i] + sp.W[i + 1] + sp.W[i + 2] - 1) < 1e-4, "Mischgewichte ≠ 1");

        // Amputation Unterschenkel rechts: Haut distal weg, Stumpf zeigt nach distal, Fuß ausgeblendet, Oberschenkel nicht
        var amp = new SkinMark { id = "amp", anchor = new MarkAnchor { meshes = new[] { "right tibia" }, offset = new float[] { 0, -0.06f, 0 }, direction = new float[] { 0, -1, 0 } },
                                 segments = new[] { "Unterschenkel rechts" }, amputation = true };
        var a = SkinMarks.Amputate(data, amp, scale, tc);
        Check(a.RemovedTriangles > 500 && a.BoundaryEdges > 10, $"Amputation: {a.RemovedTriangles} entfernt, {a.BoundaryEdges} Randkanten");
        double ny = 0;
        for (int t = 0; t < a.CapTriangles.Length; t += 3)
        {
            int i = a.CapTriangles[t], j = a.CapTriangles[t + 1], k = a.CapTriangles[t + 2];
            Func<int, int, float> P = (v, c) => a.CapPositions[3 * v + c];
            float ux = P(j, 0) - P(i, 0), uy = P(j, 1) - P(i, 1), uz = P(j, 2) - P(i, 2), vx = P(k, 0) - P(i, 0), vy = P(k, 1) - P(i, 1), vz = P(k, 2) - P(i, 2);
            ny += uz * vx - ux * vz; // y-Komponente des Kreuzprodukts
        }
        Check(ny < 0, "Stumpf zeigt nicht nach distal");
        var segs = new HashSet<int> { data.SegmentIndex("Unterschenkel rechts") };
        Func<string, bool> hidden = name => SkinMarks.IsDistal(data.Meshes.First(x => x.Info.name == name), a, segs);
        Check(hidden("right talus") || hidden("right calcaneus"), "Fußknochen nicht ausgeblendet");
        Check(!hidden("right femur") && !hidden("left tibia"), "falsche Teile ausgeblendet");
        // Auflagen enden an der Amputationskante
        var removedV = SkinMarks.RemovedVertices(skin.Indices, tc, a.KeptTriangles);
        var smear = new SkinMark { id = "s", kind = "blutspur", anchor = new MarkAnchor { meshes = new[] { "right tibia" }, offset = new float[] { 0, -0.05f, 0 }, direction = new float[] { 0, 0, 1 } },
                                   segments = new[] { "Unterschenkel rechts" }, radius_m = 0.08f, wrap = true };
        var sm = SkinMarks.Select(data, smear, scale, tc, nrm, adj);
        int before = sm.Triangles.Length;
        SkinMarks.DropRemoved(sm, removedV);
        Check(sm.Triangles.Length < before && sm.Triangles.Length > 0, $"Auflage an der Amputation nicht gekürzt ({before / 3} → {sm.Triangles.Length / 3})");
        // Jede äußere Blutung hat ein Zeichen mit Blutstrahl
        foreach (var d in Injuries.injuries.Where(x => x.effects.Any(e => e.effectType == "Hemorrhage" && (e.compartment == "Extremity" || e.compartment == "LeftArm"))))
            Check(f.marks.Any(m => m.injuryId == d.id && m.bleeding), d.id + ": äußere Blutung ohne sichtbare Blutung");

        // Blutlache und Strahl
        double thick = SkinMarks.PoolThickness_m(bp);
        Check(thick > 0.002 && thick < 0.005, $"Dicke der Lache {thick * 1000:F2} mm");
        Check(Math.Abs(SkinMarks.PoolRadius_m(1000, thick) - Math.Sqrt(1e-3 / (Math.PI * thick))) < 1e-12, "Radius der Lache");
        double jet = SkinMarks.JetSpeed_m_s(120, bp.density_kg_m3, 1);
        Check(jet > 5 && jet < 6, $"Strahl bei 120 mmHg {jet:F2} m/s");
    }

    static void CrashScenario()
    {
        var mech = Mechanisms; var inj = Injuries.injuries;
        var organs = Organs; var tissues = Tissues;
        Func<double, double, double, List<CrashMeasurement>> meas = (hic, thoraxG, lateral) => new List<CrashMeasurement> {
            new CrashMeasurement { metric = "HIC15", value = hic }, new CrashMeasurement { metric = "Thorax.a3ms", value = thoraxG },
            new CrashMeasurement { metric = "Thorax.F", value = thoraxG / 15 }, new CrashMeasurement { metric = "Abdomen.F", value = thoraxG / 12 },
            new CrashMeasurement { metric = "Abdomen.lateral", value = lateral }, new CrashMeasurement { metric = "Becken.F", value = thoraxG / 10 },
            new CrashMeasurement { metric = "Oberschenkel rechts.F", value = thoraxG / 6 }, new CrashMeasurement { metric = "Unterschenkel rechts.F", value = thoraxG / 15 },
            new CrashMeasurement { metric = "Thorax.D", value = thoraxG * 0.6 }, new CrashMeasurement { metric = "Thorax.VC", value = thoraxG / 80 },
            new CrashMeasurement { metric = "Oberschenkel rechts.Fax", value = thoraxG / 8 }, new CrashMeasurement { metric = "Nij", value = thoraxG / 100 } }
            .Concat(OrganMechanics.Measurements(OrganMechanics.Evaluate(organs, tissues, Ramp(thoraxG / 12 * 1000), 1e-4, lateral, 1.8))).ToList();
        var setup = new CrashSetup { MechanismId = "pkw_seite", Speed_kmh = 60, Rescue_min = 30 };
        int hardInjured = 0, softInjured = 0, liver = 0, spleen = 0;
        for (uint seed = 1; seed <= 200; seed++)
        {
            var hard = InjuryRisk.BuildScenario(Risk, mech, inj, meas(2500, 120, -1), setup, seed).Scenario;
            var soft = InjuryRisk.BuildScenario(Risk, mech, inj, meas(80, 8, -1), setup, seed).Scenario;
            if (hard.injuries.Length > 0) hardInjured++;
            if (soft.injuries.Length > 0) softInjured++;
            liver += hard.injuries.Count(i => i.injuryId == "leberlazeration");
            spleen += hard.injuries.Count(i => i.injuryId == "milzruptur");
            Check(hard.injuries.Count(i => i.injuryId == "sdh_akut_links" || i.injuryId == "epiduralhaematom_rechts") <= 1, "Ausschlussgruppe SHT");
            Check(!hard.description.Contains("{"), "Platzhalter: " + hard.description);
        }
        Check(hardInjured == 200 && softInjured < 10, $"verletzt: hart {hardInjured}/200, leicht {softInjured}/200");
        Check(spleen > 100 && liver == 0, $"Anprall von links: Milz {spleen}, Leber {liver}");
        var a = InjuryRisk.BuildScenario(Risk, mech, inj, meas(1200, 70, 0), setup, 42).Scenario;
        var b = InjuryRisk.BuildScenario(Risk, mech, inj, meas(1200, 70, 0), setup, 42).Scenario;
        Check(string.Join(",", a.injuries.Select(i => i.injuryId + i.severity)) == string.Join(",", b.injuries.Select(i => i.injuryId + i.severity)), "nicht reproduzierbar");
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
