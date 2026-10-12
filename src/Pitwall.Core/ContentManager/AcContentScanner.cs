using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Pitwall.Model;

namespace Pitwall.ContentManager
{
    /// <summary>A track found on disk, ready to be turned into a circuit.</summary>
    public sealed class AcTrack
    {
        public string TrackId { get; set; } = "";
        public string Layout { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public double LengthMeters { get; set; }

        public string Key => string.IsNullOrEmpty(Layout) ? TrackId : TrackId + " / " + Layout;

        public override string ToString() =>
            DisplayName + "   (" + Key + (LengthMeters > 0 ? ", " + (LengthMeters / 1000).ToString("0.00") + " km" : "") + ")";
    }

    /// <summary>
    /// Finds the Assetto Corsa installation and reads the installed tracks and cars, so circuits
    /// and teams can be added to a championship by picking from a list instead of typing folder
    /// names by hand and getting them subtly wrong.
    /// </summary>
    public static class AcContentScanner
    {
        /// <summary>
        /// Dove sta Steam secondo il registro di Windows, quando qualcuno sa leggerlo.
        ///
        /// Il registro non si legge da qui. Questo file e passato dall'applicazione Windows al
        /// motore, perche serve anche al plugin di SimHub: il plugin deve poter riscrivere il
        /// preset di Content Manager da solo, e per farlo gli serve sapere quali auto sono
        /// installate. Ma il motore e netstandard, e il registro li non c'e senza aggiungere un
        /// pacchetto - cioe una terza libreria da copiare a mano accanto alle due del plugin,
        /// per leggere un percorso che nel 99% dei casi e quello consueto.
        ///
        /// Quindi il motore cerca Steam dove sta di solito, e chi **puo** leggere il registro
        /// riempie questo gancio e allarga la ricerca. L'applicazione Windows lo fa all'avvio.
        /// </summary>
        public static Func<IEnumerable<string>> SteamPathsFromRegistry;

        /// <summary>Locates the game, following every Steam library it can find.</summary>
        public static string FindInstallation()
        {
            foreach (var steam in SteamRoots())
            {
                try
                {
                    foreach (var library in SteamLibraries(steam))
                    {
                        string candidate = Path.Combine(library, "steamapps", "common", "assettocorsa");
                        if (Directory.Exists(Path.Combine(candidate, "content", "tracks"))) return candidate;
                    }
                }
                catch (Exception)
                {
                }
            }

            // Ultima spiaggia: una libreria di Steam su un secondo disco, senza passare da
            // nessuna installazione di Steam.
            foreach (var guess in new[]
                     {
                         @"D:\SteamLibrary\steamapps\common\assettocorsa",
                         @"E:\SteamLibrary\steamapps\common\assettocorsa"
                     })
            {
                if (Directory.Exists(Path.Combine(guess, "content", "tracks"))) return guess;
            }

            return null;
        }

        /// <summary>Le installazioni di Steam da esplorare: prima il registro, poi le consuete.</summary>
        private static IEnumerable<string> SteamRoots()
        {
            var dalRegistro = SteamPathsFromRegistry;
            if (dalRegistro != null)
            {
                IEnumerable<string> letti = null;
                try { letti = dalRegistro(); }
                catch (Exception) { }

                if (letti != null)
                {
                    foreach (var path in letti)
                        if (!string.IsNullOrWhiteSpace(path)) yield return path;
                }
            }

            string x86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            string pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);

            if (!string.IsNullOrEmpty(x86)) yield return Path.Combine(x86, "Steam");
            if (!string.IsNullOrEmpty(pf)) yield return Path.Combine(pf, "Steam");
        }

        private static IEnumerable<string> SteamLibraries(string steamPath)
        {
            yield return steamPath;

            string vdf = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");
            if (!File.Exists(vdf)) yield break;

            foreach (var line in File.ReadAllLines(vdf))
            {
                int pathIndex = line.IndexOf("\"path\"", StringComparison.OrdinalIgnoreCase);
                if (pathIndex < 0) continue;

                var parts = line.Split('"').Where(p => !string.IsNullOrWhiteSpace(p)).ToList();
                if (parts.Count >= 2)
                    yield return parts[parts.Count - 1].Replace(@"\\", @"\");
            }
        }

        /// <summary>Every track and layout installed, sorted by display name.</summary>
        public static List<AcTrack> Tracks(string acRoot)
        {
            var result = new List<AcTrack>();
            if (string.IsNullOrEmpty(acRoot)) return result;

            string root = Path.Combine(acRoot, "content", "tracks");
            if (!Directory.Exists(root)) return result;

            foreach (var directory in Directory.GetDirectories(root))
            {
                string trackId = Path.GetFileName(directory);
                string ui = Path.Combine(directory, "ui");
                if (!Directory.Exists(ui)) continue;

                string rootJson = Path.Combine(ui, "ui_track.json");
                if (File.Exists(rootJson))
                    result.Add(Read(trackId, "", rootJson));

                foreach (var layout in Directory.GetDirectories(ui))
                {
                    string json = Path.Combine(layout, "ui_track.json");
                    if (File.Exists(json))
                        result.Add(Read(trackId, Path.GetFileName(layout), json));
                }
            }

            return result.OrderBy(t => t.DisplayName, StringComparer.CurrentCultureIgnoreCase).ToList();
        }

        private static AcTrack Read(string trackId, string layout, string jsonPath)
        {
            var track = new AcTrack { TrackId = trackId, Layout = layout, DisplayName = trackId };
            try
            {
                string json = File.ReadAllText(jsonPath);
                track.DisplayName = JsonValue(json, "name") ?? trackId;
                if (!string.IsNullOrEmpty(layout))
                    track.DisplayName += " - " + layout;

                string length = JsonValue(json, "length");
                if (!string.IsNullOrEmpty(length))
                {
                    string digits = new string(length.Where(c => char.IsDigit(c) || c == '.' || c == ',').ToArray())
                        .Replace(',', '.');
                    if (double.TryParse(digits, System.Globalization.NumberStyles.Any,
                                        System.Globalization.CultureInfo.InvariantCulture, out double value))
                    {
                        // Some tracks store kilometres, some store metres.
                        track.LengthMeters = value < 100 ? value * 1000 : value;
                    }
                }
            }
            catch (Exception)
            {
            }
            return track;
        }

        /// <summary>
        /// Pulls a value out of a ui_track.json without a JSON parser: these files are often
        /// hand-edited by track authors and are not always valid JSON, so a tolerant read is
        /// more useful here than a strict one.
        /// </summary>
        private static string JsonValue(string json, string key)
        {
            string needle = "\"" + key + "\"";
            int index = json.IndexOf(needle, StringComparison.OrdinalIgnoreCase);
            if (index < 0) return null;

            int colon = json.IndexOf(':', index + needle.Length);
            if (colon < 0) return null;

            int start = json.IndexOf('"', colon + 1);
            if (start < 0) return null;

            int end = start + 1;
            while (end < json.Length && (json[end] != '"' || json[end - 1] == '\\')) end++;
            return end < json.Length ? json.Substring(start + 1, end - start - 1) : null;
        }

        /// <summary>Car folders installed in the game.</summary>
        public static List<string> Cars(string acRoot)
        {
            if (string.IsNullOrEmpty(acRoot)) return new List<string>();
            string root = Path.Combine(acRoot, "content", "cars");
            if (!Directory.Exists(root)) return new List<string>();

            return Directory.GetDirectories(root)
                .Select(Path.GetFileName)
                .OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }

        /// <summary>Skin folders of a car.</summary>
        public static List<string> Skins(string acRoot, string carId)
        {
            if (string.IsNullOrEmpty(acRoot) || string.IsNullOrEmpty(carId)) return new List<string>();
            string root = Path.Combine(acRoot, "content", "cars", carId, "skins");
            if (!Directory.Exists(root)) return new List<string>();

            return Directory.GetDirectories(root)
                .Select(Path.GetFileName)
                .OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }

        /// <summary>Builds a circuit from a scanned track, filling in sensible defaults.</summary>
        public static Circuit ToCircuit(AcTrack track)
        {
            double length = track.LengthMeters > 200 ? track.LengthMeters : 5000;
            return new Circuit
            {
                Name = track.DisplayName,
                AcTrackId = track.TrackId,
                AcTrackConfig = track.Layout,
                LengthMeters = Math.Round(length),
                // A grand prix is about 305 km, rounded up to a whole number of laps.
                GrandPrixLaps = Math.Max(1, (int)Math.Ceiling(305000.0 / length)),
                ReferenceLapSeconds = Math.Round(length / 58.0, 1)
            };
        }
    }
}
