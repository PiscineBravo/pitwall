using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Pitwall.Model;
using Pitwall.Storage;
using Pitwall.Weather;

namespace Pitwall.ContentManager
{
    /// <summary>Un preset di Quick Drive gia salvato in Content Manager.</summary>
    public sealed class CmPreset
    {
        public string Name { get; set; } = "";
        public string FilePath { get; set; } = "";
        public string TrackId { get; set; } = "";
        public string CarId { get; set; } = "";

        public override string ToString() =>
            Name + (string.IsNullOrEmpty(CarId) ? "" : "   (" + CarId + ")");
    }

    public sealed class CmExportResult
    {
        public int Written { get; set; }
        public string Folder { get; set; } = "";
        public List<string> Notes { get; } = new List<string>();
    }

    /// <summary>
    /// Genera i preset di Quick Drive di Content Manager, uno per ogni gara del calendario, con
    /// la griglia di partenza gia costruita: venti vetture, nomi, livree, abilita e aggressivita
    /// di ognuno, piu pista, ora, temperatura e numero di giri.
    ///
    /// Il formato non e documentato ed e stato ricavato dall'eseguibile di Content Manager. Un
    /// preset e un JSON su tre livelli, ognuno annidato nel precedente come stringa:
    ///
    ///   QuickDrive.SaveableData
    ///     Mode = /Pages/Drive/QuickDrive_Weekend.xaml, CarId, TrackId, Time, Temperature ...
    ///     ModeData -> QuickDrive_Weekend.ViewModel.SaveableData
    ///                   PracticeLength, QualificationLength, Penalties, JumpStartPenalty, LapsNumber
    ///                   RaceGridSerialized -> RaceGridViewModel.SaveableData
    ///                                           ModeId, CarIds[], Names[], SkinIds[],
    ///                                           AiLevels[], AiAggressions[], OpponentsNumber ...
    ///
    /// Si usa la modalita fine settimana, non quella gara: cosi Content Manager fa girare prove
    /// libere, qualifiche e gara una dopo l'altra, e la griglia di partenza esce dalle qualifiche
    /// invece di essere decisa a tavolino. La modalita gara resta disponibile come opzione.
    ///
    /// Le modalita di griglia previste sono same_car, same_group, manual e filtered_by_*.
    /// Qui si usa sempre "manual": e l'unica in cui ogni vettura, nome e livrea sono elencati
    /// uno per uno, che e esattamente quello che serve a un campionato.
    /// </summary>
    public static class ContentManagerExporter
    {
        private const string RaceModeUri = "/Pages/Drive/QuickDrive_Race.xaml";
        private const string WeekendModeUri = "/Pages/Drive/QuickDrive_Weekend.xaml";
        /// <summary>
        /// Il modo griglia di Content Manager. Deve valere "custom": e la voce Custom del menu,
        /// l'unica che accetta un elenco di vetture scelte da noi. Con un valore che non
        /// riconosce, Content Manager non segnala niente e ricade sulla sua griglia predefinita,
        /// che sono tre avversari.
        /// </summary>
        private const string CustomGridMode = "custom";

        /// <summary>
        /// Versione del formato di ModeData. Senza, Content Manager legge il blocco con regole
        /// vecchie e la griglia non arriva. Preso dai preset che salva lui.
        /// </summary>
        private const int ModeDataVersion = 2;

        // ---- Percorsi ---------------------------------------------------------------------

        /// <summary>Cartella dei preset di Quick Drive, o null se Content Manager non c'e.</summary>
        public static string QuickDriveFolder()
        {
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string cm = Path.Combine(local, "AcTools Content Manager");
            if (!Directory.Exists(cm)) return null;
            return Path.Combine(cm, "Presets", "Quick Drive");
        }

        /// <summary>I preset gia salvati, utilizzabili come base al posto di quella predefinita.</summary>
        public static List<CmPreset> Templates()
        {
            var result = new List<CmPreset>();
            string folder = QuickDriveFolder();
            if (folder == null || !Directory.Exists(folder)) return result;

            foreach (var file in Directory.GetFiles(folder, "*.cmpreset", SearchOption.AllDirectories))
            {
                try
                {
                    var json = JObject.Parse(File.ReadAllText(file));
                    result.Add(new CmPreset
                    {
                        FilePath = file,
                        Name = Path.ChangeExtension(file.Substring(folder.Length).TrimStart('\\', '/'), null),
                        TrackId = (string)json["TrackId"] ?? "",
                        CarId = (string)json["CarId"] ?? ""
                    });
                }
                catch (Exception)
                {
                }
            }

            return result.OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        }

        // ---- Esportazione ---------------------------------------------------------------------

        /// <summary>
        /// Scrive un preset per ogni gara del calendario, dentro una sottocartella col nome del
        /// campionato. Se <paramref name="baseOn"/> e valorizzato si parte da quel preset e se ne
        /// sostituiscono pista, condizioni e griglia, conservando tutto il resto.
        /// </summary>
        /// <summary>
        /// Cancella la cartella di preset di un campionato dentro Content Manager.
        ///
        /// Si tocca solo la cartella che porta il nome del campionato, dentro i preset di Quick
        /// Drive: niente che Pitwall non abbia scritto. Restituisce vero se c'era qualcosa da
        /// togliere.
        /// </summary>
        /// <summary>
        /// Il file che marca una cartella di preset come scritta da Pitwall.
        ///
        /// Serve per poterle cancellare senza rischi. Content Manager tiene i preset di
        /// tutti nella stessa cartella, e andare a eliminare per nome quello che si crede
        /// proprio vuol dire, prima o poi, cancellare il preset di qualcun altro che si
        /// chiamava uguale. Con il marcatore la domanda ha una risposta certa.
        /// </summary>
        private const string Marcatore = ".pitwall";

        /// <summary>
        /// Toglie le cartelle di preset rimaste senza il loro campionato.
        ///
        /// Si cancella solo quello che porta il marcatore di Pitwall e che non corrisponde a
        /// nessun campionato esistente. E la rete che fa tornare i conti anche quando
        /// un'eliminazione e andata storta: senza, in Content Manager restavano campionati
        /// che nel Manager non esistevano piu, e nessuno capiva da dove arrivassero.
        /// </summary>
        public static int RemoveOrphans(IEnumerable<string> championshipNames)
        {
            string root = QuickDriveFolder();
            if (root == null || !Directory.Exists(root)) return 0;

            var vivi = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var nome in championshipNames ?? Enumerable.Empty<string>())
                if (!string.IsNullOrWhiteSpace(nome))
                    vivi.Add(PitwallPaths.SanitiseFileName(nome));

            int tolte = 0;
            foreach (var cartella in Directory.GetDirectories(root))
            {
                if (!File.Exists(Path.Combine(cartella, Marcatore))) continue;
                if (vivi.Contains(Path.GetFileName(cartella))) continue;

                try { Directory.Delete(cartella, true); tolte++; }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }

            return tolte;
        }

        public static bool RemovePresets(string championshipName)
        {
            if (string.IsNullOrWhiteSpace(championshipName)) return false;

            string root = QuickDriveFolder();
            if (root == null) return false;

            string folder = Path.Combine(root, PitwallPaths.SanitiseFileName(championshipName));
            if (!Directory.Exists(folder)) return false;

            // Controprova prima di cancellare: la cartella deve stare davvero dentro i preset di
            // Quick Drive. Un nome storto non deve poter portare la cancellazione altrove.
            string atteso = Path.GetFullPath(root);
            string vero = Path.GetFullPath(folder);
            if (!vero.StartsWith(atteso, StringComparison.OrdinalIgnoreCase) || vero.Length <= atteso.Length)
                return false;

            try
            {
                Directory.Delete(folder, true);
                return true;
            }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
        }

        public static CmExportResult Export(Championship championship, CmPreset baseOn = null,
                                            bool weekend = true, IEnumerable<string> availableCars = null)
        {
            var result = new CmExportResult();
            if (championship == null) return result;

            string root = QuickDriveFolder();
            if (root == null) { result.Notes.Add("Content Manager non trovato su questo PC."); return result; }

            var player = championship.PlayerDriver;
            if (player == null)
            {
                result.Notes.Add("Nessun pilota e contrassegnato come il tuo: scegline uno nella scheda Piloti.");
                return result;
            }

            var playerTeam = championship.TeamById(player.TeamId);
            if (playerTeam == null || string.IsNullOrWhiteSpace(playerTeam.AcCarId))
            {
                result.Notes.Add("Il team di " + player.FullName + " non ha un'auto di Assetto Corsa impostata.");
                return result;
            }

            string folder = Path.Combine(root, PitwallPaths.SanitiseFileName(championship.Name));
            Directory.CreateDirectory(folder);
            result.Folder = folder;

            // Si lascia il segno: da qui in poi questa cartella si sa di chi e, e si puo
            // togliere senza chiedersi se era di qualcun altro.
            try
            {
                File.WriteAllText(Path.Combine(folder, Marcatore),
                    "Cartella generata da Pitwall per il campionato \"" + championship.Name + "\".\r\n" +
                    "Si cancella da sola quando il campionato viene eliminato dal Manager.\r\n");
            }
            catch (IOException)
            {
            }

            string templateJson = baseOn != null && File.Exists(baseOn.FilePath)
                ? File.ReadAllText(baseOn.FilePath)
                : null;

            // Chi chiama da fuori l'applicazione Windows - il plugin di SimHub, che riscrive il
            // preset appena una sessione si chiude - non ha una lista di auto installate da
            // passare. Se la cerca da solo: senza, la safety car finirebbe in griglia con un
            // modello che su questo PC non esiste, e Content Manager scarterebbe il preset.
            var auto = availableCars;
            if (auto == null)
            {
                try
                {
                    string acRoot = AcContentScanner.FindInstallation();
                    if (acRoot != null) auto = AcContentScanner.Cars(acRoot);
                }
                catch (IOException) { }
            }

            string safetyCarId = ResolveSafetyCar(championship, auto);

            // Si esporta **una gara sola**: quella che tocca adesso.
            //
            // Prima si scriveva un preset per ogni gara del calendario, tutte insieme. In Content
            // Manager comparivano cosi ventiquattro voci selezionabili in qualunque ordine, e
            // niente impediva di correre due volte la stessa gara sovrascrivendone il risultato,
            // o di correre Abu Dhabi prima di Melbourne. Un campionato in cui si puo tornare
            // indietro non e un campionato.
            //
            // Adesso il Manager e l'unico che tiene il conto: in Content Manager si vede solo la
            // prossima gara, e appena il suo risultato e registrato quel preset sparisce e compare
            // il successivo.
            var round = championship.NextRound();

            if (round == null)
            {
                PruneOtherPresets(folder, null);
                result.Notes.Add("Campionato concluso: non c'e nessuna gara da correre.");
                return result;
            }

            // La griglia della gara viene dalle qualifiche di quel fine settimana, se sono state
            // corse. Finche non lo sono, resta l'ordine degli iscritti.
            var ordine = GridOrder(championship, round);

            string grid = BuildGrid(championship, player, safetyCarId, ordine,
                                    out int opponents, out int playerSlot);

            if (opponents == 0)
                result.Notes.Add("Attenzione: nessun avversario. Controlla che i team abbiano un'auto impostata.");
            if (safetyCarId != null)
                result.Notes.Add("Safety car in griglia: " + safetyCarId + ", tenuta ai box finche non serve.");
            else if (championship.Rules.RaceControl.SafetyCar.UseRealCar)
                result.Notes.Add("Nessuna auto adatta come safety car fra quelle installate: " +
                                 "la procedura resta senza vettura in pista.");

            {
                var circuit = championship.CircuitOf(round);
                if (circuit == null)
                {
                    result.Notes.Add("Saltata: " + round.Name + " (circuito mancante).");
                    PruneOtherPresets(folder, null);
                    return result;
                }

                var session = round.Session(SessionKind.Race) ?? round.Sessions.FirstOrDefault();
                var preset = templateJson != null ? JObject.Parse(templateJson) : NewPreset();

                preset["Mode"] = weekend ? WeekendModeUri : RaceModeUri;
                preset["CarId"] = playerTeam.AcCarId;
                preset["TrackId"] = TrackId(circuit);

                if (session != null)
                {
                    preset["Time"] = (int)session.StartTimeOfDay.TotalSeconds;
                    preset["Temperature"] = Math.Round(Temperature(session, circuit), 1);

                    // Il tipo di tempo va scritto sempre: senza, Content Manager ripiega su
                    // "Casuale", che puo tirare fuori la nebbia. Il controller di Pitwall lo
                    // riceve e lo ignora, perche a comandare e il piano della sessione.
                    preset["WeatherId"] = WeatherId(session);
                }

                // Le condizioni reali sovrascriverebbero ora e temperatura appena scelte.
                preset["RealConditions"] = false;
                preset["RandomTime"] = false;
                preset["RandomTemperature"] = false;

                preset["ModeData"] = BuildModeData(championship, round, session, circuit, grid, weekend);

                string name = round.Order.ToString("00") + " - " +
                              PitwallPaths.SanitiseFileName(round.Name) + ".cmpreset";
                File.WriteAllText(Path.Combine(folder, name), preset.ToString(Formatting.None));
                result.Written++;

                // Via tutto il resto: se restasse il preset di una gara gia corsa si potrebbe
                // ancora selezionarla.
                PruneOtherPresets(folder, name);
            }

            if (result.Written > 0)
            {
                result.Notes.Add("In Content Manager: " + round.Order + ". " + round.Name + ".");
                result.Notes.Add(opponents + " avversari in griglia, " + player.FullName +
                                 " al volante, " + (playerSlot > 0
                                     ? "in " + playerSlot + "a casella."
                                     : "dall ultima casella."));

                result.Notes.Add(ordine != null
                    ? "Griglia dalle qualifiche di questo fine settimana."
                    : "Qualifiche non ancora corse: la griglia la decidono quelle.");
            }

            return result;
        }

        /// <summary>
        /// Toglie dalla cartella ogni preset che non sia quello da tenere.
        ///
        /// E quello che rende vera la regola di una gara alla volta: finche in cartella resta il
        /// preset di una gara, quella gara si puo selezionare. Si cancellano solo i .cmpreset, e
        /// solo dentro una cartella che porta il marcatore di Pitwall: un nome storto non deve
        /// poter portare la cancellazione fra i preset di qualcun altro.
        /// </summary>
        private static int PruneOtherPresets(string folder, string keep)
        {
            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return 0;
            if (!File.Exists(Path.Combine(folder, Marcatore))) return 0;

            int tolti = 0;
            foreach (var file in Directory.GetFiles(folder, "*.cmpreset"))
            {
                if (keep != null &&
                    string.Equals(Path.GetFileName(file), keep, StringComparison.OrdinalIgnoreCase))
                    continue;

                try { File.Delete(file); tolti++; }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }

            return tolti;
        }

        /// <summary>
        /// L'ordine di partenza della gara: le qualifiche di quel fine settimana.
        ///
        /// Restituisce gli identificativi dei piloti dalla pole all'ultima casella, oppure niente
        /// se le qualifiche non sono ancora state corse - e allora la griglia resta quella degli
        /// iscritti, che e il massimo che si sa prima di girare.
        /// </summary>
        private static List<string> GridOrder(Championship championship, Round round)
        {
            if (championship == null || round == null) return null;

            var quali = championship.ResultFor(round.Id, SessionKind.Qualifying)
                     ?? championship.ResultFor(round.Id, SessionKind.SprintQualifying);

            if (quali == null || quali.Entries == null || quali.Entries.Count == 0) return null;

            return quali.Entries.OrderBy(e => e.Position)
                                .Select(e => e.DriverId)
                                .Where(id => !string.IsNullOrEmpty(id))
                                .ToList();
        }

        /// <summary>
        /// Il meteo di partenza, scelto esplicitamente invece di lasciarlo su casuale.
        ///
        /// Due motivi. Il primo: con "casuale" Content Manager pesca fra i tipi installati, e
        /// fra quelli ci sono anche le due nebbie, che in un gran premio non si vogliono. Il
        /// secondo: se il controller meteo di Pitwall e attivo, e lui a comandare il tempo, e
        /// questo valore conta solo per il primo istante.
        ///
        /// Si usano i tipi base di Assetto Corsa, presenti su qualunque installazione.
        /// </summary>
        private static string WeatherId(SessionConfig session)
        {
            var sky = session.Weather != null && session.Weather.Phases.Count > 0
                ? session.Weather.Phases.OrderBy(p => p.StartMinute).First().Sky
                : SkyCondition.Clear;

            switch (sky)
            {
                case SkyCondition.Clear: return "3_clear";
                case SkyCondition.FewClouds: return "4_mid_clear";
                case SkyCondition.Scattered: return "5_light_clouds";
                case SkyCondition.Overcast: return "6_mid_clouds";
                default: return "7_heavy_clouds";   // con la pioggia ci pensa il controller
            }
        }

        private static double Temperature(SessionConfig session, Circuit circuit)
        {
            if (session.Weather == null) return circuit.TypicalAirTemperature;
            return session.Weather.SampleAt(0).AirTemperature;
        }

        /// <summary>
        /// Content Manager identifica un tracciato come "pista/layout" quando la pista ne ha piu
        /// di uno, e con la sola pista quando e unica.
        /// </summary>
        private static string TrackId(Circuit circuit)
        {
            return string.IsNullOrWhiteSpace(circuit.AcTrackConfig)
                ? circuit.AcTrackId
                : circuit.AcTrackId + "/" + circuit.AcTrackConfig;
        }

        private static JObject NewPreset()
        {
            return new JObject
            {
                ["Mode"] = RaceModeUri,
                ["RealConditions"] = false,
                ["Temperature"] = 24.0,
                ["Time"] = 54000,
                ["TimeMultiplier"] = 1,
                ["UseSpecificDate"] = false,
                ["TrackPropertiesChanged"] = false,
                ["AssistsChanged"] = false,
                ["IdealConditions"] = false,
                ["WindSpeedMin"] = 0.0,
                ["WindSpeedMax"] = 15.0,
                ["WindDirection"] = 0.0,
                ["RandomWindSpeed"] = true,
                ["RandomWindDirection"] = true,
                ["RandomTemperature"] = false,
                ["RandomTime"] = false,
                ["CustomRoadTemperature"] = false
            };
        }

        private static string BuildModeData(Championship championship, Round round, SessionConfig session,
                                            Circuit circuit, string gridSerialized, bool weekend)
        {
            int laps = session != null ? session.ResolveLaps(circuit) : 0;
            if (laps <= 0) laps = Math.Max(1, circuit.GrandPrixLaps);

            // Il giro di ricognizione consuma un giro del conteggio di Assetto Corsa: se ne
            // aggiunge uno, altrimenti la gara finirebbe un giro prima della distanza voluta.
            var formation = championship.Rules.RaceControl.Formation;
            if (session != null && session.FormationLap && formation != null &&
                formation.ReturnToGrid && formation.AddLapToRaceDistance)
                laps += 1;

            var mode = new JObject();

            if (weekend)
            {
                // Content Manager accetta le due sessioni solo in minuti, quindi una prova libera
                // impostata a giri viene convertita col giro di riferimento del circuito.
                mode["PracticeLength"] = Minutes(round, circuit, SessionKind.Practice1, 15);
                mode["QualificationLength"] = Minutes(round, circuit, SessionKind.Qualifying, 30);
            }

            mode["Penalties"] = championship.Rules.RaceControl.PitLaneSpeedingEnabled;
            // 0 nessuna, 1 penalita di tempo, 2 squalifica: la via di mezzo e quella giusta.
            mode["JumpStartPenalty"] = championship.Rules.RaceControl.JumpStartEnabled ? 1 : 0;
            mode["LapsNumber"] = laps;
            mode["RaceGridSerialized"] = gridSerialized;
            mode["Version"] = ModeDataVersion;

            return mode.ToString(Formatting.None);
        }

        /// <summary>Durata di una sessione in minuti, comunque sia stata configurata.</summary>
        private static int Minutes(Round round, Circuit circuit, SessionKind kind, int fallback)
        {
            var session = round.Session(kind);
            if (session == null || !session.Enabled) return fallback;

            switch (session.LengthMode)
            {
                case SessionLengthMode.Minutes:
                    return Math.Max(1, (int)Math.Round(session.Length));
                case SessionLengthMode.Laps:
                case SessionLengthMode.DistancePercent:
                    int laps = session.ResolveLaps(circuit);
                    double lapSeconds = circuit.ReferenceLapSeconds > 5 ? circuit.ReferenceLapSeconds : 95;
                    return Math.Max(1, (int)Math.Round(laps * lapSeconds / 60.0));
                default:
                    return fallback;
            }
        }

        /// <summary>
        /// Costruisce la griglia in modalita manuale: una riga per ogni pilota diverso da te,
        /// con la sua vettura, la sua livrea e i suoi parametri di intelligenza artificiale.
        /// </summary>
        /// <summary>
        /// Sceglie l'auto della safety car: quella indicata nel regolamento se c'e, altrimenti la
        /// prima delle preferite che risulti installata. La AMG GT Black Series e quella vera del
        /// campionato attuale.
        /// </summary>
        private static string ResolveSafetyCar(Championship championship, IEnumerable<string> availableCars)
        {
            var settings = championship.Rules.RaceControl.SafetyCar;
            if (settings == null || !settings.UseRealCar) return null;

            var installed = availableCars != null
                ? new HashSet<string>(availableCars, StringComparer.OrdinalIgnoreCase)
                : null;

            if (!string.IsNullOrWhiteSpace(settings.CarId))
                return installed == null || installed.Contains(settings.CarId) ? settings.CarId : null;

            var preferred = Pitwall.RaceControl.SafetyCar.PreferredCarIds;
            if (installed == null) return preferred.FirstOrDefault();
            return preferred.FirstOrDefault(installed.Contains);
        }

        /// <summary>
        /// La griglia di partenza, nell'ordine in cui si schiera.
        ///
        /// <paramref name="order"/> sono gli identificativi dei piloti dalla pole in giu, oppure
        /// niente per lasciare l'ordine degli iscritti. <paramref name="playerSlot"/> torna la
        /// casella del giocatore, che e l'unico modo per dirla a Content Manager: la sua vettura
        /// non sta nella lista delle altre.
        /// </summary>
        private static string BuildGrid(Championship championship, Driver player, string safetyCarId,
                                        IReadOnlyList<string> order,
                                        out int opponents, out int playerSlot)
        {
            // Chi si schiera davvero, nell'ordine giusto, giocatore compreso: la casella del
            // giocatore si conta su questa lista e non sul campionato, perche un pilota il cui team
            // non ha un'auto non occupa nessuna casella e sposterebbe il conto.
            var schierati = championship.Drivers
                .Where(d => d != null)
                .Where(d =>
                {
                    var t = championship.TeamById(d.TeamId);
                    return t != null && !string.IsNullOrWhiteSpace(t.AcCarId);
                })
                .ToList();

            if (order != null && order.Count > 0)
            {
                var posto = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < order.Count; i++)
                {
                    if (!string.IsNullOrEmpty(order[i]) && !posto.ContainsKey(order[i]))
                        posto[order[i]] = i;
                }

                // Chi non compare nell'ordine - un iscritto che non ha girato in qualifica - va in
                // fondo, mantenendo fra loro l'ordine degli iscritti.
                schierati = schierati
                    .Select((d, i) => new { Driver = d, Iscritto = i })
                    .OrderBy(x => posto.ContainsKey(x.Driver.Id ?? "") ? posto[x.Driver.Id] : int.MaxValue)
                    .ThenBy(x => x.Iscritto)
                    .Select(x => x.Driver)
                    .ToList();
            }

            // La casella si dichiara SOLO se la si sa davvero, cioe se arriva dalle qualifiche.
            //
            // Senza quell ordine questa lista e semplicemente l ordine degli iscritti, e dire al
            // gioco di partire da quella posizione vuol dire mettere il giocatore primo perche e il
            // primo della lista. E successo: qualificato ultimo, in griglia primo. Quando non si sa,
            // si parte dal fondo e la griglia vera la decide la qualifica che si sta per correre.
            playerSlot = order != null && order.Count > 0
                ? schierati.FindIndex(d => d.Id == player.Id) + 1
                : 0;

            var carIds = new List<string>();
            var names = new List<string>();
            var nationalities = new List<string>();
            var skinIds = new List<string>();
            var aiLevels = new List<double>();
            var aiAggressions = new List<double>();
            var ballasts = new List<double>();
            var restrictors = new List<double>();
            var priorities = new List<int>();
            var limitations = new List<string>();

            foreach (var driver in schierati.Where(d => d.Id != player.Id))
            {
                var team = championship.TeamById(driver.TeamId);
                if (team == null || string.IsNullOrWhiteSpace(team.AcCarId)) continue;

                carIds.Add(team.AcCarId);
                names.Add(driver.MatchName);
                nationalities.Add(driver.Nationality ?? "");
                skinIds.Add(SkinFor(championship, team, driver));
                aiLevels.Add(Clamp(driver.AiSkill, 70, 100));
                aiAggressions.Add(Clamp(driver.AiAggression, 0, 100));
                ballasts.Add(0);
                restrictors.Add(0);
                priorities.Add(1);
                limitations.Add("");
            }

            // La vettura di sicurezza e un'iscritta come le altre, altrimenti il gioco non la
            // metterebbe mai in pista. L'app in pista la porta ai box al via e la tiene ferma
            // finche la direzione gara non la chiama; nella classifica non compare.
            if (safetyCarId != null)
            {
                carIds.Add(safetyCarId);
                names.Add(Pitwall.RaceControl.SafetyCar.DriverName);
                nationalities.Add("");
                skinIds.Add(championship.Rules.RaceControl.SafetyCar.SkinId ?? "");
                aiLevels.Add(100);
                aiAggressions.Add(0);
                ballasts.Add(0);
                restrictors.Add(0);
                priorities.Add(1);
                limitations.Add("");
            }

            opponents = carIds.Count;

            // Ogni campo dello schema viene valorizzato, compresi quelli che sembrano
            // facoltativi: se Content Manager trova un valore mancante dove si aspetta una
            // lista, considera il blocco danneggiato e lo butta via tutto, tornando alla
            // griglia predefinita senza dire niente.
            var grid = new JObject
            {
                ["ModeId"] = CustomGridMode,
                ["FilterValue"] = "",
                ["RandomSkinsFilter"] = "",
                ["SequentialSkins"] = false,
                ["AvoidCarsWithoutNumbers"] = false,
                ["CarIds"] = new JArray(carIds),
                ["CandidatePriorities"] = new JArray(priorities),
                ["AiLevels"] = new JArray(aiLevels),
                ["AiAggressions"] = new JArray(aiAggressions),
                ["Ballasts"] = new JArray(ballasts),
                ["Restrictors"] = new JArray(restrictors),
                ["PlayerBallast"] = 0.0,
                ["PlayerRestrictor"] = 0.0,
                ["Names"] = new JArray(names),
                ["Nationalities"] = new JArray(nationalities),
                ["SkinIds"] = new JArray(skinIds),
                ["AiLimitations"] = new JArray(limitations),
                ["ShuffleCandidates"] = false,
                ["VarietyLimitation"] = 0,
                ["OpponentsNumber"] = opponents,
                // La casella del giocatore. Era fissa all'ultima, con la spiegazione che le
                // qualifiche decidono la griglia vera: ma la griglia vera non arrivava fin qui, e
                // chi faceva la pole partiva ventesimo. Adesso ci arriva.
                ["StartingPosition"] = playerSlot > 0 ? playerSlot : opponents + 1,
                ["AiLevelArrangeReverse"] = false,
                // Booleano, non un numero: scriverci 0 fa fallire la lettura dell'intero blocco.
                ["AiLevelArrangePowerRatio"] = false,
                ["AiLevelArrangeRandomly"] = false,
                ["AiLevelArrangeRandom"] = 0.0,
                ["AiLevel"] = aiLevels.Count > 0 ? aiLevels.Max() : 95,
                ["AiLevelMin"] = aiLevels.Count > 0 ? aiLevels.Min() : 95,
                ["AiAggressionArrangeReverse"] = false,
                ["AiAggressionArrangeRandom"] = 0.0,
                ["AiAggression"] = aiAggressions.Count > 0 ? aiAggressions.Max() : 50,
                ["AiAggressionMin"] = aiAggressions.Count > 0 ? aiAggressions.Min() : 50
            };

            return grid.ToString(Formatting.None);
        }

        /// <summary>
        /// La livrea del pilota: la prima o la seconda del team secondo l'ordine in cui i due
        /// piloti compaiono nella lista. Vuota se il team non ne ha, e allora Content Manager
        /// ne sceglie una lui.
        /// </summary>
        private static string SkinFor(Championship championship, Team team, Driver driver)
        {
            if (team.AcSkins == null || team.AcSkins.Count == 0) return "";

            var teamDrivers = championship.Drivers.Where(d => d.TeamId == team.Id).ToList();
            int index = teamDrivers.FindIndex(d => d.Id == driver.Id);
            if (index < 0) index = 0;

            return team.AcSkins[Math.Min(index, team.AcSkins.Count - 1)];
        }

        private static double Clamp(double value, double min, double max) =>
            Math.Max(min, Math.Min(max, value));
    }
}
