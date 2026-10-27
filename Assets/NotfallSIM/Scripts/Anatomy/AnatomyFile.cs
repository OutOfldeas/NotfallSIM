using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace NotfallSIM.Anatomy
{
    // Kopf von body.bin (JSON). Erzeugt von tools/anatomy/build_anatomy.py.

    [Serializable]
    public class AnatomyJoint
    {
        public string name;
        public float[] pos;
    }

    [Serializable]
    public class AnatomySegment
    {
        public string name;
        public string region;      // Körperregion für Untersuchung und Crash-Sensoren
        public string parent;      // "" = Wurzel (Becken)
        public float[] joint;      // proximales Gelenk (Ruhelage, Meter)
        public float[] end;        // distaler Punkt
        public float massMale, massFemale; // Anteil am Körpergewicht (de Leva 1996)
        public float radius;       // Kapselradius
        public bool capsule;       // Gliedmaßen: Kapsel, Rumpf und Kopf: Box
        public float[] boxCenter;  // relativ zum Gelenk
        public float[] boxSize;
    }

    [Serializable]
    public class AnatomyMeshInfo
    {
        public string name;        // englischer FMA-Name
        public string label;       // deutsche Bezeichnung
        public string layer;       // skin, bone, organ, brain, artery, vein, nerve
        public int segment;        // Index in segments (für Haut −1: gewichtet)
        public float[] color;      // RGBA
        public bool weights;
        public string[] blendShapes;
        public int[] submeshes;    // Indexanzahl je Teilmesh (nur Haut: Haut, Unterhose)
        public int vertexCount, indexCount;
        public bool shortIndex;
    }

    [Serializable]
    public class AnatomyHeader
    {
        public string format;
        public int version;
        public float modelHeight, floorY;
        public AnatomyJoint[] joints;
        public AnatomySegment[] segments;
        public AnatomyMeshInfo[] meshes;
        public string attribution;
    }

    public class AnatomyMeshData
    {
        public AnatomyMeshInfo Info;
        public float[] Positions;      // 3 · n
        public int[] Indices;
        public byte[] Bones;           // 2 · n (Segmentindizes)
        public byte[] Weight0;         // n (0..255, Gewicht des ersten Segments)
        public List<float[]> BlendShapes = new List<float[]>();
    }

    public class AnatomyData
    {
        public AnatomyHeader Header;
        public List<AnatomyMeshData> Meshes = new List<AnatomyMeshData>();

        public int SegmentIndex(string name) => Array.FindIndex(Header.segments, s => s.name == name);
    }

    /// <summary>Liest body.bin. Der JSON-Parser wird übergeben (Unity: JsonUtility, Tests: DataContract).</summary>
    public static class AnatomyFile
    {
        public static AnatomyData Read(byte[] bytes, Func<string, AnatomyHeader> parseJson)
        {
            using (var r = new BinaryReader(new MemoryStream(bytes)))
            {
                if (Encoding.ASCII.GetString(r.ReadBytes(4)) != "NSAN") throw new InvalidDataException("Keine NotfallSIM-Anatomiedatei");
                int version = r.ReadInt32();
                if (version != 1) throw new InvalidDataException("Unbekannte Version " + version);
                int len = r.ReadInt32();
                var data = new AnatomyData { Header = parseJson(Encoding.UTF8.GetString(r.ReadBytes(len))) };
                foreach (var info in data.Header.meshes)
                {
                    var m = new AnatomyMeshData { Info = info, Positions = new float[info.vertexCount * 3], Indices = new int[info.indexCount] };
                    for (int i = 0; i < m.Positions.Length; i++) m.Positions[i] = r.ReadSingle();
                    for (int i = 0; i < m.Indices.Length; i++) m.Indices[i] = info.shortIndex ? r.ReadUInt16() : (int)r.ReadUInt32();
                    if (info.weights)
                    {
                        m.Bones = r.ReadBytes(info.vertexCount * 2);
                        m.Weight0 = r.ReadBytes(info.vertexCount);
                    }
                    foreach (var _ in info.blendShapes ?? new string[0])
                    {
                        var d = new float[info.vertexCount * 3];
                        for (int i = 0; i < d.Length; i++) d[i] = r.ReadSingle();
                        m.BlendShapes.Add(d);
                    }
                    data.Meshes.Add(m);
                }
                if (r.BaseStream.Position != r.BaseStream.Length) throw new InvalidDataException("Datei länger als erwartet");
                return data;
            }
        }
    }
}
