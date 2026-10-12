using System;
using System.Collections.Generic;
using System.Linq;
using Pitwall.Live;
using Pitwall.Model;
using Pitwall.Rules;

using Pitwall.Lingue;

namespace Pitwall.RaceControl
{
    public enum RaceControlStatus
    {
        Inactive,
        FormationLap,
        Green,
        LocalYellow,
        VirtualSafetyCar,
        SafetyCar,
        SafetyCarEnding,
        RedFlag,
        Checkered
    }

    /// <summary>A line on the race control feed.</summary>
    public sealed class RaceControlMessage
    {
        public DateTime Utc { get; set; } = DateTime.UtcNow;
        public string Text { get; set; } = "";
        public string Category { get; set; } = "RaceControl";
        /// <summary>0 routine, 1 notable, 2 urgent.</summary>
        public int Priority { get; set; } = 1;
        public int Lap { get; set; }

        public override string ToString() => Text;
    }

    /// <summary>
    /// Race control for a session: flags, virtual safety car, safety car, red flags and the
    /// penalties that come out of them.
    ///
    /// Assetto Corsa has no safety car of its own in single player, so Pitwall runs the
    /// procedure itself: it tells the driver what the delta has to be, checks that it is
    /// respected, and freezes the order for the classification it publishes at the end. The
    /// optional in-game Lua companion can additionally slow the AI down so the field genuinely
    /// bunches up; without it, the AI keeps racing and only the player is held to the rules.
    /// </summary>
    public sealed class RaceDirector
    {
        private readonly Random _random;
        private readonly IncidentDetector _detector = new IncidentDetector();

        private Circuit _circuit = new Circuit();
        private SessionConfig _session = new SessionConfig();
        private RaceControlRules _rules = new RaceControlRules();
        private SafetyCarSettings _safetyCar = new SafetyCarSettings();
        private FormationLapSettings _formation = new FormationLapSettings();
        private double _formationTimer;
        private double _lightsDelay;

        /// <summary>Da quanti secondi si sta girando in ricognizione.</summary>
        private double _rollingSeconds;

        /// <summary>Quando e scattato il verde. Negativo finche non e scattato.</summary>
        private double _greenSeconds = -1;

        private double _sessionSeconds;
        private double _periodStartSeconds;
        private double _periodStartDistance;
        private int _periodStartLap;
        private int _periodStartPosition;
        private double _breachSeconds;
        private bool _deltaPenaltyIssued;
        private int _overtakePenalties;
        private double _vscGreenAt = -1;
        private double _lastHazardSeconds = -9999;
        private bool _pitSpeedingFlagged;
        private bool _wasInPitLane;
        private double _pitBoxStationarySeconds;
        private bool _pitStopChecked;
        private int _safetyCarReturnLap = -1;
        private bool _safetyCarObserved;

        public RaceDirector(int seed = 0)
        {
            _random = new Random(seed == 0 ? Environment.TickCount : seed);
        }

        // ---- State -------------------------------------------------------------------------

        public RaceControlStatus Status { get; private set; } = RaceControlStatus.Inactive;

        public PenaltyBook Penalties { get; } = new PenaltyBook();

        public List<RaceControlMessage> Feed { get; } = new List<RaceControlMessage>();

        public List<Incident> Incidents { get; } = new List<Incident>();

        /// <summary>Marshalling sectors currently under local yellow, 1 based.</summary>
        /// <summary>Fino a quando resta gialla ogni settore, in secondi di sessione.</summary>
        private readonly Dictionary<int, double> _yellowUntil = new Dictionary<int, double>();

        /// <summary>
        /// Coda prudenziale della gialla dopo l'ultima evidenza del pericolo (simulazione).
        /// Un ostacolo ancora osservato rinnova la scadenza a ogni frame.
        /// </summary>
        private const double YellowSeconds = 5;

        /// <summary>I settori sotto bandiera gialla adesso.</summary>
        public ICollection<int> YellowSectors => _yellowUntil.Keys;

        /// <summary>
        /// How far ahead of the minimum time the player is under VSC or safety car, in seconds.
        /// Positive means compliant, negative means going too fast. This is the number to put
        /// on the wheel display, exactly like the delta bar of the official games.
        /// </summary>
        public double Delta { get; private set; }

        /// <summary>Target speed under the current neutralisation, in km/h.</summary>
        public double TargetSpeedKmh { get; private set; }

        /// <summary>Laps completed since the current safety car was deployed.</summary>
        public int LapsUnderNeutralisation { get; private set; }

        /// <summary>Seconds the current neutralisation has been running.</summary>
        public double NeutralisationSeconds => Status == RaceControlStatus.Green || Status == RaceControlStatus.Inactive
            ? 0
            : _sessionSeconds - _periodStartSeconds;

        public int SafetyCarPeriods { get; private set; }

        public int VirtualSafetyCarPeriods { get; private set; }

        /// <summary>FIA B5.12.4: restrictions remain active throughout the ending notice.</summary>
        public bool VirtualSafetyCarEnding => Status == RaceControlStatus.VirtualSafetyCar && _vscGreenAt >= 0;

        public bool RedFlagged { get; private set; }

        /// <summary>Explicit pit closure; a normal SC deployment does not close pit entry.</summary>
        public bool PitLaneClosed { get; private set; }

        /// <summary>
        /// Fase della procedura di safety car. Segue il regolamento: uscita, ricompattamento,
        /// sdoppiaggio dei doppiati, ultimo giro, rientro e ripartenza.
        /// </summary>
        public SafetyCarPhase SafetyCarPhase { get; private set; } = SafetyCarPhase.Idle;

        /// <summary>Impostazioni della vettura di sicurezza in uso.</summary>
        public SafetyCarSettings SafetyCarSettings => _safetyCar;

        /// <summary>Vero mentre i doppiati hanno il permesso di sdoppiarsi.</summary>
        public bool UnlappingAllowed => SafetyCarPhase == SafetyCarPhase.LappedCarsUnlapping;

        /// <summary>Fase del giro di formazione: ricognizione, griglia, semaforo, via.</summary>
        public FormationPhase FormationPhase { get; private set; } = FormationPhase.Idle;

        /// <summary>
        /// Vero quando il pilota deve partire dalla corsia box perche non ha preso la sua
        /// piazzola in tempo. L'app in pista lo porta nel suo garage e lo trattiene finche il
        /// gruppo non e passato, poi lo lascia andare.
        ///
        /// E la regola vera: chi non riesce a schierarsi non viene squalificato, parte dai box.
        /// La squalifica e per chi ignora la direzione gara, che e un'altra cosa.
        /// </summary>
        public bool PitLaneStart { get; private set; }

        /// <summary>Quante luci rosse sono accese adesso, da 0 a cinque.</summary>
        public int LightsOn { get; private set; }

        /// <summary>La piazzola che ti spetta, cioe la posizione con cui sei partito.</summary>
        public int GridSlot { get; private set; }

        /// <summary>Vero quando sei fermo dentro la tua piazzola. Lo riferisce l'app in pista.</summary>
        public bool PlayerOnGrid { get; private set; }

        /// <summary>Quanto manca alla piazzola, in metri, secondo l'app in pista.</summary>
        public double GridDistanceMeters { get; private set; }

        /// <summary>Cosa deve fare il pilota adesso, da mostrare in pista.</summary>
        public string GridNotice { get; private set; } = "";

        /// <summary>Posizione da restituire durante la ricognizione, zero se e tutto in ordine.</summary>
        public int PositionToGiveBack { get; private set; }

        /// <summary>Secondi che restano per restituirla.</summary>
        public double GiveBackSecondsLeft { get; private set; }

        private int _deletedLap = -1;
        private bool _gridSettled;
        private double _giveBackTimer;
        private bool _formationPenaltyIssued;
        private string _playerId = "";
        private string _playerName = "";

        /// <summary>
        /// L'app in pista riferisce dove si trova la vettura rispetto alla sua piazzola. La
        /// geometria della griglia la conosce solo lei, che vede il mondo 3D; qui arriva gia
        /// ridotta a due numeri.
        /// </summary>
        public void ReportGrid(bool onGrid, double distanceMeters)
        {
            PlayerOnGrid = onGrid;
            GridDistanceMeters = distanceMeters;
        }

        public FormationLapSettings FormationSettings => _formation;

        public event Action<RaceControlMessage> MessagePosted;
        public event Action<RaceControlStatus> StatusChanged;

        public string StatusText
        {
            get
            {
                switch (Status)
                {
                    case RaceControlStatus.FormationLap:
                        var formationLabel = FormationLap.Label(FormationPhase);
                        return string.IsNullOrEmpty(formationLabel) ? Testi.T("formazione.giro") : formationLabel;
                    case RaceControlStatus.Green: return Testi.T("gara.verde");
                    case RaceControlStatus.LocalYellow: return Testi.T("gara.giallo.settore") + string.Join("/", YellowSectors.OrderBy(s => s));
                    case RaceControlStatus.VirtualSafetyCar: return Testi.T(VirtualSafetyCarEnding ? "gara.vsc.fine" : "gara.vsc.sigla");
                    case RaceControlStatus.SafetyCar:
                    case RaceControlStatus.SafetyCarEnding:
                        var label = SafetyCar.Label(SafetyCarPhase);
                        return string.IsNullOrEmpty(label) ? Testi.T("gara.sc.sigla") : label;
                    case RaceControlStatus.RedFlag: return Testi.T("gara.rossa");
                    case RaceControlStatus.Checkered: return Testi.T("gara.scacchi");
                    default: return "-";
                }
            }
        }

        // ---- Lifecycle ---------------------------------------------------------------------

        /// <summary>
        /// Apre una sessione.
        ///
        /// <paramref name="fromTheStart"/> dice se la stiamo prendendo dal suo inizio vero. Se
        /// no, la procedura di partenza non si arma: niente ricognizione, niente griglia,
        /// bandiera verde e via. Schierare venti vetture basandosi su dati che non descrivono
        /// questa gara vuol dire parcheggiarle in posizioni inventate, e da li non ripartono.
        /// </summary>
        public void BeginSession(Circuit circuit, SessionConfig session, RaceControlRules rules,
                                 bool fromTheStart = true)
        {
            _circuit = circuit ?? new Circuit();
            _session = session ?? new SessionConfig();
            _rules = rules ?? new RaceControlRules();
            _safetyCar = _rules.SafetyCar ?? new SafetyCarSettings();
            _formation = _rules.Formation ?? new FormationLapSettings();
            SafetyCarPhase = SafetyCarPhase.Idle;
            FormationPhase = FormationPhase.Idle;
            _formationTimer = 0;
            _lightsDelay = 0;
            _rollingSeconds = 0;
            _greenSeconds = -1;
            GridSlot = 0;
            LightsOn = 0;
            PitLaneStart = false;
            PlayerOnGrid = false;
            GridDistanceMeters = 0;
            GridNotice = "";
            PositionToGiveBack = 0;
            GiveBackSecondsLeft = 0;
            _gridSettled = false;
            _deletedLap = -1;
            _giveBackTimer = 0;
            _formationPenaltyIssued = false;

            _detector.Reset();
            _detector.SectorCount = Math.Max(1, _circuit.Sectors);
            Penalties.Clear();
            Feed.Clear();
            Incidents.Clear();
            _yellowUntil.Clear();

            _sessionSeconds = 0;
            _breachSeconds = 0;
            _vscGreenAt = -1;
            _lastHazardSeconds = -9999;
            _pitSpeedingFlagged = false;
            _wasInPitLane = false;
            _pitBoxStationarySeconds = 0;
            _pitStopChecked = false;
            _safetyCarReturnLap = -1;
            _safetyCarObserved = false;

            SafetyCarPeriods = 0;
            VirtualSafetyCarPeriods = 0;
            RedFlagged = false;
            PitLaneClosed = false;
            LapsUnderNeutralisation = 0;
            Delta = 0;
            TargetSpeedKmh = 0;
            _periodStartSeconds = 0;
            _periodStartDistance = 0;
            _periodStartLap = 0;

            bool isRace = _session.Kind == SessionKind.Race || _session.Kind == SessionKind.Sprint;
            bool formation = isRace && _session.FormationLap && fromTheStart;

            if (formation)
            {
                FormationPhase = FormationPhase.Rolling;
                TargetSpeedKmh = _formation.PaceKmh;
            }

            SetStatus(formation ? RaceControlStatus.FormationLap : RaceControlStatus.Green,
                      isRace ? Testi.T("gara.sessione.avviata") : null);

            if (formation) Post(FormationLap.Message(FormationPhase.Rolling), "RaceControl", 2, 0);
        }

        public void EndSession()
        {
            SetStatus(RaceControlStatus.Checkered, Testi.T("gara.scacchi.radio"));
        }

        // ---- Main loop ---------------------------------------------------------------------

        /// <summary>Processes one telemetry frame. Safe to call at the full plugin rate.</summary>
        public void Update(TelemetryFrame frame, string playerDriverId, string playerDriverName)
        {
            if (frame == null || !frame.GameRunning || frame.Paused || frame.InMenu || frame.Replay) return;

            _playerId = playerDriverId;
            _playerName = playerDriverName;

            double dt = Math.Max(0, Math.Min(1.0, frame.DeltaSeconds));
            _sessionSeconds += dt;

            if (frame.FlagCheckered)
            {
                if (Status != RaceControlStatus.Checkered) EndSession();
                return;
            }
            if (Status == RaceControlStatus.Checkered || Status == RaceControlStatus.Inactive) return;

            bool isRace = _session.Kind == SessionKind.Race || _session.Kind == SessionKind.Sprint;

            // La piazzola e la posizione con cui si e partiti, e si legge solo nei primi secondi
            // sulla griglia. Se Pitwall si attacca a gara iniziata non la si inventa: resta zero,
            // e il divieto di sorpasso non si applica, invece di punire per una posizione che
            // nessuno ha mai avuto.
            if (isRace && GridSlot == 0 && frame.Position > 0 &&
                frame.CompletedLaps == 0 && _sessionSeconds < 15) GridSlot = frame.Position;

            CheckPitLane(frame, playerDriverId, playerDriverName, dt);

            if (Status == RaceControlStatus.FormationLap)
            {
                UpdateFormation(frame, dt);
                return;
            }

            if (_rules.TrackLimitsEnabled && !frame.LapValid)
            {
                if (isRace)
                {
                    CheckTrackLimits(frame, playerDriverId, playerDriverName);
                }
                else if (_deletedLap != frame.CompletedLaps)
                {
                    // Fuori dalla gara i track limits non danno secondi: cancellano il giro, e
                    // basta dirlo una volta per giro invece di ripeterlo a ogni fotogramma.
                    _deletedLap = frame.CompletedLaps;
                    Post(Testi.T("penalita.giro.cancellato"), "RaceControl", 1, frame.CompletedLaps);
                }
            }

            if (!_rules.Enabled || !isRace)
                return;

            var newIncidents = _detector.Update(frame);
            foreach (var incident in newIncidents)
            {
                Incidents.Add(incident);
                Post(incident.Description, Testi.T("incidente"), 1, frame.CompletedLaps);
            }

            if (_detector.HasActiveHazards)
            {
                _lastHazardSeconds = _sessionSeconds;
                foreach (var hazard in _detector.ActiveHazards)
                    _yellowUntil[hazard.Sector] = _sessionSeconds + YellowSeconds;
                // A fresh/ongoing hazard cancels an ending countdown, never the restrictions.
                if (VirtualSafetyCarEnding)
                {
                    _vscGreenAt = -1;
                    Post(Testi.T("gara.vsc.continua"), "RaceControl", 2, frame.CompletedLaps);
                }
            }
            ExpireLocalYellows(frame);
            // Decide once for the whole frame, with the most serious evidence first.
            foreach (var incident in newIncidents.OrderByDescending(i => i.Severity)) Consider(incident, frame);

            UpdateNeutralisation(frame, playerDriverId, playerDriverName, dt);
        }

        // ---- Neutralisation decisions --------------------------------------------------------

        private void Consider(Incident incident, TelemetryFrame frame)
        {
            if (Status != RaceControlStatus.Green && Status != RaceControlStatus.LocalYellow &&
                Status != RaceControlStatus.VirtualSafetyCar && Status != RaceControlStatus.SafetyCar &&
                Status != RaceControlStatus.SafetyCarEnding) return;
            // Disappearing from timing is not evidence of a physical obstacle.
            if (incident.Kind == IncidentKind.CarRetired) return;

            if (incident.Severity >= 5 && _rules.RedFlagEnabled)
            {
                DeployRedFlag(Testi.T("incidente.grave.radio", incident.DriverName), frame);
                return;
            }

            if (incident.Severity <= 2)
            {
                RaiseLocalYellow(incident, frame);
                return;
            }

            // FIA B5.12/B5.13: danger drives the decision. Severity is our telemetry heuristic,
            // not an FIA damage threshold. No random deployment, cooldown or final-lap veto.
            if (incident.Severity >= 4 && _rules.SafetyCarEnabled) DeploySafetyCar(incident.Description, frame);
            else if (_rules.VirtualSafetyCarEnabled) DeployVirtualSafetyCar(incident.Description, frame);
            else if (_rules.SafetyCarEnabled) DeploySafetyCar(incident.Description, frame);
            else RaiseLocalYellow(incident, frame);
        }

        private void RaiseLocalYellow(Incident incident, TelemetryFrame frame)
        {
            bool nuova = !_yellowUntil.ContainsKey(incident.Sector);
            _yellowUntil[incident.Sector] = _sessionSeconds + YellowSeconds;

            if (Status == RaceControlStatus.Green)
                SetStatus(RaceControlStatus.LocalYellow, Testi.T("gara.giallo.esposta", incident.Sector));
            else if (nuova && Status == RaceControlStatus.LocalYellow)
                Post(Testi.T("gara.giallo.anche", incident.Sector), "RaceControl", 2, 0);
        }

        /// <summary>
        /// Spegne le gialle scadute, e torna verde quando non ne resta nessuna.
        ///
        /// Senza questo una gialla di settore non finiva mai: i settori si svuotavano solo al
        /// rientro di una safety car, quindi un incidente qualsiasi lasciava la gara gialla per
        /// sempre. In pista si vedeva il pannello giallo acceso con la pista libera.
        /// </summary>
        private void ExpireLocalYellows(TelemetryFrame frame)
        {
            if (_yellowUntil.Count == 0) return;

            var scaduti = _yellowUntil.Where(p => p.Value <= _sessionSeconds)
                                      .Select(p => p.Key).ToList();
            foreach (var settore in scaduti) _yellowUntil.Remove(settore);

            if (_yellowUntil.Count > 0 || Status != RaceControlStatus.LocalYellow) return;

            SetStatus(RaceControlStatus.Green, Testi.T("gara.pista.libera"));
        }

        public void DeployVirtualSafetyCar(string reason, TelemetryFrame frame)
        {
            if (!CanDeploy(frame) || Status == RaceControlStatus.VirtualSafetyCar ||
                Status == RaceControlStatus.SafetyCar || Status == RaceControlStatus.SafetyCarEnding) return;
            StartPeriod(frame);
            VirtualSafetyCarPeriods++;
            TargetSpeedKmh = PaceSpeedKmh(_rules.VscPacePercent);
            SetStatus(RaceControlStatus.VirtualSafetyCar, Testi.T("gara.vsc.motivo", reason));
        }

        public void DeploySafetyCar(string reason, TelemetryFrame frame)
        {
            if (!CanDeploy(frame) || Status == RaceControlStatus.SafetyCar || Status == RaceControlStatus.SafetyCarEnding) return;
            StartPeriod(frame);
            SafetyCarPeriods++;
            PitLaneClosed = false;
            SafetyCarPhase = SafetyCarPhase.Deploying;
            // La vettura di sicurezza detta il ritmo: se c'e davvero in pista si usa il suo,
            // altrimenti si ricade sulla percentuale del passo gara.
            TargetSpeedKmh = _safetyCar.UseRealCar && _safetyCar.PaceKmh > 10
                ? _safetyCar.PaceKmh
                : PaceSpeedKmh(_rules.SafetyCarPacePercent);
            SetStatus(RaceControlStatus.SafetyCar, Testi.T("gara.sc.motivo", reason));
        }

        public void DeployRedFlag(string reason, TelemetryFrame frame)
        {
            if (!CanDeploy(frame)) return;
            RedFlagged = true;
            StartPeriod(frame);
            SetStatus(RaceControlStatus.RedFlag, Testi.T("gara.rossa.motivo", reason));
        }

        /// <summary>Ends the current neutralisation, going through the one-lap warning first.</summary>
        public void EndNeutralisation(TelemetryFrame frame)
        {
            if (frame == null || Status == RaceControlStatus.Checkered) return;
            if (Status == RaceControlStatus.VirtualSafetyCar)
            {
                if (!VirtualSafetyCarEnding && TrackClearForWithdrawal)
                {
                    _vscGreenAt = _sessionSeconds + 10 + _random.NextDouble() * 5;
                    Post(Testi.T("gara.vsc.fine"), "RaceControl", 2, frame.CompletedLaps);
                }
                return;
            }
            if (Status == RaceControlStatus.SafetyCar)
            {
                if (!TrackClearForWithdrawal) return;
                _safetyCarReturnLap = frame.CompletedLaps;
                SafetyCarPhase = SafetyCarPhase.InThisLap;
                SetStatus(RaceControlStatus.SafetyCarEnding, Testi.T("gara.sc.rientra.avviso"));
                PitLaneClosed = false;
                return;
            }

            // Un secondo clic non deve saltare il rientro e dare subito il verde.
            if (Status == RaceControlStatus.SafetyCarEnding) return;
            ResumeGreen(frame);
        }

        public void ResumeGreen(TelemetryFrame frame)
        {
            _yellowUntil.Clear();
            PitLaneClosed = false;
            SafetyCarPhase = SafetyCarPhase.Idle;
            _safetyCarReturnLap = -1;
            _safetyCarObserved = false;
            Delta = 0;
            _breachSeconds = 0;
            _vscGreenAt = -1;
            LapsUnderNeutralisation = 0;
            TargetSpeedKmh = 0;
            SetStatus(RaceControlStatus.Green, Testi.T("gara.verde.annuncio"));
        }

        private void StartPeriod(TelemetryFrame frame)
        {
            _vscGreenAt = -1;
            _safetyCarReturnLap = -1;
            _safetyCarObserved = frame.Opponents.Any(o => SafetyCar.IsSafetyCar(o.Name) && o.IsConnected);
            _periodStartSeconds = _sessionSeconds;
            _periodStartDistance = CumulativeDistance(frame);
            _periodStartLap = frame.CompletedLaps;
            _periodStartPosition = frame.Position;
            _breachSeconds = 0;
            _deltaPenaltyIssued = false;
            _overtakePenalties = 0;
            LapsUnderNeutralisation = 0;
            Delta = 0;
        }

        private bool CanDeploy(TelemetryFrame frame) => frame != null && frame.GameRunning &&
            !frame.FlagCheckered && !frame.Paused && !frame.Replay && !frame.InMenu &&
            (_session.Kind == SessionKind.Race || _session.Kind == SessionKind.Sprint) &&
            Status != RaceControlStatus.Checkered && Status != RaceControlStatus.FormationLap &&
            Status != RaceControlStatus.RedFlag && Status != RaceControlStatus.Inactive &&
            StartSettled;

        /// <summary>
        /// Vero quando dal verde e passato abbastanza perche il gruppo si sia disteso.
        ///
        /// Serve perche una partenza, vista dai dati, e indistinguibile da un incidente: venti
        /// vetture ferme una accanto all altra. Nel collaudo la gialla arrivava due secondi dopo il
        /// semaforo e la safety car quattro, tutte le volte; e poiche le IA sotto safety car vanno
        /// ai box - che e la strategia giusta, al momento sbagliato - la gara cominciava con il
        /// gruppo intero in corsia box invece che in pista.
        /// </summary>
        /// Vale solo per la partenza che orchestra Pitwall, cioe quella con il giro di ricognizione:
        /// li il semaforo lo diamo noi e sappiamo esattamente quando. Una sessione presa in corsa, o
        /// una partenza diretta lasciata ad Assetto Corsa, non arma nessuna finestra - e giusto cosi,
        /// perche li un gruppo fermo e davvero un incidente. Un primo tentativo bloccava i primi
        /// venticinque secondi di qualunque sessione, e ha fatto cadere sedici controlli: avevano
        /// ragione loro.
        private bool StartSettled => _greenSeconds < 0 ||
            _sessionSeconds - _greenSeconds >= Math.Max(0, _safetyCar.StartSettleSeconds);

        // Clearance debounce is a simulation setting, not a regulatory fixed duration.
        private bool TrackClearForWithdrawal => !_detector.HasActiveHazards &&
            _sessionSeconds - _lastHazardSeconds >= Math.Max(0, _rules.IncidentClearSeconds);

        private void UpdateNeutralisation(TelemetryFrame frame, string driverId, string driverName, double dt)
        {
            if (Status != RaceControlStatus.VirtualSafetyCar &&
                Status != RaceControlStatus.SafetyCar &&
                Status != RaceControlStatus.SafetyCarEnding)
                return;

            LapsUnderNeutralisation = Math.Max(0, frame.CompletedLaps - _periodStartLap);

            // ---- Delta -------------------------------------------------------------------
            double targetMs = TargetSpeedKmh / 3.6;
            if (targetMs > 0.5 && !frame.InPitLane)
            {
                double covered = CumulativeDistance(frame) - _periodStartDistance;
                double expected = covered / targetMs;
                double actual = _sessionSeconds - _periodStartSeconds;
                Delta = actual - expected;

                bool tooFast = Delta < -_rules.VscDeltaToleranceSeconds;
                if (tooFast) _breachSeconds += dt;
                else _breachSeconds = Math.Max(0, _breachSeconds - dt * 0.5);

                // Simulation cap: one delta penalty per neutralisation. Without this
                // a driver who simply cannot match an unrealistic delta collects a new five
                // seconds every few seconds and the race is over before the safety car is in.
                if (_breachSeconds >= _rules.VscBreachPenaltySeconds && !_deltaPenaltyIssued)
                {
                    _breachSeconds = 0;
                    _deltaPenaltyIssued = true;
                    Penalties.IssueTime(driverId, driverName, 5, OffenceKind.VirtualSafetyCarDelta,
                        Testi.T("penalita.delta.motivo",
                                Status == RaceControlStatus.SafetyCar
                                    ? Testi.T("gara.sc.sigla") : Testi.T("gara.vsc.sigla")),
                        frame.CompletedLaps, _rules.LicencePointsPerSeriousOffence);
                    Post(Testi.T("penalita.secondi", 5, Testi.T("penalita.delta")), Testi.T("penalita"), 2, frame.CompletedLaps);
                }
            }

            // ---- Overtaking under neutralisation ------------------------------------------
            if (!frame.InPitLane && frame.Position > 0 && _periodStartPosition > 0 &&
                frame.Position < _periodStartPosition)
            {
                // Positions gained because someone else pitted are legitimate, so only react
                // when the car ahead is still on track.
                bool someoneAheadPitted = frame.Opponents.Any(o => o.InPitLane && o.Position <= _periodStartPosition);
                if (!someoneAheadPitted && _overtakePenalties < 2)
                {
                    _overtakePenalties++;
                    Penalties.IssueTime(driverId, driverName, 5, OffenceKind.OvertakeUnderSafetyCar,
                        Testi.T("penalita.sorpasso.motivo"), frame.CompletedLaps,
                        _rules.LicencePointsPerSeriousOffence);
                    Post(Testi.T("penalita.secondi", 5, Testi.T("penalita.sorpasso")), Testi.T("penalita"), 2, frame.CompletedLaps);
                }
                _periodStartPosition = frame.Position;
            }

            // ---- Duration -----------------------------------------------------------------
            double elapsed = _sessionSeconds - _periodStartSeconds;

            if (Status == RaceControlStatus.VirtualSafetyCar)
            {
                if (VirtualSafetyCarEnding && _sessionSeconds >= _vscGreenAt && TrackClearForWithdrawal)
                    ResumeGreen(frame);
                else if (!VirtualSafetyCarEnding && elapsed >= _rules.VscMinimumSeconds && TrackClearForWithdrawal)
                    EndNeutralisation(frame);
            }
            else if (Status == RaceControlStatus.SafetyCar || Status == RaceControlStatus.SafetyCarEnding)
            {
                AdvanceSafetyCarPhase(frame);
            }
        }

        /// <summary>
        /// Vero quando la ricognizione ha finito il suo giro e si puo schierare.
        ///
        /// Per molto tempo qui c era solo il giro del giocatore, e questo bastava a rompere la
        /// gara: se il giocatore era lento, fermo, girato o ancora in corsia box, il suo giro
        /// non si chiudeva mai, la fase restava Rolling e le vetture continuavano a girare
        /// all infinito. Il semaforo non si accendeva e la gara non cominciava: non un difetto
        /// di piazzamento, un prodotto che si blocca.
        ///
        /// Adesso valgono tre segnali, in ordine di bonta. Il giro del giocatore, che e quello
        /// giusto quando le cose vanno come devono. Il giro del gruppo: se le altre hanno
        /// chiuso il loro giro la ricognizione **e** finita, e chi e rimasto indietro lo gestisce
        /// gia la fase Forming, che dopo l attesa lo fa partire dalla corsia box - che e anche
        /// quello che succede davvero a chi non prende la sua piazzola. E infine un tetto di
        /// tempo, che non deve servire mai ma esiste perche una gara che non parte non e
        /// un difetto accettabile.
        /// </summary>
        private bool RollingLapDone(TelemetryFrame frame)
        {
            if (frame.CompletedLaps >= 1) return true;

            // Nei primi secondi non si guarda nessun altro. Assetto Corsa lascia in memoria i
            // dati della sessione precedente, e un conteggio giri ereditato dalle prove
            // chiuderebbe la ricognizione nell istante in cui comincia.
            if (_rollingSeconds < 25) return false;

            int gruppo = 0;
            if (frame.Opponents != null)
            {
                foreach (var vettura in frame.Opponents)
                {
                    if (vettura == null || vettura.IsPlayer) continue;
                    if (vettura.CompletedLaps > gruppo) gruppo = vettura.CompletedLaps;
                }
            }

            if (gruppo >= 1) return true;

            return _rollingSeconds >= RollingBudgetSeconds(frame);
        }

        /// <summary>
        /// Quanto si concede alla ricognizione prima di schierare comunque.
        ///
        /// E il tempo di un giro al ritmo previsto, raddoppiato: il ritmo e un tetto, non una
        /// velocita tenuta, e con le frenate e le serpentine per scaldare le gomme un giro ne
        /// chiede molto di piu. Chi arriva a questo limite ha comunque un problema, ma la gara
        /// parte.
        /// </summary>
        private double RollingBudgetSeconds(TelemetryFrame frame)
        {
            double giro = frame.TrackLengthMeters > 100 ? frame.TrackLengthMeters : _circuit.LengthMeters;
            if (giro <= 100) giro = 5000;

            double ritmo = Math.Max(10, _formation.PaceKmh) / 3.6;
            return giro / ritmo * 2.0 + 30;
        }

        /// <summary>
        /// Porta avanti il giro di formazione: ricognizione a ritmo ridotto, ritorno in griglia,
        /// semaforo, via. Durante la ricognizione il delta viene mostrato ma non produce
        /// penalita: non e ancora gara.
        /// </summary>
        private void UpdateFormation(TelemetryFrame frame, double dt)
        {
            if (FormationPhase == FormationPhase.Rolling)
            {
                _rollingSeconds += dt;
                TargetSpeedKmh = _formation.PaceKmh;

                double targetMs = TargetSpeedKmh / 3.6;
                if (targetMs > 0.5 && !frame.InPitLane)
                {
                    double covered = CumulativeDistance(frame) - _periodStartDistance;
                    double expected = covered / targetMs;
                    Delta = (_sessionSeconds - _periodStartSeconds) - expected;
                }

                CheckFormationOvertaking(frame, dt);

                if (RollingLapDone(frame))
                {
                    if (_formation.ReturnToGrid)
                    {
                        FormationPhase = FormationPhase.Forming;
                        _formationTimer = 0;
                        Post(FormationLap.Message(FormationPhase.Forming), "RaceControl", 2, frame.CompletedLaps);
                    }
                    else
                    {
                        FinishFormation(frame);
                    }
                }
                return;
            }

            if (FormationPhase == FormationPhase.Forming)
            {
                _formationTimer += dt;

                // Finche non sei fermo nella tua piazzola il conto non parte nemmeno: in griglia
                // si aspetta l'ultimo, e il semaforo si accende solo quando sono tutti a posto.
                //
                // Il controllo su _gridSettled non e di troppo: appena l'attesa finisce il
                // cronometro riparte da zero per contare i secondi in griglia, e senza questa
                // condizione la riga qui sotto lo rileggerebbe come "non e ancora scaduta" e si
                // resterebbe fermi sulla griglia per sempre.
                if (!_gridSettled && _formation.PlayerParksOnGrid && !PlayerOnGrid &&
                    _formationTimer < _formation.GridTimeoutSeconds)
                {
                    GridNotice = GridDistanceMeters > 0
                        ? Testi.T("formazione.piazzola.distanza", GridSlot, GridDistanceMeters)
                        : Testi.T("formazione.piazzola");
                    return;
                }

                if (!_gridSettled)
                {
                    _gridSettled = true;
                    _formationTimer = 0;

                    if (PlayerOnGrid || !_formation.PlayerParksOnGrid)
                    {
                        GridNotice = Testi.T("formazione.griglia") + ".";
                    }
                    else
                    {
                        // Chi non prende la sua piazzola nei tempi parte dalla corsia box: la
                        // gara non si ferma ad aspettarlo, ma non lo butta nemmeno fuori.
                        PitLaneStart = true;
                        GridNotice = Testi.T("formazione.corsia.avviso");
                        Penalties.IssueWarning(_playerId, _playerName, OffenceKind.Other,
                            Testi.T("penalita.griglia.mancata"), frame.CompletedLaps);
                        Post(Testi.T("formazione.corsia.box"),
                             "RaceControl", 3, frame.CompletedLaps);
                    }
                    return;
                }

                if (_formationTimer >= _formation.GridHoldSeconds)
                {
                    FormationPhase = FormationPhase.Lights;
                    _formationTimer = 0;
                    LightsOn = 1;
                    // Come in Formula 1: fra l'ultima luce e il verde passa un tempo variabile,
                    // altrimenti la partenza si impara a memoria.
                    _lightsDelay = _formation.LightsMinSeconds +
                                   _random.NextDouble() * Math.Max(0, _formation.LightsMaxSeconds - _formation.LightsMinSeconds);
                    Post(FormationLap.Message(FormationPhase.Lights), "RaceControl", 3, frame.CompletedLaps);
                }
                return;
            }

            if (FormationPhase == FormationPhase.Lights)
            {
                _formationTimer += dt;

                // Le luci si accendono una per volta, come in Formula 1: cinque rosse a un
                // secondo l'una dall'altra, e quando si spengono tutte insieme e via.
                int count = Math.Max(1, Math.Min(5, _formation.LightsCount));
                double interval = _formation.LightsIntervalSeconds;
                if (double.IsNaN(interval) || double.IsInfinity(interval) || interval <= 0) interval = 1;
                // La prima luce e gia accesa all'ingresso: la quinta arriva dopo quattro intervalli.
                double sequence = (count - 1) * interval;
                LightsOn = (int)Math.Min(count, Math.Floor(_formationTimer / interval) + 1);

                if (_formationTimer >= sequence + _lightsDelay) FinishFormation(frame);
            }
        }

        /// <summary>
        /// Divieto di sorpasso in ricognizione.
        ///
        /// Il regolamento non punisce il sorpasso in se: punisce chi non restituisce la
        /// posizione. Quindi prima si avvisa, si lascia il tempo di rimettersi dietro, e solo se
        /// la fila non torna in ordine arriva la penalita. Una posizione guadagnata perche
        /// qualcuno e finito ai box o si e fermato non conta.
        /// </summary>
        private void CheckFormationOvertaking(TelemetryFrame frame, double dt)
        {
            if (!_formation.NoOvertaking || GridSlot <= 0 || frame.Position <= 0 || frame.InPitLane)
            {
                PositionToGiveBack = 0;
                return;
            }

            bool gained = frame.Position < GridSlot;
            if (gained)
            {
                // Chi e davanti a te in griglia ma ora e fermo o ai box non te l'ha regalata.
                bool legitimate = frame.Opponents.Any(o =>
                    o.StartPosition.HasValue && o.StartPosition.Value < GridSlot &&
                    (o.InPitLane || o.InPit || o.SpeedKmh < 20));
                if (legitimate) gained = false;
            }

            if (!gained)
            {
                if (PositionToGiveBack > 0)
                    Post(Testi.T("formazione.restituita"), "RaceControl", 1, frame.CompletedLaps);
                PositionToGiveBack = 0;
                _giveBackTimer = 0;
                GiveBackSecondsLeft = 0;
                return;
            }

            if (PositionToGiveBack == 0)
            {
                PositionToGiveBack = GridSlot;
                _giveBackTimer = 0;
                Post(Testi.T("formazione.sorpasso", GridSlot + "\u00b0"),
                     "RaceControl", 3, frame.CompletedLaps);
            }

            _giveBackTimer += dt;
            GiveBackSecondsLeft = Math.Max(0, _formation.GiveBackSeconds - _giveBackTimer);

            if (GiveBackSecondsLeft <= 0 && !_formationPenaltyIssued)
            {
                _formationPenaltyIssued = true;
                Penalties.IssueTime(_playerId, _playerName, _formation.OvertakePenaltySeconds,
                    OffenceKind.OvertakeUnderSafetyCar, Testi.T("formazione.sorpasso.motivo"),
                    frame.CompletedLaps, _rules.LicencePointsPerSeriousOffence);
                Post(Testi.T("formazione.sorpasso.penalita",
                             _formation.OvertakePenaltySeconds.ToString("0")),
                     Testi.T("penalita"), 2, frame.CompletedLaps);
            }
        }

        private void FinishFormation(TelemetryFrame frame)
        {
            FormationPhase = FormationPhase.Away;
            LightsOn = 0;
            PositionToGiveBack = 0;
            GiveBackSecondsLeft = 0;
            GridNotice = "";
            Delta = 0;
            TargetSpeedKmh = 0;
            _periodStartSeconds = _sessionSeconds;
            _periodStartDistance = CumulativeDistance(frame);
            _periodStartLap = frame.CompletedLaps;
            _greenSeconds = _sessionSeconds;
            SetStatus(RaceControlStatus.Green, Testi.T("gara.verde.annuncio"));
        }

        /// <summary>
        /// Calendario semplificato della procedura di safety car, un giro per fase:
        /// uscita, ricompattamento, sdoppiaggio dei doppiati, ultimo giro, ripartenza.
        /// </summary>
        private void AdvanceSafetyCarPhase(TelemetryFrame frame)
        {
            int laps = LapsUnderNeutralisation;
            int bunching = Math.Max(1, _safetyCar.BunchingLaps);
            int unlapping = _safetyCar.AllowUnlapping ? Math.Max(1, _safetyCar.UnlappingLaps) : 0;
            int minimum = Math.Max(1, _rules.SafetyCarMinimumLaps);

            // Il primo giro e l'uscita, poi il ricompattamento, poi lo sdoppiaggio. Ogni fase
            // deve durare almeno un giro, altrimenti finirebbe per non esistere mai.
            int bunchingEnds = 1 + Math.Max(bunching, minimum - 1);
            int unlappingEnds = bunchingEnds + unlapping;

            var next = SafetyCarPhase;

            var safetyCar = frame.Opponents.FirstOrDefault(o => SafetyCar.IsSafetyCar(o.Name) && o.IsConnected);
            if (safetyCar != null) _safetyCarObserved = true;

            // Una volta annunciato il richiamo, anche manuale, il calendario automatico
            // non puo riportare la SC alla fase di uscita o ricompattamento.
            if (!TrackClearForWithdrawal)
            {
                // Do not send cars unlapping or withdraw while an obstacle remains.
                // If the call-in was already announced, revoke it and keep the same period.
                _safetyCarReturnLap = -1;
                next = laps < 1 || (safetyCar != null && (safetyCar.InPitLane || safetyCar.InPit))
                    ? SafetyCarPhase.Deploying : SafetyCarPhase.Bunching;
                if (Status == RaceControlStatus.SafetyCarEnding) SetStatus(RaceControlStatus.SafetyCar, null);
            }
            else if (_safetyCarReturnLap >= 0)
                next = frame.CompletedLaps > _safetyCarReturnLap ? SafetyCarPhase.Restarting : SafetyCarPhase.InThisLap;
            else if (laps < 1) next = SafetyCarPhase.Deploying;
            else if (laps < bunchingEnds) next = SafetyCarPhase.Bunching;
            else if (laps < unlappingEnds) next = SafetyCarPhase.LappedCarsUnlapping;
            else next = SafetyCarPhase.InThisLap;

            if (next != SafetyCarPhase)
            {
                SafetyCarPhase = next;
                if (next == SafetyCarPhase.InThisLap) _safetyCarReturnLap = frame.CompletedLaps;
                string message = SafetyCar.Message(next);
                if (!string.IsNullOrEmpty(message)) Post(message, "RaceControl", 2, frame.CompletedLaps);

                if (next == SafetyCarPhase.InThisLap && Status != RaceControlStatus.SafetyCarEnding)
                    SetStatus(RaceControlStatus.SafetyCarEnding, null);
            }

            if (SafetyCarPhase == SafetyCarPhase.Restarting)
            {
                // Restarting deve restare pubblicato: il Lua usa questa fase per chiedere
                // davvero il pit stop alla SC. Il verde arriva quando e fuori dalla pista.
                // La scomparsa di una SC gia osservata non conferma il suo rientro.
                if (!_safetyCar.UseRealCar || !_safetyCarObserved ||
                    (safetyCar != null && (safetyCar.InPitLane || safetyCar.InPit)))
                    ResumeGreen(frame);
            }
        }

        // ---- Pit lane -------------------------------------------------------------------------

        /// <summary>
        /// Controlli della corsia box.
        ///
        /// Fuori dalla gara le penalità in secondi non esistono: in prove libere e in qualifica
        /// non c'è un tempo di gara a cui sommarli, e infatti in Formula 1 si prende un richiamo,
        /// non cinque secondi. Qui la velocità in corsia resta segnalata, perché sapere di aver
        /// superato il limite serve comunque, ma diventa un richiamo. La sosta minima e la pit
        /// lane chiusa invece non si applicano proprio: sono regole di gara.
        /// </summary>
        private void CheckPitLane(TelemetryFrame frame, string driverId, string driverName, double dt)
        {
            bool isRace = _session.Kind == SessionKind.Race || _session.Kind == SessionKind.Sprint;

            if (frame.InPitLane && !_wasInPitLane)
            {
                _pitSpeedingFlagged = false;
                _pitBoxStationarySeconds = 0;
                _pitStopChecked = false;

                if (isRace && PitLaneClosed)
                {
                    Penalties.IssueTime(driverId, driverName, 10, OffenceKind.Other,
                        Testi.T("penalita.box.chiusa"), frame.CompletedLaps);
                    Post(Testi.T("penalita.pitlane.chiusa"), Testi.T("penalita"), 2, frame.CompletedLaps);
                }
            }

            if (frame.InPitLane)
            {
                if (_rules.PitLaneSpeedingEnabled && !_pitSpeedingFlagged &&
                    frame.SpeedKmh > _rules.PitLaneSpeedLimitKmh + 1.0)
                {
                    _pitSpeedingFlagged = true;
                    string detail = Testi.T("penalita.velocita.box") + ": " + Math.Round(frame.SpeedKmh) + " km/h";

                    if (isRace)
                    {
                        Penalties.IssueTime(driverId, driverName, _rules.PitLaneSpeedingPenaltySeconds,
                            OffenceKind.PitLaneSpeeding, detail, frame.CompletedLaps);
                        Post(Testi.T("penalita.velocita.box.testo"), Testi.T("penalita"), 2, frame.CompletedLaps);
                    }
                    else
                    {
                        Penalties.IssueWarning(driverId, driverName, OffenceKind.PitLaneSpeeding,
                            detail, frame.CompletedLaps);
                        Post(Testi.T("penalita.richiamo.testo", detail), "RaceControl", 1, frame.CompletedLaps);
                    }
                }

                if (frame.InPit && frame.SpeedKmh < 2) _pitBoxStationarySeconds += dt;
            }

            if (isRace && !frame.InPitLane && _wasInPitLane && !_pitStopChecked)
            {
                _pitStopChecked = true;
                if (_pitBoxStationarySeconds > 0.2 && _pitBoxStationarySeconds < _rules.MinimumPitStopSeconds)
                {
                    Penalties.IssueTime(driverId, driverName, 5, OffenceKind.MinimumPitStopTime,
                        Testi.T("penalita.sosta.corta"), frame.CompletedLaps);
                    Post(Testi.T("penalita.sosta.breve"), Testi.T("penalita"), 2, frame.CompletedLaps);
                }
            }

            _wasInPitLane = frame.InPitLane;
        }

        // ---- Track limits ----------------------------------------------------------------------

        private int _lastInvalidLap = -1;

        private void CheckTrackLimits(TelemetryFrame frame, string driverId, string driverName)
        {
            // The game invalidates a lap when the car goes beyond the white line. Count one
            // strike per lap at most, which matches how the stewards handle repeat offences.
            if (_lastInvalidLap == frame.CompletedLaps) return;
            _lastInvalidLap = frame.CompletedLaps;

            var penalty = Penalties.RegisterTrackLimits(driverId, driverName,
                _rules.TrackLimitsWarnings, _rules.TrackLimitsPenaltySeconds, frame.CompletedLaps);

            if (penalty != null)
            {
                Post(penalty.Type == PenaltyType.Warning
                        ? Testi.T("penalita.track.limits.conta",
                                  Penalties.TrackLimitStrikes(driverId), _rules.TrackLimitsWarnings)
                        : Testi.T("penalita.track.limits.secondi", (int)penalty.Seconds),
                     Testi.T("penalita.track.limits"), penalty.Type == PenaltyType.Warning ? 1 : 2,
                     frame.CompletedLaps);
            }
        }

        // ---- Helpers ------------------------------------------------------------------------------

        private double CumulativeDistance(TelemetryFrame frame)
        {
            double lapLength = frame.TrackLengthMeters > 100 ? frame.TrackLengthMeters : _circuit.LengthMeters;
            if (lapLength <= 100) lapLength = 5000;
            return frame.CompletedLaps * lapLength + frame.TrackPositionMeters;
        }

        private double PaceSpeedKmh(double pacePercent)
        {
            double lapLength = _circuit.LengthMeters > 100 ? _circuit.LengthMeters : 5000;
            double lapSeconds = _circuit.ReferenceLapSeconds > 5 ? _circuit.ReferenceLapSeconds : 95;
            double racingSpeedKmh = lapLength / lapSeconds * 3.6;
            return racingSpeedKmh * pacePercent / 100.0;
        }

        private void SetStatus(RaceControlStatus status, string message)
        {
            bool changed = Status != status;
            Status = status;
            if (!string.IsNullOrEmpty(message)) Post(message, "RaceControl", status == RaceControlStatus.Green ? 1 : 2, 0);
            if (changed) StatusChanged?.Invoke(status);
        }

        private void Post(string text, string category, int priority, int lap)
        {
            var msg = new RaceControlMessage { Text = text, Category = category, Priority = priority, Lap = lap };
            Feed.Add(msg);
            if (Feed.Count > 200) Feed.RemoveAt(0);
            MessagePosted?.Invoke(msg);
        }
    }
}
