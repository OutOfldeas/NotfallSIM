using NotfallSIM.Simulation;
using TMPro;
using UnityEngine;

namespace NotfallSIM.UI
{
    /// <summary>
    /// Einfacher Patientenmonitor: Textfelder (TextMeshPro) auf einem Canvas.
    /// Werte unter/über Alarmgrenzen werden rot eingefärbt.
    /// </summary>
    public class VitalsMonitor : MonoBehaviour
    {
        [SerializeField] SimulationManager sim;
        [SerializeField] TMP_Text heartRate, bloodPressure, respRate, spo2, etco2, simTime, status;
        [SerializeField] float refreshInterval_s = 0.5f;  // echte Monitore aktualisieren auch nicht pro Frame

        [Header("Alarmgrenzen (Erwachsene, typische Monitor-Defaults)")]
        [SerializeField] float hrHigh = 120, hrLow = 50, sysLow = 90, spo2Low = 90;

        static readonly Color Normal = new Color(0.3f, 1f, 0.4f), Alarm = new Color(1f, 0.25f, 0.25f);
        float _timer;

        public void Configure(SimulationManager simulation, TMP_Text hr, TMP_Text bp, TMP_Text rr,
                              TMP_Text sat, TMP_Text co2, TMP_Text time, TMP_Text statusText)
        {
            sim = simulation;
            heartRate = hr; bloodPressure = bp; respRate = rr;
            spo2 = sat; etco2 = co2; simTime = time; status = statusText;
        }

        void Update()
        {
            _timer += Time.deltaTime;
            if (_timer < refreshInterval_s || sim.Engine == null) return;
            _timer = 0;

            var v = sim.Vitals;
            if (v.CardiacArrest)
            {
                Set(heartRate, "---", true); Set(bloodPressure, "---/---", true);
                Set(respRate, "---", true); Set(spo2, "---", true);
                Set(etco2, $"{v.EtCO2_mmHg:F0}", true);
                Set(status, "ASYSTOLIE / PEA", true);
            }
            else
            {
                Set(heartRate, $"{v.HeartRate_bpm:F0}", v.HeartRate_bpm > hrHigh || v.HeartRate_bpm < hrLow);
                Set(bloodPressure, $"{v.SystolicBP_mmHg:F0}/{v.DiastolicBP_mmHg:F0} ({v.MeanArterialPressure_mmHg:F0})",
                    v.SystolicBP_mmHg < sysLow);
                Set(respRate, $"{v.RespirationRate_bpm:F0}", v.RespirationRate_bpm > 30);
                Set(spo2, $"{v.SpO2_percent:F0}", v.SpO2_percent < spo2Low);
                Set(etco2, $"{v.EtCO2_mmHg:F0}", v.EtCO2_mmHg < 25);
                // Debug-Anzeige — im fertigen Spiel ausblenden, der Spieler soll es ja herausfinden.
                Set(status, $"Blutverlust {v.BloodLossFraction:P0}" + (v.Intubated ? " · beatmet" : ""), false);
            }
            int t = (int)v.SimTime_s;
            if (simTime) simTime.text = $"{t / 60:00}:{t % 60:00}";
        }

        static void Set(TMP_Text field, string text, bool alarm)
        {
            if (!field) return;
            field.text = text;
            field.color = alarm ? Alarm : Normal;
        }
    }
}
