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
    Core/          VitalSigns, Events, IPhysiologyEngine   (ohne Unity-Abhängigkeit)
    Engines/       MockPhysiologyEngine, PulsePhysiologyAdapter
    Data/          JSON-Datenklassen, InjuryDatabase
    Simulation/    SimulationManager, InterventionController
    UI/            VitalsMonitor
    Interaction/   BodyRegion
  StreamingAssets/NotfallSIM/
    injuries.json                Verletzungsdatenbank
    scenarios/*.json             Szenarien
```

## Einrichtung

1. **Unity Hub:** neues Projekt mit Unity 6 LTS (oder 2022.3 LTS), Vorlage „Universal 3D“, Name `NotfallSIM`.
2. **Dateien kopieren:** Inhalt dieses Ordners ins Projektverzeichnis (der `Assets`-Ordner wird zusammengeführt).
3. **TextMeshPro:** beim ersten Öffnen eines TMP-Elements „Import TMP Essentials“ bestätigen.
4. **Szene aufbauen:**
   - Leeres GameObject `Simulation` → `SimulationManager` (Engine = Mock).
   - Canvas mit TMP-Textfeldern für HF, RR, AF, SpO₂, etCO₂, Zeit, Status → `VitalsMonitor`, Felder zuweisen.
   - Buttons (Beckengurt, Kristalloid, EK, O₂) → `InterventionController`, OnClick verknüpfen.
   - Patient: vorerst Kapsel oder einfaches Modell; Kinder-Objekte mit Collidern und `BodyRegion` (Regionen `Becken`, `Oberschenkel rechts`, `Thorax`).
   - Für `BodyRegion`: *Project Settings › Player › Active Input Handling* auf **Both**.
5. **Play.** Zum schnellen Durchlaufen `timeScale` im Inspector auf 10 stellen. Die Konsole protokolliert Verletzungen und Maßnahmen.

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
