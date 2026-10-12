using System.Collections.Generic;
using Pitwall.Model;
using Pitwall.RaceControl;

namespace Pitwall.Rules
{
    /// <summary>The complete rule book of a championship.</summary>
    public sealed class ChampionshipRules
    {
        public PointsScheme Points { get; set; } = new PointsScheme();

        public TyreRules Tyres { get; set; } = new TyreRules();

        public RaceControlRules RaceControl { get; set; } = new RaceControlRules();

        public WeatherRules Weather { get; set; } = new WeatherRules();

        /// <summary>Il giro di uscita dai box in prove e qualifica.</summary>
        public OutLapRules OutLap { get; set; } = new OutLapRules();

        /// <summary>Q1, Q2, Q3: durate ed esclusioni.</summary>
        public QualifyingRules Qualifying { get; set; } = new QualifyingRules();

        /// <summary>Quando e concesso il DRS.</summary>
        public DrsRules Drs { get; set; } = new DrsRules();

        /// <summary>Quanta benzina si imbarca, in gara e nelle uscite.</summary>
        public FuelRules Fuel { get; set; } = new FuelRules();

        public EngineerRules Engineer { get; set; } = new EngineerRules();

        public AssistLevel TractionControl { get; set; } = AssistLevel.Limited;

        public AssistLevel AntiLockBrakes { get; set; } = AssistLevel.Forbidden;

        public AssistLevel AutomaticGearbox { get; set; } = AssistLevel.Forbidden;

        /// <summary>Fuel burn multiplier used in game, 1 is realistic.</summary>
        public double FuelRate { get; set; } = 1.0;

        /// <summary>Tyre wear multiplier used in game, 1 is realistic.</summary>
        public double TyreWearRate { get; set; } = 1.0;

        /// <summary>Damage multiplier used in game, 0 to 1.</summary>
        public double DamageRate { get; set; } = 1.0;
    }

    /// <summary>Tyre regulations: which compounds exist and how many sets a driver gets.</summary>
    public sealed class TyreRules
    {
        /// <summary>Slick sets available for the whole weekend.</summary>
        public int SoftSets { get; set; } = 8;

        public int MediumSets { get; set; } = 3;

        public int HardSets { get; set; } = 2;

        public int IntermediateSets { get; set; } = 4;

        public int WetSets { get; set; } = 3;

        /// <summary>
        /// Percentage of wear at which the compound falls off the cliff and lap time collapses.
        /// The engineer starts warning a few laps before this is reached.
        /// </summary>
        public double CliffWearPercent { get; set; } = 78;

        /// <summary>Wear above which the tyre is considered unusable and a stop is mandatory.</summary>
        public double CriticalWearPercent { get; set; } = 92;

        /// <summary>Let the plugin pick the compound allocation per circuit severity.</summary>
        public bool AutoCompoundSelection { get; set; } = true;
    }

    /// <summary>Everything about flags, safety cars and penalties.</summary>
    public sealed class RaceControlRules
    {
        public bool Enabled { get; set; } = true;

        public bool VirtualSafetyCarEnabled { get; set; } = true;

        public bool SafetyCarEnabled { get; set; } = true;

        /// <summary>La vettura di sicurezza vera in pista, con il suo pilota.</summary>
        public SafetyCarSettings SafetyCar { get; set; } = new SafetyCarSettings();

        /// <summary>Il giro di ricognizione e la partenza da fermo che ne segue.</summary>
        public FormationLapSettings Formation { get; set; } = new FormationLapSettings();

        public bool RedFlagEnabled { get; set; } = true;

        /// <summary>Simulation minimum before VSC ENDING; not a fixed FIA duration.</summary>
        public int VscMinimumSeconds { get; set; } = 60;

        /// <summary>Telemetry must remain clear this long before automatic withdrawal. Simulation heuristic.</summary>
        public double IncidentClearSeconds { get; set; } = 5;

        /// <summary>Minimum number of laps a full safety car stays out.</summary>
        public int SafetyCarMinimumLaps { get; set; } = 2;

        /// <summary>Simulation approximation of the FIA minimum-time delta.</summary>
        public double VscPacePercent { get; set; } = 60;

        /// <summary>Target percentage of normal pace behind the safety car.</summary>
        public double SafetyCarPacePercent { get; set; } = 55;

        /// <summary>Tolerance in seconds on the VSC delta before a warning is issued.</summary>
        public double VscDeltaToleranceSeconds { get; set; } = 0.5;

        /// <summary>Seconds of accumulated delta breach that turn a warning into a penalty.</summary>
        public double VscBreachPenaltySeconds { get; set; } = 3.0;

        /// <summary>Legacy saved setting, ignored: FIA B5.13.8 permits SC even on the final lap.</summary>
        public int NoSafetyCarInFinalLaps { get; set; } = 0;

        // ---- Track limits and penalties ---------------------------------------------------

        public bool TrackLimitsEnabled { get; set; } = true;

        /// <summary>Track limits strikes allowed before the first time penalty.</summary>
        public int TrackLimitsWarnings { get; set; } = 3;

        /// <summary>Seconds added for every strike beyond the allowance.</summary>
        public double TrackLimitsPenaltySeconds { get; set; } = 5;

        public bool PitLaneSpeedingEnabled { get; set; } = true;

        public double PitLaneSpeedLimitKmh { get; set; } = 80;

        public double PitLaneSpeedingPenaltySeconds { get; set; } = 5;

        public bool JumpStartEnabled { get; set; } = true;

        public double JumpStartPenaltySeconds { get; set; } = 5;

        /// <summary>Minimum stationary time in the pit box, in seconds. Below it, unsafe release.</summary>
        public double MinimumPitStopSeconds { get; set; } = 2.0;

        /// <summary>Convert time penalties into a drive-through above this total.</summary>
        public double DriveThroughThresholdSeconds { get; set; } = 20;

        /// <summary>Licence points added for a serious infringement.</summary>
        public int LicencePointsPerSeriousOffence { get; set; } = 2;
    }

    /// <summary>How the weather generator behaves for this championship.</summary>
    /// <summary>
    /// Le qualifiche a eliminazione. I minuti sommano a quarantacinque, la stessa durata di una
    /// qualifica singola: cosi il preset per Content Manager non cambia.
    /// </summary>
    public sealed class QualifyingRules
    {
        /// <summary>Spegnendolo si torna a una qualifica unica col miglior tempo.</summary>
        public bool Enabled { get; set; } = true;

        public int Q1Minutes { get; set; } = 18;

        public int Q2Minutes { get; set; } = 15;

        public int Q3Minutes { get; set; } = 12;

        /// <summary>Quanti vengono esclusi alla fine della Q1.</summary>
        public int Q1Eliminations { get; set; } = 5;

        /// <summary>Quanti vengono esclusi alla fine della Q2.</summary>
        public int Q2Eliminations { get; set; } = 5;

        /// <summary>Pausa fra una manche e l'altra, in minuti.</summary>
        public double BreakMinutes { get; set; }
    }

    /// <summary>
    /// Il giro di uscita dai box in prove e qualifica. I valori di partenza sono quelli che in
    /// Formula 1 si vedono davvero: un giro di preparazione sui 170, l'ultimo tratto piu veloce
    /// per arrivare lanciati, poi il giro buono senza limiti.
    /// </summary>
    public sealed class OutLapRules
    {
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// Velocita del giro di uscita, in km/h. Centosettanta era troppo poco: la vettura
        /// restava sulle marce basse e la gomma arrivava fredda al giro buono, che e
        /// esattamente quello che questo giro dovrebbe evitare.
        /// </summary>
        public double OutLapSpeedKmh { get; set; } = 205;

        public double BuildUpSpeedKmh { get; set; } = 275;

        public double InLapSpeedKmh { get; set; } = 200;

        /// <summary>Di quanto ci si sposta dalla traiettoria mentre si scalda, in metri.</summary>
        public double OffLineMeters { get; set; } = 2.6;

        /// <summary>Quanti giri lanciati di fila prima di rientrare.</summary>
        public int FlyingLapsPerRun { get; set; } = 1;

        /// <summary>
        /// Programma delle uscite: ogni pilota ha i suoi turni e fra un turno e l'altro resta
        /// ai box. Senza questo girano tutti per tutta la sessione, che in pista non succede
        /// mai e rende il traffico impossibile da leggere.
        /// </summary>
        public bool RunPlans { get; set; } = true;
    }

    /// <summary>
    /// Le regole del DRS.
    ///
    /// Assetto Corsa da se non ne applica nessuna: dentro le zone della pista l'ala si apre
    /// sempre, da subito e senza guardare chi hai davanti. In Formula 1 invece il DRS e
    /// bloccato fino all'inizio del terzo giro, e poi concesso solo a chi al punto di
    /// rilevamento e entro un secondo da chi lo precede.
    ///
    /// Quello che non si puo fare, e non si vuole fare, e aprire l'ala fuori dalle zone: le
    /// zone restano quelle del tracciato, e noi possiamo solo togliere il permesso, non darlo.
    /// </summary>
    public sealed class DrsRules
    {
        public bool Enabled { get; set; } = true;

        /// <summary>Giro dal quale il DRS e concesso in gara. Tre, come in Formula 1.</summary>
        public int EnabledFromLap { get; set; } = 3;

        /// <summary>
        /// Distacco massimo al punto di rilevamento, in secondi. Zero toglie la regola e
        /// lascia il DRS libero dentro le zone.
        /// </summary>
        public double DetectionGapSeconds { get; set; } = 1.0;

        /// <summary>
        /// In prove e qualifica il DRS in zona e libero per regolamento, ma un pilota non lo
        /// usa mentre scalda le gomme o rientra: lo apre nel giro buono. Questo lo fa fare
        /// anche alle IA.
        /// </summary>
        public bool OnlyOnFlyingLap { get; set; } = true;

        /// <summary>Il DRS resta chiuso sotto safety car e bandiere gialle.</summary>
        public bool SuspendUnderYellow { get; set; } = true;

        /// <summary>
        /// Le regole valgono anche per la vettura del giocatore. Lasciarle solo alle IA
        /// vorrebbe dire correre un campionato con il DRS libero contro avversari che lo
        /// hanno bloccato: toglierlo e una scelta, ma il valore giusto e vero.
        /// </summary>
        public bool AppliesToPlayer { get; set; } = true;
    }

    /// <summary>
    /// Quanta benzina si imbarca.
    ///
    /// Il difetto da correggere e che le IA girano sempre col pieno: in prove e qualifica e
    /// un peso che non ha senso - nessuno cerca il tempo con centodieci chili a bordo - e in
    /// gara le rende lente nei primi giri. Qui si decide per quanti giri vanno rifornite; i
    /// litri li calcola l'app in pista, che e l'unica a conoscere consumo e serbatoio di
    /// ogni vettura.
    /// </summary>
    public sealed class FuelRules
    {
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// Margine sulla benzina di gara. 1.07 vuol dire sette per cento oltre il calcolo:
        /// serve per la safety car, i giri lenti e un consumo peggiore del previsto. Restare
        /// a secco all'ultimo giro e molto peggio che portare due chili di troppo.
        /// </summary>
        public double RaceMargin { get; set; } = 1.07;

        /// <summary>Margine nelle uscite di prove e qualifica, dove si gira leggeri.</summary>
        public double RunMargin { get; set; } = 1.15;

        /// <summary>Litri di riserva aggiunti sempre, oltre al margine.</summary>
        public double ReserveLitres { get; set; } = 2.0;
    }

    public sealed class WeatherRules
    {
        public bool DynamicWeather { get; set; } = true;

        /// <summary>Global multiplier on each circuit rain probability, 0 disables rain entirely.</summary>
        public double RainMultiplier { get; set; } = 1.0;

        /// <summary>How quickly conditions change; higher means more transitions per session.</summary>
        public double Volatility { get; set; } = 1.0;

        /// <summary>Minutes of forecast the engineer is allowed to read out.</summary>
        public int ForecastHorizonMinutes { get; set; } = 30;

        /// <summary>Track temperature offset over ambient in dry conditions, in Celsius.</summary>
        public double DryTrackTempOffset { get; set; } = 12;
    }

    /// <summary>Race engineer behaviour.</summary>
    public sealed class EngineerRules
    {
        public bool Enabled { get; set; } = true;

        /// <summary>Language of the radio messages: it or en.</summary>
        public string Language { get; set; } = "it";

        /// <summary>Read messages aloud through the SimHub text-to-speech engine.</summary>
        public bool Speak { get; set; } = true;

        /// <summary>Minimum seconds between two spoken messages, so the radio is not a machine gun.</summary>
        public double MinimumSecondsBetweenMessages { get; set; } = 6;

        /// <summary>Chattiness, 0 quiet to 2 talkative.</summary>
        public int Verbosity { get; set; } = 1;

        /// <summary>Categories the engineer is allowed to talk about.</summary>
        public HashSet<string> MutedCategories { get; set; } = new HashSet<string>();
    }
}
