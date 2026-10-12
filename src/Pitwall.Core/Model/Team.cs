using System;
using System.Collections.Generic;

namespace Pitwall.Model
{
    /// <summary>A constructor. Holds the car used in Assetto Corsa and the livery colours.</summary>
    public sealed class Team
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");

        public string Name { get; set; } = "";

        /// <summary>Short tag used on timing screens, for example FER.</summary>
        public string ShortName { get; set; } = "";

        /// <summary>Hex colour used in the UI and on the wheel display.</summary>
        public string Color { get; set; } = "#FFFFFF";

        /// <summary>Assetto Corsa car folder name, for example rss_formula_hybrid_2022.</summary>
        public string AcCarId { get; set; } = "";

        /// <summary>Optional skin folders for the two cars of the team, index 0 and 1.</summary>
        public List<string> AcSkins { get; set; } = new List<string>();

        public string PowerUnit { get; set; } = "";

        /// <summary>Points carried over when the championship starts mid-season.</summary>
        public double PointsAdjustment { get; set; }

        public override string ToString() => Name;
    }
}
