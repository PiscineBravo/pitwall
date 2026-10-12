using System;
using System.Collections.Generic;
using System.Linq;
using Pitwall.Live;

namespace Pitwall.RaceControl
{
    public enum IncidentKind
    {
        CarStoppedOnTrack,
        CarRetired,
        HeavyDamage,
        PlayerOffTrack,
        PlayerStopped,
        DebrisOnTrack
    }

    /// <summary>Something that happened on track and that race control has to react to.</summary>
    public sealed class Incident
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public IncidentKind Kind { get; set; }
        public string DriverName { get; set; } = "";
        public string DriverId { get; set; } = "";

        /// <summary>Where on the lap it happened, 0 to 1.</summary>
        public double TrackPositionPercent { get; set; }

        /// <summary>Marshalling sector, 1 based.</summary>
        public int Sector { get; set; } = 1;

        /// <summary>1 harmless to 5 race stopping.</summary>
        public int Severity { get; set; } = 1;

        public int Lap { get; set; }
        public DateTime Utc { get; set; } = DateTime.UtcNow;
        public string Description { get; set; } = "";

        public override string ToString() => Description;
    }

    /// <summary>
    /// Watches the field and reports incidents. Assetto Corsa does not tell anyone that a car
    /// has crashed, so this works it out from behaviour: cars that stop away from the pits,
    /// cars that disappear from the timing screen, and damage spikes on the player car.
    /// </summary>
    public sealed class IncidentDetector
    {
        private sealed class CarState
        {
            public double StoppedSeconds;
            public double MovingSeconds;
            public int RaisedSeverity;
            public double LastSeenSeconds;
            public bool WasConnected;
            public Incident Hazard;
        }

        private readonly Dictionary<string, CarState> _cars = new Dictionary<string, CarState>(StringComparer.OrdinalIgnoreCase);
        private CarState _player = new CarState();
        private double? _lastDamage;
        private double _sessionSeconds;
        private bool _fieldMoving;
        private Incident _damageHazard;
        private double _damageUntil;

        // These are telemetry heuristics, NOT FIA incident classification thresholds.
        public double LocalYellowSecondsThreshold { get; set; } = 1.5;
        public double ClusterDistanceMeters { get; set; } = 150;
        public IEnumerable<Incident> ActiveHazards => _cars.Values.Concat(new[] { _player })
            .Where(s => s.Hazard != null).Select(s => s.Hazard)
            .Concat(_damageHazard == null ? Enumerable.Empty<Incident>() : new[] { _damageHazard });
        public bool HasActiveHazards => ActiveHazards.Any();

        /// <summary>Speed below which a car counts as stopped, in km/h.</summary>
        public double StoppedSpeedKmh { get; set; } = 8;

        /// <summary>Seconds a car has to sit still before it becomes an incident.</summary>
        public double StoppedSecondsThreshold { get; set; } = 4.0;

        /// <summary>Jump in damage that counts as a heavy accident, 0 to 1 scale.</summary>
        public double HeavyDamageDelta { get; set; } = 0.18;

        /// <summary>Number of marshalling sectors the lap is divided into.</summary>
        public int SectorCount { get; set; } = 3;

        public void Reset()
        {
            _cars.Clear();
            _player = new CarState();
            _lastDamage = null;
            _sessionSeconds = 0;
            _fieldMoving = false;
            _damageHazard = null;
        }

        /// <summary>Processes one frame and returns any new incident found.</summary>
        public List<Incident> Update(TelemetryFrame frame)
        {
            var found = new List<Incident>();
            if (frame == null || !frame.GameRunning || frame.Paused || frame.Replay || frame.InMenu) return found;

            double dt = Math.Max(0, Math.Min(1.0, frame.DeltaSeconds));
            _sessionSeconds += dt;
            if (_sessionSeconds >= _damageUntil) _damageHazard = null;
            // Do not mistake a stationary starting grid for a multi-car accident.
            _fieldMoving |= frame.CompletedLaps > 0 || frame.SpeedKmh > StoppedSpeedKmh + 4 ||
                frame.Opponents.Any(o => o.IsConnected && !o.InPit && !o.InPitLane &&
                    !SafetyCar.IsSafetyCar(o.Name) && o.SpeedKmh > StoppedSpeedKmh + 4);

            // ---- Opponents -----------------------------------------------------------------
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var o in frame.Opponents)
            {
                if (o.IsPlayer || string.IsNullOrWhiteSpace(o.Name)) continue;
                // La safety car sta ferma ai box quasi tutta la gara: non e un incidente.
                if (SafetyCar.IsSafetyCar(o.Name)) continue;
                seen.Add(o.Name);

                if (!_cars.TryGetValue(o.Name, out var state))
                    _cars[o.Name] = state = new CarState();

                state.LastSeenSeconds = _sessionSeconds;

                if (!o.IsConnected && state.WasConnected)
                {
                    found.Add(new Incident
                    {
                        Kind = IncidentKind.CarRetired,
                        DriverName = o.Name,
                        TrackPositionPercent = o.TrackPositionPercent,
                        Sector = SectorOf(o.TrackPositionPercent),
                        Severity = 1,
                        Lap = frame.CompletedLaps,
                        Description = o.Name + " si è ritirato"
                    });
                }
                state.WasConnected = o.IsConnected;

                ObserveStop(state, o.Name, o.TrackPositionPercent, o.SpeedKmh,
                    o.InPitLane || o.InPit || !o.IsConnected, false, frame.CompletedLaps, dt);
            }

            // Cars that vanished from the timing screen altogether.
            foreach (var key in _cars.Keys.ToList())
            {
                var st = _cars[key];
                if (seen.Contains(key)) continue;
                if (_sessionSeconds - st.LastSeenSeconds > 5 && st.WasConnected)
                {
                    st.WasConnected = false;
                    ClearStop(st);
                    found.Add(new Incident
                    {
                        Kind = IncidentKind.CarRetired,
                        DriverName = key,
                        Severity = 1,
                        Lap = frame.CompletedLaps,
                        Description = key + " non è più in pista"
                    });
                }
            }

            // ---- Player --------------------------------------------------------------------
            double damage = frame.DamageMax;
            if (!double.IsNaN(damage) && !double.IsInfinity(damage))
            {
                double delta = damage - (_lastDamage ?? damage);
                if (_fieldMoving && !frame.InPit && !frame.InPitLane && delta >= HeavyDamageDelta)
                {
                    _damageHazard = new Incident
                    {
                        Kind = IncidentKind.HeavyDamage,
                        DriverName = frame.PlayerName,
                        TrackPositionPercent = frame.TrackPositionPercent,
                        Sector = SectorOf(frame.TrackPositionPercent),
                        Severity = delta >= 0.45 ? 4 : 3,
                        Lap = frame.CompletedLaps,
                        Description = "Contatto: danni importanti"
                    };
                    _damageUntil = _sessionSeconds + 10;
                    found.Add(_damageHazard);
                }
                _lastDamage = damage;
            }

            ObserveStop(_player, frame.PlayerName, frame.TrackPositionPercent, frame.SpeedKmh,
                frame.InPitLane || frame.InPit, true, frame.CompletedLaps, dt);

            // Classify after observing the WHOLE field: list order must not decide VSC vs SC.
            var stopped = _cars.Values.Concat(new[] { _player }).Where(s => s.Hazard != null).ToList();
            foreach (var state in stopped)
            {
                int severity = state.StoppedSeconds >= StoppedSecondsThreshold ? 3 : 2;
                if (severity == 3 && frame.TrackLengthMeters > 0 && stopped.Any(other =>
                    other != state && other.StoppedSeconds >= StoppedSecondsThreshold &&
                    CircularDistance(state.Hazard.TrackPositionPercent, other.Hazard.TrackPositionPercent)
                        * frame.TrackLengthMeters <= ClusterDistanceMeters)) severity = 4;
                if (severity > state.RaisedSeverity)
                {
                    // Keep previous feed entries immutable, but retain the incident identity.
                    state.Hazard = new Incident
                    {
                        Id = state.Hazard.Id, Kind = state.Hazard.Kind,
                        DriverName = state.Hazard.DriverName,
                        TrackPositionPercent = state.Hazard.TrackPositionPercent,
                        Sector = state.Hazard.Sector, Severity = severity,
                        Lap = frame.CompletedLaps,
                        Description = (severity == 4 ? "Più vetture ferme nello stesso tratto: " :
                            severity == 3 ? "Vettura ancora ferma in pista: " : "Vettura lenta o ferma: ") + state.Hazard.DriverName
                    };
                    state.RaisedSeverity = severity;
                    found.Add(state.Hazard);
                }
            }

            return found;
        }

        private void ObserveStop(CarState state, string name, double position, double speed,
                                 bool clear, bool player, int lap, double dt)
        {
            if (clear || !_fieldMoving || double.IsNaN(position) || double.IsInfinity(position))
            {
                ClearStop(state);
                return;
            }
            if (speed < StoppedSpeedKmh)
            {
                state.MovingSeconds = 0;
                state.StoppedSeconds += dt;
                if (state.StoppedSeconds >= LocalYellowSecondsThreshold)
                {
                    if (state.Hazard == null) state.Hazard = new Incident
                    {
                        Kind = player ? IncidentKind.PlayerStopped : IncidentKind.CarStoppedOnTrack,
                        DriverName = name, Lap = lap
                    };
                    state.Hazard.TrackPositionPercent = Normalise(position);
                    state.Hazard.Sector = SectorOf(position);
                }
            }
            else if (speed >= StoppedSpeedKmh + 4)
            {
                state.MovingSeconds += dt;
                if (state.MovingSeconds >= 1) ClearStop(state);
            }
        }

        private static void ClearStop(CarState state)
        {
            state.StoppedSeconds = 0;
            state.MovingSeconds = 0;
            state.RaisedSeverity = 0;
            state.Hazard = null;
        }

        private static double Normalise(double p) => Math.Max(0, Math.Min(1, p > 1 ? p / 100 : p));
        private static double CircularDistance(double a, double b)
        {
            double d = Math.Abs(a - b);
            return Math.Min(d, 1 - d);
        }

        private int SectorOf(double trackPositionPercent)
        {
            if (SectorCount <= 0) return 1;
            double p = trackPositionPercent;
            if (p > 1.0) p = p / 100.0;
            int s = (int)Math.Floor(p * SectorCount) + 1;
            return Math.Max(1, Math.Min(SectorCount, s));
        }

    }
}
