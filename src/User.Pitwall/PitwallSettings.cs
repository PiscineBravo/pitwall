namespace User.Pitwall
{
    /// <summary>Plugin settings, stored by SimHub next to its own configuration.</summary>
    public class PitwallSettings
    {
        /// <summary>Championship the plugin follows. Empty means the most recently edited one.</summary>
        public string ChampionshipId { get; set; } = "";

        /// <summary>Master switch for the race engineer.</summary>
        public bool EngineerEnabled { get; set; } = true;

        /// <summary>Read the engineer messages aloud.</summary>
        public bool SpeechEnabled { get; set; } = true;

        public string SpeechVoice { get; set; } = "";

        public int SpeechRate { get; set; } = 2;

        public int SpeechVolume { get; set; } = 90;

        /// <summary>Language of the radio: it or en.</summary>
        public string Language { get; set; } = "it";

        /// <summary>Master switch for race control: flags, safety cars and penalties.</summary>
        public bool RaceControlEnabled { get; set; } = true;

        /// <summary>Write the classification into the championship when a session ends.</summary>
        public bool AutoSaveResults { get; set; } = true;

        /// <summary>
        /// Tyre wear orientation. Auto works for every car tested so far; the override exists
        /// for mods that report something unusual.
        /// </summary>
        public string TyreWearMode { get; set; } = "Auto";

        /// <summary>
        /// Riconosce da solo chi guida, dal nome usato in Assetto Corsa, e gli assegna il
        /// sedile libero nella griglia del campionato.
        /// </summary>
        public bool AutoDetectDriver { get; set; } = true;

        /// <summary>Data folder override. Empty means Documents\Pitwall.</summary>
        public string DataFolder { get; set; } = "";
    }
}
