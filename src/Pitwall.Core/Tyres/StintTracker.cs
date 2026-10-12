using System;
using System.Collections.Generic;
using System.Linq;
using Pitwall.Model;
using Pitwall.Rules;

namespace Pitwall.Tyres
{
    /// <summary>One set of tyres and what has happened to it.</summary>
    public sealed class Stint
    {
        public int Number { get; set; }
        public CompoundKind Compound { get; set; }
        public string CompoundName { get; set; } = "";
        public int StartLap { get; set; }
        public int EndLap { get; set; }
        public int Laps => Math.Max(0, EndLap - StartLap);
        public double StartWearPercent { get; set; }
        public double EndWearPercent { get; set; }
        public TimeSpan BestLap { get; set; }
        public bool FinishedByPitStop { get; set; }
    }

    /// <summary>
    /// Follows the life of the tyres fitted to the player car: wear, temperature window, how many
    /// laps are left before the cliff and whether a stop is due. Fed one telemetry frame at a time.
    /// </summary>
    public sealed class StintTracker
    {
        private readonly List<double> _wearPerLapSamples = new List<double>();
        private readonly List<TimeSpan> _stintLapTimes = new List<TimeSpan>();
        private double _wearAtLapStart;
        private int _lastSeenLap = -1;

        public TyreRules Rules { get; set; } = new TyreRules();

        /// <summary>Circuit severity, 1 to 5. Scales the modelled degradation.</summary>
        public int TyreSeverity { get; set; } = 3;

        public CompoundSpec Compound { get; private set; } = Compounds.Medium;

        public string RawCompoundName { get; private set; } = "";

        /// <summary>Worst corner wear, 0 to 100. This is the number the engineer talks about.</summary>
        public double WearPercent { get; private set; }

        public double WearFrontLeft { get; private set; }
        public double WearFrontRight { get; private set; }
        public double WearRearLeft { get; private set; }
        public double WearRearRight { get; private set; }

        public double TemperatureAvg { get; private set; }

        public int StintLaps { get; private set; }

        public int CurrentStintNumber { get; private set; } = 1;

        public List<Stint> History { get; } = new List<Stint>();

        /// <summary>Compounds used so far this session, for the two-compound rule.</summary>
        public HashSet<CompoundKind> CompoundsUsed { get; } = new HashSet<CompoundKind>();

        public int PitStops { get; private set; }

        /// <summary>Average wear consumed per lap on this set, measured from telemetry.</summary>
        public double MeasuredWearPerLap =>
            _wearPerLapSamples.Count == 0 ? Compound.WearPerLapPercent * SeverityFactor : _wearPerLapSamples.Average();

        private double SeverityFactor => 0.7 + 0.15 * TyreSeverity;

        /// <summary>Laps left before the tyres reach the cliff. Negative means you are past it.</summary>
        public double LapsToCliff
        {
            get
            {
                double rate = Math.Max(0.05, MeasuredWearPerLap);
                return (Rules.CliffWearPercent - WearPercent) / rate;
            }
        }

        /// <summary>Laps left before the tyres are simply finished.</summary>
        public double LapsToCritical
        {
            get
            {
                double rate = Math.Max(0.05, MeasuredWearPerLap);
                return (Rules.CriticalWearPercent - WearPercent) / rate;
            }
        }

        public bool PastCliff => WearPercent >= Rules.CliffWearPercent;

        public bool Critical => WearPercent >= Rules.CriticalWearPercent;

        /// <summary>True while the rubber is below its working window.</summary>
        public bool ColdTyres => TemperatureAvg > 0 && TemperatureAvg < Compound.OptimalTempMin - 8;

        /// <summary>True when the tyres are overheating and losing grip.</summary>
        public bool OverheatingTyres => TemperatureAvg > Compound.OptimalTempMax + 8;

        /// <summary>
        /// Estimated lap time penalty right now compared to a fresh set of this compound,
        /// combining warm-up, the wear curve and the cliff.
        /// </summary>
        public double PaceLossSeconds
        {
            get
            {
                double loss = 0;

                // Linear degradation up to the cliff, then it falls apart quickly.
                double linear = Math.Min(WearPercent, Rules.CliffWearPercent) * 0.012;
                loss += linear;

                if (WearPercent > Rules.CliffWearPercent)
                    loss += (WearPercent - Rules.CliffWearPercent) * Compound.CliffPenaltyPerPercent;

                if (StintLaps < Compound.WarmupLaps)
                    loss += (Compound.WarmupLaps - StintLaps) * 0.8;

                if (ColdTyres) loss += 0.6;
                if (OverheatingTyres) loss += 0.4;

                return loss;
            }
        }

        /// <summary>Resets everything, called when a new session starts.</summary>
        public void ResetSession()
        {
            History.Clear();
            CompoundsUsed.Clear();
            _wearPerLapSamples.Clear();
            _stintLapTimes.Clear();
            WearPercent = 0;
            StintLaps = 0;
            PitStops = 0;
            CurrentStintNumber = 1;
            _lastSeenLap = -1;
            _wearAtLapStart = 0;
        }

        /// <summary>
        /// Feeds one telemetry frame. Wear values are expected as percentages of tyre life used,
        /// 0 for new and 100 for gone.
        /// </summary>
        public void Update(string gameCompound, double wearFl, double wearFr, double wearRl, double wearRr,
                           double tempAvg, int completedLaps, TimeSpan lastLapTime)
        {
            var spec = Compounds.FromGameName(gameCompound);
            if (spec.Kind != Compound.Kind)
                OnCompoundChanged(spec, gameCompound, completedLaps);

            RawCompoundName = gameCompound ?? "";
            WearFrontLeft = wearFl;
            WearFrontRight = wearFr;
            WearRearLeft = wearRl;
            WearRearRight = wearRr;
            WearPercent = Math.Max(Math.Max(wearFl, wearFr), Math.Max(wearRl, wearRr));
            TemperatureAvg = tempAvg;

            if (completedLaps != _lastSeenLap)
            {
                if (_lastSeenLap >= 0)
                {
                    double delta = WearPercent - _wearAtLapStart;
                    // Ignore the lap where the set was changed, and obviously bogus samples.
                    if (delta > 0.01 && delta < 25)
                    {
                        _wearPerLapSamples.Add(delta);
                        if (_wearPerLapSamples.Count > 8) _wearPerLapSamples.RemoveAt(0);
                    }
                    StintLaps++;
                    if (lastLapTime > TimeSpan.Zero) _stintLapTimes.Add(lastLapTime);
                }
                _wearAtLapStart = WearPercent;
                _lastSeenLap = completedLaps;
            }

            CompoundsUsed.Add(Compound.Kind);
        }

        private void OnCompoundChanged(CompoundSpec newSpec, string rawName, int completedLaps)
        {
            if (History.Count > 0 || StintLaps > 0 || WearPercent > 0)
            {
                History.Add(new Stint
                {
                    Number = CurrentStintNumber,
                    Compound = Compound.Kind,
                    CompoundName = Compound.Name,
                    StartLap = Math.Max(0, completedLaps - StintLaps),
                    EndLap = completedLaps,
                    EndWearPercent = WearPercent,
                    BestLap = _stintLapTimes.Count > 0 ? _stintLapTimes.Min() : TimeSpan.Zero,
                    FinishedByPitStop = true
                });
                CurrentStintNumber++;
                PitStops++;
            }

            Compound = newSpec;
            RawCompoundName = rawName ?? "";
            StintLaps = 0;
            _wearPerLapSamples.Clear();
            _stintLapTimes.Clear();
            _wearAtLapStart = 0;
        }

        /// <summary>
        /// Whether the two-compound rule is still outstanding. Wet running suspends it, exactly
        /// like the real regulation.
        /// </summary>
        public bool TwoCompoundRuleSatisfied(bool sessionWasWet)
        {
            if (sessionWasWet) return true;
            var slicks = CompoundsUsed.Where(c => c == CompoundKind.Soft ||
                                                  c == CompoundKind.Medium ||
                                                  c == CompoundKind.Hard).ToList();
            return slicks.Count >= 2;
        }

        /// <summary>
        /// Suggests the pit window for the rest of the race: the earliest lap where the tyres
        /// would still make the end, and the last lap before they fall apart.
        /// </summary>
        public PitWindow SuggestWindow(int currentLap, int totalLaps, CompoundSpec nextCompound)
        {
            var window = new PitWindow();
            if (totalLaps <= 0) return window;

            int lapsRemaining = Math.Max(0, totalLaps - currentLap);
            var target = nextCompound ?? Compounds.Medium;
            double nextRate = Math.Max(0.05, target.WearPerLapPercent * SeverityFactor);

            // Latest lap where the new set would still be under the cliff at the flag.
            double maxLapsOnNewSet = Rules.CliffWearPercent / nextRate;
            window.EarliestLap = Math.Max(Math.Max(1, currentLap), (int)Math.Ceiling(totalLaps - maxLapsOnNewSet));

            // Latest lap the current set survives.
            window.LatestLap = currentLap + (int)Math.Floor(Math.Max(0, LapsToCritical));
            if (window.LatestLap > totalLaps) window.LatestLap = totalLaps;

            window.IdealLap = currentLap + (int)Math.Round(Math.Max(0, LapsToCliff));
            if (window.IdealLap > totalLaps) window.IdealLap = totalLaps;
            if (window.IdealLap < window.EarliestLap) window.IdealLap = window.EarliestLap;

            window.CanMakeTheEnd = LapsToCritical >= lapsRemaining;
            window.NextCompound = target;
            return window;
        }
    }

    /// <summary>The answer to the question the driver always asks: when do I box?</summary>
    public sealed class PitWindow
    {
        public int EarliestLap { get; set; }
        public int IdealLap { get; set; }
        public int LatestLap { get; set; }
        public bool CanMakeTheEnd { get; set; }
        public CompoundSpec NextCompound { get; set; }

        public bool IsOpen(int currentLap) => currentLap >= EarliestLap && currentLap <= LatestLap;
    }
}
