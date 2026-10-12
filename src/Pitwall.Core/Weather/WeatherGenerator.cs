using System;
using System.Collections.Generic;
using System.Linq;
using Pitwall.Model;
using Pitwall.Rules;

namespace Pitwall.Weather
{
    /// <summary>
    /// Builds the weather script of a session from the circuit climate, the time of day and a
    /// seed. The same seed always produces the same weekend, which is what makes a championship
    /// worth replaying: you can lose a race to rain and know it was not made up afterwards.
    /// </summary>
    public static class WeatherGenerator
    {
        /// <summary>Generates a plan for one session.</summary>
        public static WeatherPlan Generate(Circuit circuit, SessionConfig session, WeatherRules rules, int seed)
        {
            circuit = circuit ?? new Circuit();
            rules = rules ?? new WeatherRules();
            var rnd = new Random(Mix(seed, (int)session.Kind));

            double durationMinutes = EstimateMinutes(circuit, session);
            double baseAir = circuit.TypicalAirTemperature + (rnd.NextDouble() * 6 - 3);
            baseAir += TimeOfDayOffset(session.StartTimeOfDay);

            var plan = new WeatherPlan { Seed = seed };

            double rainChance = circuit.RainProbability * rules.RainMultiplier;
            if (!rules.DynamicWeather) rainChance *= 0.35;
            bool willRain = rnd.NextDouble() < rainChance;

            if (!willRain)
            {
                BuildDry(plan, rnd, rules, baseAir, durationMinutes);
            }
            else
            {
                BuildWet(plan, rnd, rules, baseAir, durationMinutes);
            }

            plan.Summary = Summarise(plan);
            return plan;
        }

        /// <summary>Generates a coherent script for every session of a round in one go.</summary>
        public static void GenerateWeekend(Circuit circuit, Round round, WeatherRules rules)
        {
            if (round == null) return;
            foreach (var session in round.Sessions)
                session.Weather = Generate(circuit, session, rules, Mix(round.Seed, round.Order));
        }

        private static void BuildDry(WeatherPlan plan, Random rnd, WeatherRules rules, double baseAir, double duration)
        {
            var sky = PickDrySky(rnd);
            double air = baseAir;
            double road = air + rules.DryTrackTempOffset + SkyTempOffset(sky);
            int wind = rnd.Next(0, 360);

            plan.Phases.Add(new WeatherPhase
            {
                StartMinute = 0,
                Sky = sky,
                Wetness = 0,
                AirTemperature = Math.Round(air, 1),
                RoadTemperature = Math.Round(road, 1),
                WindSpeedKmh = Math.Round(3 + rnd.NextDouble() * 18, 1),
                WindDirection = wind,
                Grip = 0.97
            });

            // A gentle drift in cloud cover and temperature over the session.
            if (rules.DynamicWeather && duration > 20 && rnd.NextDouble() < 0.6 * rules.Volatility)
            {
                var drifted = Drift(sky, rnd);
                double air2 = air + (rnd.NextDouble() * 4 - 2);
                plan.Phases.Add(new WeatherPhase
                {
                    StartMinute = Math.Round(duration * (0.4 + rnd.NextDouble() * 0.3)),
                    Sky = drifted,
                    Wetness = 0,
                    AirTemperature = Math.Round(air2, 1),
                    RoadTemperature = Math.Round(air2 + rules.DryTrackTempOffset + SkyTempOffset(drifted), 1),
                    WindSpeedKmh = Math.Round(3 + rnd.NextDouble() * 18, 1),
                    WindDirection = wind,
                    Grip = 1.0
                });
            }
        }

        private static void BuildWet(WeatherPlan plan, Random rnd, WeatherRules rules, double baseAir, double duration)
        {
            double air = baseAir - 3 - rnd.NextDouble() * 3;
            int wind = rnd.Next(0, 360);
            double windSpeed = Math.Round(8 + rnd.NextDouble() * 22, 1);

            // Three shapes of a wet session: rain from the start, rain arriving, rain drying up.
            int shape = rnd.Next(3);
            double onset = shape == 0 ? 0 : Math.Round(duration * (0.15 + rnd.NextDouble() * 0.45));
            var peak = rnd.NextDouble() < 0.35 ? SkyCondition.HeavyRain
                     : rnd.NextDouble() < 0.6 ? SkyCondition.Rain
                     : SkyCondition.LightRain;

            if (onset > 0)
            {
                plan.Phases.Add(Phase(0, SkyCondition.Overcast, 0, air + 2, rules, windSpeed, wind, 0.96));
                // The sky darkens a few minutes before the first drops, which is the cue for the
                // engineer to tell you rain is coming.
                plan.Phases.Add(Phase(Math.Max(1, onset - 5), SkyCondition.Overcast, 0.02, air + 1, rules, windSpeed + 4, wind, 0.95));
            }

            plan.Phases.Add(Phase(onset, SkyCondition.LightRain, 0.12, air, rules, windSpeed + 5, wind, 0.88));
            plan.Phases.Add(Phase(onset + Math.Max(4, duration * 0.12), peak, WetnessFor(peak), air - 1, rules, windSpeed + 7, wind, GripFor(peak)));

            double dryingStart = onset + Math.Max(8, duration * 0.35);
            if (shape == 2 && dryingStart < duration)
            {
                plan.Phases.Add(Phase(dryingStart, SkyCondition.LightRain, 0.10, air + 1, rules, windSpeed, wind, 0.90));
                plan.Phases.Add(Phase(dryingStart + Math.Max(5, duration * 0.12), SkyCondition.Overcast, 0.04, air + 2, rules, windSpeed, wind, 0.94));
                if (dryingStart + duration * 0.3 < duration)
                    plan.Phases.Add(Phase(dryingStart + duration * 0.3, SkyCondition.Scattered, 0.0, air + 3, rules, windSpeed - 2, wind, 0.98));
            }
        }

        /// <summary>
        /// Porta il tempo a una condizione decisa dalla direzione gara, a partire da adesso.
        ///
        /// Il passato non si tocca: le fasi gia trascorse restano dove sono, altrimenti il
        /// grafico della sessione si riscriverebbe sotto gli occhi di chi lo guarda. Da adesso
        /// si parte dalle condizioni esatte di questo istante e si arriva a quelle chieste in
        /// qualche minuto, perche il cielo che cambia di colpo si vede ed e brutto. Quello che
        /// era previsto piu avanti decade: l'ha deciso la direzione gara.
        /// </summary>
        /// <param name="transitionMinutes">Quanto ci mette ad arrivarci. Zero vale un cambio secco.</param>
        /// <returns>La fase di arrivo, per poter dire a chi ha premuto dove si va a parare.</returns>
        public static WeatherPhase Override(WeatherPlan plan, double minute, SkyCondition sky,
                                            double transitionMinutes, WeatherRules rules)
        {
            if (plan == null) return null;
            rules = rules ?? new WeatherRules();
            minute = Math.Max(0, minute);
            double transition = Math.Max(0, transitionMinutes);

            var now = plan.SampleAt(minute);

            // Ancora sul presente, cosi la transizione parte da dove siamo davvero.
            var anchor = new WeatherPhase
            {
                StartMinute = minute,
                Sky = now.Sky,
                Wetness = now.Wetness,
                AirTemperature = now.AirTemperature,
                RoadTemperature = now.RoadTemperature,
                WindSpeedKmh = now.WindSpeedKmh,
                WindDirection = now.WindDirection,
                Grip = now.Grip
            };

            // La temperatura segue il cielo: sotto la pioggia cala, al sereno risale.
            double targetAir = now.AirTemperature + SkyTempOffset(sky) - SkyTempOffset(now.Sky);
            var target = Phase(minute + transition, sky, WetnessFor(sky), targetAir, rules,
                               now.WindSpeedKmh, now.WindDirection, GripFor(sky));

            // Phase arrotonda il minuto, e con transizioni corte questo puo far finire il
            // bersaglio prima dell'ancora: in quel caso la transizione non esiste piu.
            if (target.StartMinute <= anchor.StartMinute) target.StartMinute = anchor.StartMinute + 0.01;

            plan.Phases = plan.Phases.Where(p => p.StartMinute < minute).ToList();
            plan.Phases.Add(anchor);
            plan.Phases.Add(target);
            plan.Summary = "Condizioni decise dalla direzione gara: " + target.Label + ".";
            return target;
        }

        private static WeatherPhase Phase(double minute, SkyCondition sky, double wetness, double air,
                                          WeatherRules rules, double windSpeed, int windDir, double grip)
        {
            double roadOffset = sky >= SkyCondition.LightRain ? 2 : rules.DryTrackTempOffset + SkyTempOffset(sky);
            return new WeatherPhase
            {
                StartMinute = Math.Round(minute),
                Sky = sky,
                Wetness = Math.Round(wetness, 3),
                AirTemperature = Math.Round(air, 1),
                RoadTemperature = Math.Round(air + roadOffset, 1),
                WindSpeedKmh = Math.Round(Math.Max(0, windSpeed), 1),
                WindDirection = windDir,
                Grip = Math.Round(grip, 3)
            };
        }

        private static double WetnessFor(SkyCondition sky)
        {
            switch (sky)
            {
                case SkyCondition.LightRain: return 0.18;
                case SkyCondition.Rain: return 0.45;
                case SkyCondition.HeavyRain: return 0.80;
                default: return 0;
            }
        }

        private static double GripFor(SkyCondition sky)
        {
            switch (sky)
            {
                case SkyCondition.LightRain: return 0.86;
                case SkyCondition.Rain: return 0.78;
                case SkyCondition.HeavyRain: return 0.68;
                default: return 0.98;
            }
        }

        private static SkyCondition PickDrySky(Random rnd)
        {
            double r = rnd.NextDouble();
            if (r < 0.45) return SkyCondition.Clear;
            if (r < 0.75) return SkyCondition.FewClouds;
            if (r < 0.92) return SkyCondition.Scattered;
            return SkyCondition.Overcast;
        }

        private static SkyCondition Drift(SkyCondition sky, Random rnd)
        {
            int step = rnd.Next(2) == 0 ? -1 : 1;
            int v = (int)sky + step;
            if (v < 0) v = 0;
            if (v > (int)SkyCondition.Overcast) v = (int)SkyCondition.Overcast;
            return (SkyCondition)v;
        }

        private static double SkyTempOffset(SkyCondition sky)
        {
            switch (sky)
            {
                case SkyCondition.Clear: return 4;
                case SkyCondition.FewClouds: return 2;
                case SkyCondition.Scattered: return 0;
                default: return -3;
            }
        }

        private static double TimeOfDayOffset(TimeSpan startTime)
        {
            double h = startTime.TotalHours;
            if (h < 9) return -4;
            if (h < 12) return -1;
            if (h < 16) return 1.5;
            if (h < 19) return 0;
            return -3;
        }

        private static double EstimateMinutes(Circuit circuit, SessionConfig session)
        {
            switch (session.LengthMode)
            {
                case SessionLengthMode.Minutes:
                    return Math.Max(5, session.Length);
                case SessionLengthMode.Laps:
                    return Math.Max(5, session.Length * ReferenceLapMinutes(circuit));
                default:
                    return Math.Max(5, circuit.LapsForDistancePercent(session.Length) * ReferenceLapMinutes(circuit));
            }
        }

        private static double ReferenceLapMinutes(Circuit circuit)
        {
            double seconds = circuit.ReferenceLapSeconds > 5 ? circuit.ReferenceLapSeconds : 90;
            return seconds / 60.0;
        }

        private static string Summarise(WeatherPlan plan)
        {
            if (plan.Phases.Count == 0) return "";
            var first = plan.Phases.OrderBy(p => p.StartMinute).First();
            if (!plan.HasRain) return first.Label;

            var firstRain = plan.Phases.OrderBy(p => p.StartMinute).First(p => p.IsRaining);
            var last = plan.Phases.OrderBy(p => p.StartMinute).Last();

            if (firstRain.StartMinute <= 0)
                return last.IsRaining ? "Bagnato" : "Bagnato, poi in asciugamento";

            string when = firstRain.StartMinute < 20 ? "presto" : "a metà sessione";
            return last.IsRaining
                ? first.Label + ", pioggia " + when
                : first.Label + ", pioggia " + when + " poi asciutto";
        }

        private static int Mix(int a, int b)
        {
            unchecked
            {
                int h = 17;
                h = h * 31 + a;
                h = h * 31 + b;
                return h;
            }
        }
    }
}
