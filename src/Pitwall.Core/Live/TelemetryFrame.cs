using System;
using System.Collections.Generic;

namespace Pitwall.Live
{
    /// <summary>
    /// One normalised telemetry sample. The SimHub plugin fills this in from GameData so that
    /// nothing in Pitwall.Core has to reference SimHub, which keeps the race-control and
    /// strategy code testable and reusable.
    /// </summary>
    public sealed class TelemetryFrame
    {
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;

        /// <summary>Seconds since the previous frame.</summary>
        public double DeltaSeconds { get; set; }

        public bool GameRunning { get; set; }
        public bool InMenu { get; set; }
        public bool Replay { get; set; }
        public bool Paused { get; set; }

        // ---- Session ---------------------------------------------------------------------

        /// <summary>Raw session name from the game: Practice, Qualify, Race, Hotlap ...</summary>
        public string SessionTypeName { get; set; } = "";

        public string TrackId { get; set; } = "";
        public string TrackConfig { get; set; } = "";
        public string TrackName { get; set; } = "";
        public double TrackLengthMeters { get; set; }

        public TimeSpan SessionTimeLeft { get; set; }
        public TimeSpan SessionElapsed { get; set; }
        public int TotalLaps { get; set; }
        public int RemainingLaps { get; set; }

        // ---- Player car ------------------------------------------------------------------

        public string PlayerName { get; set; } = "";
        public string CarId { get; set; } = "";

        public int Position { get; set; }
        public int OpponentsCount { get; set; }

        public int CompletedLaps { get; set; }
        public int CurrentLap { get; set; }
        public TimeSpan CurrentLapTime { get; set; }
        public TimeSpan LastLapTime { get; set; }
        public TimeSpan BestLapTime { get; set; }
        public bool LapValid { get; set; } = true;

        public double TrackPositionPercent { get; set; }
        public double TrackPositionMeters { get; set; }

        public double SpeedKmh { get; set; }
        public double Throttle { get; set; }
        public double Brake { get; set; }
        public string Gear { get; set; } = "N";
        public double Rpm { get; set; }

        public double Fuel { get; set; }
        public double MaxFuel { get; set; }
        public double? EstimatedFuelLaps { get; set; }

        public string TyreCompound { get; set; } = "";
        public double TyreWearFrontLeft { get; set; }
        public double TyreWearFrontRight { get; set; }
        public double TyreWearRearLeft { get; set; }
        public double TyreWearRearRight { get; set; }
        public double TyreTemperatureAvg { get; set; }
        public double TyrePressureAvg { get; set; }

        public double BrakeTemperatureAvg { get; set; }
        public double WaterTemperature { get; set; }
        public double OilTemperature { get; set; }
        public double DamageAvg { get; set; }
        public double DamageMax { get; set; }

        public bool InPit { get; set; }
        public bool InPitLane { get; set; }
        public bool PitLimiterOn { get; set; }
        public double LastPitStopDuration { get; set; }

        public int DrsAvailable { get; set; }
        public int DrsEnabled { get; set; }
        public double ErsPercent { get; set; }

        // ---- Flags reported by the game ---------------------------------------------------

        public bool FlagYellow { get; set; }
        public bool FlagBlue { get; set; }
        public bool FlagWhite { get; set; }
        public bool FlagCheckered { get; set; }
        public bool FlagBlack { get; set; }

        // ---- Conditions --------------------------------------------------------------------

        public double AirTemperature { get; set; }
        public double RoadTemperature { get; set; }

        public List<OpponentFrame> Opponents { get; set; } = new List<OpponentFrame>();
    }

    /// <summary>One other car, as seen in this frame.</summary>
    public sealed class OpponentFrame
    {
        public string Name { get; set; } = "";
        public string CarNumber { get; set; } = "";
        public string TeamName { get; set; } = "";
        public bool IsPlayer { get; set; }
        public bool IsConnected { get; set; } = true;

        public int Position { get; set; }
        public int CompletedLaps { get; set; }
        public double TrackPositionPercent { get; set; }
        public double SpeedKmh { get; set; }

        public TimeSpan BestLapTime { get; set; }
        public TimeSpan LastLapTime { get; set; }

        /// <summary>Settori dell'ultimo giro completo, in secondi. Null quando mancano.</summary>
        public double? LastSector1 { get; set; }
        public double? LastSector2 { get; set; }
        public double? LastSector3 { get; set; }

        /// <summary>I migliori settori di questa vettura, in secondi.</summary>
        public double? BestSector1 { get; set; }
        public double? BestSector2 { get; set; }
        public double? BestSector3 { get; set; }

        /// <summary>Posizione in griglia alla partenza, per calcolare quante se ne sono guadagnate.</summary>
        public int? StartPosition { get; set; }

        /// <summary>Seconds to the player; negative when the opponent is ahead.</summary>
        public double? GapToPlayer { get; set; }

        public double? GapToLeader { get; set; }
        public int? LapsToLeader { get; set; }

        public bool InPit { get; set; }
        public bool InPitLane { get; set; }
        public int PitCount { get; set; }

        public string TyreCompound { get; set; } = "";

        public override string ToString() => Position + " " + Name;
    }
}
