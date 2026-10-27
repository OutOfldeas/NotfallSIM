# NotfallSIM

![Tests](https://github.com/OutOfldeas/NotfallSIM/actions/workflows/tests.yml/badge.svg)

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
    interventions.json           Maßnahmen (Buttons, Volumina, Effektstärken, Quellen)
    imaging.json                 Bildgebung (eFAST, Röntgen, CCT, Ganzkörper-CT)
    mechanisms.json              Unfallmechanismen für Zufallsfälle (Verletzungswahrscheinlichkeiten)
    findings.json                Normalbefunde je Körperregion (Diagnose-Modus)
    differentials.json           Differenzialdiagnosen ohne Physiologie, mit „spricht dagegen“
    injury_risk.json             Unfall-Sandbox: Messwerte → Risikokurven → Verletzungen
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
   (`motorrad_beckenfraktur`, `pkw_spannungspneu`, `escooter_sht`, `fussgaengerin_becken_betablocker` oder `pkw_polytrauma`).
   **Zufallsfall:** Feld *Random Mechanism* auf `pkw_frontal`, `pkw_seite`, `motorrad`, `fussgaenger` oder
   `sturz_hoehe` setzen. Der Generator würfelt Patient, Verletzungsmuster, präklinischen Blutverlust und die
   passenden Bewertungsziele. *Random Seed* ≠ 0 erzeugt reproduzierbar denselben Fall wie der Fall-Code im Browser
   (z. B. Fall-Code `motorrad-4711` = Mechanismus `motorrad`, Seed `4711`).
   **Diagnose-Modus:** Oben in der Mitte stehen alle Verletzungen der Datenbank als Verdachtsdiagnosen.
   Antippen setzt einen Verdacht, erneutes Antippen verwirft ihn. Das Debriefing zeigt erkannte, übersehene
   und falsch vermutete Verletzungen und bei übersehenen den Hinweis, was gefehlt hat (Region nicht untersucht,
   keine passende Bildgebung). Unverletzte Regionen liefern einen Normalbefund statt „unauffällig“.
   Befunde ändern sich im Verlauf (`dynamicFindings` in `injuries.json`: Schwere, Blutverlust, Maßnahmen).
   Zwischen den Verletzungen stehen Differenzialdiagnosen ohne Physiologie; ein falscher Verdacht zeigt,
   was dagegen sprach.
7. **Modus wählen:** Feld *Mode* — *Textbook* (ATLS-Lehrbuchbild) oder *Registry* (Realitätsmodus nach
   Registerdaten mit individueller Streuung, Alter und Betablocker). Das Debriefing vergleicht beide.

## Unfall-Sandbox (Ragdoll-Physik)

Menü *NotfallSIM › Unfall-Sandbox erstellen* legt `Assets/NotfallSIM/Scenes/UnfallSandbox.unity` an.
Play → Mechanismus wählen (PKW frontal, PKW seitlich, Motorrad, Fußgänger, Sturz), Geschwindigkeit bzw.
Höhe und Gurt einstellen, „Crash starten“. Der Crash läuft in Zeitlupe mit 0,5-ms-Physikschritt.
Danach zeigt die Sandbox die Messwerte wie beim Crashtest-Dummy (HIC15, Brustbeschleunigung, Kontaktkräfte),
das Verletzungsrisiko je Region, färbt den Körper danach ein und übergibt den Fall auf Knopfdruck verdeckt an
den Schockraum (Schockraum-Szene muss erzeugt sein).

- Ragdoll aus Grundkörpern, Segmentmassen nach de Leva (1996) — `Scripts/Crash/RagdollBuilder.cs`
- Szenen und Crashpulse — `CrashRig.cs`; Sensoren — `CrashSensor.cs`
- Kriterien und Risikokurven ohne Unity, getestet — `Biomechanics.cs`, `InjuryRisk.cs`, `injury_risk.json`
- Messung wie beim Dummy: 10 kHz, SAE-J211-Filter, Brustkorbdeflexion über ein federndes Sternum,
  Viscous Criterion, Nij aus dem Halsgelenk, axiale Femurkraft; Fahrerairbag zuschaltbar
- „Kalibrieren“ stellt die Referenztests aus `calibration.json` nach und speichert Korrekturfaktoren
  (danach `calibration.json` committen)
- **Noch nie in Unity gelaufen.** Erste Prüfpunkte: Knie knicken richtig herum (sonst *Invert Knee Flexion*
  am Objekt `Unfall`), keine Teile „explodieren“ beim Start, Werte in plausibler Größenordnung
  (PKW frontal 50 km/h mit Gurt: HIC eher unter 500, Brust 30–50 g).

## Tests

`Tests/run_tests.sh` prüft ohne Unity die Datenkonsistenz (z. B. ob jedes Bewertungskriterium im Spiel
erreichbar ist), die Verläufe des Mock-Modells und die Bewertungslogik. Braucht `mono-mcs` und
`libmono-system-runtime-serialization4.0-cil`. Die Tests laufen bei jedem Push automatisch auf GitHub (Reiter *Actions*).

Neue Maßnahme: Eintrag in `interventions.json`, danach Szene über das Menü neu erzeugen — der Button entsteht automatisch.

## Browser-Prototyp

`web/` enthält eine spielbare Webversion der Mock-Physiologie zum Prüfen der Szenarien ohne Unity, z. B. am Handy.
`python3 web/build_prototype.py` bettet `web/engine.js` und alle JSON-Dateien in `web/dist/notfallsim.html` ein.
`Tests/parity/run_parity.sh` prüft, dass `engine.js` exakt dieselben Verläufe rechnet wie die C#-Mock-Engine
(Lehrbuchmodus; Zufallsfolgen im Realitätsmodus unterscheiden sich). Quelle der Wahrheit bleibt der C#-Code:
Änderungen an der Physiologie immer in beiden Implementierungen nachziehen, der Abgleich in CI fällt sonst rot aus.

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
