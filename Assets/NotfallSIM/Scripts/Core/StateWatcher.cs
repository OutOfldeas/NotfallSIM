using System.Collections.Generic;

namespace NotfallSIM.Core
{
    /// <summary>
    /// Leitet aus den Vitalwerten Zustandsereignisse ab (siehe <see cref="StateTags"/>).
    /// Hypotonie und Hypoxie zählen je Episode (mit Hysterese), GCS-Schwellen und Anisokorie nur einmal.
    /// Reines C#, testbar.
    /// </summary>
    public class StateWatcher
    {
        bool _hypotension, _hypoxia, _gcs13, _gcs8, _aniso, _mapLowTbi;

        public IEnumerable<(string message, string tag)> Check(VitalSigns v)
        {
            if (v.CardiacArrest) yield break;

            if (!_hypotension && v.SystolicBP_mmHg < 90) { _hypotension = true; yield return ("Hypotonie (RRsys < 90 mmHg)", StateTags.Hypotension); }
            else if (_hypotension && v.SystolicBP_mmHg >= 95) _hypotension = false;

            if (!_hypoxia && v.SpO2_percent < 90) { _hypoxia = true; yield return ("Hypoxie (SpO2 < 90 %)", StateTags.Hypoxia); }
            else if (_hypoxia && v.SpO2_percent >= 92) _hypoxia = false;

            if (!_gcs13 && v.GCS > 0 && v.GCS <= 13) { _gcs13 = true; yield return ($"Bewusstseinsminderung (GCS {v.GCS:0})", StateTags.GcsDrop); }
            if (!_gcs8 && v.GCS > 0 && v.GCS <= 8) { _gcs8 = true; yield return ($"Bewusstlos (GCS {v.GCS:0})", StateTags.Gcs8); }
            if (!_aniso && v.Anisocoria) { _aniso = true; yield return ("Anisokorie", StateTags.Anisocoria); }

            // Europäische Blutungsleitlinie (Rossaint 2023): bei schwerem SHT (GCS <= 8) MAD >= 80 mmHg.
            bool severeTbi = v.GCS > 0 && v.GCS <= 8;
            if (!_mapLowTbi && severeTbi && v.MeanArterialPressure_mmHg < 80) { _mapLowTbi = true; yield return ("MAD unter 80 mmHg bei schwerem SHT", StateTags.MapLowTbi); }
            else if (_mapLowTbi && v.MeanArterialPressure_mmHg >= 85) _mapLowTbi = false;
        }
    }
}
