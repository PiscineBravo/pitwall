using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Controls;
using System.Windows.Media;
using GameReaderCommon;
using Pitwall.Engineer;
using Pitwall.Live;
using Pitwall.RaceControl;
using Pitwall.Storage;
using SimHub.Plugins;
using User.Pitwall.Runtime;
using User.Pitwall.Ui;

namespace User.Pitwall
{
    /// <summary>
    /// The SimHub side of Pitwall. It does three things and nothing else: turn SimHub telemetry
    /// into a Pitwall frame, hand it to the session engine, and publish the results as SimHub
    /// properties, events and actions so they can be bound to a dashboard or sent to the wheel.
    /// </summary>
    [PluginDescription("Campionato in stile Formula 1 per Assetto Corsa: gestione gomme, ingegnere di pista, safety car, VSC, meteo e classifiche.")]
    [PluginAuthor("Pitwall")]
    [PluginName("Pitwall")]
    public class PitwallPlugin : IPlugin, IDataPlugin, IWPFSettingsV2
    {
        private readonly FrameBuilder _frames = new FrameBuilder();
        private readonly RadioVoice _voice = new RadioVoice();

        /// <summary>Fino a quando il valore LightsOut resta vero dopo il verde.</summary>
        private DateTime _lightsOutUntil = DateTime.MinValue;

        public PitwallSettings Settings { get; private set; } = new PitwallSettings();

        public SessionEngine Engine { get; } = new SessionEngine();

        public PluginManager PluginManager { get; set; }

        public string LeftMenuTitle => "Pitwall";

        /// <summary>
        /// Il marchio di Pitwall, disegnato nel codice.
        ///
        /// E lo stesso segno del Manager e dell'app in pista - il muretto dei box con gli
        /// schermi della squadra sopra - ridisegnato qui con le primitive di SimHub invece che
        /// caricato da un file: un plugin che porta con se delle immagini e un plugin che si
        /// puo installare male, e chi lo compra deve solo copiare un file.
        ///
        /// Le misure sono quelle di brand/pitwall.svg divise per due: quella griglia e 64,
        /// questa icona e 32.
        /// </summary>
        public ImageSource PictureIcon
        {
            get
            {
                var group = new DrawingGroup();

                var chiaro = new SolidColorBrush(Color.FromRgb(0xE8, 0xEC, 0xF2));
                var spento = new SolidColorBrush(Color.FromRgb(0x93, 0xA0, 0xB0));
                var rosso = new SolidColorBrush(Color.FromRgb(0xE1, 0x06, 0x00));

                void Blocco(Brush colore, double x, double y, double w, double h) =>
                    group.Children.Add(new GeometryDrawing(colore, null,
                        new RectangleGeometry(new System.Windows.Rect(x, y, w, h), 0.8, 0.8)));

                // Gli schermi della squadra, di altezza diversa.
                Blocco(chiaro, 7, 12, 6.5, 5);
                Blocco(chiaro, 14.5, 9.5, 6.5, 7.5);
                Blocco(spento, 22, 13.5, 4.5, 3.5);

                // Il muretto, piu largo degli schermi: cosi si legge che ci stanno sopra.
                Blocco(rosso, 4, 17, 24, 4);

                var image = new DrawingImage(group);
                image.Freeze();
                return image;
            }
        }

        // ---- Lifecycle -------------------------------------------------------------------------

        public void Init(PluginManager pluginManager)
        {
            SimHub.Logging.Current.Info("Pitwall: avvio plugin");

            Settings = this.ReadCommonSettings("GeneralSettings", () => new PitwallSettings());

            // La lingua la sceglie il Manager e la scrive in un file: il plugin la legge e basta.
            // Un posto solo da cambiare, e i tre pezzi non possono finire disallineati.
            global::Pitwall.Lingue.Testi.Applica(global::Pitwall.Model.Impostazioni.Correnti);

            if (!string.IsNullOrWhiteSpace(Settings.DataFolder))
                PitwallPaths.UseRoot(Settings.DataFolder);
            PitwallPaths.EnsureFolders();

            ApplySettings();

            Engine.Log += message => SimHub.Logging.Current.Info("Pitwall: " + message);
            Engine.EngineerSpoke += OnEngineerSpoke;
            Engine.SessionCompleted += result =>
            {
                this.TriggerEvent("SessionCompleted");
                SimHub.Logging.Current.Info("Pitwall: risultato registrato per " + result.Kind);
            };

            LoadChampionship();
            RegisterProperties();
            RegisterEventsAndActions();
        }

        public void End(PluginManager pluginManager)
        {
            try
            {
                Engine.FinishSession(null);
                Engine.SaveChampionship();
            }
            catch (Exception ex)
            {
                SimHub.Logging.Current.Warn("Pitwall: errore in chiusura - " + ex.Message);
            }

            _voice.Dispose();
            this.SaveCommonSettings("GeneralSettings", Settings);
        }

        public Control GetWPFSettingsControl(PluginManager pluginManager) => new SettingsControl(this);

        /// <summary>Re-reads the settings into the running engine. Called by the settings screen.</summary>
        public void ApplySettings()
        {
            _voice.Enabled = Settings.SpeechEnabled;
            _voice.PreferredVoice = Settings.SpeechVoice;
            _voice.Rate = Settings.SpeechRate;
            _voice.Volume = Settings.SpeechVolume;
            _voice.Language = Settings.Language;
            _voice.Rebuild();

            Engine.AutoAssignPlayerSeat = Settings.AutoDetectDriver;
            Engine.Engineer.Rules.Enabled = Settings.EngineerEnabled;
            Engine.Engineer.Rules.Language = Settings.Language;
            Engine.Engineer.Rules.Speak = Settings.SpeechEnabled;
            Engine.Engineer.Configure(Engine.Engineer.Rules);

            if (Engine.Championship != null)
                Engine.Championship.Rules.RaceControl.Enabled = Settings.RaceControlEnabled;

            switch ((Settings.TyreWearMode ?? "Auto").ToLowerInvariant())
            {
                case "usura":
                case "wear":
                    _frames.WearIsRemainingLife = false;
                    break;
                case "vita residua":
                case "remaining":
                    _frames.WearIsRemainingLife = true;
                    break;
                default:
                    _frames.WearIsRemainingLife = null;
                    break;
            }
        }

        /// <summary>
        /// Carica il campionato configurato, o il piu recente.
        ///
        /// Il plugin non ne crea mai uno: un campionato e un documento dell'utente, con dentro
        /// calendario, piloti e risultati, e nasce nel gestionale. Se non ce ne sono, il plugin
        /// resta inerte e lo scrive nel pannello, invece di riempire la cartella dei dati di
        /// campionati che nessuno ha chiesto.
        /// </summary>
        public void LoadChampionship()
        {
            bool loaded = !string.IsNullOrWhiteSpace(Settings.ChampionshipId)
                          && Engine.LoadChampionship(Settings.ChampionshipId);

            if (!loaded) loaded = Engine.LoadMostRecent();

            if (loaded && Engine.Championship != null)
            {
                Settings.ChampionshipId = Engine.Championship.Id;
            }
            else
            {
                Engine.SetChampionship(null);
                Settings.ChampionshipId = "";
                SimHub.Logging.Current.Info(
                    "Pitwall: nessun campionato in " + PitwallPaths.Championships +
                    ". Creane uno con Pitwall Manager.");
            }

            ApplySettings();
        }

        // ---- Telemetry ---------------------------------------------------------------------------

        public void DataUpdate(PluginManager pluginManager, ref GameData data)
        {
            try
            {
                // Pitwall only understands Assetto Corsa telemetry; other games simply idle.
                var frame = _frames.Build(data);
                Engine.ProcessFrame(frame);
            }
            catch (Exception ex)
            {
                SimHub.Logging.Current.Warn("Pitwall: errore nel ciclo dati - " + ex.Message);
            }
        }

        private void OnEngineerSpoke(EngineerMessage message)
        {
            this.TriggerEvent("EngineerMessage");
            if (Settings.SpeechEnabled)
                _voice.Speak(message.Text, message.Priority >= 3);
        }

        // ---- SimHub surface -------------------------------------------------------------------------

        private void RegisterProperties()
        {
            var s = Engine.State;

            Section("Sessione");
            Attach("Championship", () => s.ChampionshipName, "Nome del campionato seguito dal plugin");
            Attach("Round", () => s.RoundName, "Nome della gara riconosciuta nel calendario");
            Attach("RoundMatched", () => s.RoundMatched, "Vero se la pista corrisponde a una gara del calendario");
            Attach("Circuit", () => s.CircuitName, "Nome del circuito");
            Attach("Session", () => s.SessionLabel, "Prove libere, qualifiche, sprint o gara");
            Attach("Lap", () => s.LapCurrent, "Giro in corso");
            Attach("LapsTotal", () => s.LapsTotal, "Giri totali, zero nelle sessioni a tempo");
            Attach("LapsRemaining", () => s.LapsRemaining, "Giri che mancano alla fine");
            Attach("Position", () => s.Position, "La tua posizione");
            Attach("PlayerName", () => s.PlayerName, "Il pilota che stai guidando nel campionato");
            Attach("PlayerTeam", () => s.PlayerTeam, "La tua scuderia");
            Attach("PlayerReplaced", () => s.PlayerReplaced, "Il pilota al cui posto corri, vuoto se il sedile era già tuo");

            Section("Giro di formazione");
            Attach("FormationLapOn", () => s.FormationPhase != "Idle" && s.FormationPhase != "Away",
                "La procedura e in corso, in una qualsiasi delle sue fasi");
            Attach("FormationPhase", () => s.FormationPhase,
                "Idle, Rolling, Forming, Lights, Away");
            Attach("FormationRolling", () => s.FormationPhase == "Rolling", "Giro di ricognizione");
            Attach("FormationOnGrid", () => s.FormationPhase == "Forming", "Fermi in griglia");
            Attach("FormationLights", () => s.FormationPhase == "Lights", "Semaforo acceso, verde imminente");
            Attach("LightsOut", () => _lightsOutUntil > DateTime.UtcNow,
                "Vero per tre secondi dopo il verde: il momento per far lampeggiare i LED");

            Section("Safety car");
            Attach("SafetyCarOn", () => s.RaceControlStatus == "SafetyCar" || s.RaceControlStatus == "SafetyCarEnding",
                "Safety car in pista, giro di rientro compreso");
            Attach("SafetyCarPhase", () => s.SafetyCarPhase,
                "Idle, Deploying, Bunching, LappedCarsUnlapping, InThisLap, Restarting");
            Attach("SafetyCarDeploying", () => s.SafetyCarPhase == "Deploying", "Sta uscendo dai box");
            Attach("SafetyCarBunching", () => s.SafetyCarPhase == "Bunching", "Il gruppo si sta ricompattando");
            Attach("SafetyCarUnlapping", () => s.SafetyCarPhase == "LappedCarsUnlapping",
                "I doppiati hanno il permesso di sdoppiarsi");
            Attach("SafetyCarInThisLap", () => s.SafetyCarPhase == "InThisLap", "Rientra questo giro");
            Attach("VirtualSafetyCarOn", () => s.RaceControlStatus == "VirtualSafetyCar", "VSC attiva");
            Attach("AutopilotOn", () => s.Autopilot,
                   "Vero quando a guidare la tua vettura e il pilota automatico");
            Attach("NeutralisedOn", () =>
                s.RaceControlStatus == "SafetyCar" || s.RaceControlStatus == "SafetyCarEnding" ||
                s.RaceControlStatus == "VirtualSafetyCar" || s.RaceControlStatus == "FormationLap",
                "Safety car, VSC o giro di formazione: in ogni caso non si corre");

            Attach("DeltaOk", () => s.Delta >= 0, "Il delta e rispettato: usalo per il colore della barra");
            Attach("DeltaValue", () => s.Delta, "Delta in secondi; positivo vuol dire che sei a posto");
            Attach("TargetSpeed", () => s.TargetSpeedKmh, "Velocita obiettivo in km/h sotto neutralizzazione");

            Section("Bandiere e stato");
            Attach("GreenFlagOn", () => s.RaceControlStatus == "Green", "Si corre");
            Attach("YellowFlagOn", () => s.RaceControlStatus == "LocalYellow" || s.YellowSectors.Count > 0,
                "Bandiera gialla in almeno un settore");
            Attach("RedFlagOn", () => s.RaceControlStatus == "RedFlag", "Bandiera rossa");
            Attach("ChequeredFlagOn", () => s.RaceControlStatus == "Checkered", "Bandiera a scacchi");
            Attach("PitLaneClosedOn", () => s.PitLaneClosed, "Pit lane chiusa");
            Attach("PenaltyOn", () => s.PendingPenaltySeconds > 0, "Hai penalita da scontare");

            Attach("RaceControlCode", () =>
            {
                switch (s.RaceControlStatus)
                {
                    case "LocalYellow": return 1;
                    case "VirtualSafetyCar": return 2;
                    case "SafetyCar":
                    case "SafetyCarEnding": return 3;
                    case "RedFlag": return 4;
                    case "FormationLap": return 5;
                    case "Checkered": return 6;
                    default: return 0;
                }
            }, "Stato in un solo numero: 0 verde, 1 giallo, 2 VSC, 3 safety car, 4 rossa, 5 formazione, 6 scacchi");

            Section("Direzione gara");
            Attach("RaceControl.Status", () => s.RaceControlStatus, "Stato come testo interno");
            Attach("RaceControl.Text", () => s.RaceControlText, "Etichetta pronta da mostrare, gia in italiano");
            Attach("RaceControl.Delta", () => s.Delta, "Come DeltaValue");
            Attach("RaceControl.TargetSpeedKmh", () => s.TargetSpeedKmh, "Come TargetSpeed");
            Attach("RaceControl.SafetyCar", () => s.RaceControlStatus == "SafetyCar" || s.RaceControlStatus == "SafetyCarEnding", "Come SafetyCarOn");
            Attach("RaceControl.VSC", () => s.RaceControlStatus == "VirtualSafetyCar", "Come VirtualSafetyCarOn");
            Attach("RaceControl.Yellow", () => s.YellowSectors.Count > 0, "Almeno un settore sotto giallo");
            Attach("RaceControl.YellowSectors", () => string.Join(",", s.YellowSectors), "Numeri dei settori gialli");
            Attach("RaceControl.PitLaneClosed", () => s.PitLaneClosed, "Come PitLaneClosedOn");
            Attach("RaceControl.SafetyCarPeriods", () => s.SafetyCarPeriods, "Quante safety car finora");
            Attach("RaceControl.VscPeriods", () => s.VirtualSafetyCarPeriods, "Quante VSC finora");
            Attach("RaceControl.Feed", () => string.Join(" | ", s.RaceControlFeed), "Ultime comunicazioni della direzione gara");

            Section("Penalita");
            Attach("Penalty.PendingSeconds", () => s.PendingPenaltySeconds, "Secondi di penalita accumulati");
            Attach("Penalty.HasPenalty", () => s.PendingPenaltySeconds > 0, "Come PenaltyOn");
            Attach("Penalty.TrackLimits", () => s.TrackLimitStrikes, "Infrazioni ai limiti pista");
            Attach("Penalty.List", () => string.Join(" | ", s.Penalties), "Elenco delle penalita con la motivazione");

            Section("Gomme");
            Attach("Tyres.Compound", () => s.TyreCompound, "Mescola per esteso");
            Attach("Tyres.Code", () => s.TyreCompoundCode, "Lettera della mescola: S, M, H, I, W");
            Attach("Tyres.WearPercent", () => s.TyreWearPercent, "Usura del pneumatico peggiore");
            Attach("Tyres.Temperature", () => s.TyreTemperature, "Temperatura media");
            Attach("Tyres.LapsToCliff", () => s.LapsToCliff, "Giri che restano prima del crollo di prestazione");
            Attach("Tyres.StintLaps", () => s.StintLaps, "Giri percorsi su questo treno");
            Attach("Tyres.PitStops", () => s.PitStops, "Soste fatte");

            Section("Strategia");
            Attach("Strategy.Advice", () => s.StrategyAdvice, "La chiamata del muretto in chiaro");
            Attach("Strategy.Selected", () => s.SelectedStrategy, "Lettera dello scenario scelto, vuota se segui il consiglio");
            Attach("Strategy.OptionCount", () => s.StrategyOptions.Count, "Quanti scenari sono sul tavolo");
            Attach("Strategy.Recommended", () =>
            {
                var r = s.StrategyOptions.FirstOrDefault(o => o.Recommended);
                return r != null ? r.Id : "";
            }, "Lettera dello scenario consigliato");

            // Quattro scenari al massimo: oltre, non si sceglie piu, si subisce.
            foreach (var letter in new[] { "A", "B", "C", "D" })
            {
                string id = letter;
                string w = "Scenario " + id + ": ";
                Attach("Strategy." + id + ".Available", () => Option(id) != null, w + "esiste");
                Attach("Strategy." + id + ".Name", () => Option(id) != null ? Option(id).Name : "", w + "titolo");
                Attach("Strategy." + id + ".Summary", () => Option(id) != null ? Option(id).Summary : "", w + "cosa comporta");
                Attach("Strategy." + id + ".Plan", () => Option(id) != null ? Option(id).Plan : "", w + "stint previsti");
                Attach("Strategy." + id + ".Delta", () => Option(id) != null ? Option(id).DeltaSeconds : 0, w + "secondi persi rispetto al piu veloce");
                Attach("Strategy." + id + ".Position", () => Option(id) != null ? Option(id).ProjectedPosition : 0, w + "arrivo previsto");
                Attach("Strategy." + id + ".Stops", () => Option(id) != null ? Option(id).Stops : 0, w + "numero di soste");
                Attach("Strategy." + id + ".Risk", () => Option(id) != null ? Option(id).Risk : 0, w + "rischio da 1 a 3");
                Attach("Strategy." + id + ".Selected", () => Option(id) != null && Option(id).Selected, w + "e quello scelto");
            }

            Attach("Strategy.NextCompound", () => s.RecommendedCompound, "Mescola consigliata alla prossima sosta");
            Attach("Strategy.WindowEarliest", () => s.PitWindowEarliest, "Primo giro utile per fermarsi");
            Attach("Strategy.WindowIdeal", () => s.PitWindowIdeal, "Giro ideale");
            Attach("Strategy.WindowLatest", () => s.PitWindowLatest, "Ultimo giro possibile");
            Attach("Strategy.WindowOpen", () => s.LapCurrent >= s.PitWindowEarliest && s.LapCurrent <= s.PitWindowLatest,
                "La finestra di sosta e aperta adesso");

            Section("Benzina");
            Attach("Fuel.Laps", () => s.FuelLaps, "Giri di autonomia stimati");
            Attach("Fuel.Litres", () => s.Fuel, "Litri nel serbatoio");

            Section("Ingegnere");
            Attach("Engineer.Message", () => s.EngineerMessage, "Ultima comunicazione radio");
            Attach("Engineer.Category", () => s.EngineerCategory, "Argomento: Gomme, Strategia, Meteo ...");
            Attach("Engineer.HasMessage", () => !string.IsNullOrEmpty(s.EngineerMessage), "C'e un messaggio da mostrare");
            Attach("Engineer.History", () => string.Join(" | ", s.EngineerHistory), "Ultime comunicazioni");

            Section("Meteo");
            Attach("Weather.Now", () => s.WeatherNow, "Condizioni attuali in parole");
            Attach("Weather.Forecast", () => s.WeatherForecast, "Riassunto della previsione");
            Attach("Weather.Wetness", () => s.Wetness, "Acqua in pista, da 0 a 1");
            Attach("Weather.MinutesToRain", () => s.MinutesToRain, "Minuti alla pioggia, -1 se non prevista");
            Attach("Weather.AirTemperature", () => s.AirTemperature, "Temperatura dell'aria");
            Attach("Weather.RoadTemperature", () => s.RoadTemperature, "Temperatura dell'asfalto");

            Section("Distacchi");
            Attach("Gap.Ahead", () => s.GapAheadSeconds, "Secondi dalla vettura davanti");
            Attach("Gap.Behind", () => s.GapBehindSeconds, "Secondi dalla vettura dietro");
            Attach("Gap.NameAhead", () => s.NameAhead, "Chi hai davanti");
            Attach("Gap.NameBehind", () => s.NameBehind, "Chi hai dietro");

            // Monitor dei tempi, appiattito riga per riga cosi una dashboard puo legare
            // direttamente le prime posizioni senza dover scorrere una lista.
            Section("Monitor dei tempi");
            for (int i = 1; i <= 24; i++)
            {
                int index = i - 1;
                string w = "Riga " + i + ": ";
                Attach("Timing." + i + ".Name", () => Row(index) != null ? Row(index).Name : "", w + "pilota");
                Attach("Timing." + i + ".Abbreviation", () => Row(index) != null ? Row(index).Abbreviation : "", w + "sigla a tre lettere");
                Attach("Timing." + i + ".Team", () => Row(index) != null ? Row(index).TeamName : "", w + "team");
                Attach("Timing." + i + ".Color", () => Row(index) != null ? Row(index).Color : "#000000", w + "colore del team");
                Attach("Timing." + i + ".Gap", () => Row(index) != null ? Row(index).Gap : "", w + "distacco dal leader");
                Attach("Timing." + i + ".Interval", () => Row(index) != null ? Row(index).Interval : "", w + "intervallo sulla vettura davanti");
                Attach("Timing." + i + ".LastLap", () => Row(index) != null ? Row(index).LastLap : "", w + "ultimo giro");
                Attach("Timing." + i + ".BestLap", () => Row(index) != null ? Row(index).BestLap : "", w + "miglior giro");
                Attach("Timing." + i + ".LastLapRank", () => Row(index) != null ? Row(index).LastLapRank : 0, w + "0 normale, 1 personale, 2 miglior tempo di sessione");
                Attach("Timing." + i + ".S1", () => Row(index) != null ? Row(index).S1 : "", w + "primo settore");
                Attach("Timing." + i + ".S2", () => Row(index) != null ? Row(index).S2 : "", w + "secondo settore");
                Attach("Timing." + i + ".S3", () => Row(index) != null ? Row(index).S3 : "", w + "terzo settore");
                Attach("Timing." + i + ".Compound", () => Row(index) != null ? Row(index).Compound : "", w + "mescola");
                Attach("Timing." + i + ".TyreAge", () => Row(index) != null ? Row(index).TyreAge : 0, w + "giri su quel treno di gomme");
                Attach("Timing." + i + ".PitStops", () => Row(index) != null ? Row(index).PitStops : 0, w + "soste");
                Attach("Timing." + i + ".PositionChange", () => Row(index) != null ? Row(index).PositionChange : 0, w + "posizioni guadagnate dal via");
                Attach("Timing." + i + ".InPit", () => Row(index) != null && Row(index).InPit, w + "e in corsia box");
                Attach("Timing." + i + ".IsPlayer", () => Row(index) != null && Row(index).IsPlayer, w + "sei tu");
            }
        }

        private StrategyChoice Option(string id) =>
            Engine.State.StrategyOptions.FirstOrDefault(o => o.Id == id);

        private LiveStandingRow Row(int index)
        {
            var timing = Engine.State.Timing;
            return index >= 0 && index < timing.Count ? timing[index] : null;
        }

        /// <summary>Tutte le proprieta esposte, con la loro descrizione. Le mostra il pannello.</summary>
        public List<PitwallProperty> Properties { get; } = new List<PitwallProperty>();

        /// <summary>Azioni mappabili ed eventi agganciabili, anche questi documentati nel pannello.</summary>
        public List<PitwallTrigger> Triggers { get; } = new List<PitwallTrigger>();

        private string _section = "Generale";

        private void Section(string name) => _section = name;

        /// <summary>
        /// Registra una proprieta e la annota nel catalogo. SimHub antepone da solo il nome
        /// della classe, quindi in dashboard diventa [PitwallPlugin.NomeProprieta]: aggiungere
        /// qui un altro prefisso darebbe il doppio nome che si vede con altre plugin.
        /// </summary>
        private void Attach(string name, Func<object> provider, string description = "")
        {
            Properties.Add(new PitwallProperty(_section, name, description, provider));
            this.AttachDelegate(name, provider);
        }

        /// <summary>Registra un evento e lo annota nel catalogo mostrato nel pannello.</summary>
        private void DeclareEvent(string name, string description)
        {
            Triggers.Add(new PitwallTrigger("Eventi", name, description));
            this.AddEvent(name);
        }

        /// <summary>Registra un'azione mappabile e la annota nel catalogo.</summary>
        private void DeclareAction(string section, string name, string description,
                                   Action<PluginManager, string> handler)
        {
            Triggers.Add(new PitwallTrigger(section, name, description));
            this.AddAction(name, (a, b) => handler(a, b));
        }

        private void RegisterEventsAndActions()
        {
            DeclareEvent("EngineerMessage", "L'ingegnere ha detto qualcosa");
            DeclareEvent("PenaltyIssued", "Hai preso una penalita");
            DeclareEvent("SafetyCarDeployed", "Safety car in pista");
            DeclareEvent("VirtualSafetyCarDeployed", "VSC attivata");
            DeclareEvent("GreenFlag", "Verde, si riparte");
            DeclareEvent("YellowFlag", "Bandiera gialla");
            DeclareEvent("RedFlag", "Bandiera rossa");
            DeclareEvent("FormationLapStarted", "Inizio del giro di formazione");
            DeclareEvent("SessionCompleted", "Risultato della sessione registrato");

            Engine.RaceControlStatusChanged += status =>
            {
                switch (status)
                {
                    case RaceControlStatus.SafetyCar: this.TriggerEvent("SafetyCarDeployed"); break;
                    case RaceControlStatus.VirtualSafetyCar: this.TriggerEvent("VirtualSafetyCarDeployed"); break;
                    case RaceControlStatus.Green:
                        this.TriggerEvent("GreenFlag");
                        // Il verde che segue una neutralizzazione o la griglia e il momento in
                        // cui una dashboard vuole lampeggiare: dura poco, e va segnalato.
                        _lightsOutUntil = DateTime.UtcNow.AddSeconds(3);
                        break;
                    case RaceControlStatus.FormationLap: this.TriggerEvent("FormationLapStarted"); break;
                    case RaceControlStatus.RedFlag: this.TriggerEvent("RedFlag"); break;
                    case RaceControlStatus.LocalYellow: this.TriggerEvent("YellowFlag"); break;
                }
            };
            Engine.PenaltyIssued += penalty =>
            {
                if (penalty.Type != PenaltyType.Warning) this.TriggerEvent("PenaltyIssued");
            };

            DeclareAction("Radio", "RepeatLastMessage", "Ripete l'ultima comunicazione", (a, b) =>
            {
                var last = Engine.Engineer.Radio.History.LastOrDefault();
                if (last != null) _voice.Speak(last.Text, true);
            });

            DeclareAction("Radio", "ToggleEngineer", "Accende e spegne l'ingegnere", (a, b) =>
            {
                Settings.EngineerEnabled = !Settings.EngineerEnabled;
                Engine.Engineer.Rules.Enabled = Settings.EngineerEnabled;
            });

            DeclareAction("Radio", "ToggleSpeech", "Accende e spegne la voce", (a, b) =>
            {
                Settings.SpeechEnabled = !Settings.SpeechEnabled;
                _voice.Enabled = Settings.SpeechEnabled;
            });

            DeclareAction("Radio", "AskStrategy", "Fa leggere gli scenari di strategia", (a, b) =>
            {
                var options = Engine.State.StrategyOptions;
                if (options.Count == 0)
                {
                    string advice = Engine.State.StrategyAdvice;
                    if (!string.IsNullOrEmpty(advice)) _voice.Speak(advice, true);
                    return;
                }

                // Letti come al muretto: scenario, cosa comporta, e quanto costa rispetto al
                // migliore. Il distacco e la parte che fa decidere.
                string text = string.Join(". ", options.Select(o =>
                    "Scenario " + o.Id + ", " + o.Name.ToLowerInvariant() + ", " + o.Summary.ToLowerInvariant() +
                    (o.DeltaSeconds > 0.1
                        ? ", " + o.DeltaSeconds.ToString("0.0") + " secondi in piu"
                        : ", il piu veloce")));
                _voice.Speak(text, true);
            });

            // Il pilota automatico serve a provare il campionato senza stare al volante: la
            // vettura passa all'IA e segue le stesse regole di tutte le altre.
            DeclareAction("Direzione gara", "ToggleAutopilot", "Accende e spegne il pilota automatico", (a, b) =>
            {
                bool on = !Engine.Autopilot;
                LiveBridge.SendCommand(CommandNames.ToggleAutopilot, on ? "1" : "0");
                _voice.Speak(on ? "Pilota automatico inserito" : "Pilota automatico disinserito", true);
            });

            // Scelta dello scenario dai pulsanti del volante.
            foreach (var letter in new[] { "A", "B", "C", "D" })
            {
                string id = letter;
                DeclareAction("Strategia", "SelectStrategy" + id, "Sceglie lo scenario " + id, (a, b) =>
                {
                    var option = Option(id);
                    if (option == null) { _voice.Speak("Scenario " + id + " non disponibile", true); return; }

                    LiveBridge.SendCommand(CommandNames.SelectStrategy, id);
                    _voice.Speak("Scenario " + id + ", " + option.Name.ToLowerInvariant() + ", confermato", true);
                });
            }

            DeclareAction("Radio", "AskGaps", "Fa leggere posizione e distacchi", (a, b) =>
            {
                var state = Engine.State;
                string text = string.Format("P{0}. {1} davanti a {2:0.0}. {3} dietro a {4:0.0}.",
                    state.Position, Short(state.NameAhead), state.GapAheadSeconds,
                    Short(state.NameBehind), state.GapBehindSeconds);
                _voice.Speak(text, true);
            });

            DeclareAction("Radio", "AskTyres", "Fa leggere stato gomme e degrado", (a, b) =>
            {
                var state = Engine.State;
                string text = string.Format("Gomme {0}, usura {1:0} percento, {2:0} giri al degrado.",
                    state.TyreCompound, state.TyreWearPercent, state.LapsToCliff);
                _voice.Speak(text, true);
            });

            // Comandi per il computer di garage, da mappare sui pulsanti del volante. Vanno in
            // coppia con i binding nativi dell'app: chi ha gia tutto in SimHub usa questi, chi
            // preferisce legare i tasti direttamente in pista usa quelli.
            DeclareAction("Computer di garage", "GarageNextPage", "Pagina successiva", (a, b) => Engine.PostUiCommand("page:next"));
            DeclareAction("Computer di garage", "GaragePrevPage", "Pagina precedente", (a, b) => Engine.PostUiCommand("page:prev"));

            var pages = new[]
            {
                new { Action = "GaragePageWeather", Command = "page:1", Label = "Meteo" },
                new { Action = "GaragePageStrategy", Command = "page:2", Label = "Strategia" },
                new { Action = "GaragePageTiming", Command = "page:3", Label = "Tempi" },
                new { Action = "GaragePageCar", Command = "page:4", Label = "Auto" },
                new { Action = "GaragePageTelemetry", Command = "page:5", Label = "Telemetria" },
                new { Action = "GaragePageAnalysis", Command = "page:6", Label = "Analisi" }
            };

            foreach (var page in pages)
            {
                string command = page.Command;
                DeclareAction("Computer di garage", page.Action, "Vai alla pagina " + page.Label,
                    (a, b) => Engine.PostUiCommand(command));
            }

            // Manual race-control controls, handy when testing a dashboard.
            DeclareAction("Direzione gara", "DeployVSC", "Attiva una VSC a mano, utile per provare una dashboard", (a, b) => LiveBridge.SendCommand(CommandNames.DeployVsc));
            DeclareAction("Direzione gara", "DeploySafetyCar", "Manda in pista la safety car", (a, b) => LiveBridge.SendCommand(CommandNames.DeploySafetyCar));
            DeclareAction("Direzione gara", "ResumeGreen", "Torna al verde", (a, b) => LiveBridge.SendCommand(CommandNames.ResumeGreen));
            DeclareAction("Direzione gara", "SaveSessionResult", "Registra subito il risultato della sessione", (a, b) => LiveBridge.SendCommand(CommandNames.SaveResult));
        }

        private static string Short(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "nessuno";
            var parts = name.Trim().Split(' ');
            return parts[parts.Length - 1];
        }
    }
}
