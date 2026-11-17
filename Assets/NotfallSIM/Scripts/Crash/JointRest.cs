using UnityEngine;

namespace NotfallSIM.Crash
{
    /// <summary>Relative Lage Kind zu Eltern beim Anlegen des Gelenks — darauf beziehen sich die Gelenkgrenzen (CrashGauges).</summary>
    public class JointRest : MonoBehaviour
    {
        public Quaternion Rest;
        /// <summary>Beugung beim Anlegen (Grad, 0 = gestreckt) und Richtung der Streckung (−1/+1 bezogen auf die Beugeachse).</summary>
        public float FlexAtRest_deg;
        public int ExtensionSign = -1;
    }
}
