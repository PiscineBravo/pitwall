using System;
using System.Collections.Generic;
using System.Linq;
using Pitwall.Model;
using Pitwall.Rules;
using Pitwall.Weather;

namespace Pitwall.Storage
{
    /// <summary>
    /// The content Pitwall ships with: a circuit library covering the modern grand prix
    /// calendar, and a ready-made grid. Track and car identifiers point at the layouts of the
    /// widely used community packs; anything missing can be fixed in the manager in a few
    /// seconds without touching a file.
    /// </summary>
    public static class SeedData
    {
        private static Circuit C(string name, string country, string trackId, string config,
                                 double length, int laps, double refLap, int severity,
                                 int overtaking, double rain, double air, double pitLoss,
                                 double scProbability, int drs)
        {
            return new Circuit
            {
                Name = name,
                Country = country,
                AcTrackId = trackId,
                AcTrackConfig = config,
                LengthMeters = length,
                GrandPrixLaps = laps,
                ReferenceLapSeconds = refLap,
                TyreSeverity = severity,
                OvertakingDifficulty = overtaking,
                RainProbability = rain,
                TypicalAirTemperature = air,
                PitLaneLossSeconds = pitLoss,
                SafetyCarProbability = scProbability,
                DrsZones = drs,
                Sectors = 3
            };
        }

        /// <summary>The shipped circuit library.</summary>
        public static List<Circuit> Circuits()
        {
            return new List<Circuit>
            {
                C("Melbourne",        "Australia",      "melbourne",          "layout_f1_2025",  5278, 58, 78.0, 3, 3, 0.30, 21, 19.0, 0.55, 4),
                C("Shanghai",         "Cina",           "shanghai",           "layout_f1_2024",  5451, 56, 93.0, 4, 2, 0.25, 18, 21.0, 0.35, 2),
                C("Suzuka",           "Giappone",       "rt_suzuka",          "layout_f1_2024",  5807, 53, 90.0, 5, 4, 0.30, 17, 22.0, 0.40, 1),
                C("Sakhir",           "Bahrain",        "fn_losail",          "layout_f1_2024",  5419, 57, 84.0, 5, 2, 0.02, 27, 20.0, 0.30, 2),
                C("Jeddah",           "Arabia Saudita", "jeddah_2021_chq",    "jeddah_f1_2025",  6174, 50, 88.0, 3, 3, 0.02, 28, 18.5, 0.70, 3),
                C("Miami",            "Stati Uniti",    "miami_f1",           "layout_f1_2024",  5412, 57, 88.0, 4, 3, 0.35, 29, 20.0, 0.50, 3),
                C("Imola",            "Italia",         "imola",              "",                4909, 63, 76.0, 3, 5, 0.25, 22, 22.0, 0.45, 2),
                C("Monte Carlo",      "Monaco",         "monaco",             "",                3337, 78, 72.0, 1, 5, 0.25, 22, 17.0, 0.75, 1),
                C("Barcellona",       "Spagna",         "ks_barcelona",       "layout_gp",       4655, 66, 76.0, 5, 4, 0.10, 26, 20.0, 0.25, 2),
                C("Spielberg",        "Austria",        "fn_redbullring",     "austria_f1_2024", 4318, 71, 65.0, 4, 2, 0.35, 23, 18.0, 0.55, 3),
                C("Silverstone",      "Gran Bretagna",  "ks_silverstone",     "silverstone_f1_2024", 5891, 52, 87.0, 5, 3, 0.45, 19, 20.0, 0.50, 2),
                C("Spa-Francorchamps","Belgio",         "spa",                "layout_f1_2024",  7004, 44, 105.0, 4, 2, 0.50, 19, 19.0, 0.55, 2),
                C("Hungaroring",      "Ungheria",       "fn_hungaroring",     "layout_f1_2024",  4381, 70, 76.0, 4, 5, 0.25, 28, 19.0, 0.40, 3),
                C("Zandvoort",        "Paesi Bassi",    "zandvoort2023",      "layout_f1_2024",  4259, 72, 71.0, 4, 5, 0.40, 20, 20.0, 0.50, 2),
                C("Monza",            "Italia",         "monza",              "monza_f1_2024",   5793, 53, 81.0, 2, 1, 0.20, 26, 18.0, 0.35, 2),
                C("Baku",             "Azerbaigian",    "baku_2022",          "layout_f1_2024",  6003, 51, 102.0, 2, 1, 0.10, 25, 19.0, 0.80, 2),
                C("Singapore",        "Singapore",      "singapore_2020",     "layout_f1_2024",  4940, 62, 91.0, 3, 5, 0.40, 30, 21.0, 0.80, 3),
                C("Austin",           "Stati Uniti",    "cota_2022",          "layout_f1_2024",  5513, 56, 94.0, 4, 2, 0.20, 25, 20.0, 0.45, 2),
                C("Città del Messico","Messico",        "acu_mexico_2021",    "layout_f1_2024",  4304, 71, 78.0, 3, 3, 0.20, 21, 19.5, 0.50, 3),
                C("Interlagos",       "Brasile",        "vhe_interlagos",     "layout_f1_2024",  4309, 71, 71.0, 4, 2, 0.45, 24, 18.0, 0.60, 2),
                C("Las Vegas",        "Stati Uniti",    "lasvegas23",         "layout_f1_2024",  6201, 50, 95.0, 2, 1, 0.05, 12, 20.0, 0.60, 2),
                C("Yas Marina",       "Abu Dhabi",      "abu_dhabi_2021_chq", "layout_f1_2024",  5281, 58, 86.0, 3, 3, 0.02, 27, 20.5, 0.40, 2)
            };
        }

        private static Team T(string name, string shortName, string color, string car, string powerUnit,
                              params string[] skins)
        {
            return new Team
            {
                Name = name,
                ShortName = shortName,
                Color = color,
                AcCarId = car,
                PowerUnit = powerUnit,
                AcSkins = skins.ToList()
            };
        }

        // La vettura della stagione 2026. Si cambia qui e vale per ogni campionato nuovo.
        private const string F1Car = "vrc_formula_alpha_2026_csp";

        /// <summary>The ten teams of the shipped grid, matched to skins of the 2025 car pack.</summary>
        public static List<Team> Teams()
        {
            return new List<Team>
            {
                T("Red Bull Racing", "RBR", "#3671C6", F1Car, "Honda RBPT",
                  "Red_Bull_RB21_1_Verstappen", "Red_Bull_RB21_22_Tsunoda"),
                T("Ferrari", "FER", "#E8002D", F1Car, "Ferrari",
                  "Ferrari_SF-25_16_Leclerc", "Ferrari_SF-25_44_Hamilton"),
                T("McLaren", "MCL", "#FF8000", F1Car, "Mercedes",
                  "McLaren_MCL39_4_Norris", "McLaren_MCL39_81_Piastri"),
                T("Mercedes", "MER", "#27F4D2", F1Car, "Mercedes",
                  "Mercedes_W16_63_Russell", "Mercedes_W16_12_Antonelli"),
                T("Williams", "WIL", "#64C4FF", F1Car, "Mercedes",
                  "Williams_FW47_23_Albon", "Williams_FW47_55_Sainz"),
                T("Racing Bulls", "RB", "#6692FF", F1Car, "Honda RBPT",
                  "Racing_Bulls_VCARB-02_6_Hadjar", "Racing_Bulls_VCARB-02_30_Lawson"),
                T("Alpine", "ALP", "#FF87BC", F1Car, "Renault",
                  "A525_10_Gasly", "A525_43_Colapinto"),
                T("Haas", "HAA", "#B6BABD", F1Car, "Ferrari",
                  "Haas_VF-25_31_Oco", "Haas_VF-25_87_Bea"),
                T("Kick Sauber", "SAU", "#00E700", F1Car, "Ferrari",
                  "Sauber_C45_27_Hulkenberg", "Sauber_C45_5_Bortoleto"),
                T("Cadillac", "CAD", "#C6A76D", F1Car, "Ferrari",
                  "CR25_11_Perez", "CR25_77_Bottas")
            };
        }

        private static Driver D(string first, string last, string abbr, int number, string nationality,
                                string teamId, int skill, int aggression)
        {
            return new Driver
            {
                FirstName = first,
                LastName = last,
                Abbreviation = abbr,
                Number = number,
                Nationality = nationality,
                TeamId = teamId,
                AiSkill = skill,
                AiAggression = aggression,
                AcName = first + " " + last
            };
        }

        /// <summary>Twenty drivers matched to the teams above.</summary>
        public static List<Driver> Drivers(List<Team> teams)
        {
            string Id(string shortName) => teams.First(t => t.ShortName == shortName).Id;

            return new List<Driver>
            {
                D("Max", "Verstappen", "VER", 1, "Paesi Bassi", Id("RBR"), 99, 60),
                D("Yuki", "Tsunoda", "TSU", 22, "Giappone", Id("RBR"), 93, 65),

                D("Charles", "Leclerc", "LEC", 16, "Monaco", Id("FER"), 97, 55),
                D("Lewis", "Hamilton", "HAM", 44, "Gran Bretagna", Id("FER"), 97, 50),

                D("Lando", "Norris", "NOR", 4, "Gran Bretagna", Id("MCL"), 98, 52),
                D("Oscar", "Piastri", "PIA", 81, "Australia", Id("MCL"), 97, 50),

                D("George", "Russell", "RUS", 63, "Gran Bretagna", Id("MER"), 96, 52),
                D("Andrea Kimi", "Antonelli", "ANT", 12, "Italia", Id("MER"), 92, 58),

                D("Alexander", "Albon", "ALB", 23, "Thailandia", Id("WIL"), 93, 48),
                D("Carlos", "Sainz", "SAI", 55, "Spagna", Id("WIL"), 95, 52),

                D("Isack", "Hadjar", "HAD", 6, "Francia", Id("RB"), 91, 55),
                D("Liam", "Lawson", "LAW", 30, "Nuova Zelanda", Id("RB"), 91, 58),

                D("Pierre", "Gasly", "GAS", 10, "Francia", Id("ALP"), 92, 54),
                D("Franco", "Colapinto", "COL", 43, "Argentina", Id("ALP"), 88, 60),

                D("Esteban", "Ocon", "OCO", 31, "Francia", Id("HAA"), 91, 55),
                D("Oliver", "Bearman", "BEA", 87, "Gran Bretagna", Id("HAA"), 89, 57),

                D("Nico", "Hulkenberg", "HUL", 27, "Germania", Id("SAU"), 91, 50),
                D("Gabriel", "Bortoleto", "BOR", 5, "Brasile", Id("SAU"), 87, 55),

                D("Sergio", "Perez", "PER", 11, "Messico", Id("CAD"), 92, 52),
                D("Valtteri", "Bottas", "BOT", 77, "Finlandia", Id("CAD"), 91, 48)
            };
        }

        /// <summary>
        /// A complete championship, ready to race: the full grid, the whole calendar and the
        /// modern points system. The first driver is marked as the player, which the manager
        /// lets you change with one click.
        /// </summary>
        public static Championship DefaultChampionship(string name = null)
        {
            var teams = Teams();
            var drivers = Drivers(teams);
            var circuits = Circuits();

            var championship = new Championship
            {
                Name = string.IsNullOrWhiteSpace(name) ? "Campionato Pitwall" : name,
                SeasonYear = DateTime.Today.Year,
                Description = "Campionato generato da Pitwall con il calendario completo.",
                Teams = teams,
                Drivers = drivers,
                Circuits = circuits,
                Rules = new ChampionshipRules()
            };

            if (drivers.Count > 0) drivers[0].IsPlayer = true;

            // Three sprint weekends, roughly where the real calendar puts them.
            var sprintRounds = new HashSet<int> { 2, 13, 19 };
            var rnd = new Random(championship.SeasonYear);
            var raceDate = new DateTime(championship.SeasonYear, 3, 8);

            for (int i = 0; i < circuits.Count; i++)
            {
                var circuit = circuits[i];
                var format = sprintRounds.Contains(i + 1) ? WeekendFormat.Sprint : WeekendFormat.Standard;
                var round = Round.Create("GP " + circuit.Country + " - " + circuit.Name, circuit.Id, format, i + 1);
                round.Date = raceDate;
                round.Seed = rnd.Next();
                WeatherGenerator.GenerateWeekend(circuit, round, championship.Rules.Weather);
                championship.Calendar.Add(round);
                raceDate = raceDate.AddDays(14);
            }

            return championship;
        }
    }
}
