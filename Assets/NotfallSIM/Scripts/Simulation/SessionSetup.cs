using NotfallSIM.Core;

namespace NotfallSIM.Simulation
{
    /// <summary>Was im Hauptmenü gewählt wurde. Gilt für den nächsten Start der Schockraum-Szene.</summary>
    public static class SessionSetup
    {
        public static bool Active;
        public static string ScenarioId;      // festes Szenario oder null
        public static string RandomMechanism; // Zufallsfall aus diesem Mechanismus oder null
        public static PhysiologyMode Mode = PhysiologyMode.Textbook;
        public static bool CtInTraumaRoom;

        public const string MainMenuScene = "Hauptmenue";
        public const string TraumaRoomScene = "Schockraum";
        public const string CrashScene = "UnfallSandbox";
    }
}
