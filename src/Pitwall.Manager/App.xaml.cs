using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using Pitwall.ContentManager;
using Pitwall.Manager.Services;
using Pitwall.Storage;

namespace Pitwall.Manager
{
    public partial class App : Application
    {
        /// <summary>
        /// Supporta un avvio senza interfaccia per esportare i preset di Content Manager:
        ///
        ///     Pitwall.Manager.exe --export-cm [idCampionato]
        ///
        /// Serve per rigenerare il calendario di Content Manager da uno script, e per provare
        /// l'esportazione senza dover cliccare nella finestra.
        /// </summary>
        protected override void OnStartup(StartupEventArgs e)
        {
            // Il motore cerca Assetto Corsa dove sta di solito, perche il registro di Windows non
            // e raggiungibile da la senza aggiungere una libreria da copiare a mano accanto a
            // quelle del plugin di SimHub. Qui invece il registro c'e: si presta.
            AcContentScanner.SteamPathsFromRegistry = SteamPathsFromRegistry;

            var args = e.Args ?? new string[0];
            if (args.Any(x => string.Equals(x, "--crea-prova", StringComparison.OrdinalIgnoreCase)))
            {
                RunHeadlessCreate(args);
                Shutdown(0);
                return;
            }

            if (args.Any(a => string.Equals(a, "--export-cm", StringComparison.OrdinalIgnoreCase)))
            {
                RunHeadlessExport(args);
                Shutdown(0);
                return;
            }

            base.OnStartup(e);
        }

        /// <summary>Dove Steam dice di essere installato, secondo il registro.</summary>
        private static IEnumerable<string> SteamPathsFromRegistry()
        {
            foreach (var key in new[]
                     {
                         @"HKEY_CURRENT_USER\Software\Valve\Steam",
                         @"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Valve\Steam",
                         @"HKEY_LOCAL_MACHINE\SOFTWARE\Valve\Steam"
                     })
            {
                string steam = null;
                try
                {
                    steam = Microsoft.Win32.Registry.GetValue(key, "SteamPath", null) as string
                         ?? Microsoft.Win32.Registry.GetValue(key, "InstallPath", null) as string;
                }
                catch (Exception)
                {
                }

                if (!string.IsNullOrWhiteSpace(steam)) yield return steam;
            }
        }

        /// <summary>
        /// Crea un campionato di prova e lo esporta, dalla riga di comando.
        ///
        /// Passa dallo stesso costruttore che usa il configuratore, quindi collaudare questo
        /// vuol dire collaudare quello: se il percorso fosse diverso la prova non proverebbe
        /// niente.
        /// </summary>
        private static void RunHeadlessCreate(string[] args)
        {
            var report = new StringBuilder();
            try
            {
                PitwallPaths.EnsureFolders();

                int index = Array.FindIndex(args, a => string.Equals(a, "--crea-prova", StringComparison.OrdinalIgnoreCase));
                string nome = index >= 0 && index + 1 < args.Length && !args[index + 1].StartsWith("--")
                    ? args[index + 1] : "Campionato di prova";
                int vetture = index >= 0 && index + 2 < args.Length && int.TryParse(args[index + 2], out var n)
                    ? n : 20;

                var championship = ChampionshipBuilder.Sample(nome, vetture);
                ChampionshipStore.Save(championship);

                report.AppendLine("Creato: " + championship.Name);
                report.AppendLine("Piloti: " + championship.Drivers.Count);
                report.AppendLine("Scuderie: " + championship.Teams.Count);
                report.AppendLine("Gare: " + championship.Calendar.Count);
                report.AppendLine("Al volante: " + (championship.PlayerDriver != null
                                                     ? championship.PlayerDriver.FullName : "nessuno"));

                string acRoot = AcContentScanner.FindInstallation();
                var cars = acRoot != null ? AcContentScanner.Cars(acRoot) : null;
                var result = ContentManagerExporter.Export(championship, null, true, cars);
                report.AppendLine("Preset scritti: " + result.Written);
                report.AppendLine("Cartella: " + result.Folder);
                foreach (var note in result.Notes) report.AppendLine("Nota: " + note);
            }
            catch (Exception ex)
            {
                report.AppendLine("ERRORE: " + ex);
            }
            finally
            {
                try
                {
                    File.WriteAllText(Path.Combine(Path.GetTempPath(), "pitwall-crea-prova.txt"),
                                      report.ToString(), Encoding.UTF8);
                }
                catch (Exception)
                {
                }
            }
        }

        private static void RunHeadlessExport(string[] args)
        {
            var report = new StringBuilder();
            try
            {
                PitwallPaths.EnsureFolders();

                int index = Array.FindIndex(args, a => string.Equals(a, "--export-cm", StringComparison.OrdinalIgnoreCase));
                string id = index >= 0 && index + 1 < args.Length && !args[index + 1].StartsWith("--")
                    ? args[index + 1]
                    : null;

                var summary = id != null
                    ? ChampionshipStore.List().FirstOrDefault(s => s.Id == id)
                    : ChampionshipStore.List().FirstOrDefault();

                if (summary == null) { report.AppendLine("Nessun campionato trovato."); return; }

                var championship = ChampionshipStore.Load(summary.Id);
                if (championship == null) { report.AppendLine("Campionato non leggibile: " + summary.Name); return; }

                report.AppendLine("Campionato: " + championship.Name);

                string acRoot = AcContentScanner.FindInstallation();
                var cars = acRoot != null ? AcContentScanner.Cars(acRoot) : null;
                var result = ContentManagerExporter.Export(championship, null, true, cars);
                report.AppendLine("Preset scritti: " + result.Written);
                report.AppendLine("Cartella: " + result.Folder);
                foreach (var note in result.Notes) report.AppendLine("Nota: " + note);
            }
            catch (Exception ex)
            {
                report.AppendLine("ERRORE: " + ex);
            }
            finally
            {
                try
                {
                    File.WriteAllText(Path.Combine(Path.GetTempPath(), "pitwall-export-cm.txt"),
                                      report.ToString(), Encoding.UTF8);
                }
                catch (Exception)
                {
                }
            }
        }
    }
}
