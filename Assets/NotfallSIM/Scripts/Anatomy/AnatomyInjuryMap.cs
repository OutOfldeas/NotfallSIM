using System;

namespace NotfallSIM.Anatomy
{
    /// <summary>Welche Anatomie-Meshes zu einer Verletzung gehören (anatomy/anatomy.json) — für die Hervorhebung im Röntgenblick.</summary>
    [Serializable]
    public class AnatomyInjuryEntry
    {
        public string injuryId;
        public string[] meshes; // reguläre Ausdrücke auf die englischen FMA-Namen
        public string note;
    }

    [Serializable]
    public class AnatomyInjuryMapFile
    {
        public string version;
        public AnatomyInjuryEntry[] injuries;
    }
}
