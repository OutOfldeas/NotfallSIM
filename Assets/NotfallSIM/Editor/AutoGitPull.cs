using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace NotfallSIM.EditorTools
{
    /// <summary>
    /// Holt neue Commits automatisch (git pull --ff-only): beim Öffnen des Projekts, wenn der Editor wieder in den
    /// Vordergrund kommt, und alle 10 Minuten — nicht im Play-Modus und nicht während des Kompilierens.
    /// Sicher: Es wird nur vorgespult (fast-forward). Lokale Änderungen bleiben unangetastet; berührt ein neuer Commit
    /// eine lokal geänderte Datei, verweigert git den Pull und die Konsole sagt, welche Datei es ist.
    /// Menü: NotfallSIM › Git › Jetzt aktualisieren / Automatisch aktualisieren (an/aus).
    /// </summary>
    [InitializeOnLoad]
    public static class AutoGitPull
    {
        const string PrefKey = "NotfallSIM.AutoGitPull";
        const double Interval_s = 600;
        const string Self = "Assets/NotfallSIM/Editor/AutoGitPull.cs";
        static double _next;
        static bool _busy;
        static string _lastWarning;

        static bool Enabled
        {
            get => EditorPrefs.GetBool(PrefKey, true);
            set => EditorPrefs.SetBool(PrefKey, value);
        }

        static AutoGitPull()
        {
            EditorApplication.update += Tick;
            EditorApplication.focusChanged += focused => { if (focused) _next = 0; };
            _next = EditorApplication.timeSinceStartup + 3;   // kurz nach dem Laden
        }

        [MenuItem("NotfallSIM/Git/Jetzt aktualisieren")]
        static void PullNow() => Start(manual: true);

        [MenuItem("NotfallSIM/Git/Automatisch aktualisieren")]
        static void Toggle()
        {
            Enabled = !Enabled;
            Debug.Log($"[NotfallSIM] Automatisches Aktualisieren {(Enabled ? "an" : "aus")}.");
        }

        [MenuItem("NotfallSIM/Git/Automatisch aktualisieren", true)]
        static bool ToggleValidate() { Menu.SetChecked("NotfallSIM/Git/Automatisch aktualisieren", Enabled); return true; }

        static void Tick()
        {
            if (!Enabled || _busy || EditorApplication.timeSinceStartup < _next) return;
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            Start(manual: false);
        }

        static void Start(bool manual)
        {
            if (_busy) return;
            _busy = true;
            _next = EditorApplication.timeSinceStartup + Interval_s;
            string root = Path.GetDirectoryName(Application.dataPath);
            Task.Run(() => Pull(root, manual)).ContinueWith(t =>
            {
                var r = t.IsFaulted ? new Result { Error = t.Exception?.GetBaseException().Message } : t.Result;
                EditorApplication.delayCall += () => Finish(r, manual);
            });
        }

        class Result { public bool Updated; public string From, To, Log, Error, Warning; }

        static Result Pull(string root, bool manual)
        {
            var r = new Result();
            string git = FindGit();
            if (git == null) { r.Error = "git nicht gefunden (Git für Windows installieren oder git in den PATH aufnehmen)."; return r; }
            if (!Directory.Exists(Path.Combine(root, ".git"))) { r.Error = "Projektordner ist kein git-Repository."; return r; }
            r.From = Run(git, "rev-parse --short HEAD", root).Out.Trim();
            var fetch = Run(git, "fetch --quiet", root);
            if (fetch.Code != 0) { r.Error = "fetch fehlgeschlagen: " + fetch.Err.Trim(); return r; }
            var behind = Run(git, "rev-list --count HEAD..@{u}", root);
            if (behind.Code != 0) { r.Error = "kein Upstream-Zweig: " + behind.Err.Trim(); return r; }
            if (behind.Out.Trim() == "0") { r.To = r.From; return r; }

            var pull = Run(git, "pull --ff-only --quiet", root);
            if (pull.Code != 0 && pull.Err.Contains("untracked working tree files would be overwritten"))
            {
                // Erste Installation: dieses Skript wurde von Hand in den Ordner gelegt und kommt jetzt auch per git.
                var files = pull.Err.Split('\n').Where(l => l.StartsWith("\t")).Select(l => l.Trim().Replace('\\', '/')).ToList();
                if (files.Count > 0 && files.All(f => f == Self || f == Self + ".meta"))
                {
                    foreach (var f in files) File.Delete(Path.Combine(root, f));
                    pull = Run(git, "pull --ff-only --quiet", root);
                }
            }
            if (pull.Code != 0)
            {
                string err = pull.Err.Trim();
                r.Warning = err.Contains("would be overwritten") || err.Contains("Not possible to fast-forward") || err.Contains("diverge")
                    ? "Nicht aktualisiert — lokale Änderungen im Weg:\n" + err + "\nLösung: Änderungen sichern (git stash) oder verwerfen, dann NotfallSIM › Git › Jetzt aktualisieren."
                    : "Nicht aktualisiert: " + err;
                return r;
            }
            r.To = Run(git, "rev-parse --short HEAD", root).Out.Trim();
            r.Log = Run(git, $"log --oneline --no-merges {r.From}..{r.To}", root).Out.Trim();
            r.Updated = r.To != r.From;
            return r;
        }

        static void Finish(Result r, bool manual)
        {
            _busy = false;
            if (r.Error != null) { if (manual || _lastWarning != r.Error) Debug.LogWarning("[NotfallSIM] Git: " + r.Error); _lastWarning = r.Error; return; }
            if (r.Warning != null) { if (manual || _lastWarning != r.Warning) Debug.LogWarning("[NotfallSIM] Git: " + r.Warning); _lastWarning = r.Warning; return; }
            _lastWarning = null;
            if (!r.Updated) { if (manual) Debug.Log($"[NotfallSIM] Git: aktuell ({r.To})."); return; }
            int n = string.IsNullOrEmpty(r.Log) ? 0 : r.Log.Split('\n').Length;
            Debug.Log($"[NotfallSIM] Git: aktualisiert {r.From} → {r.To} ({n} Commits)\n{r.Log}");
            AssetDatabase.Refresh();
        }

        class Output { public int Code; public string Out = "", Err = ""; }

        static Output Run(string git, string args, string dir)
        {
            var psi = new ProcessStartInfo(git, args)
            {
                WorkingDirectory = dir, UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true
            };
            psi.EnvironmentVariables["GIT_TERMINAL_PROMPT"] = "0";   // nie auf eine Passwortabfrage warten
            psi.EnvironmentVariables["LC_ALL"] = "C";                // englische Meldungen (werden oben ausgewertet)
            using (var p = Process.Start(psi))
            {
                var o = p.StandardOutput.ReadToEndAsync();
                var e = p.StandardError.ReadToEndAsync();
                if (!p.WaitForExit(120000)) { try { p.Kill(); } catch { } return new Output { Code = -1, Err = "Zeitüberschreitung" }; }
                return new Output { Code = p.ExitCode, Out = o.Result, Err = e.Result };
            }
        }

        static string FindGit()
        {
            var candidates = new[] { "git" }.ToList();
            string pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            candidates.Add(Path.Combine(pf, "Git", "cmd", "git.exe"));
            candidates.Add(Path.Combine(local, "Programs", "Git", "cmd", "git.exe"));
            try
            {
                // GitHub Desktop bringt ein eigenes git mit
                string ghd = Path.Combine(local, "GitHubDesktop");
                if (Directory.Exists(ghd))
                    candidates.AddRange(Directory.GetDirectories(ghd, "app-*").OrderByDescending(d => d)
                        .Select(d => Path.Combine(d, "resources", "app", "git", "cmd", "git.exe")));
            }
            catch { }
            foreach (var c in candidates)
            {
                try
                {
                    if (c != "git" && !File.Exists(c)) continue;
                    if (Run(c, "--version", Environment.CurrentDirectory).Code == 0) return c;
                }
                catch { }
            }
            return null;
        }
    }
}
