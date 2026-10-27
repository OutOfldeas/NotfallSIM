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

// Zufallsszenarien (Datei als drittes Argument)
if (process.argv[3]) {
  const { generateScenario } = require("../../web/engine.js");
  const root = process.argv[4];
  const data = { injuries: JSON.parse(fs.readFileSync(root + "/injuries.json", "utf8")), mechanisms: JSON.parse(fs.readFileSync(root + "/mechanisms.json", "utf8")) };
  const lines = [];
  for (const m of data.mechanisms.mechanisms)
    for (let seed = 1; seed <= 60; seed++) {
      const s = generateScenario(data, m.id, (seed * 7919) >>> 0), p = s.patient;
      lines.push([s.id, s.description, p.sex, p.age, N(p.weight_kg), N(p.height_cm), p.betaBlocker ? "1" : "0", N(p.initialBloodLoss_mL),
        s.injuries.map((i) => `${i.injuryId}:${N(i.severity)}:${N(i.onsetTime_s)}`).join(","), s.assessment.map((c) => c.id).join(",")].join("|"));
    }
  const gref = fs.readFileSync(process.argv[3], "utf8").trim().split("\n");
  const diff = gref.findIndex((l, i) => l !== lines[i]);
  if (gref.length !== lines.length || diff >= 0) {
    console.log(`Zufallsszenarien weichen ab (Zeile ${diff + 1}):\n  C#: ${gref[diff]}\n  JS: ${lines[diff]}`);
    process.exit(1);
  }
  console.log(`${lines.length} Zufallsszenarien identisch`);
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
