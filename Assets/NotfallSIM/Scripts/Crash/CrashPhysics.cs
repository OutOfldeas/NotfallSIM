using UnityEngine;

namespace NotfallSIM.Crash
{
    /// <summary>Kleine Helfer, die zwischen Unity-Versionen abweichen.</summary>
    public static class CrashPhysics
    {
        public static Vector3 Velocity(Rigidbody rb)
        {
#if UNITY_6000_0_OR_NEWER
            return rb.linearVelocity;
#else
            return rb.velocity;
#endif
        }

        public static void SetVelocity(Rigidbody rb, Vector3 v)
        {
#if UNITY_6000_0_OR_NEWER
            rb.linearVelocity = v;
#else
            rb.velocity = v;
#endif
        }

        static Shader _lit;
        public static Material Mat(Color c)
        {
            if (_lit == null) _lit = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var m = new Material(_lit);
            m.color = c;
            return m;
        }
    }

    /// <summary>Markiert Umgebungsteile. Sitzpolster und Lehne zählen nicht als verletzende Kontakte.</summary>
    public class CrashSurface : MonoBehaviour
    {
        public bool countsForInjury = true;
    }

    /// <summary>Übergibt ein im Crash erzeugtes Szenario an die Schockraum-Szene.</summary>
    public static class CrashHandoff
    {
        public static Data.ScenarioDefinition Pending;
    }
}
