using System;
using System.IO;
using UnityEngine;

namespace NotfallSIM.UI
{
    /// <summary>
    /// Welcher Stand läuft (git-Commit, im Editor aus .git gelesen). Damit ist auf jedem Screenshot zu sehen, ob der
    /// neueste Code geladen ist — z. B. nach dem automatischen Aktualisieren im Play-Modus.
    /// </summary>
    public static class BuildInfo
    {
        static string _commit;

        public static string Commit
        {
            get
            {
                if (_commit != null) return _commit;
                _commit = "";
                try
                {
                    string git = Path.Combine(Path.GetDirectoryName(Application.dataPath) ?? ".", ".git");
                    string head = File.ReadAllText(Path.Combine(git, "HEAD")).Trim();
                    if (head.StartsWith("ref: "))
                    {
                        string rf = head.Substring(5).Trim();
                        string file = Path.Combine(git, rf.Replace('/', Path.DirectorySeparatorChar));
                        if (File.Exists(file)) head = File.ReadAllText(file).Trim();
                        else
                        {
                            string packed = Path.Combine(git, "packed-refs");
                            head = "";
                            if (File.Exists(packed))
                                foreach (var line in File.ReadAllLines(packed))
                                    if (line.EndsWith(" " + rf)) { head = line.Substring(0, line.IndexOf(' ')); break; }
                        }
                    }
                    if (head.Length >= 7) _commit = head.Substring(0, 7);
                }
                catch (Exception) { _commit = ""; }
                return _commit;
            }
        }

        /// <summary>Kurze Zeile für die Oberfläche, leer ohne git.</summary>
        public static string Label => Commit.Length > 0 ? "Stand " + Commit : "";
    }
}
