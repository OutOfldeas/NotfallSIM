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
    UI/            VitalsMonitor, MonitorWaveforms, FindingsPanel, DebriefPanel, MainMenu, PauseMenu, OrbitCamera
    Interaction/   BodyRegion
    Anatomy/       body.bin lesen, Patient aufbauen (Haut, Skelett, Muskeln, Röntgenblick)
    Crash/         Unfall-Sandbox: Ragdoll, Fahrzeuge, Sensoren, Verletzungskriterien
    Rooms/         Schockraum zur Laufzeit: Raum, Licht, Kamera, Patient, Monitor (auch für alte Szenendateien)
  NotfallSIM/Editor/
    MenuSceneBuilder.cs          Menü „NotfallSIM › Alle Szenen erstellen“
    SceneBuilder.cs              Schockraum-Szene (Raum aus Scripts/Rooms/TraumaRoom.cs, UI, Post-Processing)
    CrashSceneBuilder.cs         Unfall-Sandbox-Szene
  NotfallSIM/Resources/
    XRay.shader                  Röntgenblick (URP)
  StreamingAssets/NotfallSIM/
    anatomy/body.bin             Anatomiemodell (BodyParts3D, CC BY-SA 2.1 JP — siehe unten)
    anatomy/anatomy.json         Verletzung → Strukturen für den Röntgenblick
    tissues.json                 Mechanische Gewebeeigenschaften mit Quellen (Steifigkeit, Reißdehnung/-spannung)
    injuries.json                Verletzungsdatenbank
    interventions.json           Maßnahmen (Buttons, Volumina, Effektstärken, Quellen)
    imaging.json                 Bildgebung (eFAST, Röntgen, CCT, Ganzkörper-CT)
    mechanisms.json              Unfallmechanismen für Zufallsfälle (Verletzungswahrscheinlichkeiten)
    findings.json                Normalbefunde je Körperregion (Diagnose-Modus)
    differentials.json           Differenzialdiagnosen ohne Physiologie, mit „spricht dagegen“
    injury_risk.json             Unfall-Sandbox: Messwerte → Risikokurven → Verletzungen
    organ_model.json             Organmodell: Bauchkompression → Organdehnung gegen Gewebefestigkeit
    muscle_tone.json             Muskeltonus im Ragdoll: Maximalmomente je Gelenk, Reflex, Abstützen
    scenarios/*.json             Szenarien
tools/anatomy/                   Aufbereitung der Anatomie (Python), erzeugt body.bin
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
4. **Szenen erzeugen:** Menü *NotfallSIM › Alle Szenen erstellen*. Legt Hauptmenü, Schockraum und Unfall-Sandbox
   unter `Assets/NotfallSIM/Scenes/` an und trägt sie in die Build-Liste ein. Nach jedem `git pull` mit Änderungen
   an Editor-Skripten erneut ausführen.
5. **Play** in der Szene `Hauptmenue`. Im Schockraum: Körperregionen anklicken zum Untersuchen, Maßnahmen per Button,
   Zeitraffer 1x/5x/20x. Kamera: rechte Maustaste umsehen, Mausrad oder +/− zoomen, mittlere Maustaste verschieben,
   WASD/Pfeiltasten durch den Raum gehen, Q/E (Bild↓/Bild↑) tiefer/höher, Umschalt schneller, F zurück zum Patienten.
   Die Kamera bleibt innerhalb der Wände.
   *Menü (Esc)* pausiert und führt zurück ins Hauptmenü oder startet denselben Fall neu.
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

## Schockraum: Patient und Röntgenblick

Der Patient ist ein anatomisches Modell (Haut, ca. 210 Knochen, Organe mit Zwerchfell, Großhirnrinde, große Gefäße und schematische Nerven),
zur Laufzeit aus `anatomy/body.bin` aufgebaut und auf die Größe aus dem Szenario skaliert. Er atmet mit der
Atemfrequenz aus der Physiologie (beim Spannungspneu links hebt sich die linke Thoraxseite kaum), wird mit dem
Blutverlust blass und bei niedriger Sättigung zyanotisch. Der Monitor zeigt EKG, Pulskurve und Kapnografie;
der QRS-Ton wird mit sinkender Sättigung tiefer.

Untersuchen per Klick trifft genau die sichtbare Haut: Jedes Hautdreieck gehört zur Region seines Körpersegments
(vorher Kapseln und Quader, die sich überlappten — rund ein Viertel der Klicks landete in der falschen Region).

*Röntgenblick* (Feld oben rechts neben dem Monitor) macht die Haut durchsichtig; die Ebenen Knochen, Organe,
Gefäße und Nerven lassen sich einzeln schalten (abgedunkelter Knopf = ausgeblendet), mit der Maus über einer Struktur
steht ihr deutscher Name (Ebenen: Knochen, Organe, Gefäße, Nerven, Muskeln), darunter die belegten Gewebeeigenschaften (`tissues.json`: Steifigkeit,
Reißdehnung und -spannung mit Dehnrate; Quellen in `docs/LITERATUR.md`). Nach Szenarioende schaltet sich der Röntgenblick ein und markiert die tatsächlichen
Verletzungen rot (`anatomy/anatomy.json`; SDH/EDH: Rinde und Kalotte der betroffenen Seite).

Anatomiedaten neu erzeugen (nur nötig bei Änderungen an der Auswahl): BodyParts3D-STL-Dateien herunterladen,
dann `python3 tools/anatomy/build_anatomy.py --bp3d <Ordner>` (braucht `trimesh`, `rtree`, `embreex` (empfohlen), `scipy`, `scikit-image`,
`fast-simplification`). Fehlt `body.bin`, erscheint ein Platzhalter aus Grundkörpern.

Prüfen ohne Unity:
- `python3 tools/anatomy/check_anatomy.py <body.bin>` — 74 Lagebeziehungen (Seiten, Wirbelhöhen, vorn/hinten,
  Trigonum femorale, Rückenmark im Wirbelkanal, alles innerhalb der Haut, Bahnen nicht durch Knochen)
- `python3 tools/anatomy/check_patient.py <body.bin>` — rechnet die Rückenlage auf der Trage wie Unity nach,
  inklusive Überschneidungen, die erst durch die Lagerung der Arme entstehen
- Überschneidungen werden beim Aufbereiten aufgelöst (`tools/anatomy/collide.py`: nachgiebigere Strukturen weichen
  festeren aus); das Aufbereiten dauert dadurch rund 20–40 Minuten (mit `embreex` deutlich schneller)
- `python3 tools/anatomy/pose_check.py <body.bin> <Ordner>` — Ragdoll-Haltungen der Sandbox als GLB
- `Tests/run_tests.sh` prüft zusätzlich Umlaufrichtung der Dreiecke, Hautgewichte, Atmungsrichtung und Bezeichnungen

**Lizenz der Anatomiedaten:** BodyParts3D, © The Database Center for Life Science, lizenziert unter
[CC BY-SA 2.1 Japan](https://creativecommons.org/licenses/by-sa/2.1/jp/deed.de). `body.bin` ist eine Bearbeitung
(vereinfacht, Haut neu vernetzt, Nerven und Extremitätengefäße schematisch ergänzt) und steht unter derselben Lizenz.

## Unfall-Sandbox (Ragdoll-Physik)

Menü *NotfallSIM › Unfall-Sandbox erstellen* legt `Assets/NotfallSIM/Scenes/UnfallSandbox.unity` an.
Play → Mechanismus wählen (PKW frontal, PKW seitlich, Motorrad, Fußgänger, Sturz), Geschwindigkeit bzw.
Höhe und Gurt einstellen, „Crash starten“. Der Crash läuft in Zeitlupe mit 0,5-ms-Physikschritt.
Danach zeigt die Sandbox die Messwerte wie beim Crashtest-Dummy (HIC15, Brustbeschleunigung, Kontaktkräfte),
das Verletzungsrisiko je Region, färbt den Körper danach ein und übergibt den Fall auf Knopfdruck verdeckt an
den Schockraum (Schockraum-Szene muss erzeugt sein).

- Ragdoll mit Gelenken aus dem Anatomiemodell und der Haut als Skinned Mesh, Kollider als Kapseln (entlang der
  Segmentachse) und Quader, Segmentmassen nach de Leva (1996) — `Scripts/Crash/RagdollBuilder.cs` (ohne `body.bin`:
  Grundkörper). Körperteile stoßen sich gegenseitig ab (Arme gleiten nicht durch den Rumpf); ignoriert werden nur
  Gelenknachbarn und Teile, die sich in der Ausgangshaltung schon berühren
- Szenen und Crashpulse — `CrashRig.cs`; Sensoren — `CrashSensor.cs`
- Insasse entspannt (Reflex nach 150 ms), angespannt (abgestützt) oder bewusstlos — Muskeltonus je Gelenk aus
  Maximalmomenten der Literatur (`MuscleTone.cs`, `muscle_tone.json`)
- Kamera wie im Schockraum (rechte Maus drehen, Mausrad zoomen, WASD/Q/E verschieben, F zurück); der Blickpunkt
  folgt dem Becken des Ragdolls
- Kriterien und Risikokurven ohne Unity, getestet — `Biomechanics.cs`, `InjuryRisk.cs`, `injury_risk.json`
- Organmodell für Milz, Leber, Nieren — `OrganMechanics.cs`, `organ_model.json`: Bauchkraft → Kompression
  (27 kN/m, Hardy 2001) → Organstauchung gegen die Versagensdehnung des Gewebes bei der Dehnrate des Anpralls
  (`tissues.json`, Kemper 2011/2013); geprüft gegen Leichenversuche (Viano 1989). Ergebnis unter „Organbelastung“
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
