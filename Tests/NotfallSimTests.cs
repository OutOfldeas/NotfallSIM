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
        Run("Blutungsphysik: Bauchdruck nach Malbrain, Gerinnsel unter 64 mmHg, Nachblutung nach Volumen, Tamponade", BleedingPhysics);
        Run("Lagerung und Hydrostatik: Höhe über dem Herzen, Druck −78 mmHg/m, venöse Blutung steht hochgelagert, Autotransfusion", PositioningPhysics);
        Run("Haut: Daten mit Quellen, Blässe/Schweiß nach Schockklasse, Zyanose aus Desoxy-Hb (Anämie maskiert)", SkinData);
        Run("Crash-Zeichen: Prellung/Platzwunde/Schürfung nach Schwellen, Ort an der Haut, Gurtmarken", CrashMarksData);
        Run("Verletzungszeichen: Ort an der Haut, Region, Befund, Schwellungsvolumen, Amputation, Blutlache", InjuryVisualsData);
        Run("Temperatur: kaltes EK kühlt, Wärmer verhindert das, Auskühlung entkleidet und in Narkose, Wärmeerhalt", TemperaturePhysics);
        Run("Gerinnung: Verdünnung durch Kristalloid, FFP und Fibrinogen, Hyperfibrinolyse ohne TXA, Kälte/Azidose/Calcium", CoagulationPhysics);
        Run("Medikamente: Propofol im Schock, Fentanyl-Atemdepression nur spontan atmend, Ketamin, Calcium", DrugEffects);
        Run("Maßnahmen: Kontraindikationen gültig, Gerinnungslabor, Medikamente bekannt", NewInterventionData);
        Run("Dosierung: Angaben gültig, Wirkung skaliert, Überdosis, Perfusor verstellen und stoppen", Dosing);
        Run("Patientenmonitor: NIBP-Zyklus, Fehlmessung, Alarme mit Prioritäten, Pulsoxymetrie ohne Signal", PatientMonitorChecks);
        Run("Coach: Tipp nach xABCDE ohne Spoiler, spezifische Maßnahme erst nach Befund, Strategie vollständig", CoachChecks);
        Run("Aufprallmodell: Kontakt gegen Versuche (Faust, Thoraxpendel), Frakturschwellen, Durchstich, Stichkanal → Verletzung", ImpactModelChecks);

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
        foreach (var iv in Interventions.interventions)
            if (iv.contraindicatedBy != null && iv.contraindicatedBy.Length > 0) possible.Add("warning:contraindicated:" + iv.id);
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
            at(40, "waerme"); at(45, "infusionswaermer");
            if (bleed)
            {
                at(90, "txa");
                for (int k = 0; k < 5; k++) { at(90 + 360 * k, "ek"); at(100 + 360 * k, "ffp"); }
                at(800, "calcium"); at(1500, "calcium");
            }
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
                MaxTotalLoss_mL = x.maxTotalLoss_mL, Lesion = x.lesion, ProgressionRate_per_min = x.progressionRate_per_min, InitialLevel = x.initialLevel
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

    static void BleedingPhysics()
    {
        // Bauchraum: +2 L ≈ 12–14 mmHg, Compliance bei +4 L im Bereich 60–133 mL/mmHg (Malbrain 2016)
        double p2 = MockPhysiologyEngine.AbdominalPressure_mmHg(2000);
        Check(p2 > 10 && p2 < 16, $"IAP bei +2 L {p2:F1} mmHg");
        double c4 = 100 / (MockPhysiologyEngine.AbdominalPressure_mmHg(4050) - MockPhysiologyEngine.AbdominalPressure_mmHg(3950));
        Check(c4 > 60 && c4 < 133, $"Compliance bei +4 L {c4:F0} mL/mmHg");

        Func<string, InjuryEvent> spleen = lesion => new InjuryEvent { InjuryId = "m", Effect = InjuryEffectType.Hemorrhage, Compartment = "Spleen", Severity = 1, BaseRate_mL_min = 100, MaxTotalLoss_mL = 2500, Lesion = lesion };
        // Parenchym: Gerinnsel, sobald der MAD lange genug unter 64 mmHg liegt
        var e = new MockPhysiologyEngine(1); e.Initialize(new PatientProfile { weight_kg = 80 });
        e.ApplyInjury(spleen("parenchymal"));
        double tLow = -1;
        for (double t = 0; t < 3600 && !e.InjuryClotted("m"); t += 0.1) { if (tLow < 0 && e.Current.MeanArterialPressure_mmHg < 64) tLow = t; e.Advance(0.1); }
        Check(e.InjuryClotted("m"), "kein Gerinnsel trotz Hypotonie");
        double lost0 = e.InjuryBloodLoss_mL("m"); Advance(e, 60); double clottedRate = e.InjuryBloodLoss_mL("m") - lost0;
        Check(clottedRate < 15, $"mit Gerinnsel noch {clottedRate:F0} mL/min");
        // Volumen hebt den MAD über die Schwelle: Gerinnsel löst sich, Blutung nimmt wieder zu
        e.ApplyIntervention(new InterventionEvent { Type = InterventionType.FluidInfusion, Compound = "Blood", Volume_mL = 2000, Rate_mL_min = 400 });
        for (int k = 0; k < 6000 && e.InjuryClotted("m"); k++) e.Advance(0.1);
        Check(!e.InjuryClotted("m") && e.Current.MeanArterialPressure_mmHg >= 64, "keine Nachblutung nach Volumengabe");
        lost0 = e.InjuryBloodLoss_mL("m"); Advance(e, 60);
        Check(e.InjuryBloodLoss_mL("m") - lost0 > 3 * clottedRate, "Nachblutung nicht stärker als mit Gerinnsel");
        // Seitlicher Einriss: nie Gerinnsel
        var p = new MockPhysiologyEngine(1); p.Initialize(new PatientProfile { weight_kg = 80 });
        p.ApplyInjury(new InjuryEvent { InjuryId = "a", Effect = InjuryEffectType.Hemorrhage, Compartment = "LeftArm", Severity = 1, BaseRate_mL_min = 60, Lesion = "partial" });
        Advance(p, 1800);
        Check(!p.InjuryClotted("a"), "seitlicher Einriss mit Gerinnsel");
        // Becken: Tamponade beim Tamponadevolumen (Gegendruck = Ausgangs-MAD), bei Hypotonie früher
        var b = new MockPhysiologyEngine(1); b.Initialize(new PatientProfile { weight_kg = 80 });
        b.ApplyInjury(new InjuryEvent { InjuryId = "b", Effect = InjuryEffectType.Hemorrhage, Compartment = "Pelvis", Severity = 1, BaseRate_mL_min = 150, MaxTotalLoss_mL = 1200 });
        Advance(b, 3000); double vb = b.InjuryBloodLoss_mL("b"); Advance(b, 600);
        // Gegendruck = MAD beim Tamponadevolumen; liegt der MAD (Kompensation) etwas über dem Ausgangswert, etwas darüber
        Check(b.InjuryBloodLoss_mL("b") <= 1.05 * 1200 && b.InjuryBloodLoss_mL("b") - vb < 10, $"Becken: {b.InjuryBloodLoss_mL("b"):F0} mL, letzte 10 min {b.InjuryBloodLoss_mL("b") - vb:F0} mL");
        // Geteilter Bauchraum: Leber blutet langsamer, wenn die Milz schon Blut in den Bauch gebracht hat
        Func<bool, double> liverFirstMinute = withSpleen =>
        {
            var x = new MockPhysiologyEngine(1); x.Initialize(new PatientProfile { weight_kg = 80 });
            if (withSpleen) { x.ApplyInjury(spleen("")); Advance(x, 1200); x.ApplyIntervention(new InterventionEvent { Type = InterventionType.HemorrhageControl, TargetCompartment = "Spleen", RateMultiplier = 0 }); }
            x.ApplyIntervention(new InterventionEvent { Type = InterventionType.FluidInfusion, Compound = "Blood", Volume_mL = 5000, Rate_mL_min = 5000 });
            Advance(x, 60);
            x.ApplyInjury(new InjuryEvent { InjuryId = "l", Effect = InjuryEffectType.Hemorrhage, Compartment = "Liver", Severity = 1, BaseRate_mL_min = 90, MaxTotalLoss_mL = 2500 });
            Advance(x, 60);
            return x.InjuryBloodLoss_mL("l");
        };
        Check(liverFirstMinute(true) < liverFirstMinute(false), "Bauchraum nicht geteilt");
    }

    static MockPhysiologyEngine Trias(double loss_mL = 0, double temp = 0)
    {
        var e = new MockPhysiologyEngine(1);
        e.Initialize(new PatientProfile { weight_kg = 75, age = 40, sex = "male", initialBloodLoss_mL = loss_mL, initialTemperature_C = temp });
        return e;
    }
    static void Give(MockPhysiologyEngine e, InterventionType t, string compound, double vol = 0, double rate = 0) =>
        e.ApplyIntervention(new InterventionEvent { Type = t, Compound = compound, Volume_mL = vol, Rate_mL_min = rate });

    static void TemperaturePhysics()
    {
        var e = Trias(); double t0 = e.Current.Temperature_C;
        Check(Math.Abs(t0 - 36.5) < 1e-9, $"Ausgangstemperatur {t0}");
        Advance(e, 3600);
        Check(Math.Abs(e.Current.Temperature_C - 36.0) < 0.01, $"entkleidet 1 h: {e.Current.Temperature_C:F2} °C (−0,5 °C/h)");
        var n = Trias(); Give(n, InterventionType.Intubation, null); Advance(n, 3600);
        Check(Math.Abs(n.Current.Temperature_C - 35.5) < 0.01, $"in Narkose 1 h: {n.Current.Temperature_C:F2} °C");
        // 1 EK (300 mL, 4 °C) bei 75 kg: ΔT = 0,3·1,06·3,6·(36,5−4) / (75·2,98) ≈ 0,17 °C (plus 6 min Auskühlung 0,05 °C)
        var cold = Trias(); Give(cold, InterventionType.FluidInfusion, "Blood", 300, 50); Advance(cold, 360);
        double dCold = 36.5 - cold.Current.Temperature_C;
        Check(dCold > 0.19 && dCold < 0.24, $"kaltes EK: −{dCold:F3} °C");
        var warm = Trias(); Give(warm, InterventionType.Supportive, "FluidWarmer"); Give(warm, InterventionType.FluidInfusion, "Blood", 300, 50); Advance(warm, 360);
        Check(warm.Current.Temperature_C > 36.45, $"gewärmtes EK: {warm.Current.Temperature_C:F3} °C");
        var w = Trias(temp: 34.5); Give(w, InterventionType.Supportive, "Warming"); Advance(w, 3600);
        Check(Math.Abs(w.Current.Temperature_C - 35.0) < 0.01, $"Wärmeerhalt 1 h: {w.Current.Temperature_C:F2} °C");
        var full = Trias(temp: 36.9); Give(full, InterventionType.Supportive, "Warming"); Advance(full, 3600);
        Check(full.Current.Temperature_C <= 37 + 1e-9, "Wärmeerhalt nicht über 37 °C");
    }

    static void CoagulationPhysics()
    {
        var e = Trias();
        Check(e.Current.FactorActivity == 1 && Math.Abs(e.Current.Fibrinogen_g_L - 2.5) < 1e-9 && e.Current.Coagulation > 0.97, "normale Gerinnung");
        // 3 L Kristalloid (30 % bleiben intravasal) verdünnen das Plasma (~3,1 L) um ca. 22 %
        var d = Trias(); Give(d, InterventionType.FluidInfusion, "BalancedCrystalloid", 3000, 300); Advance(d, 600);
        Check(d.Current.FactorActivity > 0.7 && d.Current.FactorActivity < 0.85, $"Verdünnung: Faktoren {d.Current.FactorActivity:P0}");
        // Massivtransfusion nur mit EK: Faktoren fallen deutlich, Plasma hält sie
        Func<string, VitalSigns> mt = plasma =>
        {
            var x = Trias(1500);
            x.ApplyInjury(new InjuryEvent { InjuryId = "x", Effect = InjuryEffectType.Hemorrhage, Compartment = "Liver", Severity = 1, BaseRate_mL_min = 60, MaxTotalLoss_mL = 8000, Lesion = "parenchymal" });
            Give(x, InterventionType.Medication, "TXA");
            Give(x, InterventionType.FluidInfusion, "Blood", 3000, 100);
            if (plasma != null) Give(x, InterventionType.FluidInfusion, plasma, 2000, 70);
            Advance(x, 1800);
            return x.Current;
        };
        var ekOnly = mt(null); var withFfp = mt("Plasma");
        Check(ekOnly.FactorActivity < 0.6, $"nur EK: Faktoren {ekOnly.FactorActivity:P0}");
        Check(withFfp.FactorActivity > ekOnly.FactorActivity + 0.15, $"EK + FFP: Faktoren {withFfp.FactorActivity:P0} statt {ekOnly.FactorActivity:P0}");
        // Fibrinogen 2 g hebt um ca. 0,6 g/L (Plasma ~3,1 L)
        var f = Trias(); Give(f, InterventionType.Medication, "Fibrinogen");
        Check(f.Current.Fibrinogen_g_L > 3.0 && f.Current.Fibrinogen_g_L < 3.3, $"Fibrinogen nach 2 g: {f.Current.Fibrinogen_g_L:F2} g/L");
        // Hyperfibrinolyse im Schock ohne TXA, mit TXA nicht
        Func<bool, double> lysis = txa => { var x = Trias(1400); if (txa) Give(x, InterventionType.Medication, "TXA"); Advance(x, 1200); return x.Current.Fibrinogen_g_L; };
        double noTxa = lysis(false), withTxa = lysis(true);
        Check(noTxa < 2.0 && Math.Abs(withTxa - 2.5) < 1e-6, $"Fibrinogen nach 20 min Schock: ohne TXA {noTxa:F2}, mit TXA {withTxa:F2} g/L");
        // Einzelfaktoren der wirksamen Gerinnung
        double c0 = MockPhysiologyEngine.Coagulation(1, 1, 37, 7.4, 1.2);
        Check(Math.Abs(c0 - 1) < 1e-9, "normal 1");
        Check(Math.Abs(MockPhysiologyEngine.Coagulation(1, 1, 33, 7.4, 1.2) - 0.85) < 1e-9, "33 °C: 0,85");
        Check(Math.Abs(MockPhysiologyEngine.Coagulation(1, 1, 37, 7.0, 1.2) - 0.4) < 1e-9, "pH 7,0: 0,4 (FVIIa/TF −60 %, Meng 2003)");
        Check(MockPhysiologyEngine.Coagulation(1, 1, 37, 7.4, 0.8) < 0.8, "iCa 0,8 bremst");
        Check(MockPhysiologyEngine.Coagulation(0.2, 1, 37, 7.4, 1.2) == 0 && MockPhysiologyEngine.Coagulation(1, 0.8 / 2.5, 37, 7.4, 1.2) == 0, "Faktoren 20 % bzw. Fibrinogen 0,8 g/L: keine Gerinnung");
        // Schlechte Gerinnung verstärkt eine diffuse Blutung bis ×2
        Func<double, double> bleed = temp =>
        {
            var x = Trias(0, temp);
            x.ApplyInjury(new InjuryEvent { InjuryId = "x", Effect = InjuryEffectType.Hemorrhage, Compartment = "Liver", Severity = 1, BaseRate_mL_min = 30, MaxTotalLoss_mL = 5000, Lesion = "parenchymal" });
            Advance(x, 120); return x.InjuryBloodLoss_mL("x");
        };
        double warmB = bleed(37), coldB = bleed(31);
        Check(coldB > 1.2 * warmB, $"Leberblutung 2 min: 37 °C {warmB:F0} mL, 31 °C {coldB:F0} mL");
    }

    static void DrugEffects()
    {
        Func<double, string, bool, VitalSigns> after = (loss, drug, intubate) =>
        {
            var x = Trias(loss);
            if (intubate) Give(x, InterventionType.Intubation, null);
            if (drug != null) Give(x, InterventionType.Medication, drug);
            Advance(x, 120); return x.Current;
        };
        var n0 = after(0, null, false); var nP = after(0, "Propofol", false);
        var s0 = after(1500, null, false); var sP = after(1500, "Propofol", false);
        double dropN = 1 - nP.MeanArterialPressure_mmHg / n0.MeanArterialPressure_mmHg, dropS = 1 - sP.MeanArterialPressure_mmHg / s0.MeanArterialPressure_mmHg;
        Check(dropN > 0.15 && dropN < 0.25, $"Propofol normovoläm: MAD −{dropN:P0} (Vale 2024: 16–23 %)");
        Check(dropS > dropN + 0.1, $"Propofol im Schock: MAD −{dropS:P0}");
        var f = after(0, "Fentanyl", false); var fi = after(0, "Fentanyl", true);
        Check(f.RespirationRate_bpm < 0.9 * n0.RespirationRate_bpm && f.SpO2_percent < n0.SpO2_percent, $"Fentanyl spontan: AF {f.RespirationRate_bpm:F0} statt {n0.RespirationRate_bpm:F0}, SpO2 {f.SpO2_percent:F0}");
        Check(fi.RespirationRate_bpm == 14, "Fentanyl beatmet: keine Atemdepression");
        Check(f.HeartRate_bpm < n0.HeartRate_bpm, "Fentanyl senkt die HF");
        var k = after(0, "Ketamin", false); var kS = after(2000, "Ketamin", false); var kS0 = after(2000, null, false);
        Check(k.SystolicBP_mmHg > n0.SystolicBP_mmHg * 1.05 && k.HeartRate_bpm > n0.HeartRate_bpm + 5, $"Ketamin: RR {k.SystolicBP_mmHg:F0}, HF {k.HeartRate_bpm:F0}");
        Check(Math.Abs(kS.SystolicBP_mmHg - kS0.SystolicBP_mmHg) < 0.5, "Ketamin im tiefen Schock: kein RR-Anstieg");
        Check(k.RespirationRate_bpm == n0.RespirationRate_bpm, "Ketamin: keine Atemdepression");
        // Wirkung klingt ab
        var late = Trias(); Give(late, InterventionType.Medication, "Propofol"); Advance(late, 20 * 60);
        Check(Math.Abs(late.Current.MeanArterialPressure_mmHg - Trias().Current.MeanArterialPressure_mmHg) < 0.5, "Propofol nach 20 min abgeklungen");
        // Calcium: Massivtransfusion senkt iCa und RR, CaCl2 hebt beides
        var ca = Trias(); Give(ca, InterventionType.FluidInfusion, "Blood", 2400, 400); Advance(ca, 400);
        double ica = ca.Current.IonizedCalcium, sbp = ca.Current.SystolicBP_mmHg;
        Give(ca, InterventionType.Medication, "Calcium"); Give(ca, InterventionType.Medication, "Calcium");
        Check(ica < 0.8 && Math.Abs(ca.Current.IonizedCalcium - ica - 0.4) < 1e-9, $"iCa nach 8 EK {ica:F2}, nach 2 g CaCl2 {ca.Current.IonizedCalcium:F2}");
        Check(ca.Current.SystolicBP_mmHg > sbp + 6, $"RR durch Calcium {sbp:F0} → {ca.Current.SystolicBP_mmHg:F0}");
    }

    static void Dosing()
    {
        var defs = Interventions.interventions.ToDictionary(i => i.id);
        foreach (var iv in Interventions.interventions.Where(i => i.dose != null))
        {
            var d = iv.dose;
            Check(d.min <= d.usualMin && d.usualMin <= d.reference && d.reference <= d.usualMax && d.usualMax <= d.max && d.step > 0,
                  $"{iv.id}: Bereich {d.min}/{d.usualMin}/{d.reference}/{d.usualMax}/{d.max}");
            Check(Math.Abs(d.Clamp(d.reference) - d.reference) < 1e-9, $"{iv.id}: Referenz nicht im Raster");
            Check(!d.perKg || !string.IsNullOrEmpty(d.absoluteUnit), $"{iv.id}: Einheit der Gesamtdosis fehlt");
            Check(Math.Abs(iv.ToEvent(d.reference).DoseFactor - 1) < 1e-12, $"{iv.id}: Referenz ergibt Faktor 1");
        }
        var pro = defs["propofol"].dose;
        Check(pro.Format(1.5, 75) == "1,5 mg/kg (113 mg)", pro.Format(1.5, 75));
        Check(pro.Clamp(9) == 3 && pro.Clamp(1.6) == 1.5 && !pro.Usual(3) && pro.Usual(2), "Raster und üblicher Bereich");
        Check(defs["noradrenalin"].dose.Usual(0), "Perfusor aus ist keine Fehldosierung");

        Func<double, string, double, VitalSigns> bolus = (loss, drug, f) =>
        {
            var x = Trias(loss);
            x.ApplyIntervention(new InterventionEvent { Type = InterventionType.Medication, Compound = drug, DoseFactor = f });
            Advance(x, 120); return x.Current;
        };
        double map0 = Trias(1500).Current.MeanArterialPressure_mmHg;
        Func<double, double> propDrop = f => 1 - bolus(1500, "Propofol", f).MeanArterialPressure_mmHg / map0;
        double low = propDrop(1 / 3.0), std = propDrop(1), high = propDrop(5 / 3.0);
        Check(low < std && std < high, $"Propofol im Schock: 0,5 mg/kg −{low:P0}, 1,5 −{std:P0}, 2,5 −{high:P0}");
        Check(low < 0.2, $"reduzierte Dosis im Schock verträglicher: −{low:P0}");
        // Fentanyl 300 µg beim spontan atmenden Patienten: Apnoe-nahe Atemdepression
        double rr0 = Trias().Current.RespirationRate_bpm;
        var f3 = bolus(0, "Fentanyl", 3); var f1 = bolus(0, "Fentanyl", 1);
        Check(f3.RespirationRate_bpm < f1.RespirationRate_bpm && f3.SpO2_percent < 90, $"Fentanyl 300 µg: AF {f3.RespirationRate_bpm:F0} (100 µg: {f1.RespirationRate_bpm:F0}, ohne {rr0:F0}), SpO2 {f3.SpO2_percent:F0}");
        // Wirkdauer wächst mit der Dosis
        Check(MockPhysiologyEngine.DrugLevel(MockPhysiologyEngine.Drugs["Fentanyl"], 35, 3) > MockPhysiologyEngine.DrugLevel(MockPhysiologyEngine.Drugs["Fentanyl"], 35, 1),
              "höhere Dosis wirkt länger");
        // Fibrinogen 4 g hebt doppelt so viel wie 2 g; TXA 0,5 g wirkt, 0 nicht
        double fib2 = bolus(0, "Fibrinogen", 1).Fibrinogen_g_L - 2.5, fib4 = bolus(0, "Fibrinogen", 2).Fibrinogen_g_L - 2.5;
        Check(Math.Abs(fib4 - 2 * fib2) < 1e-6, $"Fibrinogen 2 g +{fib2:F2}, 4 g +{fib4:F2} g/L");
        // Noradrenalin: Rate verstellen, sättigend, stoppen
        Func<double[], double> na = rates =>
        {
            var x = Trias(1500);
            foreach (var r in rates) x.ApplyIntervention(new InterventionEvent { Type = InterventionType.Vasopressor, DoseFactor = r });
            Advance(x, 10); return x.Current.SystolicBP_mmHg;
        };
        double sbp0 = na(new double[0]), sbp1 = na(new[] { 1.0 }), sbp3 = na(new[] { 3.0 }), sbp10 = na(new[] { 10.0 }), stop = na(new[] { 3.0, 0 });
        Check(Math.Abs(sbp1 - sbp0 - 20) < 0.5 && sbp3 > sbp1 && sbp10 - sbp0 < 40, $"Noradrenalin RRsys ohne {sbp0:F0}, 0,1 {sbp1:F0}, 0,3 {sbp3:F0}, 1,0 {sbp10:F0}");
        Check(Math.Abs(stop - sbp0) < 0.5, "Perfusor aus: Wirkung weg");
    }

    static void ImpactModelChecks()
    {
        var m = Load<NotfallSIM.Crash.ImpactModelFile>("impact_model.json");
        var objs = Load<NotfallSIM.Crash.SandboxObjectFile>("objects.json");
        var injuries = new HashSet<string>(Injuries.injuries.Select(i => i.id));
        var srcIds = new HashSet<string>(m.sources.Select(x => x.id));
        foreach (var r in m.regions)
        {
            Check(r.k1_N_m > 0 && r.k2_N_m >= r.k1_N_m && r.knee2_m > r.knee1_m && r.depth_m > 0 && r.effectiveMass_kg > 0, r.region + ": Kennwerte");
            Check(r.evidence == "annahme" || srcIds.Contains(r.sourceId), r.region + ": Quelle fehlt");
            Check(r.areaStiffness.Zip(r.areaStiffness.Skip(1), (x, y) => y.area_cm2 > x.area_cm2).All(b => b), r.region + ": Flächen-Stützstellen aufsteigend");
        }
        // Faustschlag (Walilko 2005): 2,9 kg, 9,14 m/s gegen den Kopf → im Mittel 3427 ± 811 N
        var faust = objs.objects.First(o => o.id == "faust");
        var head = NotfallSIM.Crash.ImpactModel.Region(m, "Kopf");
        var punch = NotfallSIM.Crash.ImpactModel.Simulate(head, faust.mass_kg, 9.14, faust.contactArea_cm2, faust.stiffness_N_m);
        Check(punch.peakForce_N > 2400 && punch.peakForce_N < 5000, $"Faustschlag {punch.peakForce_N:0} N (Walilko: 3427 ± 811 N)");
        // Thoraxpendel (Kroell): 23,4 kg mit 6,7 m/s → ca. 40 % Kompression
        var thorax = NotfallSIM.Crash.ImpactModel.Region(m, "Thorax");
        var pend = NotfallSIM.Crash.ImpactModel.Simulate(thorax, 23.4, 6.7, 150);
        double comp = pend.maxDelta_m / thorax.depth_m;
        Check(comp > 0.28 && comp < 0.55, $"Thoraxpendel 6,7 m/s: Kompression {comp:P0} (Kroell: ca. 40 %), Kraft {pend.peakForce_N:0} N");
        var slow = NotfallSIM.Crash.ImpactModel.Simulate(thorax, 23.4, 4.3, 150);
        Check(slow.maxDelta_m < pend.maxDelta_m, "langsamer → weniger Kompression");
        // Abdomen (Hardy 2001): starrer Stab 48 kg, 6,3 m/s → Kompression unter ca. 50 %
        var abd = NotfallSIM.Crash.ImpactModel.Region(m, "Abdomen");
        var bar = NotfallSIM.Crash.ImpactModel.Simulate(abd, 48, 6.3, 100);
        Check(bar.maxDelta_m / abd.depth_m > 0.15 && bar.maxDelta_m / abd.depth_m < 0.6, $"Bauch 48 kg 6,3 m/s: Kompression {bar.maxDelta_m / abd.depth_m:P0}");
        // Kontaktfläche: kleiner Hammer gibt am Kopf weichere Steifigkeit als große Platte (Allsop)
        Check(NotfallSIM.Crash.ImpactModel.Stiffness(head, 0.001, 5) < NotfallSIM.Crash.ImpactModel.Stiffness(head, 0.001, 52), "Kopfsteifigkeit wächst mit der Fläche");
        // Frakturen
        Check(Math.Abs(NotfallSIM.Crash.ImpactModel.SkullFractureProbability(m, 5000, 5) - 0.5) < 0.01, "Schädel 5 kN / 5 cm² = 50 %");
        Check(Math.Abs(NotfallSIM.Crash.ImpactModel.SkullFractureProbability(m, 12500, 52) - 0.5) < 0.01, "Schädel 12,5 kN / 52 cm² = 50 %");
        Check(NotfallSIM.Crash.ImpactModel.SkullFractureProbability(m, 3500, 30) < 0.01, "Faustschlag bricht den Schädel nicht");
        var femur = m.fractures.longBones.First(b => b.segment == "Oberschenkel");
        Check(Math.Abs(NotfallSIM.Crash.ImpactModel.LongBoneProbability(femur, 4 * 447 / 0.43, 0.43) - 0.5) < 0.01, "Femur 50 % bei 447 Nm");
        // Durchstich (Ní Annaidh 2015)
        double knife = NotfallSIM.Crash.ImpactModel.SkinPenetrationForce(m, objs.objects.First(o => o.id == "messer"), false);
        double screw = NotfallSIM.Crash.ImpactModel.SkinPenetrationForce(m, objs.objects.First(o => o.id == "schraubendreher"), false);
        Check(knife > 10 && knife < 25 && screw > 2 * knife, $"Durchstich Messer {knife:0} N, Schraubendreher {screw:0} N");
        Check(Math.Abs(NotfallSIM.Crash.ImpactModel.SkinPenetrationForce(m, objs.objects.First(o => o.id == "messer"), true) - 1.5 * knife) < 1e-9, "Kleidung +50 %");
        // Stichkanal → Verletzung
        Func<double> zero = () => 0.0;
        var deep = NotfallSIM.Crash.ImpactModel.Evaluate(m, null, new[] { new NotfallSIM.Crash.PenetrationResult { Region = "Thorax", Side = -1, Depth_m = 0.06 } }, zero);
        Check(deep.Count == 1 && deep[0].InjuryId == "stichverletzung_thorax_links" && deep[0].Probability == 1, "tiefer Stich links → Hämatopneumothorax links");
        Check(NotfallSIM.Crash.ImpactModel.Evaluate(m, null, new[] { new NotfallSIM.Crash.PenetrationResult { Region = "Thorax", Side = -1, Depth_m = 0.06, HitBone = true } }, zero).Count == 0, "Klinge an der Rippe gestoppt → keine innere Verletzung");
        Check(NotfallSIM.Crash.ImpactModel.Evaluate(m, null, new[] { new NotfallSIM.Crash.PenetrationResult { Region = "Thorax", Side = 1, Depth_m = 0.02 } }, zero).Count == 0, "flacher Stich → nur Weichteil");
        Check(NotfallSIM.Crash.ImpactModel.Evaluate(m, null, new[] { new NotfallSIM.Crash.PenetrationResult { Region = "Arm rechts", Segment = "Oberarm rechts", Side = 1, Depth_m = 0.05 } }, zero).Count == 0, "Oberarm: kein Unterarmgefäß");
        var blunt = NotfallSIM.Crash.ImpactModel.Evaluate(m, new[] {
            new NotfallSIM.Crash.BluntContact { Region = "Kopf", Segment = "Kopf", PeakForce_N = 9000, Area_cm2 = 5 },
            new NotfallSIM.Crash.BluntContact { Region = "Oberschenkel links", Segment = "Oberschenkel links", PeakForce_N = 6000, SegmentLength_m = 0.43, Side = -1 },
            new NotfallSIM.Crash.BluntContact { Region = "Thorax", Segment = "Thorax", MaxIndentation_m = 0.1, Side = 1 } }, null, zero);
        Check(blunt.Any(x => x.InjuryId == "schaedelfraktur" && x.Probability > 0.9), "Hammer 9 kN → Schädelfraktur");
        Check(blunt.Any(x => x.InjuryId == "femur_schaft_links" && x.Probability > 0.9), "6 kN quer → Femurfraktur links");
        Check(blunt.Any(x => x.InjuryId == "rippenserienfraktur_rechts" && x.Probability > 0.5), "43 % Kompression rechts → Rippenserie rechts");
        // Jede Verletzung im Modell existiert
        foreach (var id in new[] { m.fractures.skull.injuryId, m.fractures.ribs.injuryLeft, m.fractures.ribs.injuryRight }
                 .Concat(m.fractures.longBones.SelectMany(b => new[] { b.injuryLeft, b.injuryRight }))
                 .Concat(m.penetration.regions.SelectMany(r => new[] { r.deep, r.deepLeft, r.deepRight })).Where(x => !string.IsNullOrEmpty(x)))
            Check(injuries.Contains(id), "unbekannte Verletzung " + id);
        // Gegenstände
        var objSrc = new HashSet<string>(objs.sources.Select(x => x.id));
        foreach (var o in objs.objects)
        {
            Check(new[] { "stumpf", "spitz" }.Contains(o.kind) && new[] { "werfen", "gefuehrt", "statisch" }.Contains(o.mode) && new[] { "box", "sphere", "capsule" }.Contains(o.shape), o.id + ": Art/Modus/Form");
            Check(o.size_m.Length == 3 && o.size_m.All(x => x > 0), o.id + ": Größe");
            Check(o.mode == "statisch" || (o.mass_kg > 0 && o.minSpeed_m_s <= o.defaultSpeed_m_s && o.defaultSpeed_m_s <= o.maxSpeed_m_s), o.id + ": Masse/Geschwindigkeit");
            Check(!o.Sharp || (o.tipAngle_deg > 0 && o.bladeLength_m > 0), o.id + ": Klinge");
            Check(string.IsNullOrEmpty(o.sourceId) || objSrc.Contains(o.sourceId), o.id + ": Quelle");
        }
        // Szenario aus direkten Verletzungen (Gewalt-Sandbox): sicher, reproduzierbar, ohne Messwerte
        var direct = NotfallSIM.Crash.ImpactModel.Evaluate(m, null, new[] { new NotfallSIM.Crash.PenetrationResult { Region = "Thorax", Side = -1, Depth_m = 0.07, Embedded = true } }, zero);
        var gs = NotfallSIM.Crash.InjuryRisk.BuildScenario(Risk, Mechanisms, Injuries.injuries, new List<NotfallSIM.Crash.CrashMeasurement>(),
            new NotfallSIM.Crash.CrashSetup { MechanismId = "gewalt", Rescue_min = 15 }, 7, direct, "Stumpfe/spitze Gewalt", "Messerstich links thorakal.").Scenario;
        Check(gs.injuries.Length == 1 && gs.injuries[0].injuryId == "stichverletzung_thorax_links" && gs.injuries[0].onsetTime_s == 0, "Gewalt-Szenario: Stichverletzung übernommen");
        Check(gs.title == "Stumpfe/spitze Gewalt" && gs.description.StartsWith("Messerstich") && gs.assessment.Length > 0, "Gewalt-Szenario: Titel, Text, Bewertung");
        var none = NotfallSIM.Crash.InjuryRisk.BuildScenario(Risk, Mechanisms, Injuries.injuries, new List<NotfallSIM.Crash.CrashMeasurement>(),
            new NotfallSIM.Crash.CrashSetup { MechanismId = "gewalt" }, 7, new List<NotfallSIM.Crash.DirectInjury>(), "Stumpfe/spitze Gewalt", "Nichts.").Scenario;
        Check(none.injuries.Length == 0, "ohne Einwirkung keine Verletzung");
        // Stichwunde als sichtbares Zeichen
        var stab = NotfallSIM.Anatomy.CrashMarks.Stab("Thorax", "Thorax", new[] { -0.08f, 0.35f, 0.12f }, new[] { -0.05f, 0.35f, 0f }, "Messerstich", 0.07, true, 1);
        Check(stab.Mark.kind == "stichwunde" && stab.Finding.Contains("steckendem Fremdkörper") && stab.Finding.Contains("ventral"), "Stichwunde: " + stab.Finding);
        Check(NotfallSIM.Anatomy.CrashMarks.Stab("Thorax", "Thorax", null, null, "x", 0.01, false, 2) == null, "Stichwunde ohne Ort → keine Marke");
    }

    static void CoachChecks()
    {
        var s = Scenarios.First(x => x.id == "motorrad_beckenfraktur");
        var defs = Injuries.injuries.ToDictionary(d => d.id);
        var log = new List<LogEntry>();
        var shock = new VitalSigns { SimTime_s = 30, HeartRate_bpm = 125, SystolicBP_mmHg = 88, DiastolicBP_mmHg = 60, RespirationRate_bpm = 24,
                                     SpO2_percent = 95, EtCO2_mmHg = 30, GCS = 14, BaseExcess = -7, IonizedCalcium = 1.15, Temperature_C = 36.3 };
        Func<double, CoachContext> ctx = t => { var v = shock; v.SimTime_s = t;
            return new CoachContext { Scenario = s, Injury = id => defs[id], Interventions = Interventions.interventions, Imaging = Imaging.modalities, Events = log, Vitals = v }; };
        Action<double, string> ev = (t, tag) => log.Add(new LogEntry { Time_s = t, Tag = tag, Message = tag });

        var tip = Coach.Tip(ctx(30));
        Check(tip.Phase == "B" && tip.Key == "o2" && tip.Deadline_s == 120, "zuerst Sauerstoff (B, Ziel 2 min): " + tip);
        ev(31, "intervention:Oxygen::o2");
        tip = Coach.Tip(ctx(35));
        Check(tip.Key == "exam:Thorax", "dann Thorax untersuchen: " + tip);
        ev(40, "exam:Thorax");
        tip = Coach.Tip(ctx(45));
        Check(tip.Phase == "C" && tip.Key == "exam:Becken", "im Schock: Becken untersuchen (Ziel 3 min) – nicht schon Beckengurt: " + tip);
        Check(Coach.Tip(ctx(45)).Key != "beckengurt" && !Coach.Strategy(ctx(45)).All(i => i.Key != "beckengurt"), "Beckengurt nur in der Strategie, solange das Becken nicht untersucht ist");
        ev(50, "exam:Becken");
        tip = Coach.Tip(ctx(55));
        Check(tip.Key == "beckengurt", "nach dem Befund: Beckengurt: " + tip);
        ev(60, "intervention:HemorrhageControl:Pelvis:beckengurt");
        tip = Coach.Tip(ctx(65));
        Check(tip.Phase == "C", "weiter im C: " + tip);

        var strat = Coach.Strategy(ctx(65));
        Check(strat.Any(i => i.Key == "beckengurt" && i.Done_s == 60), "Strategie: Beckengurt erledigt um 01:00");
        Check(strat.Any(i => i.Phase == "Vermeiden" && i.Action.Contains("Kristalloide")), "Strategie nennt, was zu vermeiden ist");
        Check(strat.All(i => i.Key != "intubation"), "keine Intubation ohne SHT in der Strategie");
        var order = strat.Where(i => i.Phase != "Vermeiden").Select(i => "xABCDE".IndexOf(i.Phase.Length == 1 ? i.Phase[0] : 'Z')).Select(k => k < 0 ? 9 : k).ToList();
        Check(order.SequenceEqual(order.OrderBy(k => k)), "Strategie in xABCDE-Reihenfolge");

        // Sichtbare Blutung (Amputation) kommt vor allem anderen
        var amp = new ScenarioDefinition { id = "t", injuries = new[] { new ScenarioInjury { injuryId = "amputation_unterschenkel_rechts", severity = 0.8 } },
                                           assessment = defs["amputation_unterschenkel_rechts"].criteria, patient = new PatientProfile() };
        var c2 = new CoachContext { Scenario = amp, Injury = id => defs[id], Interventions = Interventions.interventions, Imaging = Imaging.modalities, Events = new List<LogEntry>(), Vitals = shock };
        Check(Coach.Tip(c2).Key == "tourniquet" && Coach.Tip(c2).Phase == "x", "Amputation: zuerst Tourniquet (x): " + Coach.Tip(c2));
        // GCS ≤ 8: Atemweg vor allem außer x
        var coma = shock; coma.GCS = 6;
        var c3 = new CoachContext { Scenario = s, Injury = id => defs[id], Interventions = Interventions.interventions, Imaging = Imaging.modalities, Events = new List<LogEntry>(), Vitals = coma };
        Check(Coach.Tip(c3).Phase == "A", "GCS 6: Atemweg zuerst");
    }

    static void PatientMonitorChecks()
    {
        var v = new VitalSigns { SimTime_s = 0, HeartRate_bpm = 80, SystolicBP_mmHg = 120, DiastolicBP_mmHg = 80, MeanArterialPressure_mmHg = 93,
                                 RespirationRate_bpm = 14, SpO2_percent = 98, EtCO2_mmHg = 38, Temperature_C = 36.5 };
        var n = new NibpCycle { Interval_s = 150 };
        n.Update(v);
        Check(n.Measuring && !n.HasReading, "Messung startet sofort");
        v.SimTime_s = NibpCycle.Measure_s; v.SystolicBP_mmHg = 110; n.Update(v);
        Check(!n.Measuring && n.HasReading && n.Sys == 110 && n.MeasuredAt_s == NibpCycle.Measure_s, "Wert am Ende der Messung");
        v.SimTime_s = 100; v.SystolicBP_mmHg = 70; n.Update(v);
        Check(!n.Measuring && n.Sys == 110, "zwischen den Messungen bleibt der alte Wert stehen");
        v.SimTime_s = 150; n.Update(v); Check(n.Measuring, "nächste Messung nach dem Intervall (ab Beginn der letzten)");
        v.SimTime_s = 175; n.Update(v); Check(n.Sys == 70, "neuer Wert");
        n.StartNow(180); v.SimTime_s = 205; v.SystolicBP_mmHg = 35; n.Update(v);
        Check(n.HasReading && n.Failed, "unter 40 mmHg schlägt die Messung fehl");
        n.CycleInterval(210); Check(n.Interval_s == 300, "Intervall 2,5 → 5 min");
        n.CycleInterval(210); Check(n.Interval_s == 0, "dann manuell");
        v.SimTime_s = 10000; n.Update(v); Check(!n.Measuring, "manuell: keine automatische Messung");

        var L = new AlarmLimits();
        var ok = new VitalSigns { HeartRate_bpm = 80, SystolicBP_mmHg = 120, RespirationRate_bpm = 14, SpO2_percent = 98, EtCO2_mmHg = 38, Temperature_C = 36.5 };
        Check(PatientMonitorLogic.Evaluate(ok, null, L).Count == 0, "Normalwerte: kein Alarm");
        var shock = ok; shock.HeartRate_bpm = 135; shock.SpO2_percent = 82; shock.Temperature_C = 34.2; shock.SystolicBP_mmHg = 75; shock.BloodLossFraction = 0.35;
        var a = PatientMonitorLogic.Evaluate(shock, null, L);
        Check(a[0].Priority == AlarmPriority.High && a[0].Parameter == "SpO2", "Desaturation zuerst (hoch): " + string.Join(" | ", a));
        Check(a.Any(x => x.Parameter == "HF" && x.Priority == AlarmPriority.Medium) && a.Any(x => x.Parameter == "Temp" && x.Priority == AlarmPriority.Low), "HF mittel, Temp niedrig");
        var arrest = ok; arrest.CardiacArrest = true;
        var ar = PatientMonitorLogic.Evaluate(arrest, null, L);
        Check(ar.Count == 1 && ar[0].Text == "ASYSTOLIE" && ar[0].Priority == AlarmPriority.High, "Stillstand");
        var weak = ok; weak.SystolicBP_mmHg = 45;
        Check(!PatientMonitorLogic.SpO2Valid(weak) && PatientMonitorLogic.Evaluate(weak, null, L).Any(x => x.Text.Contains("kein Pulssignal")), "kein Pulssignal bei sys 45");
        var apnea = ok; apnea.RespirationRate_bpm = 2;
        Check(PatientMonitorLogic.Evaluate(apnea, null, L)[0].Text == "APNOE", "Apnoe spontan atmend");
        apnea.Intubated = true;
        Check(PatientMonitorLogic.Evaluate(apnea, null, L).All(x => x.Text != "APNOE"), "beatmet: keine Apnoe-Meldung");
        var nb = new NibpCycle(); var low = ok; low.SystolicBP_mmHg = 65; nb.Update(low); low.SimTime_s = 30; nb.Update(low);
        Check(PatientMonitorLogic.Evaluate(ok, nb, L).Any(x => x.Parameter == "NIBP" && x.Priority == AlarmPriority.High), "NIBP-Alarm aus der letzten Messung, nicht aus dem aktuellen Wert");
        Check(PatientMonitorLogic.PerfusionIndex(ok) > 2 && PatientMonitorLogic.PerfusionIndex(shock) < 1.5, $"PI normal {PatientMonitorLogic.PerfusionIndex(ok)}, Schock {PatientMonitorLogic.PerfusionIndex(shock)}");
    }

    static void NewInterventionData()
    {
        var injuries = new HashSet<string>(Injuries.injuries.Select(i => i.id));
        foreach (var iv in Interventions.interventions)
        {
            foreach (var c in iv.contraindicatedBy ?? new string[0]) Check(injuries.Contains(c), $"{iv.id}: Kontraindikation {c} unbekannt");
            if (iv.contraindicatedBy != null && iv.contraindicatedBy.Length > 0) Check(!string.IsNullOrEmpty(iv.contraindicationNote), $"{iv.id}: Begründung fehlt");
            if (iv.type == "Medication")
                Check(new[] { "TXA", "Fibrinogen", "Calcium", "Antibiotic" }.Contains(iv.compound) || MockPhysiologyEngine.Drugs.ContainsKey(iv.compound), $"{iv.id}: Medikament {iv.compound} ohne Wirkung");
        }
        var v = Trias(800).Current;
        string r = BloodGas.CoagReport(v);
        Check(r.Contains("Quick 100 %") && r.Contains("INR 1,0") && r.Contains("Fibrinogen 2,5"), r);
        Check(Math.Abs(BloodGas.Inr(0.5) - 1.5) < 1e-9, "INR bei 50 %");
    }

    static void PositioningPhysics()
    {
        var flat = MockPhysiologyEngine.Positions["flach"];
        Check(Math.Abs(MockPhysiologyEngine.HydrostaticMmHgPerM - 78.0) < 0.1, "ρg in mmHg/m");
        Check(MockPhysiologyEngine.SiteHeight(0.66, "trunk", flat, 1.8) == 0 && MockPhysiologyEngine.SiteHeight(0.162, "legR", flat, 1.8) == 0, "flach nicht auf Herzhöhe");
        double arm = MockPhysiologyEngine.SiteHeight((0.630 + 0.485) / 2, "armL", MockPhysiologyEngine.Positions["arm_links_hoch"], 1.8);
        Check(arm > 0.4 && arm < 0.5, $"Unterarm senkrecht {arm:F2} m über dem Herzen");
        double leg = MockPhysiologyEngine.SiteHeight((0.285 + 0.039) / 2, "legR", MockPhysiologyEngine.Positions["beine_hoch"], 1.8);
        Check(leg > 0.4 && leg < 0.5, $"Unterschenkel bei 45° {leg:F2} m");
        double liver = MockPhysiologyEngine.SiteHeight(0.66, "trunk", MockPhysiologyEngine.Positions["oberkoerper_30"], 1.8);
        Check(liver < 0 && liver > -0.1, $"Leber bei Oberkörper 30° {liver:F3} m (unter dem Herzen)");

        Func<string, string, string, double, double, double> rate = (compartment, lesion, position, baseRate, seconds) =>
        {
            var e = new MockPhysiologyEngine(1); e.Initialize(new PatientProfile { weight_kg = 80, height_cm = 180 });
            if (position != null) e.ApplyIntervention(new InterventionEvent { Type = InterventionType.Positioning, TargetCompartment = position });
            e.ApplyInjury(new InjuryEvent { InjuryId = "x", Effect = InjuryEffectType.Hemorrhage, Compartment = compartment, Severity = 1, BaseRate_mL_min = baseRate, Lesion = lesion });
            Advance(e, seconds);
            return e.InjuryBloodLoss_mL("x") / (seconds / 60);
        };
        // Venös am Oberschenkel: 15 cm über dem Herzen übersteigen den venösen Druck (7 mmHg) — Blutung steht
        double vFlat = rate("RightThigh", "venous", null, 20, 60), vUp = rate("RightThigh", "venous", "bein_rechts_hoch", 20, 60);
        Check(vFlat > 15 && vUp < 0.5, $"venös flach {vFlat:F1}, hoch {vUp:F1} mL/min");
        // Arteriell am Unterschenkel: Hochlagern senkt, stoppt aber nicht
        double aFlat = rate("Extremity", "partial", null, 120, 60), aUp = rate("Extremity", "partial", "bein_rechts_hoch", 120, 60);
        Check(aUp < 0.85 * aFlat && aUp > 0.5 * aFlat, $"arteriell flach {aFlat:F0}, hoch {aUp:F0} mL/min");
        // Arm hoch: Schnittverletzung deutlich weniger
        double armFlat = rate("LeftArm", "partial", null, 40, 60), armUp = rate("LeftArm", "partial", "arm_links_hoch", 40, 60);
        Check(armUp < 0.85 * armFlat, $"Arm flach {armFlat:F0}, hoch {armUp:F0} mL/min");
        // Beine hoch: Autotransfusion hebt den Blutdruck des hypovolämen Patienten, ohne das Blutvolumen zu ändern
        Func<string, VitalSigns> hypo = position =>
        {
            var e = new MockPhysiologyEngine(1); e.Initialize(new PatientProfile { weight_kg = 80, height_cm = 180, initialBloodLoss_mL = 1600 });
            if (position != null) e.ApplyIntervention(new InterventionEvent { Type = InterventionType.Positioning, TargetCompartment = position });
            return e.Current;
        };
        var vf = hypo(null); var vp = hypo("beine_hoch");
        Check(vp.SystolicBP_mmHg > vf.SystolicBP_mmHg && vp.BloodVolume_mL == vf.BloodVolume_mL && vp.BloodLossFraction == vf.BloodLossFraction, "Autotransfusion");
        foreach (var iv in Interventions.interventions.Where(x => x.type == "Positioning"))
            Check(MockPhysiologyEngine.Positions.ContainsKey(iv.target) && !iv.once, iv.id + ": unbekannte Lagerung oder einmalig");
    }

    static void SkinData()
    {
        var p = Load<SkinParams>("skin.json");
        var src = new HashSet<string>(p.sources.Select(x => x.id));
        foreach (var id in new[] { p.pallorSourceId, p.sweatSourceId, p.mottlingSourceId, p.cyanosisSourceId, p.extractionSourceId, p.reflectanceSourceId, p.wrapSourceId })
            Check(src.Contains(id), "Quelle fehlt: " + id);
        Func<double, double, double, VitalSigns> vs = (loss, spo2, hb) => new VitalSigns { BloodLossFraction = loss, SpO2_percent = spo2, Hb = hb };
        var normal = SkinPerfusion.Evaluate(p, vs(0, 98, 15));
        Check(normal.Pallor == 0 && normal.Sweat == 0 && normal.Mottling == 0 && normal.Cyanosis == 0, "gesund nicht unauffällig");
        var c2 = SkinPerfusion.Evaluate(p, vs(0.2, 98, 13));
        Check(c2.Pallor > 0 && c2.Sweat == 0, "Klasse II: blass, nicht schweißig");
        var c4 = SkinPerfusion.Evaluate(p, vs(0.45, 98, 9));
        Check(c4.Pallor == 1 && c4.Sweat == 1 && c4.Mottling > 0, "Klasse IV: blass, schweißig, marmoriert");
        // Zyanose: Desoxy-Hb im Kapillarbett; gesund ≈ 2 g/dL
        double d = SkinPerfusion.CapillaryDeoxyHb(15, 98, p.oxygenExtraction);
        Check(d > 1.5 && d < 3, $"Desoxy-Hb gesund {d:F1} g/dL");
        Check(SkinPerfusion.Evaluate(p, vs(0, 75, 15)).Cyanosis > 0, "keine Zyanose bei SpO2 75 % und Hb 15");
        Check(SkinPerfusion.Evaluate(p, vs(0, 75, 7)).Cyanosis == 0, "Zyanose trotz Anämie (Hb 7) bei SpO2 75 %");
        Check(SkinPerfusion.Evaluate(p, new VitalSigns { CardiacArrest = true, SpO2_percent = 98, Hb = 12 }).Pallor == 1, "Stillstand nicht blass");
    }

    static void CrashMarksData()
    {
        var r = Load<CrashMarkRules>("crash_marks.json");
        var src = new HashSet<string>(r.sources.Select(x => x.id));
        Check(src.Contains(r.bruiseSourceId) && src.Contains(r.lacerationSourceId), "Quellen");
        var data = AnatomyFile.Read(File.ReadAllBytes(Path.Combine(DataRoot, "anatomy", "body.bin")), json =>
        {
            var ser = new DataContractJsonSerializer(typeof(AnatomyHeader));
            using (var st = new MemoryStream(Encoding.UTF8.GetBytes(json))) return (AnatomyHeader)ser.ReadObject(st);
        });
        var skin = data.Meshes[0]; int tc = skin.Info.submeshes[0] / 3; float scale = 1.75f / data.Header.modelHeight;
        var nrm = SkinMarks.VertexNormals(skin.Positions, skin.Indices, skin.Indices.Length / 3);
        var adj = SkinMarks.Adjacency(skin.Info.vertexCount, skin.Indices, tc);
        Func<string, float[]> centroid = name => { var m = data.Meshes.First(x => x.Info.name == name); var c = new float[3]; for (int i = 0; i < m.Positions.Length; i++) c[i % 3] += m.Positions[i] / (m.Positions.Length / 3); return c; };
        Func<string, float[]> joint = seg => data.Header.segments.First(x => x.name == seg).joint;
        var head = new ContactSummary { Segment = "Kopf", Region = "Kopf", Surface = "vehicle", Impulse_Ns = 60, PeakForce_N = 5000,
                                        ModelPoint = centroid("frontal bone"), ModelInside = new[] { 0f, centroid("frontal bone")[1], 0f } };
        var thigh = new ContactSummary { Segment = "Oberschenkel links", Region = "Oberschenkel links", Surface = "ground", Impulse_Ns = 30, PeakForce_N = 1500, MaxSlide_m_s = 4,
                                         ModelPoint = Add(centroid("left femur"), -0.06f, 0, 0), ModelInside = centroid("left femur") };
        var weak = new ContactSummary { Segment = "Unterarm rechts", Region = "Arm rechts", Surface = "vehicle", Impulse_Ns = 2, PeakForce_N = 300,
                                        ModelPoint = centroid("right radius"), ModelInside = joint("Unterarm rechts") };
        var marks = CrashMarks.Generate(r, new[] { head, thigh, weak }, 120, 80);
        Func<string, int> count = kind => marks.Count(x => x.Mark.kind == kind);
        Check(marks.Any(x => x.Mark.kind == "platzwunde" && x.Region == "Kopf"), "keine Platzwunde am Kopf bei 5 kN");
        Check(marks.Any(x => x.Mark.kind == "schuerfung" && x.Region == "Oberschenkel links"), "keine Schürfung beim Gleiten über die Straße");
        Check(!marks.Any(x => x.Region == "Arm rechts"), "Zeichen trotz Impuls unter der Schwelle");
        Check(marks.Count(x => x.Mark.id.StartsWith("crash_gurt")) == 2, "Gurtmarken fehlen");
        Check(!CrashMarks.Generate(r, new ContactSummary[0], 1, 1).Any(), "Gurtmarke unter der Schwelle");
        foreach (var cm in marks)
        {
            var p = SkinMarks.Select(data, cm.Mark, scale, tc, nrm, adj);
            Check(p != null && !p.HiddenByShorts && p.Triangles.Length > 0, cm.Mark.id + ": kein sichtbarer Hautort");
            if (!cm.Mark.id.StartsWith("crash_gurt")) Check(p.HitRegion == cm.Region, $"{cm.Mark.id}: Region {p.HitRegion} statt {cm.Region}");
        }
        Check(CrashMarks.FaceLabel(new float[] { 0, 0, 1 }) == "ventral" && CrashMarks.FaceLabel(new float[] { -1, 0, 0.2f }) == "links seitlich", "Seitenbezeichnung");
    }

    static float[] Add(float[] a, float x, float y, float z) => new[] { a[0] + x, a[1] + y, a[2] + z };

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
        foreach (var id in new[] { bp.surfaceTensionSourceId, bp.densitySourceId, bp.formulaSourceId, f.spurt.sourceId, f.drying.sourceId }) Check(src.Contains(id), "Quelle fehlt: " + id);
        foreach (var k in f.kinds) Check(k.depth >= 0 && k.depth <= 1, k.id + ": Tiefe");
        foreach (var id in new[] { "schnitt", "stichwunde", "riss_quetschwunde", "offene_fraktur" }) Check(f.kinds.First(k => k.id == id).depth > 0, id + ": ohne Tiefe");
        Check(SkinMarks.Dryness(f.drying, 0) == 0 && SkinMarks.Dryness(f.drying, f.drying.wetToDry_min * 60) == 1, "Eintrocknung");

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
        // Hämatom: erst ab der Schwelle, Radius wächst mit √Volumen (Fläche = Volumen / Schichtdicke); Diffusion vernachlässigbar
        var hem = f.marks.First(x => x.subcutaneousShare > 0);
        Func<double, ExamContext> ctx = lost => new ExamContext { Time_s = 100, Lost_mL = _ => lost, Level = _ => 0, Events = new List<LogEntry>() };
        Check(SkinMarks.Amount(hem, ctx(hem.when.minLost_mL - 1)) == 0, "Hämatom vor der Schwelle sichtbar");
        double a1 = SkinMarks.Amount(hem, ctx(hem.when.minLost_mL + 100)), a4 = SkinMarks.Amount(hem, ctx(hem.when.minLost_mL + 400));
        Check(a4 < 1 && Math.Abs(a4 / a1 - 2) < 1e-6, $"Hämatomradius nicht ∝ √Volumen ({a1:F3}, {a4:F3})");
        Check(SkinMarks.DiffusionLength_m(f.hematoma.hbDiffusivity_m2_s, 3600) < 1e-4, "Diffusion in einer Stunde nicht vernachlässigbar");
        Check(src.Contains(f.hematoma.sourceId), "Quelle Hämatom");
        var bruise = f.kinds.First(k => k.id == "prellmarke");
        Check(SkinMarks.ColorAge(bruise, 0) == 0 && SkinMarks.ColorAge(bruise, bruise.ageTime_min * 60) == 1, "Farbalter");

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

        // Rinnsale: jeder Schritt abwärts, Ende an nach unten zeigender Haut oder in einer Mulde, Band zur Haut ausgerichtet
        Func<int, float[]> down = _ => new float[] { 0, 0, -1 };   // Rückenlage: Schwerkraft nach dorsal
        foreach (var m in f.marks.Where(x => x.rivulets > 0))
        {
            var pm = SkinMarks.Select(data, m, scale, tc, nrm, adj);
            var riv = BloodFlow.Trace(skin.Positions, nrm, adj, skin.Indices[3 * pm.HitTriangle], down, (float)(f.rivulets.maxLength_m / scale), 1, null);
            Check(riv.Path.Length >= 3, m.id + ": Rinnsal zu kurz");
            for (int i = 1; i < riv.Path.Length; i++)
            {
                int a0 = riv.Path[i - 1], b0 = riv.Path[i];
                float dz = skin.Positions[3 * b0 + 2] - skin.Positions[3 * a0 + 2];
                Check(dz < 0, m.id + ": Rinnsal läuft bergauf");
            }
            Check(riv.EdgeSlope.Length == riv.Path.Length - 1 && riv.EdgeSlope.All(x => x > 0) && Math.Abs(riv.EdgeEnd.Last() - 1) < 1e-5, m.id + ": Gefälle entlang des Pfads");
            BloodFlow.BuildStrip(riv, skin.Positions, nrm, 0.01f);
            for (int t = 0; t < riv.Triangles.Length; t += 3)
            {
                int i0 = riv.Triangles[t], i1 = riv.Triangles[t + 1], i2 = riv.Triangles[t + 2];
                Func<int, int, float> P = (v, c) => riv.Pos[3 * v + c];
                float ux = P(i1, 0) - P(i0, 0), uy = P(i1, 1) - P(i0, 1), uz = P(i1, 2) - P(i0, 2), vx = P(i2, 0) - P(i0, 0), vy = P(i2, 1) - P(i0, 1), vz = P(i2, 2) - P(i0, 2);
                float nx = uy * vz - uz * vy, ny2 = uz * vx - ux * vz, nz = ux * vy - uy * vx;
                Check(nx * riv.Nrm[3 * i0] + ny2 * riv.Nrm[3 * i0 + 1] + nz * riv.Nrm[3 * i0 + 2] >= 0, m.id + ": Rinnsal falsch ausgerichtet");
            }
        }
        // Bein 45° hoch (Unity: Euler(−45,0,0) am Oberschenkel): Schwerkraft im Beinsystem (0, sin45, −cos45) — Blut läuft zum Knie
        {
            var us = f.marks.First(x => x.id == "us_offene_fraktur");
            var pu = SkinMarks.Select(data, us, scale, tc, nrm, adj);
            int legR = data.SegmentIndex("Oberschenkel rechts"), shinR = data.SegmentIndex("Unterschenkel rechts");
            float sq = (float)Math.Sqrt(0.5);
            Func<int, float[]> raised = v => { int sg = SkinMarks.DominantSegment(skin.Bones, skin.Weight0, v); return sg == legR || sg == shinR ? new[] { 0, sq, -sq } : new float[] { 0, 0, -1 }; };
            int start = skin.Indices[3 * pu.HitTriangle];
            var rv = BloodFlow.Trace(skin.Positions, nrm, adj, start, raised, (float)(f.rivulets.maxLength_m / scale), 1, null);
            int end = rv.Path.Last();
            Check(skin.Positions[3 * end + 1] - skin.Positions[3 * start + 1] > 0.03f, "hochgelagertes Bein: Blut läuft nicht Richtung Knie");
        }
        Check(BloodFlow.Width_mm(f.rivulets, 0) == f.rivulets.widthMin_mm && BloodFlow.Width_mm(f.rivulets, 1e6) == f.rivulets.widthMax_mm, "Rinnsalbreite");
        // Rieselfilm: schneller bei mehr Gefälle und mehr Fluss, steht auf ebener Haut; Viskosität im gemessenen Bereich
        var rp = f.rivulets; double e0 = SkinMarks.PoolThickness_m(f.bloodPool), hh;
        Check(src.Contains(rp.viscositySourceId), "Quelle Viskosität");
        double q = 25e-6 / 60;   // 25 mL/min
        double uSteep = BloodFlow.FilmSpeed_m_s(rp, q, 0.01, 1.0, 1060, e0, out hh), uFlat = BloodFlow.FilmSpeed_m_s(rp, q, 0.01, 0.1, 1060, e0, out _);
        double uLow = BloodFlow.FilmSpeed_m_s(rp, q / 10, 0.01, 1.0, 1060, e0, out _);
        Check(uSteep > uFlat && uSteep > uLow && BloodFlow.FilmSpeed_m_s(rp, q, 0.01, 0, 1060, e0, out _) == 0, $"Rieselfilm {uSteep:F3}/{uFlat:F3}/{uLow:F3} m/s");
        Check(uSteep > 0.01 && uSteep < 0.5 && hh > 0 && hh <= e0, $"Rieselfilm senkrecht {uSteep * 100:F1} cm/s, {hh * 1000:F2} mm");
        Check(Math.Abs(BloodFlow.Viscosity_Pa_s(rp, rp.shearLow_per_s) * 1000 - rp.muLow_mPas) < 1e-6 && Math.Abs(BloodFlow.Viscosity_Pa_s(rp, rp.shearHigh_per_s) * 1000 - rp.muHigh_mPas) < 1e-6, "Viskosität an den Messpunkten");
        // Strahl: Luftwiderstand einer Kugel in plausibler Größe (bei 5 m/s weniger als die Schwerkraft)
        double kd = SkinMarks.DropDrag_per_m(f.spurt);
        Check(kd > 0 && kd * 25 < 9.81, $"Luftwiderstand k = {kd:F3}/m");

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
