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

        /// <summary>Glasgow Coma Scale 3–15 (Mock: aus SHT-Schwere und Schockklasse).</summary>
        public double GCS;
        /// <summary>Pupillendifferenz als Herniationszeichen.</summary>
        public bool Anisocoria;
        public bool Intubated;

        /// <summary>Labor (BGA): Base Excess in mmol/L (negativ = Basendefizit) und Laktat in mmol/L.</summary>
        public double BaseExcess;
        public double Lactate;
        public double Hb;              // g/dL
        public double pH;
        public double PaCO2;           // mmHg
        public double PaO2;            // mmHg
        public double HCO3;            // mmol/L
        public double Potassium;       // mmol/L
        public double IonizedCalcium;  // mmol/L
        public double Glucose;         // mg/dL
        public double Temperature_C;   // Körperkerntemperatur
        public double FactorActivity;  // Gerinnungsfaktoren relativ (1 = normal; Quick ≈ 100 · Wert)
        public double Fibrinogen_g_L;
        public double Coagulation;     // wirksame Gerinnung 0..1 (Faktoren, Fibrinogen, Temperatur, pH, Calcium)

        public override string ToString() =>
            $"t={SimTime_s:F0}s HF={HeartRate_bpm:F0} RR={SystolicBP_mmHg:F0}/{DiastolicBP_mmHg:F0} " +
            $"AF={RespirationRate_bpm:F0} SpO2={SpO2_percent:F0}% etCO2={EtCO2_mmHg:F0} " +
            $"BV={BloodVolume_mL:F0}mL Verlust={BloodLossFraction:P0} GCS={GCS:F0}" + (Anisocoria ? " aniso" : "") +
            (Intubated ? " ITN" : "") + (CardiacArrest ? " [STILLSTAND]" : "");
    }
}
