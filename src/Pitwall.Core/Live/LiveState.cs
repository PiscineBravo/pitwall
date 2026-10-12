using System;
using System.Collections.Generic;

namespace Pitwall.Live
{
    /// <summary>
    /// Everything the plugin knows right now, in the shape the manager application and the
    /// in-game companion consume. Kept deliberately flat and free of behaviour so it serialises
    /// cheaply several times a second.
    /// </summary>
    public sealed class LiveState
    {
        public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;

        public bool PluginRunning { get; set; } = true;
        public bool GameRunning { get; set; }
        public string GameName { get; set; } = "";

        // ---- What we are racing -------------------------------------------------------------

        public string ChampionshipId { get; set; } = "";
        public string ChampionshipName { get; set; } = "";
        public string RoundId { get; set; } = "";
        public string RoundName { get; set; } = "";
        public string CircuitName { get; set; } = "";
        public string SessionKind { get; set; } = "";
        public string SessionLabel { get; set; } = "";

        /// <summary>True when the plugin recognised the track and matched it to the calendar.</summary>
        public bool RoundMatched { get; set; }

        // ---- Session ----------------------------------------------------------------------

        public int LapCurrent { get; set; }
        public int LapsTotal { get; set; }
        public int LapsRemaining { get; set; }
        public double SessionTimeLeftSeconds { get; set; }
        public double SessionElapsedSeconds { get; set; }

        // ---- Player -------------------------------------------------------------------------

        public string PlayerName { get; set; } = "";

        /// <summary>La scuderia del sedile che occupi.</summary>
        public string PlayerTeam { get; set; } = "";

        /// <summary>Il pilota al cui posto stai correndo, vuoto se il sedile era gia tuo.</summary>
        public string PlayerReplaced { get; set; } = "";
        public int Position { get; set; }
        public string LastLap { get; set; } = "";
        public string BestLap { get; set; } = "";
        public double GapAheadSeconds { get; set; }
        public double GapBehindSeconds { get; set; }
        public string NameAhead { get; set; } = "";
        public string NameBehind { get; set; } = "";

        public double Fuel { get; set; }
        public double FuelLaps { get; set; }
        public string TyreCompound { get; set; } = "";
        public string TyreCompoundCode { get; set; } = "";
        public double TyreWearPercent { get; set; }
        public double TyreTemperature { get; set; }
        public double LapsToCliff { get; set; }
        public int PitStops { get; set; }
        public int StintLaps { get; set; }

        // ---- Race control ---------------------------------------------------------------------

        public string RaceControlStatus { get; set; } = "Inactive";
        public string RaceControlText { get; set; } = "-";
        public double Delta { get; set; }
        public double TargetSpeedKmh { get; set; }
        public bool PitLaneClosed { get; set; }
        public int SafetyCarPeriods { get; set; }
        public int VirtualSafetyCarPeriods { get; set; }
        public List<int> YellowSectors { get; set; } = new List<int>();

        /// <summary>Fase della procedura di safety car.</summary>
        public string SafetyCarPhase { get; set; } = "Idle";

        /// <summary>Sale a ogni pubblicazione: se smette di salire, il plugin non sta piu scrivendo.</summary>
        public long Sequence { get; set; }

        /// <summary>Fase del giro di formazione.</summary>
        public string FormationPhase { get; set; } = "Idle";

        /// <summary>Manche di qualifica in corso: Q1, Q2, Q3, vuoto fuori dalle qualifiche.</summary>
        public string QualifyingSegment { get; set; } = "";

        /// <summary>Secondi che mancano alla fine della manche.</summary>
        public double QualifyingSecondsLeft { get; set; }

        /// <summary>Quanti passano alla manche successiva.</summary>
        public int QualifyingSurviving { get; set; }

        /// <summary>Vero quando devi partire dalla corsia box invece che dalla griglia.</summary>
        public bool PitLaneStart { get; set; }

        /// <summary>Attesa dopo il verde prima del rilascio dalla corsia box.</summary>
        public double PitLaneStartDelaySeconds { get; set; } = 12;

        /// <summary>Vero quando la direzione gara ha abilitato il DRS.</summary>
        public bool DrsEnabledNow { get; set; }

        /// <summary>Perche il DRS non e concesso, da mostrare sull'HUD. Vuoto se e concesso.</summary>
        public string DrsReason { get; set; } = "";

        /// <summary>
        /// Distacco massimo al punto di rilevamento perche il DRS sia concesso, in secondi.
        /// Zero vuol dire nessuna regola di distacco: in zona il DRS e libero.
        /// </summary>
        public double DrsDetectionGapSeconds { get; set; }

        /// <summary>Vero quando le regole del DRS valgono anche per il giocatore.</summary>
        public bool DrsAppliesToPlayer { get; set; } = true;

        /// <summary>La lingua in uso: "it" o "en". La leggono le finestre dentro il gioco.</summary>
        public string Lingua { get; set; } = "it";

        /// <summary>Margine sulla benzina delle uscite in prove e qualifica.</summary>
        public double FuelRunMargin { get; set; } = 1.15;

        /// <summary>Margine sulla benzina di gara.</summary>
        public double FuelRaceMargin { get; set; } = 1.07;

        /// <summary>Litri di riserva aggiunti sempre.</summary>
        public double FuelReserveLitres { get; set; } = 2.0;

        /// <summary>Quante luci rosse sono accese al via, da 0 a cinque.</summary>
        public int LightsOn { get; set; }

        /// <summary>Vero quando a guidare la tua vettura e il pilota automatico.</summary>
        public bool Autopilot { get; set; }

        /// <summary>La piazzola che spetta al giocatore, cioe la posizione di partenza.</summary>
        public int GridSlot { get; set; }

        /// <summary>Cosa deve fare il pilota in griglia adesso.</summary>
        public string GridNotice { get; set; } = "";

        /// <summary>Posizione da restituire dopo un sorpasso in ricognizione, zero se e in regola.</summary>
        public int PositionToGiveBack { get; set; }

        /// <summary>Secondi che restano per restituirla.</summary>
        public double GiveBackSecondsLeft { get; set; }

        /// <summary>Margine accettato nella piazzola, lungo la pista e di lato, in metri.</summary>
        public double GridToleranceAlong { get; set; } = 2.5;

        public double GridToleranceAcross { get; set; } = 1.4;

        /// <summary>
        /// Comando per le app in pista, mandato da un pulsante del volante attraverso SimHub:
        /// cambiare pagina, scegliere uno scenario. Il numero progressivo serve all'app per
        /// capire che e arrivato un comando nuovo e non rieseguire sempre l'ultimo.
        /// </summary>
        public string UiCommand { get; set; } = "";

        public int UiCommandSeq { get; set; }

        /// <summary>Comandi per l'intelligenza artificiale, una riga per vettura.</summary>
        public List<string> AiCommands { get; set; } = new List<string>();

        /// <summary>Strategia prevista per ogni avversario, per il muretto box.</summary>
        public List<string> AiStrategy { get; set; } = new List<string>();
        public double PendingPenaltySeconds { get; set; }
        public int TrackLimitStrikes { get; set; }
        public List<string> Penalties { get; set; } = new List<string>();
        public List<string> RaceControlFeed { get; set; } = new List<string>();

        // ---- Strategy and engineer ---------------------------------------------------------------

        public string StrategyAdvice { get; set; } = "";

        /// <summary>Gli scenari che il muretto mette sul tavolo, da scegliere.</summary>
        public List<StrategyChoice> StrategyOptions { get; set; } = new List<StrategyChoice>();

        /// <summary>Lettera dello scenario scelto, vuota se si segue il consiglio.</summary>
        public string SelectedStrategy { get; set; } = "";
        public int PitWindowEarliest { get; set; }
        public int PitWindowIdeal { get; set; }
        public int PitWindowLatest { get; set; }
        public string RecommendedCompound { get; set; } = "";
        public string EngineerMessage { get; set; } = "";
        public string EngineerCategory { get; set; } = "";
        public List<string> EngineerHistory { get; set; } = new List<string>();

        // ---- Weather -----------------------------------------------------------------------------

        public string WeatherNow { get; set; } = "";
        public double AirTemperature { get; set; }
        public double RoadTemperature { get; set; }
        public double Wetness { get; set; }
        public string WeatherForecast { get; set; } = "";
        public double MinutesToRain { get; set; } = -1;

        /// <summary>
        /// Lo script meteo completo della sessione. Lo legge il controller WeatherFX per
        /// pilotare davvero il tempo, e il radar ai box per mostrare cosa sta arrivando.
        /// </summary>
        public List<WeatherPoint> WeatherTimeline { get; set; } = new List<WeatherPoint>();

        /// <summary>Durata prevista della sessione in minuti, per scalare il radar.</summary>
        public double SessionLengthMinutes { get; set; }

        // ---- Live classification --------------------------------------------------------------------

        public List<LiveStandingRow> Timing { get; set; } = new List<LiveStandingRow>();

        /// <summary>Dati della vettura per il computer di garage.</summary>
        public CarTelemetry Car { get; set; } = new CarTelemetry();

        /// <summary>Championship table as it would stand if the session ended now.</summary>
        public List<LiveStandingRow> ProvisionalChampionship { get; set; } = new List<LiveStandingRow>();
    }

    /// <summary>
    /// Un punto dello script meteo: da questo minuto in poi, queste condizioni. Fra un punto e
    /// il successivo il controller interpola, cosi il tempo cambia davvero durante la sessione
    /// invece di saltare da uno stato all'altro.
    /// </summary>
    public sealed class WeatherPoint
    {
        /// <summary>Minuti dall'inizio della sessione.</summary>
        public double Minute { get; set; }

        /// <summary>Tipo meteo di WeatherFX (ac.WeatherType).</summary>
        public int Type { get; set; }

        /// <summary>Intensita della pioggia, 0 a 1.</summary>
        public double Rain { get; set; }

        /// <summary>Acqua sulla pista, 0 a 1.</summary>
        public double Wetness { get; set; }

        public double AirTemperature { get; set; }
        public double RoadTemperature { get; set; }
        public double WindKmh { get; set; }
        public int WindDirection { get; set; }
        public double Grip { get; set; } = 1.0;
        public string Label { get; set; } = "";
    }

    /// <summary>
    /// Lo stato della vettura come lo vedrebbe un ingegnere al muretto: quello che serve alle
    /// pagine Auto, Telemetria e Analisi del computer di garage.
    /// </summary>
    public sealed class CarTelemetry
    {
        public string CarId { get; set; } = "";

        public double SpeedKmh { get; set; }
        public double Rpm { get; set; }
        public string Gear { get; set; } = "N";
        public double Throttle { get; set; }
        public double Brake { get; set; }

        public double FuelLitres { get; set; }
        public double FuelPerLap { get; set; }
        public double FuelLapsLeft { get; set; }

        public double WaterTemperature { get; set; }
        public double OilTemperature { get; set; }
        public double BrakeTemperature { get; set; }
        public double DamagePercent { get; set; }

        public double ErsPercent { get; set; }
        public int DrsAvailable { get; set; }
        public int DrsOpen { get; set; }

        /// <summary>Usura per ruota: anteriore sinistra, destra, posteriore sinistra, destra.</summary>
        public double[] TyreWear { get; set; } = new double[4];

        public double TyrePressure { get; set; }
        public double TyreTemperature { get; set; }

        /// <summary>Ultimi giri completati, dal piu recente, per il grafico del passo.</summary>
        public List<double> RecentLapSeconds { get; set; } = new List<double>();

        /// <summary>Passo medio degli ultimi giri utili, in secondi.</summary>
        public double AveragePaceSeconds { get; set; }

        /// <summary>Scarto fra il miglior giro e il passo medio: quanto stai gestendo.</summary>
        public double PaceDeltaSeconds { get; set; }
    }

    /// <summary>Uno scenario di strategia, nella forma che arriva alle schermate.</summary>
    public sealed class StrategyChoice
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Summary { get; set; } = "";
        public int Stops { get; set; }
        public int NextStopLap { get; set; }

        /// <summary>Secondi persi rispetto allo scenario piu veloce.</summary>
        public double DeltaSeconds { get; set; }

        public int ProjectedPosition { get; set; }
        public int Risk { get; set; }
        public string RiskNote { get; set; } = "";
        public bool Recommended { get; set; }
        public bool Selected { get; set; }

        /// <summary>Gli stint in forma breve, per esempio "M x14  H x22".</summary>
        public string Plan { get; set; } = "";
    }

    /// <summary>
    /// Una riga del monitor dei tempi, nella forma in cui la guarda un ingegnere: non solo il
    /// distacco dal leader, ma l'intervallo sulla vettura davanti, i settori, l'eta delle gomme
    /// e quante posizioni sono state guadagnate dal via.
    /// </summary>
    public sealed class LiveStandingRow
    {
        public int Position { get; set; }
        public string Name { get; set; } = "";
        public string Abbreviation { get; set; } = "";
        public string TeamName { get; set; } = "";
        public string Color { get; set; } = "#FFFFFF";
        public int Laps { get; set; }

        /// <summary>Distacco dal leader.</summary>
        public string Gap { get; set; } = "";

        /// <summary>Intervallo sulla vettura che precede.</summary>
        public string Interval { get; set; } = "";

        public string BestLap { get; set; } = "";
        public string LastLap { get; set; } = "";

        /// <summary>Settori dell'ultimo giro.</summary>
        public string S1 { get; set; } = "";
        public string S2 { get; set; } = "";
        public string S3 { get; set; } = "";

        /// <summary>Colore del settore: 0 normale, 1 personale, 2 miglior tempo di sessione.</summary>
        public int S1Rank { get; set; }
        public int S2Rank { get; set; }
        public int S3Rank { get; set; }

        /// <summary>Come sopra, per l'ultimo giro completo.</summary>
        public int LastLapRank { get; set; }

        public string Compound { get; set; } = "";

        /// <summary>Giri percorsi sul treno di gomme attuale.</summary>
        public int TyreAge { get; set; }

        public int PitStops { get; set; }
        public bool InPit { get; set; }
        public bool IsPlayer { get; set; }

        /// <summary>Vero quando il pilota e gia stato eliminato in Q1 o Q2.</summary>
        public bool Eliminated { get; set; }

        /// <summary>Vero quando adesso sarebbe fuori: la riga rossa delle qualifiche.</summary>
        public bool InDropZone { get; set; }

        /// <summary>Posizioni guadagnate dal via; negativo se perse.</summary>
        public int PositionChange { get; set; }

        public double Points { get; set; }
        public double PenaltySeconds { get; set; }

        public override string ToString() => Position + " " + Name;
    }

    /// <summary>A command sent from the manager, or from the in-game companion, to the plugin.</summary>
    public sealed class PitwallCommand
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public DateTime IssuedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>One of the names in <see cref="CommandNames"/>.</summary>
        public string Name { get; set; } = "";

        public string Argument { get; set; } = "";

        public double Value { get; set; }
    }

    public static class CommandNames
    {
        public const string SelectChampionship = "select-championship";
        public const string SelectRound = "select-round";
        public const string SelectSession = "select-session";
        public const string Reload = "reload";
        public const string DeployVsc = "deploy-vsc";
        public const string DeploySafetyCar = "deploy-sc";
        public const string EndNeutralisation = "end-neutralisation";
        public const string RedFlag = "red-flag";
        public const string ResumeGreen = "resume-green";
        public const string SaveResult = "save-result";
        public const string ClearPenalties = "clear-penalties";
        public const string AddPenalty = "add-penalty";
        public const string MuteEngineer = "mute-engineer";
        public const string RepeatLastMessage = "repeat-message";

        /// <summary>Sceglie uno scenario di strategia: argomento A, B, C o D.</summary>
        public const string SelectStrategy = "select-strategy";

        /// <summary>
        /// Cambia il tempo da adesso in poi. Argomento: il nome della condizione del cielo
        /// (Clear, FewClouds, Scattered, Overcast, LightRain, Rain, HeavyRain). Valore: i minuti
        /// di transizione, zero per un cambio immediato.
        /// </summary>
        public const string SetWeather = "set-weather";

        /// <summary>
        /// L'app in pista riferisce dove si trova la vettura rispetto alla sua piazzola.
        /// Argomento: "1" se e dentro, "0" se no. Valore: metri che mancano.
        /// </summary>
        public const string ReportGrid = "report-grid";

        /// <summary>
        /// Accende o spegne il pilota automatico sulla vettura del giocatore. Senza argomento fa
        /// da interruttore; con "1" o "0" imposta lo stato.
        /// </summary>
        public const string ToggleAutopilot = "autopilot";
    }
}
