using System;
using System.IO;
using Pitwall.Live;
using Pitwall.Model;
using Pitwall.RaceControl;
using Pitwall.Rules;
using Pitwall.Storage;

namespace Pitwall.Simulator
{
    internal static class FormationChecks
    {
        public static void Run(Action<string, Action> check)
        {
            check("Griglia automatica senza falsa partenza dai box", AutomaticGrid);
            check("Griglia manuale: attesa, timeout e via", ManualGridTimeout);
            check("Cinque luci e ritardo misurato dall'ultima luce", LightsTiming);
            check("Semaforo fermo nei menu e nei replay", InactiveFrames);
            check("Nuova sessione senza limite di formazione residuo", ResetPace);
            check("Riapertura della stessa gara dopo la chiusura", ReopenSameRace);
            check("Bandiera a scacchi senza riavvio della stessa sessione", FinishedSession);
            check("Attesa corsia box trasmessa al companion", PitDelayBridge);
            check("Intervallo semaforo non valido senza bloccare il via", InvalidLightsInterval);
            check("Ricognizione chiusa dal giro del gruppo, non solo da quello del giocatore", FieldLapEndsRolling);
            check("Ricognizione con un tetto di tempo: non gira all'infinito", RollingNeverHangs);
            check("Ricognizione non chiusa da conteggi giri ereditati", StaleLapsDoNotEndRolling);
        }

        /// <summary>
        /// Il giro del gruppo basta a chiudere la ricognizione.
        ///
        /// Qui si guardava solo il giro del giocatore, e bastava che il giocatore fosse lento,
        /// girato o ancora in corsia box perche la fase restasse Rolling per sempre: le vetture
        /// continuavano a girare, il semaforo non si accendeva e la gara non cominciava mai.
        /// </summary>
        private static void FieldLapEndsRolling()
        {
            var settings = Settings();
            var director = Director(settings);
            var frame = Frame();

            Tick(director, frame);
            Require(director.FormationPhase == FormationPhase.Rolling, "Ricognizione non avviata.");

            // Mezzo minuto di ricognizione, con il giocatore che non chiude il suo giro.
            Tick(director, frame, 140);
            Require(director.FormationPhase == FormationPhase.Rolling,
                    "Ricognizione chiusa senza che nessuno avesse finito il giro.");

            // Il gruppo passa sul traguardo. Il giocatore no.
            frame.Opponents[0].CompletedLaps = 1;
            Tick(director, frame);

            Require(frame.CompletedLaps == 0, "Il controllo deve valere con il giocatore a zero giri.");
            Require(director.FormationPhase != FormationPhase.Rolling,
                    "Il giro del gruppo non chiude la ricognizione: la gara non partirebbe mai.");
        }

        /// <summary>
        /// Anche senza nessun giro completato da nessuno, la ricognizione finisce.
        ///
        /// E una rete di sicurezza che non deve servire mai. Esiste perche una gara che non parte
        /// non e un difetto da correggere alla prossima versione: e un prodotto rotto in mano a chi
        /// lo ha comprato.
        /// </summary>
        private static void RollingNeverHangs()
        {
            var director = Director(Settings());
            var frame = Frame();

            // Un circuito corto e un ritmo alto tengono breve il tetto, che si misura su quelli.
            frame.TrackLengthMeters = 3000;

            Tick(director, frame);
            Require(director.FormationPhase == FormationPhase.Rolling, "Ricognizione non avviata.");

            // Nessuno completa un giro: ne il giocatore ne il gruppo.
            for (int i = 0; i < 4000 && director.FormationPhase == FormationPhase.Rolling; i++)
            {
                Tick(director, frame);
            }

            Require(frame.CompletedLaps == 0 && frame.Opponents[0].CompletedLaps == 0,
                    "Il controllo vale solo se nessuno ha chiuso un giro.");
            Require(director.FormationPhase != FormationPhase.Rolling,
                    "La ricognizione gira all'infinito: la gara non parte piu.");
        }

        /// <summary>
        /// Un conteggio giri ereditato dalla sessione precedente non chiude la ricognizione.
        ///
        /// Assetto Corsa lascia in memoria i dati della sessione appena chiusa, e per una ventina
        /// di secondi si leggono quelli. Se il giro del gruppo valesse da subito, una gara che
        /// comincia dopo le prove troverebbe i giri delle prove e schiererebbe immediatamente.
        /// </summary>
        private static void StaleLapsDoNotEndRolling()
        {
            var director = Director(Settings());
            var frame = Frame();

            // I giri delle prove, ancora in memoria.
            frame.Opponents[0].CompletedLaps = 7;

            Tick(director, frame, 2);
            Require(director.FormationPhase == FormationPhase.Rolling,
                    "Un conteggio giri ereditato ha schierato subito la griglia.");
        }

        private static void Require(bool value, string message)
        {
            if (!value) throw new InvalidOperationException(message);
        }

        private static FormationLapSettings Settings() => new FormationLapSettings
        {
            GridHoldSeconds = 1, GridTimeoutSeconds = 2,
            LightsCount = 5, LightsIntervalSeconds = 1,
            LightsMinSeconds = 0.5, LightsMaxSeconds = 0.5
        };

        private static RaceDirector Director(FormationLapSettings settings)
        {
            var director = new RaceDirector(123);
            director.BeginSession(new Circuit(),
                new SessionConfig { Kind = SessionKind.Race, FormationLap = true },
                new RaceControlRules { Formation = settings });
            return director;
        }

        private static TelemetryFrame Frame() => new TelemetryFrame
        {
            GameRunning = true, SessionTypeName = "Race", TrackId = "formation-check",
            DeltaSeconds = 0.25, Position = 1, TotalLaps = 10,
            Opponents = { new OpponentFrame { Name = "Driver", Position = 2 } }
        };

        private static void Tick(RaceDirector director, TelemetryFrame frame, int times = 1)
        {
            for (int i = 0; i < times; i++) director.Update(frame, "player", "Player");
        }

        private static void ReturnToGrid(RaceDirector director, TelemetryFrame frame)
        {
            Tick(director, frame);
            frame.CompletedLaps = 1;
            Tick(director, frame);
            Require(director.FormationPhase == FormationPhase.Forming, "Griglia non raggiunta.");
        }

        private static void AutomaticGrid()
        {
            var settings = Settings();
            settings.PlayerParksOnGrid = false;
            var director = Director(settings);
            var frame = Frame();
            ReturnToGrid(director, frame);
            Tick(director, frame, 5);
            Require(!director.PitLaneStart, "Giocatore mandato ai box con schieramento automatico.");
            Require(director.FormationPhase == FormationPhase.Lights, "Semaforo non avviato.");
        }

        private static void ManualGridTimeout()
        {
            var director = Director(Settings());
            var frame = Frame();
            ReturnToGrid(director, frame);
            Tick(director, frame, 7);
            Require(!director.PitLaneStart && director.FormationPhase == FormationPhase.Forming,
                "Timeout anticipato.");
            Tick(director, frame);
            Require(director.PitLaneStart, "Timeout senza partenza dai box.");
            Tick(director, frame, 40);
            Require(director.FormationPhase == FormationPhase.Away && director.LightsOn == 0,
                "Il timeout ha bloccato il via.");
        }

        private static void LightsTiming()
        {
            var director = Director(Settings());
            var frame = Frame();
            ReturnToGrid(director, frame);
            director.ReportGrid(true, 0);
            Tick(director, frame, 5);
            Require(director.FormationPhase == FormationPhase.Lights && director.LightsOn == 1,
                "La fase semaforo non accende subito la prima luce.");
            for (int light = 2; light <= 5; light++)
            {
                Tick(director, frame, 4);
                Require(director.LightsOn == light, "Sequenza delle luci errata.");
            }
            Tick(director, frame);
            Require(director.FormationPhase == FormationPhase.Lights, "Verde anticipato.");
            Tick(director, frame);
            Require(director.Status == RaceControlStatus.Green && director.LightsOn == 0,
                "Il ritardo include un intervallo aggiuntivo dopo la quinta luce.");
        }

        private static void InactiveFrames()
        {
            var director = Director(Settings());
            var frame = Frame();
            ReturnToGrid(director, frame);
            frame.InMenu = true;
            Tick(director, frame, 40);
            frame.InMenu = false;
            frame.Replay = true;
            Tick(director, frame, 40);
            frame.Replay = false;
            frame.Paused = true;
            Tick(director, frame, 40);
            Require(director.FormationPhase == FormationPhase.Forming && !director.PitLaneStart,
                "La procedura avanza senza una sessione guidabile.");
        }

        private static void ResetPace()
        {
            var director = Director(Settings());
            director.BeginSession(new Circuit(), new SessionConfig { Kind = SessionKind.Practice1 }, new RaceControlRules());
            Require(director.TargetSpeedKmh == 0, "Limite di formazione ereditato dalle prove.");
        }

        private static void ReopenSameRace()
        {
            var engine = new SessionEngine();
            var frame = Frame();
            engine.ProcessFrame(frame);
            var first = engine.RaceControl;
            frame.GameRunning = false;
            engine.ProcessFrame(frame);
            frame.GameRunning = true;
            engine.ProcessFrame(frame);
            Require(!ReferenceEquals(first, engine.RaceControl), "La stessa gara non viene riaperta dopo la disconnessione.");
        }

        private static void FinishedSession()
        {
            var engine = new SessionEngine();
            var frame = Frame();
            engine.ProcessFrame(frame);
            var first = engine.RaceControl;
            frame.FlagCheckered = true;
            engine.ProcessFrame(frame);
            engine.ProcessFrame(frame);
            Require(ReferenceEquals(first, engine.RaceControl), "La bandiera a scacchi ha aperto una gara nuova.");
        }

        private static void PitDelayBridge()
        {
            var championship = SeedData.DefaultChampionship("Formation bridge check");
            championship.Rules.RaceControl.Formation.PitLaneStartDelaySeconds = 3.5;
            var round = championship.Calendar[0];
            var circuit = championship.CircuitOf(round);
            var frame = Frame();
            frame.TrackId = circuit.AcTrackId;
            frame.TrackConfig = circuit.AcTrackConfig;
            var engine = new SessionEngine();
            engine.SetChampionship(championship);
            engine.ProcessFrame(frame);
            Require(engine.State.PitLaneStartDelaySeconds == 3.5, "Impostazione persa nel LiveState.");
            LiveBridge.PublishState(engine.State, true);
            Require(File.ReadAllText(PitwallPaths.AcRaceControlFile).Contains("pitstartdelay=3.50\n"),
                "Attesa mancante nel protocollo Lua.");
        }

        private static void InvalidLightsInterval()
        {
            var settings = Settings();
            settings.LightsIntervalSeconds = 0;
            var director = Director(settings);
            var frame = Frame();
            ReturnToGrid(director, frame);
            director.ReportGrid(true, 0);
            Tick(director, frame, 40);
            Require(director.FormationPhase == FormationPhase.Away && director.LightsOn == 0,
                "Configurazione semaforo non valida senza ripiego.");
        }
    }
}
