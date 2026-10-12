using System;
using System.Collections.Generic;
using System.Linq;

namespace Pitwall.Engineer
{
    /// <summary>What the engineer is talking about. Used for muting and for the overlay colour.</summary>
    public static class RadioCategory
    {
        public const string Strategy = "Strategia";
        public const string Tyres = "Gomme";
        public const string Fuel = "Benzina";
        public const string Weather = "Meteo";
        public const string Gaps = "Distacchi";
        public const string RaceControl = "Direzione gara";
        public const string Penalty = "Penalità";
        public const string Damage = "Danni";
        public const string Traffic = "Traffico";
        public const string Session = "Sessione";
        public const string Encouragement = "Morale";
    }

    /// <summary>One radio call.</summary>
    public sealed class EngineerMessage
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");

        /// <summary>Stable key of the rule that produced it, used for cooldowns.</summary>
        public string RuleKey { get; set; } = "";

        public string Text { get; set; } = "";

        public string Category { get; set; } = RadioCategory.Strategy;

        /// <summary>0 chatter, 1 normal, 2 important, 3 drop everything.</summary>
        public int Priority { get; set; } = 1;

        public DateTime Utc { get; set; } = DateTime.UtcNow;

        public int Lap { get; set; }

        /// <summary>Seconds the message should stay on the overlay.</summary>
        public double DisplaySeconds { get; set; } = 6;

        /// <summary>Short form for a small wheel display, at most about 16 characters.</summary>
        public string ShortText { get; set; } = "";

        public override string ToString() => "[" + Category + "] " + Text;
    }

    /// <summary>
    /// Holds the messages waiting to be read out. Keeps the radio sane: one message at a time,
    /// a minimum gap between calls, and urgent traffic jumping the queue.
    /// </summary>
    public sealed class RadioQueue
    {
        private readonly List<EngineerMessage> _pending = new List<EngineerMessage>();
        private readonly Dictionary<string, DateTime> _lastByRule = new Dictionary<string, DateTime>();
        private DateTime _lastSpoken = DateTime.MinValue;

        /// <summary>Minimum seconds between two messages.</summary>
        public double MinimumGapSeconds { get; set; } = 6;

        /// <summary>
        /// The clock the radio runs on. During a real session it advances with the session
        /// timer, which is the same as wall time; the simulator drives it much faster, and the
        /// anti-spam rules have to follow the session rather than the wall so that a race
        /// replayed in two seconds produces the same radio traffic as a race driven for real.
        /// </summary>
        public DateTime Now { get; set; } = DateTime.UtcNow;

        /// <summary>Messages already delivered, newest last. Kept for the review screen.</summary>
        public List<EngineerMessage> History { get; } = new List<EngineerMessage>();

        public EngineerMessage Current { get; private set; }

        public DateTime CurrentUntil { get; private set; } = DateTime.MinValue;

        public HashSet<string> MutedCategories { get; set; } = new HashSet<string>();

        public event Action<EngineerMessage> MessageDelivered;

        public void Clear()
        {
            _pending.Clear();
            _lastByRule.Clear();
            History.Clear();
            Current = null;
            CurrentUntil = DateTime.MinValue;
            _lastSpoken = DateTime.MinValue;
        }

        /// <summary>
        /// Queues a message unless the same rule fired too recently. Returns false when the
        /// message was dropped, which lets the caller avoid setting its own one-shot flags.
        /// </summary>
        public bool Enqueue(EngineerMessage message, double cooldownSeconds = 0)
        {
            if (message == null || string.IsNullOrWhiteSpace(message.Text)) return false;
            if (MutedCategories.Contains(message.Category)) return false;

            if (cooldownSeconds > 0 && !string.IsNullOrEmpty(message.RuleKey))
            {
                if (_lastByRule.TryGetValue(message.RuleKey, out var last) &&
                    (Now - last).TotalSeconds < cooldownSeconds)
                    return false;
                _lastByRule[message.RuleKey] = Now;
            }

            message.Utc = Now;

            // An urgent call replaces anything queued that is less important.
            if (message.Priority >= 3)
                _pending.RemoveAll(m => m.Priority < 3);

            _pending.Add(message);
            if (_pending.Count > 12) _pending.RemoveAt(0);
            return true;
        }

        /// <summary>Call once per update. Promotes the next message when the radio is free.</summary>
        public void Pump()
        {
            var now = Now;

            if (Current != null && now >= CurrentUntil)
                Current = null;

            if (Current != null) return;
            if (_pending.Count == 0) return;
            if ((now - _lastSpoken).TotalSeconds < MinimumGapSeconds &&
                _pending.All(m => m.Priority < 3)) return;

            var next = _pending.OrderByDescending(m => m.Priority).ThenBy(m => m.Utc).First();
            _pending.Remove(next);

            Current = next;
            CurrentUntil = now.AddSeconds(Math.Max(2, next.DisplaySeconds));
            _lastSpoken = now;

            History.Add(next);
            if (History.Count > 300) History.RemoveAt(0);

            MessageDelivered?.Invoke(next);
        }

        public IEnumerable<EngineerMessage> Recent(int count) =>
            History.Skip(Math.Max(0, History.Count - count));
    }
}
