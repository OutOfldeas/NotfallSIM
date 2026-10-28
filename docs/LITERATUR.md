# Literaturnotizen

Stand: 27.10.2026 · Datenbank-Version 0.6.0 · Maßnahmen v0.6.0 · Bildgebung v0.1.0

Hier steht, worauf die Zahlen im Modell beruhen und wie belastbar sie sind.
Jede Änderung an `injuries.json` oder an den Modelltabellen sollte hier nachvollziehbar sein.

## Parameter im Überblick

| Parameter | Wert im Modell | Belastbarkeit | Grundlage |
|---|---|---|---|
| Blutvolumen | 70 mL/kg (♂), 65 mL/kg (♀) | gut | Lehrbuchwert |
| Vitalwerte je Schockklasse (Lehrbuchmodus) | ATLS-Tabelle, interpoliert | Lehrbuch; real oft anders | ATLS 10th ed.; Kritik s. u. |
| Vitalwerte je Schockklasse (Realitätsmodus) | HF 84–97, RRsys 135–92 | Mittelwerte belegt, Zuordnung genähert | Mutschler 2013 (TR-DGU) |
| Individuelle Streuung (Realitätsmodus) | HF ±12, Reaktionsstärke ×0,5–1,5, RRsys ±10 | geschätzt | didaktisch |
| Betablocker | HF-Anstieg ×0,25 | qualitativ gut, Faktor geschätzt | didaktisch |
| Alter ≥ 65 (Realitätsmodus) | RRsys +15 mmHg (vorbestehende Hypertonie) | geschätzt | didaktisch |
| Base Excess je Klasse | 0 / −2 / −6 / −10 / < −10 mmol/L | gut | ATLS 10th ed.; Mutschler 2013 |
| Laktat | 1,0 → 9,0 mmol/L | geschätzt | didaktisch, an BE gekoppelt |
| Hb | aus Erythrozytenmasse / Blutvolumen; Start 14,5 (♂) bzw. 13,0 g/dL (♀) | Prinzip gut, Zahlen Lehrbuch | Verdünnungsphysiologie |
| EK | Hämatokrit 0,6 | Größenordnung plausibel | gegen Querschnitts-Leitlinie Hämotherapie prüfen |
| HCO₃⁻, pCO₂, pH | HCO₃⁻ ≈ 24 + BE; Winters-Formel; Henderson-Hasselbalch | Lehrbuchphysiologie | Standardformeln |
| Kalium | +0,6 mmol/L je 0,1 pH unter 7,40 | Faustregel | Lehrbuch |
| Ionisiertes Calcium | −0,06 mmol/L je 300 mL Blutprodukt (Citrat) | geschätzt | didaktisch |
| Glukose | 110 → 200 mg/dL mit der Schockschwere | geschätzt | didaktisch |
| Noradrenalin | RRsys +20, RRdia +12 mmHg; mehr Druck = mehr Blutfluss aus Läsionen | Richtung gut, Größe geschätzt | Rossaint 2023 (Indikation), Wirkung didaktisch |
| Akutes Subduralhämatom | GCS ca. 12 bei Ankunft, GCS ≤ 8 nach ca. 12 min | geschätzt | didaktisch |
| Präklinischer Blutverlust (Polytrauma) | 1,5 L bei ca. 60 min Rettungszeit | plausibel | TR-DGU: Ø 66 min bis Schockraum |
| Femurschaft, Blutverlust | max. 1000 mL, 20 mL/min | mäßig | Lieurance 1992 (Sekundärquelle) |
| Becken instabil, Blutverlust | max. 3000 mL, 150 mL/min | schwach (geschätzt) | Plausibilisiert über Transfusionsbedarf (Devaney 2023) |
| Beckengurt-Effekt | Blutungsrate × 0,5 | schwach | Widersprüchliche Beobachtungsstudien |
| Kristalloid, intravasal | 30 % des Volumens | grob | Vereinfachung, in Pulse realistischer |
| Spannungspneu, Verlauf (spontan atmend) | früh Hypoxie, Hypotonie ab 60 % Spannung | qualitativ gut, zeitlich geschätzt | Roberts 2015 |
| Nadeldekompression, Erfolg | 57 % | mäßig (sehr heterogene Daten) | Ahmad 2025 (Versagerrate ca. 43 %) |
| SHT, Verlauf | luzides Intervall, GCS 8 nach ca. 15 min, Einklemmung nach ca. 20 min | geschätzt | Lehrbuchbild Epiduralhämatom |
| SHT, sekundäre Hirnschädigung | Progression ×2 bei RRsys < 90 oder SpO2 < 90 | qualitativ gut, Faktor geschätzt | Chesnut 1993 |
| Beatmung bei Spannungspneu | Progression ×3 | qualitativ gut, Faktor geschätzt | Roberts 2015 |
| Kraniotomie, Zeit bis Wirkung | 10 min | geschätzt | didaktisch; reale Intervalle prüfen |

## Wichtigste Erkenntnisse

**ATLS-Schockklassen bilden die Realität nur begrenzt ab.**
Auswertungen des TraumaRegister DGU zeigen, dass echte Traumapatienten bei zunehmendem Blutverlust kaum tachykard werden.
In der Base-Deficit-Klassifikation von Mutschler et al. (16.305 Patienten) stieg die mittlere Herzfrequenz bei Klinikaufnahme nur von etwa 86 auf 97/min, der systolische Druck fiel von etwa 133 auf 95 mmHg.
Relevante Hypotonie trat nur in der schwersten Klasse auf. Die Massivtransfusionsrate stieg von 5 % auf 52 %, die Letalität von 7 % auf 52 %.
Eine weitere Auswertung zur analogen präklinischen PHTLS-Einteilung (46.689 Patienten) ordnete nur rund ein Viertel der Patienten anhand von HF, RR und GCS gemeinsam korrekt einer Klasse zu.

Konsequenz für NotfallSIM: Der Mock zeigt bewusst das Lehrbuchbild, damit die Schockklassen erlernbar sind.
Für mehr Realismus wäre ein zweiter Modus sinnvoll, der sich an den Registerdaten orientiert, plus der Base Excess als zusätzlicher Laborwert.

**Realitätsmodus: so ist er gebaut.**
Grundlage sind die mittleren Aufnahmewerte je Base-Deficit-Klasse aus dem TraumaRegister DGU (Mutschler et al. 2013): HF 86/90/96/97/min, RRsys 133/125/113/95 mmHg.
Die BD-Klassen werden über die ATLS-10-Zuordnung des Base Excess (0 bis −2 / −2 bis −6 / −6 bis −10 / unter −10 mmol/L) näherungsweise auf die Blutverlustklassen abgebildet. Das ist eine Annäherung: Registerpatienten haben oft Begleitverletzungen, Schmerzen und präklinische Therapie.
Dazu kommen individuelle Streuung sowie Alter und Betablocker. Bei 35 % Blutverlust zeigt der Lehrbuchmodus HF 128 und RR 100/72; im Realitätsmodus liegt die HF im Mittel bei 95 (Spanne 78–111) und der RRsys bei 112. Eine 76-jährige Patientin unter Betablocker hat bei gleichem Verlust HF 82 und RR 134/81 — ein „unauffälliger“ Monitor bei manifestem Schock.
Der Base Excess verhält sich in beiden Modi gleich. Das ist die Kernbotschaft: Er deckt den Schock auf, wenn die Vitalwerte es nicht tun.

**BGA: Das Hb ist im akuten Blutverlust kein Schockmarker.**
Wer Vollblut verliert, verliert Erythrozyten und Plasma im gleichen Verhältnis; das Hb bleibt zunächst gleich. Es fällt erst durch Verdünnung, also durch Infusionen und die transkapilläre Rückfüllung über Stunden. Das Modell bildet das ab: Bei 27 % Blutverlust ohne Infusion zeigt die BGA Hb 14,5 g/dL, aber BE −5,3 und Laktat 3,0 mmol/L. Kristalloide senken das Hb, Erythrozytenkonzentrate heben es und senken das ionisierte Calcium (Citrat).
Die Säure-Basen-Werte folgen Standardphysiologie: metabolische Azidose aus dem Basendefizit, respiratorische Kompensation nach der Winters-Formel beim spontan atmenden Patienten, Hyperkapnie bei ungeschütztem Atemweg (SHT mit GCS ≤ 8), Normoventilation nach Intubation.
Nicht modelliert: transkapilläre Rückfüllung, Hyperkaliämie durch Massivtransfusion, Hypothermie und Gerinnungswerte.

**Beckengurt: plausibel, aber schwach belegt.**
Eine BestBETs-Übersicht fand nur kleine Beobachtungsstudien.
Croce et al. (2007, n = 186) berichteten deutlich weniger Transfusionen mit Beckengurt (24 h: 4,9 vs. 17,1 EK) gegenüber externem Fixateur, aber keinen signifikanten Unterschied in der Letalität.
Ghaemmaghami et al. (2007, n = 237) fanden weder bei Transfusionen noch bei Letalität einen Unterschied.
Der Faktor 0,5 im Modell ist deshalb eine didaktische Setzung, keine Messgröße.

**Femurschaftfraktur: weniger Blutverlust als oft gelehrt.**
Lieurance et al. (1992, n = 53, isolierte Frakturen) schätzten den präoperativen Verlust auf durchschnittlich etwa 740 mL, gesamt inklusive OP auf etwa 1,2 L.
Ein Patient mit isolierter Femurfraktur und Hypotonie hat daher sehr wahrscheinlich eine zweite Blutungsquelle. Das ist auch ein gutes Lernziel.

**Instabile Beckenringverletzung: nur ein Teil blutet relevant.**
Devaney et al. (2023) beschreiben 144 operativ versorgte instabile Beckenringverletzungen. Etwa ein Drittel war im traumatischen Schock und erhielt dann median 6 EK in 24 Stunden.
Direkte Daten zum Blutverlust über die Zeit fehlen, die Kinetik im Modell bleibt eine Schätzung.

**Spannungspneumothorax: Beatmung verändert das Bild grundlegend.**
Eine systematische Übersicht (Roberts et al. 2015) zeigt zwei unterschiedliche Verläufe.
Spontan atmende Patienten waren nur in etwa der Hälfte der Fälle hypoxisch, und Hypotonie oder Stillstand traten deutlich seltener auf.
Unter Beatmung waren über 90 % hypoxisch, Hypotonie und Stillstand waren um ein Vielfaches wahrscheinlicher und traten oft innerhalb von Minuten ein.
Der Mock bildet vorerst den spontan atmenden Patienten ab: zuerst Hypoxie, Tachypnoe und Tachykardie, Blutdruckabfall spät.
Der beatmete Verlauf ist ein eigenes Szenario für Phase 2, z. B. „Verschlechterung nach Intubation“.

**Nadeldekompression versagt häufig.**
Eine Meta-Analyse (Ahmad et al. 2025) fand eine kombinierte Versagerrate von etwa 43 %. Die Nadellänge war wichtiger als der Punktionsort.
ATLS empfiehlt den 4./5. ICR zwischen vorderer und mittlerer Axillarlinie, das ETC weiterhin den 2. ICR medioklavikulär.
Im Spiel versagt die Punktion daher zufällig in etwa 4 von 10 Fällen. Lernziel: Erfolg prüfen und die Thoraxdrainage anlegen.

**Schädel-Hirn-Trauma: Sekundärschäden vermeiden ist der Kern.**
In der klassischen Auswertung von Chesnut et al. (1993, 717 Patienten mit schwerem SHT) hatten 34,6 % eine Hypotonie (RRsys < 90 mmHg); sie war mit einer um 150 % höheren Letalität verbunden. Hypoxie war unabhängig davon mit schlechterem Outcome assoziiert.
Die S3-Leitlinie (Stand 2011) fordert beim SHT Normoxie, Normokapnie und Normotonie (GoR A) und einen RRsys nicht unter 90 mmHg (GoR B); die permissive Hypotension gilt bei ZNS-Verletzung als kontraindiziert. Die Brain Trauma Foundation empfiehlt sogar höhere Werte (RRsys ≥ 110 mmHg bei 15–49 Jahren).
Daraus entsteht ein didaktisch wertvoller Zielkonflikt für spätere Kombinationsszenarien: Beckenblutung verlangt permissive Hypotension, das SHT verbietet sie.
Ein luzides Intervall ist kein verlässliches Zeichen: In einer Serie von 146 operierten Epiduralhämatomen (Kinder) trat es nur bei etwa 37 % auf.

**Polytrauma: der Zielkonflikt beim Blutdruck.**
Die europäische Blutungsleitlinie (Rossaint et al. 2023) empfiehlt ohne Hirnverletzung eine restriktive Volumentherapie mit RRsys 80–90 mmHg bis zur Blutstillung (Grade 1B), beim schweren SHT (GCS ≤ 8) dagegen einen MAD ≥ 80 mmHg (Grade 1C). Reicht die Volumentherapie nicht, soll Noradrenalin ergänzt werden (Grade 1C); Tranexamsäure so früh wie möglich, spätestens 3 h nach Trauma (Grade 1A).
Das Polytrauma-Szenario (akutes Subduralhämatom, instabiles Becken, Femur- und Rippenfrakturen, 1,5 L präklinischer Blutverlust) setzt genau das um: Ein eigenes Zustandsereignis meldet MAD < 80 bei GCS ≤ 8, und die Bewertung verlangt null solche Episoden. Noradrenalin hebt im Modell den Druck, erhöht aber über den höheren MAD auch den Blutverlust — es ersetzt keine Blutstillung.
Durchgespielte Strategien: unbehandelt Stillstand nach ca. 19 min; nur Blutungsversorgung ohne Atemweg und Neurochirurgie Stillstand nach ca. 24 min; vollständige Versorgung mit CT im Schockraum überlebt, mit CT in der Radiologie nur knapp und mit Hypoxie im CT.

## Bewertungskriterien im Debriefing

Die Kriterien stehen im Feld `assessment` der Szenario-Dateien. Belegt ist meist nur *dass* und *in welcher Reihenfolge* gehandelt werden soll; konkrete Minutenwerte geben weder Leitlinie noch Studien vor. Die Minuten bleiben daher didaktische Setzungen, sind aber jetzt an belegten Ankern ausgerichtet.

| Szenario | Ziel | Zeit | Kritisch | Anker |
|---|---|---|---|---|
| Becken | Sauerstoff | 2 min | nein | ABCDE-Logik |
| Becken | Becken untersucht | 3 min | nein | ABCDE-Logik |
| Becken | Oberschenkel untersucht | 5 min | nein | ABCDE-Logik |
| Becken | Beckengurt | 5 min | ja | S3 (GoR A); vor CT, die im Mittel ca. 22 min nach Aufnahme erfolgt (TR-DGU 2016) |
| Becken | Blutprodukte | 10 min | ja | Letalität steigt pro Minute Verzögerung (Duchesne 2024: OR 1,11/min) |
| Becken | Tranexamsäure | 10 min | nein | S3 (GoR A): früh, spätestens 3 h nach Trauma |
| Becken | Kristalloide max. 1000 mL | – | nein | S3 (GoR B): restriktiv, RRsys ca. 90 mmHg; ATLS: 1 L Initialbolus |
| Thorax | Sauerstoff | 2 min | nein | ABCDE-Logik |
| Thorax | Thorax untersucht | 2,5 min ab Beginn | nein | S3 (GoR B): klinische Diagnose |
| Thorax | Entlastung | 6 min ab Beginn | ja | Sofort nach klinischer Diagnose; im Modell SpO2 < 90 % nach ca. 6 min |
| Thorax | Thoraxdrainage | 15 min ab Beginn | ja | Nadel versagt in ca. 43 % (Ahmad 2025) |
| SHT | Sauerstoff | 2 min | nein | Normoxie (S3, GoR A) |
| SHT | GCS und Pupillen geprüft | 3 min | nein | ABCDE-Logik |
| SHT | Neurostatus nach Eintrübung erneut geprüft | 2 min ab GCS ≤ 13 | nein | ABCDE-Logik |
| SHT | Neurochirurgie eingebunden | 4 min ab GCS ≤ 13 | ja | Eintrübung nach luzidem Intervall = Notfall |
| SHT | Atemweg gesichert | 3 min ab GCS ≤ 8 | ja | S3: GCS ≤ 8 intubieren (innerklinisch GoR A) |
| SHT | Osmotherapie | 3 min ab Anisokorie | nein | S3, GoR 0 |
| SHT | Keine Hypoxie | 0 Episoden | ja | Chesnut 1993; S3, GoR A |
| SHT | Keine Hypotonie | 0 Episoden | ja | Chesnut 1993; S3, GoR B |
| Polytrauma | Beckengurt / Blut / TXA | 5 / 10 / 10 min | ja / ja / nein | wie Beckenszenario; Rossaint 2023 |
| Polytrauma | Atemweg bei GCS ≤ 8 | 3 min ab GCS ≤ 8 | ja | S3, GoR A |
| Polytrauma | Keine Hypoxie | 0 Episoden | ja | Chesnut 1993 |
| Polytrauma | MAD ≥ 80 beim schweren SHT | 0 Episoden | ja | Rossaint 2023, Grade 1C |
| Polytrauma | Bildgebung Kopf | 15 min | nein | didaktisch |
| Polytrauma | Neurochirurgie nach CT | 5 min ab CT-Befund | ja | didaktisch |
| Polytrauma | Kein CT-Transport im Schock | 0 | nein | ATLS; S3, GoR B |

Änderungen in v0.4: Beim Thoraxtrauma zählen die Ziele jetzt ab Beginn des Spannungspneus statt ab Szenariostart. Kristalloid ist jetzt eine balancierte Vollelektrolytlösung (S3, GoR A: keine isotone NaCl-Lösung). Tranexamsäure ist als Maßnahme und Ziel dazugekommen.

**Bessere Datenquellen für echte Zeitziele:** die aktuellen Jahresberichte des TraumaRegister DGU (Schockraumintervalle), die Langfassung der S3-Leitlinie auf MAGICapp und ein Expertenkonsens erfahrener Schockraumleiter (z. B. Delphi-Befragung).

## Unfallmechanismen und Zufallsfälle

`mechanisms.json` beschreibt fünf Mechanismen (PKW frontal, PKW seitlich Fahrerseite, Motorrad, Fußgänger, Sturz > 3 m). Je Mechanismus stehen dort Alters- und Geschlechtsverteilung, Geschwindigkeit bzw. Sturzhöhe, Rettungszeit und die möglichen Verletzungen mit Wahrscheinlichkeit, Schweregrad und Beginn. Der Generator (`ScenarioGenerator.cs`, gleich in `web/engine.js`) zieht daraus einen Fall:

- Jede Verletzung wird unabhängig mit ihrer Wahrscheinlichkeit gezogen. Ausschlussgruppen verhindern Unsinn (höchstens ein raumforderndes Hämatom, höchstens ein parenchymatöses Abdominalorgan).
- Ohne Gurt steigen beim PKW Kopf- und Femurverletzungen (Multiplikator je Verletzung).
- Fälle ohne kreislauf- oder hirnrelevante Verletzung werden neu gezogen.
- Präklinischer Blutverlust ≈ Σ (Blutungsrate × Schweregrad) × Rettungszeit × 0,2, höchstens 2000 mL. Der Faktor 0,2 steht für Hypotonie, Tamponade und präklinische Kompression; didaktische Setzung.
- Bewertungsziele kommen aus Vorlagen je Verletzung (`criteria` in `injuries.json`) plus Basiszielen (O₂, BGA, kein CT-Transport im Schock). Doppelte Ziele zählen einmal.
- Gleicher Seed ergibt in Unity und im Browser denselben Fall (Mulberry32-Zufallsgenerator; in der Parität geprüft).

**Evidenzstand: Entwurf.** Die Wahrscheinlichkeiten sind didaktische Setzungen nach typischen Verletzungsmustern (Lehrbuchwissen zur Kinematik), nicht aus Registerdaten berechnet. Qualitativ belegt:

| Aussage | Quelle |
|---|---|
| Seitenkollisionen verursachen einen großen Teil der Abdominalverletzungen; Leber und Milz am häufigsten betroffen | Yoganandan 2000 (22) |
| Hochrisiko-Mechanismen (Sturz > 3 m, Fußgänger/Zweirad mit hoher Geschwindigkeit, Herausschleudern, Intrusion) als Triagekriterium | Newgard 2022 (23) |
| Mit der Sturzhöhe steigt das Risiko schwerer Thorax- und Beckenverletzungen | J Clin Med 2021 (24), nur Titel/Abstract |

Mit Musterversorgung überleben im Test 39–40 von 40 Fällen je Mechanismus; die Ausnahmen sind Kombinationen aus schwerem SDH, Becken- und Abdominalblutung mit hohem präklinischem Verlust. Ohne Versorgung überleben 5–15 von 40.

Neue Verletzungen in v0.7: Milzruptur, Leberlazeration (je ca. 90–100 mL/min, Tamponade bei 2,5 L) und offene Unterschenkelfraktur mit arterieller Blutung (120 mL/min ohne Tamponade, Tourniquet stoppt sie). Neue Maßnahme: Notfall-Laparotomie (stillt Milz- und Leberblutung nach 15 min). Alle Raten sind Spielwerte.

## Diagnose-Modus

Jede Untersuchung liefert einen ausformulierten Befund: in verletzten Regionen die klinischen Befunde der Verletzung (ab ihrem Beginn), sonst einen Normalbefund aus `findings.json` (Secondary Survey nach ATLS). Der Spieler setzt Verdachtsdiagnosen aus allen Verletzungen der Datenbank. Bewertet wird der Stand am Ende:

- **erkannt:** Verletzung lag vor und war am Ende vermutet (mit Zeitpunkt des Verdachts)
- **übersehen:** lag vor, nicht vermutet. Hinweise: Region nicht untersucht; keine durchgeführte Bildgebung, die sie zeigt
- **falscher Verdacht:** vermutet, lag aber nicht vor

**Dynamische Befunde** (`dynamicFindings`): Ein Befund erscheint nur unter Bedingungen — Schwere der Verletzung (Mock-Level 0..1), Blutverlust aus der Verletzung, Gesamtblutverlust, vor oder nach einer Maßnahme (optional mit Verzögerung). Beispiele: Gestaute Halsvenen beim Spannungspneu erst ab Level 0,5 und nur bei < 15 % Blutverlust, sonst flach; Trachealverlagerung ab 0,8; nach der Drainage nur noch der Drainagebefund; Distension des Abdomens ab 800 mL Verlust aus Milz oder Leber; nach dem Beckengurt keine erneute Stabilitätsprüfung. Alle Schwellen sind didaktische Setzungen.

**Differenzialdiagnosen** (`differentials.json`): Hämatothorax, Perikardtamponade, Aortenverletzung, Lungenkontusion, Nierenverletzung, Darm-/Mesenterialverletzung, instabile Wirbelsäulenverletzung, diffuse axonale Schädigung. Sie stehen ununterscheidbar zwischen den Verletzungen. Bei falschem Verdacht zeigt das Debriefing, was dagegen sprach — bevorzugt das Argument zur tatsächlich vorliegenden Verletzung (z. B. Hämatothorax vs. Spannungspneu: hypersonorer statt gedämpfter Klopfschall). Bei falschem Verdacht auf eine modellierte Verletzung nennt es deren fehlende Leitbefunde.

Die Diagnosebewertung steht getrennt von den Behandlungszielen. Im Browser-Prototyp zeigt das Protokoll nicht mehr an, wann eine Verletzung aktiv wird (das hätte die Diagnose verraten).

## Unfall-Sandbox: Verletzungskriterien

Die Sandbox misst am Ragdoll wie an einem Crashtest-Dummy und übersetzt die Werte über Risikokurven in Verletzungen der Datenbank (`injury_risk.json`). Mehrere Messwerte für dieselbe Verletzung werden als 1 − Π(1 − p) kombiniert; der Schweregrad wächst mit dem Risiko.

**Messtechnik wie beim Dummy:** Physikschritt 0,1 ms (10 kHz). Alle Kanäle werden mit dem Kanalfilter nach SAE J211 / ISO 6487 gefiltert (Butterworth, vor- und rückwärts): Kopf CFC 1000, Brustwirbelsäule CFC 180, Brustkorbdeflexion CFC 600, Halskraft CFC 1000, Halsmoment CFC 600, Femur und Kontaktkräfte CFC 600.

**Brustkorb:** Vor dem Thorax sitzt eine Sternum-Platte (0,45 kg), federnd und gedämpft gelagert (26 kN/m, 520 N·s/m, Anschlag bei 90 mm) — angelehnt an das Thoraxmodell von Lobdell (1973), Werte noch zu prüfen. Der Schultergurt greift am Sternum an. Daraus: Deflexion und Viscous Criterion (Brustkorbtiefe 0,229 m wie Hybrid III).

**Hals und Femur:** Kraft und Moment im Kopf-Rumpf-Gelenk ergeben Nij (Intercepts FMVSS 208, 50. Perzentil Mann: Zug 6806 N, Druck 6160 N, Flexion 310 Nm, Extension 135 Nm). Die axiale Femurkraft kommt aus dem rechten Hüftgelenk.

**Airbag (PKW frontal):** entfaltet sich 15 ms nach Crashbeginn in 25 ms um 25 cm aus dem Lenkrad und wirkt dann als Feder-Dämpfer-Polster (25 kN/m, 1500 N·s/m). Didaktisch.

| Messwert | Grenzwert | Kurve | Status |
|---|---|---|---|
| HIC15 (CFC 1000) | 700 (FMVSS 208) | AIS3+ = Φ((ln HIC − 7,45231) / 0,73998), veröffentlicht | Zuordnung HIC15/HIC36 im Original prüfen |
| Nij | 1,0 (FMVSS 208) | log-logistisch, ≈ 22 % AIS3+ bei 1,0 | didaktisch; noch keiner Verletzung zugeordnet |
| Brustkorbdeflexion | 63 mm (FMVSS 208) | log-logistisch, x50 55 mm | didaktisch; Laituri 2005 übernehmen |
| Viscous Criterion | 1,0 m/s (ECE R94) | log-logistisch durch VC 1,0 m/s ≈ 25 % AIS4+ (Lau & Viano 1986) | Ankerpunkt veröffentlicht, Form didaktisch |
| Brustbeschleunigung, 3-ms-Clip (CFC 180) | 60 g (FMVSS 208) | log-logistisch, x50 75 g | didaktisch |
| Kontaktkraft Thorax / Abdomen / Becken, 3 ms | – / 2,5 kN / 6 kN (Euro NCAP, Seite) | log-logistisch | didaktisch; Abdomen nur noch Anzeige |
| Stauchung Leber / Milz, Querdehnung Nieren | Versagensdehnung des Gewebes | Normalverteilung der Versagensdehnung (Organmodell, siehe unten) | aus Gewebedaten hergeleitet |
| Axiale Femurkraft | 10 kN (FMVSS 208) | log-logistisch, x50 11 kN | didaktisch |
| Kontaktkraft Ober-/Unterschenkel, 3 ms | – | log-logistisch | didaktisch |

### Muskeltonus und Abstützen (`muscle_tone.json`)

Im Ragdoll hält jedes Gelenk die Ausgangshaltung mit einem aktiven Antrieb, dessen Moment auf **Maximalmoment × Aktivierung** begrenzt ist (bei voller Aktivierung nach 20° Auslenkung erreicht). Rechenteil ohne Unity: `MuscleTone.cs`, getestet.

| Gelenk | Maximalmoment Männer / Frauen | Quelle |
|---|---|---|
| Halswirbelsäule (Extension) | 52 / 21 Nm | Vasavada 2001 |
| Rumpf (Extension, je Abschnitt) | 145 / 63 Nm | Unver 2024 (isokinetisch 60°/s) |
| Schulter (Abduktion) | 60 / 43 Nm | Harbo 2012 |
| Ellbogen (Flexion) | 50 / 30 Nm | Harbo 2012 |
| Hüfte (Flexion) | 167 / 101 Nm | Harbo 2012 |
| Knie (Extension) | 265 / 185 Nm | Harbo 2012 (Kontrolle: Anderson 2007 ergibt 230 Nm für 80 kg, 1,80 m) |

Je Gelenk ein Wert, weil der Antrieb keine Richtung unterscheidet: genommen wird die Richtung, die beim Frontalanprall das Einknicken bremst.

**Zustände des Insassen** (Knopf in der Sandbox):
- **Entspannt (unvorbereitet):** Grundtonus 2 % MVC (Ruheaktivität der Halsmuskeln liegt unter 1 %; Kempter 2023), Reflex nach **150 ms** (Muskelantwort bei Probanden 125–200 ms; Kempter 2023, Kumar 2005), Anstieg in 50 ms (geschätzt) auf **75 % MVC** (höchste Aktivierung in Schlittenversuchen; Kumar 2005).
- **Angespannt (abgestützt):** 75 % MVC von Beginn an. In Probandenversuchen hob Abstützen die Thoraxkompression bei leichtem Frontalanprall praktisch auf (Kemper 2010).
- **Bewusstlos:** nur passiver Gelenktonus. Gilt auch für die Kalibrierung gegen Crashtests (Dummys haben keine Muskeln).

**Was daraus folgt:** Ein Frontalcrashpuls dauert etwa 100–150 ms — der Reflex des unvorbereiteten Insassen kommt also erst gegen Ende der Hauptbelastung. Den Unterschied macht vor allem, wer sich vorher abstützt (Halsbelastung, Kopfbewegung, Femur/Becken beim Abstützen am Fußraum).

**Grenzen:** keine Kraft-Längen- und Kraft-Geschwindigkeits-Beziehung (Hill), keine getrennten Beuger/Strecker, Rumpfwerte isokinetisch statt isometrisch, keine Skalierung auf Körpergröße und -gewicht (die Bezugswerte der Probanden fehlen in den ausgewerteten Daten). Noch nicht in Unity geprüft.

### Organbelastung beim Bauchanprall (`organ_model.json`)

Milzruptur und Leberlazeration entstehen in der Sandbox nicht mehr aus einer pauschalen Kraftkurve, sondern aus der Dehnung des Organs im Vergleich zur Belastbarkeit seines Gewebes (`tissues.json`). Kette in `OrganMechanics.cs` (ohne Unity, getestet):

1. **Eindrückung:** Kontaktkraft am Abdomen (Summe der Kontakte, CFC 600) geteilt durch die dynamische Bauchsteifigkeit **27 kN/m** — Leichenversuche mit starrem Stab bei 6,3 m/s (Hardy et al. 2001; Cavanaugh et al. 1986: 21 kN/m).
2. **Kompression C** = Eindrückung / Bauchtiefe (frontal) bzw. Bauchbreite (seitlich, |lateral| > 0,3). Maße aus dem eigenen Anatomiemodell (BodyParts3D, Rumpfhaut auf Höhe LWK 1: Tiefe 0,226 m, Breite 0,289 m bei 1,651 m Körperhöhe), mit der Patientengröße skaliert. Begrenzt auf 80 %.
3. **Lastpfad:** Organe werden gleichmäßig um C gestaucht (Annahme), Organe auf der Gegenseite eines seitlichen Anpralls nicht: Exposition e = clamp(1 + Seite · lateral, 0, 1) — didaktische Setzung, passend zur Felddaten-Verteilung (seitlich nahe Seite: Milz 47 %, Leber 13 % der AIS3+-Abdominalverletzungen; Yoganandan 2000).
4. **Dehnung:** Leber und Milz als wahre Stauchung −ln(1 − e·C), verglichen mit der Kompressions-Versagensdehnung (Kemper et al. 2013 Leber: 0,94 / 0,73 / 0,61 / 0,60; Kemper et al. 2011 Milz: 0,52 / 0,51 / 0,48 / 0,46 bei 0,01 / 0,1 / 1 / 10 s⁻¹). Nieren: keine Druckdaten — Querdehnung bei inkompressiblem Gewebe (1 − C)^−½ − 1 gegen die Zug-Reißdehnung (Siripurapu 2013).
5. **Dehnrate:** wahre Spitzendehnung / Anstiegszeit (ab 5 % der Spitze); Versagensdehnung log-linear zwischen den Messraten interpoliert, außerhalb der nächste Messwert (Anpralle liegen meist über 10 s⁻¹).
6. **Risiko** = Anteil der Spender, deren Gewebe bei dieser Dehnung versagt hätte: Φ((u − 1) / CV) mit u = Dehnung / Versagensdehnung. CV aus den Zugversuchen: Leber 0,10 / 0,30 = 0,33 (Kemper 2010, auch für die Milz verwendet, weil dort je Dehnrate nur Mittelwerte veröffentlicht sind), Niere 0,138 / 0,415 = 0,33.

**Kontrolle gegen Leichenversuche** (Test `Organmodell`):

| Fall | Modell | Literatur |
|---|---|---|
| Seitlich, 44 % Kompression, Milz (nahe Seite) | 78 % Riss | 25 % *kritische* Verletzung (AIS4+) bei 44 % (Viano et al. 1989) |
| Seitlich, 44 % Kompression, Leber (nahe Seite) | 46 % Riss | wie oben |
| 9,6 % Kompression (erster Knickpunkt der Kraftkurve, Hardy 2001) | 1 % | keine Organverletzung erwartet |

Das Modell gibt die Wahrscheinlichkeit für einen Riss *jeden* Grades an; dass sie bei Vianos Toleranzwert über 25 % (nur kritische Verletzungen) liegt, ist deshalb erwartbar. Die Milz versagt früher als die Leber (geringere Versagensdehnung) — passend dazu, dass sie bei seitlichem Anprall das am häufigsten verletzte Organ ist.

Kraft → Risiko (Körpergröße 1,80 m, frontal): 1 kN → C 15 %, Leber 1 %; 2 kN → 30 %, 11 %; 3 kN → 45 %, 50 %; 4 kN → 60 %, 94 %.

**Grenzen:** Die Kontaktkraft stammt von einem starren Ragdoll, nicht von einem nachgiebigen Bauch — das Modell rechnet die Eindrückung nachträglich aus der Kraft, statt sie zu simulieren. Die Steifigkeit steigt in den Versuchen mit der Geschwindigkeit (63 kN/m bei 9,2 m/s); verwendet wird der Wert bei 6,3 m/s, bei schnellen Anprallen ist die Eindrückung daher eher überschätzt. Gleichmäßige Stauchung aller Organe im Lastpfad, Schutz durch die unteren Rippen und Organbewegung bei Dezeleration (Abriss an Bändern und Gefäßstielen) sind nicht modelliert. Nieren haben noch keine Verletzung in der Datenbank (nur Anzeige).


**Kalibrierung (`calibration.json`):** Referenztests aus NHTSA-Berichten stellt die Sandbox auf Knopfdruck je dreimal nach. Faktor = Referenzwert / Mittel der Sandbox, begrenzt auf 0,25–4, gemittelt über alle Referenzen; danach gilt: angezeigter Wert = Messwert × Faktor. Erste Referenz: NHTSA FMVSS-208-Test 2004, Jeep Liberty, Fahrer (Hybrid III 95. Perzentil): HIC15 575, Brust 46 g, Deflexion 32,5 mm, Nij 0,31. Eine einzelne Referenz ist schwach — weitere Testberichte ergänzen (NHTSA Vehicle Crash Test Database). Faktoren weit weg von 1 bedeuten, dass das Modell selbst nachgebessert werden muss (z. B. Gurt- oder Brustkorbsteifigkeit).

**Grenzen:** Das Ragdoll bleibt ein Mehrkörpermodell aus starren Teilen: keine Weichteile, keine Fahrzeugverformung (Schlittentest mit vorgegebenem Puls), Gelenkgrenzen und Muskeltonus sind Schätzungen.

## Anatomiemodell (Patient, Röntgenblick, Ragdoll-Haut)

Haut, Knochen, Organe und große Gefäße stammen aus **BodyParts3D** (Mitsuhashi et al. 2009, Datenbank des Database Center for Life Science, Japan), einem Modell eines erwachsenen Mannes (Körperhöhe im Modell 1,66 m, im Spiel auf die Patientengröße skaliert). `tools/anatomy/build_anatomy.py` wählt ca. 330 Strukturen aus, vereinfacht sie, vernetzt die Haut neu (Voxel-Hülle, geschlossen) und schreibt `StreamingAssets/NotfallSIM/anatomy/body.bin`.

**Prüfung der Lagebeziehungen** (`tools/anatomy/check_anatomy.py`, 74 Regeln, alle erfüllt): Seiten (Leber, Gallenblase, V. cava inferior rechts; Milz, Magen, Aorta descendens links; Lungenlappen), Höhen (Nieren Th12–L3, rechte tiefer; Nebennieren auf dem oberen Pol; Leberkuppel unter dem Herzen; Trachealbifurkation Th5; Schildknorpel C5; Aortenbifurkation L4; Truncus coeliacus Th12 über A. mesenterica sup./inf.; Harnblase im kleinen Becken, Prostata darunter), vorn/hinten (Ösophagus hinter der Trachea; Nieren retroperitoneal; Pankreas hinter dem Magen; Rektum hinter der Blase; V. renalis sinistra vor der Aorta; V. subclavia vor der A. subclavia), Hals (V. jugularis interna lateral der A. carotis), Extremitäten (N.–A.–V. femoralis von lateral nach medial; N. ischiadicus hinter dem Femur; A. poplitea in der Kniekehle; A. brachialis medial des Humerus; N. radialis im Sulcus; N. ulnaris hinter dem Epicondylus medialis; A. radialis/ulnaris auf ihrer Seite), Rückenmark im Wirbelkanal C1–L1, alle Strukturen innerhalb der Haut, ergänzte Bahnen nicht durch Knochen. Die Lage der BodyParts3D-Organe zueinander stammt aus den Originaldaten; die Prüfung stellt vor allem sicher, dass die Umrechnung nach Unity nichts spiegelt und die ergänzten Bahnen stimmen. Benachbarte Organe berühren sich teils mit wenigen Millimetern Überlappung (Segmentierung der Originaldaten).

**Behobene Fehler (28.10.2026):** Dreiecke waren für Unity nach innen orientiert (Spiegelung bei der Umrechnung), dadurch Atmung an der Rückenseite; Hand-Haut zu 10–30 % am Becken gewichtet (Hände blieben beim Bewegen der Arme zurück); innere Oberarmhaut zu 30–50 % am Thorax (Nerven traten auf der Trage aus der Haut); Rückenmark lag in den Wirbelkörpern; N. ulnaris und A. tibialis posterior teils außerhalb der Haut; ergänzte Bahnen folgten Gelenkbewegungen nicht (jetzt je Segment geteilt); fünf Zungenbeinmuskeln waren als Knochen geführt; viele Bezeichnungen englisch; Arme lagen auf der Trage über den Rand hinaus und schwebten 2 cm über der Matratze.

- **Lizenz:** CC BY-SA 2.1 Japan. `body.bin` ist eine Bearbeitung und steht damit unter derselben Lizenz; Namensnennung im Hauptmenü und im Dateikopf. Der Programmcode ist davon nicht betroffen.
- **Ergänzt, nicht aus BodyParts3D:** Nerven (Rückenmark, Plexus brachialis mit N. medianus, ulnaris, radialis; N. femoralis, N. ischiadicus) und Extremitätengefäße als schematische Röhren entlang anatomischer Landmarken. Das Rückenmark verläuft durch die aus jedem Wirbel bestimmte Kanalmitte (größter umschlossener Freiraum im Querschnitt) von der Medulla oblongata bis L1. Alle Bahnen werden innerhalb der Haut und außerhalb der Knochen gehalten und je Segment geteilt, damit sie Gelenkbewegungen folgen. Verlauf didaktisch, nicht vermessen.
- **Gehirn:** Marklager, Hirnstamm, Kleinhirn, Thalamus und 33 Gyri der Großhirnrinde (BodyParts3D enthält nicht alle Windungen, z. B. fehlt der Okzipitalpol; dort ist das Marklager sichtbar). Abstand Rinde–Kalotte im Median 4 mm.
- **Zwerchfell** aus BodyParts3D (Kuppel auf Höhe der Leberkuppel).
- **Skelett:** 12 Segmente; Gelenkzentren aus den Knochen bestimmt (Hüfte aus dem Acetabulum, Knie zwischen den Femurkondylen usw.). Massen je Segment nach de Leva (1996), getrennt nach Geschlecht. Dieselben Segmente nutzt die Unfall-Sandbox.
- **Atmung:** drei Blendshapes (Thorax rechts, Thorax links, Abdomen). Beim Spannungspneu links hebt sich die linke Seite kaum noch.
- **Verletzungen im Röntgenblick** (`anatomy/anatomy.json`): Zuordnung Verletzung → Strukturen, z. B. Beckenfraktur → Os coxae und Sakrum, Rippenserie links → Rippen 4–7 links. Erst nach Szenarioende sichtbar.

**Überschneidungen (`tools/anatomy/collide.py`):** Die Originaldaten sind getrennt segmentiert; benachbarte Strukturen durchdrangen sich deshalb vor der Bearbeitung an 462 Stellen (Oberflächen schneiden sich um mehr als 0,5 mm) um bis zu 12 mm (z. B. A. subclavia in der Lungenspitze, Thymus im Manubrium, Milz im Magen, Wirbelkörper in der Lunge, V. iliaca communis im 5. Lendenwirbel). Die nachgiebigere Struktur weicht der festeren aus, wie im Körper: Knochen > Arterien > Herz > Venen > Atemwege > Nervensystem > Zwerchfell > Nieren/Harnwege > Prostata > Leber > Milz > Pankreas > Nebennieren > Thymus > Gallenblase > Magen-Darm > Lunge. Die ausweichende Oberfläche wird an der Kontaktstelle verfeinert und entlang ihrer eigenen Innennormalen eingedellt, bis 0,5 mm Abstand bleiben — so entstehen die bekannten Abdrücke (Impressio cardiaca und Sulcus a. subclaviae der Lunge, Impressio renalis der Leber, Rippenabdrücke). Erlaubt bleiben Übergänge, die zusammengehören: Äste eines Gefäßbaums, Mündungen ins Herz, Bronchien und Lungengefäße in der Lunge, Gefäße am Hilus von Milz und Niere, die retrohepatische V. cava, Darmabschnitte untereinander, Niere–Harnleiter–Blase, Rippe–Rippenknorpel–Brustbein. Ergänzte Bahnen werden als Mittellinie um alle Hindernisse und aneinander vorbei geführt (Abstand ≥ Summe der Radien + 0,6 mm). Ergebnis (28.10.2026): 82 Stellen, höchstens 5,8 mm (Thymus am Manubrium), fast alle unter 2,5 mm mit wenigen Ecken — meist am dünnen Zwerchfell und an engen Stellen (Sehnerv im Canalis opticus, Ösophagus an der Trachea). Ergänzte Bahnen: eine Stelle (0,6 mm). Auf der Trage entstehen durch die Armlagerung höchstens 1,3 mm neue Überschneidung (Achselbahnen am Schulterblatt). Formänderung: Organe meist unter 5 % Volumen; stark zusammengedrückt werden dort, wo sich die Originaldaten von zwei Seiten überlappten, die V. iliaca communis sinistra zwischen A. iliaca communis dextra und LWK 5 (31 % Restvolumen; die anatomische May-Thurner-Engstelle), die V. renalis sinistra zwischen Aorta und A. mesenterica superior (58 %; Nussknacker-Position) sowie die flachen Nebennieren (49–57 %). Prüfen mit `collide.crossings()` bzw. `tools/anatomy/check_patient.py`.

**Skelettmuskulatur (28.10.2026, Schritt 1):** 237 Muskeln aus BodyParts3D (Hals, Rumpf, Becken, Arme, Beine; ohne Gesichts-, Hand- und Fußbinnenmuskeln; Interkostalmuskeln und M. transversus thoracis folgen in einem eigenen Schritt, weil die dünnen Platten zwischen 24 Rippen mit dem allgemeinen Verfahren Stunden brauchen). Lateinische Bezeichnungen mit Caput/Pars und Seite. Kleine abgetrennte Segmentierungsreste (< 3 % der Hauptfläche) entfernt. Muskeln sind wie die Haut auf zwei benachbarte Segmente gewichtet (sie ziehen über Gelenke) und erscheinen im Röntgenblick als eigene Ebene „Muskeln“ (standardmäßig aus). In der Kollisionsbereinigung stehen sie zwischen Milz und Pankreas; der kleinere von zwei Muskeln weicht dem größeren aus, Köpfe und Teile desselben Muskels sowie beide Seiten (Linea alba, Nackenmitte) dürfen sich berühren. Gewebeeigenschaften: vom menschlichen Zwerchfell übertragen (Gaur 2016), siehe Gewebetabelle.

**Leitungsbahnen in den Muskellücken (`tools/anatomy/pathways.py`):** Die ergänzten Nerven und Extremitätengefäße folgen Wegpunkten, die aus den Querschnitten der Nachbarmuskeln bestimmt werden (größter freier Raum nahe einer Schätzung): Trigonum femorale, Adduktorenkanal, Fossa poplitea, tiefe Flexorenloge, Foramen infrapiriforme, zwischen M. biceps femoris und M. semimembranosus, Rinne zwischen M. psoas und M. iliacus, Skalenuslücke, Achsel hinter dem M. pectoralis minor, Sulcus bicipitalis medialis, Sulcus n. radialis, zwischen den Köpfen des M. pronator teres und des M. flexor carpi ulnaris, Karpaltunnel. Die Bahn wird glatt geführt (Hindernisse: Knochen, Organe, große Gefäße, Haut, andere Bahnen); die Muskeln bekommen danach eine Rinne um sie, wie im Körper (z. B. Sulcus bicipitalis). **Durchtritte**, an denen eine Bahn einen Muskel tatsächlich durchquert, sind einzeln hinterlegt und in `body.bin` gespeichert: A./V. femoralis durch den Hiatus adductorius (M. adductor magnus), N. musculocutaneus durch den M. coracobrachialis, R. profundus n. radialis durch den M. supinator (Frohse-Arkade), N. fibularis communis durch den Ursprung des M. fibularis longus, Plexus lumbalis im M. psoas. Neu ergänzt: N. musculocutaneus, N. fibularis communis (Fibulakopffraktur → Fußheberschwäche).

**Prüfung (`check_anatomy.py`):** 143 von 145 Regeln erfüllt, darunter Bauchwandschichten an der Flanke, M. psoas vor dem M. quadratus lumborum, Gluteus maximus über medius, Verlauf des M. sartorius, Quadrizeps/Ischiokrurale/Adduktoren um das Femur, Gastrocnemius über Soleus, Bizeps/Trizeps, Deltoideus, Scalenus anterior vor medius, Pectoralis major vor minor, Trapezius über den Rhomboidei sowie alle Bahnregeln (A. femoralis unter dem Sartorius im Adduktorenkanal und nach dem Hiatus hinter dem Femur, N. ischiadicus unter dem Piriformis heraus und unter dem Gluteus maximus, A. tibialis posterior zwischen Soleus und Tibialis posterior, Plexus und A. subclavia in der Skalenuslücke, N. medianus zwischen den Pronator-teres-Köpfen, A. radialis lateral der Flexor-carpi-radialis-Sehne). Offen: A. femoralis links 1 mm zu tief für die Regel „vor dem M. pectineus“; Bahnen streifen an 77 Stellen noch Muskeln (Mittellinie bis 5 mm, meist < 2 mm im Muskel, v. a. V. femoralis an den Adduktoren); Armbahnen: Faszikel und Nerven sind relativ zur A. axillaris gelegt (lateral, medial, posterior) und gehen vom Plexus ab; die früheren Zacken in Achsel und Ellbogen sind beseitigt.

**Gelenkzentren** von Hüfte und Schulter als Kugelfit an Femur- und Humeruskopf (Radius 23 bzw. 22 mm); vorher lagen sie 7 mm zu weit medial, beim Drehen des Arms verschob sich der Humeruskopf gegen die Pfanne.

**Grenzen:** ein Körperbau für alle Patienten (Frauen und Kinder nur skaliert), Organe in Ruhelage und ohne Verformung, Weichteile ohne Masse. Die Haut folgt den Segmenten mit linearer Gewichtung zweier benachbarter Segmente. In BodyParts3D liegen Oberarme und Oberschenkel innen an; die Haut ist dort verbunden. Weiche Übergänge verteilen die Dehnung, bei starkem Abspreizen (Motorrad-Sitz) entsteht eine kleine Achselfalte; eine harte Trennung wurde getestet und reißt sichtbar auf. Hand und Unterarm sind ein Segment (keine Handgelenksbewegung).

## Gewebeeigenschaften (`tissues.json`)

Mechanische Eigenschaften der Gewebe: 19 Gewebetypen, 89 Einzelmessungen aus 21 Quellen. Jedes Anatomieteil aus `body.bin` ist einem Gewebe zugeordnet (reguläre Ausdrücke auf die FMA-Namen; Test prüft, dass kein Teil fehlt). Jede Zahl steht mit Quelle, Versuchsart, Dehnrate, Richtung, Spezies und ggf. ± SD in der Datei; die Tabelle zeigt nur den Wert, den die Simulation als repräsentativ nutzt. Im Röntgenblick erscheint er unter dem Namen der Struktur.

| Gewebe | Evidenz | Dichte g/cm³ | Steifigkeit | Versagensdehnung | Versagensspannung |
|---|---|---|---|---|---|
| Haut | human | 1,09 [37] | 83,3 ± 34,9 MPa bei 0,012/s [42] | 54 ± 17 % bei 0,012/s [42] | 21,6 ± 8,4 MPa bei 0,012/s [42] |
| Knochen (Kortikalis) | human | 1,92 [37] | 13,9 GPa bei 0,5/s [40] | 2,7 % bei 0,5/s [40] | 124,2 MPa bei 0,5/s [40] |
| Knochen (überwiegend Spongiosa) | human | 1,18 [37] | 329,04 ± 91,78 MPa [41] | 0,7 ± 0,03 % [41] | Fließgr. 1,62 ± 0,44 MPa [41] |
| Knorpel (hyalin) | human | 1,1 [37] | 11,1 ± 5,6 MPa [54] | — | 3,8 (2–5,6) MPa [54] |
| Leber | human | 1,06 [37] | G 2 ± 0,3 kPa [46] | 30 ± 10 % bei 1/s [43] | 52,61 ± 25,73 kPa bei 1/s [43] |
| Milz | human | 1,06 [37] | — | 19 % bei 1/s [44] | 31,5 kPa bei 1/s [44] |
| Niere | eingeschränkt | 1,05 [37] | — | 41,5 ± 13,8 % bei 0,704/s [47] | 84,8 ± 29,3 kPa bei 0,704/s [47] |
| Gehirn | human | 1,04 [37] | G 0,9 (0,4–1,4) kPa [48] | — | — |
| Herzmuskel | — | 1,06 [37] | — | — | — |
| Lunge | Tierdaten | 0,26 [37] | 3,4 ± 0,4 kPa [57] | — | — |
| Zwerchfell (Skelettmuskel) | human | 1,05 [37] | 64,6 MPa bei 65/s [55] | 12,19 % bei 65/s [55] | 2,53 MPa bei 65/s [55] |
| Magen-Darm-Wand | eingeschränkt | 1,03 [37] | — | 15,8 ± 3,6 % bei 69,2/s [56] | 2,35 ± 1,37 MPa bei 69,2/s [56] |
| Arterie | human | 1,06 [37] | — | 39,4 ± 15,7 % [51] | 1,511 ± 0,621 MPa [51] |
| Vene | eingeschränkt | 1,06 [37] | — | λ 1,55 ± 0,15 [52] | 3,42 ± 1,38 MPa [52] |
| Peripherer Nerv | eingeschränkt | — | — | 19 (7–31) % [53] | — |
| Rückenmark | — | — | — | — | — |
| Weiches Organparenchym | — | 1,04 [37] | — | — | — |
| Hohlorgan-Wand (Harnwege, Gallenblase) | — | — | — | — | — |
| Auge | — | — | — | — | — |

**Lesehilfe:** G = Schermodul (für Weichgewebe gilt näherungsweise E = 3G, so rechnet der Code um); λ = Streckung (Dehnung = λ − 1); Fließgr. = Beginn bleibender Verformung (Spongiosa: Fließen, nicht Bruch). „eingeschränkt“: menschliche Daten, aber per Analogie auf andere Strukturen übertragen (Colon → Magen/Dünndarm, Aorta → alle Arterien, Brückenvenen → Körpervenen, Rippenknorpel → Kehlkopf/Trachea) oder nur als Poster veröffentlicht (Niere). „—“: keine verlässlichen frei zugänglichen Zahlen gefunden, absichtlich leer statt geschätzt.

**Was die Daten zeigen:**
- **Größenordnungen** liegen über sechs Zehnerpotenzen: Kortikalis ≈ 14 GPa, Spongiosa ≈ 0,3–3 GPa, Haut im linearen Bereich ≈ 80 MPa, Leber und Gehirn ≈ 3–6 kPa.
- **Knochen ist spröde** (Bruch bei ≈ 3 % Dehnung), Weichgewebe reißt erst bei 15–55 %.
- **Dehnrate:** Leber und Milz werden bei schneller Belastung fester, reißen aber bei kleinerer Dehnung (Kemper 2010/2012); Aorta und Zwerchfell sind dynamisch drei- bis viermal so fest wie quasistatisch. Venen reagieren kaum auf die Dehnrate (Lee & Haut 1989). Für Unfälle zählen deshalb die Werte bei 1–100 s⁻¹.
- **Milz < Leber < Niere** in der Reißfestigkeit des Parenchyms — passt dazu, dass Milz und Leber beim stumpfen Bauchtrauma am häufigsten verletzt sind (neben Lage und Größe). Die Kapsel erhöht die Festigkeit deutlich (Milz: 68 statt 32 kPa bei 1 s⁻¹).
- **Alter:** Aortenfestigkeit und -dehnbarkeit sinken linear mit dem Alter (Li 2023), Rippenknorpel wird weniger steif (Weber 2021).

**Grenzen:** Ex-vivo-Präparate (meist innerhalb 48 h post mortem; Einfrieren halbiert z. B. die Bruchdehnung von Rinderleber, Santago 2010), verschiedene Spannungs- und Dehnungsmaße zwischen Studien (2. Piola-Kirchhoff, wahr, technisch; Green-Lagrange vs. technisch), große Streuung zwischen Spendern (oft ± 30–100 %). Die Werte sind daher Größenordnungen für die Simulation, keine Grenzwerte für den Einzelfall. Noch nicht in der Physik genutzt: Stufe 2 (Organdehnung im Unfallmodell gegen die Versagensdehnung) baut darauf auf.

## Blutungsphysik (Mock-Engine und Browser-Prototyp)

Statt fester Raten mit linearer Obergrenze:

| Baustein | Modell | Wert | Quelle |
|---|---|---|---|
| Fluss durch die Läsion | Q = Q₀ · √((MAD − P_Raum) / (MAD₀ − P_Raum,0)) (Ausflussformel/Bernoulli); Q₀ = Ausgangsrate der Verletzung (didaktisch) | – | [72] |
| Bauchraum | IAP = IAP₀ · exp(V / (IAP₀ · C₀)); Milz, Leber und Mesenterium teilen sich den Raum | IAP₀ 7 mmHg, C₀ 400 mL/mmHg; ergibt +2 L ≈ 14 mmHg, Compliance bei +4 L ≈ 97 mL/mmHg | [73] |
| Becken, Oberschenkel | Gegendruck linear, erreicht beim Tamponadevolumen den Ausgangs-MAD (bei Hypotonie früher) | Tamponadevolumen wie bisher (didaktisch) | Annahme |
| Außen (Extremität, Arm) | kein Gegendruck | – | – |
| Läsionsart | seitlicher Einriss (zieht sich nicht zurück, kein Gerinnsel), Durchtrennung (Retraktion, Gerinnsel), Parenchym, venös | – | [ATLS] |
| Gerinnsel und Nachblutung | Gerinnsel, wenn der MAD 5 min unter 64 mmHg bleibt (Restfluss 10 %); löst sich, sobald der MAD 64 mmHg erreicht | 64 ± 2 mmHg | [74]; Bildungszeit und Restfluss Annahmen |

Folge im Spiel: Großzügige Volumengabe vor der Blutstillung kann ein Gerinnsel lösen (permissive Hypotension). Die
intraabdominelle Blutung tamponiert sich nicht von selbst (der Bauchraum fasst mehr als das Blutvolumen). Venöse und
parenchymatöse Blutungen sickern (kein Strahl), arterielle spritzen. Pulse rechnet Blutungen mit eigenem Modell; Läsionsart
und Gerinnsel gelten nur für die Mock-Engine.

## Haut (`skin.json`, Shader `NotfallSIM/Skin`)

| Merkmal | Modell | Quelle |
|---|---|---|
| Blässe | ab 15 % Blutverlust, voll bei 40 % | ATLS-Schockklassen |
| Kaltschweißig (glänzend) | ab 30 %, voll bei 40 % | ATLS |
| Marmorierung | netzförmig von den Knien aus, Ausdehnung mit der Schwere; ab 40 %, voll bei 55 % (Schwellen Annahme) | [75] |
| Zyanose | kapilläres Desoxy-Hb = Hb · (1 − (SaO2 + SvO2)/2), SvO2 = SaO2 − 25 %; sichtbar ab 5 g/dL, betont an Gesicht und Händen. Bei Anämie erst bei tieferer Sättigung | [76], Extraktion: Guyton |
| Licht unter der Haut | Wrap-Lighting je Farbkanal (Rot 0,55, Grün 0,25, Blau 0,15; Werte Annahme), rötliche Schattenkante | [77] |
| Glanz | zwei GGX-Keulen (Rauheit 0,35 und 0,6), F0 0,028 (Brechungsindex ≈ 1,38) | [78] |
| Poren | Normalenstörung aus Rauschen auf der Ruhelage (bewegt sich mit der Haut) | Darstellung |

## Sichtbare Verletzungszeichen (`injury_visuals.json`)

Jedes Zeichen gehört zu einer Verletzung und zeigt einen ihrer Befundtexte (geprüft). Der Ort wird nicht von Hand
gesetzt: Ein Strahl von einer anatomischen Struktur (z. B. 5./6. Rippe links) nach außen trifft die Haut; von dort wird
der Hautbereich über die Kanten des Hautnetzes ausgewählt (greift nicht auf den anliegenden Arm über). Kleine Zeichen
(Stich, Schnitt) werden lokal feiner unterteilt. Die Auflagen tragen die Gewichte und Blendshapes der Haut, gehen also
mit Lagerung, Atmung und Schwellung mit.

| Größe | Wert | Herleitung | Quelle |
|---|---|---|---|
| Arten | Prellmarke, Hämatom, Platzwunde, Schnitt, Riss-Quetschwunde, Schürfung, Stich, offene Fraktur, Amputation, Blut auf der Haut | Farben/Formen sind Darstellungsannahmen | Farbe frischer Prellungen: [68] |
| Schwellung | Volumen = Blutverlust der Verletzung ins Gewebe (Femurfraktur, Hämoperitoneum) | Σ Fläche × Normalverschiebung = Volumen | Masseerhaltung |
| Blutlache | Dicke e = 2·√(γ/ρg)·sin(θ/2) ≈ 3,2 mm, Fläche = Volumen/e | Pfützenformel | [69], γ = 52,6 mN/m [70], ρ = 1,06 g/mL [71] |
| Blutstrahl | v = √(2·p_sys/ρ) ≈ 5,5 m/s bei 120 mmHg | Bernoulli, ideale Obergrenze | [72] |

Penetrierende Verletzungen und Amputation (`injuries.json`, Mechanismus „Messerangriff“ in `mechanisms.json`):
Unterschenkelamputation rechts (Motorrad, selten; schließt die offene Unterschenkelfraktur aus; Tourniquet), Stichverletzung
linker Oberbauch mit Mesenterialblutung (eFAST positiv, Laparotomie, die jetzt auch den Blutungsort „Abdomen“ stillt),
Schnittverletzung linker Unterarm mit A.-radialis-Verletzung als Abwehrverletzung (neue Maßnahme Druckverband). Vorgehen nach
ATLS (externe Blutung zuerst, penetrierendes Abdominaltrauma mit Schock: Laparotomie) und S3-Leitlinie (Tourniquet); die
Blutungsraten sind didaktische Spielwerte wie bei den übrigen Verletzungen. Eine vollständig durchtrennte Arterie zieht sich
zurück und kontrahiert (ATLS), daher blutet die Amputation hier langsamer als die offene Fraktur mit Gefäßverletzung.

Wundtiefe und Eintrocknung: Der Abfall eines Wundzeichens dient im Shader als Höhenfeld (Wundgrund bis 4 mm tief, Rand
leicht erhaben), daraus die Normale über den Oberflächengradienten (Mikkelsen 2010) und eine Verschattung im Wundgrund;
Tiefe je Art (Schnitt klafft, Stich tief, Schürfung flach). Blut bleibt frisch und glänzend, solange die Verletzung blutet,
und wird danach in etwa 15 min matt und dunkler (Annahme); die Bräunung durch Oxidation dauert Stunden bis Tage [79].
Wunden ohne laufende Blutung sind bei Ankunft etwa 20 min alt (Annahme).

Blut als Flüssigkeit: Rinnsale laufen vom Wundort im steilsten Abstieg über das Hautnetz; die Schwerkraft wird je
Körpersegment aus der Lagerung bestimmt (Arme leicht nach hinten gedreht). Sie enden, wo die Haut nach unten zeigt (Abtropfen
auf die Trage), oder wo das Gefälle unter etwa 5° fällt (Blut bleibt stehen). Je 40 mL Verlust erscheint ein weiteres
Rinnsal, die Front läuft mit 2 cm/s, solange es blutet; Breite 3–12 mm mit der dritten Wurzel des Flusses. Diese Werte sind
Darstellungsannahmen; die Blutmenge selbst kommt aus der Physiologie. Rinnsale trocknen wie das übrige Blut.

Annahmen: Kontaktwinkel 90° (keine Messung für Blut auf Matratzenkunststoff gefunden); die Bauchdeckenhebung setzt das
ganze intraperitoneale Blut an (Obergrenze); Galeaschwellung beim EDH 15 mL. Tropfen sind nur Darstellung, die Blutmenge
kommt aus der Physiologie (Verlust je Verletzung).

## Offene Punkte zum Prüfen

- Haut-Shader in Unity prüfen (nur außerhalb mit einer GLSL-Portierung gerendert): Kompiliert er unter URP (Forward/Forward+), passt die Helligkeit unter der OP-Leuchte? Ragdoll-Haut im Crash-Simulator nutzt noch URP/Lit.

- Verletzungszeichen in Unity prüfen (Lage, Farbe, Blutstrahl, Lache, Amputationsstumpf). SDH/EDH-Form im Röntgenblick weiter offen.
- Neue Verletzungen (Unterschenkelamputation, Stichverletzung Oberbauch, Schnittverletzung Unterarm): Blutungsraten didaktisch wie bei den übrigen; Hohlorganverletzung, Nervenverletzung und Replantation nicht modelliert; Pulse-Kompartiment der Mesenterialblutung (SmallIntestine) prüfen.

- [ ] Empfehlungen und Empfehlungsgrade zu Beckengurt, Volumentherapie und permissiver Hypotension in der S3-Leitlinie (MAGICapp) nachlesen
- [ ] Lieurance 1992 im Volltext prüfen (Uni-Zugang)
- [ ] ATLS-Tabelle der Schockklassen gegen die 10. Auflage abgleichen
- [x] Entscheiden: Lehrbuch- oder Registerphysiologie? → MVP im Lehrbuchmodus, Realitätsmodus als zweite Schwierigkeitsstufe in Phase 2 (27.10.2026)
- [x] Literatur zum Spannungspneumothorax (Verlauf bei Spontanatmung vs. Beatmung)
- [ ] Zeitverlauf des Spannungspneus im Mock (ca. 20 Minuten bis Stillstand) klinisch plausibilisieren
- [ ] Empfehlung der S3-Leitlinie zu Ort und Technik der Entlastung nachlesen
- [ ] Zeitziele und „kritisch“-Markierungen der Bewertung prüfen (Tabelle oben)
- [ ] SHT-Empfehlungen (Intubation, Blutdruckziel, Osmotherapie) gegen S3-Leitlinie Version 5.0 prüfen
- [ ] Realistische Zeit von Anordnung bis Kraniotomie (CT, Transport, OP-Beginn) recherchieren
- [ ] Faktoren für Streuung, Betablocker und Alter im Realitätsmodus klinisch plausibilisieren
- [ ] Laktatwerte je Schockklasse mit Literatur belegen
- [ ] Transkapilläre Rückfüllung modellieren, damit das Hb über Stunden auch ohne Infusion sinkt
- [ ] Calcium-Abfall je Blutprodukt und Hämatokrit des EK mit Literatur belegen
- [ ] Polytrauma-Szenario klinisch durchspielen: Ist der Zeitdruck realistisch, sind die Prioritäten richtig gewichtet?
- [ ] Narkoseinduzierte Hypotonie bei der Intubation modellieren (relevant beim Polytrauma)
- [ ] Verletzungswahrscheinlichkeiten je Mechanismus mit TraumaRegister-DGU-Daten oder Kinematik-Studien belegen (Jahresbericht, ggf. Datenanfrage)
- [ ] Blutungsraten von Milz- und Leberverletzung nach AAST-Grad belegen
- [ ] Präklinischen Blutverlust gegen Registerdaten (Hb bzw. Schockindex bei Aufnahme nach Rettungszeit) plausibilisieren
- [ ] Normalbefunde (`findings.json`) und Befundtexte der Verletzungen klinisch gegenlesen
- [x] Befunde zeitabhängig machen (dynamicFindings, v0.8)
- [x] Differenzialdiagnosen ohne Physiologie ergänzen (differentials.json)
- [ ] Review-Tabelle aller Befundtexte durch Nutzer und Arzt abarbeiten, Ergebnisse in die Daten übernehmen
- [ ] Sandbox in Unity testen: Stabilität, Kniebeugung, Größenordnung der Messwerte
- [ ] Risikokurven Thorax (Deflexion, Laituri 2005), Femur (AIS2+) und Nij aus 73 FR 40016 (NCAP 2008, Anhang C) übernehmen
- [ ] HIC-Kurve: Zuordnung der Parameter (7,45231/0,73998) zu HIC15 vs. HIC36 im NHTSA-Original prüfen
- [ ] Sandbox gegen NHTSA-Crashtestdaten kalibrieren (Kalibrierlauf in Unity ausführen, weitere Referenzen ergänzen)
- [ ] Thoraxmodell: Parameter von Lobdell 1973 bzw. Kroell 1974 prüfen
- [ ] Vorzeichen von Halskraft/-moment und Femurkraft aus Joint.currentForce/currentTorque in Unity prüfen
- [ ] Halsverletzung (HWS) mit Physiologie in die Datenbank aufnehmen, dann Nij zuordnen
- [ ] Fehlende typische Verletzungen ergänzen: Wirbelsäule, Kalkaneus (Sturz), Aortenverletzung (Dezeleration), Rippenserie rechts
- [ ] Anatomie: Zuordnung Verletzung → Strukturen (`anatomy.json`) und schematische Nervenverläufe gegenlesen
- [ ] SDH/EDH als eigene Hämatom-Form (Sichel/Linse) zwischen Kalotte und Rinde darstellen statt Rinde einzufärben
- [ ] Restliche Überschneidungen am Zwerchfell (dünne Platte) gezielt nachbearbeiten
- [ ] Gewebe: Volltexte für Herzmuskel (Sommer 2015), Rückenmark (Bilston & Thibault 1996), Nierenkapsel (Snedeker 2005), Magen-Darm (Egorov 2002) und Harnblase prüfen und Werte ergänzen
- [ ] Gewebe: Werte je Dehnrate für Milz und Leber im Original (Kemper 2010/2012) gegenprüfen (hier aus Sammeltabelle in Accidental Injury, Kap. 14)
- [ ] Gewebe: menschliche Lungendaten suchen (bisher Schwein)
- [ ] Organmodell: Bauchsteifigkeit geschwindigkeitsabhängig (Hardy 2001: 27 → 63 kN/m) und Rippenschutz von Leber/Milz ergänzen; Nierenverletzung in die Datenbank aufnehmen
- [ ] Organmodell: Dezelerationsverletzungen (Aortenisthmus, Leberbänder, Nierenstiel) als Masse-Feder-Modell ergänzen
- [ ] Muskeltonus in Unity prüfen: Unterschied entspannt/angespannt/bewusstlos bei PKW frontal 30/50 km/h (Halskraft, Kopfweg, Femurkraft) gegen Probanden- und Leichenversuche (Beeman/Kemper)
- [ ] Muskeln Schritt 2: Interkostalmuskeln und M. transversus thoracis (eigenes Verfahren zwischen den Rippen)
- [x] Armbahnen geglättet: Faszikel relativ zur A. axillaris (28.10.2026)
- [ ] Restberührungen Bahn–Muskel (77 Stellen) beseitigen; Muskeln nach dem Formen wieder vereinfachen (derzeit ~890 000 Dreiecke)
- [ ] Muskelgewebe: menschliche Werte für Extremitätenmuskeln suchen (bisher vom Zwerchfell übertragen)
- [ ] Weibliches Anatomiemodell (z. B. Z-Anatomy, ebenfalls CC BY-SA) für Patientinnen prüfen

## Quellen

1. American College of Surgeons. *Advanced Trauma Life Support (ATLS) Student Course Manual*, 10th ed. Chicago: ACS; 2018.
2. DGU/DGOU. *S3-Leitlinie Polytrauma/Schwerverletzten-Behandlung*. AWMF-Register-Nr. 187-023, Version 5.0 (2025). [Leitlinienreport](https://register.awmf.org/assets/guidelines/187-023m_S3_KF_Polytrauma-Schwerverletzten-Behandlung_2026-05.pdf) · [MAGICapp](https://app.magicapp.org/#/guideline/8812)
3. Mutschler M, Nienaber U, Brockamp T, et al. Renaissance of base deficit for the initial assessment of trauma patients: a base deficit-based classification for hypovolemic shock developed on data from 16,305 patients derived from the TraumaRegister DGU. *Crit Care*. 2013;17:R42. [doi:10.1186/cc12555](https://doi.org/10.1186/cc12555)
4. Mutschler M, Nienaber U, Brockamp T, et al. A critical reappraisal of the ATLS classification of hypovolaemic shock: does it really reflect clinical reality? *Resuscitation*. 2013;84(3):309-313.
5. Mutschler M, Nienaber U, Münzberg M, et al. Assessment of hypovolaemic shock at scene: is the PHTLS classification of hypovolaemic shock really valid? *Emerg Med J*. 2014;31(1):35-40. [Artikel](https://emj.bmj.com/content/31/1/35)
6. Mutschler M, Nienaber U, Münzberg M, et al. The Shock Index revisited – a fast guide to transfusion requirement? *Crit Care*. 2013;17:R172. [doi:10.1186/cc12851](https://doi.org/10.1186/cc12851)
7. Devaney GL, Tarrant SM, Weaver N, King KL, Balogh ZJ. Major Pelvic Ring Injuries: Fewer Transfusions Without Deaths from Bleeding During the Last Decade. *World J Surg*. 2023;47(5):1136-1143. [doi:10.1007/s00268-023-06897-7](https://doi.org/10.1007/s00268-023-06897-7)
8. Croce MA, Magnotti LJ, Savage SA, Wood GW, Fabian TC. Emergent pelvic fixation in patients with exsanguinating pelvic fractures. *J Am Coll Surg*. 2007;204(5):935-939.
9. Ghaemmaghami V, Sperry J, Gunst M, et al. Effects of early use of external pelvic compression on transfusion requirements and mortality in pelvic fractures. *Am J Surg*. 2007;194(6):720-723.
10. BestBETs. Pelvic compression devices: Panacea or myth? [bestbets.org](https://www.bestbets.org/bets/pelvic-compression-devices-panacea-or-myth/)
11. Lieurance R, Benjamin JB, Rappaport WD. Blood loss and transfusion in patients with isolated femur fractures. *J Orthop Trauma*. 1992;6(2):175-179. (Zahlen bisher aus [Sekundärquelle](https://www.crisis-medicine.com/femur-fracture-blood-loss/))

12. Roberts DJ, et al. Clinical Presentation of Patients With Tension Pneumothorax: A Systematic Review. *Ann Surg*. 2015;261(6):1068-1078. [doi:10.1097/SLA.0000000000001073](https://doi.org/10.1097/SLA.0000000000001073)
13. Ahmad SJS, et al. Meta-analysis of the optimal needle length and decompression site for tension pneumothorax and consensus recommendations on current ATLS and ETC guidelines. *World J Emerg Surg*. 2025;20:39. [doi:10.1186/s13017-025-00613-7](https://doi.org/10.1186/s13017-025-00613-7)

14. Trentzsch H, Huber-Wagner S. S3-Leitlinie Polytrauma/Schwerverletzten-Behandlung: Neuerungen 2016. *Bayerisches Ärzteblatt*. 2017;(1-2):8-14. [PDF](https://www.bayerisches-aerzteblatt.de/fileadmin/aerzteblatt/ausgaben/2017/01/einzelpdf/BAB_1-2_2017_8_14.pdf) — Empfehlungen und Empfehlungsgrade der Version 2016; in der aktuellen Version 5.0 prüfen
15. Deutsches Ärzteblatt. Unfallchirurgen melden mehr Schwerverletzte (TraumaRegister DGU, Jahresbericht 2016). 15.09.2017. [Artikel](https://www.aerzteblatt.de/news/unfallchirurgen-melden-mehr-schwerverletzte-3f5de2e0-a89d-4b1b-a311-ebff9ae5bffd)
16. Duchesne J, McLafferty BJ, Broome JM, et al. Every minute matters: Improving outcomes for penetrating trauma through prehospital advanced resuscitative care. *J Trauma Acute Care Surg*. 2024. [doi:10.1097/TA.0000000000004363](https://doi.org/10.1097/TA.0000000000004363)

17. Chesnut RM, et al. The role of secondary brain injury in determining outcome from severe head injury. *J Trauma*. 1993;34(2):216-222. [Abstract](https://www.qxmd.com/r/8459458)
18. Böttiger BW, et al. DGAInfo zur S3-Leitlinie Polytrauma/Schwerverletzten-Behandlung. *Anästh Intensivmed*. 2011;52:S693-S700. [PDF](https://www.ai-online.info/images/ai-ausgabe/2011/11-2011/supplement-10-2011/2011_11_S693-S700_SUPPLEMENT%20Nr.%2010%20-%202011%20-%20Trauma-Update%20%20S3-Leitlinie%20Polytrauma.pdf)
19. Brain Trauma Foundation. *Guidelines for the Management of Severe Traumatic Brain Injury*, 4th ed. 2016. (Zusammenfassung: [UMEM](https://em.umaryland.edu/educational_pearls/3108/))
20. Extradural hematoma: analysis of 146 cases. *Childs Nerv Syst*. 1993;9(2):96-99. [doi:10.1007/BF00305316](https://doi.org/10.1007/BF00305316)

21. Rossaint R, Afshari A, Bouillon B, et al. The European guideline on management of major bleeding and coagulopathy following trauma: sixth edition. *Crit Care*. 2023;27:80. [doi:10.1186/s13054-023-04327-7](https://doi.org/10.1186/s13054-023-04327-7)

22. Yoganandan N, Pintar FA, Gennarelli TA, Maltese MR. Patterns of abdominal injuries in frontal and side impacts. *Annu Proc Assoc Adv Automot Med*. 2000;44:17-36. [TRID](https://trid.trb.org/View/710752)
23. Newgard CD, et al. National guideline for the field triage of injured patients: recommendations of the National Expert Panel on Field Triage, 2021. *J Trauma Acute Care Surg*. 2022;93(2):e49-e60.
24. Falls from Great Heights: Risk to Sustain Severe Thoracic and Pelvic Injuries Increases with Height of the Fall. *J Clin Med*. 2021;10(11):2307.

25. NHTSA. Federal Motor Vehicle Safety Standards; Occupant Crash Protection (FMVSS No. 208), Final Rule. 65 FR 30680; 2000.
26. NHTSA. Consumer Information; New Car Assessment Program. 73 FR 40016; 11.07.2008. [PDF](https://www.nhtsa.gov/sites/nhtsa.gov/files/nhtsa-2006-26555-0114_0.pdf)
27. Prasad P, Mertz HJ, et al. Evaluation of the field relevance of several injury risk functions. *Stapp Car Crash J*. 2010;54:49-72.
28. Head Injury Criterion – mini review. *Am J Biomed Sci Res*. 2019;5(2). [Artikel](https://biomedgrid.com/fulltext/volume5/head-injury-criterion-mini-review.000957.php)
29. de Leva P. Adjustments to Zatsiorsky-Seluyanov's segment inertia parameters. *J Biomech*. 1996;29(9):1223-1230.
30. Euro NCAP. Assessment Protocol – Adult Occupant Protection. [euroncap.com](https://www.euroncap.com/en/for-engineers/protocols/)

31. Lau IV, Viano DC. The Viscous Criterion – Bases and Applications of an Injury Severity Index for Soft Tissues. SAE 861882; 1986. [SAE](https://saemobilus.sae.org/papers/viscous-criterion-bases-applications-injury-severity-index-soft-tissues-861882)
32. SAE J211-1 / ISO 6487. Instrumentation for impact test – Electronic instrumentation (Kanalfilterklassen CFC). [Übersicht](https://www.dadisp.com/cfc1.htm)
33. NHTSA. FMVSS 208 Test Report 2004 Jeep Liberty (Test v05137). [PDF](https://nrd-static.nhtsa.dot.gov/reports/vehdb/v00000/v05100/v05137R007.pdf)
34. Injury Criteria and Motor Vehicle Regulations (Tabellen zu FMVSS 208 / ECE R94). [Artikel](https://musculoskeletalkey.com/injury-criteria-and-motor-vehicle-regulations/)
35. Lobdell TE, et al. Impact response of the human thorax. In: Human Impact Response. Plenum; 1973.

36. Mitsuhashi N, Fujieda K, Tamura T, Kawamoto S, Takagi T, Okubo K. BodyParts3D: 3D structure database for anatomical concepts. *Nucleic Acids Res*. 2009;37(Database issue):D782-D785. [doi:10.1093/nar/gkn613](https://doi.org/10.1093/nar/gkn613) · Daten: [lifesciencedb.jp/bp3d](https://lifesciencedb.jp/bp3d/), CC BY-SA 2.1 Japan

37. ICRU Report 46: Photon, Electron, Proton and Neutron Interaction Data for Body Tissues. International Commission on Radiation Units and Measurements; 1992. Dichten wie dort tabelliert, hier übernommen aus Tab. 2 in: Evaluation of CT calibration curves from stoichiometric and tissue substitute methods according to tissue characteristics. Radioprotection. 2019;54(2):117-123. doi:10.1051/radiopro/2019011 [Link](https://www.radioprotection.org/articles/radiopro/full_html/2019/02/radiopro180050/T2.html)
38. Reilly DT, Burstein AH. The elastic and ultimate properties of compact bone tissue. J Biomech. 1975;8(6):393-405. doi:10.1016/0021-9290(75)90075-5 [Link](https://lakeslab.ep.wisc.edu/BoneAniso.html)
39. Park JB. Biomaterials: An Introduction. Plenum Press; 1979. Zitiert nach der Zusammenstellung von R. Lakes (University of Wisconsin). [Link](https://lakeslab.ep.wisc.edu/BoneAniso.html)
40. Kemper AR. Material Properties of Human Rib Cortical Bone from Dynamic Tension Coupon Testing. Masterarbeit, Virginia Polytechnic Institute and State University; 2005. Veröffentlicht als: Kemper AR, McNally C, Kennedy EA, et al. Stapp Car Crash J. 2005;49:199-230 (SAE 2005-22-0010). [Link](https://vtechworks.lib.vt.edu/bitstream/10919/43709/1/KemperThesis.pdf)
41. Zhou B, Liu XS, Wang J, Lu XL, Fields AJ, Guo XE. Dependence of mechanical properties of trabecular bone on plate-rod microstructure determined by individual trabecula segmentation (ITS). J Biomech. 2014;47(3):702-708. doi:10.1016/j.jbiomech.2013.11.039 [Link](https://escholarship.org/content/qt81m8h5pk/qt81m8h5pk.pdf)
42. Ní Annaidh A, Bruyère K, Destrade M, Gilchrist MD, Otténio M. Characterization of the anisotropic mechanical properties of excised human skin. J Mech Behav Biomed Mater. 2012;5(1):139-148. [Link](https://arxiv.org/abs/1302.3022)
43. Kemper AR, Santago AC, Stitzel JD, Sparks JL, Duma SM. Biomechanical response of human liver in tensile loading. Ann Adv Automot Med. 2010;54:15-26. [Link](https://vtechworks.lib.vt.edu/handle/10919/32768)
44. Kemper AR, Santago AC, Stitzel JD, Sparks JL, Duma SM. Biomechanical response of human spleen in tensile loading. J Biomech. 2012;45(2):348-355. doi:10.1016/j.jbiomech.2011.10.022 [Link](https://wbldb.lievers.net/10027917.html)
45. Yoganandan N, Nahum AM, Melvin JW (Hrsg.). Accidental Injury: Biomechanics and Prevention. 3. Aufl. Springer; 2015. Kap. 14: Impact and Injury Response of the Abdomen (Tabellen der Materialeigenschaften). [Link](https://www.springer.com/us/book/9781493917310)
46. Rouvière O, Yin M, Dresner MA, et al. MR elastography of the liver: preliminary results. Radiology. 2006;240(2):440-448. doi:10.1148/radiol.2402050606 [Link](https://pubs.rsna.org/doi/10.1148/radiol.2402050606)
47. Siripurapu et al. Material and Failure Modeling of Human Kidney (Poster). Injury Biomechanics Symposium, Ohio State University; 2013. [Link](https://ibrc.osu.edu/wp-content/uploads/2013/12/Siripurapu_Poster_2013.pdf)
48. Budday S, Sommer G, Birkl C, et al. Mechanical characterization of human brain tissue. Acta Biomater. 2017;48:319-340. doi:10.1016/j.actbio.2016.10.036 [Link](https://cris.fau.de/publications/121788744/)
49. Green MA, Bilston LE, Sinkus R. In vivo brain viscoelastic properties measured by magnetic resonance elastography. NMR Biomed. 2008;21(7):755-764. Zitiert nach Tab. 1 in: Bartlett RD, Choi D, Phillips JB. Biomechanical properties of the spinal cord: implications for tissue engineering and clinical translation (UCL). [Link](https://discovery-pp.ucl.ac.uk/1514941/)
50. Mohan D, Melvin JW. Failure properties of passive human aortic tissue. I—Uniaxial tension tests. J Biomech. 1982;15(11):887-893. doi:10.1016/0021-9290(82)90055-0 [Link](https://deepblue.lib.umich.edu/items/f9c46d14-7802-431a-8387-b5fe1844b71f)
51. Li et al. Failure properties of healthy human descending thoracic aorta (proximal/distal, Alter 27–86 J., n = 50). Front Bioeng Biotechnol. 2023;11:1178199. doi:10.3389/fbioe.2023.1178199 [Link](https://www.frontiersin.org/journals/bioengineering-and-biotechnology/articles/10.3389/fbioe.2023.1178199/full)
52. Lee MC, Haut RC. Insensitivity of tensile failure properties of human bridging veins to strain rate: implications in biomechanics of subdural hematoma. J Biomech. 1989;22(6-7):537-542. [Link](https://wbldb.lievers.net/10147592.html)
53. Sunderland S, Bradley KC. Stress-strain phenomena in human peripheral nerve trunks. Brain. 1961;84:102-119. Zitiert nach: Kleinrensink GJ et al. Mechanical tension in the median nerve. Clin Biomech. 1995;10(5):240-244. [Link](https://repub.eur.nl/pub/73013/REPUB_73013_OA.pdf)
54. Weber M, Rothschild MA, Niehoff A. Anisotropic and age-dependent elastic material behavior of the human costal cartilage. Sci Rep. 2021;11:13618. doi:10.1038/s41598-021-93176-x [Link](https://www.nature.com/articles/s41598-021-93176-x)
55. Gaur P, Chawla A, Verma K, et al. Characterisation of human diaphragm at high strain rate loading. J Mech Behav Biomed Mater. 2016;60:603-616. [Link](https://www.imechanica.org/sites/default/files/diaphragm.pdf)
56. Howes MK, Hardy WN. Dynamic material properties of the post-mortem human colon. Proc IRCOBI Conf. 2013 (Paper 20). [Link](https://wbldb.lievers.net/10098480.html)
57. Polio SR et al. Cross-platform mechanical characterization of lung tissue. PLoS One. 2018. Zitiert nach: An analytical model for estimating alveolar wall elastic moduli from lung tissue uniaxial stress-strain curves. Front Physiol. 2020;11:121. doi:10.3389/fphys.2020.00121 [Link](https://www.frontiersin.org/articles/10.3389/fphys.2020.00121/full)

58. Hardy WN, Schneider LW, Rouhana SW. Abdominal impact response to rigid-bar, seatbelt, and airbag loading. *Stapp Car Crash J*. 2001;45:1-32. (Werte zitiert nach Kap. 14 in Accidental Injury, 3. Aufl., Springer 2015)
59. Viano DC, Lau IV, Asbury C, King AI, Begeman P. Biomechanics of the human chest, abdomen, and pelvis in lateral impact. *Accid Anal Prev*. 1989;21(6):553-574. [TRID](https://trid.trb.org/View/302819)
60. Cavanaugh JM, Nyquist GW, Goldberg SJ, King AI. Lower abdominal tolerance and response. SAE 861878; 1986. (zitiert nach Kap. 14 in Accidental Injury)

61. Harbo T, Brincks J, Andersen H. Maximal isokinetic and isometric muscle strength of major muscle groups related to age, body mass, height, and sex in 178 healthy subjects. *Eur J Appl Physiol*. 2012;112:267-275. [PDF](https://scispace.com/pdf/maximal-isokinetic-and-isometric-muscle-strength-of-major-4p5ixgnfk2.pdf)
62. Vasavada AN, Li S, Delp SL. Three-dimensional isometric strength of neck muscles in humans. *Spine*. 2001;26(17):1904-1909.
63. Unver F, Gur Kabul E, Buke M, Unver B. Trunk flexor and extensor muscle strength capacity in healthy individuals. *Turk J Sports Med*. 2024;59(3):112-118. [Artikel](https://journalofsportsmedicine.org/full-text/735/eng)
64. Anderson DE, Madigan ML, Nussbaum MA. Maximum voluntary joint torque as a function of joint angle and angular velocity. *J Biomech*. 2007;40(14):3105-3113. [CDC](https://stacks.cdc.gov/view/cdc/188754)
65. Kempter F, Lantella L, Stutzig N, Fehr J, Siebert T. Role of rotated head postures on volunteer kinematics and muscle activity in braking scenarios performed on a driving simulator. *Ann Biomed Eng*. 2023;51(4):771-782. [PDF](https://link.springer.com/content/pdf/10.1007/s10439-022-03087-9.pdf)
66. Kumar S, Ferrari R, Narayan Y. Kinematic and electromyographic response to whiplash loading in low-velocity whiplash impacts — a review. *Clin Biomech*. 2005;20:343-356. (zitiert nach 65)
67. Kemper AR, Beeman SM, Duma SM. Chest deflection of relaxed and braced human occupants in low severity frontal sled tests. Proc. 38th Int. Workshop on Human Subjects for Biomechanical Research; 2010. [Abstract](https://wbldb.lievers.net/10123431.html)

Angaben ohne Link (4, 8, 9, 23, 24, 25, 27, 29, 35) sind aus Referenzlisten übernommen und vor Zitation in einer Arbeit im Original zu prüfen.
68. Langlois NEI, Gresham GA. The ageing of bruises: a review and study of the colour changes with time. *Forensic Sci Int*. 1991;50(2):227-238. [DOI](https://doi.org/10.1016/0379-0738(91)90154-B)
69. de Gennes P-G, Brochard-Wyart F, Quéré D. *Capillarity and Wetting Phenomena*. Springer; 2004. Kap. 2. [DOI](https://doi.org/10.1007/978-0-387-21656-0)
70. Rosina J, Kvašňák E, Šuta D, Kolářová H, Málek J, Krajči L. Temperature dependence of blood surface tension. *Physiol Res*. 2007;56(Suppl 1):S93-S98. [PDF](https://www.biomed.cas.cz/physiolres/pdf/56/56_S93.pdf)
71. Kenner T. The measurement of blood density and its meaning. *Basic Res Cardiol*. 1989;84(2):111-124. [DOI](https://doi.org/10.1007/BF01907921)
72. White FM. *Fluid Mechanics*. 7th ed. McGraw-Hill; 2011. Kap. 3 (Bernoulli, Austrittsgeschwindigkeit).
73. Malbrain MLNG, Peeters Y, Wise R. The neglected role of abdominal compliance in organ-organ interactions. *Crit Care*. 2016;20:67. [DOI](https://doi.org/10.1186/s13054-016-1220-x)
74. Sondeen JL, Coppes VG, Holcomb JB. Blood pressure at which rebleeding occurs after resuscitation in swine with aortic injury. *J Trauma*. 2003;54(5 Suppl):S110-S117. [DOI](https://doi.org/10.1097/01.TA.0000047220.81795.3D)
75. Ait-Oufella H, Lemoinne S, Boelle PY, et al. Mottling score predicts survival in septic shock. *Intensive Care Med*. 2011;37(5):801-807. [DOI](https://doi.org/10.1007/s00134-011-2163-y)
76. Lundsgaard C, Van Slyke DD. Cyanosis. *Medicine (Baltimore)*. 1923;2(1):1-76. [DOI](https://doi.org/10.1097/00005792-192302010-00001)
77. Green S. Real-time approximations to subsurface scattering. In: Fernando R, ed. *GPU Gems*. Addison-Wesley; 2004. Kap. 16. [Online](https://developer.nvidia.com/gpugems/gpugems/part-iii-materials/chapter-16-real-time-approximations-subsurface-scattering)
78. Weyrich T, Matusik W, Pfister H, et al. Analysis of human faces using a measurement-based skin reflectance model. *ACM Trans Graph*. 2006;25(3):1013-1024. [DOI](https://doi.org/10.1145/1141911.1141987)
79. Bremmer RH, de Bruin KG, van Gemert MJC, van Leeuwen TG, Aalders MCG. Forensic quest for age determination of bloodstains. *Forensic Sci Int*. 2012;216(1-3):1-11. [DOI](https://doi.org/10.1016/j.forsciint.2011.07.027)
