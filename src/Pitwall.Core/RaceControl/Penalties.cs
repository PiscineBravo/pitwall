using System;
using System.Collections.Generic;
using System.Linq;

namespace Pitwall.RaceControl
{
    public enum PenaltyType
    {
        Warning,
        TimePenalty,
        DriveThrough,
        StopAndGo,
        GridPenalty,
        Disqualification
    }

    public enum OffenceKind
    {
        TrackLimits,
        PitLaneSpeeding,
        UnsafeRelease,
        JumpStart,
        VirtualSafetyCarDelta,
        OvertakeUnderSafetyCar,
        IgnoringBlueFlags,
        CausingACollision,
        MinimumPitStopTime,
        MissingMandatoryStop,
        TwoCompoundRule,
        Other
    }

    /// <summary>A decision of race control against one driver.</summary>
    public sealed class Penalty
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string DriverId { get; set; } = "";
        public string DriverName { get; set; } = "";
        public PenaltyType Type { get; set; } = PenaltyType.Warning;
        public OffenceKind Offence { get; set; } = OffenceKind.Other;

        /// <summary>Seconds added, for a time penalty.</summary>
        public double Seconds { get; set; }

        /// <summary>Grid places lost, for a grid penalty applied to the next race.</summary>
        public int GridPlaces { get; set; }

        public int Lap { get; set; }
        public DateTime IssuedUtc { get; set; } = DateTime.UtcNow;
        public string Reason { get; set; } = "";

        /// <summary>True once a drive-through or stop-and-go has actually been taken.</summary>
        public bool Served { get; set; }

        public int LicencePoints { get; set; }

        public string ShortText
        {
            get
            {
                switch (Type)
                {
                    case PenaltyType.Warning: return "AVVERTIMENTO";
                    case PenaltyType.TimePenalty: return "+" + (int)Seconds + "s";
                    case PenaltyType.DriveThrough: return "DRIVE THROUGH";
                    case PenaltyType.StopAndGo: return "STOP & GO " + (int)Seconds + "s";
                    case PenaltyType.GridPenalty: return "-" + GridPlaces + " IN GRIGLIA";
                    default: return "SQUALIFICA";
                }
            }
        }

        public override string ToString() => ShortText + " - " + Reason;
    }

    /// <summary>
    /// The ledger of everything race control has decided in the current session, plus the
    /// counters that lead up to those decisions, such as track-limit strikes.
    /// </summary>
    public sealed class PenaltyBook
    {
        private readonly Dictionary<string, int> _trackLimitStrikes = new Dictionary<string, int>();
        private readonly Dictionary<string, int> _blueFlagWarnings = new Dictionary<string, int>();

        public List<Penalty> Penalties { get; } = new List<Penalty>();

        public event Action<Penalty> PenaltyIssued;

        public void Clear()
        {
            Penalties.Clear();
            _trackLimitStrikes.Clear();
            _blueFlagWarnings.Clear();
        }

        public Penalty Issue(Penalty penalty)
        {
            Penalties.Add(penalty);
            PenaltyIssued?.Invoke(penalty);
            return penalty;
        }

        public Penalty IssueTime(string driverId, string driverName, double seconds, OffenceKind offence,
                                 string reason, int lap, int licencePoints = 0)
        {
            return Issue(new Penalty
            {
                DriverId = driverId,
                DriverName = driverName,
                Type = PenaltyType.TimePenalty,
                Seconds = seconds,
                Offence = offence,
                Reason = reason,
                Lap = lap,
                LicencePoints = licencePoints
            });
        }

        public Penalty IssueWarning(string driverId, string driverName, OffenceKind offence, string reason, int lap)
        {
            return Issue(new Penalty
            {
                DriverId = driverId,
                DriverName = driverName,
                Type = PenaltyType.Warning,
                Offence = offence,
                Reason = reason,
                Lap = lap
            });
        }

        /// <summary>
        /// Records one track-limits infringement and returns the penalty it caused, or null when
        /// the driver is still inside the allowance. The fourth strike costs five seconds, then
        /// every further strike costs five more, which is how the stewards handle it.
        /// </summary>
        public Penalty RegisterTrackLimits(string driverId, string driverName, int allowance,
                                           double penaltySeconds, int lap)
        {
            _trackLimitStrikes.TryGetValue(driverId, out int n);
            n++;
            _trackLimitStrikes[driverId] = n;

            if (n <= allowance)
            {
                return IssueWarning(driverId, driverName, OffenceKind.TrackLimits,
                    "Track limits " + n + "/" + allowance, lap);
            }

            return IssueTime(driverId, driverName, penaltySeconds, OffenceKind.TrackLimits,
                "Track limits, " + n + " infrazioni", lap);
        }

        public int TrackLimitStrikes(string driverId)
        {
            _trackLimitStrikes.TryGetValue(driverId, out int n);
            return n;
        }

        public void ResetTrackLimits(string driverId) => _trackLimitStrikes.Remove(driverId);

        public int BlueFlagWarnings(string driverId)
        {
            _blueFlagWarnings.TryGetValue(driverId, out int n);
            return n;
        }

        public void RegisterBlueFlagWarning(string driverId)
        {
            _blueFlagWarnings.TryGetValue(driverId, out int n);
            _blueFlagWarnings[driverId] = n + 1;
        }

        /// <summary>Time penalties still to be added to a driver classification.</summary>
        public double PendingSeconds(string driverId)
        {
            return Penalties
                .Where(p => p.DriverId == driverId && p.Type == PenaltyType.TimePenalty && !p.Served)
                .Sum(p => p.Seconds);
        }

        /// <summary>Drive-throughs or stop-and-go penalties the driver still owes.</summary>
        public IEnumerable<Penalty> PendingServable(string driverId)
        {
            return Penalties.Where(p => p.DriverId == driverId && !p.Served &&
                                        (p.Type == PenaltyType.DriveThrough || p.Type == PenaltyType.StopAndGo));
        }

        public bool IsDisqualified(string driverId) =>
            Penalties.Any(p => p.DriverId == driverId && p.Type == PenaltyType.Disqualification);

        public int LicencePoints(string driverId) =>
            Penalties.Where(p => p.DriverId == driverId).Sum(p => p.LicencePoints);

        public IEnumerable<Penalty> For(string driverId) => Penalties.Where(p => p.DriverId == driverId);
    }
}
