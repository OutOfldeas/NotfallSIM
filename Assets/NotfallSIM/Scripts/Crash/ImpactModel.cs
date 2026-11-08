using System;
using System.Collections.Generic;
using System.Linq;

namespace NotfallSIM.Crash
{
    // ---------- impact_model.json ----------
    [Serializable] public class AreaStiffness { public double area_cm2, k_N_m; }

    /// <summary>Kontaktparameter einer Körperregion (nachgiebiger Kontakt statt starrer Kollision).</summary>
    [Serializable]
    public class RegionContact
    {
        public string region;            // "Kopf", "Thorax", "Abdomen", "Becken", "Oberschenkel", "Unterschenkel", "Arm"
        public double k1_N_m, k2_N_m, knee1_m, knee2_m;
        public AreaStiffness[] areaStiffness;
        public double restitution, effectiveMass_kg, depth_m, maxArea_cm2;
        public string evidence, sourceId, note;
    }

    [Serializable] public class SkullPoint { public double area_cm2, force50_N; }
    [Serializable] public class SkullFracture { public SkullPoint[] points; public double cv; public string injuryId, sourceId, note; }
    [Serializable] public class LongBone { public string segment; public double moment50_Nm, cv; public string injuryRight, injuryLeft, sourceId, note; }
    [Serializable] public class RibFracture { public double aisIntercept, aisSlope; public string injuryRight, injuryLeft, sourceId, note; }
    [Serializable] public class FractureModel { public SkullFracture skull; public LongBone[] longBones; public RibFracture ribs; }

    [Serializable] public class SkinPenetration { public double intercept_N, perTipRadius_um, perTipAngle_deg, perCuttingAngle_deg, minimum_N, clothingFactor; public string sourceId, note; }
    [Serializable] public class TissueResistance { public string id; public double resistance_N; public string evidence, note; }
    [Serializable] public class PenetrationRegion
    {
        public string region; public double wall_m, boneHitProbability; public string deep, deepRight, deepLeft; public double probability; public string note;
        public string boneTissue;        // Gewebe der knöchernen Wand ("rippe", "schaedel"); leer = "knochen"
        public double boneThickness_m;   // Dicke der knöchernen Schicht vor dem Ende der Wand (0 = 1 cm)
    }
    [Serializable] public class PenetrationModel { public SkinPenetration skin; public TissueResistance[] tissues; public PenetrationRegion[] regions; }

    /// <summary>Schnitt: Klinge gleitet unter Andruck über die Haut.</summary>
    [Serializable] public class CutRule { public string segmentPrefix; public double vesselDepth_m, minLength_m; public string injuryRight, injuryLeft, note; }
    [Serializable]
    public class CuttingModel
    {
        public double smoothLoad_N, serratedLoad_N;   // Andruck, ab dem die Haut beim Ziehen durchtrennt wird
        public double depthPerN_m, maxDepth_m;       // Schnitttiefe je N über der Schwelle (Annahme), Obergrenze
        public double minSlide_m_s;                  // Mindestgleiten der Klinge
        public CutRule[] rules;
        public string sourceId, note;
    }

    [Serializable] public class ImpactSource { public string id, citation, url, note; }

    // ---------- vehicle_model.json ----------
    /// <summary>Nachgiebiges Fahrzeugteil (Fußgängerfront): Steifigkeit in Reihe zum Körper, Federweg bis zum Anschlag.</summary>
    [Serializable] public class VehiclePart { public string part; public double stiffness_N_m, clearance_m; public string evidence, sourceId, note; }
    /// <summary>Seitenaufprall als Feder-Masse-Modell: stoßendes und getroffenes Fahrzeug, Front und Seite in Reihe.</summary>
    [Serializable]
    public class SideImpactModel
    {
        public double bulletMass_kg, struckMass_kg, frontStiffness_N_m, sideStiffness_N_m, unloadFactor, doorLag_s, maxIntrusion_m;
        public string sourceId, note;
    }
    /// <summary>Frontalaufprall: Masse und Frontsteifigkeit (Halbsinus-Puls), Knautschlänge, ab der der Innenraum eindrückt.</summary>
    [Serializable]
    public class FrontalImpactModel
    {
        public double mass_kg, stiffness_N_m, crushZone_m, intrusionPerCrush, maxIntrusion_m;
        public string sourceId, note;
    }
    /// <summary>Frontalaufprall gegen die Barriere: Pulsdauer und Eindringen des Innenraums (reines C#).</summary>
    public static class FrontalImpact
    {
        /// <summary>Pulsdauer T = π·√(m/k) (s).</summary>
        public static double Pulse_s(FrontalImpactModel m) => Math.PI * Math.Sqrt(m.mass_kg / m.stiffness_N_m);
        /// <summary>Knautschweg am Ende des Halbsinus-Pulses: v·T/2 (m).</summary>
        public static double Crush_m(FrontalImpactModel m, double v_m_s) => v_m_s * Pulse_s(m) / 2;
        /// <summary>Eindringen von Fußraum/Armaturenbrett (m) bei einem Knautschweg: über die Knautschzone hinaus anteilig, gedeckelt.</summary>
        public static double Intrusion_m(FrontalImpactModel m, double crush_m) =>
            Math.Min(m.maxIntrusion_m, Math.Max(0, crush_m - m.crushZone_m) * m.intrusionPerCrush);
    }

    /// <summary>
    /// Zustand des Seitenaufprall-Modells (reines C#, im Crash-Rig und im Test gleich): Kraft F = k·c beim Belasten
    /// (k = Front und Seite in Reihe), Entlastung unloadFactor-fach steifer bis zur bleibenden Verformung.
    /// Die Tür folgt dem Anteil der Seitenstruktur am größten Knautschweg (verzögert um doorLag_s, höchstens maxIntrusion_m).
    /// </summary>
    public class SideImpactState
    {
        readonly SideImpactModel _m;
        readonly double _k, _share;
        public double BulletVelocity, StruckVelocity, Crush, MaxCrush, Force, Door, DoorVelocity, MaxDoorVelocity;
        /// <summary>Anteil der Seitenstruktur am Knautschweg (Rest: Front des Gegners).</summary>
        public double SideShare => _share;
        double _fMax;
        public SideImpactState(SideImpactModel m, double impactSpeed_m_s)
        {
            _m = m;
            double kf = m.frontStiffness_N_m, ks = m.sideStiffness_N_m;
            _k = kf * ks / (kf + ks); _share = kf / (kf + ks);
            BulletVelocity = impactSpeed_m_s;
        }
        public void Step(double dt)
        {
            if (Crush >= MaxCrush) { Force = _k * Crush; MaxCrush = Crush; _fMax = Force; }
            else
            {
                double kU = _k * Math.Max(1, _m.unloadFactor), cPerm = MaxCrush - _fMax / kU;
                Force = Math.Max(0, kU * (Crush - cPerm));
            }
            BulletVelocity -= Force / _m.bulletMass_kg * dt;
            StruckVelocity += Force / _m.struckMass_kg * dt;
            Crush += (BulletVelocity - StruckVelocity) * dt;
            double target = Math.Min(_m.maxIntrusion_m, _share * MaxCrush);
            double prev = Door;
            Door += (target - Door) * Math.Min(1, dt / Math.Max(1e-6, _m.doorLag_s));
            DoorVelocity = (Door - prev) / dt + StruckVelocity;   // Tür gegenüber dem Boden (Insasse steht anfangs)
            MaxDoorVelocity = Math.Max(MaxDoorVelocity, DoorVelocity);
        }
    }

    [Serializable]
    public class MotorcycleModel
    {
        public double mass_kg, pitchInertia_kgm2, wheelbase_m, wheelRadius_m, comForward_m, comHeight_m;
        public FaceCurve fork;
        public double unloadFactor, tyreStiffness_N_m, tyreDamping_Ns_m, slideFriction;
        public string sourceId, evidence, note;
    }

    /// <summary>Anprallkonfiguration (ISO 13232-2, Tabelle 1, bzw. Alleinsturz).</summary>
    [Serializable]
    public class MotorcycleConfig
    {
        public string id, label;
        public double carSpeed_m_s, bikeSpeed_m_s, heading_deg, offset_m;
        /// <summary>side: Motorrad trifft PKW-Seite; front: PKW-Front; bikeside: PKW-Front trifft Motorradseite; none: Alleinsturz.</summary>
        public string contact;
    }

    [Serializable]
    public class VehicleModelFile
    {
        public string version, note;
        public FrontalImpactModel frontal;
        public SideImpactModel side;
        public VehiclePart[] pedestrianFront;
        /// <summary>Fahrzeugtypen für frei gestellte Unfälle (<see cref="TrafficSim"/>).</summary>
        [System.Runtime.Serialization.OptionalField] public VehicleType[] vehicleTypes;
        [System.Runtime.Serialization.OptionalField] public MotorcycleModel motorcycle;
        [System.Runtime.Serialization.OptionalField] public MotorcycleConfig[] motorcycleConfigs;
        public ImpactSource[] sources;
    }

    /// <summary>Unterhautfett einer Region: Dicke beim Bezugs-BMI (Mann), Zunahme je BMI-Punkt, Zuschlag für Frauen.</summary>
    [Serializable] public class FatRegion { public string region; public double ref_mm, perBmi_mm, female_mm; public string sourceId, note; }
    /// <summary>Fettgewebe: Dicke je Region, dehnratenabhängiger Elastizitätsmodul (Potenzgesetz durch zwei Messpunkte), Stichwiderstand.</summary>
    [Serializable]
    public class FatModel
    {
        public double refBmi;
        public double rate1_per_s, modulus1_Pa, rate2_per_s, modulus2_Pa, minModulus_Pa, maxModulus_Pa;
        public FatRegion[] regions;
        public string modulusSourceId, note;
    }

    [Serializable]
    public class ImpactModelFile
    {
        public string version, note;
        public RegionContact[] regions;
        public FractureModel fractures;
        public PenetrationModel penetration;
        public CuttingModel cutting;
        public FatModel fat;
        public ImpactSource[] sources;
    }

    // ---------- objects.json ----------
    [Serializable]
    public class SandboxObject
    {
        public string id, label, kind, mode, shape;   // kind: stumpf | spitz; mode: werfen | gefuehrt | statisch
        public float[] size_m;
        public double mass_kg, contactArea_cm2, stiffness_N_m, tipRadius_um, tipAngle_deg, cuttingAngle_deg, bladeLength_m;
        public double defaultSpeed_m_s, minSpeed_m_s, maxSpeed_m_s;
        public float[] color;
        public string evidence, sourceId, note;
        public string edge;               // Schneide: "glatt", "wellen" oder leer (keine)
        public double supportHeight_m;    // statisch: Höhe der Oberseite über dem Boden, mit Beinen (Tisch); 0 = liegt auf
        public bool Sharp => kind == "spitz";
        public bool HasEdge => !string.IsNullOrEmpty(edge);
        [System.Runtime.Serialization.OptionalField] public double clearance_m;   // Federweg bis zum starren Anschlag (0 = keiner)
    }

    [Serializable] public class SandboxObjectFile { public string version, note; public SandboxObject[] objects; public ImpactSource[] sources; }

    /// <summary>Verletzung, die direkt aus einem Aufprall folgt (Fraktur, Stichkanal), mit Wahrscheinlichkeit und Schweregrad.</summary>
    public class DirectInjury
    {
        public string InjuryId;
        public double Probability, Severity;
        public string Reason;
        public override string ToString() => $"{InjuryId} {Probability:P0} ({Reason})";
    }

    /// <summary>Stärkster stumpfer Kontakt einer Region (aus den Messaufnehmern).</summary>
    public class BluntContact
    {
        public string Region;          // Region wie in der Anatomie ("Kopf", "Oberschenkel rechts" …)
        public string Segment;         // Segmentname ("Oberschenkel rechts", "Unterarm links" …)
        public double PeakForce_N, Area_cm2, MaxIndentation_m, SegmentLength_m;
        public int Side;               // −1 links, +1 rechts, 0 Mitte
        public string Surface;         // Fläche ("ground", "object:hammer" …), nur zur Anzeige
    }

    /// <summary>Ergebnis eines eingedrungenen spitzen Gegenstands.</summary>
    public class PenetrationResult
    {
        public string Region, Segment;
        public int Side;
        public double Depth_m;
        public bool HitBone;
        public string ObjectId;
        public bool Embedded;          // steckt noch (Pfählung, Messer nicht zurückgezogen)
        public bool BonePierced;       // Knochen getroffen und durchstoßen (z. B. Rippe)
        public double FatExtra_m;      // Unterhautfett über dem Bezugs-BMI: verlängert den Weg bis hinter die Wand
        public float[] ModelPoint, ModelInside;   // Einstich im Modellraum (für die Wundmarke), sonst null
    }

    /// <summary>Schnitt einer Klinge über die Haut (Länge des Gleitwegs unter Andruck, geschätzte Tiefe).</summary>
    public class CutResult
    {
        public string Region, Segment, ObjectId;
        public int Side;
        public double Length_m, Depth_m, PeakLoad_N;
        public float[] ModelStart, ModelEnd, ModelInside;   // Anfang/Ende im Modellraum (für die Wundmarke), sonst null
    }

    /// <summary>
    /// Kontaktgesetz und Verletzungsschwellen für stumpfe und spitze Gewalt. Reines C#, testbar.
    /// </summary>
    public static class ImpactModel
    {
        /// <summary>Region des Kontaktmodells zu einer Körperregion bzw. einem Segment.</summary>
        public static string ContactRegion(string region)
        {
            if (string.IsNullOrEmpty(region)) return "Thorax";
            if (region.StartsWith("Oberschenkel")) return "Oberschenkel";
            if (region.StartsWith("Unterschenkel")) return "Unterschenkel";
            if (region.StartsWith("Arm") || region.StartsWith("Oberarm") || region.StartsWith("Unterarm")) return "Arm";
            return region;
        }

        public static RegionContact Region(ImpactModelFile m, string region)
        {
            string key = ContactRegion(region);
            return m.regions.FirstOrDefault(r => r.region == key) ?? m.regions.First(r => r.region == "Thorax");
        }

        static double LogInterp(double x, double x0, double y0, double x1, double y1)
        {
            x = Math.Max(x0, Math.Min(x1, x));
            double t = Math.Log(x / x0) / Math.Log(x1 / x0);
            return y0 * Math.Pow(y1 / y0, t);
        }

        /// <summary>Steifigkeit der Region bei Eindrücktiefe δ (stückweise linear) und Kontaktfläche (nur Kopf).</summary>
        public static double Stiffness(RegionContact r, double delta_m, double area_cm2)
        {
            double k1 = r.k1_N_m, k2 = r.k2_N_m;
            if (r.areaStiffness != null && r.areaStiffness.Length >= 2)
            {
                // ohne LINQ: läuft pro Kontaktpunkt auf den Physik-Threads (Stützstellen aufsteigend nach Fläche, im Test geprüft)
                var a = r.areaStiffness;
                double area = Math.Min(area_cm2 > 0 ? area_cm2 : r.maxArea_cm2, r.maxArea_cm2);
                double k = LogInterp(area, a[0].area_cm2, a[0].k_N_m, a[a.Length - 1].area_cm2, a[a.Length - 1].k_N_m);
                k2 *= k / k1; k1 = k;
            }
            if (delta_m <= r.knee1_m) return k1;
            if (delta_m >= r.knee2_m) return k2;
            return k1 + (k2 - k1) * (delta_m - r.knee1_m) / (r.knee2_m - r.knee1_m);
        }

        /// <summary>Dämpfungsgrad aus der Restitution (Feder-Dämpfer-Stoß): ζ = −ln e / √(π² + ln² e).</summary>
        public static double DampingRatio(double restitution)
        {
            double e = Math.Max(0.01, Math.Min(0.99, restitution)), l = Math.Log(e);
            return -l / Math.Sqrt(Math.PI * Math.PI + l * l);
        }

        /// <summary>
        /// Kontaktkraft (nur Druck): F = k·δ + c·δ̇, k = Körper in Reihe mit dem Gegenstand (0 = starr), c = 2ζ√(k·m).
        /// m = reduzierte Masse aus Region und Gegenstand (0 = unendlich schwer, z. B. Boden).
        /// </summary>
        public static double Force(RegionContact r, double delta_m, double closing_m_s, double area_cm2, double objectStiffness_N_m, double objectMass_kg)
        {
            if (delta_m <= 0 && closing_m_s <= 0) return 0;
            double kb = Stiffness(r, Math.Max(0, delta_m), area_cm2);
            double k = objectStiffness_N_m > 0 ? 1 / (1 / kb + 1 / objectStiffness_N_m) : kb;
            double mb = r.effectiveMass_kg, m = objectMass_kg > 0 ? mb * objectMass_kg / (mb + objectMass_kg) : mb;
            double c = 2 * DampingRatio(r.restitution) * Math.Sqrt(k * m);
            return Math.Max(0, k * Math.Max(0, delta_m) + c * closing_m_s);
        }

        /// <summary>
        /// Stoß eines Gegenstands (Masse m, Geschwindigkeit v) auf die Region, eindimensional, Körperseite mit der wirksamen
        /// Masse der Region (frei beweglich). Liefert Spitzenkraft, größte Eindrückung und Kontaktdauer. Für Tests und Doku.
        /// </summary>
        public static (double peakForce_N, double maxDelta_m, double duration_s) Simulate(RegionContact r, double objectMass_kg, double v0, double area_cm2, double objectStiffness_N_m = 0, double dt = 1e-6)
        {
            double mo = objectMass_kg, mb = r.effectiveMass_kg;
            double xo = 0, vo = v0, xb = 0, vb = 0, peak = 0, maxD = 0, t = 0;
            bool started = false;
            while (t < 0.5)
            {
                double d = xo - xb, closing = vo - vb;
                double f = d > 0 || closing > 0 ? Force(r, d, closing, area_cm2, objectStiffness_N_m, mo) : 0;
                if (d <= 0 && started && closing <= 0) break;
                if (d > 0) started = true;
                peak = Math.Max(peak, f); maxD = Math.Max(maxD, d);
                vo -= f / mo * dt; vb += f / mb * dt;
                xo += vo * dt; xb += vb * dt;
                t += dt;
            }
            return (peak, maxD, t);
        }

        // ---------- Frakturen

        static double Phi(double z) => Biomechanics.NormalCdf(z);

        /// <summary>Schädelfraktur: Bruchkraft wächst mit der Kontaktfläche (Allsop 1991), normalverteilt mit CV.</summary>
        public static double SkullFractureProbability(ImpactModelFile m, double force_N, double area_cm2)
        {
            var s = m.fractures.skull;
            var p = s.points.OrderBy(x => x.area_cm2).ToArray();
            double f50 = LogInterp(Math.Max(1, area_cm2), p[0].area_cm2, p[0].force50_N, p[p.Length - 1].area_cm2, p[p.Length - 1].force50_N);
            return force_N <= 0 ? 0 : Phi((force_N - f50) / (s.cv * f50));
        }

        /// <summary>Biegebruch eines Röhrenknochens bei Querkraft F mittig: M = F·L/4 gegen das 50-%-Moment (Kerrigan 2004).</summary>
        public static double LongBoneProbability(LongBone b, double force_N, double length_m)
        {
            double moment = force_N * length_m / 4;
            return force_N <= 0 ? 0 : Phi((moment - b.moment50_Nm) / (b.cv * b.moment50_Nm));
        }

        /// <summary>AIS des Thorax aus der Kompression (Kroell).</summary>
        public static double RibAis(ImpactModelFile m, double compression) => m.fractures.ribs.aisIntercept + m.fractures.ribs.aisSlope * compression;

        // ---------- Spitze Gewalt

        /// <summary>Kraft zum Durchstechen der Haut aus der Klingengeometrie (Ní Annaidh 2015), mit Kleidung höher.</summary>
        // ---------- Fettgewebe

        static FatRegion FatRule(ImpactModelFile m, string region)
        {
            if (m?.fat?.regions == null) return null;
            string key = ContactRegion(region);
            foreach (var f in m.fat.regions) if (f.region == key) return f;
            return null;
        }

        /// <summary>Dicke des Unterhautfetts (m) einer Region bei BMI und Geschlecht (mind. 1 mm; Kopf: 0).</summary>
        public static double FatThickness_m(ImpactModelFile m, string region, double bmi, bool male)
        {
            var f = FatRule(m, region);
            if (f == null) return 0;
            return Math.Max(1, f.ref_mm + f.perBmi_mm * (bmi - m.fat.refBmi) + (male ? 0 : f.female_mm)) / 1000;
        }

        /// <summary>Zusätzliches Fett gegenüber dem Bezugs-BMI (gleiches Geschlecht, m): verlängert die Körperwand im Stichkanal.</summary>
        public static double FatExtra_m(ImpactModelFile m, string region, double bmi, bool male) =>
            FatRule(m, region) == null ? 0 : FatThickness_m(m, region, bmi, male) - FatThickness_m(m, region, m.fat.refBmi, male);

        /// <summary>Elastizitätsmodul von Fettgewebe bei der Dehnrate (1/s): Potenzgesetz durch die beiden Messpunkte, begrenzt.</summary>
        public static double FatModulus_Pa(ImpactModelFile m, double strainRate_per_s)
        {
            var f = m.fat;
            if (f == null) return 0;
            double rate = Math.Max(1e-3, strainRate_per_s);
            double n = Math.Log(f.modulus2_Pa / f.modulus1_Pa) / Math.Log(f.rate2_per_s / f.rate1_per_s);
            double e = f.modulus1_Pa * Math.Pow(rate / f.rate1_per_s, n);
            return Math.Max(f.minModulus_Pa, Math.Min(f.maxModulus_Pa, e));
        }

        /// <summary>
        /// Kontaktkraft mit Fettschicht: Fett (k = E·A/Dicke, E nach Dehnrate) liegt in Reihe vor dem Gewebe und dem
        /// Gegenstand. <paramref name="tissueDelta_m"/>: Anteil der Eindrückung, der auf das Gewebe darunter entfällt
        /// (für Thoraxkompression und Rippen). Ohne Fett wie <see cref="Force(RegionContact,double,double,double,double,double)"/>.
        /// </summary>
        /// <summary>
        /// Wie unten, dazu ein Gegenstand mit begrenztem Federweg (<paramref name="objectClearance_m"/>, z. B. Motorhaube bis
        /// zum Motorblock): Ist der Federweg aufgebraucht, wirkt der Gegenstand starr, die Kraft steigt dann mit dem Gewebe.
        /// </summary>
        public static double Force(ImpactModelFile m, RegionContact r, double delta_m, double closing_m_s, double area_cm2,
                                   double objectStiffness_N_m, double objectMass_kg, double fat_m, double objectClearance_m, out double tissueDelta_m)
        {
            double f = Force(m, r, delta_m, closing_m_s, area_cm2, objectStiffness_N_m, objectMass_kg, fat_m, out tissueDelta_m);
            if (objectClearance_m <= 0 || objectStiffness_N_m <= 0) return f;
            double objectDefl = Math.Max(0, delta_m) - tissueDelta_m;
            // Anteil des Gegenstands an der Eindrückung: Gesamt minus Gewebe (ohne Fett-Anteil genau genug, Fett ist meist steif beim Stoß)
            if (objectDefl <= objectClearance_m) return f;
            double fStop = objectStiffness_N_m * objectClearance_m;
            double fRigid = Force(m, r, delta_m - objectClearance_m, closing_m_s, area_cm2, 0, objectMass_kg, fat_m, out double td);
            tissueDelta_m = td;
            return Math.Max(fStop, fRigid);
        }

        public static double Force(ImpactModelFile m, RegionContact r, double delta_m, double closing_m_s, double area_cm2,
                                   double objectStiffness_N_m, double objectMass_kg, double fat_m, out double tissueDelta_m)
        {
            tissueDelta_m = Math.Max(0, delta_m);
            if (fat_m <= 0 || m?.fat == null)
            {
                double kb0 = Stiffness(r, Math.Max(0, delta_m), area_cm2);
                if (objectStiffness_N_m > 0) tissueDelta_m = Math.Max(0, delta_m) * (1 / (1 / kb0 + 1 / objectStiffness_N_m)) / kb0;
                return Force(r, delta_m, closing_m_s, area_cm2, objectStiffness_N_m, objectMass_kg);
            }
            if (delta_m <= 0 && closing_m_s <= 0) return 0;
            double kFat = FatModulus_Pa(m, Math.Abs(closing_m_s) / fat_m) * area_cm2 * 1e-4 / fat_m;
            // Gewebesteifigkeit bei der Gewebe-Eindrückung (eine Fixpunkt-Iteration genügt: k ändert sich stückweise linear)
            double kb = Stiffness(r, Math.Max(0, delta_m), area_cm2), kSeries = 0;
            for (int i = 0; i < 2; i++)
            {
                double inv = 1 / kb + 1 / kFat + (objectStiffness_N_m > 0 ? 1 / objectStiffness_N_m : 0);
                kSeries = 1 / inv;
                tissueDelta_m = Math.Max(0, delta_m) * kSeries / kb;
                kb = Stiffness(r, tissueDelta_m, area_cm2);
            }
            double mb = r.effectiveMass_kg, mm = objectMass_kg > 0 ? mb * objectMass_kg / (mb + objectMass_kg) : mb;
            double c = 2 * DampingRatio(r.restitution) * Math.Sqrt(kSeries * mm);
            return Math.Max(0, kSeries * Math.Max(0, delta_m) + c * closing_m_s);
        }

        public static double BoneThickness(PenetrationRegion rule) => rule != null && rule.boneThickness_m > 0 ? rule.boneThickness_m : 0.01;
        public static string BoneTissue(PenetrationRegion rule) => rule != null && !string.IsNullOrEmpty(rule.boneTissue) ? rule.boneTissue : "knochen";

        /// <summary>
        /// Kontaktfläche aus der Geometrie des Gegenstands (cm²), statt einer festen Fläche je Gegenstand.
        /// <paramref name="localNormal"/>: Kontaktnormale im Koordinatensystem des Gegenstands (Längsachse z), Betrag egal.
        /// Kugel und Kapselende: Kugelkappe A = 2πrδ. Quader: Fläche, Kante (Länge × Breite) oder Ecke je nach Lage.
        /// Kapsel quer: Linienkontakt (Länge × Sehne 2√(2rδ)). Die Breite einer Kante wächst mit der Eindrücktiefe (Annahme).
        /// </summary>
        public static double GeometricArea(string shape, double sx, double sy, double sz, double nx, double ny, double nz, double delta_m)
        {
            double len = Math.Sqrt(nx * nx + ny * ny + nz * nz);
            if (len < 1e-9) { nx = 0; ny = 0; nz = 1; len = 1; }
            double ax = Math.Abs(nx / len), ay = Math.Abs(ny / len), az = Math.Abs(nz / len);
            double d = Math.Max(0.002, delta_m);
            const double maxLine = 0.15;   // tragende Länge höchstens etwa eine Segmentbreite
            double a;
            switch (shape)
            {
                case "sphere":
                {
                    double r = sx / 2;
                    a = Math.Min(Math.PI * r * r, 2 * Math.PI * r * d);
                    break;
                }
                case "capsule":
                {
                    double r = Math.Max(sx, sy) / 2, h = Math.Max(sz - 2 * r, 0);
                    double chord = 2 * Math.Sqrt(Math.Max(0, 2 * r * d - d * d));
                    if (az < 0.3 && h > 0) a = Math.Min(h, maxLine) * Math.Max(chord, 0.003);
                    else a = Math.Min(Math.PI * r * r, 2 * Math.PI * r * d);
                    break;
                }
                default: // box
                {
                    double w = 0.003 + 2 * d;   // Kante: Breite wächst mit der Eindrückung (Annahme)
                    var dims = new[] { sx, sy, sz }; var al = new[] { ax, ay, az };
                    int iMax = al[0] >= al[1] && al[0] >= al[2] ? 0 : al[1] >= al[2] ? 1 : 2;
                    int iMin = al[0] <= al[1] && al[0] <= al[2] ? 0 : al[1] <= al[2] ? 1 : 2;
                    if (al[iMax] > 0.95)
                    {
                        double f1 = dims[(iMax + 1) % 3], f2 = dims[(iMax + 2) % 3];
                        a = Math.Min(f1, maxLine) * Math.Min(f2, maxLine);       // Fläche
                    }
                    else if (al[iMin] < 0.2) a = Math.Min(dims[iMin], maxLine) * w;   // Kante entlang der Achse iMin
                    else a = w * w;                                                    // Ecke
                    break;
                }
            }
            return Math.Max(0.1, a * 1e4);
        }

        /// <summary>Andruck, ab dem eine Schneide beim Ziehen die Haut durchtrennt (N); 0 = keine Schneide.</summary>
        public static double CutLoad(ImpactModelFile m, SandboxObject o)
        {
            if (m.cutting == null || o == null || !o.HasEdge) return 0;
            return o.edge == "wellen" ? m.cutting.serratedLoad_N : m.cutting.smoothLoad_N;
        }

        /// <summary>Schnitttiefe aus dem größten Andruck über der Schwelle (Annahme: linear, begrenzt).</summary>
        public static double CutDepth(ImpactModelFile m, double peakLoad_N, double threshold_N)
        {
            if (m.cutting == null || peakLoad_N < threshold_N) return 0;
            return Math.Min(m.cutting.maxDepth_m, 0.001 + (peakLoad_N - threshold_N) * m.cutting.depthPerN_m);
        }

        public static double SkinPenetrationForce(ImpactModelFile m, SandboxObject o, bool clothed)
        {
            var s = m.penetration.skin;
            double f = s.intercept_N + s.perTipRadius_um * o.tipRadius_um + s.perTipAngle_deg * o.tipAngle_deg + s.perCuttingAngle_deg * o.cuttingAngle_deg;
            f = Math.Max(s.minimum_N, f);
            return clothed ? f * s.clothingFactor : f;
        }

        public static double TissueResistance(ImpactModelFile m, string tissue) =>
            m.penetration.tissues.FirstOrDefault(t => t.id == tissue)?.resistance_N ?? 25;

        public static PenetrationRegion PenetrationRule(ImpactModelFile m, string region)
        {
            string key = ContactRegion(region);
            return m.penetration.regions.FirstOrDefault(r => r.region == key);
        }

        /// <summary>
        /// Widerstand gegen die Klinge in Tiefe d (nach dem Hautdurchstich): Weichteil bis zur Wand, dort ggf. Knochen
        /// (wenn <paramref name="boneHit"/>), dahinter Organ.
        /// </summary>
        /// <summary>Widerstand mit zusätzlichem Unterhautfett (<paramref name="fatExtra_m"/>): erst Fett, dann die Wand wie beim Bezugs-BMI.</summary>
        public static double ResistanceAt(ImpactModelFile m, string region, double depth_m, bool boneHit, double fatExtra_m)
        {
            if (fatExtra_m > 0 && depth_m < fatExtra_m) return TissueResistance(m, "fett");
            return ResistanceAt(m, region, depth_m - Math.Max(0, fatExtra_m), boneHit);
        }

        public static double ResistanceAt(ImpactModelFile m, string region, double depth_m, bool boneHit)
        {
            var rule = PenetrationRule(m, region);
            double wall = rule != null ? rule.wall_m : 0.03;
            double bone = BoneThickness(rule);
            if (depth_m < wall - bone) return TissueResistance(m, "weichteil");
            if (depth_m < wall && boneHit) return TissueResistance(m, BoneTissue(rule));
            return depth_m < wall ? TissueResistance(m, "weichteil") : TissueResistance(m, "organ");
        }

        // ---------- Verletzungen aus den Aufprallen

        /// <summary>
        /// Direkte Verletzungen: Schädelfraktur (Kraft und Fläche), Röhrenknochen (Biegemoment), Rippen (Kompression nach Kroell),
        /// Stichkanäle (Tiefe hinter der Körperwand). <paramref name="draw"/> liefert Zufallszahlen 0..1 (reproduzierbar).
        /// </summary>
        public static List<DirectInjury> Evaluate(ImpactModelFile m, IEnumerable<BluntContact> blunt, IEnumerable<PenetrationResult> penetrations, Func<double> draw)
            => Evaluate(m, blunt, penetrations, null, draw);

        public static List<DirectInjury> Evaluate(ImpactModelFile m, IEnumerable<BluntContact> blunt, IEnumerable<PenetrationResult> penetrations,
                                                  IEnumerable<CutResult> cuts, Func<double> draw)
        {
            var result = new List<DirectInjury>();
            Action<string, double, double, string> add = (id, p, sev, why) =>
            {
                if (string.IsNullOrEmpty(id) || p <= 0.005) return;
                var old = result.FirstOrDefault(x => x.InjuryId == id);
                if (old == null) result.Add(new DirectInjury { InjuryId = id, Probability = Math.Min(1, p), Severity = sev, Reason = why });
                else { old.Probability = 1 - (1 - old.Probability) * (1 - Math.Min(1, p)); old.Severity = Math.Max(old.Severity, sev); old.Reason += "; " + why; }
            };
            foreach (var b in blunt ?? new BluntContact[0])
            {
                string key = ContactRegion(b.Region);
                if (key == "Kopf")
                {
                    double p = SkullFractureProbability(m, b.PeakForce_N, b.Area_cm2);
                    add(m.fractures.skull.injuryId, p, 0.4 + 0.5 * p, $"Kopf {b.PeakForce_N / 1000:0.0} kN auf {b.Area_cm2:0} cm²");
                }
                else if (key == "Thorax")
                {
                    var reg = Region(m, "Thorax");
                    double comp = b.MaxIndentation_m / reg.depth_m, ais = RibAis(m, comp);
                    double p = Math.Max(0, Math.Min(1, (ais - 2) / 2));   // AIS 2 → 0, AIS 4 → 1 (linear, Annahme)
                    add(b.Side > 0 ? m.fractures.ribs.injuryRight : m.fractures.ribs.injuryLeft, p, 0.5 + 0.4 * p, $"Thoraxkompression {comp:P0}");
                }
                else
                {
                    var bone = m.fractures.longBones.FirstOrDefault(x => x.segment == key && b.Segment != null && b.Segment.StartsWith(key));
                    if (bone == null) continue;
                    double p = LongBoneProbability(bone, b.PeakForce_N, b.SegmentLength_m);
                    add(b.Side > 0 ? bone.injuryRight : bone.injuryLeft, p, 0.5 + 0.4 * p, $"{b.Segment} {b.PeakForce_N / 1000:0.0} kN quer");
                }
            }
            foreach (var pen in penetrations ?? new PenetrationResult[0])
            {
                var rule = PenetrationRule(m, pen.Region);
                if (rule == null || pen.Depth_m < rule.wall_m + Math.Max(0, pen.FatExtra_m)) continue;   // in der Wand geblieben (auch: Rippe, dickes Fett)
                if (ContactRegion(pen.Region) == "Arm" && (pen.Segment == null || !pen.Segment.StartsWith("Unterarm"))) continue;
                string id = !string.IsNullOrEmpty(rule.deep) ? rule.deep : pen.Side > 0 ? rule.deepRight : rule.deepLeft;
                double p = draw() < rule.probability ? 1 : 0;
                double extra = Math.Min(1, (pen.Depth_m - rule.wall_m - Math.Max(0, pen.FatExtra_m)) / 0.08);
                add(id, p, 0.5 + 0.4 * extra, $"Stichkanal {pen.Region} {pen.Depth_m * 100:0} cm tief" + (pen.BonePierced ? " durch die Rippe" : "") + (pen.Embedded ? " (Fremdkörper steckt)" : ""));
            }
            foreach (var c in cuts ?? new CutResult[0])
            {
                var rule = m.cutting?.rules?.FirstOrDefault(r => c.Segment != null && c.Segment.StartsWith(r.segmentPrefix));
                if (rule == null || c.Length_m < rule.minLength_m || c.Depth_m < rule.vesselDepth_m) continue;
                string id = c.Side > 0 ? rule.injuryRight : rule.injuryLeft;
                add(id, 1, 0.5 + 0.4 * Math.Min(1, (c.Depth_m - rule.vesselDepth_m) / 0.01), $"Schnitt {c.Segment} {c.Length_m * 100:0} cm lang, ca. {c.Depth_m * 1000:0} mm tief");
            }
            return result;
        }
    }
}
