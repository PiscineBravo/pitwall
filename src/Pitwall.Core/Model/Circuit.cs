using System;
using System.Collections.Generic;

namespace Pitwall.Model
{
    /// <summary>
    /// A track in the championship library. Everything the strategy and race-control code needs
    /// to reason about a circuit, without having to read the Assetto Corsa content folder.
    /// </summary>
    public sealed class Circuit
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");

        public string Name { get; set; } = "";

        public string Country { get; set; } = "";

        /// <summary>Assetto Corsa track folder, for example ks_monza.</summary>
        public string AcTrackId { get; set; } = "";

        /// <summary>Assetto Corsa layout folder, empty when the track has a single layout.</summary>
        public string AcTrackConfig { get; set; } = "";

        public double LengthMeters { get; set; }

        public int Sectors { get; set; } = 3;

        /// <summary>Laps that make up a full-distance grand prix, roughly 305 km.</summary>
        public int GrandPrixLaps { get; set; }

        /// <summary>Seconds lost driving through the pit lane, excluding the stationary time.</summary>
        public double PitLaneLossSeconds { get; set; } = 20.0;

        /// <summary>Typical stationary time for a tyre change, in seconds.</summary>
        public double PitStopStationarySeconds { get; set; } = 2.6;

        /// <summary>Number of DRS activation zones.</summary>
        public int DrsZones { get; set; } = 2;

        /// <summary>How hard the surface is on tyres, 1 gentle to 5 brutal. Drives degradation.</summary>
        public int TyreSeverity { get; set; } = 3;

        /// <summary>How hard it is to pass here, 1 easy to 5 processional. Used by strategy advice.</summary>
        public int OvertakingDifficulty { get; set; } = 3;

        /// <summary>Legacy circuit metadata; incident deployment no longer uses random probability.</summary>
        public double SafetyCarProbability { get; set; } = 0.35;

        /// <summary>Chance of rain during a weekend at this venue, 0 to 1. Seeds the weather generator.</summary>
        public double RainProbability { get; set; } = 0.15;

        /// <summary>Typical ambient temperature in Celsius at the time of year it is raced.</summary>
        public double TypicalAirTemperature { get; set; } = 24;

        /// <summary>Reference lap in seconds, used to sanity-check telemetry and size pit windows.</summary>
        public double ReferenceLapSeconds { get; set; }

        /// <summary>Free-form notes shown in the manager.</summary>
        public string Notes { get; set; } = "";

        /// <summary>Track identifier as SimHub reports it: trackid, or trackid-layout.</summary>
        public string TelemetryKey =>
            string.IsNullOrWhiteSpace(AcTrackConfig) ? AcTrackId : AcTrackId + "-" + AcTrackConfig;

        /// <summary>Laps needed to cover the requested share of a full grand prix, at least one.</summary>
        public int LapsForDistancePercent(double percent)
        {
            if (GrandPrixLaps <= 0) return 1;
            int laps = (int)Math.Round(GrandPrixLaps * percent / 100.0, MidpointRounding.AwayFromZero);
            return Math.Max(1, laps);
        }

        public override string ToString() => Name;
    }

    /// <summary>The shipped circuit library plus anything the user added.</summary>
    public sealed class CircuitLibrary
    {
        public List<Circuit> Circuits { get; set; } = new List<Circuit>();
    }
}
