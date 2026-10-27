# NotfallSIM

Experimenteller Simulator für Schockraum- und Polytrauma-Szenarien.
Unity-Frontend, austauschbare Physiologie-Engine (eigener Mock oder Pulse Physiology Engine),
medizinisches Wissen in versionierten JSON-Dateien mit Quellenangaben.

> Experimentelles Lern- und Forschungsprojekt. Kein Medizinprodukt, nicht für klinische Entscheidungen.

## Architektur

```
 ┌──────────────── Unity ────────────────┐
 │  VitalsMonitor   InterventionController│   UI / Interaktion
 │  BodyRegion (Befunde per Klick)        │
 └───────────────┬───────────────────────┘
                 │
        SimulationManager                     Zeitsteuerung, Szenario-Ablauf, Log
           │            │
  InjuryDatabase   IPhysiologyEngine          gemeinsame Schnittstelle
  (JSON + Quellen)   ├─ MockPhysiologyEngine  ATLS-Lehrmodell, läuft sofort
                     └─ PulsePhysiologyAdapter  realistisch, nach Pulse-Import
```

Grundregeln:
- **Keine medizinischen Zahlen im Code.** Alles, was eine Quelle braucht, gehört nach `StreamingAssets/NotfallSIM/*.json`.
- **Jeder Datensatz hat `sources` und `evidenceStatus`** (`draft` → `reviewed` → `validated`).
- **Spiel spricht nur mit `IPhysiologyEngine`.** So lassen sich Mock und Pulse direkt vergleichen.

## Ordnerstruktur

```
Assets/
  NotfallSIM/Scripts/
    Core/          VitalSigns, Events, IPhysiologyEngine, SessionLog   (ohne Unity-Abhängigkeit)
    Engines/       MockPhysiologyEngine, PulsePhysiologyAdapter
    Data/          JSON-Datenklassen, InjuryDatabase
    Simulation/    SimulationManager, InterventionController, SessionRecorder
    UI/            VitalsMonitor, FindingsPanel, DebriefPanel
    Interaction/   BodyRegion
  NotfallSIM/Editor/
    SceneBuilder.cs              Menü „NotfallSIM › Schockraum-Szene erstellen“
  StreamingAssets/NotfallSIM/
    injuries.json                Verletzungsdatenbank
    scenarios/*.json             Szenarien
```

## Einrichtung

1. **Unity Hub:** neues Projekt mit Unity 6 LTS, Vorlage „Universal 3D“, Name `NotfallSIM`. Unity einmal schließen.
2. **Repository in den Projektordner holen** (Terminal im neuen Projektordner):
   ```
   git init -b main
   git remote add origin https://github.com/OutOfldeas/NotfallSIM.git
   git pull origin main
   git add -A
   git commit -m "Unity-Projekteinstellungen"
   git push -u origin main
   ```
3. **Projekt öffnen**, dann *Window › TextMeshPro › Import TMP Essential Resources*.
4. **Szene erzeugen:** Menü *NotfallSIM › Schockraum-Szene erstellen*. Das legt `Assets/NotfallSIM/Scenes/Schockraum.unity`
   mit Kamera, Trage, Platzhalter-Patient, Monitor, Maßnahmen, Befund- und Debriefing-Panel an — fertig verdrahtet.
5. **Play.** Körperregionen anklicken zum Untersuchen, Maßnahmen per Button, Zeitraffer 1x/5x/20x.
   „Szenario beenden“ zeigt das Debriefing und speichert die Verläufe als CSV
   (Ordner: `Application.persistentDataPath/Sessions`, wird im Debriefing angezeigt).
6. **Szenario wechseln:** im Inspector des Objekts `Simulation` das Feld *Scenario Id* setzen
   (`motorrad_beckenfraktur` oder `pkw_spannungspneu`).

## Pulse anbinden

1. *Pulse Physiology Engine* aus dem Unity Asset Store (kostenlos) importieren.
2. *Project Settings › Player › Scripting Define Symbols*: `NOTFALLSIM_PULSE` hinzufügen.
3. Alle mit `VERIFY` markierten Stellen in `PulsePhysiologyAdapter.cs` gegen die C#-HowTo-Beispiele
   der importierten Version prüfen (die API hat sich zwischen Pulse-Versionen geändert, v. a. bei `SEHemorrhage`).
4. Im `SimulationManager` Engine = Pulse wählen. Schlägt die Initialisierung fehl, fällt das Spiel auf den Mock zurück.

Erstes Validierungsexperiment: dasselbe Szenario mit Mock und Pulse laufen lassen, Verläufe
(HF, RR, Blutvolumen über die Zeit) als CSV exportieren und gegen die ATLS-Schockklassen vergleichen.

## Bekannte Grenzen des Mock-Modells

Keine Gerinnung, Hämodilution, Azidose oder Hypothermie; Kristalloide wirken mit festem Anteil von 30 %;
Stillstand ist irreversibel. Der Mock ist Lehr- und Testwerkzeug, nicht das Realismusziel.
