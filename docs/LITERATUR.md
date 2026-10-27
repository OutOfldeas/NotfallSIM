# Literaturnotizen

Stand: 27.10.2026 · Datenbank-Version 0.3.0

Hier steht, worauf die Zahlen im Modell beruhen und wie belastbar sie sind.
Jede Änderung an `injuries.json` oder an den Modelltabellen sollte hier nachvollziehbar sein.

## Parameter im Überblick

| Parameter | Wert im Modell | Belastbarkeit | Grundlage |
|---|---|---|---|
| Blutvolumen | 70 mL/kg (♂), 65 mL/kg (♀) | gut | Lehrbuchwert |
| Vitalwerte je Schockklasse | ATLS-Tabelle, interpoliert | Lehrbuch; real oft anders | ATLS 10th ed.; Kritik s. u. |
| Femurschaft, Blutverlust | max. 1000 mL, 20 mL/min | mäßig | Lieurance 1992 (Sekundärquelle) |
| Becken instabil, Blutverlust | max. 3000 mL, 150 mL/min | schwach (geschätzt) | Plausibilisiert über Transfusionsbedarf (Devaney 2023) |
| Beckengurt-Effekt | Blutungsrate × 0,5 | schwach | Widersprüchliche Beobachtungsstudien |
| Kristalloid, intravasal | 30 % des Volumens | grob | Vereinfachung, in Pulse realistischer |
| Spannungspneu, Verlauf (spontan atmend) | früh Hypoxie, Hypotonie ab 60 % Spannung | qualitativ gut, zeitlich geschätzt | Roberts 2015 |
| Nadeldekompression, Erfolg | 57 % | mäßig (sehr heterogene Daten) | Ahmad 2025 (Versagerrate ca. 43 %) |

## Wichtigste Erkenntnisse

**ATLS-Schockklassen bilden die Realität nur begrenzt ab.**
Auswertungen des TraumaRegister DGU zeigen, dass echte Traumapatienten bei zunehmendem Blutverlust kaum tachykard werden.
In der Base-Deficit-Klassifikation von Mutschler et al. (16.305 Patienten) stieg die mittlere Herzfrequenz bei Klinikaufnahme nur von etwa 86 auf 97/min, der systolische Druck fiel von etwa 133 auf 95 mmHg.
Relevante Hypotonie trat nur in der schwersten Klasse auf. Die Massivtransfusionsrate stieg von 5 % auf 52 %, die Letalität von 7 % auf 52 %.
Eine weitere Auswertung zur analogen präklinischen PHTLS-Einteilung (46.689 Patienten) ordnete nur rund ein Viertel der Patienten anhand von HF, RR und GCS gemeinsam korrekt einer Klasse zu.

Konsequenz für NotfallSIM: Der Mock zeigt bewusst das Lehrbuchbild, damit die Schockklassen erlernbar sind.
Für mehr Realismus wäre ein zweiter Modus sinnvoll, der sich an den Registerdaten orientiert, plus der Base Excess als zusätzlicher Laborwert.

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

## Bewertungskriterien im Debriefing (Entwurf)

Die Zeitziele sind didaktische Setzungen und stehen im Feld `assessment` der Szenario-Dateien. Gezählt wird ab Szenariostart.

| Szenario | Ziel | Zeit | Kritisch |
|---|---|---|---|
| Beckenfraktur | Sauerstoff gegeben | 2 min | nein |
| Beckenfraktur | Becken untersucht | 3 min | nein |
| Beckenfraktur | Oberschenkel untersucht | 5 min | nein |
| Beckenfraktur | Beckengurt angelegt | 5 min | ja |
| Beckenfraktur | Blutprodukte begonnen | 10 min | ja |
| Beckenfraktur | Kristalloide max. 1000 mL | — | nein |
| Spannungspneu | Sauerstoff gegeben | 2 min | nein |
| Spannungspneu | Thorax untersucht | 4 min | nein |
| Spannungspneu | Thorax entlastet (Nadel oder Drainage) | 8 min | ja |
| Spannungspneu | Thoraxdrainage angelegt | 15 min | ja |

## Offene Punkte zum Prüfen

- [ ] Empfehlungen und Empfehlungsgrade zu Beckengurt, Volumentherapie und permissiver Hypotension in der S3-Leitlinie (MAGICapp) nachlesen
- [ ] Lieurance 1992 im Volltext prüfen (Uni-Zugang)
- [ ] ATLS-Tabelle der Schockklassen gegen die 10. Auflage abgleichen
- [x] Entscheiden: Lehrbuch- oder Registerphysiologie? → MVP im Lehrbuchmodus, Realitätsmodus als zweite Schwierigkeitsstufe in Phase 2 (27.10.2026)
- [x] Literatur zum Spannungspneumothorax (Verlauf bei Spontanatmung vs. Beatmung)
- [ ] Zeitverlauf des Spannungspneus im Mock (ca. 20 Minuten bis Stillstand) klinisch plausibilisieren
- [ ] Empfehlung der S3-Leitlinie zu Ort und Technik der Entlastung nachlesen
- [ ] Zeitziele und „kritisch“-Markierungen der Bewertung prüfen (Tabelle oben)

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

Angaben ohne Link (4, 8, 9) sind aus Referenzlisten übernommen und vor Zitation in einer Arbeit im Original zu prüfen.
