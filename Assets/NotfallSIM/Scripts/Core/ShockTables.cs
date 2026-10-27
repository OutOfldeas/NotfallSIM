namespace NotfallSIM.Core
{
    public enum PhysiologyMode
    {
        /// <summary>ATLS-Lehrbuchbild: klare Stufen je Schockklasse.</summary>
        Textbook,
        /// <summary>An Registerdaten angelehnt: kaum Tachykardie, späte Hypotonie, individuelle Streuung.</summary>
        Registry
    }

    /// <summary>
    /// Stützstellen relativer Blutverlust -> erwartete Werte, für beide Modi.
    /// Wird von der Mock-Engine und vom Debriefing (Modusvergleich) genutzt. Reines C#.
    /// </summary>
    public static class ShockTables
    {
        public static readonly double[] LossPoints = { 0.00, 0.15, 0.30, 0.40, 0.50, 0.58 };

        // Lehrbuch: ATLS Student Course Manual, 10th ed. (2018), Tabelle der Schockklassen.
        // Zwischenwerte eigene Interpolation (Entwurf).
        static readonly double[] TextbookHr  = { 75,  98,  118, 138, 150, 110 };
        static readonly double[] TextbookSbp = { 120, 120, 112, 88,  70,  50  };
        static readonly double[] TextbookDbp = { 78,  84,  82,  62,  45,  30  };
        static readonly double[] TextbookRr  = { 14,  18,  26,  34,  40,  40  };

        // Register: Mittelwerte bei Klinikaufnahme je Base-Deficit-Klasse aus dem TraumaRegister DGU
        // (Mutschler et al., Crit Care 2013;17:R42): HF 86/90/96/97, RRsys 133/125/113/95.
        // Die BD-Klassen sind hier über die ATLS-10-Zuordnung (BE 0..-2 / -2..-6 / -6..-10 / < -10)
        // als Annäherung auf die Blutverlustklassen abgebildet; Werte jenseits von 45 % sind extrapoliert.
        static readonly double[] RegistryHr  = { 84,  88,  92,  96,  97,  90  };
        static readonly double[] RegistrySbp = { 135, 129, 119, 105, 92,  70  };
        static readonly double[] RegistryDbp = { 80,  78,  74,  66,  56,  42  };
        static readonly double[] RegistryRr  = { 15,  17,  20,  24,  28,  30  };

        // Für beide Modi gleich.
        public static readonly double[] EtCo2 = { 38, 37, 33, 27, 20, 14 };
        public static readonly double[] SpO2Air = { 97, 97, 96, 95, 92, 88 };
        // Base Excess in mmol/L: ATLS 10th ed. nennt je Klasse 0 bis -2 / -2 bis -6 / -6 bis -10 / < -10.
        public static readonly double[] BaseExcess = { 0, -2, -6, -10, -14, -18 };
        // Laktat in mmol/L: didaktische Setzung, steigt mit dem Basendefizit (nicht aus Daten abgeleitet).
        public static readonly double[] Lactate = { 1.0, 1.8, 3.2, 4.8, 6.8, 9.0 };

        public static double Hr(PhysiologyMode m, double loss) => Interp(loss, m == PhysiologyMode.Registry ? RegistryHr : TextbookHr);
        public static double Sbp(PhysiologyMode m, double loss) => Interp(loss, m == PhysiologyMode.Registry ? RegistrySbp : TextbookSbp);
        public static double Dbp(PhysiologyMode m, double loss) => Interp(loss, m == PhysiologyMode.Registry ? RegistryDbp : TextbookDbp);
        public static double Rr(PhysiologyMode m, double loss) => Interp(loss, m == PhysiologyMode.Registry ? RegistryRr : TextbookRr);

        public static double Interp(double x, double[] table) => Interp(x, LossPoints, table);

        public static double Interp(double x, double[] points, double[] table)
        {
            if (x <= points[0]) return table[0];
            for (int i = 1; i < points.Length; i++)
            {
                if (x <= points[i])
                {
                    double t = (x - points[i - 1]) / (points[i] - points[i - 1]);
                    return table[i - 1] + t * (table[i] - table[i - 1]);
                }
            }
            return table[table.Length - 1];
        }
    }
}
