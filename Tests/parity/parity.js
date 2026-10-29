// Gleiche Verläufe wie Parity.cs mit web/engine.js; vergleicht mit der C#-Ausgabe (Datei als Argument).
const fs = require("fs");
const { MockEngine } = require("../../web/engine.js");
const N = (x) => Number(x.toFixed(3));
const out = [];
function run(name, p, plan, seconds) {
  const e = new MockEngine(1, "Textbook");
  e.initialize(p);
  for (let step = 0; step <= seconds * 10; step++) {
    plan(e, step);
    if (step % 300 === 0) {
      const v = e.current;
      out.push([name, step / 10, N(v.hr), N(v.sbp), N(v.dbp), N(v.rr), N(v.spo2), N(v.etco2), N(v.bv), N(v.gcs), N(v.be), v.arrest ? 1 : 0,
        N(v.hb), N(v.ph), N(v.paco2), N(v.pao2), N(v.potassium), N(v.calcium), N(v.glucose)]);
    }
    e.advance(0.1);
  }
}
const bleed = (c, s, r, m) => ({ effect: "Hemorrhage", compartment: c, severity: s, baseRate_mL_min: r, maxTotalLoss_mL: m });
const iv = (type, o = {}) => ({ type, target: null, compound: null, volume_mL: 0, rate_mL_min: 0, rateMultiplier: 1, successProbability: 1, ...o });
const man80 = { weight_kg: 80, age: 35, sex: "male" };
run("becken_unbehandelt", man80, (e, s) => { if (s === 0) { e.applyInjury(bleed("Pelvis", 0.8, 150, 3000)); e.applyInjury(bleed("RightThigh", 0.6, 20, 1000)); } }, 1800);
run("becken_behandelt", man80, (e, s) => {
  if (s === 0) { e.applyInjury(bleed("Pelvis", 0.8, 150, 3000)); e.applyInjury(bleed("RightThigh", 0.6, 20, 1000)); }
  if (s === 1800) e.applyIntervention(iv("HemorrhageControl", { target: "Pelvis", rateMultiplier: 0.5 }));
  if (s === 3000) e.applyIntervention(iv("FluidInfusion", { compound: "Blood", volume_mL: 600, rate_mL_min: 50 }));
  if (s === 4000) e.applyIntervention(iv("FluidInfusion", { compound: "BalancedCrystalloid", volume_mL: 500, rate_mL_min: 100 }));
  if (s === 1200) e.applyIntervention(iv("Oxygen"));
}, 1800);
const tp = { effect: "TensionPneumothorax", compartment: "LeftLung", severity: 0.8, progressionRate_per_min: 0.06 };
run("tp_unbehandelt", { weight_kg: 85, sex: "male" }, (e, s) => { if (s === 900) e.applyInjury({ ...tp }); }, 1800);
run("tp_drainage", { weight_kg: 85, sex: "male" }, (e, s) => { if (s === 900) e.applyInjury({ ...tp }); if (s === 6000) e.applyIntervention(iv("ChestDrain", { target: "LeftLung" })); }, 1500);
run("tp_beatmet", { weight_kg: 85, sex: "male" }, (e, s) => { if (s === 0) { e.applyInjury({ ...tp }); e.applyIntervention(iv("Intubation")); } }, 900);
const tbi = { effect: "TraumaticBrainInjury", compartment: "Brain", severity: 0.9, progressionRate_per_min: 0.05 };
run("sht_unbehandelt", { weight_kg: 78, age: 24, sex: "male" }, (e, s) => { if (s === 0) e.applyInjury({ ...tbi }); }, 1500);
run("sht_behandelt", { weight_kg: 78, age: 24, sex: "male" }, (e, s) => {
  if (s === 0) { e.applyInjury({ ...tbi }); e.applyIntervention(iv("Oxygen")); }
  if (s === 8000) e.applyIntervention(iv("Osmotherapy"));
  if (s === 9000) e.applyIntervention(iv("Intubation"));
  if (s === 9600) e.applyIntervention(iv("NeurosurgicalDecompression"));
}, 1800);
const sdh = { effect: "TraumaticBrainInjury", compartment: "Brain", severity: 1.0, progressionRate_per_min: 0.018, initialLevel: 0.45 };
run("polytrauma_noradrenalin", { weight_kg: 85, age: 42, sex: "male", initialBloodLoss_mL: 1500 }, (e, s) => {
  if (s === 0) { e.applyInjury({ ...sdh }); e.applyInjury(bleed("Pelvis", 0.7, 150, 3000)); e.applyInjury(bleed("RightThigh", 0.6, 20, 1000)); }
  if (s === 6000) e.applyIntervention(iv("Intubation"));
  if (s === 7000) e.applyIntervention(iv("Vasopressor"));
}, 1800);
run("laparotomie_mehrfach", man80, (e, s) => {
  if (s === 0) { e.applyInjury(bleed("Spleen", 0.8, 100, 2500)); e.applyInjury(bleed("Liver", 0.7, 90, 2500)); e.applyInjury(bleed("Extremity", 0.8, 120, 0)); }
  if (s === 300) e.applyIntervention(iv("HemorrhageControl", { target: "Extremity", rateMultiplier: 0 }));
  if (s === 3000) e.applyIntervention(iv("HemorrhageControl", { target: "Spleen,Liver", rateMultiplier: 0.05 }));
}, 1200);
run("betablocker", { weight_kg: 65, age: 76, sex: "female", betaBlocker: true }, (e, s) => { if (s === 0) e.applyInjury(bleed("Pelvis", 0.6, 150, 3000)); }, 1800);
run("gerinnsel_nachblutung", man80, (e, s) => {
  if (s === 0) {
    e.applyInjury({ ...bleed("Spleen", 0.9, 100, 2500), lesion: "parenchymal" });
    e.applyInjury({ ...bleed("Abdomen", 0.6, 110, 3000), lesion: "partial" });
    e.applyInjury({ ...bleed("Extremity", 0.8, 100, 0), lesion: "transection" });
    e.applyInjury({ ...bleed("Pelvis", 0.7, 150, 3000), lesion: "venous" });
  }
  if (s === 6000) e.applyIntervention(iv("HemorrhageControl", { target: "Abdomen", rateMultiplier: 0 }));
  if (s === 9000) e.applyIntervention(iv("FluidInfusion", { target: "", compound: "Blood", volume_mL: 1500, rate_mL_min: 150 }));
}, 1800);
run("lagerung", { weight_kg: 80, age: 35, sex: "male", height_cm: 175, initialBloodLoss_mL: 800 }, (e, s) => {
  if (s === 0) {
    e.applyInjury({ ...bleed("Extremity", 0.8, 120, 0), lesion: "partial" });
    e.applyInjury({ ...bleed("RightThigh", 0.7, 20, 1000), lesion: "venous" });
    e.applyInjury({ ...bleed("LeftArm", 0.8, 40, 0), lesion: "partial" });
    e.applyInjury({ ...bleed("Liver", 0.6, 90, 2500), lesion: "parenchymal" });
  }
  if (s === 1500) e.applyIntervention(iv("Positioning", { target: "beine_hoch" }));
  if (s === 4000) e.applyIntervention(iv("Positioning", { target: "oberkoerper_30" }));
  if (s === 6000) e.applyIntervention(iv("Positioning", { target: "arm_links_hoch" }));
  if (s === 8000) e.applyIntervention(iv("Positioning", { target: "flach" }));
}, 1200);

// Zufallsszenarien (Datei als drittes Argument)
if (process.argv[3]) {
  const { generateScenario, evaluateDiagnoses } = require("../../web/engine.js");
  const root = process.argv[4];
  const data = { injuries: JSON.parse(fs.readFileSync(root + "/injuries.json", "utf8")), mechanisms: JSON.parse(fs.readFileSync(root + "/mechanisms.json", "utf8")) };
  const mods = JSON.parse(fs.readFileSync(root + "/imaging.json", "utf8")).modalities;
  const diffs = JSON.parse(fs.readFileSync(root + "/differentials.json", "utf8"));
  const byId = Object.fromEntries(data.injuries.injuries.map((d) => [d.id, d]));
  const dxLine = (s) => {
    const first = byId[s.injuries[0].injuryId];
    const events = [{ time: 5, tag: "exam:" + first.region }, { time: 10, tag: "diagnosis:" + first.id }, { time: 20, tag: "diagnosis:leberlazeration" },
      { time: 25, tag: "diagnosis:milzruptur" }, { time: 26, tag: "diagnosis:aortenverletzung" }, { time: 30, tag: "result:Imaging:efast" }, { time: 35, tag: "diagnosis-retracted:milzruptur" }];
    return evaluateDiagnoses(s.injuries, events, (id) => byId[id], mods, diffs)
      .map((r) => `${r.injuryId}:${r.outcome}:${r.time != null ? N(r.time) : "-"}:${r.examined ? 1 : 0}${r.imagingShown ? 1 : 0}${r.clinicalOnly ? 1 : 0}${r.isDifferential ? 1 : 0}:${r.against.join(" ")}`).join(",");
  };
  const lines = [];
  for (const m of data.mechanisms.mechanisms)
    for (let seed = 1; seed <= 60; seed++) {
      const s = generateScenario(data, m.id, (seed * 7919) >>> 0), p = s.patient;
      lines.push([s.id, s.description, p.sex, p.age, N(p.weight_kg), N(p.height_cm), p.betaBlocker ? "1" : "0", N(p.initialBloodLoss_mL),
        s.injuries.map((i) => `${i.injuryId}:${N(i.severity)}:${N(i.onsetTime_s)}`).join(","), s.assessment.map((c) => c.id).join(","), dxLine(s)].join("|"));
    }
  // Befundverlauf wie ExamCourse in Parity.cs
  {
    const { examFindings } = require("../../web/engine.js");
    const normal = JSON.parse(fs.readFileSync(root + "/findings.json", "utf8"));
    const sc = { injuries: [{ injuryId: "spannungspneu_links", severity: 0.8, onsetTime_s: 60 }, { injuryId: "milzruptur", severity: 0.9, onsetTime_s: 0 }, { injuryId: "becken_instabil", severity: 0.7, onsetTime_s: 0 }] };
    const e = new MockEngine(3, "Textbook");
    e.initialize({ weight_kg: 80, age: 40, sex: "male" });
    const log = [];
    const plan = { 3000: ["beckengurt"], 6000: ["nadel_li"], 9000: ["laparotomie", "drainage_li"] };
    const ivs = {
      beckengurt: iv("HemorrhageControl", { id: "beckengurt", target: "Pelvis", rateMultiplier: 0.5 }),
      nadel_li: iv("NeedleDecompression", { id: "nadel_li", target: "LeftLung", successProbability: 1 }),
      laparotomie: iv("HemorrhageControl", { id: "laparotomie", target: "Spleen,Liver", rateMultiplier: 0.05 }),
      drainage_li: iv("ChestDrain", { id: "drainage_li", target: "LeftLung" }) };
    let tpOn = false;
    for (let step = 0; step <= 21000; step++) {
      const t = step / 10;
      for (const si of sc.injuries) {
        const due = si.injuryId === "spannungspneu_links" ? (!tpOn && t >= si.onsetTime_s) : step === 0;
        if (!due) continue;
        if (si.injuryId === "spannungspneu_links") tpOn = true;
        for (const x of byId[si.injuryId].effects) e.applyInjury({ injuryId: si.injuryId, effect: x.effectType, compartment: x.compartment, severity: si.severity,
          baseRate_mL_min: x.baseRate_mL_min, maxTotalLoss_mL: x.maxTotalLoss_mL, progressionRate_per_min: x.progressionRate_per_min, initialLevel: x.initialLevel || 0 });
      }
      for (const id of plan[step] || []) {
        const v = ivs[id];
        log.push({ time: t, tag: `intervention:${v.type}:${v.target}:${v.id}` });
        if (id !== "laparotomie") e.applyIntervention(v);
      }
      if (step % 600 === 0) {
        const ctx = { time: t, bodyLoss: e.current.loss, level: (id) => e.injuryLevel(id), lost: (id) => e.injuryBloodLoss(id), events: log };
        for (const region of ["Thorax", "Abdomen", "Becken"]) lines.push(`exam|${N(t)}|${region}|` + examFindings(region, sc, (id) => byId[id], normal, ctx).join(" / "));
      }
      e.advance(0.1);
    }
  }
  const gref = fs.readFileSync(process.argv[3], "utf8").trim().split("\n");
  const diff = gref.findIndex((l, i) => l !== lines[i]);
  if (gref.length !== lines.length || diff >= 0) {
    console.log(`Zufallsszenarien weichen ab (Zeile ${diff + 1}):\n  C#: ${gref[diff]}\n  JS: ${lines[diff]}`);
    process.exit(1);
  }
  console.log(`${lines.filter((l) => !l.startsWith("exam|")).length} Zufallsszenarien und ${lines.filter((l) => l.startsWith("exam|")).length} Befunde im Verlauf identisch`);
}

const ref = fs.readFileSync(process.argv[2], "utf8").trim().split("\n").map((l) => l.split(";"));
let maxDiff = 0, worst = "", fails = 0;
if (ref.length !== out.length) { console.log(`Zeilenzahl verschieden: C# ${ref.length}, JS ${out.length}`); process.exit(1); }
const cols = ["HF", "RRsys", "RRdia", "AF", "SpO2", "etCO2", "BV", "GCS", "BE", "Stillstand", "Hb", "pH", "pCO2", "pO2", "K", "Ca", "Glukose"];
for (let i = 0; i < ref.length; i++) {
  for (let c = 2; c < 2 + cols.length; c++) {
    const d = Math.abs(Number(ref[i][c]) - Number(out[i][c]));
    if (d > maxDiff) { maxDiff = d; worst = `${ref[i][0]} t=${ref[i][1]}s ${cols[c - 2]}: C# ${ref[i][c]} vs JS ${out[i][c]}`; }
    if (d > 0.01) fails++;
  }
}
console.log(`${ref.length} Zeitpunkte × ${cols.length} Werte verglichen, max. Abweichung ${maxDiff.toFixed(4)}${worst ? " (" + worst + ")" : ""}`);
process.exit(fails ? 1 : 0);
