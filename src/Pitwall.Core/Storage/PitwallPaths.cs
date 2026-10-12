using System;
using System.IO;
using System.Text;

namespace Pitwall.Storage
{
    /// <summary>
    /// Where Pitwall keeps its data. Both the SimHub plugin and the manager application point
    /// at the same folder under Documents, which is what lets them share a championship without
    /// any installation step.
    /// </summary>
    public static class PitwallPaths
    {
        /// <summary>
        /// UTF-8 senza marcatore iniziale, da usare per ogni file che leggono Assetto Corsa o
        /// Custom Shaders Patch. Encoding.UTF8 premette un BOM, e quei tre byte invisibili
        /// bastano a far fallire in silenzio i parser del gioco: la prima riga non viene
        /// riconosciuta, il file sembra a posto e non funziona niente.
        /// </summary>
        public static readonly Encoding Utf8NoBom = new UTF8Encoding(false);

        private static string _overrideRoot;

        /// <summary>Overrides all data folders, including the AC bridge, for tests and portable installs.</summary>
        public static void UseRoot(string root)
        {
            _overrideRoot = string.IsNullOrWhiteSpace(root) ? null : root;
        }

        public static string Root
        {
            get
            {
                if (!string.IsNullOrEmpty(_overrideRoot)) return _overrideRoot;
                string documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                return Path.Combine(documents, "Pitwall");
            }
        }

        public static string Championships => Path.Combine(Root, "championships");

        public static string Exports => Path.Combine(Root, "exports");

        public static string Logs => Path.Combine(Root, "logs");

        public static string CircuitLibraryFile => Path.Combine(Root, "circuits.json");

        public static string SettingsFile => Path.Combine(Root, "settings.json");

        /// <summary>
        /// Live state written by the plugin. The manager prefers the named pipe, but this file
        /// is the fallback and it is also what the in-game Lua companion reads.
        /// </summary>
        public static string LiveStateFile => Path.Combine(Root, "live", "state.json");

        /// <summary>Commands dropped by the manager or the Lua app for the plugin to pick up.</summary>
        public static string CommandFile => Path.Combine(Root, "live", "command.json");

        /// <summary>
        /// Seconda copia dello stato, dentro la cartella documenti di Assetto Corsa.
        ///
        /// Custom Shaders Patch limita i percorsi che gli script delle app possono leggere, e
        /// una cartella qualsiasi sotto Documenti puo non essere fra quelli. Questa invece e
        /// casa sua: le app la leggono di sicuro.
        /// </summary>
        public static string AcDocumentsFolder
        {
            get
            {
                // Isolare anche i comandi e lo stato del Lua durante le simulazioni.
                if (!string.IsNullOrEmpty(_overrideRoot))
                    return Path.Combine(_overrideRoot, "Assetto Corsa", "Pitwall");
                string documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                return Path.Combine(documents, "Assetto Corsa", "Pitwall");
            }
        }

        public static string AcLiveStateFile => Path.Combine(AcDocumentsFolder, "state.json");

        public static string AcRaceControlFile => Path.Combine(AcDocumentsFolder, "race_control.txt");

        public static string ChampionshipFile(string id) =>
            Path.Combine(Championships, SanitiseFileName(id) + ".json");

        /// <summary>Creates every folder Pitwall needs. Safe to call repeatedly.</summary>
        public static void EnsureFolders()
        {
            Directory.CreateDirectory(Root);
            Directory.CreateDirectory(Championships);
            Directory.CreateDirectory(Exports);
            Directory.CreateDirectory(Logs);
            Directory.CreateDirectory(Path.Combine(Root, "live"));
        }

        public static string SanitiseFileName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "senza-nome";
            foreach (char c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '-');
            return name.Trim();
        }
    }
}
