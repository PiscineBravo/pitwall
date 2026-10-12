using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Pitwall.Ai;
using Pitwall.Live;
using Pitwall.Model;
using Pitwall.RaceControl;
using Pitwall.Rules;
using Pitwall.Storage;
using Pitwall.Tyres;
using Pitwall.Weather;

namespace Pitwall.Simulator
{
    // Controlli ripetibili senza avviare il gioco: dotnet run -- --regressioni.
    internal static class RegressionChecks
    {
        public static int Run()
        {
            string previousRoot = PitwallPaths.Root;
            string sandbox = Path.Combine(Path.GetTempPath(), "PitwallChecks-" + Guid.NewGuid().ToString("N"));
            PitwallPaths.UseRoot(sandbox);
            int failed = 0;
            try
            {
                Action<string, Action> check = (name, test) =>
                {
                    try { test(); Console.WriteLine("OK   " + name); }
                    catch (Exception ex) { failed++; Console.WriteLine("FAIL " + name + ": " + ex.Message); }
                };
                check("Isolamento dei file di Assetto Corsa", () => RequireSandbox(sandbox));
                check("Stato e comandi del bridge nella sandbox", () => CheckBridge(sandbox));
                check("Salvataggio e caricamento del campionato", CheckChampionship);
                check("Liste cresciute da sole riportate alla misura", CheckListePotate);
                check("Rientro automatico SC prima del verde", CheckAutomaticSafetyCar);
                check("Rientro manuale SC persistente e idempotente", CheckManualSafetyCar);
                check("Safety car senza vettura reale", CheckVirtualSafetyCar);
                check("Richiesta di sosta IA fino alla conferma", () => CheckPitRequest(false, false));
                check("Richiesta gomme da pioggia persistente", () => CheckPitRequest(true, false));
                check("Sosta IA sotto VSC fino alla conferma", () => CheckPitRequest(false, true));
                FormationChecks.Run(check);
                SafetyChecks.Run(check);
                QualifyingChecks.Run(check);
                StartChecks.Run(check);
                Console.WriteLine(failed == 0 ? "Tutte le regressioni superate." : "Controlli falliti: " + failed);
                return failed == 0 ? 0 : 1;
            }
            finally
            {
                PitwallPaths.UseRoot(null);
                if (!string.Equals(previousRoot, PitwallPaths.Root, StringComparison.OrdinalIgnoreCase))
                    PitwallPaths.UseRoot(previousRoot);
                // La cartella e generata qui, sempre sotto Temp, e non proviene dai dati utente.
                if (Directory.Exists(sandbox)) Directory.Delete(sandbox, true);
            }
        }

        /// <summary>
        /// Le liste che si erano allungate da sole tornano alla misura giusta.
        ///
        /// Newtonsoft riempiva le liste invece di sostituirle, quindi ogni lista con un valore di
        /// partenza non vuoto cresceva a ogni lettura e riscrittura. La causa e corretta, ma i file
        /// gia scritti restano lunghi: in un campionato vero i punteggi 25-18-15... erano ripetuti
        /// circa centocinquanta volte, e le durate delle manche trecento. La potatura cerca il
        /// periodo invece di troncare a una lunghezza fissa, perche un punteggio puo legittimamente
        /// avere piu di dieci posizioni.
        /// </summary>
        private static void CheckListePotate()
        {
            var c = SeedData.DefaultChampionship("Prova potatura");

            var puntiVeri = new List<int>(c.Rules.Points.Race);
            for (int i = 0; i < 40; i++) c.Rules.Points.Race.AddRange(puntiVeri);

            var sessione = c.Calendar[0].Session(SessionKind.Qualifying);
            var mancheVere = new List<int>(sessione.KnockoutSegmentMinutes);
            for (int i = 0; i < 40; i++) sessione.KnockoutSegmentMinutes.AddRange(mancheVere);

            // Una lista scritta a mano, che NON e una ripetizione: non si deve toccare.
            var sprintSuo = new List<int> { 9, 7, 5, 4, 3, 2, 1 };
            c.Rules.Points.Sprint = new List<int>(sprintSuo);

            Require(Migrazioni.Applica(c), "La migrazione non ha trovato niente da potare.");

            Require(c.Rules.Points.Race.SequenceEqual(puntiVeri),
                    "Punteggi gara non potati: " + c.Rules.Points.Race.Count + " voci.");
            Require(sessione.KnockoutSegmentMinutes.SequenceEqual(mancheVere),
                    "Durate delle manche non potate: " + sessione.KnockoutSegmentMinutes.Count + " voci.");
            Require(c.Rules.Points.Sprint.SequenceEqual(sprintSuo),
                    "Una lista non ripetuta e stata accorciata: non si deve toccare.");
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        private static void RequireSandbox(string root)
        {
            string prefix = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            foreach (string path in new[] { PitwallPaths.LiveStateFile, PitwallPaths.CommandFile,
                                           PitwallPaths.AcDocumentsFolder, PitwallPaths.AcLiveStateFile,
                                           PitwallPaths.AcRaceControlFile })
                Require(Path.GetFullPath(path).StartsWith(prefix, StringComparison.OrdinalIgnoreCase),
                        "Percorso esterno alla sandbox: " + path);
        }

        private static void CheckBridge(string sandbox)
        {
            // Fallire PRIMA di qualsiasi I/O se un percorso punta all'installazione reale.
            RequireSandbox(sandbox);
            LiveBridge.PublishState(new LiveState { ChampionshipName = "Prova isolata", GameRunning = true }, true);
            Require(LiveBridge.ReadState()?.ChampionshipName == "Prova isolata", "Stato del manager mancante.");
            Require(File.Exists(PitwallPaths.AcLiveStateFile) && File.Exists(PitwallPaths.AcRaceControlFile),
                    "Stato per le app in pista mancante.");
            LiveBridge.SendCommand("prova-manager");
            string commandPath = Path.Combine(PitwallPaths.AcDocumentsFolder, "command.txt");
            File.WriteAllText(commandPath, "report-grid 1 0.4\n", PitwallPaths.Utf8NoBom);
            var commands = LiveBridge.TakeCommands();
            Require(commands.Any(c => c.Name == "prova-manager"), "Comando manager perso.");
            Require(commands.Any(c => c.Name == "report-grid" && c.Argument == "1" && c.Value == 0.4),
                    "Comando Lua perso o alterato.");
            Require(!File.Exists(commandPath) && LiveBridge.TakeCommands().Count == 0,
                    "Comandi gia eseguiti riproposti.");
        }

        private static void CheckChampionship()
        {
            var original = SeedData.DefaultChampionship("Prova salvataggio");
            ChampionshipStore.Save(original);
            var loaded = ChampionshipStore.Load(original.Id);
            Require(loaded != null && loaded.Name == original.Name, "Campionato non ricaricato.");
            Require(loaded.Drivers.Count == original.Drivers.Count && loaded.Calendar.Count == original.Calendar.Count,
                    "Piloti o calendario persi.");
            Require(loaded.Drivers.All(d => loaded.TeamById(d.TeamId) != null), "Scuderia di un pilota persa.");
            Require(loaded.Calendar.All(r => loaded.CircuitOf(r) != null), "Circuito di una gara perso.");
        }

        private static RaceDirector Director(bool realCar = true)
        {
            var director = new RaceDirector(123);
            director.BeginSession(new Circuit(), new SessionConfig { Kind = SessionKind.Race, FormationLap = false },
                new RaceControlRules
                {
                    SafetyCarMinimumLaps = 2,
                    SafetyCar = new SafetyCarSettings { UseRealCar = realCar, BunchingLaps = 1, AllowUnlapping = false }
                });
            return director;
        }

        private static TelemetryFrame Frame() => new TelemetryFrame
        {
            GameRunning = true, DeltaSeconds = 0.25, CompletedLaps = 10, TotalLaps = 40,
            Position = 2, PlayerName = "Giocatore", SpeedKmh = 100,
            Opponents = { new OpponentFrame { Name = SafetyCar.DriverName, SpeedKmh = 120 } }
        };

        private static void Tick(RaceDirector director, TelemetryFrame frame, int lap)
        {
            frame.CompletedLaps = lap;
            director.Update(frame, "player", frame.PlayerName);
        }

        private static void CheckAutomaticSafetyCar()
        {
            var director = Director();
            var frame = Frame();
            director.DeploySafetyCar("prova", frame);
            Tick(director, frame, 11);
            Require(director.SafetyCarPhase == SafetyCarPhase.Bunching, "Ricompattamento mancante.");
            Tick(director, frame, 12);
            Require(director.SafetyCarPhase == SafetyCarPhase.InThisLap, "Avviso ultimo giro mancante.");
            Tick(director, frame, 13);
            Require(director.SafetyCarPhase == SafetyCarPhase.Restarting && director.Status == RaceControlStatus.SafetyCarEnding,
                    "Il comando di rientro scompare prima che la SC sia ai box.");
            for (int i = 0; i < 4; i++) Tick(director, frame, 13);
            Require(director.SafetyCarPhase == SafetyCarPhase.Restarting, "Rientro non mantenuto fra i frame.");
            var safetyCar = frame.Opponents[0];
            frame.Opponents.Clear();
            Tick(director, frame, 13);
            Require(director.Status == RaceControlStatus.SafetyCarEnding, "Telemetria assente scambiata per SC ai box.");
            frame.Opponents.Add(safetyCar);
            frame.Opponents[0].InPitLane = true;
            Tick(director, frame, 13);
            Require(director.Status == RaceControlStatus.Green && director.SafetyCarPhase == SafetyCarPhase.Idle,
                    "Manca il verde dopo il rientro.");
            safetyCar.InPitLane = false;
            director.DeploySafetyCar("secondo intervento", frame);
            Tick(director, frame, 13);
            Require(director.SafetyCarPhase == SafetyCarPhase.Deploying && director.SafetyCarPeriods == 2,
                    "Il rientro precedente contamina l'intervento successivo.");
        }

        private static void CheckManualSafetyCar()
        {
            var director = Director();
            var frame = Frame();
            director.DeploySafetyCar("prova", frame);
            director.EndNeutralisation(frame);
            Require(director.SafetyCarPhase == SafetyCarPhase.InThisLap, "Il richiamo non cambia la fase inviata al Lua.");
            director.EndNeutralisation(frame);
            Tick(director, frame, 10);
            Require(director.Status == RaceControlStatus.SafetyCarEnding && director.SafetyCarPhase == SafetyCarPhase.InThisLap,
                    "Il richiamo ripetuto salta o annulla il rientro.");
            Tick(director, frame, 11);
            Require(director.SafetyCarPhase == SafetyCarPhase.Restarting, "Il richiamo aspetta ancora il piano automatico.");
            frame.Opponents[0].InPit = true;
            Tick(director, frame, 11);
            Require(director.Status == RaceControlStatus.Green, "SC ai box ma gara ancora neutralizzata.");
        }

        private static void CheckVirtualSafetyCar()
        {
            foreach (bool realCar in new[] { false, true })
            {
                var director = Director(realCar);
                var frame = Frame();
                frame.Opponents.Clear();
                director.DeploySafetyCar("prova", frame);
                Tick(director, frame, 11);
                Tick(director, frame, 12);
                Tick(director, frame, 13);
                Require(director.Status == RaceControlStatus.Green, "Attesa di una vettura SC mai presente nella sessione.");
            }
        }

        private static void CheckPitRequest(bool rain, bool neutralised)
        {
            var championship = SeedData.DefaultChampionship("Prova IA");
            var round = championship.Calendar[0];
            var session = new SessionConfig { Kind = SessionKind.Race };
            var opponent = new OpponentFrame { Name = championship.Drivers[1].FullName, CompletedLaps = 10, SpeedKmh = 150 };
            var ai = new AiDirector { RetirementProbability = 0, MistakeProbability = 0 };
            ai.BeginRace(championship, round, session, championship.CircuitOf(round), new[] { opponent }, 40);
            var plan = ai.Plans[opponent.Name];
            plan.Stints.Clear();
            plan.Stints.Add(new PlannedStint { Compound = CompoundKind.Medium, StartLap = 0, EndLap = rain || neutralised ? 20 : 10 });
            plan.Stints.Add(new PlannedStint { Compound = CompoundKind.Hard, StartLap = plan.Stints[0].EndLap, EndLap = 40 });
            var frame = Frame();
            frame.Opponents.Clear();
            frame.Opponents.Add(opponent);
            var director = Director();
            if (neutralised) director.DeployVirtualSafetyCar("prova", frame);
            var weather = new WeatherSnapshot { Wetness = rain ? 0.8 : 0 };
            string expectedCompound = rain ? Compounds.ForConditions(weather.Wetness).Code : "H";
            for (int i = 0; i < 5; i++)
            {
                var command = ai.Update(frame, director, weather).Single();
                Require(command.Pit && command.Compound == expectedCompound,
                        "Richiesta persa prima della sosta, frame " + i + ".");
                if (neutralised) Require(command.TopSpeedKmh > 0, "Sosta cancella il limite VSC.");
            }
            if (!rain)
            {
                opponent.PitCount = 1;
                var command = ai.Update(frame, director, weather).Single();
                Require(!command.Pit && !plan.PitRequested && plan.StopsDone == 1,
                        "Richiesta non revocata dopo la sosta.");
            }
        }
    }
}
