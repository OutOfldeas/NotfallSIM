namespace NotfallSIM.Core
{
    /// <summary>
    /// Zustandsereignisse, die der SimulationManager aus den Vitalwerten ableitet und protokolliert.
    /// Bewertungskriterien können sich darauf beziehen (z. B. "keine Hypotonie" oder "Intubation nach GCS ≤ 8").
    /// </summary>
    public static class StateTags
    {
        public const string Hypotension = "state:hypotension"; // RRsys < 90, je Episode
        public const string Hypoxia = "state:hypoxia";         // SpO2 < 90, je Episode
        public const string GcsDrop = "state:gcs13";           // GCS erstmals <= 13
        public const string Gcs8 = "state:gcs8";               // GCS erstmals <= 8
        public const string Anisocoria = "state:anisocoria";   // erstmals Pupillendifferenz

        public static readonly string[] All = { Hypotension, Hypoxia, GcsDrop, Gcs8, Anisocoria };
    }
}
