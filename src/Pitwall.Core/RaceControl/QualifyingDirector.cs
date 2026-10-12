using System;
using System.Collections.Generic;
using System.Linq;
using Pitwall.Live;
using Pitwall.Model;
using Pitwall.Rules;

using Pitwall.Lingue;

namespace Pitwall.RaceControl
{
    /// <summary>
    /// Le qualifiche a eliminazione, Q1 Q2 Q3.
    ///
    /// Assetto Corsa conosce una sola sessione di qualifica, aperta e lunga quanto si vuole.
    /// Il formato della Formula 1 invece sono tre manche dentro la stessa sessione: alla fine
    /// di ognuna i piu lenti vengono esclusi e non girano piu, e la griglia si compone al
    /// contrario, dal fondo verso la pole.
    ///
    /// Qui dentro c'e solo il conteggio: in che manche siamo, quanto manca, chi e in zona
    /// esclusione adesso e chi e gia fuori. Chi e fuori viene rimandato ai box dall'app in
    /// pista, che e l'unica che puo parlare alle vetture.
    /// </summary>
    public sealed class QualifyingDirector
    {
        private QualifyingRules _rules = new QualifyingRules();
        private readonly List<string> _order = new List<string>();
        private readonly Dictionary<string, int> _eliminatedIn =
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        private int _segment = -1;
        private int _entrants;

        /// <summary>La sessione su cui e stato fatto il piano, per poterlo rifare.</summary>
        private SessionConfig _session;

        /// <summary>Vero quando il piano e gia stato rifatto sulla durata misurata.</summary>
        private bool _replanned;

        /// <summary>Durata di Q1, Q2 e Q3 in minuti, come si corrono davvero in questa sessione.</summary>
        private readonly double[] _minuti = { 18, 15, 12 };

        /// <summary>Quanti escono alla fine di Q1 e di Q2 in questa sessione.</summary>
        private readonly int[] _tagli = { 5, 5 };

        /// <summary>Vero quando questa sessione si corre a eliminazione.</summary>
        public bool Active { get; private set; }

        /// <summary>Manche in corso: 1, 2 o 3. Zero quando e finita.</summary>
        public int Segment => _segment >= 0 && _segment < 3 ? _segment + 1 : 0;

        /// <summary>Come si chiama la manche in corso: Q1, Q2, Q3.</summary>
        public string SegmentName => Segment > 0 ? "Q" + Segment : "";

        /// <summary>Secondi che mancano alla fine della manche in corso.</summary>
        public double SecondsLeft { get; private set; }

        /// <summary>Quanti passano alla manche successiva.</summary>
        public int Surviving { get; private set; }

        /// <summary>Minuti passati dall'inizio della manche in corso.</summary>
        public double SegmentElapsedMinutes { get; private set; }

        /// <summary>Quanto dura la manche in corso, in minuti.</summary>
        public double SegmentMinutes =>
            _segment >= 0 && _segment <= 2 ? SegmentSeconds(_segment) / 60.0 : 0;

        /// <summary>Piloti gia esclusi, dal primo eliminato all'ultimo.</summary>
        public IReadOnlyList<string> Eliminated => _order;

        /// <summary>Quanto durano davvero Q1, Q2 e Q3 in questa sessione, in minuti.</summary>
        public IReadOnlyList<double> PlannedMinutes => _minuti;

        /// <summary>Quanti escono davvero alla fine di Q1 e di Q2 in questa sessione.</summary>
        public IReadOnlyList<int> PlannedEliminations => _tagli;

        /// <summary>Messaggio da mostrare quando una manche si chiude.</summary>
        public string LastNotice { get; private set; } = "";

        /// <summary>Chiamato quando una manche si chiude, con i nomi appena esclusi.</summary>
        public event Action<string, IReadOnlyList<string>> SegmentClosed;

        public void Begin(SessionConfig session, QualifyingRules rules, int entrants)
        {
            _rules = rules ?? new QualifyingRules();
            _order.Clear();
            _eliminatedIn.Clear();
            _entrants = Math.Max(1, entrants);
            LastNotice = "";

            Active = session != null
                  && (session.Kind == SessionKind.Qualifying || session.Kind == SessionKind.SprintQualifying)
                  && session.QualifyingFormat == QualifyingFormat.Knockout
                  && _rules.Enabled;

            _session = session;
            _replanned = false;
            Plan(session, 0);

            _segment = Active ? 0 : -1;
            SecondsLeft = Active ? SegmentSeconds(0) : 0;
            SegmentElapsedMinutes = 0;
            Surviving = Active ? _entrants : 0;
        }

        /// <summary>
        /// Spegne le qualifiche a eliminazione.
        ///
        /// Serve quando ci si accorge che la sessione non e quella che si credeva: il tipo di
        /// sessione arriva da SimHub come testo e puo sbagliare, e una Q1 che continua a
        /// contare durante una gara si vede sull'HUD e confonde tutto.
        /// </summary>
        public void Suspend()
        {
            Active = false;
            _segment = -1;
            SecondsLeft = 0;
            Surviving = 0;
            SegmentElapsedMinutes = 0;
        }

        /// <summary>
        /// Riceve la durata che la sessione ha **davvero**, misurata dal cronometro del gioco.
        ///
        /// Il piano nasce da quello che c'e scritto nel campionato, perche a quel momento e tutto
        /// quello che si sa. Ma le due cose possono non coincidere, e quando non coincidono ha
        /// ragione il gioco: in Content Manager la durata della sessione si cambia a mano prima di
        /// andare in pista, e un banco di prova che scrive race.ini la cambia senza passare dal
        /// campionato. Succede davvero: con quarantacinque minuti scritti nel campionato e tre
        /// corsi per davvero, la Q1 da diciotto minuti non scadeva mai e dopo la Q1 partiva la
        /// gara. Lo stesso difetto che si credeva risolto, entrato da un'altra porta.
        ///
        /// Si rifa il piano una volta sola e solo prima che una manche si sia chiusa: cambiare le
        /// durate a eliminazioni gia fatte sposterebbe il traguardo a chi sta girando.
        /// </summary>
        public void Observe(double realMinutes)
        {
            if (!Active || _replanned || _segment != 0) return;
            if (realMinutes <= 0.5 || _session == null) return;

            double previsti = _minuti[0] + _minuti[1] + _minuti[2];

            // Un filo di scarto non vale una rifacitura: il cronometro del gioco e una stima e
            // oscilla di qualche secondo.
            if (Math.Abs(previsti - realMinutes) < 0.5) { _replanned = true; return; }

            Plan(_session, realMinutes);
            _replanned = true;

            // La manche in corso e la prima, quindi ricomincia da adesso con la durata nuova.
            SecondsLeft = SegmentSeconds(0);
        }

        /// <summary>
        /// Decide quanto durano le tre manche e quanti ne escono, per questa sessione.
        ///
        /// Serviva perche qui si leggeva solo il regolamento del campionato - diciotto, quindici e
        /// dodici minuti - ignorando sia la durata vera della sessione sia i valori che il
        /// configuratore scrive gara per gara. Il risultato era che in una qualifica piu corta di
        /// quarantacinque minuti la Q1 non arrivava mai a scadere: nessuno veniva eliminato, le tre
        /// manche non esistevano, e dopo la Q1 cominciava direttamente la gara.
        ///
        /// Le durate si prendono dalla gara se le ha, altrimenti dal campionato, e in ogni caso
        /// vengono **riportate dentro** il tempo che la sessione ha davvero, in proporzione: una
        /// qualifica da dieci minuti diventa circa 4 / 3,3 / 2,7 e le tre manche si corrono per
        /// intero. Meglio tre manche corte che una sola che non finisce.
        ///
        /// I tagli vengono limitati al numero di iscritti: con dodici vetture non si possono
        /// eliminare cinque e cinque, perche in Q3 non resterebbe nessuno.
        /// </summary>
        /// <param name="measuredMinutes">
        /// La durata misurata dal gioco, se la si conosce; zero per usare quella configurata.
        /// </param>
        private void Plan(SessionConfig session, double measuredMinutes)
        {
            double[] voluti =
            {
                Math.Max(1, _rules.Q1Minutes),
                Math.Max(1, _rules.Q2Minutes),
                Math.Max(1, _rules.Q3Minutes)
            };

            if (session != null && session.KnockoutSegmentMinutes != null &&
                session.KnockoutSegmentMinutes.Count >= 3)
            {
                for (int i = 0; i < 3; i++)
                {
                    if (session.KnockoutSegmentMinutes[i] > 0) voluti[i] = session.KnockoutSegmentMinutes[i];
                }
            }

            // Il tempo che la sessione ha davvero. Solo a tempo: una qualifica a giri non ha una
            // scadenza su cui riportare le manche, e allora si tengono le durate chieste.
            double disponibili = measuredMinutes > 0.5
                ? measuredMinutes
                : (session != null && session.LengthMode == SessionLengthMode.Minutes
                    ? session.Length : 0);

            double pause = Math.Max(0, _rules.BreakMinutes) * 2;
            double somma = voluti[0] + voluti[1] + voluti[2] + pause;

            if (disponibili > 0.5 && somma > disponibili)
            {
                // Si restringe tutto in proporzione, pause comprese, lasciando un filo di margine:
                // una manche che scade nello stesso istante in cui la sessione chiude non fa in
                // tempo a eliminare nessuno.
                double fattore = disponibili * 0.97 / somma;
                for (int i = 0; i < 3; i++) voluti[i] *= fattore;
            }

            for (int i = 0; i < 3; i++) _minuti[i] = Math.Max(0.4, voluti[i]);

            int[] tagli = { Math.Max(0, _rules.Q1Eliminations), Math.Max(0, _rules.Q2Eliminations) };

            if (session != null && session.KnockoutEliminations != null &&
                session.KnockoutEliminations.Count >= 2)
            {
                for (int i = 0; i < 2; i++)
                {
                    if (session.KnockoutEliminations[i] >= 0) tagli[i] = session.KnockoutEliminations[i];
                }
            }

            // Due vetture devono restare in pista anche nella manche finale, altrimenti la pole si
            // assegna a tavolino.
            int rimasti = _entrants;
            for (int i = 0; i < 2; i++)
            {
                tagli[i] = Math.Max(0, Math.Min(tagli[i], rimasti - 2));
                rimasti -= tagli[i];
            }

            _tagli[0] = tagli[0];
            _tagli[1] = tagli[1];
        }

        /// <summary>Durata di una manche in secondi, con l'indice da zero.</summary>
        private double SegmentSeconds(int index)
        {
            if (index < 0) index = 0;
            if (index > 2) index = 2;
            return _minuti[index] * 60.0;
        }

        /// <summary>Quanti ne escono alla fine di una manche.</summary>
        private int EliminationsAfter(int index)
        {
            if (index == 0) return _tagli[0];
            if (index == 1) return _tagli[1];
            return 0;
        }

        /// <summary>Vero se questo pilota e gia fuori e non deve piu girare.</summary>
        public bool IsEliminated(string driverName) =>
            !string.IsNullOrEmpty(driverName) && _eliminatedIn.ContainsKey(driverName);

        /// <summary>
        /// Avanza il conteggio. <paramref name="standings"/> va passata ordinata per tempo, dal
        /// piu veloce al piu lento, e deve contenere solo chi e ancora in pista.
        /// </summary>
        public void Update(double sessionSeconds, IReadOnlyList<string> standings)
        {
            if (!Active || _segment < 0 || _segment > 2) return;

            double start = 0;
            for (int i = 0; i < _segment; i++) start += SegmentSeconds(i) + _rules.BreakMinutes * 60.0;

            double end = start + SegmentSeconds(_segment);
            SecondsLeft = Math.Max(0, end - sessionSeconds);
            SegmentElapsedMinutes = Math.Max(0, (sessionSeconds - start) / 60.0);

            int cut = EliminationsAfter(_segment);
            if (SecondsLeft <= 0) CloseSegment(standings, cut);

            // Si ricalcola dopo l'eventuale chiusura, altrimenti nel fotogramma in cui una
            // manche finisce si leggerebbe ancora il numero di quella appena conclusa.
            Surviving = Math.Max(0, Running() - EliminationsAfter(_segment));
        }

        /// <summary>Quanti piloti sono ancora in corsa adesso.</summary>
        public int Running() => Math.Max(0, _entrants - _order.Count);

        private void CloseSegment(IReadOnlyList<string> standings, int cut)
        {
            var still = (standings ?? new List<string>())
                .Where(n => !string.IsNullOrWhiteSpace(n) && !IsEliminated(n))
                .ToList();

            var dropped = new List<string>();
            if (cut > 0 && still.Count > cut)
            {
                // I piu lenti sono in fondo alla classifica dei tempi. Vengono esclusi in blocco
                // e la loro posizione in griglia e gia decisa: occupano le ultime caselle
                // disponibili, nell'ordine in cui erano.
                dropped = still.Skip(still.Count - cut).ToList();
                foreach (var name in dropped)
                {
                    _eliminatedIn[name] = _segment + 1;
                    _order.Add(name);
                }
            }

            string closed = SegmentName;
            _segment++;

            if (_segment > 2)
            {
                _segment = 3;
                SecondsLeft = 0;
                LastNotice = Testi.T("qualifica.conclusa");
            }
            else
            {
                SecondsLeft = SegmentSeconds(_segment);
                LastNotice = dropped.Count > 0
                    ? Testi.T("qualifica.chiusa", closed, string.Join(", ", dropped))
                    : Testi.T("qualifica.chiusa.nessuno", closed);
            }

            SegmentClosed?.Invoke(closed, dropped);
        }

        /// <summary>
        /// La griglia finale. Chi e arrivato in fondo viene messo in fondo, e davanti a tutti
        /// restano i tempi dell'ultima manche: e esattamente il modo in cui si compone davvero,
        /// dal fondo verso la pole.
        /// </summary>
        public List<string> FinalOrder(IReadOnlyList<string> lastSegmentStandings)
        {
            var grid = (lastSegmentStandings ?? new List<string>())
                .Where(n => !string.IsNullOrWhiteSpace(n) && !IsEliminated(n))
                .ToList();

            // Gli esclusi si aggiungono per gruppi, dal gruppo piu veloce al piu lento: prima chi
            // e uscito in Q2, poi chi e uscito in Q1. Dentro ogni gruppo l'ordine resta quello in
            // cui sono stati esclusi, che e quello dei loro tempi.
            //
            // Qui si scorreva _order dal fondo, con il commento "l'ultimo escluso e il piu veloce
            // fra loro". Fra i **gruppi** era giusto, dentro ogni gruppo no: rovesciava anche
            // quello, e il piu lento della Q2 si trovava davanti al piu veloce della Q2. In
            // griglia si vedeva undicesimo chi aveva fatto il quindicesimo tempo.
            for (int manche = 2; manche >= 1; manche--)
            {
                foreach (var name in _order)
                {
                    int uscito;
                    if (_eliminatedIn.TryGetValue(name, out uscito) && uscito == manche) grid.Add(name);
                }
            }

            // Chi fosse stato escluso in una manche fuori da quelle previste non deve sparire
            // dalla griglia: senza questo un posto resterebbe vuoto e nessuno saprebbe perche.
            foreach (var name in _order)
            {
                if (!grid.Contains(name)) grid.Add(name);
            }

            return grid;
        }

        /// <summary>
        /// Chi e in zona esclusione adesso, cioe chi verrebbe eliminato se la manche finisse in
        /// questo momento. E la riga rossa che in televisione si guarda piu dei tempi.
        /// </summary>
        public List<string> DropZone(IReadOnlyList<string> standings)
        {
            int cut = EliminationsAfter(_segment);
            if (!Active || cut <= 0 || standings == null) return new List<string>();

            var still = standings.Where(n => !IsEliminated(n)).ToList();
            return still.Count <= cut ? still : still.Skip(still.Count - cut).ToList();
        }
    }
}
