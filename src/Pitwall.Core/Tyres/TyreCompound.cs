using System;
using System.Collections.Generic;
using System.Linq;

namespace Pitwall.Tyres
{
    /// <summary>The five compounds a Formula weekend works with.</summary>
    public enum CompoundKind
    {
        Soft,
        Medium,
        Hard,
        Intermediate,
        Wet
    }

    /// <summary>
    /// Performance profile of a compound. Values are relative to the medium, which is the
    /// reference: the soft is quicker but wears faster, the hard is the opposite.
    /// </summary>
    public sealed class CompoundSpec
    {
        public CompoundKind Kind { get; set; }

        public string Name { get; set; } = "";

        /// <summary>Single letter shown on the wheel display: S, M, H, I, W.</summary>
        public string Code { get; set; } = "";

        public string Color { get; set; } = "#FFFFFF";

        /// <summary>Lap time offset against the medium, in seconds. Negative is quicker.</summary>
        public double LapDeltaSeconds { get; set; }

        /// <summary>Wear percentage consumed per lap on a medium-severity circuit.</summary>
        public double WearPerLapPercent { get; set; } = 2.0;

        /// <summary>Laps needed to bring the tyre into its working window.</summary>
        public double WarmupLaps { get; set; } = 1.0;

        /// <summary>Ideal surface temperature range in Celsius.</summary>
        public double OptimalTempMin { get; set; } = 90;

        public double OptimalTempMax { get; set; } = 110;

        /// <summary>Seconds lost per lap once past the cliff, per extra percent of wear.</summary>
        public double CliffPenaltyPerPercent { get; set; } = 0.12;

        /// <summary>True for intermediates and full wets.</summary>
        public bool IsWetWeather => Kind == CompoundKind.Intermediate || Kind == CompoundKind.Wet;

        /// <summary>Minimum amount of standing water this tyre wants, 0 to 1.</summary>
        public double MinWetness { get; set; }

        /// <summary>Above this wetness the tyre is out of its element.</summary>
        public double MaxWetness { get; set; } = 0.15;

        public override string ToString() => Name;
    }

    /// <summary>The compound catalogue plus the per-circuit allocation logic.</summary>
    public static class Compounds
    {
        public static readonly CompoundSpec Soft = new CompoundSpec
        {
            Kind = CompoundKind.Soft,
            Name = "Soft",
            Code = "S",
            Color = "#E10600",
            LapDeltaSeconds = -0.7,
            WearPerLapPercent = 3.0,
            WarmupLaps = 0.5,
            OptimalTempMin = 85,
            OptimalTempMax = 105,
            MaxWetness = 0.10
        };

        public static readonly CompoundSpec Medium = new CompoundSpec
        {
            Kind = CompoundKind.Medium,
            Name = "Medium",
            Code = "M",
            Color = "#FFD500",
            LapDeltaSeconds = 0.0,
            WearPerLapPercent = 2.1,
            WarmupLaps = 1.0,
            OptimalTempMin = 90,
            OptimalTempMax = 110,
            MaxWetness = 0.10
        };

        public static readonly CompoundSpec Hard = new CompoundSpec
        {
            Kind = CompoundKind.Hard,
            Name = "Hard",
            Code = "H",
            Color = "#F0F0F0",
            LapDeltaSeconds = 0.6,
            WearPerLapPercent = 1.5,
            WarmupLaps = 2.0,
            OptimalTempMin = 95,
            OptimalTempMax = 120,
            MaxWetness = 0.08
        };

        public static readonly CompoundSpec Intermediate = new CompoundSpec
        {
            Kind = CompoundKind.Intermediate,
            Name = "Intermediate",
            Code = "I",
            Color = "#43B02A",
            LapDeltaSeconds = 4.5,
            WearPerLapPercent = 2.4,
            WarmupLaps = 1.0,
            OptimalTempMin = 55,
            OptimalTempMax = 80,
            MinWetness = 0.08,
            MaxWetness = 0.55
        };

        public static readonly CompoundSpec Wet = new CompoundSpec
        {
            Kind = CompoundKind.Wet,
            Name = "Full Wet",
            Code = "W",
            Color = "#0067AD",
            LapDeltaSeconds = 9.0,
            WearPerLapPercent = 1.9,
            WarmupLaps = 1.0,
            OptimalTempMin = 45,
            OptimalTempMax = 70,
            MinWetness = 0.40,
            MaxWetness = 1.0
        };

        public static readonly IReadOnlyList<CompoundSpec> All =
            new List<CompoundSpec> { Soft, Medium, Hard, Intermediate, Wet };

        public static CompoundSpec Get(CompoundKind kind) => All.First(c => c.Kind == kind);

        /// <summary>
        /// Maps whatever the game calls the tyre onto a compound. Assetto Corsa mods use a
        /// wide variety of names, so this matches on the usual substrings.
        /// </summary>
        public static CompoundSpec FromGameName(string gameName)
        {
            if (string.IsNullOrWhiteSpace(gameName)) return Medium;
            string s = gameName.Trim().ToLowerInvariant();

            if (s.Contains("wet") || s.Contains("rain") || s == "w") return Wet;
            if (s.Contains("inter") || s == "i") return Intermediate;
            if (s.Contains("super") && s.Contains("soft")) return Soft;
            if (s.Contains("ultra") || s.Contains("hyper")) return Soft;
            if (s.Contains("soft") || s == "s" || s.Contains("sm")) return Soft;
            if (s.Contains("medium") || s == "m") return Medium;
            if (s.Contains("hard") || s == "h") return Hard;

            // Pirelli style C1..C5: C1 is the hardest, C5 the softest.
            if (s.StartsWith("c") && s.Length >= 2 && char.IsDigit(s[1]))
            {
                int n = s[1] - '0';
                if (n <= 2) return Hard;
                if (n == 3) return Medium;
                return Soft;
            }

            return Medium;
        }

        /// <summary>
        /// The right tyre for the current amount of water on track, 0 dry to 1 flooded.
        /// This is what the engineer uses when it tells you to come in for inters.
        /// </summary>
        public static CompoundSpec ForConditions(double wetness, CompoundSpec preferredSlick = null)
        {
            if (wetness >= 0.40) return Wet;
            if (wetness >= 0.10) return Intermediate;
            return preferredSlick ?? Medium;
        }
    }
}
