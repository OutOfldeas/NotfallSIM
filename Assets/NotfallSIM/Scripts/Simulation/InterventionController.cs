using System.Collections.Generic;
using NotfallSIM.Data;
using UnityEngine;

namespace NotfallSIM.Simulation
{
    /// <summary>
    /// Führt Maßnahmen aus StreamingAssets/NotfallSIM/interventions.json aus.
    /// Buttons rufen <see cref="Apply"/> mit der Maßnahmen-ID auf — alle Zahlenwerte
    /// (Volumen, Laufraten, Effektstärken) stehen mit Quellen in der JSON-Datei, nicht im Code.
    /// </summary>
    public class InterventionController : MonoBehaviour
    {
        [SerializeField] SimulationManager sim;

        readonly Dictionary<string, InterventionDefinition> _byId = new Dictionary<string, InterventionDefinition>();
        readonly HashSet<string> _used = new HashSet<string>();

        public void Configure(SimulationManager simulation) => sim = simulation;

        void Awake()
        {
            foreach (var d in InjuryDatabase.LoadInterventions().interventions) _byId[d.id] = d;
        }

        public bool IsUsed(string id) => _used.Contains(id);
        public InterventionDefinition Get(string id) => _byId.TryGetValue(id, out var d) ? d : null;

        /// <summary>Für Buttons: OnClick mit String-Parameter, z. B. "beckengurt". Bei einstellbarer Dosis öffnet sich der Dosisdialog.</summary>
        public void Apply(string interventionId)
        {
            if (!_byId.TryGetValue(interventionId, out var def))
            {
                Debug.LogWarning($"[Maßnahmen] Unbekannte ID '{interventionId}'");
                return;
            }
            if (def.once && _used.Contains(def.id)) return;
            if (sim.Ended || sim.IsAway || sim.Vitals.CardiacArrest) { sim.Note($"Nicht möglich: {def.label}"); return; }
            if (def.dose != null) { NotfallSIM.UI.DoseDialog.Show(def, Weight, d => ApplyDose(interventionId, d)); return; }
            Execute(def, def.ToEvent(), null);
        }

        /// <summary>Maßnahme mit gewählter Dosis (Einheit wie in "dose.unit").</summary>
        public void ApplyDose(string interventionId, double doseValue)
        {
            if (!_byId.TryGetValue(interventionId, out var def) || def.dose == null) { Apply(interventionId); return; }
            if (def.once && _used.Contains(def.id)) return;
            if (sim.Ended || sim.IsAway || sim.Vitals.CardiacArrest) { sim.Note($"Nicht möglich: {def.label}"); return; }
            Execute(def, def.ToEvent(doseValue), doseValue);
        }

        double Weight => sim.Scenario != null && sim.Scenario.patient != null ? sim.Scenario.patient.weight_kg : 0;

        void Execute(InterventionDefinition def, NotfallSIM.Core.InterventionEvent ev, double? doseValue)
        {
            if (ev == null)
            {
                Debug.LogWarning($"[Maßnahmen] Unbekannter Typ '{def.type}' bei '{def.id}'");
                return;
            }
            _used.Add(def.id);
            string label = string.IsNullOrEmpty(def.logLabel) ? def.label : def.logLabel;
            if (doseValue.HasValue) label += " " + def.dose.Format(doseValue.Value, Weight);
            sim.ApplyIntervention(ev, label, def.effectDelay_s);
            if (doseValue.HasValue && !def.dose.Usual(doseValue.Value))
                sim.Note($"Warnung: Dosis {def.dose.Format(doseValue.Value, Weight)} außerhalb des üblichen Bereichs " +
                         $"({def.dose.Format(def.dose.usualMin, 0)} bis {def.dose.Format(def.dose.usualMax, 0)})", "warning:dose:" + def.id);
            WarnContraindicated(def);
        }

        /// <summary>Warnung im Protokoll (Tag "warning:contraindicated:&lt;id&gt;"), wenn eine aktive Verletzung die Maßnahme verbietet.</summary>
        void WarnContraindicated(InterventionDefinition def)
        {
            if (def.contraindicatedBy == null || def.contraindicatedBy.Length == 0 || sim.Scenario == null) return;
            double t = sim.Vitals.SimTime_s;
            foreach (var si in sim.Scenario.injuries)
                if (si.onsetTime_s <= t && System.Array.IndexOf(def.contraindicatedBy, si.injuryId) >= 0)
                {
                    sim.Note($"Warnung: {def.label} kontraindiziert — {def.contraindicationNote}", "warning:contraindicated:" + def.id);
                    return;
                }
        }
    }
}
