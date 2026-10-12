using System;
using System.Linq;
using GameReaderCommon;
using Pitwall.Live;

namespace User.Pitwall.Runtime
{
    /// <summary>
    /// Translates one SimHub <see cref="GameData"/> sample into the game-agnostic
    /// <see cref="TelemetryFrame"/> the rest of Pitwall works with.
    /// </summary>
    public sealed class FrameBuilder
    {
        private DateTime _lastPacket = DateTime.MinValue;
        private readonly WearNormaliser _wear = new WearNormaliser();

        /// <summary>Forces the tyre wear orientation instead of detecting it. Null means auto.</summary>
        public bool? WearIsRemainingLife
        {
            get { return _wear.ForcedRemainingLife; }
            set { _wear.ForcedRemainingLife = value; }
        }

        public void Reset() => _wear.Reset();

        public TelemetryFrame Build(GameData data)
        {
            var frame = new TelemetryFrame
            {
                Timestamp = DateTime.UtcNow,
                GameRunning = data.GameRunning,
                InMenu = data.GameInMenu,
                Replay = data.GameReplay,
                Paused = data.GamePaused
            };

            var d = data.NewData;
            if (d == null) return frame;

            frame.DeltaSeconds = _lastPacket == DateTime.MinValue
                ? 0
                : Math.Max(0, (d.PacketTime - _lastPacket).TotalSeconds);
            _lastPacket = d.PacketTime;

            // ---- Session ---------------------------------------------------------------------
            frame.SessionTypeName = d.SessionTypeName ?? "";
            frame.TrackId = d.TrackId ?? "";
            frame.TrackConfig = d.TrackConfig ?? "";
            frame.TrackName = d.TrackName ?? "";
            frame.TrackLengthMeters = d.TrackLength > 100 ? d.TrackLength : d.ReportedTrackLength;
            frame.SessionTimeLeft = d.SessionTimeLeft;
            frame.SessionElapsed = d.GuessedElaspedGameTime;
            frame.TotalLaps = d.TotalLaps;
            frame.RemainingLaps = d.RemainingLaps;

            // ---- Player ----------------------------------------------------------------------
            frame.PlayerName = d.PlayerName ?? "";
            frame.CarId = d.CarId ?? "";
            frame.Position = d.Position;
            frame.OpponentsCount = d.OpponentsCount;
            frame.CompletedLaps = d.CompletedLaps;
            frame.CurrentLap = d.CurrentLap;
            frame.CurrentLapTime = d.CurrentLapTime;
            frame.LastLapTime = d.LastLapTime;
            frame.BestLapTime = d.BestLapTime;
            frame.LapValid = d.IsLapValid;

            frame.TrackPositionPercent = d.TrackPositionPercent;
            frame.TrackPositionMeters = d.TrackPositionMeters;

            frame.SpeedKmh = d.SpeedKmh;
            frame.Throttle = d.Throttle;
            frame.Brake = d.Brake;
            frame.Gear = d.Gear ?? "N";
            frame.Rpm = d.Rpms;

            frame.Fuel = d.Fuel;
            frame.MaxFuel = d.MaxFuel;
            frame.EstimatedFuelLaps = d.EstimatedFuelRemaingLaps;

            frame.TyreWearFrontLeft = _wear.Normalise(d.TyreWearFrontLeft);
            frame.TyreWearFrontRight = _wear.Normalise(d.TyreWearFrontRight);
            frame.TyreWearRearLeft = _wear.Normalise(d.TyreWearRearLeft);
            frame.TyreWearRearRight = _wear.Normalise(d.TyreWearRearRight);
            frame.TyreTemperatureAvg = d.TyresTemperatureAvg;
            frame.TyrePressureAvg = (d.TyrePressureFrontLeft + d.TyrePressureFrontRight +
                                     d.TyrePressureRearLeft + d.TyrePressureRearRight) / 4.0;

            frame.BrakeTemperatureAvg = d.BrakesTemperatureAvg;
            frame.WaterTemperature = d.WaterTemperature;
            frame.OilTemperature = d.OilTemperature;

            // SimHub reports damage on a 0-100 scale; Pitwall works with 0-1.
            frame.DamageAvg = d.CarDamagesAvg / 100.0;
            frame.DamageMax = d.CarDamagesMax / 100.0;

            frame.InPit = d.IsInPit > 0;
            frame.InPitLane = d.IsInPitLane > 0;
            frame.PitLimiterOn = d.PitLimiterOn > 0;
            frame.LastPitStopDuration = d.LastPitStopDuration;

            frame.DrsAvailable = d.DRSAvailable;
            frame.DrsEnabled = d.DRSEnabled;
            frame.ErsPercent = d.ERSPercent;

            frame.FlagYellow = d.Flag_Yellow > 0;
            frame.FlagBlue = d.Flag_Blue > 0;
            frame.FlagWhite = d.Flag_White > 0;
            frame.FlagCheckered = d.Flag_Checkered > 0;
            frame.FlagBlack = d.Flag_Black > 0;

            frame.AirTemperature = d.AirTemperature;
            frame.RoadTemperature = d.RoadTemperature;

            // ---- Opponents ---------------------------------------------------------------------
            if (d.Opponents != null)
            {
                foreach (var o in d.Opponents)
                {
                    if (o == null) continue;
                    frame.Opponents.Add(new OpponentFrame
                    {
                        Name = o.Name ?? "",
                        CarNumber = o.CarNumber ?? "",
                        TeamName = o.TeamName ?? "",
                        IsPlayer = o.IsPlayer,
                        IsConnected = o.IsConnected,
                        Position = o.Position,
                        // SimHub counts the lap in progress, Pitwall counts laps finished.
                        CompletedLaps = Math.Max(0, (o.CurrentLap ?? 1) - 1),
                        TrackPositionPercent = o.TrackPositionPercent ?? 0,
                        SpeedKmh = o.Speed ?? 0,
                        BestLapTime = o.BestLapTime,
                        LastLapTime = o.LastLapTime,
                        // Via le proprieta per settore, deprecate: SimHub ora li espone come
                        // insieme di tempi, che e anche l'unica forma che reggera i tracciati
                        // con un numero di settori diverso da tre.
                        LastSector1 = Split(o.LastLapSectorTimes, 1),
                        LastSector2 = Split(o.LastLapSectorTimes, 2),
                        LastSector3 = Split(o.LastLapSectorTimes, 3),
                        BestSector1 = Split(o.BestSectorSplits, 1),
                        BestSector2 = Split(o.BestSectorSplits, 2),
                        BestSector3 = Split(o.BestSectorSplits, 3),
                        StartPosition = o.StartPosition,
                        GapToPlayer = o.GaptoPlayer,
                        GapToLeader = o.GaptoLeader,
                        LapsToLeader = o.LapsToLeader,
                        InPit = o.IsCarInPit,
                        InPitLane = o.IsCarInPitLane,
                        PitCount = o.PitCount ?? 0,
                        TyreCompound = o.FrontTyreCompound ?? ""
                    });
                }
            }

            // Assetto Corsa exposes the compound per car rather than on the player structure,
            // so take it from the player entry of the timing screen.
            var me = frame.Opponents.FirstOrDefault(o => o.IsPlayer);
            if (me != null && !string.IsNullOrWhiteSpace(me.TyreCompound))
                frame.TyreCompound = me.TyreCompound;

            return frame;
        }

        /// <summary>
        /// I settori arrivano da SimHub come valori opzionali e, a seconda del gioco, in secondi
        /// o come durata. Qui diventano secondi, o nulla se non ci sono.
        /// </summary>
        private static double? Seconds(double? value)
        {
            if (!value.HasValue || value.Value <= 0 || double.IsNaN(value.Value)) return null;
            return value.Value;
        }

        private static double? ToSeconds(TimeSpan? value) =>
            value.HasValue ? value.Value.TotalSeconds : (double?)null;

        /// <summary>
        /// Un settore da un insieme di tempi di SimHub. L'indice e 1 based e un tracciato puo
        /// averne meno di tre, quindi si accetta tranquillamente che manchi.
        /// </summary>
        private static double? Split(SectorTimes times, int sector)
        {
            if (times == null) return null;
            try
            {
                return Seconds(ToSeconds(times.GetSectorSplit(sector)));
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static double? Split(SectorSplits splits, int sector)
        {
            if (splits == null) return null;
            try
            {
                return Seconds(ToSeconds(splits.GetSectorSplit(sector)));
            }
            catch (Exception)
            {
                return null;
            }
        }
    }

    /// <summary>
    /// Games disagree on what a tyre wear number means: some count life remaining, some count
    /// life consumed, and the scale is sometimes 0-1 and sometimes 0-100. This works out the
    /// convention from the first sample of a session, when the tyres are fresh, and converts
    /// everything to a percentage of life consumed.
    /// </summary>
    public sealed class WearNormaliser
    {
        private bool _decided;
        private bool _invert;
        private double _scale = 1.0;

        /// <summary>Set to skip detection when a car mod reports something unusual.</summary>
        public bool? ForcedRemainingLife { get; set; }

        public void Reset()
        {
            _decided = false;
            _invert = false;
            _scale = 1.0;
        }

        public double Normalise(double raw)
        {
            if (double.IsNaN(raw) || double.IsInfinity(raw)) return 0;

            if (!_decided && Math.Abs(raw) > 0.0001)
            {
                _scale = Math.Abs(raw) <= 1.5 ? 100.0 : 1.0;
                double scaled = raw * _scale;
                _invert = ForcedRemainingLife ?? (scaled >= 50);
                _decided = true;
            }

            double value = raw * _scale;
            if (_invert) value = 100.0 - value;
            return Math.Max(0, Math.Min(100, value));
        }
    }
}
