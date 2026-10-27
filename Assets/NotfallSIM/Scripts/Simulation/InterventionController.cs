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

        /// <summary>Für Buttons: OnClick mit String-Parameter, z. B. "beckengurt".</summary>
        public void Apply(string interventionId)
        {
            if (!_byId.TryGetValue(interventionId, out var def))
            {
                Debug.LogWarning($"[Maßnahmen] Unbekannte ID '{interventionId}'");
                return;
            }
            if (def.once && _used.Contains(def.id)) return;

            var ev = def.ToEvent();
            if (ev == null)
            {
                Debug.LogWarning($"[Maßnahmen] Unbekannter Typ '{def.type}' bei '{def.id}'");
                return;
            }
            _used.Add(def.id);
            sim.ApplyIntervention(ev, string.IsNullOrEmpty(def.logLabel) ? def.label : def.logLabel);
        }
    }
}
