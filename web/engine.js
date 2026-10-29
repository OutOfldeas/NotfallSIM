// NotfallSIM – Browser-Prototyp: JavaScript-Port der Mock-Physiologie und Simulationslogik.
// Quelle der Wahrheit bleibt der C#-Code in Assets/NotfallSIM/Scripts. Dieser Port wird mit
// web/compare_with_csharp.sh gegen die C#-Version abgeglichen. Keine Abhängigkeiten, läuft in Node und im Browser.
(function (root) {
  "use strict";

  // ---------------------------------------------------------------- ShockTables (Core/ShockTables.cs)
  const LossPoints = [0.0, 0.15, 0.30, 0.40, 0.50, 0.58];
  const T = {
    Textbook: { hr: [75, 98, 118, 138, 150, 110], sbp: [120, 120, 112, 88, 70, 50], dbp: [78, 84, 82, 62, 45, 30], rr: [14, 18, 26, 34, 40, 40] },
    Registry: { hr: [84, 88, 92, 96, 97, 90], sbp: [135, 129, 119, 105, 92, 70], dbp: [80, 78, 74, 66, 56, 42], rr: [15, 17, 20, 24, 28, 30] },
  };
  const EtCo2 = [38, 37, 33, 27, 20, 14];
  const SpO2Air = [97, 97, 96, 95, 92, 88];
  const BaseExcess = [0, -2, -6, -10, -14, -18];
  const Lactate = [1.0, 1.8, 3.2, 4.8, 6.8, 9.0];

  function interp(x, points, table) {
    if (x <= points[0]) return table[0];
    for (let i = 1; i < points.length; i++) {
      if (x <= points[i]) {
        const t = (x - points[i - 1]) / (points[i] - points[i - 1]);
        return table[i - 1] + t * (table[i] - table[i - 1]);
      }
    }
    return table[table.length - 1];
  }
  const loss = (x, table) => interp(x, LossPoints, table);
  const Shock = {
    hr: (m, f) => loss(f, T[m].hr), sbp: (m, f) => loss(f, T[m].sbp),
    dbp: (m, f) => loss(f, T[m].dbp), rr: (m, f) => loss(f, T[m].rr),
    be: (f) => loss(f, BaseExcess), lactate: (f) => loss(f, Lactate),
  };

  // C# Math.Round rundet halbe Werte zur geraden Zahl; für identische GCS-Werte nachgebildet.
  function roundHalfEven(x) {
    const r = Math.round(x);
    return Math.abs(x % 1) === 0.5 && r % 2 !== 0 ? r - 1 : r;
  }

  // Einfacher, reproduzierbarer Zufallsgenerator (mulberry32). Folgen weichen von C# System.Random ab.
  function rng(seed) {
    let a = seed >>> 0;
    return function () {
      a = (a + 0x6D2B79F5) >>> 0;
      let t = a;
      t = Math.imul(t ^ (t >>> 15), t | 1);
      t ^= t + Math.imul(t ^ (t >>> 7), t | 61);
      return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
    };
  }

  // ---------------------------------------------------------------- BloodGas (Core/BloodGas.cs)
  const SatPoints = [50, 60, 70, 80, 85, 90, 94, 96, 97, 98];
  const Po2Values = [27, 32, 37, 45, 50, 60, 75, 85, 95, 105];
  const BloodGas = {
    hco3: (be) => Math.max(4, 24 + be),
    paco2: (hco3, intubated, airway, tension) => intubated ? 38 : Math.min(40, 1.5 * hco3 + 8) + 15 * airway + 6 * tension,
    ph: (hco3, paco2) => 6.1 + Math.log10(hco3 / (0.03 * paco2)),
    pao2: (spo2, oxygen, intubated) => spo2 >= 96 && intubated ? 250 : spo2 >= 96 && oxygen ? 140 : interp(spo2, SatPoints, Po2Values),
    potassium: (ph) => 4.0 + Math.max(0, 7.40 - ph) * 6,
    calcium: (transfused) => Math.max(0.75, 1.20 - 0.0002 * transfused),
    glucose: (f) => 110 + 150 * f,
  };
  const dec = (x, n) => x.toFixed(n).replace(".", ",");
  /** Strukturierte BGA-Werte fürs UI und der gleiche Text wie BloodGas.Report in C#. */
  function bloodGasValues(v) {
    // [Bezeichnung, Anzeigewert, Einheit, außerhalb des Referenzbereichs]
    const out = (x, lo, hi) => x < lo || x > hi;
    return [
      ["pH", dec(v.ph, 2), "", out(v.ph, 7.35, 7.45)], ["pCO₂", dec(v.paco2, 0), "mmHg", out(v.paco2, 35, 45)],
      ["pO₂", dec(v.pao2, 0), "mmHg", out(v.pao2, 80, 500)], ["SaO₂", dec(v.spo2, 0), "%", out(v.spo2, 94, 100)],
      ["HCO₃⁻", dec(v.hco3, 1), "mmol/L", out(v.hco3, 22, 26)], ["BE", (v.be >= 0 ? "+" : "") + dec(v.be, 1), "mmol/L", out(v.be, -2, 2)],
      ["Laktat", dec(v.lactate, 1), "mmol/L", out(v.lactate, 0, 2)], ["Hb", dec(v.hb, 1), "g/dL", out(v.hb, 12, 17.5)],
      ["K⁺", dec(v.potassium, 1), "mmol/L", out(v.potassium, 3.5, 5.0)], ["Ca²⁺", dec(v.calcium, 2), "mmol/L", out(v.calcium, 1.15, 1.30)],
      ["Glukose", dec(v.glucose, 0), "mg/dL", out(v.glucose, 70, 140)],
    ];
  }
  function bloodGasReport(v) {
    return `pH ${dec(v.ph, 2)} · pCO2 ${dec(v.paco2, 0)} mmHg · pO2 ${dec(v.pao2, 0)} mmHg · SaO2 ${dec(v.spo2, 0)} % · ` +
      `HCO3 ${dec(v.hco3, 1)} mmol/L · BE ${v.be >= 0 ? "+" : ""}${dec(v.be, 1)} mmol/L · Laktat ${dec(v.lactate, 1)} mmol/L · ` +
      `Hb ${dec(v.hb, 1)} g/dL · K ${dec(v.potassium, 1)} mmol/L · Ca++ ${dec(v.calcium, 2)} mmol/L · Glukose ${dec(v.glucose, 0)} mg/dL`;
  }

  // ---------------------------------------------------------------- Mock-Engine (Engines/MockPhysiologyEngine.cs)
  const C = {
    ArrestThreshold: 0.60,
    Retention: { BalancedCrystalloid: 0.3, Saline: 0.3, Blood: 1.0 },
    TpMaxHrAdd: 30, TpMaxRrAdd: 12, TpMaxSpO2Drop: 25, TpMaxEtCo2Drop: 6,
    TpHypotensionOnset: 0.6, TpMaxSbpDrop: 50, TpLevelAfterNeedle: 0.15, TpRateAfterNeedle: 0.2,
    TbiPoints: [0.0, 0.3, 0.5, 0.7, 0.85, 1.0], TbiGcs: [15, 14, 11, 8, 5, 3], ShockGcs: [15, 15, 15, 14, 12, 9],
    TbiAnisocoriaLevel: 0.7, TbiCushingLevel: 0.8, TbiSecondaryInsultFactor: 2.0,
    TbiOsmotherapyReduction: 0.15, TbiLevelAfterSurgery: 0.4,
    AirwayMaxSpO2Drop: 15, AirwayMaxEtCo2Rise: 10, TpFactorWhenVentilated: 3.0,
    VasopressorSbpRise: 20, VasopressorDbpRise: 12,
    // Blutungsphysik wie MockPhysiologyEngine.cs (Malbrain 2016, Sondeen 2003; Bildungszeit/Restfluss Annahmen)
    IapBaseline: 7, AbdominalCompliance: 400, RebleedMap: 64, ClotFormation_s: 300, ClotFlowFactor: 0.1,
    // Hydrostatik und Lagerung wie MockPhysiologyEngine.cs
    HydroPerM: 1060 * 9.81 / 133.322, VenousPressure: 7, HipLevel: 0.530, ShoulderLevel: 0.818, HeartLevel: 0.72,
  };
  const Sites = {
    Extremity: [(0.285 + 0.039) / 2, "legR"], RightThigh: [(0.530 + 0.285) / 2, "legR"], LeftThigh: [(0.530 + 0.285) / 2, "legL"],
    LeftArm: [(0.630 + 0.485) / 2, "armL"], RightArm: [(0.630 + 0.485) / 2, "armR"],
    Pelvis: [0.53, "trunk"], Abdomen: [0.62, "trunk"], Liver: [0.66, "trunk"], Spleen: [0.66, "trunk"],
  };
  const Positions = {
    flach: [0, 0, 0, 0, 0, 0], beine_hoch: [0, 45, 45, 0, 0, 150], bein_rechts_hoch: [0, 45, 0, 0, 0, 75],
    oberkoerper_30: [30, 0, 0, 0, 0, 0], arm_links_hoch: [0, 0, 0, 0, 90, 0],
  };
  function siteHeight(level, limb, pos, H) {
    const rad = Math.PI / 180, trunk = pos[0] * rad;
    const heart = (C.HeartLevel - C.HipLevel) * H * Math.sin(trunk);
    let h;
    if (limb === "legR") h = (C.HipLevel - level) * H * Math.sin(pos[1] * rad);
    else if (limb === "legL") h = (C.HipLevel - level) * H * Math.sin(pos[2] * rad);
    else if (limb === "armR" || limb === "armL") {
      const arm = (limb === "armR" ? pos[3] : pos[4]) * rad;
      h = (C.ShoulderLevel - C.HipLevel) * H * Math.sin(trunk) + (C.ShoulderLevel - level) * H * Math.sin(arm - trunk);
    } else h = (level - C.HipLevel) * H * Math.sin(trunk);
    return h - heart;
  }
  function spaceOf(c) {
    if (c === "Spleen" || c === "Liver" || c === "Abdomen") return "Peritoneum";
    if (c === "Extremity" || c === "LeftArm" || c === "RightArm") return "";
    return c;
  }
  const canClot = (l) => l === "transection" || l === "parenchymal" || l === "venous";

  function bloodVolume(p) { return p.weight_kg * (String(p.sex).toLowerCase() === "female" ? 65 : 70); }

  class MockEngine {
    constructor(seed, mode) {
      this.mode = mode === "Registry" ? "Registry" : "Textbook";
      this.random = rng(seed == null ? Math.floor(Math.random() * 2 ** 31) : seed);
    }
    get name() { return this.mode === "Registry" ? "Mock (Realitätsmodus)" : "Mock (Lehrbuchmodus)"; }
    uniform(min, max) { return min + this.random() * (max - min); }

    initialize(patient) {
      this.baselineVolume = this.volume = bloodVolume(patient);
      this.time = 0;
      this.bleeds = {}; this.tension = {}; this.brain = {}; this.infusions = [];
      this.oxygen = this.arrest = this.intubated = this.vasopressor = false;
      this.position = Positions.flach; this.positionId = "flach";
      this.height = patient.height_cm > 100 ? patient.height_cm / 100 : 1.8;
      this.hrBaseOffset = 0; this.hrResponse = 1; this.sbpOffset = 0;
      if (this.mode === "Registry") {
        this.hrBaseOffset = this.uniform(-12, 12);
        this.hrResponse = this.uniform(0.5, 1.5);
        this.sbpOffset = this.uniform(-10, 10) + (patient.age >= 65 ? 15 : 0);
      }
      if (patient.betaBlocker) this.hrResponse *= 0.25;
      const hb0 = String(patient.sex).toLowerCase() === "female" ? 13.0 : 14.5;
      this.redCells = this.volume * hb0 * 3 / 100;
      this.transfused = 0;
      this.current = null;
      this.updateVitals();
      this.baselineMap = this.current.map;
      if (patient.initialBloodLoss_mL > 0) {
        const lost = Math.min(patient.initialBloodLoss_mL, this.volume * 0.5);
        this.redCells -= lost * this.redCells / this.volume;
        this.volume -= lost;
        this.updateVitals();
      }
      return true;
    }

    /** Schwere 0..1 (Spannungspneu, SHT) bzw. Blutverlust einer Verletzung — für dynamische Befunde. */
    injuryLevel(injuryId) {
      let level = 0;
      for (const t of Object.values(this.tension)) if (t.inj.injuryId === injuryId) level = Math.max(level, t.level);
      for (const b of Object.values(this.brain)) if (b.inj.injuryId === injuryId) level = Math.max(level, b.level);
      return level;
    }
    injuryBloodLoss(injuryId) {
      let lost = 0;
      for (const b of Object.values(this.bleeds)) if (b.inj.injuryId === injuryId) lost += b.lost;
      return lost;
    }

    applyInjury(inj) {
      if (inj.effect === "Hemorrhage") this.bleeds[inj.compartment] = { inj, multiplier: 1, lost: 0, clotTimer: 0, clotted: false };
      else if (inj.effect === "TensionPneumothorax") this.tension[inj.compartment] = { inj, level: 0, rateFactor: 1, drained: false };
      else if (inj.effect === "TraumaticBrainInjury") this.brain[inj.compartment] = { inj, level: Math.max(0, Math.min(1, inj.initialLevel || 0)), stopped: false };
    }

    applyIntervention(iv) {
      switch (iv.type) {
        case "Positioning": {
          const pos = Positions[iv.target || ""];
          if (pos) { this.position = pos; this.positionId = iv.target; this.updateVitals(); }
          break;
        }
        case "HemorrhageControl": {
          for (const t of String(iv.target || "").split(",")) { const b = this.bleeds[t.trim()]; if (b) b.multiplier = Math.min(b.multiplier, iv.rateMultiplier); }
          break;
        }
        case "FluidInfusion": this.infusions.push({ compound: iv.compound, remaining: iv.volume_mL, rate: iv.rate_mL_min }); break;
        case "Oxygen": this.oxygen = true; break;
        case "NeedleDecompression": {
          const t = this.tension[iv.target];
          if (t && !t.drained && this.random() < iv.successProbability) {
            t.level = Math.min(t.level, C.TpLevelAfterNeedle);
            t.rateFactor = Math.min(t.rateFactor, C.TpRateAfterNeedle);
          }
          break;
        }
        case "Intubation": this.intubated = true; break;
        case "Vasopressor": this.vasopressor = true; break;
        case "Osmotherapy": for (const b of Object.values(this.brain)) b.level = Math.max(0, b.level - C.TbiOsmotherapyReduction); break;
        case "NeurosurgicalDecompression": for (const b of Object.values(this.brain)) { b.level = Math.min(b.level, C.TbiLevelAfterSurgery); b.stopped = true; } break;
        case "ChestDrain": { const t = this.tension[iv.target]; if (t) { t.level = 0; t.drained = true; } break; }
      }
    }

    advance(dt) {
      if (dt <= 0) return;
      this.time += dt;
      const minutes = dt / 60;
      const cur = this.current;
      if (!this.arrest) {
        const map = cur.map, spaceVol = {};
        for (const b of Object.values(this.bleeds)) { const sp = spaceOf(b.inj.compartment); spaceVol[sp] = (spaceVol[sp] || 0) + b.lost; }
        const back = (b, v) => {
          const sp = spaceOf(b.inj.compartment);
          if (sp === "") return 0;
          if (sp === "Peritoneum") return C.IapBaseline * Math.exp(v / (C.IapBaseline * C.AbdominalCompliance));
          return b.inj.maxTotalLoss_mL > 0 ? v / b.inj.maxTotalLoss_mL * pref(b) : 0;
        };
        const pref = (b) => b.inj.lesion === "venous" ? C.VenousPressure : this.baselineMap;
        const sh = (c) => { const st = Sites[c]; return st ? siteHeight(st[0], st[1], this.position, this.height) : 0; };
        for (const b of Object.values(this.bleeds)) {
          if (canClot(b.inj.lesion)) {
            if (map < C.RebleedMap) { b.clotTimer += dt; if (b.clotTimer >= C.ClotFormation_s) b.clotted = true; }
            else { b.clotTimer = 0; b.clotted = false; }
          }
          const p = (b.inj.lesion === "venous" ? C.VenousPressure * map / this.baselineMap : map) - C.HydroPerM * sh(b.inj.compartment);
          const drive = Math.max(0, p - back(b, spaceVol[spaceOf(b.inj.compartment)])) / Math.max(0.5, pref(b) - back(b, 0));
          const rate = b.inj.baseRate_mL_min * b.inj.severity * b.multiplier * Math.sqrt(drive) * (b.clotted ? C.ClotFlowFactor : 1);
          const l = rate * minutes; b.lost += l;
          if (this.volume > 0) this.redCells -= l * this.redCells / this.volume;
          this.volume -= l;
        }
        for (const t of Object.values(this.tension)) {
          if (t.drained) continue;
          const rate = t.inj.progressionRate_per_min * t.inj.severity * t.rateFactor * (this.intubated ? C.TpFactorWhenVentilated : 1);
          t.level = Math.min(1, t.level + rate * minutes);
        }
        const insult = cur.sbp < 90 || cur.spo2 < 90;
        for (const b of Object.values(this.brain)) {
          if (b.stopped) continue;
          const rate = b.inj.progressionRate_per_min * b.inj.severity * (insult ? C.TbiSecondaryInsultFactor : 1);
          b.level = Math.min(1, b.level + rate * minutes);
        }
      }
      for (const inf of this.infusions) {
        const given = Math.min(inf.rate * minutes, inf.remaining);
        inf.remaining -= given;
        const ret = C.Retention[inf.compound] != null ? C.Retention[inf.compound] : 0.3;
        this.volume += given * ret;
        if (inf.compound === "Blood") { this.redCells += given * 0.6; this.transfused += given; }
      }
      this.infusions = this.infusions.filter((i) => i.remaining > 0);
      this.redCells = Math.max(0, Math.min(this.redCells, this.volume));
      this.volume = Math.max(0, Math.min(this.volume, this.baselineVolume * 1.1));
      this.updateVitals();
    }

    updateVitals() {
      const f = Math.max(0, 1 - this.volume / this.baselineVolume);
      let tp = 0; for (const t of Object.values(this.tension)) tp = Math.max(tp, t.level);
      let tbi = 0; for (const b of Object.values(this.brain)) tbi = Math.max(tbi, b.level);
      if (f >= C.ArrestThreshold || tp >= 1 || tbi >= 1) this.arrest = true;
      const hb = this.volume > 0 ? this.redCells / this.volume * 100 / 3 : 0;
      if (this.arrest) {
        this.current = { hb, ph: 6.90, paco2: 70, pao2: 30, hco3: 4, potassium: 6.5, calcium: BloodGas.calcium(this.transfused), glucose: BloodGas.glucose(f),
          time: this.time, hr: 0, sbp: 0, dbp: 0, map: 0, rr: 0, spo2: 0, etco2: 5, bv: this.volume, loss: f,
          arrest: true, gcs: 3, be: -20, lactate: 12, anisocoria: tbi >= C.TbiAnisocoriaLevel, intubated: this.intubated };
        return;
      }
      const m = this.mode;
      const fh = Math.max(0, 1 - (this.volume + this.position[5]) / this.baselineVolume);
      const gcs = Math.min(interp(tbi, C.TbiPoints, C.TbiGcs), loss(f, C.ShockGcs));
      const airway = !this.intubated && gcs <= 8 ? Math.min(1, (8 - gcs) / 5 + 0.2) : 0;
      const cushing = tbi > C.TbiCushingLevel ? (tbi - C.TbiCushingLevel) / (1 - C.TbiCushingLevel) : 0;
      const sbpDrop = tp > C.TpHypotensionOnset ? (tp - C.TpHypotensionOnset) / (1 - C.TpHypotensionOnset) * C.TpMaxSbpDrop : 0;
      const hr0 = Shock.hr(m, 0);
      const hr = hr0 + this.hrBaseOffset + (Shock.hr(m, fh) - hr0) * this.hrResponse;
      const sbp = Shock.sbp(m, fh) + this.sbpOffset - sbpDrop + 50 * cushing + (this.vasopressor ? C.VasopressorSbpRise : 0);
      const dbp = Shock.dbp(m, fh) + this.sbpOffset * 0.5 - sbpDrop * 0.6 + 25 * cushing + (this.vasopressor ? C.VasopressorDbpRise : 0);
      let spo2 = loss(f, SpO2Air) - C.TpMaxSpO2Drop * tp - C.AirwayMaxSpO2Drop * airway + (this.oxygen ? 3 : 0) + (this.intubated ? 3 : 0);
      spo2 = Math.max(50, Math.min(100, spo2));
      const be = Shock.be(f), hco3 = BloodGas.hco3(be), paco2 = BloodGas.paco2(hco3, this.intubated, airway, tp), ph = BloodGas.ph(hco3, paco2);
      this.current = {
        time: this.time,
        hr: hr + C.TpMaxHrAdd * tp - 35 * cushing,
        sbp, dbp, map: dbp + (sbp - dbp) / 3,
        rr: this.intubated ? 14 : Shock.rr(m, f) + C.TpMaxRrAdd * tp - 6 * airway - 6 * cushing,
        spo2,
        etco2: loss(f, EtCo2) - C.TpMaxEtCo2Drop * tp + C.AirwayMaxEtCo2Rise * airway,
        be, lactate: Shock.lactate(f), hb, hco3, paco2, ph,
        pao2: BloodGas.pao2(spo2, this.oxygen, this.intubated), potassium: BloodGas.potassium(ph),
        calcium: BloodGas.calcium(this.transfused), glucose: BloodGas.glucose(f),
        gcs: roundHalfEven(gcs), anisocoria: tbi >= C.TbiAnisocoriaLevel, intubated: this.intubated,
        bv: this.volume, loss: f, arrest: false,
      };
    }
  }

  // ---------------------------------------------------------------- StateWatcher (Core/StateWatcher.cs)
  const StateTags = { Hypotension: "state:hypotension", Hypoxia: "state:hypoxia", GcsDrop: "state:gcs13", Gcs8: "state:gcs8", Anisocoria: "state:anisocoria", MapLowTbi: "state:map-low-tbi" };
  class StateWatcher {
    constructor() { this.hypo = this.hypox = this.g13 = this.g8 = this.aniso = this.mapLow = false; }
    check(v) {
      const out = [];
      if (v.arrest) return out;
      if (!this.hypo && v.sbp < 90) { this.hypo = true; out.push(["Hypotonie (RRsys < 90 mmHg)", StateTags.Hypotension]); }
      else if (this.hypo && v.sbp >= 95) this.hypo = false;
      if (!this.hypox && v.spo2 < 90) { this.hypox = true; out.push(["Hypoxie (SpO2 < 90 %)", StateTags.Hypoxia]); }
      else if (this.hypox && v.spo2 >= 92) this.hypox = false;
      if (!this.g13 && v.gcs > 0 && v.gcs <= 13) { this.g13 = true; out.push([`Bewusstseinsminderung (GCS ${v.gcs})`, StateTags.GcsDrop]); }
      if (!this.g8 && v.gcs > 0 && v.gcs <= 8) { this.g8 = true; out.push([`Bewusstlos (GCS ${v.gcs})`, StateTags.Gcs8]); }
      if (!this.aniso && v.anisocoria) { this.aniso = true; out.push(["Anisokorie", StateTags.Anisocoria]); }
      const severeTbi = v.gcs > 0 && v.gcs <= 8;
      if (!this.mapLow && severeTbi && v.map < 80) { this.mapLow = true; out.push(["MAD unter 80 mmHg bei schwerem SHT", StateTags.MapLowTbi]); }
      else if (this.mapLow && v.map >= 85) this.mapLow = false;
      return out;
    }
  }

  // ---------------------------------------------------------------- Assessment (Core/Assessment.cs)
  function evaluate(criteria, events) {
    const matches = (tag, prefixes) => tag != null && prefixes.some((p) => tag.startsWith(p));
    return (criteria || []).map((c) => {
      const matching = events.filter((e) => matches(e.tag, c.tags));
      const r = { criterion: c, count: matching.length, time: null, deadline: 0, outcome: "" };
      if (c.type === "maxCount") { r.outcome = matching.length <= c.limit ? "Met" : "Exceeded"; return r; }
      let start = 0;
      if (c.relativeTo) {
        const refs = events.filter((e) => matches(e.tag, [c.relativeTo]));
        if (!refs.length) { r.outcome = "NotApplicable"; return r; }
        start = Math.min(...refs.map((e) => e.time));
      }
      r.deadline = start + c.limit;
      const rel = c.onlyAfterReference ? matching.filter((e) => e.time >= start) : matching;
      r.time = rel.length ? Math.min(...rel.map((e) => e.time)) : null;
      r.outcome = r.time == null ? "Missed" : r.time <= r.deadline ? "Met" : "Late";
      return r;
    });
  }

  function shockClass(f) { return f < 0.15 ? "I" : f < 0.30 ? "II" : f < 0.40 ? "III" : "IV"; }

  // ---------------------------------------------------------------- Simulation (Simulation/SimulationManager.cs)
  const clock = (s) => { const t = Math.floor(s); return `${String(Math.floor(t / 60)).padStart(2, "0")}:${String(t % 60).padStart(2, "0")}`; };
  const de1 = (x) => x.toFixed(1).replace(".", ",");

  class Simulation {
    constructor(data, scenarioId, mode, seed, ctInTraumaRoom) {
      this.data = data;
      this.ctInTraumaRoom = !!ctInTraumaRoom;
      this.imaging = Object.fromEntries(data.imaging.modalities.map((m) => [m.id, m]));
      this.awayUntil = -1;
      this.scenario = typeof scenarioId === "object" ? scenarioId : data.scenarios.find((s) => s.id === scenarioId);
      this.injuries = Object.fromEntries(data.injuries.injuries.map((i) => [i.id, i]));
      this.interventions = Object.fromEntries(data.interventions.interventions.map((i) => [i.id, i]));
      this.engine = new MockEngine(seed, mode);
      this.engine.initialize(this.scenario.patient);
      this.pending = this.scenario.injuries.slice();
      this.delayed = [];
      this.watcher = new StateWatcher();
      this.events = [];
      this.samples = [this.engine.current];
      this.nextSample = 5;
      this.used = new Set();
      this.suspected = new Set();
      this.imagingResults = [];
      this.labResults = [];
      this.ended = false;
      this.step = 0.1;
      this.log(`Szenario „${this.scenario.title}“ gestartet (${this.engine.name})`, null);
    }
    get vitals() { return this.engine.current; }
    get isAway() { return this.engine.current.time < this.awayUntil; }
    log(message, tag) { this.events.push({ time: this.engine.current.time, message, tag }); }

    /** Rückt um dt Sekunden in Schritten von 0,1 s vor (wie SimulationManager.Update). */
    advance(dt) {
      let remaining = dt;
      while (remaining > 1e-9 && !this.ended && !this.engine.current.arrest) {
        const t = this.engine.current.time;
        for (let i = this.pending.length - 1; i >= 0; i--) {
          const si = this.pending[i];
          if (si.onsetTime_s > t) continue;
          const def = this.injuries[si.injuryId];
          for (const e of def.effects) this.engine.applyInjury({ injuryId: si.injuryId, effect: e.effectType, compartment: e.compartment, severity: si.severity,
            baseRate_mL_min: e.baseRate_mL_min, maxTotalLoss_mL: e.maxTotalLoss_mL, lesion: e.lesion || "", progressionRate_per_min: e.progressionRate_per_min, initialLevel: e.initialLevel || 0 });
          this.log(`Verletzung aktiv: ${def.name}`, "injury:" + si.injuryId);
          this.pending.splice(i, 1);
        }
        for (let i = this.delayed.length - 1; i >= 0; i--) {
          const d = this.delayed[i];
          if (d.due > t) continue;
          if (d.result) {
            const img = d.result.startsWith("Befund ");
            if (d.bga) this.labResults.push({ time: t, ...d.bga });
            this.log(d.result, img ? "result:Imaging:" + d.iv.id : "result:" + d.iv.type);
            if (img) { this.imagingResults.push({ time: t, id: d.iv.id, label: d.label, lines: d.lines, images: d.images });
              if (this.awayUntil > 0 && t >= this.awayUntil - 1e-6) { this.awayUntil = -1; this.log("Patient zurück im Schockraum", "transport:back"); } }
          }
          else { this.engine.applyIntervention(d.iv); this.log(`Wirksam: ${d.label}`, "effect:" + d.iv.type); }
          this.delayed.splice(i, 1);
        }
        this.engine.advance(this.step);
        for (const [msg, tag] of this.watcher.check(this.engine.current)) this.log(msg, tag);
        if (this.engine.current.time >= this.nextSample) { this.samples.push(this.engine.current); this.nextSample += 5; }
        remaining -= this.step;
      }
      if (this.engine.current.arrest && !this.arrestLogged) { this.arrestLogged = true; this.samples.push(this.engine.current); this.log("Kreislaufstillstand", "arrest"); }
    }

    /** Wie InterventionController.Apply: Definition aus interventions.json. Gibt false zurück, wenn nicht (mehr) möglich. */
    apply(id) {
      const def = this.interventions[id];
      if (!def || this.ended || this.engine.current.arrest || this.isAway) return false;
      if (def.once && this.used.has(id)) return false;
      this.used.add(id);
      const iv = { id: def.id, type: def.type, target: def.target || null, compound: def.compound || null, volume_mL: def.volume_mL,
        rate_mL_min: def.rate_mL_min, rateMultiplier: def.rateMultiplier, successProbability: def.successProbability };
      const tag = `intervention:${iv.type}:${iv.target || iv.compound || ""}:${iv.id}`;
      const label = def.logLabel || def.label;
      const v = this.engine.current;
      if (iv.type === "BloodGas") {
        const result = `BGA-Ergebnis (abgenommen ${clock(v.time)}): ` + bloodGasReport(v);
        this.delayed.push({ due: v.time + def.effectDelay_s, iv, label, result, bga: { sampled: v.time, values: bloodGasValues(v) } });
        this.log(`Maßnahme: ${label}`, tag);
        return true;
      }
      if (def.effectDelay_s > 0) {
        this.delayed.push({ due: v.time + def.effectDelay_s, iv, label, result: null });
        this.log(`Angeordnet: ${label} (wirksam in ca. ${Math.round(def.effectDelay_s / 60)} min)`, tag);
        return true;
      }
      this.engine.applyIntervention(iv);
      this.log(`Maßnahme: ${label}`, tag);
      return true;
    }

    /** Wie SimulationManager.RequestImaging. Gibt false zurück, wenn nicht möglich. */
    requestImaging(id) {
      const m = this.imaging[id];
      if (!m || this.ended || this.engine.current.arrest || this.isAway) return false;
      const v = this.engine.current;
      const transport = m.transport_s > 0 && !(m.isCT && this.ctInTraumaRoom);
      const total = m.duration_s + (transport ? m.transport_s : 0);
      const tags = this.events.map((e) => e.tag).filter(Boolean);
      const lines = [], images = [];
      for (const si of this.scenario.injuries) {
        if (si.onsetTime_s > v.time) continue;
        for (const f of this.injuries[si.injuryId].imaging || []) {
          if (!m.covers.includes(f.modality)) continue;
          const resolved = f.resolvedByTag && tags.some((x) => x.startsWith(f.resolvedByTag));
          lines.push(resolved && f.resolvedFinding ? f.resolvedFinding : f.finding);
          if (f.image && !resolved) images.push({ src: f.image, credit: f.imageCredit });
        }
      }
      if (!lines.length) lines.push(m.normalFinding);
      this.log(`Bildgebung: ${m.logLabel}` + (transport ? " (Transport in die Radiologie)" : m.isCT ? " (CT im Schockraum)" : ""), "imaging:" + m.id);
      if (transport) {
        this.awayUntil = v.time + total;
        if (v.sbp < 90) this.log("Warnung: hämodynamisch instabiler Patient verlässt den Schockraum", "warning:unstable-transport");
      }
      this.delayed.push({ due: v.time + total, iv: { type: "Supportive", id: m.id }, label: m.label, result: `Befund ${m.label}: ` + lines.join(" "), lines, images });
      return true;
    }

    /** Wie BodyRegion.Examine. */
    examine(region) {
      if (this.ended || this.isAway) return null;
      const t = this.engine.current.time;
      // Verletzungsbefunde (fest und dynamisch) oder Normalbefund (wie Data/Examination.cs)
      const v = this.engine.current;
      const findings = examFindings(region, this.scenario, (id) => this.injuries[id], this.data.findings, {
        time: t, bodyLoss: v.loss, level: (id) => this.engine.injuryLevel(id), lost: (id) => this.engine.injuryBloodLoss(id), events: this.events });
      const pathological = this.scenario.injuries.some((si) => si.onsetTime_s <= t && this.injuries[si.injuryId].region === region);
      if (region === "Kopf") {
        const v = this.engine.current;
        findings.push(`Aktuell GCS ${v.gcs}` + (v.intubated ? " (sediert/beatmet)" : ""));
        findings.push(v.anisocoria ? "Pupillen anisokor: eine Pupille weit und lichtstarr" : "Pupillen isokor, prompt lichtreagibel");
      }
      this.log(`Untersuchung: ${region}` + (pathological ? "" : " (unauffällig)"), "exam:" + region);
      return findings;
    }

    /** Verdachtsdiagnose setzen oder zurücknehmen (wie SimulationManager.ToggleSuspected). */
    toggleSuspected(injuryId) {
      if (this.ended) return;
      const diff = this.data.differentials && this.data.differentials.differentials.find((d) => d.id === injuryId);
      const name = diff ? diff.name : this.injuries[injuryId].name;
      if (this.suspected.has(injuryId)) { this.suspected.delete(injuryId); this.log(`Verdacht verworfen: ${name}`, "diagnosis-retracted:" + injuryId); }
      else { this.suspected.add(injuryId); this.log(`Verdachtsdiagnose: ${name}`, "diagnosis:" + injuryId); }
    }

    end() {
      if (this.ended) return;
      this.samples.push(this.engine.current);
      this.log("Szenario beendet", "end");
      this.ended = true;
    }

    /** Daten fürs Debriefing (SessionLog.BuildDebrief), strukturiert statt als Rich-Text. */
    debrief() {
      const s = this.samples, last = this.engine.current;
      const alive = s.filter((v) => !v.arrest);
      const maxLoss = Math.max(...s.map((v) => v.loss));
      const results = evaluate(this.scenario.assessment, this.events).filter((r) => r.outcome !== "NotApplicable");
      return {
        mode: this.engine.mode, duration: last.time, arrest: last.arrest, last,
        maxLoss, shockClass: shockClass(maxLoss),
        maxHr: alive.length ? Math.max(...alive.map((v) => v.hr)) : null,
        minSbp: alive.length ? Math.min(...alive.map((v) => v.sbp)) : null,
        minSpo2: alive.length ? Math.min(...alive.map((v) => v.spo2)) : null,
        comparison: maxLoss >= 0.15 ? {
          textbook: { hr: Shock.hr("Textbook", maxLoss), sbp: Shock.sbp("Textbook", maxLoss) },
          registry: { hr: Shock.hr("Registry", maxLoss), sbp: Shock.sbp("Registry", maxLoss) },
          be: Shock.be(maxLoss),
        } : null,
        results, events: this.events.slice(),
        diagnoses: evaluateDiagnoses(this.scenario.injuries, this.events, (id) => this.injuries[id], this.data.imaging.modalities, this.data.differentials),
      };
    }
  }


  // ---------- Untersuchung (wie Data/Examination.cs) ----------

  function findingApplies(d, injuryId, ctx) {
    const level = ctx.level ? ctx.level(injuryId) : 0;
    if (level < d.minLevel - 1e-9 || level > d.maxLevel + 1e-9) return false;
    if ((ctx.lost ? ctx.lost(injuryId) : 0) < d.minLost_mL) return false;
    if (ctx.bodyLoss < d.minBodyLoss || ctx.bodyLoss > d.maxBodyLoss) return false;
    const first = (prefix) => { const e = (ctx.events || []).find((x) => x.tag && x.tag.startsWith(prefix)); return e ? e.time : null; };
    if (d.requiresTag) { const t = first(d.requiresTag); if (t === null || ctx.time - t < d.requiresTagDelay_s) return false; }
    if (d.untilTag) { const t = first(d.untilTag); if (t !== null && ctx.time - t >= (d.untilTagDelay_s || 0)) return false; }
    return true;
  }

  function examFindings(region, scenario, getInjury, normal, ctx) {
    const findings = [];
    for (const si of scenario.injuries) {
      if (si.onsetTime_s > ctx.time) continue;
      const def = getInjury(si.injuryId);
      if (def.region !== region) continue;
      findings.push(...(def.clinicalFindings || []));
      for (const d of def.dynamicFindings || []) if (findingApplies(d, def.id, ctx)) findings.push(d.text);
    }
    if (!findings.length && normal) {
      const r = normal.regions.find((x) => x.region === region);
      if (r) findings.push(...r.normal);
    }
    return findings;
  }

  // ---------- Diagnose-Modus (wie Data/DiagnosisEvaluation.cs) ----------

  function evaluateDiagnoses(actual, events, getInjury, modalities, differentials) {
    const suspected = new Map();
    for (const e of events) {
      if (!e.tag) continue;
      if (e.tag.startsWith("diagnosis:")) { const id = e.tag.slice(10); if (!suspected.has(id)) suspected.set(id, e.time); }
      else if (e.tag.startsWith("diagnosis-retracted:")) suspected.delete(e.tag.slice(20));
    }
    const tags = new Set(events.filter((e) => e.tag).map((e) => e.tag));
    const results = [], actualIds = new Set();
    for (const si of actual) {
      if (actualIds.has(si.injuryId)) continue;
      actualIds.add(si.injuryId);
      const def = getInjury(si.injuryId);
      const shownBy = new Set((def.imaging || []).map((f) => f.modality));
      const hit = suspected.has(si.injuryId);
      results.push({
        injuryId: def.id, name: def.name, region: def.region, outcome: hit ? "Correct" : "Missed",
        time: hit ? suspected.get(si.injuryId) : null, onset: si.onsetTime_s,
        examined: tags.has("exam:" + def.region),
        imagingShown: modalities.some((m) => m.covers.some((c) => shownBy.has(c)) && tags.has("result:Imaging:" + m.id)),
        clinicalOnly: shownBy.size === 0, isDifferential: false, against: [],
      });
    }
    for (const [id, time] of suspected) {
      if (actualIds.has(id)) continue;
      const diff = differentials && differentials.differentials.find((d) => d.id === id);
      if (diff) {
        const specific = diff.against.filter((x) => x.injuryId && actualIds.has(x.injuryId)).map((x) => x.text);
        results.push({ injuryId: id, name: diff.name, region: diff.region, outcome: "FalsePositive", time, onset: 0, examined: false, imagingShown: false, clinicalOnly: false,
          isDifferential: true, against: specific.length ? specific : diff.against.filter((x) => !x.injuryId).map((x) => x.text) });
        continue;
      }
      const def = getInjury(id);
      const typical = (def.clinicalFindings || []).concat((def.dynamicFindings || [])
        .filter((d) => !d.requiresTag && d.minLevel <= 0 && d.minLost_mL <= 0).map((d) => d.text)).slice(0, 2);
      results.push({ injuryId: id, name: def.name, region: def.region, outcome: "FalsePositive", time, onset: 0, examined: false, imagingShown: false, clinicalOnly: false,
        isDifferential: false, against: typical.length ? ["Typische Befunde fehlten: " + typical.join("; ") + "."] : [] });
    }
    return results;
  }

  // ---------- Unfallmechanismus → Zufallsszenario (wie Data/ScenarioGenerator.cs) ----------

  /** Mulberry32 — identisch zu SeededRandom in C#, damit ein Fall-Code überall dasselbe Szenario ergibt. */
  function seededRandom(seed) {
    let state = seed >>> 0;
    const next = () => {
      state = (state + 0x6D2B79F5) >>> 0;
      let t = state;
      t = Math.imul(t ^ (t >>> 15), t | 1) >>> 0;
      t = (t ^ (t + Math.imul(t ^ (t >>> 7), t | 61))) >>> 0;
      return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
    };
    return { next, range: (min, max) => min + next() * (max - min) };
  }

  function generateScenario(data, mechanismId, seed) {
    const db = data.mechanisms;
    const mech = db.mechanisms.find((m) => m.id === mechanismId);
    if (!mech) throw new Error(`Mechanismus '${mechanismId}' nicht gefunden`);
    const defs = Object.fromEntries(data.injuries.injuries.map((d) => [d.id, d]));
    const rng = seededRandom(seed), a = db.anthropometry;
    const R = (v) => Math.floor(v + 0.5), round2 = (v) => R(v * 100) / 100;
    const conflicts = (chosen, id) => db.exclusiveGroups.some((g) => g.injuryIds.includes(id) && chosen.some((si) => g.injuryIds.includes(si.injuryId)));
    const relevant = (list) => list.some((si) => defs[si.injuryId].effects.length > 0);

    const male = rng.next() < mech.maleShare;
    const age = R(rng.range(mech.ageMin, mech.ageMax));
    const weight = R(male ? rng.range(a.maleWeightMin_kg, a.maleWeightMax_kg) : rng.range(a.femaleWeightMin_kg, a.femaleWeightMax_kg));
    const height = R(male ? rng.range(a.maleHeightMin_cm, a.maleHeightMax_cm) : rng.range(a.femaleHeightMin_cm, a.femaleHeightMax_cm));
    const betaBlocker = rng.next() < a.betaBlockerShare && age >= a.betaBlockerMinAge;
    const unrestrained = rng.next() < mech.unrestrainedShare;
    const speed = R(rng.range(mech.speedMin, mech.speedMax) / 5) * 5;
    const fallHeight = R(rng.range(mech.heightMin, mech.heightMax));
    const rescue = R(rng.range(mech.rescueMin, mech.rescueMax) / 5) * 5;

    let chosen = [];
    for (let attempt = 0; attempt < 50; attempt++) {
      chosen = [];
      for (const c of mech.injuries) {
        let p = c.probability;
        if (unrestrained) for (const f of mech.unrestrainedMultipliers) if (f.injuryId === c.injuryId) p *= f.factor;
        const pick = rng.next(), sev = rng.range(c.severityMin, c.severityMax), onset = rng.range(c.onsetMin_s, c.onsetMax_s);
        if (pick >= Math.min(1, p) || conflicts(chosen, c.injuryId)) continue;
        chosen.push({ injuryId: c.injuryId, severity: round2(sev), onsetTime_s: R(onset) });
      }
      if (relevant(chosen)) break;
    }
    if (!relevant(chosen)) {
      const fb = mech.injuries.filter((c) => defs[c.injuryId].effects.length > 0 && !conflicts(chosen, c.injuryId))
        .reduce((best, c) => (!best || c.probability > best.probability ? c : best), null);
      chosen.push({ injuryId: fb.injuryId, severity: round2((fb.severityMin + fb.severityMax) / 2), onsetTime_s: fb.onsetMin_s });
    }

    let rate = 0;
    for (const si of chosen) for (const e of defs[si.injuryId].effects)
      if (e.effectType === "Hemorrhage" && si.onsetTime_s <= 0) rate += e.baseRate_mL_min * si.severity;
    const ph = db.prehospital;
    const loss = R(Math.min(ph.maxLoss_mL, rate * rescue * ph.lossFactor) / ph.roundTo_mL) * ph.roundTo_mL;

    const criteria = [], ids = new Set();
    for (const cr of db.baseCriteria.concat(...chosen.map((si) => defs[si.injuryId].criteria || [])))
      if (!ids.has(cr.id)) { ids.add(cr.id); criteria.push(cr); }

    let description = mech.descriptionTemplate
      .split("{age}").join(String(age)).split("{r}").join(male ? "r" : "").split("{in}").join(male ? "" : "in")
      .split("{driver}").join(male ? "Fahrer" : "Fahrerin").split("{belt}").join(unrestrained ? "nicht angeschnallt" : "angeschnallt")
      .split("{speed}").join(String(speed)).split("{height}").join(String(fallHeight)).split("{rescue}").join(String(rescue));
    if (loss >= ph.reportThreshold_mL) description += ` Präklinisch geschätzter Blutverlust ca. ${loss} mL.`;
    if (betaBlocker) description += " Dauermedikation: Betablocker.";

    return {
      id: `zufall_${mech.id}_${seed}`, title: `Zufall: ${mech.label}`, description, mechanism: mech.label,
      patient: { sex: male ? "male" : "female", age, weight_kg: weight, height_cm: height, betaBlocker, initialBloodLoss_mL: loss },
      injuries: chosen, learningGoals: db.learningGoals,
      expectedCourse: "Verletzungen: " + chosen.map((si) => defs[si.injuryId].name).join(", ") + ".",
      defaultTimeScale: 1, assessment: criteria, generated: true, seed,
    };
  }

  const api = { examFindings, evaluateDiagnoses, seededRandom, generateScenario, BloodGas, bloodGasReport, MockEngine, Simulation, StateWatcher, evaluate, Shock, LossPoints, clock, de1, shockClass };
  if (typeof module !== "undefined" && module.exports) module.exports = api;
  else root.NotfallEngine = api;
})(typeof window !== "undefined" ? window : globalThis);
