using System;
using System.Collections.Generic;
using Pitwall.Live;

namespace Pitwall.Ai
{
    /// <summary>Cosa sta facendo una vettura in prove o qualifica.</summary>
    public enum LapKind
    {
        /// <summary>Ai box o ferma: non la stiamo guidando noi.</summary>
        Idle,
        /// <summary>Appena uscita: piano, fuori traiettoria, a scaldare le gomme.</summary>
        OutLap,
        /// <summary>Ultimo tratto del giro di uscita: si prende velocita per lanciarsi.</summary>
        BuildUp,
        /// <summary>Il giro vero, a tutta.</summary>
        Flying,
        /// <summary>Dopo il tempo: si rallenta e si rientra.</summary>
        InLap
    }

    /// <summary>
    /// Il comportamento del giro di uscita in prove libere e qualifica.
    ///
    /// In Formula 1 nessuno esce dai box e va subito a trecento all'ora: le gomme sono fredde e
    /// non ci sarebbe aderenza. Si fa un giro lento, si zigzaga per portarle in temperatura, si
    /// sta fuori dalla traiettoria per non intralciare chi sta spingendo, e solo alla fine ci si
    /// lancia per il giro buono. Dopo il tempo si rallenta e si rientra.
    ///
    /// Qui c'e solo la decisione: in che fase e ogni vettura e che andatura tenere. Applicarla
    /// alle vetture tocca all'app in pista, che e l'unica che puo parlarci.
    /// </summary>
    public sealed class OutLapDirector
    {
        private sealed class Car
        {
            public LapKind Kind = LapKind.Idle;
            public bool WasInPitLane = true;
            public int LapAtExit = -1;
            public double LastSpline;
            public int FlyingLaps;

            /// <summary>Quanti giri lanciati prevede l'uscita in corso.</summary>
            public int FlyingTarget;

            /// <summary>Mescola montata per questa uscita.</summary>
            public string Compound = "";

            /// <summary>Dove si trovava sul giro quando ha lasciato i box.</summary>
            public double ExitSpline;
        }

        /// <summary>Una uscita programmata: quando, per quanti giri, con che gomma.</summary>
        private sealed class Run
        {
            public double StartMinute;
            public double EndMinute;
            public int FlyingLaps;
            public string Compound = "";
        }

        private readonly Dictionary<string, Car> _cars =
            new Dictionary<string, Car>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, List<Run>> _plans =
            new Dictionary<string, List<Run>>(StringComparer.OrdinalIgnoreCase);

        private double _sessionMinutes = 60;
        private int _seed = 1;
        private string _planKey = "";

        /// <summary>Velocita massima nel giro di uscita, in km/h.</summary>
        public double OutLapSpeedKmh { get; set; } = 170;

        /// <summary>Velocita massima nell'ultimo tratto, quando ci si prepara al lancio.</summary>
        public double BuildUpSpeedKmh { get; set; } = 240;

        /// <summary>Velocita massima nel giro di rientro.</summary>
        public double InLapSpeedKmh { get; set; } = 190;

        /// <summary>
        /// Da che punto del giro ci si prepara al lancio. 0.75 vuol dire nell'ultimo quarto.
        /// </summary>
        public double BuildUpFrom { get; set; } = 0.75;

        /// <summary>
        /// Di quanto ci si sposta dalla traiettoria mentre si scaldano le gomme, in metri. Il
        /// segno alterna fra le vetture: se si spostassero tutte dalla stessa parte si
        /// ritroverebbero in fila indiana fuori traiettoria, che e il problema di partenza.
        /// </summary>
        public double OffLineMeters { get; set; } = 2.6;

        /// <summary>Quanti giri lanciati di fila prima di rientrare.</summary>
        public int FlyingLapsPerRun { get; set; } = 1;

        /// <summary>
        /// Vero se il DRS va concesso solo nel giro lanciato. In zona il regolamento lo
        /// permetterebbe sempre, ma un pilota non apre l'ala mentre scalda le gomme.
        /// </summary>
        public bool DrsOnlyOnFlyingLap { get; set; } = true;

        /// <summary>Vero se dobbiamo decidere noi quanta benzina imbarcare.</summary>
        public bool ManageFuel { get; set; } = true;

        public void Reset()
        {
            _cars.Clear();
            _plans.Clear();
            _planKey = "";
        }

        /// <summary>
        /// Rifa il programma solo quando serve. La chiave e la sessione o la manche di qualifica:
        /// finche resta la stessa il programma non cambia, cosi le uscite non si rimescolano a
        /// ogni fotogramma. Quando si passa da Q1 a Q2 la chiave cambia e si riparte da zero,
        /// che e giusto perche fra una manche e l'altra tutti rientrano.
        /// </summary>
        public void EnsurePlan(string key, double sessionMinutes, IEnumerable<string> drivers,
                               bool qualifying, int seed)
        {
            if (key == _planKey && _plans.Count > 0) return;
            Begin(sessionMinutes, drivers, qualifying, seed);
            _planKey = key ?? "";
        }

        /// <summary>Vero quando c'e un programma da seguire.</summary>
        public bool HasPlan => _plans.Count > 0;

        /// <summary>I piloti per cui c'e un programma.</summary>
        public IEnumerable<string> PlannedDrivers => _plans.Keys;

        /// <summary>Vero se a questo minuto il programma vuole questo pilota in pista.</summary>
        public bool ScheduledOut(string name, double minute) =>
            name != null && ActiveRun(name, minute) != null;

        /// <summary>Il programma di un pilota in forma leggibile, per il muretto e i controlli.</summary>
        public string DescribePlan(string name)
        {
            List<Run> plan;
            if (name == null || !_plans.TryGetValue(name, out plan)) return "";

            var parts = new List<string>();
            foreach (var run in plan)
                parts.Add(string.Format("{0}' {1}x{2}",
                    (int)Math.Round(run.StartMinute),
                    string.IsNullOrEmpty(run.Compound) ? "-" : run.Compound,
                    Math.Max(1, run.FlyingLaps)));

            return string.Join("  ", parts);
        }

        /// <summary>
        /// Prepara il programma della sessione: a ogni pilota le sue uscite.
        ///
        /// Non escono tutti insieme, ed e il punto. In una sessione vera il traffico si forma e
        /// si dirada: qualcuno prova subito, qualcuno aspetta la pista gommata, e negli ultimi
        /// minuti escono quasi tutti. Qui le uscite si distribuiscono nel tempo con uno
        /// scaglionamento che dipende dal pilota, cosi resta lo stesso per tutto il weekend
        /// invece di cambiare a ogni ricaricamento.
        /// </summary>
        public void Begin(double sessionMinutes, IEnumerable<string> drivers, bool qualifying, int seed)
        {
            Reset();
            _sessionMinutes = Math.Max(5, sessionMinutes);
            _seed = seed;

            var names = new List<string>();
            foreach (var n in drivers ?? new List<string>())
                if (!string.IsNullOrWhiteSpace(n)) names.Add(n);

            for (int i = 0; i < names.Count; i++)
            {
                var rnd = new Random((seed ^ StableHash(names[i])) & 0x7FFFFFFF);
                _plans[names[i]] = qualifying ? QualifyingRuns(rnd) : PracticeRuns(rnd);
            }
        }

        /// <summary>
        /// Un hash che non cambia fra un avvio e l'altro.
        ///
        /// Non si usa <c>string.GetHashCode</c> di proposito: su .NET moderno e volutamente
        /// casuale a ogni processo, e il programma delle uscite sarebbe diverso ogni volta che
        /// si riaccende il gioco. Qui invece il weekend deve essere quello: se un avversario ti
        /// trova in traffico nel suo ultimo tentativo, era scritto nei dati, non nella fortuna.
        /// </summary>
        private static int StableHash(string text)
        {
            int hash = 17;
            foreach (var ch in text) hash = (hash * 31 + ch) & 0x7FFFFFFF;
            return hash;
        }

        /// <summary>
        /// Una uscita pescata dentro una finestra. La finestra e la durata decidono quante
        /// vetture si trovano in pista nello stesso momento: una finestra larga e una uscita
        /// corta sfilacciano il gruppo, una finestra strettissima li manda fuori tutti insieme.
        /// E la leva con cui si regola il traffico, e tutto e in frazioni della sessione cosi
        /// un turno di quindici minuti e uno di un'ora si comportano allo stesso modo.
        /// </summary>
        private Run Slot(Random rnd, double fromShare, double toShare, double lengthShare,
                         int flyingLaps, string compound)
        {
            double from = _sessionMinutes * fromShare;
            double to = _sessionMinutes * toShare;
            double start = from + rnd.NextDouble() * Math.Max(0, to - from);
            double length = Math.Max(2, _sessionMinutes * lengthShare);

            return new Run
            {
                StartMinute = start,
                EndMinute = Math.Min(_sessionMinutes, start + length),
                FlyingLaps = flyingLaps,
                Compound = compound
            };
        }

        /// <summary>
        /// In qualifica si esce due volte: un tentativo presto, per avere un tempo in cassaforte,
        /// e uno negli ultimi minuti quando la pista e al meglio. Il primo e sfilacciato, il
        /// secondo no: la finestra e strettissima di proposito, perche la corsa al tempo del
        /// finale - tutti fuori insieme, traffico, chi non trova lo spazio - e la qualifica.
        /// </summary>
        private List<Run> QualifyingRuns(Random rnd)
        {
            return new List<Run>
            {
                Slot(rnd, 0.03, 0.38, 0.20, 1, "S"),
                Slot(rnd, 0.62, 0.78, 0.25, 1, "S")
            };
        }

        /// <summary>
        /// In prove libere il programma e piu vario: un giro di installazione per controllare che
        /// la vettura stia insieme, un tentativo sul giro secco con la gomma morbida, e una
        /// simulazione di passo gara piu lunga con una mescola che regge.
        /// </summary>
        private List<Run> PracticeRuns(Random rnd)
        {
            return new List<Run>
            {
                Slot(rnd, 0.01, 0.08, 0.110, 1, "H"),
                Slot(rnd, 0.22, 0.48, 0.140, 2, "S"),
                Slot(rnd, 0.58, 0.84, 0.180, 5, "M")
            };
        }

        /// <summary>
        /// Per quanti giri rifornire una vettura in vista di un'uscita: il giro di uscita, i
        /// giri lanciati, il giro di rientro, e mezzo giro di margine. Non si imbarca il
        /// pieno per fare tre giri, che e il difetto da correggere.
        /// </summary>
        private static double LapsToCarry(Run run) =>
            Math.Max(1, run.FlyingLaps) + 2.5;

        /// <summary>L'uscita in corso adesso per questo pilota, nulla se deve stare ai box.</summary>
        private Run ActiveRun(string name, double minute)
        {
            List<Run> plan;
            if (!_plans.TryGetValue(name, out plan)) return null;

            foreach (var run in plan)
                if (minute >= run.StartMinute && minute < run.EndMinute) return run;

            return null;
        }

        /// <summary>Quanti minuti mancano alla prossima uscita, meno di zero se non ce ne sono.</summary>
        public double MinutesToNextRun(string name, double minute)
        {
            var next = NextRun(name, minute);
            return next != null ? next.StartMinute - minute : -1;
        }

        private Run NextRun(string name, double minute)
        {
            List<Run> plan;
            if (name == null || !_plans.TryGetValue(name, out plan)) return null;

            foreach (var run in plan)
                if (run.StartMinute > minute) return run;

            return null;
        }

        /// <summary>
        /// La mescola che si monta al rientro: quella della prossima uscita, non di questa. E il
        /// motivo per cui una sosta in prove libere serve, invece di essere solo tempo perso.
        /// </summary>
        private string NextCompound(string name, double minute)
        {
            var next = NextRun(name, minute);
            return next != null ? next.Compound : "";
        }

        private Car For(string name)
        {
            Car car;
            if (!_cars.TryGetValue(name, out car))
            {
                car = new Car();
                _cars[name] = car;
            }
            return car;
        }

        /// <summary>
        /// Aggiorna la fase di una vettura e scrive nel comando l'andatura che le tocca.
        /// Restituisce falso se la vettura non va guidata da noi adesso.
        /// </summary>
        public bool Apply(OpponentFrame opponent, AiCommand command, double sessionMinute = -1)
        {
            if (opponent == null || command == null || string.IsNullOrWhiteSpace(opponent.Name))
                return false;

            var car = For(opponent.Name);
            bool inPitLane = opponent.InPitLane || opponent.InPit;
            bool scheduled = sessionMinute >= 0 && _plans.ContainsKey(opponent.Name);
            var run = scheduled ? ActiveRun(opponent.Name, sessionMinute) : null;

            // Uscita dai box: comincia il giro di preparazione.
            if (car.WasInPitLane && !inPitLane)
            {
                car.Kind = LapKind.OutLap;
                car.LapAtExit = opponent.CompletedLaps;
                car.FlyingLaps = 0;
                car.ExitSpline = opponent.TrackPositionPercent;
                if (run != null)
                {
                    car.FlyingTarget = Math.Max(1, run.FlyingLaps);
                    car.Compound = run.Compound;
                }
            }
            car.WasInPitLane = inPitLane;

            if (inPitLane)
            {
                car.Kind = LapKind.Idle;
                car.FlyingLaps = 0;

                if (!scheduled) return false;

                command.LapKind = LapKind.Idle.ToString();
                command.Warmup = false;
                command.SplineOffset = 0;
                command.TopSpeedKmh = 0;

                // Ferma ai box il DRS non ha senso, e la benzina si imbarca adesso: e il solo
                // momento in cui si puo, perche cambiarla in pista annulla il giro.
                command.DrsAllowed = false;

                if (run == null)
                {
                    // Non e il suo turno. Resta ferma nel box: e cosi che la pista resta libera
                    // invece di avere venti vetture in giro tutte insieme per un'ora.
                    command.HoldInPit = true;
                    var next = NextRun(opponent.Name, sessionMinute);
                    command.Compound = next != null ? next.Compound : "";
                    if (ManageFuel && next != null) command.FuelLaps = LapsToCarry(next);
                    return true;
                }

                // Tocca a lei: gomma del programma montata, e la si lascia andare.
                command.HoldInPit = false;
                command.Compound = run.Compound;
                car.FlyingTarget = Math.Max(1, run.FlyingLaps);
                car.Compound = run.Compound;
                if (ManageFuel) command.FuelLaps = LapsToCarry(run);
                return true;
            }

            // Finestra chiusa mentre era in pista: si rientra, come quando in televisione la
            // bandiera a scacchi arriva e chi e fuori completa il giro e va dentro.
            if (scheduled && run == null && car.Kind != LapKind.Idle) car.Kind = LapKind.InLap;

            double spline = opponent.TrackPositionPercent;

            switch (car.Kind)
            {
                case LapKind.OutLap:
                    // Nell'ultimo tratto si prende velocita, per arrivare lanciati al traguardo.
                    if (spline >= BuildUpFrom) car.Kind = LapKind.BuildUp;
                    break;

                case LapKind.BuildUp:
                    // Linea del traguardo: da qui si spinge.
                    if (opponent.CompletedLaps > car.LapAtExit)
                    {
                        car.Kind = LapKind.Flying;
                        car.LapAtExit = opponent.CompletedLaps;
                    }
                    break;

                case LapKind.Flying:
                    if (opponent.CompletedLaps > car.LapAtExit)
                    {
                        car.FlyingLaps++;
                        car.LapAtExit = opponent.CompletedLaps;
                        int target = car.FlyingTarget > 0 ? car.FlyingTarget : Math.Max(1, FlyingLapsPerRun);
                        if (car.FlyingLaps >= target) car.Kind = LapKind.InLap;
                    }
                    break;

                case LapKind.InLap:
                    break;

                default:
                    // Gia in pista quando ci siamo attaccati: la si lascia spingere, non si sa
                    // da quanto e fuori e rallentarla rovinerebbe un giro magari gia lanciato.
                    car.Kind = LapKind.Flying;
                    break;
            }

            car.LastSpline = spline;
            command.LapKind = car.Kind.ToString();

            switch (car.Kind)
            {
                case LapKind.OutLap:
                    command.TopSpeedKmh = OutLapSpeedKmh * OutLapRamp(car, spline);
                    command.Warmup = true;
                    command.SplineOffset = SideFor(opponent.Name);
                    command.DrsAllowed = !DrsOnlyOnFlyingLap;
                    return true;

                case LapKind.BuildUp:
                    command.TopSpeedKmh = BuildUpSpeedKmh;
                    command.Warmup = false;
                    command.SplineOffset = 0;
                    command.DrsAllowed = !DrsOnlyOnFlyingLap;
                    return true;

                case LapKind.InLap:
                    command.TopSpeedKmh = InLapSpeedKmh;
                    command.Warmup = false;
                    command.SplineOffset = SideFor(opponent.Name);
                    command.Pit = true;
                    command.DrsAllowed = !DrsOnlyOnFlyingLap;
                    // Al rientro si monta la gomma della prossima uscita, non di questa.
                    if (scheduled) command.Compound = NextCompound(opponent.Name, sessionMinute);
                    return true;

                default:
                    command.TopSpeedKmh = 0;     // nessun limite: e il giro buono
                    command.Warmup = false;
                    command.SplineOffset = 0;
                    command.DrsAllowed = true;   // il giro buono: ala aperta, dove si puo
                    return true;
            }
        }

        /// <summary>
        /// Quanto della velocita di uscita si concede, man mano che il giro avanza.
        ///
        /// Appena fuori dai box le gomme sono fredde e non c'e aderenza: partire a tutta non lo
        /// fa nessuno, e una IA che lo fa finisce fuori alla prima curva o consuma la gomma
        /// prima ancora di cominciare. Si parte a poco piu di meta e si arriva al ritmo pieno
        /// dopo mezzo giro, che e il tempo che ci vuole perche la gomma cominci a rispondere.
        ///
        /// Si misura sulla distanza percorsa da quando ha lasciato i box, non sul tempo: una
        /// vettura ferma in corsia non sta scaldando niente, e il tempo passerebbe lo stesso.
        /// </summary>
        private static double OutLapRamp(Car car, double spline)
        {
            double percorso = spline - car.ExitSpline;
            if (percorso < 0) percorso += 1;        // ha passato il traguardo nel frattempo

            const double SaliteIn = 0.50;           // mezzo giro per arrivare a regime
            const double Partenza = 0.55;           // poco piu di meta della velocita piena

            if (percorso >= SaliteIn) return 1.0;
            return Partenza + (1.0 - Partenza) * (percorso / SaliteIn);
        }

        /// <summary>
        /// Da che parte si scosta una vettura. Dipende dal nome, cosi resta la stessa per tutto
        /// il weekend invece di cambiare a ogni uscita, e si alterna fra i piloti.
        /// </summary>
        private double SideFor(string name) =>
            (StableHash(name) % 2 == 0 ? 1 : -1) * OffLineMeters;
    }
}
