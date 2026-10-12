using System;
using System.Collections.Generic;
using System.Linq;
using Pitwall.Live;
using Pitwall.Model;
using Pitwall.RaceControl;

namespace Pitwall.Results
{
    /// <summary>
    /// Turns the last telemetry frame of a session into an official classification: positions,
    /// gaps, retirements and, above all, the time penalties race control handed out during the
    /// session. Assetto Corsa knows nothing about those, so the order it shows at the flag and
    /// the order Pitwall publishes can legitimately differ.
    /// </summary>
    public static class ClassificationBuilder
    {
        public static SessionResult Build(Championship championship, Round round, SessionConfig session,
                                          TelemetryFrame frame, RaceDirector raceControl,
                                          IDictionary<string, int> pitCounts = null,
                                          IDictionary<string, List<string>> compounds = null)
        {
            var result = new SessionResult
            {
                ChampionshipId = championship != null ? championship.Id : "",
                RoundId = round != null ? round.Id : "",
                Kind = session != null ? session.Kind : SessionKind.Race,
                RecordedUtc = DateTime.UtcNow
            };

            if (frame == null) return result;

            var circuit = championship != null ? championship.CircuitOf(round) : null;
            result.ScheduledLaps = session != null ? session.ResolveLaps(circuit) : 0;
            if (result.ScheduledLaps == 0 && frame.TotalLaps > 0) result.ScheduledLaps = frame.TotalLaps;

            if (raceControl != null)
            {
                result.SafetyCarPeriods = raceControl.SafetyCarPeriods;
                result.VirtualSafetyCarPeriods = raceControl.VirtualSafetyCarPeriods;
                result.RedFlagged = raceControl.RedFlagged;
                result.RaceControlLog = raceControl.Feed.Select(m => m.Text).ToList();
            }

            if (session != null && session.Weather != null)
                result.WeatherSummary = session.Weather.Summary;

            // ---- Collect every car, including the player ---------------------------------------
            var rows = new List<EntryResult>();

            foreach (var o in frame.Opponents)
            {
                // La vettura di sicurezza e un'iscritta come le altre per il gioco, ma non corre:
                // non entra nella classifica.
                if (SafetyCar.IsSafetyCar(o.Name)) continue;

                var driver = championship != null ? championship.MatchTelemetryName(o.Name) : null;
                var team = driver != null && championship != null ? championship.TeamById(driver.TeamId) : null;

                rows.Add(new EntryResult
                {
                    DriverId = driver != null ? driver.Id : "",
                    DriverName = driver != null ? driver.FullName : o.Name,
                    TeamId = driver != null ? driver.TeamId : "",
                    TeamName = team != null ? team.Name : o.TeamName,
                    Position = o.Position,
                    Laps = o.CompletedLaps,
                    BestLap = o.BestLapTime,
                    PitStops = o.PitCount,
                    Status = o.IsConnected ? FinishStatus.Finished : FinishStatus.Retired
                });
            }

            // SimHub normally includes the player among the opponents. When it does not, add it.
            bool playerPresent = frame.Opponents.Any(o => o.IsPlayer);
            if (!playerPresent)
            {
                var playerDriver = championship != null ? championship.PlayerDriver : null;
                var playerTeam = playerDriver != null && championship != null
                    ? championship.TeamById(playerDriver.TeamId) : null;

                rows.Add(new EntryResult
                {
                    DriverId = playerDriver != null ? playerDriver.Id : "",
                    DriverName = playerDriver != null ? playerDriver.FullName : frame.PlayerName,
                    TeamId = playerDriver != null ? playerDriver.TeamId : "",
                    TeamName = playerTeam != null ? playerTeam.Name : "",
                    Position = frame.Position,
                    Laps = frame.CompletedLaps,
                    BestLap = frame.BestLapTime,
                    Status = FinishStatus.Finished
                });
            }

            // ---- Apply what race control decided -------------------------------------------------
            if (raceControl != null)
            {
                foreach (var row in rows)
                {
                    if (string.IsNullOrEmpty(row.DriverId)) continue;
                    row.PenaltySeconds = raceControl.Penalties.PendingSeconds(row.DriverId);
                    if (raceControl.Penalties.IsDisqualified(row.DriverId))
                        row.Status = FinishStatus.Disqualified;
                }
            }

            // ---- Extra data the caller tracked -----------------------------------------------------
            if (pitCounts != null)
            {
                foreach (var row in rows)
                    if (!string.IsNullOrEmpty(row.DriverName) && pitCounts.TryGetValue(row.DriverName, out int n))
                        row.PitStops = Math.Max(row.PitStops, n);
            }

            if (compounds != null)
            {
                foreach (var row in rows)
                    if (!string.IsNullOrEmpty(row.DriverName) && compounds.TryGetValue(row.DriverName, out var list))
                        row.CompoundsUsed = list.ToList();
            }

            int winnerLaps = rows.Count > 0 ? rows.Max(r => r.Laps) : 0;
            result.LapsCompleted = winnerLaps;
            result.Shortened = result.ScheduledLaps > 0 && winnerLaps < result.ScheduledLaps - 1;

            // Anyone more than one lap behind the winner is classified as lapped.
            foreach (var row in rows)
            {
                row.LapsDown = Math.Max(0, winnerLaps - row.Laps);
                if (row.Status == FinishStatus.Finished && row.LapsDown >= 1)
                    row.Status = FinishStatus.Lapped;

                // Race distance rule: less than 90 percent of the winner means not classified.
                if (winnerLaps > 0 && row.Laps < winnerLaps * 0.9 &&
                    (result.Kind == SessionKind.Race || result.Kind == SessionKind.Sprint))
                    row.Status = row.Status == FinishStatus.Disqualified ? row.Status : FinishStatus.NotClassified;
            }

            // ---- Order ---------------------------------------------------------------------------
            bool timed = result.Kind != SessionKind.Race && result.Kind != SessionKind.Sprint;
            if (timed)
            {
                // Practice and qualifying are ordered on best lap, not on track position.
                var ordered = rows
                    .OrderBy(r => r.BestLap <= TimeSpan.Zero ? 1 : 0)
                    .ThenBy(r => r.BestLap <= TimeSpan.Zero ? TimeSpan.MaxValue : r.BestLap)
                    .ToList();

                int p = 1;
                foreach (var r in ordered)
                {
                    r.Position = p++;
                    r.Status = r.BestLap > TimeSpan.Zero ? FinishStatus.Finished : FinishStatus.NotClassified;
                }

                var pole = ordered.FirstOrDefault(r => r.BestLap > TimeSpan.Zero);
                foreach (var r in ordered)
                    r.GapToWinner = pole != null && r.BestLap > TimeSpan.Zero ? r.BestLap - pole.BestLap : (TimeSpan?)null;

                result.Entries = ordered;
            }
            else
            {
                // A race is ordered on distance, then on the time each car took, with penalties
                // added before the order is settled.
                var ordered = rows
                    .OrderBy(r => Rank(r.Status))
                    .ThenByDescending(r => r.Laps)
                    .ThenBy(r => r.Position <= 0 ? int.MaxValue : r.Position)
                    .ToList();

                // Penalties can reorder cars that finished on the same lap. Without per-car race
                // time from the game the safest model is to compare the on-track gaps between
                // neighbours, which the plugin recorded into GapToWinner before the flag.
                ApplyTimePenalties(ordered);

                int p = 1;
                foreach (var r in ordered) r.Position = p++;
                result.Entries = ordered;
            }

            foreach (var row in result.Entries)
            {
                var driver = championship != null ? championship.DriverById(row.DriverId) : null;
                if (driver != null && string.IsNullOrEmpty(row.DriverName)) row.DriverName = driver.FullName;
            }

            var fastest = result.FastestLapEntry;
            if (fastest != null) fastest.SetFastestLap = true;

            return result;
        }

        private static int Rank(FinishStatus status)
        {
            switch (status)
            {
                case FinishStatus.Finished:
                case FinishStatus.Lapped:
                    return 0;
                case FinishStatus.NotClassified:
                    return 1;
                case FinishStatus.Retired:
                    return 2;
                case FinishStatus.DidNotStart:
                    return 3;
                default:
                    return 4;
            }
        }

        /// <summary>
        /// Moves cars down the order when a time penalty is bigger than the gap to the car
        /// behind. Only neighbours on the same lap can swap, which is how the stewards apply it.
        /// </summary>
        private static void ApplyTimePenalties(List<EntryResult> ordered)
        {
            for (int i = 0; i < ordered.Count; i++)
            {
                var entry = ordered[i];
                if (entry.PenaltySeconds <= 0) continue;

                double remaining = entry.PenaltySeconds;
                int index = i;

                while (index + 1 < ordered.Count)
                {
                    var next = ordered[index + 1];
                    if (next.Laps != entry.Laps) break;

                    double gapToNext = GapBetween(entry, next);
                    if (gapToNext <= 0 || remaining <= gapToNext) break;

                    ordered[index] = next;
                    ordered[index + 1] = entry;
                    index++;
                }
            }
        }

        private static double GapBetween(EntryResult ahead, EntryResult behind)
        {
            if (ahead.GapToWinner.HasValue && behind.GapToWinner.HasValue)
                return (behind.GapToWinner.Value - ahead.GapToWinner.Value).TotalSeconds;

            // No gap data available: assume a second between neighbours so a five second
            // penalty still costs positions instead of silently doing nothing.
            return 1.0;
        }
    }
}
