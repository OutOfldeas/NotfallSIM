# Literaturnotizen

Stand: 27.10.2026 · Datenbank-Version 0.4.0 · Maßnahmen v0.3.0

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

Änderungen in v0.4: Beim Thoraxtrauma zählen die Ziele jetzt ab Beginn des Spannungspneus statt ab Szenariostart. Kristalloid ist jetzt eine balancierte Vollelektrolytlösung (S3, GoR A: keine isotone NaCl-Lösung). Tranexamsäure ist als Maßnahme und Ziel dazugekommen.

**Bessere Datenquellen für echte Zeitziele:** die aktuellen Jahresberichte des TraumaRegister DGU (Schockraumintervalle), die Langfassung der S3-Leitlinie auf MAGICapp und ein Expertenkonsens erfahrener Schockraumleiter (z. B. Delphi-Befragung).

## Offene Punkte zum Prüfen

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

Angaben ohne Link (4, 8, 9) sind aus Referenzlisten übernommen und vor Zitation in einer Arbeit im Original zu prüfen.
