using System;
using System.Collections.Generic;
using System.Linq;
using Pitwall.Live;
using Pitwall.Model;
using Pitwall.RaceControl;
using Pitwall.Rules;
using Pitwall.Tyres;
using Pitwall.Weather;

namespace Pitwall.Engineer
{
    /// <summary>
    /// The race engineer. Looks at telemetry, tyres, strategy, weather and race control once per
    /// frame and decides whether there is anything worth saying. Everything it produces goes
    /// through <see cref="RadioQueue"/>, which keeps the radio from becoming noise.
    /// </summary>
    public sealed class EngineerBrain
    {
        private Phrasebook _book;
        private Circuit _circuit = new Circuit();
        private SessionConfig _session = new SessionConfig();
        private WeatherPlan _weather;

        private int _lastLap = -1;
        private int _lastPosition;
        private int _lastPenaltyCount;
        private RaceControlStatus _lastStatus = RaceControlStatus.Inactive;
        private bool _saidRainSoon;
        private bool _saidRainNow;
        private bool _saidDrying;
        private bool _saidWetTyres;
        private bool _saidSlickTyres;
        private bool _saidWindowOpen;
        private bool _saidFinalLap;
        private bool _saidGreeting;
        private bool _saidTwoCompounds;
        private bool _saidMandatoryStop;
        private int _lastGapReportLap = -99;
        private double _previousGapAhead;
        private double _previousGapBehind;
        private TimeSpan _bestLapSeen = TimeSpan.Zero;
        private readonly Dictionary<string, int> _opponentPitCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// The radio runs on session time rather than wall time, accumulated from the telemetry
        /// frames themselves. In a real session the two are identical; when a race is replayed
        /// by the simulator in a couple of seconds this is what keeps the anti-spam rules
        /// meaningful instead of silencing the engineer completely.
        /// </summary>
        private static readonly DateTime ClockEpoch = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        private double _sessionSeconds;

        public EngineerRules Rules { get; private set; } = new EngineerRules();

        public RadioQueue Radio { get; } = new RadioQueue();

        /// <summary>The strategy call currently on the table, shown on the dashboard.</summary>
        public string StrategyAdvice { get; private set; } = "";

        /// <summary>Pit window computed on the last lap, or null outside a race.</summary>
        public PitWindow Window { get; private set; }

        /// <summary>Compound the engineer would fit at the next stop.</summary>
        public CompoundSpec RecommendedCompound { get; private set; } = Compounds.Medium;

        /// <summary>How far ahead the engineer is allowed to read the forecast, in minutes.</summary>
        public int ForecastHorizonMinutes { get; set; } = 30;

        public EngineerBrain(EngineerRules rules = null)
        {
            Configure(rules ?? new EngineerRules());
        }

        public void Configure(EngineerRules rules)
        {
            Rules = rules ?? new EngineerRules();
            // La lingua e quella scelta nel Manager, una per tutto Pitwall.
            //
            // Nel regolamento del campionato ce ne era una sua, e due scelte per la stessa cosa
            // vogliono dire prima o poi un ingegnere che parla inglese dentro un programma in
            // italiano. Il campo resta nei dati dei campionati gia salvati, ma non comanda piu.
            _book = new Phrasebook(Pitwall.Lingue.Testi.Codice);
            Radio.MinimumGapSeconds = Rules.MinimumSecondsBetweenMessages;
            Radio.MutedCategories = Rules.MutedCategories ?? new HashSet<string>();
        }

        public void BeginSession(Circuit circuit, SessionConfig session, WeatherPlan weather)
        {
            _circuit = circuit ?? new Circuit();
            _session = session ?? new SessionConfig();
            _weather = weather ?? session?.Weather;

            Radio.Clear();
            _lastLap = -1;
            _lastPosition = 0;
            _lastPenaltyCount = 0;
            _lastStatus = RaceControlStatus.Inactive;
            _saidRainSoon = _saidRainNow = _saidDrying = _saidWetTyres = _saidSlickTyres = false;
            _saidWindowOpen = _saidFinalLap = _saidGreeting = false;
            _saidTwoCompounds = _saidMandatoryStop = false;
            _lastGapReportLap = -99;
            _previousGapAhead = _previousGapBehind = 0;
            _bestLapSeen = TimeSpan.Zero;
            _opponentPitCounts.Clear();
            StrategyAdvice = "";
            Window = null;
            _sessionSeconds = 0;
            Radio.Now = ClockEpoch;
        }

        /// <summary>One pass over everything. Call from the plugin data loop.</summary>
        public void Update(TelemetryFrame frame, StintTracker tyres, RaceDirector raceControl)
        {
            if (frame != null)
            {
                _sessionSeconds += Math.Max(0, Math.Min(2.0, frame.DeltaSeconds));
                Radio.Now = ClockEpoch.AddSeconds(_sessionSeconds);
            }

            if (!Rules.Enabled || frame == null || !frame.GameRunning || frame.Paused)
            {
                Radio.Pump();
                return;
            }

            bool isRace = _session.Kind == SessionKind.Race || _session.Kind == SessionKind.Sprint;
            bool newLap = frame.CompletedLaps != _lastLap;

            Greeting(frame, isRace);
            RaceControlCalls(frame, raceControl);
            PenaltyCalls(frame, raceControl);
            WeatherCalls(frame, tyres);
            DamageCalls(frame);

            if (newLap)
            {
                _lastLap = frame.CompletedLaps;
                TyreCalls(frame, tyres);
                FuelCalls(frame);
                if (isRace)
                {
                    StrategyCalls(frame, tyres, raceControl);
                    GapCalls(frame);
                    EndOfRaceCalls(frame);
                }
                else
                {
                    QualifyingCalls(frame);
                }
            }

            PositionCalls(frame, isRace);
            Radio.Pump();
        }

        // ---- Rules --------------------------------------------------------------------------

        private void Greeting(TelemetryFrame frame, bool isRace)
        {
            if (_saidGreeting) return;
            if (frame.SessionElapsed.TotalSeconds < 1 && frame.CompletedLaps == 0 && frame.SpeedKmh < 1) return;
            _saidGreeting = true;

            string key = isRace ? "session.start.race"
                       : _session.Kind == SessionKind.Qualifying || _session.Kind == SessionKind.SprintQualifying
                         ? "session.start.qualifying"
                         : "session.start.practice";

            int laps = frame.TotalLaps > 0 ? frame.TotalLaps : _session.ResolveLaps(_circuit);
            Say("greeting", key, RadioCategory.Session, 1, frame, laps);
        }

        private void RaceControlCalls(TelemetryFrame frame, RaceDirector rc)
        {
            if (rc == null) return;

            if (rc.Status != _lastStatus)
            {
                switch (rc.Status)
                {
                    case RaceControlStatus.VirtualSafetyCar:
                        Say("rc.vsc", "rc.vsc", RadioCategory.RaceControl, 3, frame);
                        break;
                    case RaceControlStatus.SafetyCar:
                        Say("rc.sc", "rc.sc", RadioCategory.RaceControl, 3, frame);
                        break;
                    case RaceControlStatus.SafetyCarEnding:
                        Say("rc.scEnding", "rc.scEnding", RadioCategory.RaceControl, 2, frame);
                        break;
                    case RaceControlStatus.Green:
                        if (_lastStatus != RaceControlStatus.Inactive && _lastStatus != RaceControlStatus.FormationLap)
                            Say("rc.green", "rc.green", RadioCategory.RaceControl, 2, frame);
                        break;
                    case RaceControlStatus.RedFlag:
                        Say("rc.red", "rc.red", RadioCategory.RaceControl, 3, frame);
                        break;
                    case RaceControlStatus.LocalYellow:
                        Say("rc.yellow", "rc.yellow", RadioCategory.RaceControl, 2, frame,
                            rc.YellowSectors.Count > 0 ? rc.YellowSectors.Min() : 1);
                        break;
                }
                _lastStatus = rc.Status;
            }

            bool neutralised = rc.Status == RaceControlStatus.VirtualSafetyCar || rc.Status == RaceControlStatus.SafetyCar;
            if (neutralised && rc.Delta < -0.3)
                Say("rc.delta", "rc.deltaWarning", RadioCategory.RaceControl, 3, frame, cooldown: 12);

            if (frame.FlagBlue)
                Say("rc.blue", "rc.blueFlags", RadioCategory.Traffic, 2, frame, cooldown: 25);
        }

        private void PenaltyCalls(TelemetryFrame frame, RaceDirector rc)
        {
            if (rc == null) return;
            var penalties = rc.Penalties.Penalties;
            if (penalties.Count <= _lastPenaltyCount) { _lastPenaltyCount = penalties.Count; return; }

            for (int i = _lastPenaltyCount; i < penalties.Count; i++)
            {
                var p = penalties[i];
                if (p.Offence == OffenceKind.TrackLimits && p.Type == PenaltyType.Warning)
                {
                    Say("pen.limits", "penalty.trackLimits", RadioCategory.Penalty, 2, frame,
                        rc.Penalties.TrackLimitStrikes(p.DriverId), 3);
                }
                else if (p.Type != PenaltyType.Warning)
                {
                    Say("pen." + p.Id, "penalty.issued", RadioCategory.Penalty, 3, frame, p.ShortText);
                }
            }
            _lastPenaltyCount = penalties.Count;
        }

        private void TyreCalls(TelemetryFrame frame, StintTracker tyres)
        {
            if (tyres == null) return;

            if (tyres.StintLaps <= 1 && tyres.ColdTyres)
                Say("tyre.cold", "tyres.cold", RadioCategory.Tyres, 1, frame, cooldown: 60);

            if (tyres.OverheatingTyres)
                Say("tyre.hot", "tyres.overheating", RadioCategory.Tyres, 2, frame, cooldown: 45);

            if (tyres.Critical)
                Say("tyre.crit", "tyres.wear.critical", RadioCategory.Tyres, 3, frame, cooldown: 30);
            else if (tyres.PastCliff)
                Say("tyre.cliff", "tyres.wear.cliff", RadioCategory.Tyres, 2, frame, cooldown: 60);
            else if (tyres.LapsToCliff <= 4 && tyres.LapsToCliff > 0)
                Say("tyre.warn", "tyres.wear.warning", RadioCategory.Tyres, 2, frame,
                    (int)Math.Round(tyres.LapsToCliff), cooldown: 90);
            else if (Rules.Verbosity >= 1 && tyres.WearPercent >= 45 && tyres.WearPercent < 55)
                Say("tyre.half", "tyres.wear.half", RadioCategory.Tyres, 0, frame,
                    (int)Math.Round(tyres.WearPercent), cooldown: 300);
        }

        private void FuelCalls(TelemetryFrame frame)
        {
            if (!frame.EstimatedFuelLaps.HasValue) return;
            double laps = frame.EstimatedFuelLaps.Value;
            int lapsLeft = frame.TotalLaps > 0 ? frame.TotalLaps - frame.CompletedLaps : 0;

            if (lapsLeft > 0 && laps < lapsLeft - 0.5)
            {
                if (laps < lapsLeft - 3)
                    Say("fuel.crit", "fuel.critical", RadioCategory.Fuel, 3, frame, cooldown: 90);
                else
                    Say("fuel.low", "fuel.low", RadioCategory.Fuel, 2, frame, (int)Math.Floor(laps), cooldown: 120);
            }
            else if (Rules.Verbosity >= 2 && lapsLeft > 0)
            {
                Say("fuel.ok", "fuel.fine", RadioCategory.Fuel, 0, frame, cooldown: 600);
            }
        }

        private void StrategyCalls(TelemetryFrame frame, StintTracker tyres, RaceDirector rc)
        {
            if (tyres == null) return;

            int totalLaps = frame.TotalLaps > 0 ? frame.TotalLaps : _session.ResolveLaps(_circuit);
            if (totalLaps <= 0) return;

            int lapsLeft = totalLaps - frame.CompletedLaps;
            RecommendedCompound = ChooseNextCompound(frame, tyres, lapsLeft);
            Window = tyres.SuggestWindow(frame.CompletedLaps, totalLaps, RecommendedCompound);

            StrategyAdvice = BuildAdvice(frame, tyres, lapsLeft);

            // Announcing an open pit window on the opening laps is technically true on a short
            // race and completely useless to the driver. Wait until the tyres have done some
            // work, or until a quarter of the race has gone.
            bool worthSaying = tyres.WearPercent > 20 || frame.CompletedLaps >= totalLaps * 0.25;

            if (!_saidWindowOpen && worthSaying && Window.IsOpen(frame.CompletedLaps) && lapsLeft > 2)
            {
                _saidWindowOpen = true;
                Say("strat.window", "strategy.window.open", RadioCategory.Strategy, 2, frame);
            }

            // A free stop under neutralisation is the single biggest strategy call there is.
            bool neutralised = rc != null && (rc.Status == RaceControlStatus.SafetyCar ||
                                              rc.Status == RaceControlStatus.VirtualSafetyCar);
            if (neutralised && !rc.PitLaneClosed && lapsLeft > 4 && tyres.WearPercent > 25)
                Say("strat.scpit", "strategy.pitUnderSc", RadioCategory.Strategy, 3, frame, cooldown: 120);

            if (_session.MandatoryPitStops > 0 && tyres.PitStops < _session.MandatoryPitStops &&
                lapsLeft <= 6 && !_saidMandatoryStop)
            {
                _saidMandatoryStop = true;
                Say("strat.mand", "strategy.mandatoryStop", RadioCategory.Strategy, 3, frame, lapsLeft);
            }

            if (_session.TwoCompoundRule && lapsLeft <= 8 && !_saidTwoCompounds &&
                !tyres.TwoCompoundRuleSatisfied(WetnessNow(frame) > 0.05))
            {
                _saidTwoCompounds = true;
                Say("strat.two", "strategy.twoCompounds", RadioCategory.Strategy, 3, frame);
            }

            DetectUndercut(frame, tyres);
        }

        private void DetectUndercut(TelemetryFrame frame, StintTracker tyres)
        {
            foreach (var o in frame.Opponents)
            {
                if (o.IsPlayer || string.IsNullOrWhiteSpace(o.Name)) continue;
                _opponentPitCounts.TryGetValue(o.Name, out int previous);
                _opponentPitCounts[o.Name] = o.PitCount;

                if (o.PitCount <= previous) continue;
                if (!o.GapToPlayer.HasValue) continue;

                // Only the cars right behind can undercut us.
                double gap = o.GapToPlayer.Value;
                if (gap > 0 && gap < 25 && tyres.WearPercent > 20)
                    Say("strat.under." + o.Name, "strategy.undercutThreat", RadioCategory.Strategy, 2, frame,
                        ShortName(o.Name), cooldown: 60);
            }
        }

        private void GapCalls(TelemetryFrame frame)
        {
            if (Rules.Verbosity < 1) return;
            int every = Rules.Verbosity >= 2 ? 2 : 4;
            if (frame.CompletedLaps - _lastGapReportLap < every) return;
            _lastGapReportLap = frame.CompletedLaps;

            var ahead = frame.Opponents
                .Where(o => !o.IsPlayer && o.GapToPlayer.HasValue && o.GapToPlayer.Value < 0 && o.Position == frame.Position - 1)
                .OrderByDescending(o => o.GapToPlayer.Value)
                .FirstOrDefault();

            var behind = frame.Opponents
                .Where(o => !o.IsPlayer && o.GapToPlayer.HasValue && o.GapToPlayer.Value > 0 && o.Position == frame.Position + 1)
                .OrderBy(o => o.GapToPlayer.Value)
                .FirstOrDefault();

            if (frame.Position == 1 && behind != null)
            {
                Say("gap.lead", "gap.leading", RadioCategory.Gaps, 1, frame,
                    Fmt(Math.Abs(behind.GapToPlayer.Value)), cooldown: 25);
            }
            else if (ahead != null)
            {
                double gap = Math.Abs(ahead.GapToPlayer.Value);
                double trend = _previousGapAhead > 0 ? _previousGapAhead - gap : 0;
                _previousGapAhead = gap;

                if (Math.Abs(trend) >= 0.25 && frame.CompletedLaps > 2)
                {
                    Say("gap.trend", trend > 0 ? "gap.closing" : "gap.losing", RadioCategory.Gaps, 1, frame,
                        ShortName(ahead.Name), Fmt(Math.Abs(trend)), cooldown: 25);
                }
                else
                {
                    Say("gap.ahead", "gap.ahead", RadioCategory.Gaps, 1, frame,
                        ShortName(ahead.Name), Fmt(gap), cooldown: 25);
                }

                if (gap < 1.0 && frame.DrsAvailable > 0)
                    Say("gap.drs", "gap.drsRange", RadioCategory.Gaps, 2, frame, cooldown: 40);
            }

            if (behind != null && frame.Position > 1)
            {
                double gap = Math.Abs(behind.GapToPlayer.Value);
                _previousGapBehind = gap;
                if (gap < 1.5)
                    Say("gap.behind", "gap.behind", RadioCategory.Gaps, 1, frame,
                        ShortName(behind.Name), Fmt(gap), cooldown: 30);
            }
        }

        private void WeatherCalls(TelemetryFrame frame, StintTracker tyres)
        {
            if (_weather == null) return;

            double minute = frame.SessionElapsed.TotalMinutes;
            var now = _weather.SampleAt(minute);
            var forecast = _weather.Forecast(minute, Math.Max(5, ForecastHorizonMinutes));

            var rainSoon = forecast.FirstOrDefault(f => f.StartsRain);
            if (rainSoon != null && !_saidRainSoon)
            {
                _saidRainSoon = true;
                Say("wx.soon", "weather.rainSoon", RadioCategory.Weather, 3, frame,
                    (int)Math.Round(rainSoon.MinutesFromNow));
            }

            if (now.IsRaining && !_saidRainNow)
            {
                _saidRainNow = true;
                _saidDrying = false;
                Say("wx.now", "weather.rainNow", RadioCategory.Weather, 3, frame);
            }

            if (!now.IsRaining && _saidRainNow && !_saidDrying && now.Wetness < 0.12)
            {
                _saidDrying = true;
                Say("wx.dry", "weather.drying", RadioCategory.Weather, 2, frame);
            }

            if (tyres == null) return;

            bool onWets = tyres.Compound.IsWetWeather;
            double wetness = now.Wetness;

            if (!onWets && wetness >= 0.40 && !_saidWetTyres)
            {
                _saidWetTyres = true;
                _saidSlickTyres = false;
                Say("wx.wets", "weather.boxForWets", RadioCategory.Weather, 3, frame);
            }
            else if (!onWets && wetness >= 0.12 && !_saidWetTyres)
            {
                _saidWetTyres = true;
                _saidSlickTyres = false;
                Say("wx.inters", "weather.boxForInters", RadioCategory.Weather, 3, frame);
            }
            else if (onWets && wetness < 0.06 && !_saidSlickTyres)
            {
                _saidSlickTyres = true;
                _saidWetTyres = false;
                Say("wx.slicks", "weather.boxForSlicks", RadioCategory.Weather, 3, frame);
            }
        }

        private void DamageCalls(TelemetryFrame frame)
        {
            if (frame.DamageMax > 0.35)
                Say("dmg.heavy", "damage.heavy", RadioCategory.Damage, 3, frame, cooldown: 120);
            else if (frame.DamageMax > 0.10)
                Say("dmg.light", "damage.light", RadioCategory.Damage, 1, frame, cooldown: 180);
        }

        private void PositionCalls(TelemetryFrame frame, bool isRace)
        {
            if (!isRace || frame.Position <= 0) { _lastPosition = frame.Position; return; }
            if (_lastPosition == 0) { _lastPosition = frame.Position; return; }
            if (frame.Position == _lastPosition) return;
            if (frame.InPitLane) { _lastPosition = frame.Position; return; }

            if (frame.Position < _lastPosition)
                Say("pos.up", "position.gained", RadioCategory.Encouragement, 1, frame, frame.Position, cooldown: 10);
            else
                Say("pos.down", "position.lost", RadioCategory.Encouragement, 1, frame, frame.Position, cooldown: 10);

            _lastPosition = frame.Position;
        }

        private void QualifyingCalls(TelemetryFrame frame)
        {
            if (frame.BestLapTime > TimeSpan.Zero &&
                (_bestLapSeen == TimeSpan.Zero || frame.BestLapTime < _bestLapSeen))
            {
                _bestLapSeen = frame.BestLapTime;
                if (frame.CompletedLaps > 1)
                    Say("q.pb", "good.lap", RadioCategory.Encouragement, 1, frame, cooldown: 20);
            }
        }

        private void EndOfRaceCalls(TelemetryFrame frame)
        {
            int totalLaps = frame.TotalLaps > 0 ? frame.TotalLaps : _session.ResolveLaps(_circuit);
            if (totalLaps <= 0) return;
            int lapsLeft = totalLaps - frame.CompletedLaps;

            if (lapsLeft == 1 && !_saidFinalLap)
            {
                _saidFinalLap = true;
                Say("end.final", "session.finalLap", RadioCategory.Session, 3, frame);
            }
            else if (lapsLeft > 1 && (lapsLeft == 5 || lapsLeft == 10) && Rules.Verbosity >= 1)
            {
                Say("end.count" + lapsLeft, "session.lapsRemaining", RadioCategory.Session, 1, frame, lapsLeft);
            }
        }

        // ---- Support ------------------------------------------------------------------------

        private CompoundSpec ChooseNextCompound(TelemetryFrame frame, StintTracker tyres, int lapsLeft)
        {
            double wetness = WetnessNow(frame);
            if (wetness >= 0.12) return Compounds.ForConditions(wetness);

            // Enough laps left to need something durable, otherwise the softest available.
            double severity = 0.7 + 0.15 * _circuit.TyreSeverity;
            double softLaps = 78.0 / (Compounds.Soft.WearPerLapPercent * severity);
            double mediumLaps = 78.0 / (Compounds.Medium.WearPerLapPercent * severity);

            if (lapsLeft <= softLaps) return Compounds.Soft;
            if (lapsLeft <= mediumLaps) return Compounds.Medium;
            return Compounds.Hard;
        }

        private string BuildAdvice(TelemetryFrame frame, StintTracker tyres, int lapsLeft)
        {
            if (Window == null) return "";
            if (lapsLeft <= 0) return "";

            if (Window.CanMakeTheEnd && _session.MandatoryPitStops == 0)
                return "Fino alla fine con queste gomme";

            string compound = RecommendedCompound != null ? RecommendedCompound.Name : "";
            if (frame.CompletedLaps >= Window.LatestLap)
                return "Box subito, " + compound;
            if (Window.IsOpen(frame.CompletedLaps))
                return "Finestra aperta, box entro il giro " + Window.LatestLap + " su " + compound;
            return "Box previsto al giro " + Window.IdealLap + " su " + compound;
        }

        private double WetnessNow(TelemetryFrame frame)
        {
            if (_weather == null) return 0;
            return _weather.SampleAt(frame.SessionElapsed.TotalMinutes).Wetness;
        }

        private void Say(string ruleKey, string phraseKey, string category, int priority,
                         TelemetryFrame frame, object arg0 = null, object arg1 = null, double cooldown = 0)
        {
            object[] args = arg1 != null ? new[] { arg0, arg1 }
                          : arg0 != null ? new[] { arg0 }
                          : new object[0];

            var message = new EngineerMessage
            {
                RuleKey = ruleKey,
                Text = _book.Say(phraseKey, args),
                Category = category,
                Priority = priority,
                Lap = frame.CompletedLaps,
                DisplaySeconds = priority >= 3 ? 8 : 6
            };
            Radio.Enqueue(message, cooldown);
        }

        private static string Fmt(double seconds) =>
            seconds.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);

        private static string ShortName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "";
            var parts = name.Trim().Split(' ');
            return parts[parts.Length - 1];
        }
    }
}
