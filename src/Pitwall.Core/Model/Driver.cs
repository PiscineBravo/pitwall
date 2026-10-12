using System;
using System.Globalization;
using Newtonsoft.Json;

namespace Pitwall.Model
{
    /// <summary>
    /// A championship entrant. One of them is normally the human player; the others are
    /// Assetto Corsa AI drivers, matched back to telemetry through <see cref="AcName"/>.
    /// </summary>
    public sealed class Driver
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");

        public string FirstName { get; set; } = "";

        public string LastName { get; set; } = "";

        /// <summary>Broadcast three-letter code, for example LEC.</summary>
        public string Abbreviation { get; set; } = "";

        /// <summary>Permanent racing number.</summary>
        public int Number { get; set; }

        public string Nationality { get; set; } = "";

        public string TeamId { get; set; } = "";

        /// <summary>True for the driver the human being actually races.</summary>
        public bool IsPlayer { get; set; }

        /// <summary>
        /// Name exactly as it appears in the Assetto Corsa entry list. This is the key used to
        /// match a SimHub opponent back to a championship driver, so it has to match character
        /// for character. Falls back to FirstName + LastName when empty.
        /// </summary>
        public string AcName { get; set; } = "";

        /// <summary>Assetto Corsa AI strength, 70 to 100.</summary>
        public int AiSkill { get; set; } = 95;

        /// <summary>Assetto Corsa AI aggression, 0 to 100.</summary>
        public int AiAggression { get; set; } = 50;

        /// <summary>Quello che si legge quando un valore IA non si applica a questo sedile.</summary>
        public const string NotApplicable = "-";

        /// <summary>
        /// Abilita IA come va mostrata in tabella. Sul sedile del giocatore non vuol dire niente
        /// - al volante ci sei tu - quindi resta un trattino, ma il valore salvato non si perde:
        /// se il sedile torna all'IA si riprende il suo.
        /// </summary>
        [JsonIgnore]
        public string AiSkillText
        {
            get { return IsPlayer ? NotApplicable : AiSkill.ToString(CultureInfo.InvariantCulture); }
            set { AiSkill = ParseLevel(value, AiSkill, 70, 100); }
        }

        /// <summary>Aggressivita IA come va mostrata in tabella, vedi <see cref="AiSkillText"/>.</summary>
        [JsonIgnore]
        public string AiAggressionText
        {
            get { return IsPlayer ? NotApplicable : AiAggression.ToString(CultureInfo.InvariantCulture); }
            set { AiAggression = ParseLevel(value, AiAggression, 0, 100); }
        }

        /// <summary>
        /// Legge un livello IA digitato a mano. Se non e un numero - per esempio il trattino del
        /// sedile del giocatore - tiene quello di prima invece di azzerarlo.
        /// </summary>
        private static int ParseLevel(string text, int current, int min, int max)
        {
            int parsed;
            if (!int.TryParse((text ?? "").Trim(), NumberStyles.Integer,
                              CultureInfo.InvariantCulture, out parsed)) return current;
            return parsed < min ? min : parsed > max ? max : parsed;
        }

        /// <summary>Points carried over when the championship starts mid-season.</summary>
        public double PointsAdjustment { get; set; }

        /// <summary>Penalty points on the licence; 12 inside a season means a race ban.</summary>
        public int LicencePoints { get; set; }

        /// <summary>
        /// Nome del pilota al cui posto si e subentrati, vuoto se il sedile e sempre stato
        /// questo. Pitwall lo compila da solo quando riconosce che sei tu a guidare quella
        /// vettura, e serve a ricordare di chi era il sedile.
        /// </summary>
        public string ReplacedName { get; set; } = "";

        /// <summary>Vero quando questo sedile e stato assegnato automaticamente al giocatore.</summary>
        public bool SeatTakenOver => !string.IsNullOrEmpty(ReplacedName);

        public string FullName => (FirstName + " " + LastName).Trim();

        /// <summary>The name to look for in telemetry.</summary>
        public string MatchName => string.IsNullOrWhiteSpace(AcName) ? FullName : AcName;

        public override string ToString() => FullName;
    }
}
