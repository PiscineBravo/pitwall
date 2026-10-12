using System;
using System.Collections.Generic;
using System.Linq;
using Pitwall.Model;
using Pitwall.Results;
using Pitwall.Rules;

namespace Pitwall.Scoring
{
    /// <summary>A line in the drivers or constructors table.</summary>
    public sealed class StandingRow
    {
        public int Position { get; set; }
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string TeamName { get; set; } = "";
        public string Color { get; set; } = "#FFFFFF";
        public double Points { get; set; }
        public double DroppedPoints { get; set; }
        public int Wins { get; set; }
        public int Podiums { get; set; }
        public int Poles { get; set; }
        public int FastestLaps { get; set; }
        public int Starts { get; set; }
        public int Retirements { get; set; }
        public int BestFinish { get; set; } = int.MaxValue;

        /// <summary>Points per finishing position, used for countback tie-breaks.</summary>
        public Dictionary<int, int> FinishCounts { get; set; } = new Dictionary<int, int>();

        public override string ToString() => Position + ". " + Name + " " + Points;
    }

    /// <summary>Turns recorded session results into drivers and constructors tables.</summary>
    public static class StandingsCalculator
    {
        /// <summary>
        /// Fills in the Points field of every entry of a session, applying the fastest lap bonus
        /// and the reduced scale when the race was cut short.
        /// </summary>
        public static void ApplyPoints(Championship championship, SessionResult result)
        {
            if (championship == null || result == null) return;
            var scheme = championship.Rules.Points;

            foreach (var e in result.Entries)
            {
                e.Points = 0;
                e.PointsNote = "";
            }

            if (!(result.Kind == SessionKind.Race || result.Kind == SessionKind.Sprint))
            {
                // Qualifying can still award a pole bonus.
                if (result.Kind == SessionKind.Qualifying && scheme.PolePosition > 0)
                {
                    var pole = result.Entries.FirstOrDefault(e => e.Position == 1);
                    if (pole != null)
                    {
                        pole.Points = scheme.PolePosition;
                        pole.PointsNote = "Pole position";
                    }
                }
                return;
            }

            bool scoring = result.Entries.Any();
            if (!scoring) return;

            foreach (var e in result.Entries)
            {
                if (e.Status == FinishStatus.Disqualified ||
                    e.Status == FinishStatus.DidNotStart ||
                    e.Status == FinishStatus.NotClassified)
                {
                    e.PointsNote = e.Status == FinishStatus.Disqualified ? "Squalificato" : "Non classificato";
                    continue;
                }

                e.Points = result.Kind == SessionKind.Sprint
                    ? scheme.SprintPointsFor(e.Position)
                    : scheme.RacePointsFor(e.Position, result.CompletedFraction, result.LapsCompleted);
            }

            if (result.Kind == SessionKind.Race && result.Shortened && scheme.UseShortenedRaceScale)
            {
                string note = "Gara accorciata (" + Math.Round(result.CompletedFraction * 100) + "% distanza)";
                foreach (var e in result.Entries.Where(x => x.Points > 0))
                    e.PointsNote = note;
            }

            if (result.Kind == SessionKind.Race && scheme.FastestLap > 0)
            {
                var fl = result.FastestLapEntry;
                if (fl != null && fl.Position <= scheme.FastestLapMaxPosition &&
                    fl.Status == FinishStatus.Finished)
                {
                    fl.SetFastestLap = true;
                    fl.Points += scheme.FastestLap;
                    fl.PointsNote = string.IsNullOrEmpty(fl.PointsNote)
                        ? "Giro veloce"
                        : fl.PointsNote + " + giro veloce";
                }
                else if (fl != null)
                {
                    fl.SetFastestLap = true;
                }
            }
        }

        /// <summary>Drivers table over the whole championship, or up to a given round.</summary>
        public static List<StandingRow> Drivers(Championship championship, int upToRoundOrder = int.MaxValue)
        {
            var rows = new Dictionary<string, StandingRow>();
            var scheme = championship.Rules.Points;

            foreach (var d in championship.Drivers)
            {
                var team = championship.TeamById(d.TeamId);
                rows[d.Id] = new StandingRow
                {
                    Id = d.Id,
                    Name = d.FullName,
                    TeamName = team != null ? team.Name : "",
                    Color = team != null ? team.Color : "#FFFFFF",
                    Points = d.PointsAdjustment
                };
            }

            var perDriverScores = new Dictionary<string, List<double>>();

            foreach (var result in OrderedResults(championship, upToRoundOrder))
            {
                foreach (var e in result.Entries)
                {
                    if (!rows.TryGetValue(e.DriverId, out var row)) continue;

                    row.Points += e.Points;
                    if (!perDriverScores.TryGetValue(e.DriverId, out var scores))
                        perDriverScores[e.DriverId] = scores = new List<double>();
                    if (result.Kind == SessionKind.Race || result.Kind == SessionKind.Sprint)
                        scores.Add(e.Points);

                    if (result.Kind == SessionKind.Qualifying && e.Position == 1) row.Poles++;

                    if (result.Kind == SessionKind.Race)
                    {
                        row.Starts++;
                        if (e.SetFastestLap) row.FastestLaps++;
                        if (e.Status == FinishStatus.Retired) row.Retirements++;
                        if (e.Position == 1) row.Wins++;
                        if (e.Position <= 3 && IsClassified(e)) row.Podiums++;
                        if (IsClassified(e))
                        {
                            row.BestFinish = Math.Min(row.BestFinish, e.Position);
                            row.FinishCounts.TryGetValue(e.Position, out int n);
                            row.FinishCounts[e.Position] = n + 1;
                        }
                    }
                }
            }

            if (scheme.DroppedScores > 0)
            {
                foreach (var kv in perDriverScores)
                {
                    if (!rows.TryGetValue(kv.Key, out var row)) continue;
                    var dropped = kv.Value.OrderBy(v => v).Take(scheme.DroppedScores).Sum();
                    row.DroppedPoints = dropped;
                    row.Points -= dropped;
                }
            }

            return Rank(rows.Values);
        }

        /// <summary>Constructors table.</summary>
        public static List<StandingRow> Constructors(Championship championship, int upToRoundOrder = int.MaxValue)
        {
            var rows = new Dictionary<string, StandingRow>();
            foreach (var t in championship.Teams)
            {
                rows[t.Id] = new StandingRow
                {
                    Id = t.Id,
                    Name = t.Name,
                    TeamName = t.Name,
                    Color = t.Color,
                    Points = t.PointsAdjustment
                };
            }

            foreach (var result in OrderedResults(championship, upToRoundOrder))
            {
                foreach (var e in result.Entries)
                {
                    var driver = championship.DriverById(e.DriverId);
                    string teamId = driver != null ? driver.TeamId : e.TeamId;
                    if (string.IsNullOrEmpty(teamId) || !rows.TryGetValue(teamId, out var row)) continue;

                    row.Points += e.Points;

                    if (result.Kind == SessionKind.Qualifying && e.Position == 1) row.Poles++;
                    if (result.Kind == SessionKind.Race)
                    {
                        row.Starts++;
                        if (e.SetFastestLap) row.FastestLaps++;
                        if (e.Status == FinishStatus.Retired) row.Retirements++;
                        if (e.Position == 1) row.Wins++;
                        if (e.Position <= 3 && IsClassified(e)) row.Podiums++;
                        if (IsClassified(e))
                        {
                            row.BestFinish = Math.Min(row.BestFinish, e.Position);
                            row.FinishCounts.TryGetValue(e.Position, out int n);
                            row.FinishCounts[e.Position] = n + 1;
                        }
                    }
                }
            }

            return Rank(rows.Values);
        }

        private static bool IsClassified(EntryResult e) =>
            e.Status == FinishStatus.Finished || e.Status == FinishStatus.Lapped;

        private static IEnumerable<SessionResult> OrderedResults(Championship championship, int upToRoundOrder)
        {
            return championship.Results
                .Select(r => new { Result = r, Round = championship.RoundById(r.RoundId) })
                .Where(x => x.Round != null && x.Round.Order <= upToRoundOrder)
                .OrderBy(x => x.Round.Order)
                .ThenBy(x => (int)x.Result.Kind)
                .Select(x => x.Result);
        }

        /// <summary>
        /// Sorts a table and assigns positions. Ties are broken the Formula 1 way: most wins,
        /// then most second places, then most thirds, and so on down the order.
        /// </summary>
        private static List<StandingRow> Rank(IEnumerable<StandingRow> rows)
        {
            var list = rows.ToList();
            list.Sort((a, b) =>
            {
                int c = b.Points.CompareTo(a.Points);
                if (c != 0) return c;

                for (int pos = 1; pos <= 30; pos++)
                {
                    a.FinishCounts.TryGetValue(pos, out int an);
                    b.FinishCounts.TryGetValue(pos, out int bn);
                    if (an != bn) return bn.CompareTo(an);
                }

                c = b.Poles.CompareTo(a.Poles);
                if (c != 0) return c;
                return string.Compare(a.Name, b.Name, StringComparison.CurrentCulture);
            });

            int p = 1;
            foreach (var r in list)
            {
                r.Position = p++;
                if (r.BestFinish == int.MaxValue) r.BestFinish = 0;
            }
            return list;
        }
    }
}
