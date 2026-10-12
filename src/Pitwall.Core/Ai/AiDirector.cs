using System;
using System.Collections.Generic;
using System.Linq;
using Pitwall.Live;
using Pitwall.Model;
using Pitwall.RaceControl;
using Pitwall.Tyres;
using Pitwall.Weather;

namespace Pitwall.Ai
{
    /// <summary>
    /// Il direttore dell'intelligenza artificiale.
    ///
    /// L'IA di Assetto Corsa gira sempre allo stesso ritmo, non consuma le gomme e si ferma ai
    /// box quando le pare. In un campionato in stile Formula 1 questo toglie senso a tutto: se
    /// gli avversari non degradano, la sosta e solo tempo perso e la strategia non esiste.
    ///
    /// Questa classe decide, prima del via, che gara fara ogni avversario — quante soste, con
    /// quali mescole, in che forma e con quale destino — e poi, giro dopo giro, traduce quel
    /// piano in comandi concreti. L'app in pista li applica con le funzioni physics.* di Custom
    /// Shaders Patch: livello IA, limite di velocita, richiesta di rientro ai box, mescola.
    ///
    /// Tutto nasce dal seme del round, quindi la gara e riproducibile: un undercut subito era
    /// gia scritto nei dati, non improvvisato per farti perdere.
    /// </summary>
    public sealed class AiDirector
    {
        private readonly Dictionary<string, AiRacePlan> _plans =
            new Dictionary<string, AiRacePlan>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, int> _pitCounts =
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        private Circuit _circuit = new Circuit();
        private int _totalLaps;
        private bool _active;

        /// <summary>Piani costruiti, uno per avversario. Utile per la schermata strategia.</summary>
        public IReadOnlyDictionary<string, AiRacePlan> Plans => _plans;

        /// <summary>Attivo solo in gara e solo se il campionato lo prevede.</summary>
        public bool Active => _active;

        /// <summary>Quanto il degrado pesa sul ritmo: 1 e realistico, 0 lo disattiva.</summary>
        public double DegradationStrength { get; set; } = 1.0;

        /// <summary>Probabilita che un avversario si ritiri durante la gara, 0 a 1.</summary>
        public double RetirementProbability { get; set; } = 0.12;

        /// <summary>Probabilita che un avversario commetta un errore serio, 0 a 1.</summary>
        public double MistakeProbability { get; set; } = 0.25;

        /// <summary>Un livello IA vale circa questi secondi al giro.</summary>
        private const double SecondsPerAiLevel = 0.08;

        // ---- Costruzione dei piani ------------------------------------------------------------

        public void Reset()
        {
            _plans.Clear();
            _pitCounts.Clear();
            _active = false;
        }

        /// <summary>
        /// Prepara la gara. Va chiamato quando la sessione parte, con la lista degli avversari
        /// cosi come li riporta la telemetria.
        /// </summary>
        public void BeginRace(Championship championship, Round round, SessionConfig session,
                              Circuit circuit, IEnumerable<OpponentFrame> opponents, int totalLaps)
        {
            Reset();
            if (championship == null || session == null) return;
            if (session.Kind != SessionKind.Race && session.Kind != SessionKind.Sprint) return;

            _circuit = circuit ?? new Circuit();
            _totalLaps = Math.Max(1, totalLaps);

            int seed = round != null ? round.Seed : Environment.TickCount;
            var rnd = new Random(seed ^ 0x5EED);

            foreach (var opponent in opponents ?? Enumerable.Empty<OpponentFrame>())
            {
                // Il piano si prepara per tutti, giocatore compreso: se poi accende il pilota
                // automatico il suo piano e gia pronto, invece di doverlo inventare a meta gara.
                if (string.IsNullOrWhiteSpace(opponent.Name)) continue;
                if (SafetyCar.IsSafetyCar(opponent.Name)) continue;

                var driver = championship.MatchTelemetryName(opponent.Name);
                var plan = new AiRacePlan
                {
                    DriverName = opponent.Name,
                    DriverId = driver != null ? driver.Id : "",
                    BaseAiLevel = driver != null ? driver.AiSkill : 95,
                    BaseAggression = driver != null ? driver.AiAggression : 50,
                    // Fino a quattro decimi al giro di forma, in un senso o nell'altro.
                    FormSeconds = (rnd.NextDouble() - 0.5) * 0.8
                };

                plan.Stints = BuildStrategy(session, rnd);
                plan.StintStartLap = 0;

                if (rnd.NextDouble() < RetirementProbability)
                    plan.RetirementLap = 3 + rnd.Next(Math.Max(1, _totalLaps - 5));
                else if (rnd.NextDouble() < MistakeProbability)
                    plan.MistakeLap = 2 + rnd.Next(Math.Max(1, _totalLaps - 3));

                _plans[opponent.Name] = plan;
                _pitCounts[opponent.Name] = opponent.PitCount;
            }

            _active = _plans.Count > 0;
        }

        /// <summary>
        /// Sceglie la strategia. Una sosta quando la distanza sta in due stint, due quando non ci
        /// sta, con le mescole scelte in modo che la regola delle due mescole sia rispettata.
        /// </summary>
        private List<PlannedStint> BuildStrategy(SessionConfig session, Random rnd)
        {
            double severity = 0.7 + 0.15 * _circuit.TyreSeverity;
            double cliff = 78.0;

            Func<CompoundSpec, double> lapsOn = spec =>
                cliff / Math.Max(0.05, spec.WearPerLapPercent * severity);

            double softLaps = lapsOn(Compounds.Soft);
            double mediumLaps = lapsOn(Compounds.Medium);
            double hardLaps = lapsOn(Compounds.Hard);

            int stops = _totalLaps <= (int)(mediumLaps + hardLaps) ? 1 : 2;
            if (session.MandatoryPitStops > stops) stops = session.MandatoryPitStops;
            if (_totalLaps < 8) stops = Math.Min(stops, 1);

            var stints = new List<PlannedStint>();

            if (stops <= 1)
            {
                // Mescola morbida e stint corto, oppure dura e stint lungo. Sceglie il tipo di
                // gara che quel pilota fara, e da li nascono undercut e overcut.
                bool startSoft = rnd.NextDouble() < 0.5;
                var first = startSoft ? Compounds.Soft : Compounds.Medium;
                var second = startSoft ? Compounds.Hard : Compounds.Hard;

                int ideal = (int)Math.Round(startSoft ? softLaps * 0.85 : mediumLaps * 0.85);
                int stop = Clamp(ideal + rnd.Next(-3, 4), 5, _totalLaps - 4);

                stints.Add(new PlannedStint { Compound = first.Kind, StartLap = 0, EndLap = stop });
                stints.Add(new PlannedStint { Compound = second.Kind, StartLap = stop, EndLap = _totalLaps });
            }
            else
            {
                int first = Clamp((int)Math.Round(softLaps * 0.8) + rnd.Next(-2, 3), 4, _totalLaps - 8);
                int second = Clamp(first + (int)Math.Round(mediumLaps * 0.8) + rnd.Next(-2, 3),
                                   first + 4, _totalLaps - 4);

                stints.Add(new PlannedStint { Compound = CompoundKind.Soft, StartLap = 0, EndLap = first });
                stints.Add(new PlannedStint { Compound = CompoundKind.Medium, StartLap = first, EndLap = second });
                stints.Add(new PlannedStint { Compound = CompoundKind.Hard, StartLap = second, EndLap = _totalLaps });
            }

            return stints;
        }

        // ---- Comandi giro per giro ---------------------------------------------------------------

        /// <summary>
        /// Calcola cosa deve fare ogni avversario adesso. Da chiamare a ogni aggiornamento: e
        /// deliberatamente senza stato temporale, cosi puo essere chiamata a qualsiasi frequenza.
        /// </summary>
        /// <param name="includePlayer">
        /// Vero quando la vettura del giocatore e guidata dal pilota automatico: allora riceve i
        /// comandi come tutte le altre, ed e cosi che rispetta safety car, bandiere e strategia
        /// invece di essere un corpo estraneo in mezzo al gruppo.
        /// </param>
        public List<AiCommand> Update(TelemetryFrame frame, RaceDirector raceControl, WeatherSnapshot weather,
                                      bool includePlayer = false)
        {
            var commands = new List<AiCommand>();
            if (!_active || frame == null) return commands;

            bool neutralised = raceControl != null &&
                               (raceControl.Status == RaceControlStatus.SafetyCar ||
                                raceControl.Status == RaceControlStatus.SafetyCarEnding ||
                                raceControl.Status == RaceControlStatus.VirtualSafetyCar ||
                                raceControl.Status == RaceControlStatus.FormationLap);

            double neutralisedSpeed = raceControl != null && raceControl.TargetSpeedKmh > 10
                ? raceControl.TargetSpeedKmh
                : 100;

            bool pitLaneOpen = raceControl == null || !raceControl.PitLaneClosed;
            double wetness = weather != null ? weather.Wetness : 0;

            foreach (var opponent in frame.Opponents)
            {
                if (opponent.IsPlayer && !includePlayer) continue;
                if (string.IsNullOrWhiteSpace(opponent.Name)) continue;
                if (SafetyCar.IsSafetyCar(opponent.Name)) continue;
                if (!_plans.TryGetValue(opponent.Name, out var plan)) continue;

                commands.Add(Command(plan, opponent, frame, neutralised, neutralisedSpeed,
                                     pitLaneOpen, wetness));
            }

            return commands;
        }

        private AiCommand Command(AiRacePlan plan, OpponentFrame opponent, TelemetryFrame frame,
                                  bool neutralised, double neutralisedSpeed, bool pitLaneOpen,
                                  double wetness)
        {
            int lap = opponent.CompletedLaps;
            AdvanceStint(plan, opponent);

            var command = new AiCommand
            {
                Name = opponent.Name,
                Aggression = plan.BaseAggression
            };

            // ---- Ritiro -------------------------------------------------------------------
            if (plan.RetirementLap > 0 && lap >= plan.RetirementLap)
            {
                plan.Retired = true;
                command.AiLevel = 70;
                command.TopSpeedKmh = 1;
                // Riproposto di continuo: la vettura resta ferma in pista, e da li la direzione
                // gara si accorge dell'incidente esattamente come farebbe con un pilota vero.
                command.StopSeconds = 30;
                return command;
            }

            // ---- Errore isolato ------------------------------------------------------------
            if (plan.MistakeLap > 0 && lap == plan.MistakeLap)
            {
                plan.MistakeLap = 0;
                command.StopSeconds = 3.5;
            }

            // Il bridge pubblica meno spesso della telemetria e il Lua revoca il rientro
            // quando Pit torna falso. Ripetere l'ordine fino alla sosta effettiva, anche
            // se nel frattempo cambia lo stato della neutralizzazione.
            command.Pit = plan.PitRequested;
            command.Compound = plan.RequestedCompound;

            // ---- Neutralizzazione -----------------------------------------------------------
            if (neutralised)
            {
                command.AiLevel = Clamp(plan.BaseAiLevel, 70, 100);
                command.TopSpeedKmh = neutralisedSpeed;

                // La sosta gratis sotto safety car: chi deve ancora fermarsi ne approfitta,
                // come succede sempre nella realta.
                //
                // Il giro minimo assoluto non e la stessa cosa del "tre giri dall inizio dello
                // stint", ed e li apposta. Quel conto parte dallo stint, e il primo stint comincia
                // al giro zero: basta che il conteggio dei giri sia piu avanti del previsto - e in
                // Assetto Corsa il giro di ricognizione conta come un giro - perche al semaforo
                // verde la condizione risulti gia soddisfatta. Con una neutralizzazione nei primi
                // istanti di gara il risultato e il gruppo intero in corsia box prima di aver
                // percorso un metro, che e esattamente cio che si e visto in pista.
                //
                // Due giri veri sono il minimo perche una sosta abbia senso: prima di allora non
                // c e gomma da cambiare e non c e strategia da sfruttare.
                if (pitLaneOpen && !plan.PitRequested && plan.NextStopLap > 0 && lap >= 2 &&
                    lap >= plan.Stints[plan.CurrentStint].StartLap + 3)
                {
                    plan.PitRequested = true;
                    command.Pit = true;
                    plan.RequestedCompound = Compounds.Get(plan.NextCompound).Code;
                    command.Compound = plan.RequestedCompound;
                }
                return command;
            }

            // ---- Ritmo ------------------------------------------------------------------------
            double penalty = plan.FormSeconds;
            penalty += DegradationPenalty(plan, lap);
            penalty += WeatherPenalty(plan, wetness);

            // Primi giri con il pieno: tutti un po' piu lenti, e la gara si apre col serbatoio.
            if (lap <= 2) penalty += 0.25;

            double levels = penalty / SecondsPerAiLevel;
            command.AiLevel = Clamp(plan.BaseAiLevel - levels, 70, 100);
            command.TopSpeedKmh = 0;

            // ---- Sosta ---------------------------------------------------------------------------
            var stint = plan.CurrentStint < plan.Stints.Count ? plan.Stints[plan.CurrentStint] : null;
            bool dueByPlan = stint != null && plan.NextStopLap > 0 && lap >= stint.EndLap;
            bool dueByWeather = NeedsWeatherStop(plan, wetness);

            if (!plan.PitRequested && pitLaneOpen && (dueByPlan || dueByWeather))
            {
                plan.PitRequested = true;
                command.Pit = true;
                plan.RequestedCompound = dueByWeather
                    ? Compounds.ForConditions(wetness).Code
                    : Compounds.Get(plan.NextCompound).Code;
                command.Compound = plan.RequestedCompound;
            }

            return command;
        }

        /// <summary>
        /// Perdita di ritmo per l'usura. Cresce piano dentro lo stint previsto e precipita dopo,
        /// che e la forma della curva vera: la gomma non cala, poi cala tutta insieme.
        /// </summary>
        private double DegradationPenalty(AiRacePlan plan, int lap)
        {
            if (DegradationStrength <= 0) return 0;
            var stint = plan.CurrentStint < plan.Stints.Count ? plan.Stints[plan.CurrentStint] : null;
            if (stint == null) return 0;

            int age = Math.Max(0, lap - plan.StintStartLap);
            int planned = Math.Max(1, stint.Laps);
            double share = (double)age / planned;

            double loss = share * 0.45;
            if (share > 1.0) loss += (share - 1.0) * 3.0;

            var spec = Compounds.Get(stint.Compound);
            loss += Math.Max(0, spec.WarmupLaps - age) * 0.3;

            return loss * DegradationStrength;
        }

        private static double WeatherPenalty(AiRacePlan plan, double wetness)
        {
            var current = Compounds.Get(plan.CurrentCompound);
            if (wetness < 0.05) return current.IsWetWeather ? 3.0 : 0;
            if (current.IsWetWeather) return 0;
            // Slick sul bagnato: il distacco diventa enorme, ed e giusto che lo sia.
            return 2.0 + wetness * 12.0;
        }

        private static bool NeedsWeatherStop(AiRacePlan plan, double wetness)
        {
            var current = Compounds.Get(plan.CurrentCompound);
            var wanted = Compounds.ForConditions(wetness);
            return current.Kind != wanted.Kind &&
                   (current.IsWetWeather != wanted.IsWetWeather);
        }

        /// <summary>Quando la sosta e avvenuta davvero, passa allo stint successivo.</summary>
        private void AdvanceStint(AiRacePlan plan, OpponentFrame opponent)
        {
            _pitCounts.TryGetValue(opponent.Name, out int previous);
            if (opponent.PitCount <= previous) return;

            _pitCounts[opponent.Name] = opponent.PitCount;
            plan.StopsDone++;
            plan.PitRequested = false;
            plan.RequestedCompound = "";
            plan.StintStartLap = opponent.CompletedLaps;
            if (plan.CurrentStint + 1 < plan.Stints.Count) plan.CurrentStint++;
        }

        private static int Clamp(int value, int min, int max) => Math.Max(min, Math.Min(max, value));

        private static double Clamp(double value, double min, double max) =>
            Math.Max(min, Math.Min(max, value));

        /// <summary>Riassunto leggibile della strategia, per il muretto box.</summary>
        public IEnumerable<string> Summary()
        {
            return _plans.Values
                .OrderBy(p => p.DriverName)
                .Select(p => p.DriverName + ": " +
                             string.Join(" ", p.Stints.Select(s => Compounds.Get(s.Compound).Code + s.Laps)) +
                             (p.RetirementLap > 0 ? "  (ritiro al " + p.RetirementLap + ")" : ""));
        }
    }
}
