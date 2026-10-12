using System;
using System.Linq;
using Pitwall.Live;
using Pitwall.Model;
using Pitwall.RaceControl;
using Pitwall.Rules;

namespace Pitwall.Simulator
{
    // FIA 2026 Section B, Issue 09 (01/10/2026), B5.12 and B5.13.
    // https://www.fia.com/system/files/documents/fia_2026_f1_regulations_-_section_b_sporting_-_iss_09_-_2026-10-01.pdf
    // The incident thresholds below test Pitwall heuristics, not FIA-defined thresholds.
    // Still simplified: sector deltas, actual SC queue/unlapping and per-car restart line.
    internal static class SafetyChecks
    {
        public static void Run(Action<string, Action> check)
        {
            check("SC/VSC: griglia immobile senza falso incidente", StationaryGrid);
            check("SC/VSC: danni preesistenti e danni ai box ignorati", DamageBaseline);
            check("SC/VSC: gialla, poi VSC per sosta persistente", StopEscalation);
            check("SC/VSC: cluster sul traguardo senza dipendenza dall'ordine", Cluster);
            check("SC/VSC: vetture lontane non diventano un cluster", SeparateStops);
            check("SC/VSC: peggioramento VSC verso SC una sola volta", Escalation);
            check("SC/VSC: scelta indipendente dal sorteggio del circuito", Deterministic);
            check("SC/VSC: ostacolo persistente impedisce fine VSC", PersistentVsc);
            check("SC/VSC: VSC ENDING dura fra 10 e 15 secondi", EndingWindow);
            check("SC/VSC: nuovo pericolo annulla VSC ENDING", InterruptedEnding);
            check("SC/VSC: pausa e replay non consumano il preavviso", PauseEnding);
            check("SC/VSC: nuovo intervento anche subito dopo il verde", NoCooldown);
            check("SC/VSC: incidente all'ultimo giro", FinalLap);
            check("SC/VSC: nessun intervento dopo scacchi o in formazione", GuardedStates);
            check("SC/VSC: nessuna chiusura automatica dei box", PitEntry);
            check("SC/VSC: SC resta fuori finche resta l'ostacolo", PersistentSc);
            check("SC/VSC: gialla persistente con neutralizzazioni disabilitate", PersistentYellow);
            check("SC/VSC: ritiri senza ostacoli non generano gialle", Retirement);
            check("SC/VSC: reset cancella countdown e pericoli", Reset);
            check("SC/VSC: scelta alternativa se VSC disabilitata", DisabledVsc);
            check("SC/VSC: box e Safety Car esclusi dagli ostacoli", ExcludedCars);
            check("SC/VSC: sosta breve risolta senza neutralizzazione", TransientStop);
        }

        private static void Require(bool condition, string message)
        { if (!condition) throw new InvalidOperationException(message); }

        private static RaceControlRules Rules() => new RaceControlRules
        {
            VscMinimumSeconds = 0, IncidentClearSeconds = 2,
            TrackLimitsEnabled = false, PitLaneSpeedingEnabled = false,
            SafetyCar = new SafetyCarSettings { UseRealCar = false, AllowUnlapping = false }
        };
        private static RaceDirector Director(RaceControlRules rules = null, double probability = 0)
        {
            var d = new RaceDirector(123);
            d.BeginSession(new Circuit { LengthMeters = 5000, SafetyCarProbability = probability },
                new SessionConfig { Kind = SessionKind.Race, FormationLap = false }, rules ?? Rules());
            return d;
        }
        private static TelemetryFrame Frame() => new TelemetryFrame
        {
            GameRunning = true, DeltaSeconds = .25, PlayerName = "Player", Position = 3,
            SpeedKmh = 150, CompletedLaps = 4, TotalLaps = 20, TrackLengthMeters = 5000,
            TrackPositionPercent = .5,
            Opponents = {
                new OpponentFrame { Name = "A", SpeedKmh = 150, TrackPositionPercent = .2, Position = 1 },
                new OpponentFrame { Name = "B", SpeedKmh = 150, TrackPositionPercent = .8, Position = 2 }
            }
        };
        private static void Tick(RaceDirector d, TelemetryFrame f, double seconds = .25)
        { for (int i = 0; i < (int)Math.Ceiling(seconds / f.DeltaSeconds); i++) d.Update(f, "player", f.PlayerName); }

        private static void StationaryGrid()
        {
            var d = Director(); var f = Frame(); f.CompletedLaps = 0; f.SpeedKmh = 0;
            foreach (var o in f.Opponents) o.SpeedKmh = 0;
            Tick(d, f, 30);
            Require(d.Status == RaceControlStatus.Green && d.Incidents.Count == 0, "La griglia chiama la SC.");
        }
        private static void DamageBaseline()
        {
            var d = Director(); var f = Frame(); f.DamageMax = .6; Tick(d, f);
            f.InPitLane = true; f.DamageMax = 1; Tick(d, f, 10);
            f.InPitLane = false; Tick(d, f);
            Require(d.Incidents.Count == 0, "Danno vecchio o ai box scambiato per impatto.");
        }
        private static void StopEscalation()
        {
            var d = Director(); var f = Frame(); f.Opponents[0].SpeedKmh = 0;
            Tick(d, f, 2); Require(d.Status == RaceControlStatus.LocalYellow, "Manca la gialla iniziale.");
            Tick(d, f, 2); Require(d.Status == RaceControlStatus.VirtualSafetyCar, "Manca l'aggravamento a VSC.");
            Require(d.Incidents.Count == 2 && d.Incidents[0].Id == d.Incidents[1].Id && d.Incidents[0].Severity == 2,
                "Escalation duplicata o storico mutato.");
            Tick(d, f, 20); Require(d.Incidents.Count == 2, "Incidente ripetuto ogni frame.");
        }
        private static void Cluster()
        {
            foreach (bool reverse in new[] { false, true })
            {
                var d = Director(); var f = Frame();
                f.Opponents[0].TrackPositionPercent = .99; f.Opponents[1].TrackPositionPercent = .01;
                foreach (var o in f.Opponents) o.SpeedKmh = 0;
                if (reverse) f.Opponents.Reverse();
                Tick(d, f, 4);
                Require(d.Status == RaceControlStatus.SafetyCar && d.SafetyCarPeriods == 1 && d.VirtualSafetyCarPeriods == 0,
                    "Due stop vicini non generano direttamente SC.");
            }
        }
        private static void SeparateStops()
        {
            var d = Director(); var f = Frame(); foreach (var o in f.Opponents) o.SpeedKmh = 0;
            Tick(d, f, 4); Require(d.Status == RaceControlStatus.VirtualSafetyCar, "Stop lontani aggregati.");
        }
        private static void Escalation()
        {
            var d = Director(); var f = Frame(); Tick(d, f); f.Opponents[0].SpeedKmh = 0;
            Tick(d, f, 4); f.DamageMax = .6; Tick(d, f, 20);
            Require(d.Status == RaceControlStatus.SafetyCar && d.SafetyCarPeriods == 1 && d.VirtualSafetyCarPeriods == 1,
                "VSC non promossa a SC o conteggi duplicati.");
        }
        private static void Deterministic()
        {
            foreach (double p in new[] { 0.0, 1.0 })
            {
                var d = Director(probability: p); var f = Frame(); Tick(d, f); f.DamageMax = .5; Tick(d, f);
                Require(d.Status == RaceControlStatus.SafetyCar, "Decisione ancora casuale.");
            }
        }
        private static void PersistentVsc()
        {
            var d = Director(); var f = Frame(); f.Opponents[0].SpeedKmh = 0; Tick(d, f, 100);
            Require(d.Status == RaceControlStatus.VirtualSafetyCar && !d.VirtualSafetyCarEnding, "Timer libera pista occupata.");
            f.Opponents[0].SpeedKmh = 100; Tick(d, f, .5); f.Opponents[0].SpeedKmh = 0; Tick(d, f, 3);
            Require(!d.VirtualSafetyCarEnding, "Una lettura momentanea cancella l'ostacolo.");
            f.Opponents[0].InPit = true; Tick(d, f, 3);
            Require(d.VirtualSafetyCarEnding, "Rimozione ai box non avvia il preavviso.");
            Tick(d, f, 16); Require(d.Status == RaceControlStatus.Green && d.TargetSpeedKmh == 0, "VSC non termina pulita.");
        }
        private static void EndingWindow()
        {
            var d = Director(); var f = Frame(); d.DeployVirtualSafetyCar("test", f); d.EndNeutralisation(f);
            Require(d.VirtualSafetyCarEnding && d.StatusText.Contains("VSC"), "Preavviso non esposto.");
            for (int i = 0; i < 39; i++) { d.EndNeutralisation(f); Tick(d, f); }
            Require(d.Status == RaceControlStatus.VirtualSafetyCar && d.TargetSpeedKmh > 0, "Verde prima di 10 secondi.");
            Tick(d, f, 5.25); Require(d.Status == RaceControlStatus.Green, "Preavviso oltre 15 secondi o clic lo riavvia.");
        }
        private static void InterruptedEnding()
        {
            var d = Director(); var f = Frame(); d.DeployVirtualSafetyCar("test", f); d.EndNeutralisation(f);
            Tick(d, f, 8); f.Opponents[0].SpeedKmh = 0; Tick(d, f, 2);
            Require(!d.VirtualSafetyCarEnding && d.Status == RaceControlStatus.VirtualSafetyCar, "Fine non revocata.");
            f.DamageMax = .6; Tick(d, f); Require(d.Status == RaceControlStatus.SafetyCar, "Aggravamento durante fine perso.");
        }
        private static void PauseEnding()
        {
            var d = Director(); var f = Frame(); d.DeployVirtualSafetyCar("test", f); d.EndNeutralisation(f);
            f.Paused = true; Tick(d, f, 30); f.Paused = false; f.Replay = true; Tick(d, f, 30);
            f.Replay = false; Tick(d, f, 9);
            Require(d.VirtualSafetyCarEnding, "Pausa/replay consuma il preavviso.");
        }
        private static void NoCooldown()
        {
            var d = Director(); var f = Frame(); Tick(d, f);
            d.DeployVirtualSafetyCar("test", f); d.EndNeutralisation(f); Tick(d, f, 15);
            f.DamageMax = .6; Tick(d, f);
            Require(d.Status == RaceControlStatus.SafetyCar, "Cooldown impedisce la nuova SC.");
        }
        private static void FinalLap()
        {
            var r = Rules(); r.NoSafetyCarInFinalLaps = 3; var d = Director(r); var f = Frame();
            f.CompletedLaps = f.TotalLaps - 1; Tick(d, f); f.DamageMax = .6; Tick(d, f);
            Require(d.Status == RaceControlStatus.SafetyCar, "Vecchia impostazione blocca ultimo giro.");
        }
        private static void GuardedStates()
        {
            var d = Director(); var f = Frame(); Tick(d, f); f.DamageMax = .6; f.FlagCheckered = true; Tick(d, f);
            d.DeploySafetyCar("test", f); d.DeployVirtualSafetyCar("test", f);
            Require(d.Status == RaceControlStatus.Checkered && d.Incidents.Count == 0, "Incidente dopo scacchi.");
            d.BeginSession(new Circuit(), new SessionConfig { Kind = SessionKind.Race, FormationLap = true }, Rules());
            f.FlagCheckered = false; d.DeploySafetyCar("test", f); d.DeployVirtualSafetyCar("test", f);
            Require(d.Status == RaceControlStatus.FormationLap, "Comando SC interrompe formazione.");
        }
        private static void PitEntry()
        {
            var d = Director(); var f = Frame(); d.DeploySafetyCar("test", f);
            Require(!d.PitLaneClosed, "Pit entry chiusa al deployment.");
            f.InPitLane = true; f.SpeedKmh = 60; Tick(d, f);
            Require(d.Penalties.Penalties.Count == 0, "Penalita per box chiusi sotto SC.");
        }
        private static void PersistentSc()
        {
            var d = Director(); var f = Frame(); foreach (var o in f.Opponents) { o.SpeedKmh = 0; o.TrackPositionPercent = .2; }
            Tick(d, f, 4);
            for (int i = 0; i < 8; i++) { f.CompletedLaps++; Tick(d, f, 1); }
            d.EndNeutralisation(f);
            Require(d.Status == RaceControlStatus.SafetyCar && d.SafetyCarPhase == SafetyCarPhase.Bunching,
                "SC richiamata con ostacoli.");
            foreach (var o in f.Opponents) o.InPit = true;
            Tick(d, f, 3);
            Require(d.Status == RaceControlStatus.SafetyCarEnding && d.SafetyCarPhase == SafetyCarPhase.InThisLap,
                "Manca preavviso dopo lunga permanenza, oppure verde diretto.");
        }
        private static void PersistentYellow()
        {
            var r = Rules(); r.SafetyCarEnabled = false; r.VirtualSafetyCarEnabled = false;
            var d = Director(r); var f = Frame(); f.Opponents[0].SpeedKmh = 0; Tick(d, f, 90);
            Require(d.Status == RaceControlStatus.LocalYellow && d.YellowSectors.Count > 0, "Gialla scade su ostacolo.");
            f.Opponents[0].InPit = true; Tick(d, f, 31);
            Require(d.Status == RaceControlStatus.Green, "Gialla non scade dopo rimozione.");
        }
        private static void Retirement()
        {
            var d = Director(); var f = Frame(); Tick(d, f); f.Opponents.Clear(); Tick(d, f, 10);
            Require(d.Status == RaceControlStatus.Green && d.Incidents.All(i => i.Kind == IncidentKind.CarRetired),
                "Ritiro senza evidenza di ostacolo neutralizza.");
        }
        private static void Reset()
        {
            var d = Director(); var f = Frame(); d.DeployVirtualSafetyCar("test", f); d.EndNeutralisation(f);
            d.BeginSession(new Circuit(), new SessionConfig { Kind = SessionKind.Race, FormationLap = false }, Rules());
            Require(!d.VirtualSafetyCarEnding && d.TargetSpeedKmh == 0, "Countdown persistente fra sessioni.");
            Tick(d, f, 20); Require(d.Status == RaceControlStatus.Green, "Stato vecchio contamina nuova gara.");
        }
        private static void DisabledVsc()
        {
            var r = Rules(); r.VirtualSafetyCarEnabled = false; var d = Director(r); var f = Frame();
            f.Opponents[0].SpeedKmh = 0; Tick(d, f, 4);
            Require(d.Status == RaceControlStatus.SafetyCar, "Nessuna alternativa alla VSC disabilitata.");
        }
        private static void ExcludedCars()
        {
            var d = Director(); var f = Frame(); f.InPit = true; f.SpeedKmh = 0;
            f.Opponents[0].InPitLane = true; f.Opponents[0].SpeedKmh = 0;
            f.Opponents[1].Name = SafetyCar.DriverName; f.Opponents[1].SpeedKmh = 0;
            Tick(d, f, 30);
            Require(d.Incidents.Count == 0 && d.Status == RaceControlStatus.Green, "Box/SC scambiati per incidenti.");
        }
        private static void TransientStop()
        {
            var d = Director(); var f = Frame(); f.Opponents[0].SpeedKmh = 0; Tick(d, f, 2);
            f.Opponents[0].SpeedKmh = 100; Tick(d, f, 7);
            Require(d.Status == RaceControlStatus.Green && d.VirtualSafetyCarPeriods == 0 && d.SafetyCarPeriods == 0,
                "Sosta breve risolta causa neutralizzazione.");
        }
    }
}
