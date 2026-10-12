using System;
using System.Collections.Generic;
using System.Linq;
using Pitwall.Model;

namespace Pitwall.Results
{
    /// <summary>The classification of one session, as published by race control.</summary>
    public sealed class SessionResult
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");

        public string ChampionshipId { get; set; } = "";

        public string RoundId { get; set; } = "";

        public SessionKind Kind { get; set; }

        public DateTime RecordedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>Laps the winner completed.</summary>
        public int LapsCompleted { get; set; }

        /// <summary>Laps the session was scheduled for; zero for a timed session.</summary>
        public int ScheduledLaps { get; set; }

        /// <summary>True when the race was stopped early and reduced points apply.</summary>
        public bool Shortened { get; set; }

        public bool RedFlagged { get; set; }

        public int SafetyCarPeriods { get; set; }

        public int VirtualSafetyCarPeriods { get; set; }

        /// <summary>Weather summary shown in the history, for example "Asciutto, poi pioggia".</summary>
        public string WeatherSummary { get; set; } = "";

        public List<EntryResult> Entries { get; set; } = new List<EntryResult>();

        /// <summary>Radio and race-control log kept for the review screen.</summary>
        public List<string> RaceControlLog { get; set; } = new List<string>();

        /// <summary>Share of the scheduled distance actually completed, 0 to 1.</summary>
        public double CompletedFraction =>
            ScheduledLaps > 0 ? Math.Min(1.0, (double)LapsCompleted / ScheduledLaps) : 1.0;

        public EntryResult Winner => Entries.OrderBy(e => e.Position).FirstOrDefault();

        public EntryResult ForDriver(string driverId) => Entries.FirstOrDefault(e => e.DriverId == driverId);

        public EntryResult FastestLapEntry =>
            Entries.Where(e => e.BestLap > TimeSpan.Zero).OrderBy(e => e.BestLap).FirstOrDefault();

        /// <summary>Sorts entries by position and renumbers them, after a penalty is applied.</summary>
        public void Resequence()
        {
            var ordered = Entries
                .OrderBy(e => e.Status == FinishStatus.Disqualified || e.Status == FinishStatus.DidNotStart ? 2
                            : e.Status == FinishStatus.Retired ? 1 : 0)
                .ThenByDescending(e => e.Laps)
                .ThenBy(e => e.TotalTime + TimeSpan.FromSeconds(e.PenaltySeconds))
                .ToList();

            int p = 1;
            foreach (var e in ordered)
                e.Position = p++;

            Entries = ordered;
        }
    }

    /// <summary>One driver inside a session classification.</summary>
    public sealed class EntryResult
    {
        public string DriverId { get; set; } = "";

        /// <summary>Kept alongside the id so an exported result stays readable on its own.</summary>
        public string DriverName { get; set; } = "";

        public string TeamId { get; set; } = "";

        public string TeamName { get; set; } = "";

        public int Position { get; set; }

        public int GridPosition { get; set; }

        public int Laps { get; set; }

        public TimeSpan TotalTime { get; set; }

        public TimeSpan BestLap { get; set; }

        /// <summary>Gap to the winner. Null for the winner and for anyone not classified.</summary>
        public TimeSpan? GapToWinner { get; set; }

        public int LapsDown { get; set; }

        public FinishStatus Status { get; set; } = FinishStatus.Finished;

        /// <summary>Time penalties already added into <see cref="TotalTime"/> ordering.</summary>
        public double PenaltySeconds { get; set; }

        public int PitStops { get; set; }

        /// <summary>Compounds used, in order, for the two-compound check and the review screen.</summary>
        public List<string> CompoundsUsed { get; set; } = new List<string>();

        public bool SetFastestLap { get; set; }

        /// <summary>Points awarded, filled in by the standings calculator.</summary>
        public double Points { get; set; }

        /// <summary>Why points were reduced or withheld, if that happened.</summary>
        public string PointsNote { get; set; } = "";

        public override string ToString() => Position + ". " + DriverName;
    }
}
