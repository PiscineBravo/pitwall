using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Pitwall.Ai;
using Pitwall.Engineer;
using Pitwall.Model;
using Pitwall.RaceControl;
using Pitwall.Results;
using Pitwall.Scoring;
using Pitwall.Storage;
using Pitwall.Strategy;
using Pitwall.Tyres;
using Pitwall.Weather;

using Pitwall.Lingue;

namespace Pitwall.Live
{
    /// <summary>
    /// The brain of the whole thing, living outside SimHub so it can be tested on its own.
    ///
    /// It recognises which round and which session of the championship is being driven, runs
    /// race control, the tyre model and the engineer over every telemetry frame, publishes the
    /// live state, and writes the classification into the championship when the flag falls.
    /// The SimHub plugin does nothing but translate GameData into a frame and hand it over.
    /// </summary>
    public sealed class SessionEngine
    {
        private string _sessionKey = "";
        private double _lastElapsedSeconds;
        private TelemetryFrame _lastFrame;
        private bool _sessionActive;
        private bool _resultSaved;
        private DateTime _sessionStartUtc = DateTime.UtcNow;
        private readonly Dictionary<string, int> _pitCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, List<string>> _compounds = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        public Championship Championship { get; private set; }

        public Round CurrentRound { get; private set; }

        public SessionConfig CurrentSession { get; private set; }

        public RaceDirector RaceControl { get; private set; } = new RaceDirector();

        /// <summary>Il giro di uscita in prove e qualifica.</summary>
        public OutLapDirector OutLap { get; private set; } = new OutLapDirector();

        /// <summary>Le qualifiche a eliminazione, quando la sessione le prevede.</summary>
        public QualifyingDirector Qualifying { get; private set; } = new QualifyingDirector();

        /// <summary>Quando la direzione gara concede il DRS.</summary>
        public DrsController Drs { get; private set; } = new DrsController();

        /// <summary>
        /// Il direttore dell'intelligenza artificiale: strategia, degrado e destino degli
        /// avversari. I suoi comandi vengono pubblicati per l'app in pista, che li applica.
        /// </summary>
        public AiDirector Ai { get; } = new AiDirector();

        /// <summary>
        /// Gli scenari di strategia che il muretto propone in gara. Si sceglie da volante, dal
        /// gestionale o dal computer di garage.
        /// </summary>
        public StrategyPlanner Strategy { get; } = new StrategyPlanner();

        /// <summary>Ultimi comandi calcolati, gia serializzati per l'app in pista.</summary>
        public List<string> AiCommands { get; private set; } = new List<string>();

        /// <summary>
        /// Riconosce da solo chi sta guidando e gli assegna il sedile libero nella griglia.
        /// Si puo spegnere se si preferisce decidere a mano nel gestionale.
        /// </summary>
        public bool AutoAssignPlayerSeat { get; set; } = true;

        /// <summary>
        /// Vero quando a guidare la vettura del giocatore e il pilota automatico. Serve per
        /// collaudare il campionato senza stare al volante: la vettura entra nel giro dei
        /// comandi come tutte le altre e segue le stesse regole.
        /// </summary>
        public bool Autopilot { get; set; }

        public StintTracker Tyres { get; } = new StintTracker();

        public EngineerBrain Engineer { get; } = new EngineerBrain();

        public LiveState State { get; private set; } = new LiveState();

        /// <summary>Raised whenever the engineer decides to say something.</summary>
        public event Action<EngineerMessage> EngineerSpoke;

        /// <summary>Raised when a session classification has been written into the championship.</summary>
        public event Action<SessionResult> SessionCompleted;

        /// <summary>Raised for anything worth writing into the SimHub log.</summary>
        public event Action<string> Log;

        /// <summary>
        /// Race control status changes. Subscribe here rather than to <see cref="RaceControl"/>,
        /// which is replaced with a fresh director at the start of every session.
        /// </summary>
        public event Action<RaceControlStatus> RaceControlStatusChanged;

        /// <summary>Raised whenever race control issues a penalty.</summary>
        public event Action<Penalty> PenaltyIssued;

        /// <summary>Scatta quando viene scelto uno scenario di strategia.</summary>
        public event Action<StrategyOption> StrategyChosen;

        public SessionEngine()
        {
            Engineer.Radio.MessageDelivered += m => EngineerSpoke?.Invoke(m);
            WireRaceControl(RaceControl);
        }

        private void WireRaceControl(RaceDirector director)
        {
            if (director == null) return;
            director.StatusChanged += status => RaceControlStatusChanged?.Invoke(status);
            director.Penalties.PenaltyIssued += penalty => PenaltyIssued?.Invoke(penalty);
        }

        // ---- Championship ---------------------------------------------------------------------

        public bool LoadChampionship(string championshipId)
        {
            var loaded = ChampionshipStore.Load(championshipId);
            if (loaded == null) return false;
            SetChampionship(loaded);
            return true;
        }

        /// <summary>Loads the most recently modified championship, which is almost always the right one.</summary>
        public bool LoadMostRecent()
        {
            var summary = ChampionshipStore.List().FirstOrDefault();
            if (summary == null) return false;
            return LoadChampionship(summary.Id);
        }

        public void SetChampionship(Championship championship)
        {
            Championship = championship;
            if (championship != null)
            {
                Engineer.Configure(championship.Rules.Engineer);
                Engineer.ForecastHorizonMinutes = championship.Rules.Weather.ForecastHorizonMinutes;
                Tyres.Rules = championship.Rules.Tyres;
                Raise("Campionato caricato: " + championship.Name);
            }
        }

        /// <summary>
        /// Manda un comando alle app in pista: cambio pagina, scelta di uno scenario, e simili.
        /// Lo usa il plugin quando scatta un pulsante del volante mappato in SimHub.
        /// </summary>
        public void PostUiCommand(string command)
        {
            if (string.IsNullOrWhiteSpace(command)) return;
            State.UiCommand = command.Trim();
            State.UiCommandSeq++;
            LiveBridge.PublishState(State, true);
        }

        public void SaveChampionship()
        {
            if (Championship == null) return;
            ChampionshipStore.Save(Championship);
        }

        /// <summary>
        /// Riscrive il preset di Content Manager sulla gara che tocca adesso.
        ///
        /// Va chiamato appena un risultato e registrato. In Content Manager si vede una gara alla
        /// volta, e il preset lo scrive l'esportatore guardando quale sia la prossima: finche
        /// nessuno lo richiama, quello in cartella resta quello della gara appena finita, che
        /// quindi si potrebbe riaprire. Aspettare la prossima apertura dell'applicazione Windows
        /// non basta, perche nessuno obbliga a riaprirla fra una gara e l'altra.
        ///
        /// Non alza mai un'eccezione verso la sessione: Content Manager puo non essere installato,
        /// la cartella puo essere di sola lettura, e niente di tutto questo vale un risultato
        /// perso. Al massimo si scrive in cronologia.
        /// </summary>
        private void RefreshContentManager()
        {
            if (Championship == null) return;

            try
            {
                var esito = Pitwall.ContentManager.ContentManagerExporter.Export(Championship);
                if (esito.Written > 0)
                {
                    var prossima = Championship.NextRound();
                    if (prossima != null)
                        Raise(Testi.T("cm.prossima.gara", prossima.Order + ". " + prossima.Name));
                }
                else
                {
                    Raise(Testi.T("cm.campionato.concluso"));
                }
            }
            catch (Exception ex)
            {
                Raise(Testi.T("cm.aggiornamento.fallito", ex.Message));
            }
        }

        // ---- Frame loop --------------------------------------------------------------------------

        /// <summary>Processes one telemetry frame. Call this from the plugin data loop.</summary>
        public void ProcessFrame(TelemetryFrame frame)
        {
            if (frame == null) return;
            HandleCommands();

            if (!frame.GameRunning)
            {
                if (_sessionActive) FinishSession(_lastFrame);
                // Anche lo stesso circuito/tipo/distanza deve aprire una sessione al ritorno.
                _sessionKey = "";
                State.GameRunning = false;
                State.PluginRunning = true;
                LiveBridge.PublishState(State);
                return;
            }

            string key = BuildSessionKey(frame);

            // Il cronometro che torna indietro vuol dire sessione nuova, anche quando la chiave
            // non cambia.
            //
            // La chiave e fatta di circuito, tipo di sessione e numero di giri: due gare di
            // fila sullo stesso tracciato hanno la stessa chiave, e la seconda non veniva
            // riconosciuta come nuova. Peggio ancora con i dati che Assetto Corsa lascia in
            // memoria dopo essersi chiuso: Pitwall apriva la sessione su quelli e poi, quando
            // la gara vera partiva davvero, non se ne accorgeva piu.
            double elapsed = frame.SessionElapsed.TotalSeconds;
            bool rewound = _sessionActive && elapsed < _lastElapsedSeconds - 5;

            if (key != _sessionKey || rewound)
            {
                if (_sessionActive && !rewound) FinishSession(_lastFrame);
                StartSession(frame, key);
            }

            _lastElapsedSeconds = elapsed;

            _lastFrame = frame;

            if (_sessionActive)
            {
                var playerDriver = Championship != null ? Championship.PlayerDriver : null;
                string playerId = playerDriver != null ? playerDriver.Id : "player";
                string playerName = playerDriver != null ? playerDriver.FullName : frame.PlayerName;

                Tyres.Update(frame.TyreCompound,
                             frame.TyreWearFrontLeft, frame.TyreWearFrontRight,
                             frame.TyreWearRearLeft, frame.TyreWearRearRight,
                             frame.TyreTemperatureAvg, frame.CompletedLaps, frame.LastLapTime);

                // Il riconoscimento aspetta che la entry list sia completa: al primo fotogramma
                // il gioco non ha ancora dichiarato tutte le vetture, e il conto non tornerebbe.
                if (!_seatAssigned && frame.Opponents.Count > 1)
                {
                    _seatAssigned = true;
                    AssignPlayerSeat(frame);

                    playerDriver = Championship != null ? Championship.PlayerDriver : null;
                    playerId = playerDriver != null ? playerDriver.Id : "player";
                    playerName = playerDriver != null ? playerDriver.FullName : frame.PlayerName;
                }

                RaceControl.Update(frame, playerId, playerName);
                Engineer.Update(frame, Tyres, RaceControl);

                UpdateAi(frame);
                UpdateStrategy(frame);
                TrackOpponentDetails(frame);

                if (frame.FlagCheckered && !_resultSaved)
                    FinishSession(frame);
            }

            BuildLiveState(frame);
            LiveBridge.PublishState(State);
        }

        // ---- Session lifecycle ---------------------------------------------------------------------

        private string BuildSessionKey(TelemetryFrame frame)
        {
            return frame.TrackId + "|" + frame.TrackConfig + "|" + frame.SessionTypeName + "|" +
                   (frame.TotalLaps > 0 ? frame.TotalLaps.ToString() : "timed");
        }

        private void StartSession(TelemetryFrame frame, string key)
        {
            _sessionKey = key;
            _resultSaved = false;
            _sessionStartUtc = DateTime.UtcNow;
            _pitCounts.Clear();
            _compounds.Clear();

            CurrentRound = MatchRound(frame);
            CurrentSession = MatchSession(frame, CurrentRound);

            var circuit = Championship != null ? Championship.CircuitOf(CurrentRound) : null;
            if (circuit == null)
            {
                circuit = new Circuit
                {
                    Name = string.IsNullOrEmpty(frame.TrackName) ? frame.TrackId : frame.TrackName,
                    AcTrackId = frame.TrackId,
                    AcTrackConfig = frame.TrackConfig,
                    LengthMeters = frame.TrackLengthMeters,
                    GrandPrixLaps = frame.TotalLaps
                };
            }

            if (CurrentSession == null)
                CurrentSession = new SessionConfig { Kind = GuessKind(frame.SessionTypeName) };

            if (CurrentSession.Weather == null && Championship != null)
                CurrentSession.Weather = WeatherGenerator.Generate(circuit, CurrentSession,
                    Championship.Rules.Weather, CurrentRound != null ? CurrentRound.Seed : Environment.TickCount);

            int seed = CurrentRound != null ? CurrentRound.Seed : Environment.TickCount;
            RaceControl = new RaceDirector(seed);
            WireRaceControl(RaceControl);
            // Questa sessione la stiamo prendendo dall'inizio, o ci siamo arrivati in mezzo?
            //
            // La domanda sembra oziosa e non lo e. Quando Assetto Corsa si chiude lascia in
            // memoria i dati dell'ultima sessione, e alla partenza successiva per una ventina
            // di secondi si leggono quelli: un altro circuito, un altro giro, un'altra gara.
            // Pitwall ci faceva partire sopra la procedura di partenza, schierava venti vetture
            // in piazzole calcolate su quei dati e le teneva ferme. Al verde non ripartivano
            // piu, e la gara cominciava con tutti immobili e una bandiera gialla che non si
            // spegneva. Era questo, non il blocco ai box.
            //
            // Nel dubbio si rinuncia alla ricognizione e si da il verde: una gara senza giro di
            // formazione e un peccato, una gara che non parte e un prodotto rotto.
            // Niente condizioni sul cronometro: quello arriva da SimHub come stima e non si
            // azzera fra una sessione e l'altra. All'inizio della gara, dopo prove e qualifica,
            // segnava gia piu di cinque minuti, e la ricognizione non partiva mai.
            //
            // Quello che conta e un'altra cosa: nessuno ha ancora completato un giro. Se la
            // sessione fosse gia in corso qualcuno un giro lo avrebbe fatto. Per i dati che
            // Assetto Corsa lascia in memoria dopo essersi chiuso c'e gia il controllo del
            // cronometro che torna indietro, che quelli li riconosce.
            bool fromTheStart = frame.CompletedLaps == 0 && frame.Opponents.Count > 1;

            if (!fromTheStart)
                Raise("Sessione presa in corsa: niente giro di formazione.");

            RaceControl.BeginSession(circuit, CurrentSession,
                Championship != null ? Championship.Rules.RaceControl : new Rules.RaceControlRules(),
                fromTheStart);

            OutLap.Reset();
            Drs.Begin(Championship != null ? Championship.Rules.Drs : new Rules.DrsRules());
            _lastPlanMinute = -1;
            _planMinuteAt = DateTime.MinValue;
            _planClockBroken = false;
            _sessionMismatchSeen = false;
            Qualifying = new QualifyingDirector();
            Qualifying.SegmentClosed += (name, dropped) =>
            {
                Raise(dropped.Count > 0
                    ? name + " chiusa: eliminati " + string.Join(", ", dropped)
                    : name + " chiusa.");
            };
            Qualifying.Begin(CurrentSession,
                Championship != null ? Championship.Rules.Qualifying : new Rules.QualifyingRules(),
                Championship != null ? Championship.Drivers.Count : frame.Opponents.Count);

            Ai.Reset();
            Strategy.Begin(circuit, CurrentSession);
            _tyreAgeBase.Clear();
            _aiPlanned = false;
            _seatAssigned = false;
            AiCommands = new List<string>();
            _lapTimes.Clear();
            _lastLapRecorded = -1;

            Tyres.ResetSession();
            Tyres.TyreSeverity = circuit.TyreSeverity;
            if (Championship != null) Tyres.Rules = Championship.Rules.Tyres;

            Engineer.BeginSession(circuit, CurrentSession, CurrentSession.Weather);

            _sessionActive = true;
            Raise("Sessione avviata: " + CurrentSession.Kind + " a " + circuit.Name +
                  (CurrentRound != null ? " (" + CurrentRound.Name + ")" : " - non in calendario"));
        }

        /// <summary>Closes the session and writes its classification into the championship.</summary>
        public void FinishSession(TelemetryFrame frame)
        {
            if (!_sessionActive) return;
            _sessionActive = false;

            if (frame == null || Championship == null || CurrentRound == null || CurrentSession == null)
            {
                Raise("Sessione terminata senza risultato registrabile.");
                return;
            }

            RaceControl.EndSession();

            var result = ClassificationBuilder.Build(Championship, CurrentRound, CurrentSession,
                                                     frame, RaceControl, _pitCounts, _compounds);

            // Si chiede il permesso prima di assegnare i punti. Se la gara era gia conclusa il
            // risultato non si registra, e i punti non devono essere stati sommati nel frattempo.
            if (!Championship.CanRecord(result))
            {
                _resultSaved = true;
                Raise(Testi.T("risultato.gia.registrato", CurrentRound.Name));
                return;
            }

            StandingsCalculator.ApplyPoints(Championship, result);
            Championship.RecordResult(result);
            SaveChampionship();

            // Content Manager si aggiorna adesso, non alla prossima apertura dell'applicazione
            // Windows. E il momento in cui la gara appena corsa deve sparire dall'elenco e
            // comparire la successiva: se si aspettasse il Manager, chi gioca tornerebbe in
            // Content Manager e troverebbe ancora selezionabile la gara che ha appena finito.
            RefreshContentManager();

            _resultSaved = true;
            Raise("Risultato salvato: " + CurrentSession.Kind + " - " + CurrentRound.Name);
            SessionCompleted?.Invoke(result);
        }

        /// <summary>
        /// Riconosce chi sta guidando e gli assegna un sedile.
        ///
        /// Il nome che usi in Assetto Corsa non compare nel campionato, quindi Pitwall deve
        /// capire al posto di chi stai correndo. Lo deduce dalla entry list: in una griglia di
        /// venti vetture, se diciannove nomi del campionato sono in pista e uno manca, quello
        /// che manca e il sedile che occupi tu. E un ragionamento che non richiede alcuna
        /// configurazione e funziona anche cambiando scuderia da una gara all'altra.
        ///
        /// Il sedile conserva team, numero e sigla: cambia il pilota, non la macchina. Il nome
        /// di chi lo occupava resta annotato.
        /// </summary>
        private void AssignPlayerSeat(TelemetryFrame frame)
        {
            if (!AutoAssignPlayerSeat || Championship == null) return;

            string playerName = (frame.PlayerName ?? "").Trim();
            if (playerName.Length == 0) return;

            // Gia riconosciuto: basta assicurarsi che sia l'unico marcato come giocatore.
            var existing = Championship.MatchTelemetryName(playerName);
            if (existing != null)
            {
                if (!existing.IsPlayer) SetSolePlayer(existing);
                return;
            }

            // Chi del campionato non e in pista? Quel sedile e il tuo.
            var onTrack = new HashSet<string>(
                frame.Opponents
                    .Where(o => !o.IsPlayer && !SafetyCar.IsSafetyCar(o.Name))
                    .Select(o => o.Name.Trim()),
                StringComparer.OrdinalIgnoreCase);

            var missing = Championship.Drivers
                .Where(d => !onTrack.Contains(d.MatchName) && !onTrack.Contains(d.FullName))
                .ToList();

            Driver seat;
            if (missing.Count == 1)
            {
                seat = missing[0];
            }
            else
            {
                // Entry list non conclusiva: si resta sul sedile gia marcato come tuo, se c'e.
                seat = Championship.PlayerDriver;
                if (seat == null)
                {
                    Raise("Non riesco a capire quale sedile occupi: " + missing.Count +
                          " piloti del campionato non sono in pista. Spunta tu nel gestionale.");
                    return;
                }
            }

            TakeOverSeat(seat, playerName);
        }

        private void TakeOverSeat(Driver seat, string playerName)
        {
            string previous = seat.FullName;

            // Il nome originale si annota solo la prima volta: subentrare due volte di fila non
            // deve far perdere di chi era davvero il sedile.
            if (!seat.SeatTakenOver && !string.Equals(previous, playerName, StringComparison.OrdinalIgnoreCase))
                seat.ReplacedName = previous;

            var parts = playerName.Split(new[] { ' ' }, 2, StringSplitOptions.RemoveEmptyEntries);
            seat.FirstName = parts.Length > 0 ? parts[0] : playerName;
            seat.LastName = parts.Length > 1 ? parts[1] : "";
            seat.AcName = playerName;

            // Il numero resta quello della vettura, la sigla no: lasciare VER accanto a un altro
            // nome sul monitor dei tempi confonderebbe e basta.
            seat.Abbreviation = Abbreviation(seat);

            SetSolePlayer(seat);
            SaveChampionship();

            var team = Championship.TeamById(seat.TeamId);
            Raise("Sei tu a guidare: " + playerName +
                  (team != null ? " su " + team.Name : "") +
                  (seat.SeatTakenOver ? ", al posto di " + seat.ReplacedName : ""));
        }

        /// <summary>Tre lettere dal cognome, o dal nome quando il cognome non c'e.</summary>
        private static string Abbreviation(Driver driver)
        {
            string source = !string.IsNullOrWhiteSpace(driver.LastName) ? driver.LastName : driver.FirstName;
            source = new string(source.Where(char.IsLetter).ToArray());
            if (source.Length == 0) return "PLR";
            return source.Substring(0, Math.Min(3, source.Length)).ToUpperInvariant();
        }

        private void SetSolePlayer(Driver driver)
        {
            foreach (var d in Championship.Drivers) d.IsPlayer = d.Id == driver.Id;
        }

        private Round MatchRound(TelemetryFrame frame)
        {
            if (Championship == null) return null;
            var circuit = Championship.MatchTelemetryTrack(frame.TrackId, frame.TrackConfig);
            if (circuit == null) return null;

            // Prefer the first round at this circuit that still has sessions to run.
            return Championship.Calendar
                       .Where(r => r.CircuitId == circuit.Id && r.Status != RoundStatus.Completed)
                       .OrderBy(r => r.Order)
                       .FirstOrDefault()
                   ?? Championship.Calendar.FirstOrDefault(r => r.CircuitId == circuit.Id);
        }

        private SessionConfig MatchSession(TelemetryFrame frame, Round round)
        {
            var kind = GuessKind(frame.SessionTypeName);
            if (round == null) return null;

            var done = new HashSet<SessionKind>(
                Championship.ResultsFor(round.Id).Select(r => r.Kind));

            if (kind == SessionKind.Practice1)
            {
                // The game only says "Practice"; pick the first one not yet recorded.
                foreach (var candidate in new[] { SessionKind.Practice1, SessionKind.Practice2, SessionKind.Practice3 })
                {
                    var session = round.Session(candidate);
                    if (session != null && session.Enabled && !done.Contains(candidate)) return session;
                }
                return round.Session(SessionKind.Practice1);
            }

            if (kind == SessionKind.Qualifying)
            {
                if (round.Format == WeekendFormat.Sprint && !done.Contains(SessionKind.SprintQualifying))
                {
                    var sq = round.Session(SessionKind.SprintQualifying);
                    if (sq != null && sq.Enabled) return sq;
                }
                return round.Session(SessionKind.Qualifying);
            }

            if (kind == SessionKind.Race)
            {
                if (round.Format == WeekendFormat.Sprint && !done.Contains(SessionKind.Sprint))
                {
                    var sprint = round.Session(SessionKind.Sprint);
                    if (sprint != null && sprint.Enabled) return sprint;
                }
                return round.Session(SessionKind.Race);
            }

            return round.Session(kind);
        }

        private static SessionKind GuessKind(string sessionTypeName)
        {
            string s = (sessionTypeName ?? "").ToLowerInvariant();
            if (s.Contains("qual")) return SessionKind.Qualifying;
            if (s.Contains("race")) return SessionKind.Race;
            return SessionKind.Practice1;
        }

        // ---- Intelligenza artificiale e safety car -------------------------------------------------

        private bool _aiPlanned;
        private bool _seatAssigned;

        /// <summary>
        /// Prepara i piani degli avversari al primo giro utile e poi, a ogni frame, calcola
        /// cosa deve fare ognuno. Alla lista si aggiunge la vettura di sicurezza, che non corre
        /// ma va comandata anche lei.
        /// </summary>
        /// <summary>
        /// Prove libere e qualifica: nessuna strategia di gara, ma il giro di uscita si. Ogni
        /// vettura che lascia i box fa il suo giro di preparazione - piano, fuori traiettoria,
        /// a scaldare le gomme - e solo dopo si lancia. Vale anche per la vettura del giocatore
        /// quando guida il pilota automatico.
        /// </summary>
        private double _lastPlanMinute = -1;
        private DateTime _planMinuteAt = DateTime.MinValue;
        private bool _planClockBroken;
        private bool _sessionMismatchSeen;

        private List<string> BuildOutLapCommands(TelemetryFrame frame)
        {
            var commands = new List<string>();
            if (Championship == null) return commands;

            // Controprova prima di toccare qualunque cosa.
            //
            // Qui ci si arriva solo quando la sessione non e una gara. Ma se la direzione gara
            // sta conducendo una procedura di partenza, allora una gara c'e, e le due meta del
            // programma non sono d'accordo fra loro. E successo: il tipo di sessione arriva da
            // SimHub come testo, nei primi fotogrammi di una sessione nuova puo ancora essere
            // quello di prima, e a Melbourne la gara e partita con venti vetture tenute ferme in
            // garage perche Pitwall si credeva ancora in qualifica.
            //
            // In disaccordo non si comanda: si lascia correre e si dice che e successo.
            if (RaceControl != null && RaceControl.FormationPhase != FormationPhase.Idle)
            {
                if (!_sessionMismatchSeen)
                {
                    _sessionMismatchSeen = true;
                    Raise("Sessione incerta: comandi di prove sospesi, la direzione gara dice gara.");
                }
                return commands;
            }

            // Fuori dalla gara il regolamento non blocca il DRS: lo dichiariamo concesso, e che
            // le IA lo aprano solo nel giro buono lo decide il comando di ogni vettura.
            Drs.Update(false, 0, RaceControl != null ? RaceControl.Status : RaceControlStatus.Inactive);

            if (!Championship.Rules.OutLap.Enabled) return commands;

            OutLap.OutLapSpeedKmh = Championship.Rules.OutLap.OutLapSpeedKmh;
            OutLap.BuildUpSpeedKmh = Championship.Rules.OutLap.BuildUpSpeedKmh;
            OutLap.InLapSpeedKmh = Championship.Rules.OutLap.InLapSpeedKmh;
            OutLap.OffLineMeters = Championship.Rules.OutLap.OffLineMeters;
            OutLap.FlyingLapsPerRun = Championship.Rules.OutLap.FlyingLapsPerRun;
            OutLap.DrsOnlyOnFlyingLap = Championship.Rules.Drs.Enabled &&
                                        Championship.Rules.Drs.OnlyOnFlyingLap;
            OutLap.ManageFuel = Championship.Rules.Fuel.Enabled;

            // Il programma delle uscite. In qualifica si ragiona per manche: ogni manche e una
            // sessione a se, con il suo traffico e la sua corsa al tempo negli ultimi minuti.
            double minute;
            if (Championship.Rules.OutLap.RunPlans && !_planClockBroken)
            {
                // Quanto dura DAVVERO questa sessione, secondo il gioco.
                //
                // Prima si guardava la durata scritta nel regolamento del campionato. Finche le
                // due coincidono non si vede la differenza; quando non coincidono il programma
                // delle uscite si distribuisce su un'ora mentre la sessione ne dura due minuti,
                // e il risultato e che quasi nessuno arriva al suo turno: venti vetture ferme in
                // garage per tutta la sessione. E successo, e da fuori sembra che il programma
                // non funzioni invece che semplicemente guardare l'orologio sbagliato.
                double rimasto = TempoRimasto(frame);
                double durataVera = rimasto > 0
                    ? frame.SessionElapsed.TotalMinutes + rimasto
                    : 0;

                string key;
                double windowMinutes;
                if (Qualifying.Active && Qualifying.Segment > 0)
                {
                    key = CurrentSession.Id + "/" + Qualifying.SegmentName;
                    minute = Qualifying.SegmentElapsedMinutes;

                    // La manche non puo durare piu della sessione che la contiene.
                    windowMinutes = Qualifying.SegmentMinutes;
                    if (durataVera > 1 && durataVera < windowMinutes) windowMinutes = durataVera;
                }
                else
                {
                    key = CurrentSession.Id;
                    minute = frame.SessionElapsed.TotalMinutes;

                    windowMinutes = durataVera > 1
                        ? durataVera
                        : (CurrentSession.LengthMode == SessionLengthMode.Minutes
                            ? CurrentSession.Length
                            : 60);
                }

                var names = frame.Opponents
                    .Where(o => !string.IsNullOrWhiteSpace(o.Name) && !SafetyCar.IsSafetyCar(o.Name))
                    .Where(o => !o.IsPlayer || Autopilot)
                    .Select(o => o.Name)
                    .ToList();

                int planSeed = CurrentRound != null ? CurrentRound.Seed : 1;
                OutLap.EnsurePlan(key, windowMinutes, names, Qualifying.Active, planSeed);

                // Il programma dipende interamente dal cronometro della sessione. Se quello non
                // avanza - e succede: ci sono configurazioni in cui arriva solo il tempo che
                // resta - il minuto resterebbe zero per sempre e nessuno uscirebbe mai dai box.
                // Dopo un minuto e mezzo senza che avanzi si butta via il programma: una pista
                // troppo affollata e un difetto, venti vetture inchiodate in garage e un guasto.
                if (minute > _lastPlanMinute + 0.005)
                {
                    _lastPlanMinute = minute;
                    _planMinuteAt = DateTime.UtcNow;
                }
                else if (_planMinuteAt != DateTime.MinValue &&
                         (DateTime.UtcNow - _planMinuteAt).TotalSeconds > 90)
                {
                    _planClockBroken = true;
                    OutLap.Reset();
                    Raise("Programma uscite sospeso: il tempo di sessione non avanza.");
                    minute = -1;
                }
            }
            else
            {
                OutLap.Reset();
                minute = -1;
            }

            // Quanta acqua c'e in pista adesso.
            //
            // Il programma delle uscite sceglie le mescole da asciutto - morbida per il giro
            // secco, dura per il passo gara - ma se piove quella scelta non vale piu niente:
            // nessuno esce con le slick sul bagnato, e una IA che lo fa gira otto secondi al
            // giro piu piano e finisce fuori alla prima curva.
            double acqua = CurrentSession.Weather != null
                ? CurrentSession.Weather.SampleAt(frame.SessionElapsed.TotalMinutes).Wetness
                : 0;

            foreach (var opponent in frame.Opponents)
            {
                if (opponent.IsPlayer && !Autopilot) continue;
                if (string.IsNullOrWhiteSpace(opponent.Name)) continue;
                if (SafetyCar.IsSafetyCar(opponent.Name)) continue;

                var driver = Championship.MatchTelemetryName(opponent.Name);
                var command = new AiCommand
                {
                    Name = opponent.Name,
                    AiLevel = driver != null ? driver.AiSkill : 95,
                    Aggression = driver != null ? driver.AiAggression : 50
                };

                // Chi e stato escluso dalla qualifica non gira piu: rientra e resta dentro.
                if (Qualifying.Active && Qualifying.IsEliminated(opponent.Name))
                {
                    command.LapKind = LapKind.InLap.ToString();
                    bool inPit = opponent.InPitLane || opponent.InPit;
                    command.Pit = !inPit;
                    command.HoldInPit = inPit;
                    command.TopSpeedKmh = inPit ? 0 : OutLap.InLapSpeedKmh;
                    command.SplineOffset = 0;
                    commands.Add(command.Serialise());
                    continue;
                }

                // La vettura del giocatore sta fuori dal programma delle uscite.
                //
                // In pista ci si va quando si vuole: il programma dice cosa fanno gli altri. Ma
                // non basta non tenerla ferma - cosi facendo, a turno chiuso, il programma la
                // mandava in corsia box; appena dentro non era piu trattenuta, usciva, e subito
                // le si diceva di rientrare. Entrava e usciva all'infinito, che e esattamente
                // quello che si e visto in Q1.
                //
                // Fuori dal suo turno non le si dice niente e si guida come si vuole.
                if (opponent.IsPlayer && minute >= 0 && OutLap.HasPlan &&
                    !OutLap.ScheduledOut(opponent.Name, minute))
                    continue;

                if (!OutLap.Apply(opponent, command, minute)) continue;

                if (opponent.IsPlayer) command.HoldInPit = false;

                // Sul bagnato comanda l'acqua, non il programma.
                if (acqua >= 0.10) command.Compound = Compounds.ForConditions(acqua).Code;

                commands.Add(command.Serialise());
            }

            return commands;
        }

        private void UpdateAi(TelemetryFrame frame)
        {
            if (Championship == null || CurrentSession == null) { AiCommands = new List<string>(); return; }

            bool isRace = CurrentSession.Kind == SessionKind.Race || CurrentSession.Kind == SessionKind.Sprint;
            if (!isRace) { AiCommands = BuildOutLapCommands(frame); return; }

            if (!_aiPlanned && frame.Opponents.Count > 1)
            {
                int laps = frame.TotalLaps > 0
                    ? frame.TotalLaps
                    : CurrentSession.ResolveLaps(Championship.CircuitOf(CurrentRound));

                Ai.BeginRace(Championship, CurrentRound, CurrentSession,
                             Championship.CircuitOf(CurrentRound), frame.Opponents, laps);
                _aiPlanned = true;

                foreach (var line in Ai.Summary().Take(4)) Raise("Strategia IA - " + line);
            }

            var weather = CurrentSession.Weather != null
                ? CurrentSession.Weather.SampleAt(frame.SessionElapsed.TotalMinutes)
                : null;

            var commands = Ai.Update(frame, RaceControl, weather, Autopilot);
            ApplyRaceDrsAndFuel(frame, commands);
            commands.Add(SafetyCarCommand(frame));

            AiCommands = commands.Select(c => c.Serialise()).ToList();
        }

        /// <summary>
        /// Il DRS e la benzina in gara.
        ///
        /// Il DRS e un permesso unico per tutti, come in televisione: "DRS abilitato" arriva
        /// quando il leader comincia il terzo giro, e sparisce appena la gara non e piu verde.
        /// Il secondo di distacco lo misura l'app in pista, che e la sola a sapere dove sono i
        /// punti di rilevamento.
        ///
        /// La benzina invece si tocca una volta sola, in griglia. Cambiarla in pista annulla il
        /// giro, e comunque in Formula 1 non si rifornisce: quello che si imbarca prima del via
        /// e quello con cui si arriva alla fine.
        /// </summary>
        private void ApplyRaceDrsAndFuel(TelemetryFrame frame, List<AiCommand> commands)
        {
            int leaderLaps = frame.CompletedLaps;
            foreach (var o in frame.Opponents)
                if (o.CompletedLaps > leaderLaps) leaderLaps = o.CompletedLaps;

            Drs.Update(true, leaderLaps, RaceControl.Status);

            // Prima del via, e solo prima del via, si decide quanta benzina portare.
            //
            // Non basta guardare le fasi del giro di formazione: una gara con partenza lanciata
            // o senza ricognizione non le attraversa, e il rifornimento non avverrebbe mai. La
            // condizione vera e che la gara non sia ancora cominciata; che la vettura sia ferma
            // in piazzola lo controlla l'app in pista, che la vede.
            bool onGrid = RaceControl.FormationPhase == FormationPhase.Rolling
                       || RaceControl.FormationPhase == FormationPhase.Forming
                       || RaceControl.FormationPhase == FormationPhase.Lights
                       || leaderLaps == 0;

            double fuelLaps = 0;
            if (Championship.Rules.Fuel.Enabled && onGrid)
            {
                int total = frame.TotalLaps > 0
                    ? frame.TotalLaps
                    : CurrentSession.ResolveLaps(Championship.CircuitOf(CurrentRound));

                // Un giro in piu del previsto: il giro di ricognizione la benzina la consuma,
                // e in griglia si sta accesi ad aspettare il semaforo.
                if (total > 0) fuelLaps = total + 1;
            }

            // La gomma del via.
            //
            // La strategia sceglie le mescole prima della gara, quando il cielo poteva essere
            // un altro. Se al momento di partire c'e acqua in pista, quella scelta si butta:
            // partire con le slick sotto la pioggia non lo fa nessuno, e vedere venti IA che
            // lo fanno toglie credibilita a tutto il resto.
            string gommaDelVia = "";
            if (onGrid)
            {
                var cielo = CurrentSession.Weather != null
                    ? CurrentSession.Weather.SampleAt(frame.SessionElapsed.TotalMinutes)
                    : null;
                double acqua = cielo != null ? cielo.Wetness : 0;
                if (acqua >= 0.10) gommaDelVia = Compounds.ForConditions(acqua).Code;
            }

            foreach (var command in commands)
            {
                if (command == null) continue;
                command.DrsAllowed = Drs.EnabledNow;
                if (fuelLaps > 0) command.FuelLaps = fuelLaps;
                if (gommaDelVia != "") command.Compound = gommaDelVia;
            }
        }

        /// <summary>
        /// Il comando per la vettura di sicurezza. Ferma ai box per quasi tutta la gara, in
        /// pista solo quando la direzione gara la manda, e di nuovo ai box alla ripartenza.
        /// </summary>
        private AiCommand SafetyCarCommand(TelemetryFrame frame)
        {
            var settings = RaceControl.SafetyCarSettings;
            var phase = RaceControl.SafetyCarPhase;

            var command = new AiCommand
            {
                Name = SafetyCar.DriverName,
                IsSafetyCar = true,
                Phase = phase.ToString(),
                AiLevel = 100,
                Aggression = 0
            };

            switch (phase)
            {
                case SafetyCarPhase.Idle:
                    // Tenuta ferma nella sua piazzola: il comando va ripetuto, altrimenti riparte.
                    command.TopSpeedKmh = 1;
                    command.StopSeconds = 5;
                    break;
                case SafetyCarPhase.Restarting:
                    command.TopSpeedKmh = settings.PitInKmh;
                    break;
                default:
                    command.TopSpeedKmh = settings.PaceKmh;
                    break;
            }

            return command;
        }

        /// <summary>
        /// Ricalcola gli scenari di strategia una volta per giro. Piu spesso non servirebbe, e
        /// vedere le opzioni ballare mentre si guida sarebbe solo confusione.
        /// </summary>
        private void UpdateStrategy(TelemetryFrame frame)
        {
            if (CurrentSession == null) return;
            bool isRace = CurrentSession.Kind == SessionKind.Race || CurrentSession.Kind == SessionKind.Sprint;
            if (!isRace) return;
            if (Strategy.LastBuiltLap == frame.CompletedLaps) return;

            string before = Strategy.Options.FirstOrDefault(o => o.Recommended)?.Name ?? "";
            Strategy.Rebuild(frame, Tyres, CurrentSession.Weather, State.Car.AveragePaceSeconds);
            var now = Strategy.Options.FirstOrDefault(o => o.Recommended);

            // L'ingegnere parla solo quando il consiglio cambia davvero: ripetere ogni giro la
            // stessa strategia sarebbe rumore, e il muretto vero non lo fa.
            if (now != null && now.Name != before && Strategy.Options.Count > 1)
            {
                string text = "Strategia: " + string.Join(", ",
                    Strategy.Options.Select(o => "scenario " + o.Id + " " + o.Name.ToLowerInvariant()));
                text += ". Consiglio " + now.Id + ".";

                // Nome completo: la proprieta Engineer nasconde l'omonimo spazio dei nomi.
                Engineer.Radio.Enqueue(new Pitwall.Engineer.EngineerMessage
                {
                    RuleKey = "strategy.options",
                    Text = text,
                    Category = Pitwall.Engineer.RadioCategory.Strategy,
                    Priority = 2,
                    Lap = frame.CompletedLaps,
                    DisplaySeconds = 9
                }, 90);
            }
        }

        private readonly Dictionary<string, int> _tyreAgeBase =
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        private void TrackOpponentDetails(TelemetryFrame frame)
        {
            foreach (var o in frame.Opponents)
            {
                if (string.IsNullOrWhiteSpace(o.Name)) continue;
                _pitCounts[o.Name] = Math.Max(o.PitCount, _pitCounts.TryGetValue(o.Name, out int n) ? n : 0);

                if (!string.IsNullOrWhiteSpace(o.TyreCompound))
                {
                    if (!_compounds.TryGetValue(o.Name, out var list))
                        _compounds[o.Name] = list = new List<string>();
                    string code = Compounds.FromGameName(o.TyreCompound).Code;
                    if (list.Count == 0 || list[list.Count - 1] != code) list.Add(code);
                }
            }
        }

        // ---- Commands ---------------------------------------------------------------------------------

        private void HandleCommands()
        {
            var commands = LiveBridge.TakeCommands();
            foreach (var command in commands)
            {
                switch (command.Name)
                {
                    case CommandNames.SelectChampionship:
                        LoadChampionship(command.Argument);
                        break;
                    case CommandNames.Reload:
                        if (Championship != null) LoadChampionship(Championship.Id);
                        else LoadMostRecent();
                        break;
                    case CommandNames.DeployVsc:
                        RaceControl.DeployVirtualSafetyCar("Richiesta manuale", _lastFrame);
                        break;
                    case CommandNames.DeploySafetyCar:
                        RaceControl.DeploySafetyCar("Richiesta manuale", _lastFrame);
                        break;
                    case CommandNames.EndNeutralisation:
                        RaceControl.EndNeutralisation(_lastFrame);
                        break;
                    case CommandNames.ResumeGreen:
                        RaceControl.ResumeGreen(_lastFrame);
                        break;
                    case CommandNames.RedFlag:
                        RaceControl.DeployRedFlag("Richiesta manuale", _lastFrame);
                        break;
                    case CommandNames.SaveResult:
                        FinishSession(_lastFrame);
                        break;
                    case CommandNames.ClearPenalties:
                        RaceControl.Penalties.Clear();
                        break;
                    case CommandNames.MuteEngineer:
                        Engineer.Rules.Enabled = Math.Abs(command.Value) < 0.5;
                        break;
                    case CommandNames.ToggleAutopilot:
                        Autopilot = string.IsNullOrEmpty(command.Argument)
                            ? !Autopilot
                            : command.Argument == "1";
                        Raise(Autopilot
                            ? "Pilota automatico inserito: guida l'IA, tu guardi."
                            : "Pilota automatico disinserito: la vettura torna tua.");
                        break;
                    case CommandNames.ReportGrid:
                        if (RaceControl != null)
                            RaceControl.ReportGrid(command.Argument == "1", command.Value);
                        break;
                    case CommandNames.SetWeather:
                        ApplyWeatherOverride(command.Argument, command.Value);
                        break;
                    case CommandNames.SelectStrategy:
                        if (Strategy.Select(command.Argument))
                        {
                            var chosen = Strategy.Active;
                            Raise("Strategia scelta: " + (chosen != null ? chosen.ToString() : command.Argument));
                            StrategyChosen?.Invoke(chosen);
                        }
                        break;
                }
                // La posizione in griglia arriva di continuo mentre ti sistemi: scriverla nel
                // registro lo riempirebbe di righe identiche.
                if (command.Name != CommandNames.ReportGrid)
                    Raise("Comando ricevuto: " + command.Name);
            }
        }

        /// <summary>
        /// Cambia il tempo su richiesta della direzione gara.
        ///
        /// Il piano riscritto finisce nello stato pubblicato, e da li lo legge il controller
        /// meteo di Custom Shaders Patch: e quello che fa cambiare davvero il cielo in pista,
        /// non solo il radar. Senza controller attivo cambia solo la previsione, e il radar lo
        /// dice da solo.
        /// </summary>
        private void ApplyWeatherOverride(string skyName, double transitionMinutes)
        {
            if (CurrentSession == null) { Raise("Meteo: nessuna sessione in corso."); return; }

            SkyCondition sky;
            try { sky = (SkyCondition)Enum.Parse(typeof(SkyCondition), (skyName ?? "").Trim(), true); }
            catch { Raise("Meteo: condizione non riconosciuta '" + skyName + "'."); return; }

            if (CurrentSession.Weather == null)
                CurrentSession.Weather = new WeatherPlan();

            double minute = _lastFrame != null ? _lastFrame.SessionElapsed.TotalMinutes : 0;
            var rules = Championship != null ? Championship.Rules.Weather : null;
            var target = WeatherGenerator.Override(CurrentSession.Weather, minute, sky, transitionMinutes, rules);
            if (target == null) return;

            Raise(transitionMinutes >= 1
                ? "Meteo: " + target.Label + " fra " + transitionMinutes.ToString("0") + " minuti."
                : "Meteo: " + target.Label + ".");
        }

        // ---- Live state --------------------------------------------------------------------------------

        private void BuildLiveState(TelemetryFrame frame)
        {
            var state = State;
            state.PluginRunning = true;
            state.GameRunning = frame.GameRunning;

            state.ChampionshipId = Championship != null ? Championship.Id : "";
            state.ChampionshipName = Championship != null ? Championship.Name : "Nessun campionato";
            state.RoundId = CurrentRound != null ? CurrentRound.Id : "";
            state.RoundName = CurrentRound != null ? CurrentRound.Name : "Sessione libera";
            state.RoundMatched = CurrentRound != null;
            state.CircuitName = string.IsNullOrEmpty(frame.TrackName) ? frame.TrackId : frame.TrackName;
            state.SessionKind = CurrentSession != null ? CurrentSession.Kind.ToString() : "";
            state.SessionLabel = CurrentSession != null ? SessionLabel(CurrentSession.Kind) : frame.SessionTypeName;

            state.LapCurrent = frame.CompletedLaps + 1;
            state.LapsTotal = frame.TotalLaps > 0 ? frame.TotalLaps
                : (CurrentSession != null && Championship != null
                    ? CurrentSession.ResolveLaps(Championship.CircuitOf(CurrentRound)) : 0);
            state.LapsRemaining = state.LapsTotal > 0 ? Math.Max(0, state.LapsTotal - frame.CompletedLaps) : 0;
            // Zero vuol dire "a giri, nessun limite di tempo": meglio di novecento miliardi,
            // che l'HUD mostrava per intero.
            state.SessionTimeLeftSeconds = TempoRimasto(frame) * 60.0;
            state.SessionElapsedSeconds = frame.SessionElapsed.TotalSeconds;

            var seat = Championship != null ? Championship.PlayerDriver : null;
            var seatTeam = seat != null && Championship != null ? Championship.TeamById(seat.TeamId) : null;
            state.PlayerName = seat != null ? seat.FullName : frame.PlayerName;
            state.PlayerTeam = seatTeam != null ? seatTeam.Name : "";
            state.PlayerReplaced = seat != null ? seat.ReplacedName : "";
            state.Position = frame.Position;
            state.LastLap = Lap(frame.LastLapTime);
            state.BestLap = Lap(frame.BestLapTime);

            var ahead = frame.Opponents.FirstOrDefault(o => !o.IsPlayer && o.Position == frame.Position - 1);
            var behind = frame.Opponents.FirstOrDefault(o => !o.IsPlayer && o.Position == frame.Position + 1);
            state.NameAhead = ahead != null ? ahead.Name : "";
            state.NameBehind = behind != null ? behind.Name : "";
            state.GapAheadSeconds = ahead != null && ahead.GapToPlayer.HasValue ? Math.Abs(ahead.GapToPlayer.Value) : 0;
            state.GapBehindSeconds = behind != null && behind.GapToPlayer.HasValue ? Math.Abs(behind.GapToPlayer.Value) : 0;

            state.Fuel = frame.Fuel;
            state.FuelLaps = frame.EstimatedFuelLaps ?? 0;
            state.TyreCompound = Tyres.Compound.Name;
            state.TyreCompoundCode = Tyres.Compound.Code;
            state.TyreWearPercent = Math.Round(Tyres.WearPercent, 1);
            state.TyreTemperature = Math.Round(Tyres.TemperatureAvg, 1);
            state.LapsToCliff = Math.Round(Tyres.LapsToCliff, 1);
            state.PitStops = Tyres.PitStops;
            state.StintLaps = Tyres.StintLaps;

            state.RaceControlStatus = RaceControl.Status.ToString();
            state.RaceControlText = RaceControl.StatusText;
            state.Delta = Math.Round(RaceControl.Delta, 2);
            state.TargetSpeedKmh = Math.Round(RaceControl.TargetSpeedKmh, 1);
            state.PitLaneClosed = RaceControl.PitLaneClosed;
            state.SafetyCarPeriods = RaceControl.SafetyCarPeriods;
            state.VirtualSafetyCarPeriods = RaceControl.VirtualSafetyCarPeriods;
            state.YellowSectors = RaceControl.YellowSectors.OrderBy(s => s).ToList();
            state.SafetyCarPhase = RaceControl.SafetyCarPhase.ToString();
            state.FormationPhase = RaceControl.FormationPhase.ToString();
            state.Autopilot = Autopilot;
            state.LightsOn = RaceControl.LightsOn;
            state.PitLaneStart = RaceControl.PitLaneStart;
            state.DrsEnabledNow = Drs.EnabledNow;
            state.DrsReason = Drs.Reason;

            var drsRules = Championship != null ? Championship.Rules.Drs : new Rules.DrsRules();
            var fuelRules = Championship != null ? Championship.Rules.Fuel : new Rules.FuelRules();
            // La regola del secondo vale solo in gara. In prove e qualifica il DRS in zona e
            // libero per regolamento, e chiederlo renderebbe impossibile un giro da soli.
            bool racing = CurrentSession != null &&
                          (CurrentSession.Kind == SessionKind.Race || CurrentSession.Kind == SessionKind.Sprint);
            state.DrsDetectionGapSeconds = drsRules.Enabled && racing ? drsRules.DetectionGapSeconds : 0;
            state.FuelRunMargin = fuelRules.RunMargin;
            state.DrsAppliesToPlayer = drsRules.Enabled && drsRules.AppliesToPlayer;
            state.Lingua = Pitwall.Lingue.Testi.Codice;
            state.FuelRaceMargin = fuelRules.RaceMargin;
            state.FuelReserveLitres = fuelRules.ReserveLitres;
            state.GridSlot = RaceControl.GridSlot;
            state.GridNotice = RaceControl.GridNotice;
            state.PositionToGiveBack = RaceControl.PositionToGiveBack;
            state.GiveBackSecondsLeft = RaceControl.GiveBackSecondsLeft;
            state.GridToleranceAlong = RaceControl.FormationSettings.GridToleranceAlong;
            state.GridToleranceAcross = RaceControl.FormationSettings.GridToleranceAcross;
            state.PitLaneStartDelaySeconds = RaceControl.FormationSettings.PitLaneStartDelaySeconds;
            state.AiCommands = AiCommands;
            state.AiStrategy = Ai.Active ? Ai.Summary().ToList() : new List<string>();

            var playerDriver = Championship != null ? Championship.PlayerDriver : null;
            string playerId = playerDriver != null ? playerDriver.Id : "player";
            state.PendingPenaltySeconds = RaceControl.Penalties.PendingSeconds(playerId);
            state.TrackLimitStrikes = RaceControl.Penalties.TrackLimitStrikes(playerId);
            state.Penalties = RaceControl.Penalties.For(playerId)
                .Where(p => p.Type != PenaltyType.Warning)
                .Select(p => p.ShortText + " - " + p.Reason).ToList();
            state.RaceControlFeed = RaceControl.Feed
                .Skip(Math.Max(0, RaceControl.Feed.Count - 8))
                .Select(m => m.Text).ToList();

            state.StrategyAdvice = Engineer.StrategyAdvice;
            state.SelectedStrategy = Strategy.SelectedId;
            state.StrategyOptions = Strategy.Options.Select(o => new StrategyChoice
            {
                Id = o.Id,
                Name = o.Name,
                Summary = o.Summary,
                Stops = o.Stops,
                NextStopLap = o.NextStopLap,
                DeltaSeconds = o.DeltaSeconds,
                ProjectedPosition = o.ProjectedPosition,
                Risk = o.Risk,
                RiskNote = o.RiskNote,
                Recommended = o.Recommended,
                Selected = o.Selected,
                Plan = string.Join("  ", o.Stints.Select(s => s.Describe()))
            }).ToList();
            state.RecommendedCompound = Engineer.RecommendedCompound != null ? Engineer.RecommendedCompound.Name : "";
            if (Engineer.Window != null)
            {
                state.PitWindowEarliest = Engineer.Window.EarliestLap;
                state.PitWindowIdeal = Engineer.Window.IdealLap;
                state.PitWindowLatest = Engineer.Window.LatestLap;
            }

            var current = Engineer.Radio.Current;
            state.EngineerMessage = current != null ? current.Text : "";
            state.EngineerCategory = current != null ? current.Category : "";
            state.EngineerHistory = Engineer.Radio.Recent(8).Select(m => m.Text).ToList();

            if (CurrentSession != null && CurrentSession.Weather != null)
            {
                var snapshot = CurrentSession.Weather.SampleAt(frame.SessionElapsed.TotalMinutes);
                state.WeatherNow = snapshot.Label;
                state.Wetness = Math.Round(snapshot.Wetness, 3);
                state.WeatherForecast = CurrentSession.Weather.Summary;

                var rain = CurrentSession.Weather
                    .Forecast(frame.SessionElapsed.TotalMinutes, 45)
                    .FirstOrDefault(f => f.StartsRain);
                state.MinutesToRain = rain != null ? Math.Round(rain.MinutesFromNow, 1) : -1;

                state.WeatherTimeline = CurrentSession.Weather.Phases
                    .OrderBy(p => p.StartMinute)
                    .Select(p => new WeatherPoint
                    {
                        Minute = Math.Round(p.StartMinute, 2),
                        Type = p.WeatherFxType,
                        Rain = Math.Round(p.RainIntensity, 3),
                        Wetness = Math.Round(p.Wetness, 3),
                        AirTemperature = Math.Round(p.AirTemperature, 1),
                        RoadTemperature = Math.Round(p.RoadTemperature, 1),
                        WindKmh = Math.Round(p.WindSpeedKmh, 1),
                        WindDirection = p.WindDirection,
                        Grip = Math.Round(p.Grip, 3),
                        Label = p.Label
                    })
                    .ToList();
            }

            state.SessionLengthMinutes = Math.Round(SessionMinutes(frame), 1);
            state.AirTemperature = Math.Round(frame.AirTemperature, 1);
            state.RoadTemperature = Math.Round(frame.RoadTemperature, 1);

            BuildTiming(frame, state);
            BuildCar(frame, state);
        }

        private readonly List<double> _lapTimes = new List<double>();
        private int _lastLapRecorded = -1;

        /// <summary>Raccoglie i dati della vettura per il computer di garage.</summary>
        private void BuildCar(TelemetryFrame frame, LiveState state)
        {
            if (frame.CompletedLaps != _lastLapRecorded)
            {
                _lastLapRecorded = frame.CompletedLaps;
                if (frame.LastLapTime > TimeSpan.Zero && frame.LastLapTime.TotalSeconds < 600)
                {
                    _lapTimes.Add(frame.LastLapTime.TotalSeconds);
                    if (_lapTimes.Count > 20) _lapTimes.RemoveAt(0);
                }
            }

            var car = state.Car;
            car.CarId = frame.CarId;
            car.SpeedKmh = Math.Round(frame.SpeedKmh, 1);
            car.Rpm = Math.Round(frame.Rpm);
            car.Gear = frame.Gear;
            car.Throttle = Math.Round(frame.Throttle, 3);
            car.Brake = Math.Round(frame.Brake, 3);

            car.FuelLitres = Math.Round(frame.Fuel, 2);
            car.FuelLapsLeft = Math.Round(frame.EstimatedFuelLaps ?? 0, 1);
            car.FuelPerLap = frame.EstimatedFuelLaps.HasValue && frame.EstimatedFuelLaps.Value > 0.1
                ? Math.Round(frame.Fuel / frame.EstimatedFuelLaps.Value, 2)
                : 0;

            car.WaterTemperature = Math.Round(frame.WaterTemperature, 1);
            car.OilTemperature = Math.Round(frame.OilTemperature, 1);
            car.BrakeTemperature = Math.Round(frame.BrakeTemperatureAvg, 1);
            car.DamagePercent = Math.Round(frame.DamageMax * 100, 1);

            car.ErsPercent = Math.Round(frame.ErsPercent, 1);
            car.DrsAvailable = frame.DrsAvailable;
            car.DrsOpen = frame.DrsEnabled;

            car.TyreWear = new[]
            {
                Math.Round(frame.TyreWearFrontLeft, 1),
                Math.Round(frame.TyreWearFrontRight, 1),
                Math.Round(frame.TyreWearRearLeft, 1),
                Math.Round(frame.TyreWearRearRight, 1)
            };
            car.TyrePressure = Math.Round(frame.TyrePressureAvg, 1);
            car.TyreTemperature = Math.Round(frame.TyreTemperatureAvg, 1);

            car.RecentLapSeconds = _lapTimes.AsEnumerable().Reverse().Take(10)
                .Select(v => Math.Round(v, 3)).ToList();

            if (_lapTimes.Count >= 2)
            {
                // Si scartano i giri palesemente fuori ritmo: out lap, in lap, bandiere.
                double best = _lapTimes.Min();
                var clean = _lapTimes.Where(v => v < best * 1.07).ToList();
                car.AveragePaceSeconds = Math.Round(clean.Average(), 3);
                car.PaceDeltaSeconds = Math.Round(car.AveragePaceSeconds - best, 3);
            }
        }

        /// <summary>
        /// Quanto manca alla fine della sessione, in minuti, **se il numero ha senso**.
        ///
        /// Restituisce zero quando non si sa, e quello zero va trattato come "non lo so", non come
        /// "e finita". Il controllo e necessario perche Assetto Corsa non dice "nessun limite di
        /// tempo": mette una sentinella enorme. In una gara a giri qui arrivava 922.337.203.685
        /// secondi, cioe TimeSpan.MaxValue, e il vecchio controllo - maggiore di 0,1 - la
        /// prendeva per buona.
        ///
        /// Il danno era silenzioso e grosso: il programma delle uscite si distribuiva su
        /// quindici miliardi di minuti, quindi nessuna vettura arrivava mai al suo turno e
        /// restavano tutte in garage per tutta la sessione; e le manche di qualifica, dovendo
        /// starci dentro, non venivano mai accorciate e la Q1 non scadeva. Da fuori sembravano
        /// due difetti diversi, ed era lo stesso numero.
        ///
        /// Il tetto e sei ore: piu di cosi non dura nessuna sessione di un fine settimana, e
        /// qualunque cosa oltre quel valore e una sentinella, non una durata.
        /// </summary>
        private static double TempoRimasto(TelemetryFrame frame)
        {
            double minuti = frame.SessionTimeLeft.TotalMinutes;
            return minuti > 0.1 && minuti < 360 ? minuti : 0;
        }

        /// <summary>
        /// Durata prevista della sessione in minuti. Serve a scalare il radar meteo: una
        /// sessione a tempo la sa gia, una a giri si stima dal giro di riferimento.
        /// </summary>
        private double SessionMinutes(TelemetryFrame frame)
        {
            double rimasto = TempoRimasto(frame);
            if (rimasto > 0)
                return frame.SessionElapsed.TotalMinutes + rimasto;

            var circuit = Championship != null ? Championship.CircuitOf(CurrentRound) : null;
            double lapSeconds = circuit != null && circuit.ReferenceLapSeconds > 5 ? circuit.ReferenceLapSeconds : 95;
            int laps = frame.TotalLaps > 0 ? frame.TotalLaps : 0;
            if (laps > 0) return laps * lapSeconds / 60.0;

            return Math.Max(30, frame.SessionElapsed.TotalMinutes + 15);
        }

        private void BuildTiming(TelemetryFrame frame, LiveState state)
        {
            var racers = frame.Opponents
                .Where(o => !SafetyCar.IsSafetyCar(o.Name))
                .OrderBy(o => o.Position <= 0 ? int.MaxValue : o.Position)
                .ToList();

            // Le posizioni si rinumerano da uno.
            //
            // Assetto Corsa conta anche la vettura di sicurezza come un'iscritta, e siccome sta
            // in testa si prende la posizione uno: la classifica partiva da due e arrivava a
            // ventuno, senza nessun leader. La safety car non e in gara, quindi non occupa una
            // posizione, e la classifica deve leggersi da uno a venti come su qualunque monitor.
            var placeOf = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < racers.Count; i++) placeOf[racers[i].Name] = i + 1;

            // I migliori della sessione: servono a colorare i settori e i giri, come sui monitor
            // veri, dove il viola si vede prima ancora di leggere il numero.
            double bestS1 = BestOf(racers, o => o.BestSector1);
            double bestS2 = BestOf(racers, o => o.BestSector2);
            double bestS3 = BestOf(racers, o => o.BestSector3);
            var bestLap = racers.Where(o => o.BestLapTime > TimeSpan.Zero)
                                .Select(o => o.BestLapTime)
                                .DefaultIfEmpty(TimeSpan.Zero).Min();

            var rows = new List<LiveStandingRow>();
            OpponentFrame previous = null;

            foreach (var o in racers)
            {
                var driver = Championship != null ? Championship.MatchTelemetryName(o.Name) : null;
                var team = driver != null && Championship != null ? Championship.TeamById(driver.TeamId) : null;

                // Eta delle gomme: giri fatti da quando e rientrato ai box l'ultima volta.
                if (!_tyreAgeBase.TryGetValue(o.Name, out int ageBase)) { ageBase = 0; _tyreAgeBase[o.Name] = 0; }
                if (o.InPitLane) _tyreAgeBase[o.Name] = o.CompletedLaps;

                var row = new LiveStandingRow
                {
                    Position = placeOf[o.Name],
                    Name = driver != null ? driver.FullName : o.Name,
                    Abbreviation = driver != null ? driver.Abbreviation : Initials(o.Name),
                    TeamName = team != null ? team.Name : o.TeamName,
                    Color = team != null ? team.Color : "#888888",
                    Laps = o.CompletedLaps,
                    Gap = FormatGap(o, placeOf[o.Name]),
                    Interval = FormatInterval(o, previous),
                    BestLap = Lap(o.BestLapTime),
                    LastLap = Lap(o.LastLapTime),
                    S1 = Sector(o.LastSector1),
                    S2 = Sector(o.LastSector2),
                    S3 = Sector(o.LastSector3),
                    S1Rank = Rank(o.LastSector1, o.BestSector1, bestS1),
                    S2Rank = Rank(o.LastSector2, o.BestSector2, bestS2),
                    S3Rank = Rank(o.LastSector3, o.BestSector3, bestS3),
                    LastLapRank = RankLap(o.LastLapTime, o.BestLapTime, bestLap),
                    Compound = string.IsNullOrEmpty(o.TyreCompound) ? "" : Compounds.FromGameName(o.TyreCompound).Code,
                    TyreAge = Math.Max(0, o.CompletedLaps - _tyreAgeBase[o.Name]),
                    PitStops = o.PitCount,
                    InPit = o.InPitLane || o.InPit,
                    IsPlayer = o.IsPlayer,
                    PositionChange = o.StartPosition.HasValue && o.Position > 0
                        ? o.StartPosition.Value - o.Position
                        : 0
                };

                rows.Add(row);
                previous = o;
            }

            state.Timing = rows;

            // Qualifiche a eliminazione: la classifica per tempo decide chi e in zona rossa.
            // Stessa controprova dei comandi: se la direzione gara sta conducendo una partenza,
            // allora siamo in gara, e le qualifiche a eliminazione si spengono. Senza questa
            // riga a Melbourne l'HUD ha mostrato "Q1, passano in 15" per mezza gara.
            if (Qualifying.Active && RaceControl != null && RaceControl.FormationPhase != FormationPhase.Idle)
                Qualifying.Suspend();

            if (Qualifying.Active)
            {
                // Quanto dura la sessione secondo il gioco, non secondo il campionato. Le manche
                // si riportano dentro questa, perche e quella che scade davvero.
                double rimastoQ = TempoRimasto(frame);
                if (rimastoQ > 0)
                {
                    Qualifying.Observe(frame.SessionElapsed.TotalMinutes + rimastoQ);
                }

                var byTime = rows.Select(r => r.Name).ToList();
                Qualifying.Update(frame.SessionElapsed.TotalSeconds, byTime);

                state.QualifyingSegment = Qualifying.SegmentName;
                state.QualifyingSecondsLeft = Qualifying.SecondsLeft;
                state.QualifyingSurviving = Qualifying.Surviving;

                var zone = new HashSet<string>(Qualifying.DropZone(byTime), StringComparer.OrdinalIgnoreCase);
                foreach (var row in rows)
                {
                    row.Eliminated = Qualifying.IsEliminated(row.Name);
                    row.InDropZone = !row.Eliminated && zone.Contains(row.Name);
                }
            }
            else
            {
                // Si cancella quello che resta della qualifica.
                //
                // Lo stato pubblicato e un oggetto solo, riusato a ogni fotogramma: qui dentro i
                // valori si scrivevano ma non si toglievano mai. Finita la qualifica, "Q1" e il
                // suo conto alla rovescia restavano appiccicati per tutto il fine settimana, e in
                // gara l'HUD diceva "Q1, passano in 15" mentre si correva. Non era un errore di
                // riconoscimento della sessione: era roba vecchia che nessuno cancellava.
                state.QualifyingSegment = "";
                state.QualifyingSecondsLeft = 0;
                state.QualifyingSurviving = 0;
            }
        }

        private static double BestOf(List<OpponentFrame> cars, Func<OpponentFrame, double?> pick)
        {
            var values = cars.Select(pick).Where(v => v.HasValue && v.Value > 0).Select(v => v.Value).ToList();
            return values.Count > 0 ? values.Min() : 0;
        }

        /// <summary>0 tempo normale, 1 personale migliore, 2 miglior tempo della sessione.</summary>
        private static int Rank(double? last, double? personalBest, double sessionBest)
        {
            if (!last.HasValue || last.Value <= 0) return 0;
            if (sessionBest > 0 && last.Value <= sessionBest + 0.001) return 2;
            if (personalBest.HasValue && personalBest.Value > 0 && last.Value <= personalBest.Value + 0.001) return 1;
            return 0;
        }

        private static int RankLap(TimeSpan last, TimeSpan personalBest, TimeSpan sessionBest)
        {
            if (last <= TimeSpan.Zero) return 0;
            if (sessionBest > TimeSpan.Zero && last <= sessionBest) return 2;
            if (personalBest > TimeSpan.Zero && last <= personalBest) return 1;
            return 0;
        }

        private static string Sector(double? seconds)
        {
            if (!seconds.HasValue || seconds.Value <= 0) return "";
            return seconds.Value.ToString("0.000", CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// L'intervallo sulla vettura davanti, che e il numero che conta quando si attacca:
        /// il distacco dal leader dice la classifica, questo dice se puoi provarci.
        /// </summary>
        private static string FormatInterval(OpponentFrame car, OpponentFrame ahead)
        {
            if (ahead == null) return "";
            if (car.LapsToLeader.HasValue && ahead.LapsToLeader.HasValue &&
                car.LapsToLeader.Value > ahead.LapsToLeader.Value)
                return "+" + (car.LapsToLeader.Value - ahead.LapsToLeader.Value) + "L";

            if (car.GapToLeader.HasValue && ahead.GapToLeader.HasValue)
            {
                double delta = car.GapToLeader.Value - ahead.GapToLeader.Value;
                if (delta >= 0) return Signed(delta);
            }
            return "";
        }

        /// <summary>
        /// Il distacco dal leader, come si scrive su un monitor di gara.
        ///
        /// Il segno si prende dal numero e non si mette a mano. Scrivere \"+\" davanti a un
        /// valore che puo essere negativo dava \"+-0.098\", che non vuol dire niente; e succede
        /// davvero, perche nei primi istanti di una gara i distacchi che arrivano dal gioco
        /// possono essere negativi.
        /// </summary>
        private static string FormatGap(OpponentFrame o, int place)
        {
            if (place == 1) return "leader";

            if (o.LapsToLeader.HasValue && o.LapsToLeader.Value > 0)
                return "+" + o.LapsToLeader.Value + (o.LapsToLeader.Value == 1 ? " giro" : " giri");

            if (o.GapToLeader.HasValue) return Signed(o.GapToLeader.Value);
            return "";
        }

        /// <summary>Un distacco col suo segno, mai due.</summary>
        private static string Signed(double seconds) =>
            (seconds < 0 ? "-" : "+") + Math.Abs(seconds).ToString("0.000", CultureInfo.InvariantCulture);

        private static string Initials(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "";
            var parts = name.Trim().Split(' ');
            string last = parts[parts.Length - 1];
            return last.Length >= 3 ? last.Substring(0, 3).ToUpperInvariant() : last.ToUpperInvariant();
        }

        private static string Lap(TimeSpan t)
        {
            if (t <= TimeSpan.Zero) return "--:--.---";
            return t.Minutes.ToString("0") + ":" + t.Seconds.ToString("00") + "." +
                   t.Milliseconds.ToString("000");
        }

        /// <summary>Italian label for a session kind, used on overlays.</summary>
        public static string SessionLabel(SessionKind kind)
        {
            switch (kind)
            {
                case SessionKind.Practice1: return Testi.T("sessione.prove1");
                case SessionKind.Practice2: return Testi.T("sessione.prove2");
                case SessionKind.Practice3: return Testi.T("sessione.prove3");
                case SessionKind.Qualifying: return Testi.T("sessione.qualifica");
                case SessionKind.SprintQualifying: return Testi.T("sessione.qualifica.sprint");
                case SessionKind.Sprint: return Testi.T("sessione.sprint");
                default: return Testi.T("sessione.gara");
            }
        }

        private void Raise(string message) => Log?.Invoke(message);
    }
}
