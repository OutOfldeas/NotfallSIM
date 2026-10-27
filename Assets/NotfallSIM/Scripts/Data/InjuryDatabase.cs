using System;
using System.Collections.Generic;
using System.IO;
using NotfallSIM.Core;
using UnityEngine;

namespace NotfallSIM.Data
{
    /// <summary>
    /// Lädt Verletzungsdefinitionen und Szenarien aus StreamingAssets/NotfallSIM.
    /// Die JSON-Dateien sind die "Wahrheit" — Code enthält keine medizinischen Zahlenwerte.
    /// </summary>
    public class InjuryDatabase
    {
        public static string Root => Path.Combine(Application.streamingAssetsPath, "NotfallSIM");

        readonly Dictionary<string, InjuryDefinition> _byId = new Dictionary<string, InjuryDefinition>();
        public string Version { get; private set; }

        public static InjuryDatabase Load()
        {
            var db = new InjuryDatabase();
            string json = File.ReadAllText(Path.Combine(Root, "injuries.json"));
            var file = JsonUtility.FromJson<InjuryDatabaseFile>(json);
            db.Version = file.version;
            foreach (var inj in file.injuries)
            {
                db._byId[inj.id] = inj;
                if (inj.evidenceStatus != "validated")
                    Debug.Log($"[DB] '{inj.id}' hat Status '{inj.evidenceStatus}' — Werte noch nicht validiert.");
            }
            return db;
        }

        public static ImagingDatabaseFile LoadImaging() =>
            JsonUtility.FromJson<ImagingDatabaseFile>(File.ReadAllText(Path.Combine(Root, "imaging.json")));

        public static InterventionDatabaseFile LoadInterventions() =>
            JsonUtility.FromJson<InterventionDatabaseFile>(File.ReadAllText(Path.Combine(Root, "interventions.json")));

        public static MechanismDatabaseFile LoadMechanisms() =>
            JsonUtility.FromJson<MechanismDatabaseFile>(File.ReadAllText(Path.Combine(Root, "mechanisms.json")));

        public IEnumerable<InjuryDefinition> All => _byId.Values;

        public static ScenarioDefinition LoadScenario(string scenarioId)
        {
            string json = File.ReadAllText(Path.Combine(Root, "scenarios", scenarioId + ".json"));
            return JsonUtility.FromJson<ScenarioDefinition>(json);
        }

        public InjuryDefinition Get(string id) =>
            _byId.TryGetValue(id, out var d) ? d : throw new KeyNotFoundException($"Verletzung '{id}' nicht in DB");

        /// <summary>Übersetzt eine Szenario-Verletzung in Engine-Events (eine Verletzung kann mehrere Effekte haben).</summary>
        public IEnumerable<InjuryEvent> ToEvents(ScenarioInjury si)
        {
            var def = Get(si.injuryId);
            foreach (var e in def.effects)
            {
                if (!Enum.TryParse(e.effectType, out InjuryEffectType type))
                {
                    Debug.LogWarning($"[DB] Unbekannter Effekttyp '{e.effectType}' bei '{def.id}'");
                    continue;
                }
                yield return new InjuryEvent
                {
                    InjuryId = def.id,
                    Effect = type,
                    Compartment = e.compartment,
                    Severity = si.severity,
                    BaseRate_mL_min = e.baseRate_mL_min,
                    MaxTotalLoss_mL = e.maxTotalLoss_mL,
                    ProgressionRate_per_min = e.progressionRate_per_min,
                    InitialLevel = e.initialLevel
                };
            }
        }
    }
}
