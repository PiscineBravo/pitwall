using System;
using System.Globalization;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Pitwall.Live;
using Pitwall.Model;
using Pitwall.Results;
using Pitwall.Scoring;
using Pitwall.Storage;

namespace Pitwall.Simulator
{
    /// <summary>
    /// Drives the whole Pitwall engine with synthetic telemetry, without Assetto Corsa and
    /// without SimHub.
    ///
    /// It exists for two reasons. It is the fastest way to check that race control, the tyre
    /// model, the engineer and the scoring still behave after a change; and it lets a
    /// championship be tuned - degradation, safety car frequency, points - by watching a whole
    /// season play out in a few seconds instead of a few months of evenings.
    /// </summary>
    public static class Program
    {
        public static int Main(string[] args)
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;

            // --lingua en fa parlare tutto in inglese.
            //
            // Il simulatore lavora in una cartella isolata e non legge le impostazioni vere:
            // senza questa riga non ci sarebbe modo di provare le traduzioni senza entrare in
            // gioco, e una traduzione che non si prova e una traduzione di cui non si sa niente.
            int iLingua = Array.FindIndex(args, x => string.Equals(x, "--lingua", StringComparison.OrdinalIgnoreCase));
            if (iLingua >= 0 && iLingua + 1 < args.Length)
            {
                Pitwall.Lingue.Testi.Lingua = args[iLingua + 1].StartsWith("en", StringComparison.OrdinalIgnoreCase)
                    ? Pitwall.Model.Lingua.Inglese
                    : Pitwall.Model.Lingua.Italiano;
            }

            if (args.Contains("--regressioni")) return RegressionChecks.Run();
            if (args.Contains("--qualifica")) return RunQualifying(args);
            if (args.Contains("--programmi")) return RunRunPlans(args);
            if (args.Contains("--drs")) return RunDrs();

            // Never touch the real Documents\Pitwall folder from a simulation.
            string sandbox = Path.Combine(Path.GetTempPath(), "PitwallSim-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            PitwallPaths.UseRoot(sandbox);
            PitwallPaths.EnsureFolders();

            int rounds = ArgInt(args, "--rounds", 1);
            int laps = ArgInt(args, "--laps", 20);
            bool verbose = args.Contains("--verbose");
            string driverName = ArgText(args, "--driver", null);
            // --meteo Rain@12 chiede alla direzione gara di portare pioggia al minuto 12.
            string weatherOrder = ArgText(args, "--meteo", null);
            int startSlot = ArgInt(args, "--griglia", 1);
            bool autopilot = args.Contains("--pilota-automatico");
            // Con questo il pilota simulato non si mette mai in piazzola: serve a provare che la
            // gara parta lo stesso a tempo scaduto, invece di restare ferma sulla griglia.
            bool neverPark = args.Contains("--non-parcheggiare");

            try
            {
                var championship = SeedData.DefaultChampionship("Simulazione");
                ChampionshipStore.Save(championship);

                var engine = new SessionEngine();
                engine.SetChampionship(championship);
                engine.Autopilot = autopilot;

                Console.WriteLine("Pitwall - simulazione");
                Console.WriteLine("Campionato : " + championship.Name);
                Console.WriteLine("Piloti     : " + championship.Drivers.Count);
                Console.WriteLine("Calendario : " + championship.Calendar.Count + " gare");
                Console.WriteLine();

                foreach (var round in championship.Calendar.OrderBy(r => r.Order).Take(rounds))
                    RunRace(engine, championship, round, laps, verbose, driverName, weatherOrder, startSlot, neverPark);

                PrintStandings(championship);
                return 0;
            }
            finally
            {
                // Con --keep-data la cartella resta: serve per ispezionare lo stato pubblicato,
                // che e esattamente quello che leggono le app in pista.
                if (args.Contains("--keep-data"))
                    Console.WriteLine("Dati lasciati in: " + sandbox);
                else
                    try { Directory.Delete(sandbox, true); } catch (IOException) { }
            }
        }

        private static void RunRace(SessionEngine engine, Championship championship, Round round, int laps, bool verbose, string driverName = null, string weatherOrder = null, int startSlot = 1, bool neverPark = false)
        {
            var circuit = championship.CircuitOf(round);

            // On a sprint weekend the engine correctly treats the first race-type session as the
            // sprint, so both have to be shortened or the simulation and the engine disagree
            // about how long the race is.
            foreach (var kind in new[] { SessionKind.Race, SessionKind.Sprint })
            {
                var s = round.Session(kind);
                if (s == null) continue;
                s.LengthMode = SessionLengthMode.Laps;
                s.Length = laps;
            }

            var session = round.Session(SessionKind.Race);

            Console.WriteLine("=== " + round.Name + " - " + circuit.Name + " - " + laps + " giri ===");
            Console.WriteLine("Meteo previsto: " + (session.Weather != null ? session.Weather.Summary : "-"));

            List<string> midRaceStrategy = null;
            var radio = new List<string>();
            var control = new List<string>();
            engine.EngineerSpoke += m => radio.Add("[giro " + m.Lap + "] " + m.Category + ": " + m.Text);
            engine.Log += line => { if (line.StartsWith("Meteo")) control.Add("   " + line); };

            double gridTimer = 0;
            bool gridReported = false;
            string weatherSky = "";
            double weatherMinute = 0;
            if (weatherOrder != null)
            {
                var parts = weatherOrder.Split('@');
                weatherSky = parts[0];
                if (parts.Length > 1) double.TryParse(parts[1], NumberStyles.Float,
                                                     CultureInfo.InvariantCulture, out weatherMinute);
            }

            var world = new World(championship, circuit, laps, driverName, startSlot);
            double step = 0.25;
            var lastControl = "";

            for (double t = 0; t < laps * circuit.ReferenceLapSeconds * 1.25; t += step)
            {
                // I comandi tornano nel mondo simulato, esattamente come l'app in pista li
                // applicherebbe alle vetture vere. E anche il modo per verificare che il formato
                // serializzato sia leggibile dall'altra parte.
                world.ApplyAiCommands(engine.AiCommands);

                var frame = world.Advance(step);

                // Il pilota simulato si sistema nella piazzola dopo qualche secondo, scrivendo
                // sullo stesso file di comandi che usa l'app in pista: cosi si prova anche il
                // formato, non solo la regola.
                if (engine.RaceControl != null &&
                    engine.RaceControl.FormationPhase == Pitwall.RaceControl.FormationPhase.Forming)
                {
                    gridTimer += step;
                    if (gridTimer >= 4 && !gridReported && !neverPark)
                    {
                        gridReported = true;
                        File.WriteAllText(Path.Combine(PitwallPaths.AcDocumentsFolder, "command.txt"),
                                          "report-grid 1 0.4\n");
                    }
                }

                // Il comando passa dallo stesso canale su file che usa il pulsante in SimHub,
                // cosi quello che si prova qui e esattamente quello che succede in gara.
                if (weatherOrder != null && frame.SessionElapsed.TotalMinutes >= weatherMinute)
                {
                    LiveBridge.SendCommand(CommandNames.SetWeather, weatherSky, 0);
                    weatherOrder = null;
                }

                engine.ProcessFrame(frame);

                // Gli scenari si guardano a meta gara: in fondo restano sempre pochi, perche
                // una sosta non si ripaga piu.
                if (midRaceStrategy == null && frame.CompletedLaps >= laps / 2)
                    midRaceStrategy = engine.Strategy.Options.Select(o => string.Format(
                        "{0}) {1,-24} {2,-36} +{3,5:0.0}s  P{4}  rischio {5}{6}",
                        o.Id, o.Name, o.Summary, o.DeltaSeconds, o.ProjectedPosition, o.Risk,
                        o.Recommended ? "  <- consigliato" : "")).ToList();

                if (engine.RaceControl.StatusText != lastControl)
                {
                    lastControl = engine.RaceControl.StatusText;
                    control.Add("[giro " + frame.CompletedLaps + "] " + lastControl +
                                "   (comandi IA: " + engine.AiCommands.Count + ")");
                }

                if (world.Finished) break;
            }

            var final = world.FinalFrame();
            final.FlagCheckered = true;
            engine.ProcessFrame(final);
            engine.FinishSession(final);

            var result = championship.ResultFor(round.Id, SessionKind.Race)
                         ?? championship.ResultFor(round.Id, SessionKind.Sprint);
            if (result == null)
            {
                Console.WriteLine("Nessun risultato registrato.");
                Console.WriteLine();
                return;
            }

            Console.WriteLine();
            if (midRaceStrategy != null)
            {
                Console.WriteLine("Scenari proposti a meta gara:");
                foreach (var line in midRaceStrategy) Console.WriteLine("  " + line);
                Console.WriteLine();
            }

            Console.WriteLine("Strategia decisa prima del via:");
            foreach (var line in engine.Ai.Summary().Take(verbose ? 20 : 6))
                Console.WriteLine("  " + line);

            Console.WriteLine();
            Console.WriteLine("Direzione gara:");
            foreach (var line in control) Console.WriteLine("  " + line);
            Console.WriteLine("  safety car: " + result.SafetyCarPeriods + ", VSC: " + result.VirtualSafetyCarPeriods);

            Console.WriteLine();
            Console.WriteLine("Radio dell'ingegnere (" + radio.Count + " messaggi" +
                              (verbose ? "" : ", primi 12") + "):");
            foreach (var line in verbose ? radio : radio.Take(12)) Console.WriteLine("  " + line);

            Console.WriteLine();
            Console.WriteLine("Ordine d'arrivo:");
            foreach (var entry in result.Entries.Take(10))
                Console.WriteLine(string.Format("  {0,2}. {1,-24} {2,2} giri  {3,-10} {4}",
                    entry.Position, entry.DriverName, entry.Laps,
                    entry.BestLap > TimeSpan.Zero ? entry.BestLap.ToString(@"m\:ss\.fff") : "-",
                    entry.PenaltySeconds > 0 ? "+" + entry.PenaltySeconds + "s" : ""));

            Console.WriteLine();
            Console.WriteLine("Punti assegnati:");
            foreach (var entry in result.Entries.Where(e => e.Points > 0))
                Console.WriteLine(string.Format("  {0,-24} {1,4} {2}", entry.DriverName, entry.Points, entry.PointsNote));

            Console.WriteLine();
        }

        /// <summary>
        /// Prova le qualifiche a eliminazione senza bisogno della pista.
        ///
        /// Il direttore delle manche non dipende dalla telemetria: gli si passa il tempo
        /// trascorso e la classifica per tempo, e lui decide chi esce. Qui gli si da una
        /// classifica finta e si guarda se Q1, Q2 e Q3 si chiudono quando devono, se escono i
        /// piloti giusti e se la griglia finale si compone dal fondo verso la pole.
        /// </summary>
        /// <summary>
        /// Controlla il programma delle uscite in prove e qualifica.
        ///
        /// La cosa da verificare non e che il codice giri, ma che la pista non sia ne deserta
        /// ne intasata: se escono tutti al minuto zero non abbiamo risolto niente, e se non
        /// esce nessuno negli ultimi minuti manca la corsa al tempo che e il bello della
        /// qualifica. Qui si stampa quante vetture il programma vuole in pista minuto per
        /// minuto, e si guarda la forma della curva.
        /// </summary>
        private static int RunRunPlans(string[] args)
        {
            bool qualifying = args.Contains("--qualifica-programmi") || args.Contains("--q");
            double minutes = ArgInt(args, "--minuti", qualifying ? 18 : 60);

            var championship = SeedData.DefaultChampionship("Prova programmi");
            var names = championship.Drivers.Select(d => d.FullName).ToList();

            var director = new Pitwall.Ai.OutLapDirector();
            director.Begin(minutes, names, qualifying, ArgInt(args, "--seme", 1234));

            Console.WriteLine("Programmi delle uscite - " + (qualifying ? "qualifica" : "prove libere") +
                              ", " + (int)minutes + " minuti, " + names.Count + " piloti");
            Console.WriteLine();

            foreach (var name in names.Take(8))
                Console.WriteLine(string.Format("  {0,-24} {1}", name, director.DescribePlan(name)));
            if (names.Count > 8) Console.WriteLine("  ... e altri " + (names.Count - 8));
            Console.WriteLine();

            Console.WriteLine("Vetture che il programma vuole in pista:");
            int peak = 0, empty = 0, lateOut = 0, total = 0, samples = 0;
            int lateTotal = 0, lateSamples = 0;
            for (int m = 0; m <= (int)minutes; m++)
            {
                int outNow = names.Count(n => director.ScheduledOut(n, m));
                if (outNow > peak) peak = outNow;
                if (outNow == 0) empty++;
                if (m >= minutes - 3) lateOut = Math.Max(lateOut, outNow);
                if (m >= minutes * 2.0 / 3.0) { lateTotal += outNow; lateSamples++; }
                total += outNow;
                samples++;

                if (m % 2 == 0)
                    Console.WriteLine(string.Format("  {0,3}'  {1,2}  {2}", m, outNow,
                                      new string('#', outNow)));
            }

            double average = samples > 0 ? (double)total / samples : 0;
            double lateAverage = lateSamples > 0 ? (double)lateTotal / lateSamples : 0;
            Console.WriteLine();
            Console.WriteLine("Massimo in pista insieme : " + peak + " su " + names.Count);
            Console.WriteLine("Media in pista           : " + average.ToString("0.0"));
            Console.WriteLine("Minuti con pista vuota   : " + empty);
            Console.WriteLine("Punta negli ultimi 3'    : " + lateOut);
            Console.WriteLine("Media nell'ultimo terzo  : " + lateAverage.ToString("0.0"));

            // Tre condizioni. In media la pista non deve essere piena - se lo e, il programma
            // non sta dividendo niente. Non deve restare deserta per mezza sessione. E
            // nell'ultimo terzo ci deve essere lavoro: e la parte della sessione che conta,
            // la pista gommata e i tempi veri. Il massimo invece puo toccare tutto il gruppo,
            // perche in qualifica negli ultimi minuti escono davvero tutti insieme.
            bool ok = average <= names.Count * 0.70
                   && empty <= 2 + minutes * 0.20
                   && lateAverage >= names.Count * 0.20;
            Console.WriteLine();
            Console.WriteLine(ok ? "Programma plausibile." : "ANOMALIA: il programma non sta in piedi.");
            return ok ? 0 : 1;
        }

        /// <summary>
        /// Controlla quando la direzione gara concede il DRS.
        ///
        /// Due cose devono risultare: che fino al terzo giro sia negato a tutti, e che una
        /// safety car lo richiuda e la bandiera verde lo riapra. Sono le due meta della
        /// regola che si decidono guardando la gara nel suo insieme; il secondo di distacco
        /// si misura in pista e si controlla altrove.
        /// </summary>
        private static int RunDrs()
        {
            var rules = new Pitwall.Rules.DrsRules();
            var drs = new Pitwall.RaceControl.DrsController();
            drs.Begin(rules);

            Console.WriteLine("DRS - regola: dal giro " + rules.EnabledFromLap +
                              ", entro " + rules.DetectionGapSeconds.ToString("0.0") + " s al rilevamento");
            Console.WriteLine();

            int fails = 0;
            Action<string, bool, bool> check = (what, got, want) =>
            {
                if (got != want) fails++;
                Console.WriteLine(string.Format("  {0}  {1,-46} atteso {2}, ottenuto {3}",
                    got == want ? "ok  " : "NO  ", what, want, got));
            };

            var green = Pitwall.RaceControl.RaceControlStatus.Green;

            drs.Update(true, 0, green);
            check("primo giro, verde", drs.EnabledNow, false);
            Console.WriteLine("        avviso: " + drs.Reason);

            drs.Update(true, 1, green);
            check("secondo giro, verde", drs.EnabledNow, false);

            drs.Update(true, 2, green);
            check("terzo giro, verde", drs.EnabledNow, true);

            drs.Update(true, 5, Pitwall.RaceControl.RaceControlStatus.SafetyCar);
            check("safety car in pista", drs.EnabledNow, false);

            drs.Update(true, 5, Pitwall.RaceControl.RaceControlStatus.VirtualSafetyCar);
            check("safety car virtuale", drs.EnabledNow, false);

            drs.Update(true, 5, Pitwall.RaceControl.RaceControlStatus.SafetyCarEnding);
            check("safety car che rientra", drs.EnabledNow, false);

            drs.Update(true, 5, green);
            check("ripartenza in verde", drs.EnabledNow, true);

            drs.Update(true, 5, Pitwall.RaceControl.RaceControlStatus.RedFlag);
            check("bandiera rossa", drs.EnabledNow, false);

            drs.Update(true, 5, Pitwall.RaceControl.RaceControlStatus.LocalYellow);
            check("giallo locale: resta concesso", drs.EnabledNow, true);

            drs.Update(false, 0, Pitwall.RaceControl.RaceControlStatus.Inactive);
            check("prove e qualifica: libero", drs.EnabledNow, true);

            var off = new Pitwall.Rules.DrsRules { Enabled = false };
            drs.Begin(off);
            drs.Update(true, 0, green);
            check("regola spenta: come Assetto Corsa", drs.EnabledNow, true);

            // La riga che arriva all'app in pista. Il Lua legge i campi per posizione, quindi
            // aggiungerne uno in mezzo rompe tutto in silenzio: qui si controlla il numero.
            var sample = new Pitwall.Ai.AiCommand
            {
                Name = "Prova", AiLevel = 97, Aggression = 40, TopSpeedKmh = 170,
                Pit = true, Compound = "S", SplineOffset = 2.6, StopSeconds = 0,
                Phase = "", Warmup = true, LapKind = "OutLap", HoldInPit = false,
                DrsAllowed = false, FuelLaps = 3.5
            };
            string line = sample.Serialise();
            int fieldCount = line.Split('|').Length;
            Console.WriteLine();
            Console.WriteLine("  riga comando: " + line);
            check("la riga ha quindici campi", fieldCount == 15, true);
            Console.WriteLine();
            Console.WriteLine(fails == 0 ? "Regola del DRS coerente." : "ANOMALIA: " + fails + " controlli falliti.");
            return fails == 0 ? 0 : 1;
        }

        private static int RunQualifying(string[] args)
        {
            var championship = SeedData.DefaultChampionship("Prova qualifiche");
            var rules = championship.Rules.Qualifying;

            // Quanto dura la sessione, che e il punto: le tre manche devono starci dentro.
            // Senza questo si provava solo il caso in cui ci stanno da sole.
            int minuti = ArgInt(args, "--minuti", 30);

            var session = new SessionConfig
            {
                Kind = SessionKind.Qualifying,
                QualifyingFormat = QualifyingFormat.Knockout,
                LengthMode = SessionLengthMode.Minutes,
                Length = minuti
            };

            // Classifica per tempo: il piu veloce davanti. Qui e semplicemente l'ordine dei
            // piloti del campionato, che basta per verificare chi viene tagliato.
            var order = championship.Drivers.Select(d => d.FullName).ToList();

            var director = new Pitwall.RaceControl.QualifyingDirector();
            director.SegmentClosed += (name, dropped) =>
                Console.WriteLine("   " + name + " chiusa -> fuori: " + string.Join(", ", dropped));

            director.Begin(session, rules, order.Count);
            var piano = director.PlannedMinutes;
            double totale = piano[0] + piano[1] + piano[2];

            Console.WriteLine("Qualifiche a eliminazione - " + order.Count + " piloti");
            Console.WriteLine("Sessione: " + minuti + " minuti");
            Console.WriteLine("Chieste dal regolamento: Q1 " + rules.Q1Minutes + "', Q2 " +
                              rules.Q2Minutes + "', Q3 " + rules.Q3Minutes + "'   (totale " +
                              (rules.Q1Minutes + rules.Q2Minutes + rules.Q3Minutes) + "')");
            Console.WriteLine(string.Format("In uso: Q1 {0:0.0}', Q2 {1:0.0}', Q3 {2:0.0}'   (totale {3:0.0}')",
                              piano[0], piano[1], piano[2], totale));
            Console.WriteLine("Eliminati: " + director.PlannedEliminations[0] + " dopo Q1, " +
                              director.PlannedEliminations[1] + " dopo Q2");

            if (totale > minuti)
            {
                Console.WriteLine();
                Console.WriteLine("NO: le tre manche non stanno nella sessione. La Q1 non scadrebbe");
                Console.WriteLine("    mai, nessuno verrebbe eliminato, e dopo la Q1 partirebbe la gara.");
                return 1;
            }

            Console.WriteLine();

            string lastSegment = "";
            for (double t = 0; t <= totale * 60 + 5; t += 1)
            {
                var running = order.Where(n => !director.IsEliminated(n)).ToList();
                director.Update(t, running);

                if (director.SegmentName != lastSegment && director.SegmentName != "")
                {
                    lastSegment = director.SegmentName;
                    Console.WriteLine(lastSegment + " al via: " + director.Running() + " piloti in pista, " +
                                      "ne passano " + director.Surviving);
                    var zone = director.DropZone(running);
                    if (zone.Count > 0)
                        Console.WriteLine("   zona esclusione: " + string.Join(", ", zone.Take(3)) + " ...");
                }
            }

            var finalRunning = order.Where(n => !director.IsEliminated(n)).ToList();
            var grid = director.FinalOrder(finalRunning);

            Console.WriteLine();
            Console.WriteLine("Griglia finale:");
            for (int i = 0; i < grid.Count; i++)
                Console.WriteLine(string.Format("  {0,2}. {1}", i + 1, grid[i]));

            bool ok = grid.Count == order.Count && grid.Distinct().Count() == order.Count;
            Console.WriteLine();
            Console.WriteLine(ok
                ? "Griglia completa e senza doppioni: " + grid.Count + " piloti."
                : "ANOMALIA: la griglia ha " + grid.Count + " voci su " + order.Count);
            return ok ? 0 : 1;
        }

        private static void PrintStandings(Championship championship)
        {
            Console.WriteLine("=== Classifica piloti ===");
            foreach (var row in StandingsCalculator.Drivers(championship).Take(12))
                Console.WriteLine(string.Format("  {0,2}. {1,-24} {2,-18} {3,5}  V{4} P{5}",
                    row.Position, row.Name, row.TeamName, row.Points, row.Wins, row.Podiums));

            Console.WriteLine();
            Console.WriteLine("=== Classifica costruttori ===");
            foreach (var row in StandingsCalculator.Constructors(championship))
                Console.WriteLine(string.Format("  {0,2}. {1,-22} {2,5}", row.Position, row.Name, row.Points));
        }

        private static string ArgText(string[] args, string name, string fallback)
        {
            int index = Array.IndexOf(args, name);
            if (index >= 0 && index + 1 < args.Length) return args[index + 1];
            return fallback;
        }

        private static int ArgInt(string[] args, string name, int fallback)
        {
            int index = Array.IndexOf(args, name);
            if (index >= 0 && index + 1 < args.Length && int.TryParse(args[index + 1], out int value)) return value;
            return fallback;
        }
    }

    /// <summary>
    /// A very small race simulation: twenty cars going round at slightly different paces, tyres
    /// wearing out, a couple of pit stops, and one car parking itself on track halfway through
    /// so that race control has something to react to.
    /// </summary>
    internal sealed class World
    {
        private readonly Circuit _circuit;
        private readonly int _totalLaps;
        private readonly List<Car> _cars = new List<Car>();
        private readonly Random _random = new Random(1234);
        private double _time;

        public bool Finished { get; private set; }

        private readonly string _playerName;

        public World(Championship championship, Circuit circuit, int totalLaps, string playerName = null, int startSlot = 1)
        {
            _circuit = circuit;
            _totalLaps = totalLaps;
            _playerName = playerName;

            int grid = 1;
            foreach (var driver in championship.Drivers)
            {
                _cars.Add(new Car
                {
                    Driver = driver,
                    Team = championship.TeamById(driver.TeamId),
                    Position = grid,
                    // Faster drivers start further up and lap a touch quicker.
                    BaseLapSeconds = circuit.ReferenceLapSeconds + grid * 0.18 + (100 - driver.AiSkill) * 0.05,
                    IsPlayer = driver.IsPlayer
                });
                grid++;
            }

            // Partendo davanti a tutti non si puo guadagnare una posizione, quindi il divieto di
            // sorpasso in ricognizione non si riuscirebbe mai a provare. Con --griglia si mette
            // il giocatore piu indietro e la regola ha modo di scattare.
            if (startSlot > 1)
            {
                var mine = _cars.FirstOrDefault(c => c.IsPlayer);
                if (mine != null)
                {
                    _cars.Remove(mine);
                    _cars.Insert(Math.Min(startSlot - 1, _cars.Count), mine);
                    for (int i = 0; i < _cars.Count; i++) _cars[i].Position = i + 1;
                }
            }
        }

        /// <summary>
        /// Applica i comandi del direttore dell'IA al mondo simulato. Qui interessa soprattutto
        /// l'arresto forzato: e cosi che un ritiro pianificato diventa una vettura ferma in
        /// pista, e da li un incidente che la direzione gara deve gestire.
        /// </summary>
        public void ApplyAiCommands(IEnumerable<string> serialised)
        {
            if (serialised == null) return;

            foreach (var line in serialised)
            {
                var f = line.Split('|');
                if (f.Length < 8) continue;

                var car = _cars.FirstOrDefault(c => c.Driver.FullName == f[0]);
                if (car == null) continue;

                if (double.TryParse(f[7], System.Globalization.NumberStyles.Any,
                                    System.Globalization.CultureInfo.InvariantCulture, out double stop) && stop > 0)
                {
                    car.StopSeconds = Math.Max(car.StopSeconds, stop);
                }
            }
        }

        public TelemetryFrame Advance(double step)
        {
            _time += step;

            foreach (var car in _cars)
            {
                // Una vettura ferma resta dov e: ci pensa il blocco StopSeconds qui sotto.

                if (car.StopSeconds > 0)
                {
                    car.StopSeconds -= step;
                    car.Speed = 0;
                    continue;
                }

                double lapTime = car.BaseLapSeconds + car.TyreWear * 0.02;
                car.Progress += step / lapTime;
                car.TyreWear = Math.Min(100, car.TyreWear + step / lapTime * 2.4);
                car.Speed = _circuit.LengthMeters / lapTime * 3.6;

                if (car.Progress >= 1)
                {
                    car.Progress -= 1;
                    car.Laps++;
                    car.LastLap = TimeSpan.FromSeconds(lapTime);
                    if (car.BestLap == TimeSpan.Zero || car.LastLap < car.BestLap) car.BestLap = car.LastLap;

                    // A single stop around a third distance, staggered so the order shuffles.
                    if (car.PitStops == 0 && car.Laps >= _totalLaps / 3 + car.Position % 4)
                    {
                        car.PitStops = 1;
                        car.TyreWear = 0;
                        car.InPitLane = true;
                        car.PitTimer = _circuit.PitLaneLossSeconds + _circuit.PitStopStationarySeconds;
                    }
                }

                if (car.InPitLane)
                {
                    car.PitTimer -= step;
                    car.Speed = 70;
                    if (car.PitTimer <= 0) car.InPitLane = false;
                }
            }

            Rank();
            if (_cars.Any(c => c.Laps >= _totalLaps)) Finished = true;
            return BuildFrame();
        }

        public TelemetryFrame FinalFrame()
        {
            var frame = BuildFrame();
            frame.FlagCheckered = true;
            return frame;
        }

        private void Rank()
        {
            var ordered = _cars.OrderByDescending(c => c.Laps + c.Progress).ToList();
            for (int i = 0; i < ordered.Count; i++) ordered[i].Position = i + 1;
        }

        private TelemetryFrame BuildFrame()
        {
            var player = _cars.First(c => c.IsPlayer);
            var leader = _cars.OrderBy(c => c.Position).First();

            var frame = new TelemetryFrame
            {
                GameRunning = true,
                DeltaSeconds = 0.25,
                SessionTypeName = "Race",
                TrackId = _circuit.AcTrackId,
                TrackConfig = _circuit.AcTrackConfig,
                TrackName = _circuit.Name,
                TrackLengthMeters = _circuit.LengthMeters,
                TotalLaps = _totalLaps,
                RemainingLaps = Math.Max(0, _totalLaps - player.Laps),
                SessionElapsed = TimeSpan.FromSeconds(_time),
                PlayerName = string.IsNullOrEmpty(_playerName) ? player.Driver.FullName : _playerName,
                Position = player.Position,
                CompletedLaps = player.Laps,
                CurrentLap = player.Laps + 1,
                LastLapTime = player.LastLap,
                BestLapTime = player.BestLap,
                LapValid = true,
                TrackPositionPercent = player.Progress,
                TrackPositionMeters = player.Progress * _circuit.LengthMeters,
                SpeedKmh = player.Speed,
                Fuel = Math.Max(0, 100 - player.Laps * 2.0),
                MaxFuel = 110,
                EstimatedFuelLaps = Math.Max(0, _totalLaps - player.Laps + 2),
                TyreCompound = player.PitStops == 0 ? "Soft" : "Medium",
                TyreWearFrontLeft = player.TyreWear,
                TyreWearFrontRight = player.TyreWear,
                TyreWearRearLeft = player.TyreWear * 1.05,
                TyreWearRearRight = player.TyreWear * 1.05,
                TyreTemperatureAvg = 95,
                InPitLane = player.InPitLane,
                InPit = false,
                AirTemperature = _circuit.TypicalAirTemperature,
                RoadTemperature = _circuit.TypicalAirTemperature + 12
            };

            // La vettura di sicurezza e un'iscritta come le altre, ferma ai box.
            frame.Opponents.Add(new OpponentFrame
            {
                Name = "Safety Car",
                IsConnected = true,
                Position = 99,
                CompletedLaps = 0,
                SpeedKmh = 0,
                InPit = true,
                InPitLane = true
            });

            foreach (var car in _cars.OrderBy(c => c.Position))
            {
                double gap = ((car.Laps + car.Progress) - (player.Laps + player.Progress)) * -car.BaseLapSeconds;
                frame.Opponents.Add(new OpponentFrame
                {
                    Name = car.IsPlayer && !string.IsNullOrEmpty(_playerName) ? _playerName : car.Driver.FullName,
                    CarNumber = car.Driver.Number.ToString(),
                    TeamName = car.Team != null ? car.Team.Name : "",
                    IsPlayer = car.IsPlayer,
                    // A crashed AI car stays in the session in Assetto Corsa, it just sits there.
                    // Reporting it as still connected is what the game really does, and it is
                    // what lets race control notice a car stopped on track.
                    IsConnected = true,
                    Position = car.Position,
                    CompletedLaps = car.Laps,
                    TrackPositionPercent = car.Progress,
                    SpeedKmh = car.Speed,
                    BestLapTime = car.BestLap,
                    LastLapTime = car.LastLap,
                    GapToPlayer = gap,
                    GapToLeader = ((leader.Laps + leader.Progress) - (car.Laps + car.Progress)) * car.BaseLapSeconds,
                    InPitLane = car.InPitLane,
                    PitCount = car.PitStops,
                    TyreCompound = car.PitStops == 0 ? "Soft" : "Medium"
                });
            }

            return frame;
        }

        private sealed class Car
        {
            public Driver Driver;
            public Team Team;
            public bool IsPlayer;
            public int Position;
            public int Laps;
            public double Progress;
            public double BaseLapSeconds;
            public double TyreWear;
            public double Speed;
            public bool InPitLane;
            public double PitTimer;
            public int PitStops;
            public double StopSeconds;
            public TimeSpan LastLap;
            public TimeSpan BestLap;
        }
    }
}
