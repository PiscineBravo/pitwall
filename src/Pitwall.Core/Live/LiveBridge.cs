using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.IO;
using System.Text;
using Pitwall.Storage;

namespace Pitwall.Live
{
    /// <summary>
    /// The link between the SimHub plugin, the manager application and the in-game companion.
    ///
    /// It is deliberately a pair of small files rather than a socket: every side can start and
    /// stop independently, nothing has to be running for the others to work, and the whole thing
    /// is trivial to inspect when something goes wrong. Writes are atomic, so a reader never
    /// sees half a document.
    /// </summary>
    public static class LiveBridge
    {
        private static DateTime _lastWrite = DateTime.MinValue;

        /// <summary>Minimum interval between two state writes, in milliseconds.</summary>
        public static int WriteIntervalMs { get; set; } = 250;

        /// <summary>
        /// Publishes the live state. Calls that arrive faster than the write interval are
        /// dropped, so this is safe to call from the telemetry loop at full rate.
        /// </summary>
        public static void PublishState(LiveState state, bool force = false)
        {
            if (state == null) return;
            var now = DateTime.UtcNow;
            if (!force && (now - _lastWrite).TotalMilliseconds < WriteIntervalMs) return;
            _lastWrite = now;

            state.UpdatedUtc = now;
            // Contatore che sale a ogni pubblicazione. Serve alle app in pista per distinguere
            // "il file c'e" da "i dati sono vivi": se il plugin si ferma il file resta li, e
            // senza questo si continuerebbe a leggere una fotografia vecchia come se fosse ora.
            state.Sequence++;
            try
            {
                PitwallPaths.EnsureFolders();
                string json = ChampionshipStore.Serialize(state);

                WriteAtomic(PitwallPaths.LiveStateFile, json);
                WriteCompanionFile(state, PitwallPaths.LiveStateFile);

                // Seconda copia dentro i documenti di Assetto Corsa: e li che le app in pista
                // hanno il permesso di leggere.
                Directory.CreateDirectory(PitwallPaths.AcDocumentsFolder);
                WriteAtomic(PitwallPaths.AcLiveStateFile, json);
                WriteCompanionFile(state, PitwallPaths.AcLiveStateFile);
            }
            catch (IOException)
            {
                // The manager may be reading at this instant; the next frame will publish again.
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        /// <summary>Reads the last published state, or null when the plugin is not running.</summary>
        public static LiveState ReadState()
        {
            try
            {
                if (!File.Exists(PitwallPaths.LiveStateFile)) return null;
                string json = ReadShared(PitwallPaths.LiveStateFile);
                if (string.IsNullOrWhiteSpace(json)) return null;
                var state = ChampionshipStore.Deserialize<LiveState>(json);

                // Anything older than a few seconds means the plugin stopped.
                if (state != null && (DateTime.UtcNow - state.UpdatedUtc).TotalSeconds > 5)
                    state.PluginRunning = false;

                return state;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>Queues a command for the plugin.</summary>
        public static void SendCommand(PitwallCommand command)
        {
            if (command == null) return;
            try
            {
                PitwallPaths.EnsureFolders();
                var queue = ReadCommands(false);
                queue.Add(command);
                WriteAtomic(PitwallPaths.CommandFile, ChampionshipStore.Serialize(queue));
            }
            catch (IOException)
            {
            }
        }

        public static void SendCommand(string name, string argument = "", double value = 0) =>
            SendCommand(new PitwallCommand { Name = name, Argument = argument, Value = value });

        /// <summary>
        /// Takes every queued command and clears the queue. Called by the plugin a few times a
        /// second.
        /// </summary>
        public static List<PitwallCommand> TakeCommands()
        {
            var commands = ReadCommands(true);
            commands.AddRange(TakeAcCommands());
            return commands;
        }

        /// <summary>
        /// Comandi scritti dalle app in pista, in un file di testo con una riga per comando:
        /// nome e argomento separati da uno spazio. Formato minimo apposta, perche dall'altra
        /// parte c'e uno script Lua che scrive e basta.
        /// </summary>
        private static List<PitwallCommand> TakeAcCommands()
        {
            var result = new List<PitwallCommand>();
            string path = Path.Combine(PitwallPaths.AcDocumentsFolder, "command.txt");

            try
            {
                if (!File.Exists(path)) return result;
                string text = ReadShared(path);
                File.Delete(path);

                foreach (var line in text.Split('\n'))
                {
                    string trimmed = line.Trim();
                    if (trimmed.Length == 0) continue;

                    // "nome", "nome argomento" oppure "nome argomento valore": il terzo pezzo e
                    // un numero quando serve, per esempio i metri che mancano alla piazzola.
                    var parts = trimmed.Split(new[] { ' ' }, 3, StringSplitOptions.RemoveEmptyEntries);
                    var command = new PitwallCommand { Name = parts[0] };
                    if (parts.Length > 1) command.Argument = parts[1];
                    if (parts.Length > 2)
                    {
                        double value;
                        if (double.TryParse(parts[2], NumberStyles.Float,
                                            CultureInfo.InvariantCulture, out value)) command.Value = value;
                        else command.Argument = parts[1] + " " + parts[2];
                    }
                    result.Add(command);
                }
            }
            catch (Exception)
            {
            }

            return result;
        }

        private static List<PitwallCommand> ReadCommands(bool clear)
        {
            var result = new List<PitwallCommand>();
            try
            {
                if (!File.Exists(PitwallPaths.CommandFile)) return result;
                string json = ReadShared(PitwallPaths.CommandFile);
                if (!string.IsNullOrWhiteSpace(json))
                {
                    var parsed = ChampionshipStore.Deserialize<List<PitwallCommand>>(json);
                    if (parsed != null) result = parsed;
                }
                if (clear && result.Count > 0)
                    WriteAtomic(PitwallPaths.CommandFile, "[]");
            }
            catch (Exception)
            {
            }
            return result;
        }

        /// <summary>
        /// A compact, line-based view of race control for the in-game Lua companion, which has
        /// no JSON parser and reads this once a second.
        /// </summary>
        private static void WriteCompanionFile(LiveState state, string besideStateFile)
        {
            var sb = new StringBuilder();
            sb.Append("status=").Append(state.RaceControlStatus).Append('\n');
            sb.Append("text=").Append(state.RaceControlText).Append('\n');
            sb.Append("delta=").Append(state.Delta.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)).Append('\n');
            sb.Append("targetkmh=").Append(state.TargetSpeedKmh.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)).Append('\n');
            sb.Append("pitclosed=").Append(state.PitLaneClosed ? "1" : "0").Append('\n');
            sb.Append("yellow=").Append(string.Join(",", state.YellowSectors)).Append('\n');
            sb.Append("message=").Append((state.EngineerMessage ?? "").Replace('\n', ' ')).Append('\n');
            sb.Append("penalties=").Append(state.PendingPenaltySeconds.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)).Append('\n');
            sb.Append("position=").Append(state.Position).Append('\n');
            sb.Append("lap=").Append(state.LapCurrent).Append('/').Append(state.LapsTotal).Append('\n');
            sb.Append("scphase=").Append(state.SafetyCarPhase).Append('\n');
            sb.Append("seq=").Append(state.Sequence).Append('\n');
            sb.Append("fphase=").Append(state.FormationPhase).Append('\n');

            // Procedura di griglia: la piazzola che spetta, cosa fare adesso, la posizione da
            // restituire e i margini accettati. L'app in pista li usa per disegnare la piazzola
            // e per decidere se la vettura e dentro.
            var invariant = System.Globalization.CultureInfo.InvariantCulture;
            // Qualifiche a eliminazione: manche in corso, quanto manca, quanti passano.
            sb.Append("qseg=").Append(state.QualifyingSegment).Append('\n');
            sb.Append("qleft=").Append((int)state.QualifyingSecondsLeft).Append('\n');

            // Il cronometro vero della sessione, in secondi. Zero vuol dire "a giri, nessun limite
            // di tempo": Assetto Corsa lo esprime con una sentinella enorme - novecento miliardi di
            // secondi - e qui arriva gia ripulito. Serve all'HUD per mostrare quanto manca, e ai
            // collaudi per vedere su quale orologio stanno ragionando le manche di qualifica,
            // invece di doverlo dedurre.
            sb.Append("sleft=").Append((int)state.SessionTimeLeftSeconds).Append('\n');
            sb.Append("selapsed=").Append((int)state.SessionElapsedSeconds).Append('\n');
            sb.Append("qsurv=").Append(state.QualifyingSurviving).Append('\n');
            sb.Append("lights=").Append(state.LightsOn).Append('\n');
            sb.Append("pitstart=").Append(state.PitLaneStart ? "1" : "0").Append('\n');
            sb.Append("pitstartdelay=").Append(state.PitLaneStartDelaySeconds.ToString("0.00", invariant)).Append('\n');
            sb.Append("auto=").Append(state.Autopilot ? "1" : "0").Append('\n');
            sb.Append("gridslot=").Append(state.GridSlot).Append('\n');
            sb.Append("gridnotice=").Append((state.GridNotice ?? "").Replace('\n', ' ')).Append('\n');
            sb.Append("giveback=").Append(state.PositionToGiveBack).Append('\n');
            sb.Append("gbleft=").Append(state.GiveBackSecondsLeft.ToString("0.0", invariant)).Append('\n');
            sb.Append("gtalong=").Append(state.GridToleranceAlong.ToString("0.00", invariant)).Append('\n');
            sb.Append("gtacross=").Append(state.GridToleranceAcross.ToString("0.00", invariant)).Append('\n');

            // DRS e benzina. Qui passa la meta della regola che tocca all'app in pista: il
            // distacco oltre il quale l'ala resta chiusa, e i margini con cui trasformare in
            // litri i giri da percorrere. Consumo e capienza del serbatoio li sa solo lei.
            sb.Append("drs=").Append(state.DrsEnabledNow ? "1" : "0").Append('\n');
            sb.Append("drsnotice=").Append((state.DrsReason ?? "").Replace('\n', ' ')).Append('\n');
            sb.Append("drsgap=").Append(state.DrsDetectionGapSeconds.ToString("0.00", invariant)).Append('\n');
            sb.Append("drsplayer=").Append(state.DrsAppliesToPlayer ? "1" : "0").Append('\n');
            sb.Append("lingua=").Append(state.Lingua).Append('\n');
            sb.Append("fuelrun=").Append(state.FuelRunMargin.ToString("0.00", invariant)).Append('\n');
            sb.Append("fuelrace=").Append(state.FuelRaceMargin.ToString("0.00", invariant)).Append('\n');
            sb.Append("fuelres=").Append(state.FuelReserveLitres.ToString("0.0", invariant)).Append('\n');

            // La classifica, una riga per vettura, nell'ordine in cui corrono. L'HUD in pista
            // disegna questa: posizione, sigla, distacco, mescola, giri sulle gomme, e se e il
            // giocatore. Si fermano a quindici righe perche piu di cosi su uno schermo di gara
            // non si leggono comunque.
            foreach (var row in state.Timing.Take(15))
                sb.Append("pos=")
                  .Append(row.Position).Append('|')
                  .Append(Clean(row.Abbreviation)).Append('|')
                  .Append(Clean(row.Interval)).Append('|')
                  .Append(Clean(row.Compound)).Append('|')
                  .Append(row.TyreAge).Append('|')
                  .Append(row.IsPlayer ? "1" : "0").Append('|')
                  .Append(row.InPit ? "1" : "0").Append('|')
                  // 0 in corsa, 1 gia eliminato, 2 in zona esclusione adesso.
                  .Append(row.Eliminated ? "1" : row.InDropZone ? "2" : "0").Append('\n');

            // Gli scenari di strategia, uno per riga: servono al pannello dell'ingegnere in pista,
            // dove si sceglie col volante senza aprire niente.
            foreach (var option in state.StrategyOptions.Take(4))
                sb.Append("str=")
                  .Append(Clean(option.Id)).Append('|')
                  .Append(Clean(option.Name)).Append('|')
                  .Append(Clean(option.Summary)).Append('|')
                  .Append(option.DeltaSeconds.ToString("0.0", invariant)).Append('|')
                  .Append(option.ProjectedPosition).Append('|')
                  .Append(option.Risk).Append('|')
                  .Append(option.Recommended ? "1" : "0").Append('|')
                  .Append(option.Selected ? "1" : "0").Append('\n');

            sb.Append("tyre=").Append(Clean(state.TyreCompound)).Append('\n');
            sb.Append("nexttyre=").Append(Clean(state.RecommendedCompound)).Append('\n');

            // Il piano meteo, un punto per riga. Serve al controller di Custom Shaders Patch, che
            // gira in un contesto piu chiuso delle app e non e detto che possa leggere file: l'app
            // in pista legge queste righe e gliele passa per il canale condiviso fra script.
            // In MINUTI, come il nome dice e come sono i punti del piano.
            //
            // Qui per molto tempo sono finiti i secondi. Il controller del meteo se ne era
            // accorto e si era aggiustato dividendo per sessanta, cosi in pista il tempo era
            // quello giusto; ma il radar e la previsione leggevano lo stesso numero senza
            // correggerlo, e dopo un minuto di sessione erano gia oltre la fine del piano.
            // Era il motivo per cui il pannello diceva pioggia forte su pista asciutta.
            sb.Append("wxmin=").Append((state.SessionElapsedSeconds / 60.0).ToString("0.00", invariant)).Append('\n');
            foreach (var p in state.WeatherTimeline)
                sb.Append("wx=")
                  .Append(p.Minute.ToString("0.##", invariant)).Append('|')
                  .Append(p.Rain.ToString("0.###", invariant)).Append('|')
                  .Append(p.Type).Append('|')
                  .Append(p.AirTemperature.ToString("0.#", invariant)).Append('|')
                  .Append(p.RoadTemperature.ToString("0.#", invariant)).Append('|')
                  .Append(p.Wetness.ToString("0.###", invariant)).Append('|')
                  .Append(p.WindKmh.ToString("0.#", invariant)).Append('|')
                  .Append(p.WindDirection).Append('\n');

            // Una riga per vettura. L'app in pista le raccoglie tutte e le traduce in chiamate
            // a physics.*: livello, limite di velocita, rientro ai box, mescola.
            foreach (var command in state.AiCommands)
                sb.Append("ai=").Append(command).Append('\n');

            string path = Path.Combine(Path.GetDirectoryName(besideStateFile) ?? ".", "race_control.txt");
            WriteAtomic(path, sb.ToString());
        }

        /// <summary>Toglie i caratteri che romperebbero il formato a righe e barre verticali.</summary>
        private static string Clean(string text) =>
            string.IsNullOrEmpty(text) ? "" : text.Replace('|', ' ').Replace('\n', ' ').Trim();

        /// <summary>
        /// UTF-8 senza marcatore iniziale. Encoding.UTF8 scrive un BOM, e tre byte invisibili
        /// prima della graffa bastano a far fallire in silenzio il parser JSON degli script in
        /// pista: il file viene letto, ma non ne esce niente.
        /// </summary>
        private static readonly Encoding Utf8NoBom = new UTF8Encoding(false);

        private static void WriteAtomic(string path, string content)
        {
            string directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

            string temp = path + ".tmp";
            File.WriteAllText(temp, content, Utf8NoBom);
            if (File.Exists(path)) File.Delete(path);
            File.Move(temp, path);
        }

        private static string ReadShared(string path)
        {
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var reader = new StreamReader(stream, Encoding.UTF8))
            {
                return reader.ReadToEnd();
            }
        }
    }
}
