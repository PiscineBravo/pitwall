using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Pitwall.Model;

namespace Pitwall.Manager.Services
{
    public sealed class PhysicsPatchResult
    {
        public int Patched { get; set; }
        public int AlreadyDone { get; set; }
        public int Restored { get; set; }
        public List<string> Notes { get; } = new List<string>();
    }

    /// <summary>
    /// Abilita gli script di physics di Custom Shaders Patch sui circuiti del campionato.
    ///
    /// Senza questo permesso le funzioni physics.* non sono disponibili, e quindi niente safety
    /// car che rallenta davvero l'intelligenza artificiale, niente soste comandate, niente
    /// degrado: l'app in pista resta un overlay. E una misura di sicurezza di CSP, non un
    /// limite di Pitwall: le piste devono acconsentire.
    ///
    /// L'intervento e minimo e reversibile: si aggiunge una sezione a surfaces.ini e si cambia
    /// un valore, dopo aver messo da parte una copia dell'originale. Resta comunque una
    /// modifica ai dati del gioco, quindi la si fa solo su richiesta esplicita, e una pista con
    /// physics estesa non vale piu per le classifiche online.
    /// </summary>
    public static class TrackPhysicsEnabler
    {
        private const string BackupSuffix = ".pitwall-backup";
        private const string Section = "[_SCRIPTING_PHYSICS]";

        /// <summary>I file surfaces.ini che riguardano un circuito, di solito uno o due.</summary>
        public static List<string> SurfaceFiles(string acRoot, Circuit circuit)
        {
            var files = new List<string>();
            if (string.IsNullOrEmpty(acRoot) || circuit == null || string.IsNullOrWhiteSpace(circuit.AcTrackId))
                return files;

            string track = Path.Combine(acRoot, "content", "tracks", circuit.AcTrackId);
            if (!Directory.Exists(track)) return files;

            // Il layout ha i suoi dati; la radice serve quando la pista non ha layout.
            if (!string.IsNullOrWhiteSpace(circuit.AcTrackConfig))
            {
                string layout = Path.Combine(track, circuit.AcTrackConfig, "data", "surfaces.ini");
                if (File.Exists(layout)) files.Add(layout);
            }

            string root = Path.Combine(track, "data", "surfaces.ini");
            if (File.Exists(root)) files.Add(root);

            return files;
        }

        public static bool IsEnabled(string path)
        {
            if (!File.Exists(path)) return false;
            string text = File.ReadAllText(path);
            return text.IndexOf(Section, StringComparison.OrdinalIgnoreCase) >= 0 &&
                   text.IndexOf("extended-0", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>Quante piste del campionato sono gia pronte, e quante no.</summary>
        public static void Survey(string acRoot, Championship championship,
                                  out int ready, out int missing, out int notFound)
        {
            ready = missing = notFound = 0;
            foreach (var circuit in championship.Circuits)
            {
                var files = SurfaceFiles(acRoot, circuit);
                if (files.Count == 0) { notFound++; continue; }
                if (files.All(IsEnabled)) ready++;
                else missing++;
            }
        }

        public static PhysicsPatchResult Enable(string acRoot, Championship championship)
        {
            var result = new PhysicsPatchResult();

            foreach (var circuit in championship.Circuits)
            {
                var files = SurfaceFiles(acRoot, circuit);
                if (files.Count == 0)
                {
                    result.Notes.Add(circuit.Name + ": surfaces.ini non trovato, probabilmente e dentro data.acd.");
                    continue;
                }

                foreach (var file in files)
                {
                    if (IsEnabled(file)) { result.AlreadyDone++; continue; }

                    try
                    {
                        string backup = file + BackupSuffix;
                        if (!File.Exists(backup)) File.Copy(file, backup);

                        File.WriteAllText(file, Patch(File.ReadAllText(file)), Pitwall.Storage.PitwallPaths.Utf8NoBom);
                        result.Patched++;
                    }
                    catch (Exception ex)
                    {
                        result.Notes.Add(circuit.Name + ": " + ex.Message);
                    }
                }
            }

            return result;
        }

        public static PhysicsPatchResult Restore(string acRoot, Championship championship)
        {
            var result = new PhysicsPatchResult();

            foreach (var circuit in championship.Circuits)
            {
                foreach (var file in SurfaceFiles(acRoot, circuit))
                {
                    string backup = file + BackupSuffix;
                    if (!File.Exists(backup)) continue;
                    try
                    {
                        File.Copy(backup, file, true);
                        File.Delete(backup);
                        result.Restored++;
                    }
                    catch (Exception ex)
                    {
                        result.Notes.Add(circuit.Name + ": " + ex.Message);
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// Aggiunge il permesso e attiva la physics estesa. Il secondo passo e il piu strano:
        /// CSP usa il campo WAV_PITCH della prima superficie come interruttore, perche non
        /// esisteva un posto migliore dove metterlo nei dati originali del gioco.
        /// </summary>
        private static string Patch(string text)
        {
            var lines = text.Replace("\r\n", "\n").Split('\n').ToList();

            bool inSurface0 = false;
            bool pitchDone = false;

            for (int i = 0; i < lines.Count; i++)
            {
                string trimmed = lines[i].Trim();

                if (trimmed.StartsWith("[", StringComparison.Ordinal))
                {
                    inSurface0 = trimmed.Equals("[SURFACE_0]", StringComparison.OrdinalIgnoreCase);
                    continue;
                }

                if (inSurface0 && !pitchDone &&
                    trimmed.StartsWith("WAV_PITCH", StringComparison.OrdinalIgnoreCase))
                {
                    lines[i] = "WAV_PITCH=extended-0";
                    pitchDone = true;
                }
            }

            if (!pitchDone)
            {
                int index = lines.FindIndex(l => l.Trim().Equals("[SURFACE_0]", StringComparison.OrdinalIgnoreCase));
                if (index >= 0) lines.Insert(index + 1, "WAV_PITCH=extended-0");
                else
                {
                    lines.Insert(0, "WAV_PITCH=extended-0");
                    lines.Insert(0, "[SURFACE_0]");
                }
            }

            if (text.IndexOf(Section, StringComparison.OrdinalIgnoreCase) < 0)
            {
                lines.Add("");
                lines.Add("; Aggiunto da Pitwall: consente agli script di modificare la physics,");
                lines.Add("; senza cui non esistono safety car ne strategia dell'intelligenza artificiale.");
                lines.Add(Section);
                lines.Add("ALLOW_TOOLS=1");
                lines.Add("ALLOW_TRACK_SCRIPTS=1");
                lines.Add("ALLOW_DISPLAY_SCRIPTS=1");
                lines.Add("ALLOW_NEW_MODE_SCRIPTS=1");
            }

            return string.Join(Environment.NewLine, lines);
        }
    }
}
