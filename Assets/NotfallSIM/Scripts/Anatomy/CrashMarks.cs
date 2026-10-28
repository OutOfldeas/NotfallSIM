using System;
using System.Collections.Generic;
using System.Linq;
using NotfallSIM.Data;

namespace NotfallSIM.Anatomy
{
    // ---------- Datenmodell (crash_marks.json) ----------

    [Serializable]
    public class CrashMarkSource { public string id, citation, url, note; }

    [Serializable]
    public class BeltTemplate
    {
        public string id, label, finding;     // finding: Befundtext für die Untersuchung
        public MarkAnchor anchor;
        public string[] segments;
        public float radius_m, stretch;
        public float[] stretchAxis;
    }

    [Serializable]
    public class CrashMarkRules
    {
        public string version, note;
        public double bruiseImpulse_Ns; public string bruiseSourceId, bruiseNote;
        public double lacerationForce_N; public string lacerationSourceId, lacerationNote;
        public string[] thinTissueSegments;      // Haut direkt über Knochen (Platzwunde möglich)
        public double abrasionSlide_m_s; public string abrasionNote;
        public string[] abrasiveSurfaces;        // rauhe Flächen (Straße, Fahrzeugfront)
        public double bruiseRadius_m, lacerationRadius_m, abrasionRadius_m;
        public BeltTemplate shoulderBelt, lapBelt;
        public CrashMarkSource[] sources;
    }

    /// <summary>Zusammengefasster Kontakt eines Körpersegments mit einer Fläche während des Crashs.</summary>
    public class ContactSummary
    {
        public string Segment, Region, Surface;
        public float[] ModelPoint;       // Ort des stärksten Kontakts in Modellkoordinaten (Ruhelage)
        public float[] ModelInside;      // Punkt auf der Segmentachse darunter (Strahl von innen nach außen)
        public double Impulse_Ns, PeakForce_N, MaxSlide_m_s;
    }

    /// <summary>Ein im Crash entstandenes Zeichen mit Befundtext und Region für die Untersuchung.</summary>
    public class CrashMark
    {
        public SkinMark Mark;
        public string Finding, Region;
    }

    /// <summary>
    /// Wundentstehung aus dem Crash (ohne Unity, getestet): Prellung ab einem Kontaktimpuls, Platzwunde ab einer
    /// Spitzenkraft über dünnem Weichteilmantel, Schürfung bei Gleiten über rauhe Flächen, Gurtmarken aus der Gurtkraft.
    /// Der Ort ist der Kontaktpunkt, zurückgerechnet in die Ruhelage des Anatomiemodells.
    /// </summary>
    public static class CrashMarks
    {
        static readonly DynamicFinding Always = new DynamicFinding { maxLevel = 1, maxBodyLoss = 1, requiresTag = "", untilTag = "", text = "" };

        public static string FaceLabel(float[] outward)
        {
            float ax = Math.Abs(outward[0]), ay = Math.Abs(outward[1]), az = Math.Abs(outward[2]);
            if (az >= ax && az >= ay) return outward[2] > 0 ? "ventral" : "dorsal";
            if (ax >= ay) return outward[0] > 0 ? "rechts seitlich" : "links seitlich";
            return outward[1] > 0 ? "kranial" : "kaudal";
        }

        static string SurfaceLabel(string s) =>
            s == "ground" ? "Bodenkontakt" : s == "airbag" ? "Airbag" : s == "vehicle" ? "Fahrzeug" : "Aufprall";

        public static List<CrashMark> Generate(CrashMarkRules r, IEnumerable<ContactSummary> contacts,
                                               double shoulderBeltImpulse_Ns, double lapBeltImpulse_Ns)
        {
            var list = new List<CrashMark>();
            int k = 0;
            foreach (var c in contacts.OrderByDescending(x => x.Impulse_Ns))
            {
                if (c.ModelPoint == null || c.ModelInside == null) continue;
                var outward = new[] { c.ModelPoint[0] - c.ModelInside[0], c.ModelPoint[1] - c.ModelInside[1], c.ModelPoint[2] - c.ModelInside[2] };
                string where = $"{c.Segment} {FaceLabel(outward)} ({SurfaceLabel(c.Surface)})";
                Func<string, float, string, SkinMark> make = (kind, radius, idSuffix) => new SkinMark
                {
                    id = $"crash_{k}_{idSuffix}", injuryId = "", kind = kind, region = c.Region,
                    anchor = new MarkAnchor { meshes = new string[0], point = (float[])c.ModelInside.Clone(), offset = new float[0], direction = outward },
                    segments = new[] { c.Segment }, radius_m = radius, stretch = 1, stretchAxis = new float[0], when = Always
                };
                bool slide = c.MaxSlide_m_s >= r.abrasionSlide_m_s && r.abrasiveSurfaces != null && r.abrasiveSurfaces.Contains(c.Surface);
                if (slide)
                {
                    var m = make("schuerfung", (float)r.abrasionRadius_m, "s");
                    m.stretch = 1.6f;
                    list.Add(new CrashMark { Mark = m, Region = c.Region, Finding = "Schürfwunde " + where });
                }
                if (c.PeakForce_N >= r.lacerationForce_N && r.thinTissueSegments != null && r.thinTissueSegments.Contains(c.Segment))
                {
                    var m = make("platzwunde", (float)r.lacerationRadius_m * 1.6f, "p");
                    m.stretch = 2.5f;   // Platzwunden über Knochen sind länglich-unregelmäßig, nicht rund
                    list.Add(new CrashMark { Mark = m, Region = c.Region, Finding = "Platzwunde " + where });
                }
                if (c.Impulse_Ns >= r.bruiseImpulse_Ns)
                {
                    // Größe wächst schwach (logarithmisch) mit dem Impuls (Darstellungsannahme)
                    double scale = Math.Max(0.6, Math.Min(1.4, 0.6 + 0.25 * Math.Log10(c.Impulse_Ns / r.bruiseImpulse_Ns)));
                    list.Add(new CrashMark { Mark = make("prellmarke", (float)(r.bruiseRadius_m * scale), "b"), Region = c.Region, Finding = "Prellmarke " + where });
                }
                k++;
            }
            if (shoulderBeltImpulse_Ns >= r.bruiseImpulse_Ns && r.shoulderBelt != null) list.Add(Belt(r.shoulderBelt));
            if (lapBeltImpulse_Ns >= r.bruiseImpulse_Ns && r.lapBelt != null) list.Add(Belt(r.lapBelt));
            return list;
        }

        static CrashMark Belt(BeltTemplate t) => new CrashMark
        {
            Region = t.segments != null && t.segments.Length > 0 ? t.segments[0] : "",
            Finding = t.finding,
            Mark = new SkinMark
            {
                id = "crash_" + t.id, injuryId = "", kind = "prellmarke", anchor = t.anchor, segments = t.segments,
                radius_m = t.radius_m, stretch = t.stretch, stretchAxis = t.stretchAxis, wrap = true, when = Always
            }
        };
    }
}
