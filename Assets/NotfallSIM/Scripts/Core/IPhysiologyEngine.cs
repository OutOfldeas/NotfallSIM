namespace NotfallSIM.Core
{
    /// <summary>
    /// Zentrale Schnittstelle zwischen Spiel und Physiologie.
    /// Alles im Spiel spricht nur mit diesem Interface, nie direkt mit Pulse.
    /// So lässt sich die Engine austauschen (Mock zum Prototypen, Pulse für Realismus)
    /// und beide Ergebnisse direkt gegeneinander validieren.
    /// </summary>
    public interface IPhysiologyEngine
    {
        string Name { get; }
        bool Initialize(PatientProfile patient);
        /// <summary>Rückt die Simulationszeit vor und aktualisiert <see cref="Current"/>.</summary>
        void Advance(double dt_s);
        void ApplyInjury(InjuryEvent injury);
        void ApplyIntervention(InterventionEvent intervention);
        VitalSigns Current { get; }
    }
}
