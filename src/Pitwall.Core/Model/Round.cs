using System;
using System.Collections.Generic;
using System.Linq;

namespace Pitwall.Model
{
    /// <summary>One grand prix of the calendar: a circuit, a format and its list of sessions.</summary>
    public sealed class Round
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");

        /// <summary>Position in the calendar, starting at 1.</summary>
        public int Order { get; set; }

        /// <summary>Event name, for example Gran Premio d'Italia.</summary>
        public string Name { get; set; } = "";

        /// <summary>Id of a <see cref="Circuit"/> in the championship circuit list.</summary>
        public string CircuitId { get; set; } = "";

        public WeekendFormat Format { get; set; } = WeekendFormat.Standard;

        /// <summary>Calendar date of the race day. Only used for display and weather seeding.</summary>
        public DateTime Date { get; set; } = DateTime.Today;

        public RoundStatus Status { get; set; } = RoundStatus.Scheduled;

        public List<SessionConfig> Sessions { get; set; } = new List<SessionConfig>();

        /// <summary>Random seed for weather and race-control decisions, so a weekend is reproducible.</summary>
        public int Seed { get; set; } = Environment.TickCount;

        /// <summary>The next session that has not produced a result yet, or null when the round is done.</summary>
        public SessionConfig NextSession(IEnumerable<SessionKind> completed)
        {
            var done = new HashSet<SessionKind>(completed);
            return Sessions.FirstOrDefault(s => s.Enabled && !done.Contains(s.Kind));
        }

        public SessionConfig Session(SessionKind kind) => Sessions.FirstOrDefault(s => s.Kind == kind);

        /// <summary>Creates a round with the default session set for its format.</summary>
        public static Round Create(string name, string circuitId, WeekendFormat format, int order)
        {
            return new Round
            {
                Name = name,
                CircuitId = circuitId,
                Format = format,
                Order = order,
                Sessions = SessionConfig.DefaultsFor(format)
            };
        }

        public override string ToString() => Order + ". " + Name;
    }
}
