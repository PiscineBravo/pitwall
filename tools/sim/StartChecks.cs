using System;
using Pitwall.Live;
using Pitwall.Model;
using Pitwall.RaceControl;
using Pitwall.Rules;

namespace Pitwall.Simulator
{
    /// <summary>
    /// La partenza, provata senza avviare il gioco.
    ///
    /// Una partenza, letta dai dati, e indistinguibile da un incidente: venti vetture ferme una
    /// accanto all'altra. Esisteva gia una protezione contro questo - "griglia immobile senza falso
    /// incidente" - ma e legata al giro zero, e con il giro di ricognizione al semaforo verde le
    /// vetture sono al giro uno. Quella protezione quindi non si applicava piu.
    ///
    /// Il risultato, misurato in pista: verde al secondo 583, gialla locale al 585, safety car al
    /// 587. E poiche le IA sotto safety car vanno ai box - che e la strategia giusta al momento
    /// sbagliato - la gara cominciava con il gruppo intero in corsia box.
    ///
    /// La protezione nuova non guarda i giri ma il semaforo: conta i secondi dal verde che diamo
    /// noi. Vale solo per la partenza con ricognizione, che e quella che Pitwall orchestra; una
    /// sessione presa in corsa resta scoperta di proposito, perche li un gruppo fermo e davvero
    /// un incidente.
    /// </summary>
    internal static class StartChecks
    {
        public static void Run(Action<string, Action> check)
        {
            check("Via: nessuna neutralizzazione nei primi secondi dal verde", NoSafetyCarAtTheStart);
            check("Via: la neutralizzazione torna possibile dopo l'assestamento", SafetyCarAfterSettling);
            check("Via: sessione presa in corsa senza finestra di assestamento", TakenOverMidRace);
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        private static RaceDirector Director(double settle, bool fromTheStart = true)
        {
            var director = new RaceDirector(123);
            director.BeginSession(
                new Circuit { LengthMeters = 5000 },
                new SessionConfig { Kind = SessionKind.Race, FormationLap = true },
                new RaceControlRules
                {
                    SafetyCar = new SafetyCarSettings { StartSettleSeconds = settle },
                    Formation = new FormationLapSettings
                    {
                        PlayerParksOnGrid = false,
                        GridHoldSeconds = 1, GridTimeoutSeconds = 2,
                        LightsCount = 1, LightsIntervalSeconds = 0.25,
                        LightsMinSeconds = 0.25, LightsMaxSeconds = 0.25
                    }
                },
                fromTheStart);
            return director;
        }

        private static TelemetryFrame Frame() => new TelemetryFrame
        {
            GameRunning = true, DeltaSeconds = .25, PlayerName = "Player", Position = 3,
            SpeedKmh = 150, TotalLaps = 20, TrackLengthMeters = 5000, TrackPositionPercent = .5,
            Opponents =
            {
                new OpponentFrame { Name = "A", SpeedKmh = 150, TrackPositionPercent = .2, Position = 1 },
                new OpponentFrame { Name = "B", SpeedKmh = 150, TrackPositionPercent = .2, Position = 2 }
            }
        };

        private static void Tick(RaceDirector director, TelemetryFrame frame, double seconds)
        {
            int passi = (int)Math.Ceiling(seconds / frame.DeltaSeconds);
            for (int i = 0; i < passi; i++) director.Update(frame, "player", frame.PlayerName);
        }

        /// <summary>Porta la gara dalla ricognizione al verde, come in pista.</summary>
        private static void AlVerde(RaceDirector director, TelemetryFrame frame)
        {
            Tick(director, frame, 1);
            frame.CompletedLaps = 1;          // la ricognizione, che Assetto Corsa conta come un giro
            Tick(director, frame, 12);

            Require(director.Status == RaceControlStatus.Green,
                    "Il verde non e arrivato: stato " + director.Status + ".");
        }

        private static void Ferma(TelemetryFrame frame)
        {
            foreach (var o in frame.Opponents) o.SpeedKmh = 0;
            frame.SpeedKmh = 0;
        }

        private static bool Neutralizzata(RaceDirector director) =>
            director.Status == RaceControlStatus.SafetyCar ||
            director.Status == RaceControlStatus.SafetyCarEnding ||
            director.Status == RaceControlStatus.VirtualSafetyCar;

        /// <summary>
        /// Appena dato il verde, un gruppo fermo e una partenza, non un incidente.
        /// </summary>
        private static void NoSafetyCarAtTheStart()
        {
            var director = Director(25);
            var frame = Frame();

            AlVerde(director, frame);
            Ferma(frame);
            Tick(director, frame, 10);

            Require(!Neutralizzata(director),
                    "Safety car nei primi secondi dal verde: le IA andrebbero tutte ai box.");
        }

        /// <summary>
        /// Passato l'assestamento lo stesso quadro la fa uscire: la regola e un ritardo, non una
        /// cancellazione. Senza questo controllo si potrebbe "risolvere" il difetto spegnendo del
        /// tutto le neutralizzazioni, e il primo controllo sarebbe verde lo stesso.
        /// </summary>
        private static void SafetyCarAfterSettling()
        {
            var director = Director(1);
            var frame = Frame();

            AlVerde(director, frame);
            Ferma(frame);
            Tick(director, frame, 30);

            Require(Neutralizzata(director),
                    "Con due vetture ferme vicine e l'assestamento passato, la safety car non esce piu.");
        }

        /// <summary>
        /// Una sessione presa in corsa non ha nessuna partenza da proteggere.
        ///
        /// Qui non c'e ricognizione, non c'e semaforo e non c'e griglia: due vetture ferme vicine
        /// sono un incidente e basta, e la safety car deve uscire subito come ha sempre fatto.
        /// </summary>
        private static void TakenOverMidRace()
        {
            var director = Director(600, fromTheStart: false);
            var frame = Frame();
            frame.CompletedLaps = 4;

            Tick(director, frame, 5);
            Ferma(frame);
            Tick(director, frame, 15);

            Require(Neutralizzata(director),
                    "La finestra di assestamento blocca anche una sessione presa in corsa.");
        }
    }
}
