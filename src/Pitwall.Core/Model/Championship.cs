using System;
using System.Collections.Generic;
using System.Linq;
using Pitwall.Results;
using Pitwall.Rules;

namespace Pitwall.Model
{
    /// <summary>
    /// The whole championship: rule book, entry list, calendar and every result recorded so far.
    /// This is the single document the manager edits and the SimHub plugin reads.
    /// </summary>
    public sealed class Championship
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");

        public string Name { get; set; } = "Nuovo campionato";

        public int SeasonYear { get; set; } = DateTime.Today.Year;

        public string Description { get; set; } = "";

        public ChampionshipRules Rules { get; set; } = new ChampionshipRules();

        public List<Team> Teams { get; set; } = new List<Team>();

        public List<Driver> Drivers { get; set; } = new List<Driver>();

        /// <summary>Circuits used by this championship, copied from the library on import.</summary>
        public List<Circuit> Circuits { get; set; } = new List<Circuit>();

        public List<Round> Calendar { get; set; } = new List<Round>();

        public List<SessionResult> Results { get; set; } = new List<SessionResult>();

        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        public DateTime ModifiedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>Schema version, so old files can be migrated later.</summary>
        public int Version { get; set; } = 1;

        // ---- Lookups ---------------------------------------------------------------------

        public Driver DriverById(string id) => Drivers.FirstOrDefault(d => d.Id == id);

        public Team TeamById(string id) => Teams.FirstOrDefault(t => t.Id == id);

        public Circuit CircuitById(string id) => Circuits.FirstOrDefault(c => c.Id == id);

        public Round RoundById(string id) => Calendar.FirstOrDefault(r => r.Id == id);

        public Circuit CircuitOf(Round round) => round == null ? null : CircuitById(round.CircuitId);

        public Driver PlayerDriver => Drivers.FirstOrDefault(d => d.IsPlayer);

        public IEnumerable<Driver> DriversOf(Team team) => Drivers.Where(d => d.TeamId == team.Id);

        /// <summary>
        /// Finds the championship driver behind a telemetry name. Matching is case insensitive
        /// and ignores extra spaces, because entry lists are rarely tidy.
        /// </summary>
        public Driver MatchTelemetryName(string telemetryName)
        {
            if (string.IsNullOrWhiteSpace(telemetryName)) return null;
            string needle = Normalize(telemetryName);
            return Drivers.FirstOrDefault(d => Normalize(d.MatchName) == needle)
                ?? Drivers.FirstOrDefault(d => Normalize(d.FullName) == needle)
                ?? Drivers.FirstOrDefault(d => !string.IsNullOrEmpty(d.LastName)
                                               && needle.EndsWith(Normalize(d.LastName), StringComparison.Ordinal));
        }

        /// <summary>Finds the circuit matching the track SimHub reports.</summary>
        public Circuit MatchTelemetryTrack(string trackId, string trackConfig)
        {
            if (string.IsNullOrWhiteSpace(trackId)) return null;
            string id = trackId.Trim();
            string cfg = (trackConfig ?? "").Trim();

            var exact = Circuits.FirstOrDefault(c =>
                string.Equals(c.AcTrackId, id, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(c.AcTrackConfig ?? "", cfg, StringComparison.OrdinalIgnoreCase));
            if (exact != null) return exact;

            // SimHub also exposes the combined "track-layout" form; accept both shapes.
            return Circuits.FirstOrDefault(c =>
                string.Equals(c.TelemetryKey, id, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(c.AcTrackId, id, StringComparison.OrdinalIgnoreCase));
        }

        private static string Normalize(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return string.Join(" ", s.Trim().ToLowerInvariant()
                .Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries));
        }

        // ---- Calendar ---------------------------------------------------------------------

        /// <summary>The first round that has not been completed yet.</summary>
        public Round NextRound()
        {
            return Calendar.OrderBy(r => r.Order)
                           .FirstOrDefault(r => r.Status != RoundStatus.Completed && r.Status != RoundStatus.Cancelled);
        }

        /// <summary>Results recorded for a round.</summary>
        public IEnumerable<SessionResult> ResultsFor(string roundId) =>
            Results.Where(r => r.RoundId == roundId);

        public SessionResult ResultFor(string roundId, SessionKind kind) =>
            Results.FirstOrDefault(r => r.RoundId == roundId && r.Kind == kind);

        /// <summary>Renumbers the calendar after a reorder.</summary>
        public void ResequenceCalendar()
        {
            int i = 1;
            foreach (var r in Calendar.OrderBy(r => r.Order).ToList())
                r.Order = i++;
            Calendar = Calendar.OrderBy(r => r.Order).ToList();
        }

        /// <summary>
        /// Se questo risultato si puo registrare.
        ///
        /// Falso quando la gara e gia stata corsa fino in fondo. Una gara conclusa non si rifa:
        /// altrimenti basta riaprirla in Content Manager per riscriverne il risultato e rifare la
        /// classifica a piacere, e un campionato in cui si puo tornare indietro non e un
        /// campionato. In Content Manager compare solo la prossima gara proprio per questo, e
        /// questo controllo e la rete sotto: vale anche se quel preset venisse riaperto a mano,
        /// o se Assetto Corsa rimandasse una sessione che aveva gia chiuso.
        ///
        /// Chi chiama deve guardarlo **prima** di assegnare i punti, non dopo: i punti si
        /// sommano alla classifica, e assegnarli per un risultato che poi viene rifiutato li
        /// conterebbe due volte.
        /// </summary>
        public bool CanRecord(SessionResult result)
        {
            if (result == null) return false;

            var round = RoundById(result.RoundId);
            if (round == null) return true;

            return round.Status != RoundStatus.Completed;
        }

        /// <summary>
        /// Registra il risultato di una sessione, al posto di quello che c'era per la stessa.
        /// Restituisce falso se la gara era gia conclusa e non si tocca.
        /// </summary>
        public bool RecordResult(SessionResult result)
        {
            if (result == null) return false;
            if (!CanRecord(result)) return false;

            Results.RemoveAll(r => r.RoundId == result.RoundId && r.Kind == result.Kind);
            Results.Add(result);

            var round = RoundById(result.RoundId);
            if (round != null)
            {
                bool allDone = round.Sessions.Where(s => s.Enabled)
                                   .All(s => Results.Any(r => r.RoundId == round.Id && r.Kind == s.Kind));
                round.Status = allDone ? RoundStatus.Completed : RoundStatus.InProgress;
            }

            ModifiedUtc = DateTime.UtcNow;
            return true;
        }

        /// <summary>
        /// Riapre una gara conclusa, cancellandone i risultati.
        ///
        /// Serve perche il divieto di rifare una gara sia una regola e non un vicolo cieco: se un
        /// fine settimana e andato storto - una sessione chiusa per sbaglio, un risultato
        /// registrato a vuoto - si deve poter tornare indietro di proposito, da un posto dove e
        /// evidente che lo si sta facendo. Non succede mai da solo.
        ///
        /// I punti **non** si toccano qui: la classifica si ricalcola dai risultati, e cancellare
        /// quelli e quanto serve.
        /// </summary>
        public bool ReopenRound(string roundId)
        {
            var round = RoundById(roundId);
            if (round == null) return false;

            Results.RemoveAll(r => r.RoundId == roundId);
            round.Status = RoundStatus.Scheduled;
            ModifiedUtc = DateTime.UtcNow;
            return true;
        }
    }
}
