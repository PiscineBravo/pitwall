using System;
using System.Collections.Generic;
using System.Linq;

namespace Pitwall.Rules
{
    /// <summary>
    /// How points are awarded. Defaults follow the current Formula 1 regulations, including the
    /// tiered scale used when a race is stopped before three quarters distance.
    /// </summary>
    public sealed class PointsScheme
    {
        public string Name { get; set; } = "F1 moderno";

        /// <summary>Points for positions 1, 2, 3 ... in a full-length grand prix.</summary>
        public List<int> Race { get; set; } = new List<int> { 25, 18, 15, 12, 10, 8, 6, 4, 2, 1 };

        /// <summary>Points for positions 1, 2, 3 ... in a sprint.</summary>
        public List<int> Sprint { get; set; } = new List<int> { 8, 7, 6, 5, 4, 3, 2, 1 };

        /// <summary>Bonus point for the fastest lap of the race.</summary>
        public int FastestLap { get; set; }

        /// <summary>The fastest lap bonus only counts inside this finishing position.</summary>
        public int FastestLapMaxPosition { get; set; } = 10;

        /// <summary>Bonus for pole position. Zero in Formula 1, non-zero in some club series.</summary>
        public int PolePosition { get; set; }

        /// <summary>Apply the reduced-points scale when a race is cut short.</summary>
        public bool UseShortenedRaceScale { get; set; } = true;

        /// <summary>Both cars of a constructor score, as in Formula 1.</summary>
        public bool AllDriversScoreForTeam { get; set; } = true;

        /// <summary>Drop the worst N results of each driver from the final tally. Zero disables it.</summary>
        public int DroppedScores { get; set; }

        /// <summary>Points for a given finishing position in a full-length race.</summary>
        public int RacePointsFor(int position)
        {
            if (position < 1 || position > Race.Count) return 0;
            return Race[position - 1];
        }

        public int SprintPointsFor(int position)
        {
            if (position < 1 || position > Sprint.Count) return 0;
            return Sprint[position - 1];
        }

        /// <summary>
        /// Points for a race that did not reach full distance. Mirrors the FIA scale introduced
        /// in 2022: nothing below two laps, then three reduced tables, then the full table from
        /// three quarters distance onwards.
        /// </summary>
        public int RacePointsFor(int position, double completedFraction, int lapsCompleted)
        {
            if (!UseShortenedRaceScale) return RacePointsFor(position);
            if (lapsCompleted < 2) return 0;
            if (completedFraction >= 0.75) return RacePointsFor(position);

            List<int> scale;
            if (completedFraction < 0.25) scale = new List<int> { 6, 4, 3, 2, 1 };
            else if (completedFraction < 0.50) scale = new List<int> { 13, 10, 8, 6, 5, 4, 3, 2, 1 };
            else scale = new List<int> { 19, 14, 12, 9, 8, 6, 5, 3, 2, 1 };

            if (position < 1 || position > scale.Count) return 0;
            return scale[position - 1];
        }

        /// <summary>A few ready-made schemes offered in the manager.</summary>
        public static IEnumerable<PointsScheme> Presets()
        {
            yield return new PointsScheme();
            yield return new PointsScheme
            {
                Name = "F1 con punto giro veloce",
                FastestLap = 1
            };
            yield return new PointsScheme
            {
                Name = "F1 anni 90",
                Race = new List<int> { 10, 6, 4, 3, 2, 1 },
                Sprint = new List<int>(),
                UseShortenedRaceScale = false
            };
            yield return new PointsScheme
            {
                Name = "Top 15",
                Race = new List<int> { 30, 24, 20, 17, 15, 13, 11, 9, 7, 6, 5, 4, 3, 2, 1 },
                FastestLap = 1,
                PolePosition = 1
            };
        }

        public PointsScheme Clone()
        {
            return new PointsScheme
            {
                Name = Name,
                Race = Race.ToList(),
                Sprint = Sprint.ToList(),
                FastestLap = FastestLap,
                FastestLapMaxPosition = FastestLapMaxPosition,
                PolePosition = PolePosition,
                UseShortenedRaceScale = UseShortenedRaceScale,
                AllDriversScoreForTeam = AllDriversScoreForTeam,
                DroppedScores = DroppedScores
            };
        }

        public override string ToString() => Name;
    }
}
