using System;
using System.Collections.Generic;
using Pitwall.Model;
using Pitwall.RaceControl;
using Pitwall.Rules;

namespace Pitwall.Simulator
{
    /// <summary>
    /// Le qualifiche a eliminazione, provate senza avviare il gioco.
    ///
    /// Tutti questi controlli nascono dallo stesso difetto, che e rimasto nascosto a lungo perche
    /// non produceva nessun errore: il regista leggeva soltanto le durate del regolamento del
    /// campionato - diciotto, quindici e dodici minuti - e ignorava sia la durata vera della
    /// sessione sia i valori che il configuratore scrive gara per gara.
    ///
    /// In una qualifica piu corta di quarantacinque minuti la Q1 non arrivava mai a scadere.
    /// Nessuno veniva eliminato, Q2 e Q3 non cominciavano mai, e quando la sessione si chiudeva
    /// partiva direttamente la gara con una griglia che non veniva dalle qualifiche. Visto da
    /// fuori sembrava che le qualifiche a eliminazione non fossero mai state scritte.
    /// </summary>
    internal static class QualifyingChecks
    {
        public static void Run(Action<string, Action> check)
        {
            check("Qualifica: tre manche dentro una sessione corta", SegmentsFitShortSession);
            check("Qualifica: durate della gara preferite a quelle del campionato", RoundOverridesChampionship);
            check("Qualifica: eliminazioni limitate agli iscritti", EliminationsClampToField);
            check("Qualifica: sessione lunga lascia le durate chieste", LongSessionKeepsRules);
            check("Qualifica: griglia dal fondo verso la pole", GridBuiltBackwards);
            check("Qualifica: la durata misurata vince su quella configurata", MeasuredLengthWins);
            check("Qualifica: durata misurata ignorata a eliminazioni gia fatte", MeasuredLengthIgnoredLate);
        }

        /// <summary>
        /// Quello che il gioco misura vince su quello che il campionato dichiara.
        ///
        /// La durata della sessione si cambia a mano in Content Manager prima di andare in pista, e
        /// il banco di prova la scrive in race.ini senza passare dal campionato. Con quarantacinque
        /// minuti dichiarati e tre corsi davvero, la Q1 da diciotto minuti non scadeva mai: lo stesso
        /// difetto di prima, entrato da un altra porta.
        /// </summary>
        private static void MeasuredLengthWins()
        {
            var order = Field(20);
            var director = new QualifyingDirector();

            // Il campionato dice quarantacinque minuti, e li le manche ci stanno senza stringerle.
            director.Begin(Session(45), Rules(), order.Count);
            Require(Math.Abs(director.PlannedMinutes[0] - 18) < 0.01, "Piano di partenza inatteso.");

            // Il gioco ne corre tre.
            director.Observe(3);

            var piano = director.PlannedMinutes;
            double totale = piano[0] + piano[1] + piano[2];
            Require(totale <= 3,
                    "Le manche chiedono " + totale.ToString("0.0") + "' in una sessione da 3'.");

            int closed = RunToEnd(director, order);
            Require(closed == 3, "Si sono chiuse " + closed + " manche invece di tre.");
            Require(director.Eliminated.Count == 10,
                    "Eliminati " + director.Eliminated.Count + " piloti invece di dieci.");
        }

        /// <summary>
        /// A eliminazioni gia fatte la durata misurata non sposta piu niente.
        ///
        /// Rifare il piano mentre si gira in Q2 sposterebbe il traguardo a chi e in pista: chi aveva
        /// ancora due minuti per migliorare se li vedrebbe diventare trenta secondi.
        /// </summary>
        private static void MeasuredLengthIgnoredLate()
        {
            var order = Field(20);
            var director = new QualifyingDirector();
            director.Begin(Session(45), Rules(), order.Count);

            // Si corre tutta la Q1 e la si chiude.
            double fine = director.PlannedMinutes[0] * 60 + 1;
            for (double t = 0; t <= fine; t += 0.25)
            {
                director.Update(t, order.FindAll(n => !director.IsEliminated(n)));
            }

            Require(director.Eliminated.Count == 5, "La Q1 non si e chiusa come previsto.");

            double prima = director.PlannedMinutes[1];
            director.Observe(3);
            Require(Math.Abs(director.PlannedMinutes[1] - prima) < 0.01,
                    "Il piano e cambiato a eliminazioni gia fatte.");
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        private static QualifyingRules Rules() => new QualifyingRules
        {
            Enabled = true,
            Q1Minutes = 18, Q2Minutes = 15, Q3Minutes = 12,
            Q1Eliminations = 5, Q2Eliminations = 5
        };

        private static SessionConfig Session(int minutes) => new SessionConfig
        {
            Kind = SessionKind.Qualifying,
            QualifyingFormat = QualifyingFormat.Knockout,
            LengthMode = SessionLengthMode.Minutes,
            Length = minutes
        };

        /// <summary>
        /// Porta avanti il cronometro fino alla fine del piano e restituisce quante manche si sono
        /// davvero chiuse. Ventiquattro passi al secondo come nel gioco, non uno: una manche che
        /// scade fra due letture non deve sfuggire.
        /// </summary>
        private static int RunToEnd(QualifyingDirector director, List<string> order)
        {
            int closed = 0;
            director.SegmentClosed += (name, dropped) => closed++;

            var piano = director.PlannedMinutes;
            double totale = (piano[0] + piano[1] + piano[2]) * 60 + 10;

            for (double t = 0; t <= totale; t += 0.25)
            {
                var running = order.FindAll(n => !director.IsEliminated(n));
                director.Update(t, running);
            }

            return closed;
        }

        private static List<string> Field(int count)
        {
            var names = new List<string>();
            for (int i = 1; i <= count; i++) names.Add("Pilota " + i);
            return names;
        }

        /// <summary>
        /// Una qualifica da dieci minuti corre tutte e tre le manche.
        ///
        /// E il caso che si rompeva: le durate chieste sommano quarantacinque minuti, e in dieci
        /// la prima non scadeva nemmeno.
        /// </summary>
        private static void SegmentsFitShortSession()
        {
            var order = Field(20);
            var director = new QualifyingDirector();
            director.Begin(Session(10), Rules(), order.Count);

            Require(director.Active, "Qualifica a eliminazione non attiva.");

            var piano = director.PlannedMinutes;
            double totale = piano[0] + piano[1] + piano[2];
            Require(totale <= 10,
                    "Le tre manche chiedono " + totale.ToString("0.0") + "' in una sessione da 10'.");

            // L'ordine delle durate e quello della Formula 1: ogni manche piu corta della prima.
            Require(piano[0] >= piano[1] && piano[1] >= piano[2],
                    "Le manche non sono in ordine decrescente di durata.");

            int closed = RunToEnd(director, order);
            Require(closed == 3, "Si sono chiuse " + closed + " manche invece di tre.");
            Require(director.Eliminated.Count == 10,
                    "Eliminati " + director.Eliminated.Count + " piloti invece di dieci.");
        }

        /// <summary>
        /// Le durate scelte nel configuratore per quella gara vincono su quelle del campionato.
        ///
        /// Erano scritte nel file e non le leggeva nessuno: un cliente che imposta la qualifica
        /// gara per gara non vedeva alcun effetto.
        /// </summary>
        private static void RoundOverridesChampionship()
        {
            var session = Session(60);
            session.KnockoutSegmentMinutes = new List<int> { 6, 5, 4 };
            session.KnockoutEliminations = new List<int> { 4, 3 };

            var order = Field(20);
            var director = new QualifyingDirector();
            director.Begin(session, Rules(), order.Count);

            var piano = director.PlannedMinutes;
            Require(Math.Abs(piano[0] - 6) < 0.01 && Math.Abs(piano[1] - 5) < 0.01 &&
                    Math.Abs(piano[2] - 4) < 0.01,
                    "Durate della gara ignorate: in uso " + piano[0] + " / " + piano[1] + " / " + piano[2]);

            Require(director.PlannedEliminations[0] == 4 && director.PlannedEliminations[1] == 3,
                    "Eliminazioni della gara ignorate.");

            RunToEnd(director, order);
            Require(director.Eliminated.Count == 7,
                    "Eliminati " + director.Eliminated.Count + " piloti invece di sette.");
        }

        /// <summary>
        /// Con pochi iscritti i tagli si riducono: in Q3 devono restare almeno due vetture.
        ///
        /// Il configuratore lascia scegliere quante vetture schierare. Con cinque e cinque e un
        /// campo da sei, la manche finale resterebbe vuota e la pole si assegnerebbe a tavolino.
        /// </summary>
        private static void EliminationsClampToField()
        {
            var order = Field(6);
            var director = new QualifyingDirector();
            director.Begin(Session(20), Rules(), order.Count);

            int primo = director.PlannedEliminations[0];
            int secondo = director.PlannedEliminations[1];
            Require(6 - primo - secondo >= 2,
                    "In Q3 resterebbero " + (6 - primo - secondo) + " vetture.");

            RunToEnd(director, order);

            int inPista = order.Count - director.Eliminated.Count;
            Require(inPista >= 2, "In pista alla fine " + inPista + " vetture.");

            var grid = director.FinalOrder(order.FindAll(n => !director.IsEliminated(n)));
            Require(grid.Count == 6, "Griglia da " + grid.Count + " posti invece di sei.");
        }

        /// <summary>
        /// Una sessione lunga abbastanza non viene toccata: 18, 15 e 12 restano quelli.
        ///
        /// Il ridimensionamento serve a far stare le manche dentro il tempo, non a cambiare un
        /// regolamento che gia funziona.
        /// </summary>
        private static void LongSessionKeepsRules()
        {
            var director = new QualifyingDirector();
            director.Begin(Session(60), Rules(), 20);

            var piano = director.PlannedMinutes;
            Require(Math.Abs(piano[0] - 18) < 0.01 && Math.Abs(piano[1] - 15) < 0.01 &&
                    Math.Abs(piano[2] - 12) < 0.01,
                    "Durate cambiate senza motivo: " + piano[0] + " / " + piano[1] + " / " + piano[2]);
        }

        /// <summary>
        /// La griglia si compone dal fondo: chi esce in Q1 parte dietro a chi esce in Q2.
        ///
        /// Qui la classifica di ogni manche e l'ordine dei nomi, quindi gli ultimi cinque escono
        /// in Q1 e i cinque prima di loro in Q2. In griglia devono tornare esattamente al loro
        /// posto, e nessun nome deve mancare o comparire due volte.
        /// </summary>
        private static void GridBuiltBackwards()
        {
            var order = Field(20);
            var director = new QualifyingDirector();
            director.Begin(Session(45), Rules(), order.Count);

            RunToEnd(director, order);

            var grid = director.FinalOrder(order.FindAll(n => !director.IsEliminated(n)));

            Require(grid.Count == 20, "Griglia da " + grid.Count + " posti invece di venti.");

            var visti = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var name in grid)
                Require(visti.Add(name), "Nome ripetuto in griglia: " + name);

            Require(grid[0] == "Pilota 1", "In pole " + grid[0] + " invece del piu veloce.");
            Require(grid[19] == "Pilota 20", "Ultimo in griglia " + grid[19] + ".");

            // Gli eliminati in Q1 occupano le ultime cinque caselle, quelli in Q2 le cinque prima.
            Require(grid[15] == "Pilota 16", "Gli eliminati in Q1 non occupano il fondo della griglia.");
            Require(grid[10] == "Pilota 11", "Gli eliminati in Q2 non stanno davanti a quelli di Q1.");
        }
    }
}
