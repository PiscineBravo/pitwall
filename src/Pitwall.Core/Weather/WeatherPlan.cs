using System;
using System.Collections.Generic;
using System.Linq;

using Pitwall.Lingue;

namespace Pitwall.Weather
{
    /// <summary>Sky states, ordered from the brightest to the wettest.</summary>
    public enum SkyCondition
    {
        Clear,
        FewClouds,
        Scattered,
        Overcast,
        LightRain,
        Rain,
        HeavyRain
    }

    /// <summary>Conditions at one point of a session. A plan is a list of these.</summary>
    public sealed class WeatherPhase
    {
        /// <summary>Minutes from the green light of the session.</summary>
        public double StartMinute { get; set; }

        public SkyCondition Sky { get; set; } = SkyCondition.Clear;

        /// <summary>Standing water on track, 0 bone dry to 1 flooded.</summary>
        public double Wetness { get; set; }

        public double AirTemperature { get; set; } = 24;

        public double RoadTemperature { get; set; } = 36;

        public double WindSpeedKmh { get; set; } = 8;

        /// <summary>Wind direction in degrees, 0 is north.</summary>
        public int WindDirection { get; set; }

        /// <summary>Overall grip level, 0 to 1.</summary>
        public double Grip { get; set; } = 1.0;

        public bool IsRaining => Sky >= SkyCondition.LightRain;

        /// <summary>CSP WeatherFX identifier, used when a session is written out for Content Manager.</summary>
        public string AcWeatherId
        {
            get
            {
                switch (Sky)
                {
                    case SkyCondition.Clear: return "clear";
                    case SkyCondition.FewClouds: return "few_clouds";
                    case SkyCondition.Scattered: return "scattered_clouds";
                    case SkyCondition.Overcast: return "overcast";
                    case SkyCondition.LightRain: return "light_rain";
                    case SkyCondition.Rain: return "rain";
                    default: return "heavy_rain";
                }
            }
        }

        /// <summary>
        /// Tipo meteo di WeatherFX, il numero che il controller passa a Custom Shaders Patch.
        /// I valori vengono dall'enumerazione ac.WeatherType di CSP.
        /// </summary>
        public int WeatherFxType
        {
            get
            {
                switch (Sky)
                {
                    case SkyCondition.Clear: return 15;            // Clear
                    case SkyCondition.FewClouds: return 16;        // FewClouds
                    case SkyCondition.Scattered: return 17;        // ScatteredClouds
                    case SkyCondition.Overcast: return 19;         // OvercastClouds
                    case SkyCondition.LightRain: return 6;         // LightRain
                    case SkyCondition.Rain: return 7;              // Rain
                    default: return 8;                             // HeavyRain
                }
            }
        }

        /// <summary>Intensita di pioggia per WeatherFX, da 0 a 1.</summary>
        public double RainIntensity
        {
            get
            {
                switch (Sky)
                {
                    case SkyCondition.LightRain: return 0.15;
                    case SkyCondition.Rain: return 0.38;
                    case SkyCondition.HeavyRain: return 0.70;
                    default: return 0;
                }
            }
        }

        public string Label
        {
            get
            {
                switch (Sky)
                {
                    case SkyCondition.Clear: return Testi.T("meteo.sereno");
                    case SkyCondition.FewClouds: return Testi.T("meteo.poco.nuvoloso");
                    case SkyCondition.Scattered: return Testi.T("meteo.nuvoloso");
                    case SkyCondition.Overcast: return Testi.T("meteo.coperto");
                    case SkyCondition.LightRain: return Testi.T("meteo.pioggia.leggera");
                    case SkyCondition.Rain: return Testi.T("meteo.pioggia");
                    default: return Testi.T("meteo.pioggia.forte");
                }
            }
        }
    }

    /// <summary>Conditions right now, interpolated between two phases.</summary>
    public sealed class WeatherSnapshot
    {
        public SkyCondition Sky { get; set; }
        public double Wetness { get; set; }
        public double AirTemperature { get; set; }
        public double RoadTemperature { get; set; }
        public double WindSpeedKmh { get; set; }
        public int WindDirection { get; set; }
        public double Grip { get; set; } = 1.0;
        public bool IsRaining => Sky >= SkyCondition.LightRain;
        public string Label { get; set; } = "";
    }

    /// <summary>An upcoming change the engineer can warn about.</summary>
    public sealed class ForecastEntry
    {
        public double MinutesFromNow { get; set; }
        public SkyCondition Sky { get; set; }
        public double Wetness { get; set; }
        public string Label { get; set; } = "";
        public bool StartsRain { get; set; }
        public bool StopsRain { get; set; }
    }

    /// <summary>The weather script of a single session.</summary>
    public sealed class WeatherPlan
    {
        public List<WeatherPhase> Phases { get; set; } = new List<WeatherPhase>();

        /// <summary>Seed the plan was generated from, so it can be rebuilt identically.</summary>
        public int Seed { get; set; }

        /// <summary>One-line description shown in the manager, for example "Asciutto, pioggia a metà gara".</summary>
        public string Summary { get; set; } = "";

        public bool HasRain => Phases.Any(p => p.IsRaining);

        /// <summary>Conditions at a given point of the session, linearly interpolated.</summary>
        public WeatherSnapshot SampleAt(double minute)
        {
            if (Phases.Count == 0)
                return new WeatherSnapshot { Sky = SkyCondition.Clear, AirTemperature = 24, RoadTemperature = 36, Label = "Sereno" };

            var ordered = Phases.OrderBy(p => p.StartMinute).ToList();
            var previous = ordered[0];
            var next = ordered[0];

            for (int i = 0; i < ordered.Count; i++)
            {
                if (ordered[i].StartMinute > minute) break;
                previous = ordered[i];
                next = i + 1 < ordered.Count ? ordered[i + 1] : ordered[i];
            }

            if (ReferenceEquals(previous, next) || next.StartMinute <= previous.StartMinute)
                return Snapshot(previous);

            double span = next.StartMinute - previous.StartMinute;
            double t = Math.Max(0, Math.Min(1, (minute - previous.StartMinute) / span));

            // Sky is a step: it changes when the next phase starts. Everything else eases across.
            var snap = new WeatherSnapshot
            {
                Sky = t < 0.5 ? previous.Sky : next.Sky,
                Wetness = Lerp(previous.Wetness, next.Wetness, t),
                AirTemperature = Lerp(previous.AirTemperature, next.AirTemperature, t),
                RoadTemperature = Lerp(previous.RoadTemperature, next.RoadTemperature, t),
                WindSpeedKmh = Lerp(previous.WindSpeedKmh, next.WindSpeedKmh, t),
                WindDirection = (int)Lerp(previous.WindDirection, next.WindDirection, t),
                Grip = Lerp(previous.Grip, next.Grip, t)
            };
            snap.Label = (t < 0.5 ? previous : next).Label;
            return snap;
        }

        /// <summary>Changes coming up inside the horizon, as the engineer would read them out.</summary>
        public List<ForecastEntry> Forecast(double currentMinute, double horizonMinutes)
        {
            var list = new List<ForecastEntry>();
            var now = SampleAt(currentMinute);
            bool rainingNow = now.IsRaining;

            foreach (var p in Phases.OrderBy(p => p.StartMinute))
            {
                if (p.StartMinute <= currentMinute) continue;
                double delta = p.StartMinute - currentMinute;
                if (delta > horizonMinutes) break;

                list.Add(new ForecastEntry
                {
                    MinutesFromNow = delta,
                    Sky = p.Sky,
                    Wetness = p.Wetness,
                    Label = p.Label,
                    StartsRain = p.IsRaining && !rainingNow,
                    StopsRain = !p.IsRaining && rainingNow
                });
                rainingNow = p.IsRaining;
            }

            return list;
        }

        private static WeatherSnapshot Snapshot(WeatherPhase p) => new WeatherSnapshot
        {
            Sky = p.Sky,
            Wetness = p.Wetness,
            AirTemperature = p.AirTemperature,
            RoadTemperature = p.RoadTemperature,
            WindSpeedKmh = p.WindSpeedKmh,
            WindDirection = p.WindDirection,
            Grip = p.Grip,
            Label = p.Label
        };

        private static double Lerp(double a, double b, double t) => a + (b - a) * t;

        /// <summary>A plan with a single, unchanging phase.</summary>
        public static WeatherPlan Constant(SkyCondition sky, double airTemp, double roadTemp)
        {
            return new WeatherPlan
            {
                Phases = new List<WeatherPhase>
                {
                    new WeatherPhase { StartMinute = 0, Sky = sky, AirTemperature = airTemp, RoadTemperature = roadTemp }
                },
                Summary = new WeatherPhase { Sky = sky }.Label
            };
        }
    }
}
