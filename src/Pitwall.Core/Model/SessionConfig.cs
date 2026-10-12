using System;
using System.Collections.Generic;
using Pitwall.Weather;

using Pitwall.Lingue;

namespace Pitwall.Model
{
    /// <summary>
    /// One session of a race weekend: how long it lasts, what the regulations allow and what the
    /// sky is expected to do. The manager writes these, the SimHub plugin reads them.
    /// </summary>
    public sealed class SessionConfig
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");

        public SessionKind Kind { get; set; } = SessionKind.Practice1;

        public bool Enabled { get; set; } = true;

        public SessionLengthMode LengthMode { get; set; } = SessionLengthMode.Minutes;

        /// <summary>Laps, minutes or percent of grand prix distance, depending on <see cref="LengthMode"/>.</summary>
        public double Length { get; set; } = 30;

        /// <summary>Time of day the session starts, in the local time of the circuit.</summary>
        public TimeSpan StartTimeOfDay { get; set; } = new TimeSpan(14, 0, 0);

        /// <summary>Multiplier applied to the in-game clock, 1 is real time.</summary>
        public double TimeMultiplier { get; set; } = 1.0;

        // ---- Qualifying only -------------------------------------------------------------

        public QualifyingFormat QualifyingFormat { get; set; } = QualifyingFormat.Knockout;

        /// <summary>Minutes for Q1, Q2 and Q3 when the knockout format is used.</summary>
        public List<int> KnockoutSegmentMinutes { get; set; } = new List<int> { 18, 15, 12 };

        /// <summary>How many drivers are eliminated at the end of Q1 and Q2.</summary>
        public List<int> KnockoutEliminations { get; set; } = new List<int> { 5, 5 };

        // ---- Race only -------------------------------------------------------------------

        /// <summary>Formation lap before a standing start.</summary>
        public bool FormationLap { get; set; } = true;

        /// <summary>Standing start; false gives a rolling start behind the safety car.</summary>
        public bool StandingStart { get; set; } = true;

        /// <summary>Minimum number of pit stops. Formula racing uses one in a dry grand prix.</summary>
        public int MandatoryPitStops { get; set; }

        /// <summary>
        /// Both slick compounds must be used in a dry race. Suspended automatically once a wet
        /// tyre has been fitted, exactly like the real regulation.
        /// </summary>
        public bool TwoCompoundRule { get; set; } = true;

        public bool RefuellingAllowed { get; set; }

        /// <summary>Fuel the car starts with as a share of a full tank, 0 to 1. Zero means auto.</summary>
        public double StartingFuelFraction { get; set; }

        /// <summary>Parc ferme: setup is locked from the start of qualifying to the flag.</summary>
        public bool ParcFerme { get; set; } = true;

        // ---- Conditions ------------------------------------------------------------------

        /// <summary>
        /// Weather script for this session. Null means the weather generator will build one from
        /// the circuit climate when the weekend is created.
        /// </summary>
        public WeatherPlan Weather { get; set; }

        /// <summary>Starting track grip, 0 to 1. Green track is roughly 0.90, rubbered in is 1.00.</summary>
        public double InitialGrip { get; set; } = 0.96;

        public override string ToString() => Kind + " - " + Describe();

        /// <summary>Human readable length, for lists and overlays.</summary>
        public string Describe()
        {
            switch (LengthMode)
            {
                case SessionLengthMode.Laps:
                    return Testi.T("sessione.giri", (int)Length);
                case SessionLengthMode.Minutes:
                    return Testi.T("sessione.minuti", (int)Length);
                default:
                    return Testi.T("sessione.distanza", (int)Length);
            }
        }

        /// <summary>
        /// Resolves the configured length into an actual lap count for the given circuit.
        /// Returns zero for timed sessions, where the lap count is not known in advance.
        /// </summary>
        public int ResolveLaps(Circuit circuit)
        {
            switch (LengthMode)
            {
                case SessionLengthMode.Laps:
                    return Math.Max(1, (int)Math.Round(Length));
                case SessionLengthMode.DistancePercent:
                    return circuit == null ? 0 : circuit.LapsForDistancePercent(Length);
                default:
                    return 0;
            }
        }

        /// <summary>True for the two sessions that award championship points.</summary>
        public bool AwardsPoints => Kind == SessionKind.Race || Kind == SessionKind.Sprint;

        /// <summary>Builds the default session set for a weekend format.</summary>
        public static List<SessionConfig> DefaultsFor(WeekendFormat format)
        {
            var list = new List<SessionConfig>();
            if (format == WeekendFormat.Sprint)
            {
                list.Add(new SessionConfig { Kind = SessionKind.Practice1, LengthMode = SessionLengthMode.Minutes, Length = 60, StartTimeOfDay = new TimeSpan(13, 30, 0) });
                list.Add(new SessionConfig
                {
                    Kind = SessionKind.SprintQualifying,
                    LengthMode = SessionLengthMode.Minutes,
                    Length = 44,
                    StartTimeOfDay = new TimeSpan(17, 30, 0),
                    QualifyingFormat = QualifyingFormat.Knockout,
                    KnockoutSegmentMinutes = new List<int> { 12, 10, 8 },
                    KnockoutEliminations = new List<int> { 5, 5 }
                });
                list.Add(new SessionConfig
                {
                    Kind = SessionKind.Sprint,
                    LengthMode = SessionLengthMode.DistancePercent,
                    Length = 33,
                    StartTimeOfDay = new TimeSpan(12, 0, 0),
                    MandatoryPitStops = 0,
                    TwoCompoundRule = false,
                    FormationLap = true
                });
                list.Add(new SessionConfig { Kind = SessionKind.Qualifying, LengthMode = SessionLengthMode.Minutes, Length = 45, StartTimeOfDay = new TimeSpan(16, 0, 0) });
                list.Add(new SessionConfig
                {
                    Kind = SessionKind.Race,
                    LengthMode = SessionLengthMode.DistancePercent,
                    Length = 100,
                    StartTimeOfDay = new TimeSpan(15, 0, 0),
                    MandatoryPitStops = 1
                });
                return list;
            }

            list.Add(new SessionConfig { Kind = SessionKind.Practice1, LengthMode = SessionLengthMode.Minutes, Length = 60, StartTimeOfDay = new TimeSpan(13, 30, 0) });
            list.Add(new SessionConfig { Kind = SessionKind.Practice2, LengthMode = SessionLengthMode.Minutes, Length = 60, StartTimeOfDay = new TimeSpan(17, 0, 0) });
            list.Add(new SessionConfig { Kind = SessionKind.Practice3, LengthMode = SessionLengthMode.Minutes, Length = 60, StartTimeOfDay = new TimeSpan(12, 30, 0) });
            list.Add(new SessionConfig { Kind = SessionKind.Qualifying, LengthMode = SessionLengthMode.Minutes, Length = 45, StartTimeOfDay = new TimeSpan(16, 0, 0) });
            list.Add(new SessionConfig
            {
                Kind = SessionKind.Race,
                LengthMode = SessionLengthMode.DistancePercent,
                Length = 100,
                StartTimeOfDay = new TimeSpan(15, 0, 0),
                MandatoryPitStops = 1
            });
            return list;
        }
    }
}
