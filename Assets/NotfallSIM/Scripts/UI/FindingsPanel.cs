using System.Collections.Generic;
using System.Text;
using NotfallSIM.Interaction;
using TMPro;
using UnityEngine;

namespace NotfallSIM.UI
{
    /// <summary>Zeigt die Befunde der zuletzt untersuchten Körperregion an.</summary>
    public class FindingsPanel : MonoBehaviour
    {
        [SerializeField] TMP_Text text;

        public void Configure(TMP_Text target) => text = target;

        void OnEnable() => BodyRegion.OnExamined += Show;
        void OnDisable() => BodyRegion.OnExamined -= Show;

        void Show(string region, IReadOnlyList<string> findings)
        {
            if (!text) return;
            var sb = new StringBuilder();
            sb.AppendLine($"<b>Untersuchung: {region}</b>");
            if (findings.Count == 0)
                sb.AppendLine("Unauffällig.");
            else
                foreach (var f in findings) sb.AppendLine("• " + f);
            text.text = sb.ToString();
        }
    }
}
