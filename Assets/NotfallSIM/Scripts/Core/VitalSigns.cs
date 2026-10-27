namespace NotfallSIM.Core
{
    /// <summary>
    /// Momentaufnahme der Vitalparameter, die der Monitor anzeigt.
    /// Bewusst engine-unabhängig: Mock und Pulse liefern dasselbe Format.
    /// </summary>
    public struct VitalSigns
    {
        public double SimTime_s;
        public double HeartRate_bpm;
        public double SystolicBP_mmHg;
        public double DiastolicBP_mmHg;
        public double MeanArterialPressure_mmHg;
        public double RespirationRate_bpm;
        public double SpO2_percent;
        public double EtCO2_mmHg;
        public double BloodVolume_mL;

        /// <summary>Anteil des verlorenen Blutvolumens (0..1), Basis für Schockklassen.</summary>
        public double BloodLossFraction;

        /// <summary>True, wenn der Patient kreislaufstillständig ist (Reanimationspflicht).</summary>
        public bool CardiacArrest;

        public override string ToString() =>
            $"t={SimTime_s:F0}s HF={HeartRate_bpm:F0} RR={SystolicBP_mmHg:F0}/{DiastolicBP_mmHg:F0} " +
            $"AF={RespirationRate_bpm:F0} SpO2={SpO2_percent:F0}% etCO2={EtCO2_mmHg:F0} " +
            $"BV={BloodVolume_mL:F0}mL Verlust={BloodLossFraction:P0}" + (CardiacArrest ? " [STILLSTAND]" : "");
    }
}
