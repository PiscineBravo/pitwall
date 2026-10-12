using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Pitwall.Manager.Services
{
    /// <summary>
    /// Attiva il controller meteo di Pitwall dentro Custom Shaders Patch.
    ///
    /// Il controller e lo script che fa cambiare il tempo durante la sessione seguendo il piano
    /// del campionato. Copiarlo fra i controller disponibili non cambia nulla da solo: CSP ne
    /// usa uno alla volta, scelto in weather_fx.ini. Qui si scrive quella scelta nella
    /// configurazione utente, che ha la precedenza su quella di sistema e si ripristina in un
    /// attimo, invece di toccare i file del gioco.
    /// </summary>
    public static class WeatherControllerInstaller
    {
        public const string ControllerId = "pitwall";

        /// <summary>Il controller che Content Manager sceglie quando non gliene indichi uno.</summary>
        public const string DefaultControllerId = "base";
        private const string BackupSuffix = ".pitwall-backup";

        /// <summary>Cartella dove CSP cerca i controller meteo.</summary>
        public static string ControllersFolder(string acRoot) =>
            string.IsNullOrEmpty(acRoot) ? null : Path.Combine(acRoot, "extension", "weather-controllers");

        /// <summary>La configurazione utente di CSP, che vince su quella di sistema.</summary>
        public static string UserConfigFile()
        {
            string documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            return Path.Combine(documents, "Assetto Corsa", "cfg", "extension", "weather_fx.ini");
        }

        public static bool ControllerInstalled(string acRoot)
        {
            string folder = ControllersFolder(acRoot);
            return folder != null && File.Exists(Path.Combine(folder, ControllerId, "controller.lua"));
        }

        /// <summary>Vero se CSP sta gia usando il controller di Pitwall.</summary>
        /// <summary>
        /// Vero quando lo script di Pitwall e al posto del controller predefinito, che e quello
        /// che Content Manager avvia davvero. La voce CONTROLLER nei file conta poco: Content
        /// Manager la scavalca scrivendo __CM_WEATHER_CONTROLLER in race.ini a ogni avvio.
        /// </summary>
        public static bool IsSelected(string acRoot = null)
        {
            string target = ControllersFolder(acRoot);
            if (target == null) return false;

            string script = Path.Combine(target, DefaultControllerId, "controller.lua");
            if (!File.Exists(script)) return false;
            return File.ReadAllText(script).IndexOf("pitwall", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool PointsToPitwall(string file)
        {
            if (!File.Exists(file)) return false;
            return File.ReadAllLines(file)
                .Select(l => l.Trim())
                .Any(l => l.StartsWith("CONTROLLER", StringComparison.OrdinalIgnoreCase) &&
                          l.IndexOf(ControllerId, StringComparison.OrdinalIgnoreCase) > 0);
        }

        /// <summary>Copia il controller fra quelli disponibili. Da solo non attiva nulla.</summary>
        public static bool CopyController(string acRoot, string sourceFolder)
        {
            string target = ControllersFolder(acRoot);
            if (target == null || !Directory.Exists(sourceFolder)) return false;

            string destination = Path.Combine(target, ControllerId);
            Directory.CreateDirectory(destination);

            foreach (var file in Directory.GetFiles(sourceFolder))
                File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), true);

            TakeOverDefault(acRoot, sourceFolder);
            return true;
        }

        /// <summary>
        /// Mette il nostro script anche al posto del controller predefinito.
        ///
        /// Serve perche la scelta del controller non la fa Custom Shaders Patch: la fa Content
        /// Manager, che a ogni avvio scrive __CM_WEATHER_CONTROLLER dentro race.ini, e vi mette
        /// il nome di un controller solo se ne hai scelto uno dall'elenco "Dynamic" del menu
        /// meteo. Quella scelta e globale e ogni preset del campionato la cancella, perche
        /// caricare un preset riscrive la voce del tempo. Non esiste un campo, nel preset, che
        /// possa portarsela dietro.
        ///
        /// Quindi invece di farsi scegliere, lo script si mette dove Content Manager guarda
        /// comunque. Senza un piano di Pitwall si comporta esattamente come l'originale - applica
        /// il tempo selezionato e basta - percio le sessioni che con Pitwall non c'entrano non se
        /// ne accorgono. L'originale resta accanto, e Ripristina meteo lo rimette.
        /// </summary>
        private static void TakeOverDefault(string acRoot, string sourceFolder)
        {
            string target = ControllersFolder(acRoot);
            if (target == null) return;

            string defaults = Path.Combine(target, DefaultControllerId);
            string script = Path.Combine(defaults, "controller.lua");
            if (!File.Exists(script)) return;

            string backup = script + BackupSuffix;
            if (!File.Exists(backup)) File.Copy(script, backup);

            string source = Path.Combine(sourceFolder, "controller.lua");
            if (File.Exists(source)) File.Copy(source, script, true);

            // Le impostazioni dello script vanno accanto, o i valori predefiniti non si leggono.
            string settings = Path.Combine(sourceFolder, "settings.ini");
            if (File.Exists(settings))
            {
                string targetSettings = Path.Combine(defaults, "settings.ini");
                if (File.Exists(targetSettings) && !File.Exists(targetSettings + BackupSuffix))
                    File.Copy(targetSettings, targetSettings + BackupSuffix);
                File.Copy(settings, targetSettings, true);
            }
        }

        /// <summary>La configurazione di sistema di CSP, dentro la cartella del gioco.</summary>
        public static string GameConfigFile(string acRoot) =>
            string.IsNullOrEmpty(acRoot) ? null
                : Path.Combine(acRoot, "extension", "config", "weather_fx.ini");

        /// <summary>
        /// Sceglie il controller di Pitwall, mettendo da parte la configurazione attuale.
        ///
        /// La scelta va scritta in due posti. Quella utente, sotto Documenti, dovrebbe bastare da
        /// sola, ma non e detto che venga letta: nella configurazione di sistema il controller
        /// predefinito e "sol", che su questa installazione non esiste nemmeno, e un valore che
        /// punta nel vuoto fa ripiegare CSP sul controller base. Scriverli entrambi toglie di
        /// mezzo la domanda su quale dei due comandi.
        /// </summary>
        public static void Select(string acRoot = null)
        {
            SelectIn(UserConfigFile());

            string game = GameConfigFile(acRoot);
            if (game != null && File.Exists(game)) SelectIn(game);
        }

        private static void SelectIn(string file)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file));

            if (File.Exists(file) && !File.Exists(file + BackupSuffix))
                File.Copy(file, file + BackupSuffix);

            // La configurazione utente non e solo nostra: ci possono essere ombre delle nuvole,
            // qualita degli effetti, roba messa li da Content Manager. Si tocca la sola voce che
            // ci serve e il resto resta dov'e, invece di riscrivere il file da capo.
            var lines = File.Exists(file)
                ? new List<string>(File.ReadAllLines(file))
                : new List<string>();

            if (lines.Count == 0)
                lines.Add("; Scritto da Pitwall: seleziona il controller meteo del campionato.");

            int basic = lines.FindIndex(l => l.Trim().Equals("[BASIC]", StringComparison.OrdinalIgnoreCase));
            if (basic < 0)
            {
                if (lines.Count > 0 && lines[lines.Count - 1].Trim().Length > 0) lines.Add("");
                lines.Add("[BASIC]");
                basic = lines.Count - 1;
            }

            SetKey(lines, basic, "ENABLED", "1");
            SetKey(lines, basic, "CONTROLLER", ControllerId);

            File.WriteAllLines(file, lines, Pitwall.Storage.PitwallPaths.Utf8NoBom);
        }

        /// <summary>
        /// Scrive una voce dentro la sezione che comincia a <paramref name="section"/>: la
        /// sostituisce se c'e gia, altrimenti la aggiunge in fondo alla sezione, senza sbordare
        /// in quella dopo. I commenti in coda alla riga si perdono, il resto del file no.
        /// </summary>
        private static void SetKey(List<string> lines, int section, string key, string value)
        {
            int end = lines.Count;
            for (int i = section + 1; i < lines.Count; i++)
            {
                string trimmed = lines[i].Trim();
                if (trimmed.StartsWith("[", StringComparison.Ordinal)) { end = i; break; }
            }

            for (int i = section + 1; i < end; i++)
            {
                string trimmed = lines[i].TrimStart();
                if (trimmed.StartsWith(";", StringComparison.Ordinal)) continue;

                int equals = trimmed.IndexOf('=');
                if (equals <= 0) continue;
                if (!trimmed.Substring(0, equals).Trim().Equals(key, StringComparison.OrdinalIgnoreCase)) continue;

                lines[i] = key + "=" + value;
                return;
            }

            lines.Insert(end, key + "=" + value);
        }

        /// <summary>Rimette la configurazione meteo com'era, in entrambi i posti.</summary>
        public static bool Restore(string acRoot = null)
        {
            bool done = RestoreIn(UserConfigFile(), true);

            string game = GameConfigFile(acRoot);
            // Quella di sistema non si cancella mai: senza, CSP resta senza impostazioni.
            if (game != null) done |= RestoreIn(game, false);
            done |= RestoreDefault(acRoot);

            return done;
        }

        /// <summary>Rimette l'originale al posto del controller predefinito.</summary>
        private static bool RestoreDefault(string acRoot)
        {
            string target = ControllersFolder(acRoot);
            if (target == null) return false;

            bool done = false;
            string defaults = Path.Combine(target, DefaultControllerId);
            foreach (var name in new[] { "controller.lua", "settings.ini" })
            {
                string file = Path.Combine(defaults, name);
                string backup = file + BackupSuffix;
                if (!File.Exists(backup)) continue;
                File.Copy(backup, file, true);
                File.Delete(backup);
                done = true;
            }
            return done;
        }

        private static bool RestoreIn(string file, bool deleteIfNoBackup)
        {
            string backup = file + BackupSuffix;

            if (File.Exists(backup))
            {
                File.Copy(backup, file, true);
                File.Delete(backup);
                return true;
            }

            // Non c'era configurazione utente prima: toglierla rimette la scelta di sistema.
            if (deleteIfNoBackup && File.Exists(file))
            {
                File.Delete(file);
                return true;
            }

            return false;
        }
    }
}
