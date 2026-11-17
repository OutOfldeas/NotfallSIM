# Roadmap und offene Punkte

Arbeitsliste von NotfallSIM. Die Modellbeschreibungen und Quellen stehen in [`LITERATUR.md`](LITERATUR.md).
Häkchen = erledigt; *kursiv* = Stand der Prüfung. „In Unity nachgemessen“ heißt: im Editor auf dem PC durchgespielt.

## Stand 17.11.2026 — erledigt (nur offline geprüft, in Unity noch nicht nachgemessen)

- [x] **Schädelbruch als Kraftplateau**: Übersteigt die Kontaktkraft am Kopf die Bruchkraft (Allsop 1991: 5 kN auf 5 cm², 12,5 kN auf 52 cm²), trägt die Kalotte nicht mehr. Die Auswertung bekommt weiter die geforderte Kraft (Bruchrisiko). Test: Kopf mit 7 m/s auf Asphalt 30,5 → 12,5 kN, HIC 12 051 → 5 798, Impression 0,9 cm; 3 m/s unverändert.
- [x] **Sturz aus der Höhe**: Kalkaneusfraktur (re/li) und Berstungsfraktur LWK 1 als Verletzungen mit Befunden, CT-Befund, Bewertungskriterium und Anatomie. Messgrößen: axiale Kraft im Knie und in der LWS. Risikokurven (Weibull): Kalkaneus/Pilon nach Yoganandan 2015 (50 % bei 7,67 kN), LWS nach Stemper 2018 (50 % bei 4,5 kN, Formparameter angenommen). Zufallsfälle: LWK 35 %, Kalkaneus je Seite 15 %.
- [x] **Szenario**: Delle am Gerüst so breit wie die Überdeckung mit dem Gegner.
- [x] Offene Punkte aus `LITERATUR.md` hierher ausgelagert.

**Beim nächsten Durchlauf in Unity prüfen**
- [ ] Sturz 6 m: Werte „Axiale Kraft Unterschenkel“ und „Axiale Kraft LWS“ plausibel? Vorzeichen von `Joint.currentForce` (ausgewertet wird der Betrag); Kalkaneusfraktur und LWK im Ergebnis und im Schockraum
- [ ] Fußgänger 40 km/h: HIC nach dem Schädel-Plateau; Schädelfraktur im Ergebnis
- [ ] Szenario mit Motorrad: schmale Delle in der PKW-Seite

## Stand 17.11.2026 (abends) — Gewalt-Sandbox: Zielen und Rückmeldung (offline gebaut, in Unity noch nicht geprüft)

- [x] Zielen auf die sichtbare Haut statt auf die Kapseln; Vorschau (Zielring, Gegenstand, Bahn, Hinweis mit Winkel zur Haut), +/− für das Tempo
- [x] Marke an der Trefferstelle mit Ergebnis (Stichtiefe, Kraft, neue Verletzungen; „nicht eingedrungen“, „verfehlt“), wandert mit dem Körperteil
- [ ] In Unity prüfen: Lage des Zielrings auf der Haut, Größe/Lage des Hinweises bei verschiedenen Fenstergrößen, Bildrate beim Zielen (60 000 Dreiecke), Marken während der Wiedergabe
- [x] Strukturen in der Bahn der Klinge (Anatomiemodell, BodyParts3D): Vorschau „In der Bahn (Klinge 11 cm): 8. Rippe 2,5 cm · Zwerchfell 3,4 cm · Leber 4 cm“, nach dem Stich „durch: …“ bis zur erreichten Tiefe. Test: seitlicher Stich auf Leber-/Milzhöhe trifft Rippe/Zwerchfell, dann das Organ
- [ ] Offen: Stichrichtung frei wählen (z. B. mit gedrückter rechter Maustaste ziehen); Verletzung aus den getroffenen Strukturen ableiten statt aus Region und Tiefe (Schritt 1 des Wundmodells)

## Stand 17.11.2026 (spät) — Organmodell (offline, in Unity noch nicht nachgemessen)

- [x] Bauchkompression aus der im Kontakt berechneten Eindrückung statt Kraft / 27 kN/m: Vianos Pendel 9,4 m/s → Milz 37 % statt 100 %, 6,5 m/s → 12 % statt 46 % (Viano 1989: Organrisse nur vereinzelt bei 9,4 m/s)
- [x] Kraftgrenzwert Bauch 2,5 kN (ES-2) nur beim Seitenaufprall; frontal nur Anzeige
- [ ] In Unity: PKW seitlich 50 km/h und Szenario Kreuzung — Milz/Leber neu ablesen
- [x] HWS-Fraktur (HWK 5, ohne neurologisches Defizit) in der Datenbank, an Nij gekoppelt (Anteil 0,6; Kurve wie Eppinger 1999). In Unity prüfen: Vorzeichen von Halskraft/-moment, Nij beim Fußgänger (zuletzt 8,7)

## Nächste Schritte (Vorschlag, nach Nutzen)

1. In Unity nachmessen (Liste oben) — vieles seit dem 14.11. ist nur offline geprüft.
2. ~~Organmodell: Eindrückung statt statischer Steifigkeit, Grenzwert 2,5 kN nicht frontal~~ — erledigt 17.11. (in Unity nachmessen: Milz/Leber im Seitenaufprall vorher 88–99 %).
3. Weiche Gelenkanschläge als Verletzung auswerten (Luxation, Bandriss) statt nur zu bremsen.
4. Gekoppelter Seitenaufprall zweier Gerüste abgleichen (Rückprall, Reifen quer).
5. Dezelerationsverletzungen (Aortenisthmus, Leberbänder, Nierenstiel).

## Gesamtliste (aus LITERATUR.md übernommen)

- Lagerung in Unity prüfen: Klickflächen nach Umlagerung, Rinnsale neu verlegt, Lache auf der richtigen Fläche; Luftwiderstand der Partikel (limitVelocityOverLifetime mit Geschwindigkeitsfaktor). Außerhalb von Unity geprüft: Drehrichtungen und Auflage auf Rücken-/Beinteil und Keil. Bekannt: Unterkante des Rückenteils taucht am Gelenk etwas ins Sitzteil; bei senkrechtem Arm dehnt sich die Haut an der Achsel (lineares Skinning mit zwei Knochen); Arm ohne Auflage.

- Crash-Zeichen in Unity prüfen: Kontaktorte (Lenkrad, Armaturenbrett, Boden), Gurtkraft aus der Gelenkkraft (currentForce), Übergabe in den Schockraum. Das Ragdoll in der Sandbox selbst zeigt die Zeichen noch nicht (nur als Liste); der Primitiv-Ragdoll ohne Anatomiemodell erzeugt keine.

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
- [ ] Sandbox in Unity testen: Stabilität, Kniebeugung, Größenordnung der Messwerte — *9.11.2026: alle sieben Modi in Unity durchgeklickt, laufen stabil; Größenordnung der Messwerte weiter offen (siehe „Unplausible Messwerte“ unten)*
- [ ] Risikokurven Thorax (Deflexion, Laituri 2005), Femur (AIS2+) und Nij aus 73 FR 40016 (NCAP 2008, Anhang C) übernehmen
- [ ] HIC-Kurve: Zuordnung der Parameter (7,45231/0,73998) zu HIC15 vs. HIC36 im NHTSA-Original prüfen
- [ ] Sandbox gegen NHTSA-Crashtestdaten kalibrieren (Kalibrierlauf in Unity ausführen, weitere Referenzen ergänzen)
- [ ] Thoraxmodell: Parameter von Lobdell 1973 bzw. Kroell 1974 prüfen
- [ ] Gewalt-Sandbox in Unity: Vorzeichen der Kontaktnormale, Faustschlag 3–4 kN, Ziegel auf den Kopf, Messer stoppt an Rippe — *9.11.2026: Faustschlag 9 m/s gegen Becken 2,4 kN; Messerstich 6 m/s in den Thorax 15 cm tief „durch den Knochen“ (Messer stoppt also nicht an der Rippe — prüfen); Ziegel auf den Kopf offen*
- [ ] Körperbau: Brustwand-Streuung bei gleichem BMI, Fettdicke am Unterschenkel, Fett-Modul bei niedrigen Dehnraten belegen
- [ ] Schädel- und Organwiderstand im Stichkanal, Kantenbreite und Schnitttiefe je N mit Messungen belegen; Steifigkeit von Becken und Extremitäten
- [ ] Vorzeichen von Halskraft/-moment und Femurkraft aus Joint.currentForce/currentTorque in Unity prüfen
- [ ] Halsverletzung (HWS) mit Physiologie in die Datenbank aufnehmen, dann Nij zuordnen
- [ ] Fehlende typische Verletzungen: Kalkaneus und LWK 1 (Sturz) am 17.11. ergänzt, Rippenserie rechts vorhanden; HWS (HWK 5) am 17.11. ergänzt; offen: übrige Wirbelsäule, Aortenverletzung (Dezeleration)
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

### Stand 11.11.2026 — Unfall-Sandbox, Fahrzeuge, Darstellung

**In Unity noch nicht geprüft** (eingespielt, automatische Tests grün)

- [ ] Verformbares Fahrzeug bei PKW frontal (Stand 066980f): Front faltet sich und bleibt an der Barriere, kein Flattern; Abschnitt „Fahrzeug“ plausibel; HIC und Brustwerte „verformbar (neu)“ gegen „fester Verlauf (alt)“ vergleichen
- [ ] Gewalt-Sandbox (Stand 6d2e5c9): Aufliegen erzeugt keine Prellmarken mehr (Schwelle 0,5 m/s); Zeitlupe nur 0,5 s nach dem Treffer; Anzeige „Berechnet — Wiedergabe läuft“; Schnitt mit Umschalttaste weiterhin möglich
- [ ] Frontscheibe: Spinnennetz nach korrigierter Anprallgeschwindigkeit (Fußgänger 40 km/h: nach innen gewölbt; PKW frontal ohne Gurt/Airbag: am Kopfanprall); Aussehen der Risslinien und der Wölbung
- [ ] Glaswürfel: Flugbahn und Liegenbleiben ansehen (bisher nur als Objekt in der Hierarchie bestätigt); Zeichen „Glassplitterverletzungen“ im Schockraum
- [ ] Sichtbare Frakturen: Achsenknick am Ragdoll ansehen (im Szenario erschien das Ereignis „Fraktur Femurschaft rechts“, die Darstellung ist nicht begutachtet); Fußgänger 40–50 km/h → Unterschenkel
- [ ] Motorrad: übrige Konfigurationen (45°, 135°, frontal, Ecke, PKW in die Motorradseite, Alleinsturz); Motorrad im Szenario mit Vorlage „Linksabbieger übersieht Motorrad“, Sozius
- [ ] Automatisches Aktualisieren: Warnung in der Konsole, wenn der neue Stand im Play-Modus geholt wird
- [ ] Rettung und Vorsichtung (mSTaRT) im Szenario an zwei, drei Vorlagen klinisch gegenlesen

**Unplausible Messwerte** (in Unity am 9.11.2026 gesehen)

- [ ] HIC viel zu hoch: PKW seitlich 21 634, Fußgänger 6 392, Motorrad 1 500–3 300, Szenario (Kreuzung 30/50 km/h, angegurtet) 3 500–3 900 — vermutlich Kopfanprall auf starre Kollider (Unfallgegner, Dachkante, A-Säule, Tür); Kontaktsteifigkeit und Kopfairbag/Seitenairbag fehlen
- [ ] PKW frontal 50 km/h mit Gurt und Airbag: Brustkorbdeflexion 90 mm (94 % AIS3+) trotz HIC 420 — Gurt-/Thoraxmodell prüfen
- [ ] Fußgänger 40 km/h: axiale Femurkraft 43 kN, Nij 6,2; Seitenaufprall: Nij 6,3 — Größenordnung prüfen (hängt mit dem Vorzeichenpunkt zu Joint.currentForce zusammen)
- [ ] Insassenwerte für das verformbare Fahrzeug neu einordnen: Verzögerung kürzer und spitzer als der Halbsinus (50 km/h: ca. 40 g statt 22 g)

**Verformbares Fahrzeug (Punkte und Balken) — Ausbau**

- [ ] Über ca. 65 km/h zu hart (80 km/h: 135 g Spitze im Schlitten-Umriss): Streben Motor–Spritzwand und Blockieren nachstellen
- [ ] Einzelne Fließkräfte sind Annahmen (nur die Summe ist an der Kennlinie eingestellt); versetzter Anprall nicht an Messdaten geprüft — Crashtest-Verläufe (Verzögerung über der Zeit, Verformungsprofil C1–C6) beschaffen; NHTSA-Seiten waren gesperrt
- [ ] Auto gegen Auto, Seitenaufprall, Fußgänger, Szenario auf das Gerüst umstellen (Kollision Punkt gegen Fläche)
- [ ] Schwerkraft, Räder mit Aufhängung, Kollision der Teile untereinander, abreißende Teile (Stoßfänger, Haube, Türen, Räder), Trümmer
- [ ] Feineres Gerüst und feineres Fahrzeugnetz mit getrennten Teilen (Ziel: Detailgrad wie in Soft-Body-Crashvideos); Rückwirkung des Insassen auf das Fahrzeug
- [ ] Unstimmigkeit der beiden alten Frontmodelle klären: Halbsinus (69 cm bei 50 km/h) gegen Kennlinie der Fahrzeugtypen (50 cm)

**Scheiben, Frakturen, Motorrad — bekannte Grenzen**

- [ ] Scheiben: alle Schwellen sind Annahmen (3 m/s, 3 cm, 25 cm, 75 cm); Würfel kollidieren nicht; Seitenscheibe bricht nicht durch Kopfanprall; gerissene Frontscheibe wird in der Physik nicht weicher
- [ ] Frakturen: Physik bleibt unverändert (Bein schwingt wie intakt); nur die Haut knickt, Knochen und Muskeln nicht; im Schockraum keine Fehlstellung; Armfrakturen fehlen in der Verletzungsdatenbank
- [ ] Motorrad: Nicken auf 75° begrenzt und Umkippen vorgegeben (Annahmen), kein Rollen/Gieren beim Anprall; Gabel-Kennlinie mit Lucernoni 2026 (Volltext) prüfen; im Szenario kein Nicken
- [ ] Hautzeichen am Ragdoll in der Sandbox zeigen (bisher nur als Liste und im Schockraum)

**Verletzungsmodell (geplant, Reihenfolge wie besprochen)**

- [ ] 1 · Einheitliches Wundmodell (Ort, Art, Größe, Tiefe in Schichten, betroffene Gefäße/Nerven/Organe aus dem Anatomiemodell); Haut im Shader öffnen, sodass Muskeln und Knochen sichtbar werden; Blutungsrate in den Schockraum
- [ ] 2 · Verlust von Gliedmaßen: Haut/Muskel/Knochen schneiden, Stumpf, Ragdoll trennen; Tourniquet; Auslöser als belegte oder gekennzeichnete Wahrscheinlichkeit
- [ ] 3 · Verbrennungen: Wärmequellen, Tiefe aus Temperatur und Zeit (Moritz & Henriques 1947 nachschlagen), Fläche aus dem Hautnetz, Darstellung je Grad, Verbrennungsschock, Inhalationstrauma; Fahrzeugbrand mit Rettungszeit koppeln
- [ ] 4 · Zeitverlauf (Rötung → Hämatom) und Behandlung an der Wunde (Verband, Naht, Escharotomie); Einstellung „schematisch / realistisch“

**Darstellung und Bedienung**

- [ ] Unfallorte statt Bodenplatte (Straße, Kreuzung, Leitplanke), Himmel/Licht, Nachbearbeitung; Bremsspuren, Funken, Staub
- [ ] Oberfläche der Sandbox neu aufbauen (einheitliche Knöpfe, keine zur Laufzeit kopierten); Schrift ohne fehlende Zeichen
- [ ] Browser-Prototyp: neue Unfallfunktionen (Scheiben, Frakturen, verformbares Fahrzeug) fehlen dort

**Technik**

- [ ] `.meta`-Dateien der neuen Skripte liegen nicht im Repository (Unity erzeugt sie lokal): vom PC aus einchecken
- [ ] CI prüft nur die reinen Rechenteile; der Unity-Code wird nur lokal gegen Platzhalter kompiliert — Unity-Kompilierung in die CI aufnehmen (Lizenz nötig) oder Platzhalter ins Repository
- [x] Offene Punkte in eigene Datei ausgelagert (diese Datei) — 17.11.

**Erledigt seit dem 8.11.2026**

- [x] Fahrzeugmodelle aus Code in Unity: Flächen geschlossen, Scheiben durchsichtig (9.11.2026)
- [x] Frontal-Knautschweg wuchs nach dem Aufprall weiter (Keil-Auto, HIC 4253 → 420) (9.11.2026)
- [x] Alle Modi der Unfall-Sandbox bedienbar: Mechanismus aus dem Hauptmenü, Klicks während der Wiedergabe, scrollbares Ergebnis (9.11.2026)
- [x] Messerstich erzeugt wieder einen Stichkanal (9.11.2026)
- [x] Frontscheibe: Anprallgeschwindigkeit 25-fach zu hoch, im Szenario nur eigene Insassen (9.11.2026)
- [x] Prototyp verformbares Fahrzeug an der Front-Kennlinie eingestellt: 30/56/80 km/h → 23/61/76 cm (11.11.2026)

### Stand 14.11.2026 — Durchlauf in Unity (Stand 3bfda6e)

**Geprüft, läuft**

- [x] Alle sieben Modi starten, rechnen und spielen ab; Übergabe in den Schockraum mit sichtbaren Wunden
- [x] Ergebnisliste scrollt mit dem Mausrad (vorher abgeschnitten); Kamera zoomt nicht mehr über Panels
- [x] Seitenaufprall: Seitenscheiben links zerfallen (28/34 ms)
- [x] Motorrad: „PKW in die Motorradseite“ und „Alleinsturz“; Szenario „Linksabbieger übersieht Motorrad“ (Fahrer T2, frei zugänglich, Seitenscheibe rechts zerfällt, Fraktur-Ereignis)
- [x] Gewalt: Messerstich 15 cm Stichkanal, Hämatopneumothorax; keine Prellmarken mehr durch Aufliegen; Anzeige „Berechnet — Wiedergabe läuft“
- [x] Sichtbare Frakturen: Haut bleibt beim Abspielen intakt (keine Darstellungsfehler)

**Neu gefunden — Stand nach der Fehlersuche am 14.11. (Stand in Unity nachgemessen, soweit nicht anders vermerkt)**

Ursachen der überhöhten Messwerte (alle vier lagen im Menschmodell bzw. Kontakt, nicht im Fahrzeugverlauf):

1. **Gelenkanschläge starr**: Am Ende des Bewegungsumfangs stoppte ein Segment in einem Rechenschritt (Seitenaufprall: Halskraft 147 kN, Kopf 2 700 g, HIC 11 500 ohne jeden Kopfkontakt). Jetzt weicher Anschlag (Eigenfrequenz 100 rad/s, Dämpfungsgrad 0,5 — Annahme, `RagdollBuilder.StopFrequency_rad_s`).
2. **Körperteil auf Körperteil starr**: Arm zwischen Tür und Brustkorb, Bein an Bein ergaben 70–240 kN. Jetzt geben beide Gewebe nach (je halbe Eindrückung).
3. **Fahrzeugteile nicht am nachgiebigen Kontakt angemeldet** (Tür, Dach, Armaturenbrett, Lenkrad, Sitze, Karosserie des Unfallgegners, Airbag): `vehicle_model.json › interior`, Steifigkeiten sind Annahmen, an FMVSS 201 ausgerichtet (freier Prüfkopf 24 km/h: HIC 731 an der Verkleidung, 541 am Lenkrad; starr 10 702 — Test).
4. **Gurt und Airbag**: Der ganze Schultergurt hing am 0,45 kg leichten Brustbein (Deflexion immer 90 mm) → 20 % am Brustbein, 80 % am Brustkorb (Annahme). Der Gurt hält nicht mehr seitlich. Der Airbag zählt nicht als örtliche Baucheindrückung und trifft das Brustbein nicht allein.

Messwerte danach (50 km/h, Gurt, Airbag, entspannt):

| Fall | vorher | nachher |
|---|---|---|
| PKW frontal, fester Verlauf | HIC 383, Deflexion 90 mm | HIC 228, Deflexion 44 mm, Brust 42 g, Femur 4,1 kN |
| PKW frontal, verformbar | HIC 22 700–30 500, Bauch 80 % | HIC 236, Deflexion 36 mm, Brust 75 g, Bauch 0 % |
| PKW seitlich | HIC 21 561, Nij 6,3 | HIC 276, Nij 0,5, Becken 6,1 kN, Milz 88 % |
| Szenario Kreuzung, getroffener Fahrer | HIC 3 500–9 987 | kein Kopfkriterium über der Schwelle, Becken 6,2 kN |

- [x] Verformbares Fahrzeug: Messwerte plausibel → wieder Standard
- [x] Nach geänderter Einstellung (auch Motorrad-Konfiguration) wird das alte Ergebnis gelöscht
- [x] Haut bleibt nach dem Crash und beim Wiederholen sichtbar (Brüche); Risikofarben mit Taste H
- [x] Kamera steht seitlich zur Fahrtrichtung, wenn ein Fahrzeug auf die Person zufährt (Fußgänger nachgesehen; „PKW in die Motorradseite“ nur gerechnet)
- [x] Im Ergebnis steht der härteste Kopfkontakt (Teil, Kraft, nachgiebig/starr); ins Protokoll (Editor.log) gehen je Segment Spitzenbeschleunigung, Kontaktpartner und die Schritte um die Kopfspitze
- [ ] Nur geändert, nicht nachgemessen: „PKW in die Motorradseite“ (Bein nicht mehr zwischen zwei geführten Körpern), Messerstich ohne zusätzlichen Schnitt, Alleinsturz
- [ ] Fußgänger 40 km/h: HIC 3 800–9 100 kommt jetzt vom Aufschlag des Kopfes auf den Boden (39–72 kN). Die Kontaktkraft steigt über die Bruchlast des Schädels hinaus (Plateau fehlt), und die Aufschlaggeschwindigkeit ist nicht geprüft. Axiale Femurkraft 20–44 kN. Spinnennetz: der Kopf traf in diesen Läufen die Scheibe nicht. — *17.11.: Bruchplateau des Schädels eingebaut (Kraft höchstens Bruchkraft nach Allsop); in Unity noch nicht nachgemessen.*
- [ ] Seitenaufprall: Endlage ansehen — der Insasse sackt stark zur Beifahrerseite (kein seitlicher Gurthalt mehr, weiche Anschläge)
- [ ] PKW seitlich und Szenario: Milzruptur 88–99 %, Leber 100 % bei Δv 35 km/h — Organmodell rechnet jede Kontaktkraft am Bauch als örtliche Eindrückung; Grenzwert „Kontaktkraft Abdomen 2,5 kN“ gilt für den Seitenaufprall, wird aber auch frontal angewendet
- [ ] Verformbares Fahrzeug: Brustbeschleunigung 75 g (fester Verlauf 42 g) — Verlauf der Zellenverzögerung gegen Messkurve prüfen
- [ ] Weiche Gelenkanschläge: Überschreiten des Bewegungsumfangs als Verletzung auswerten (Bandriss, Luxation) statt nur zu bremsen; Steifigkeit mit Literatur belegen
- [ ] Schockraum bei kleinem Fenster: „Menü“, „Ansicht & Ton“ und Hinweistext überlappen die Maßnahmenliste; Warnung „crash_13_s: kein sichtbarer Hautort“
- [ ] Wiedergabe läuft schneller als die eingestellte Zeitlupe (×0,05 zeigte ca. 100 ms in 1,3 s)

**Stand 15.11.2026 — Gerüst und nachgiebiger Kontakt in allen Modi (in Unity nachgesehen, Stand 9148532)**

| Modus | Bewegung/Kraft | Form der Verformung |
|---|---|---|
| PKW frontal | Gerüst rechnet den Stoß (SoftCrashRun) | Gerüst |
| PKW seitlich | Federmodell (Reichert 2018) | Gerüst beider Fahrzeuge, Tiefe aus dem Federmodell (SoftDent) |
| Motorrad | Motorrad-/Stoßmodell (ISO 13232) | Gerüst des PKW, schmale Delle (Vorderrad 0,4 m) |
| Fußgänger | nachgiebige Front (pedestrianFront) | Stoßfänger und Haube am Gerüst, Tiefe = Kontaktkraft / Steifigkeit des Teils |
| Szenario | Draufsicht-Modell (TrafficSim) | Gerüst je Fahrzeug, Tiefe je Seite aus TrafficSim |

- [x] Druckflächen am Gerüst (SoftPlate, haftend, höchstens 15 m/s), festgehaltene Punkte, Dämpfung; Tests: Delle 25 cm wird erreicht, Gegenseite bleibt, Sprung der Tiefe wird nachgeholt, Kopfdelle in der Haube 6 cm
- [x] Seitenstruktur der Zelle quer weicher (`SideYield_N`, nur Querbalken der Spanten) — Frontal-Kennlinie unverändert (30/56/80 km/h: 23/60/80 cm)
- [x] Boden und Motorradteile am nachgiebigen Kontakt (waren starr). Fußgänger: Kopf auf Boden bleibt HIC ≈ 3 900 — das ist das Gewebemodell (Kopfschwarte 4,2 MN/m, Allsop 1991) bei 6–8 m/s auf Beton; ohne Bruchplateau des Schädels (offen, siehe oben)
- [x] Knopf „Fahrzeug: verformbar (Gerüst)“ in allen Modi mit Fahrzeug
- [ ] Gekoppelter Seitenaufprall zweier Gerüste (SoftSideRun) noch nicht abgeglichen: ohne Räder/Schwerkraft kippte das getroffene Fahrzeug (Drehung wird jetzt herausgenommen), danach zu elastisch (Δv 20 statt ≤ 15 km/h bei 30 km/h) und Türeindringen kaum geschwindigkeitsabhängig — Rückprall dämpfen, Reifen quer, Abgleich an FMVSS 214/IIHS-Seitentest
- [ ] Gerüst ist grob (Haube: drei Punkte quer, Stationen alle 20 cm): Kopfdellen werden breit verschmiert; feinere Haube/Türen als eigene Teilnetze
- [x] Szenario: Breite der Delle je Gegner (Überdeckung aus TrafficSim) — 17.11.
- [ ] Motorrad selbst: Gabel weiter aus der berechneten Form, nicht als Gerüst

