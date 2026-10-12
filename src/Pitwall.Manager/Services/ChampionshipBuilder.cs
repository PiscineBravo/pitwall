using System;
using System.Collections.Generic;
using System.Linq;
using Pitwall.Model;
using Pitwall.Rules;
using Pitwall.Storage;

namespace Pitwall.Manager
{
    /// <summary>
    /// Costruisce un campionato a partire dalle scelte del configuratore.
    ///
    /// Sta qui e non dentro la finestra per una ragione pratica: cosi lo stesso codice che gira
    /// quando premi "Crea" si puo chiamare anche da riga di comando per provarlo. Un collaudo che
    /// passa da un'altra strada non prova quello che si voleva provare.
    /// </summary>
    public static class ChampionshipBuilder
    {
        /// <summary>Le scelte fatte nel configuratore, raccolte in un posto solo.</summary>
        public sealed class Options
        {
            public string Name { get; set; } = "Nuovo campionato";
            public int SeasonYear { get; set; } = DateTime.Today.Year;
            public string Description { get; set; } = "";

            public List<Team> Teams { get; set; } = new List<Team>();
            public List<Driver> Drivers { get; set; } = new List<Driver>();

            /// <summary>Il pilota di cui prende il posto chi gioca.</summary>
            public string PlayerDriverId { get; set; } = "";

            public string PointsName { get; set; } = "Formula 1 moderno";
            public List<int> PointsRace { get; set; } = new List<int> { 25, 18, 15, 12, 10, 8, 6, 4, 2, 1 };
            public List<int> PointsSprint { get; set; } = new List<int> { 8, 7, 6, 5, 4, 3, 2, 1 };

            public int FastestLap { get; set; }
            public int FastestLapMaxPosition { get; set; } = 10;
            public int PolePosition { get; set; }

            public bool WithCalendar { get; set; } = true;

            /// <summary>Le gare scelte, in ordine di calendario.</summary>
            public List<RoundChoice> Rounds { get; set; } = new List<RoundChoice>();

            /// <summary>Quante prove libere per fine settimana, da zero a tre.</summary>
            public int PracticeSessions { get; set; } = 3;

            /// <summary>Quanto dura ogni prova libera, in minuti.</summary>
            public int PracticeMinutes { get; set; } = 60;

            /// <summary>Quanto dura la qualifica, in minuti.</summary>
            public int QualifyingMinutes { get; set; } = 45;

            /// <summary>Qualifica a eliminazione Q1, Q2, Q3 invece che a sessione unica.</summary>
            public bool KnockoutQualifying { get; set; } = true;

            /// <summary>Lunghezza della gara come percentuale della distanza vera.</summary>
            public int RaceDistancePercent { get; set; } = 100;
        }

        /// <summary>Una gara del calendario, come la si sceglie nel configuratore.</summary>
        public sealed class RoundChoice
        {
            public Circuit Circuit { get; set; }
            public bool Sprint { get; set; }

            /// <summary>Giri della gara. Zero vuol dire: calcolali dalla percentuale.</summary>
            public int Laps { get; set; }
        }

        public static Championship Build(Options options)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));

            int anno = options.SeasonYear;
            if (anno < 1950 || anno > 2100) anno = DateTime.Today.Year;

            // Al volante ci va uno solo, e la bandierina va messa prima di salvare.
            foreach (var driver in options.Drivers)
                driver.IsPlayer = driver.Id == options.PlayerDriverId;

            var championship = new Championship
            {
                Name = string.IsNullOrWhiteSpace(options.Name) ? "Nuovo campionato" : options.Name.Trim(),
                SeasonYear = anno,
                Description = options.Description ?? "",
                Teams = options.Teams,
                Drivers = options.Drivers,
                Circuits = options.Rounds.Where(r => r.Circuit != null).Select(r => r.Circuit).ToList(),
                Rules = new ChampionshipRules()
            };

            var punti = championship.Rules.Points;
            punti.Name = options.PointsName;
            punti.Race = new List<int>(options.PointsRace);
            punti.Sprint = new List<int>(options.PointsSprint);
            punti.FastestLap = Math.Max(0, options.FastestLap);
            punti.FastestLapMaxPosition = Math.Max(1, options.FastestLapMaxPosition);
            punti.PolePosition = Math.Max(0, options.PolePosition);

            if (options.WithCalendar) BuildCalendar(championship, options);

            return championship;
        }

        /// <summary>
        /// Il calendario: una gara per circuito, con il meteo gia generato.
        ///
        /// Si usa lo stesso percorso del campionato predefinito - Round.Create e il generatore
        /// del meteo - invece di rifarlo a mano: cosi un campionato creato dal configuratore e
        /// uno creato dai dati di partenza hanno davvero lo stesso calendario, e non due
        /// calendari che si somigliano.
        /// </summary>
        /// <summary>
        /// Il calendario, con le gare e le sessioni scelte nel configuratore.
        /// </summary>
        private static void BuildCalendar(Championship championship, Options options)
        {
            var rnd = new Random(championship.SeasonYear);
            var data = new DateTime(championship.SeasonYear, 3, 8);
            int ordine = 0;

            foreach (var scelta in options.Rounds)
            {
                if (scelta.Circuit == null) continue;
                ordine++;

                var formato = scelta.Sprint ? WeekendFormat.Sprint : WeekendFormat.Standard;
                var round = Round.Create("GP " + scelta.Circuit.Country + " - " + scelta.Circuit.Name,
                                         scelta.Circuit.Id, formato, ordine);
                round.Date = data;
                round.Seed = rnd.Next();
                round.Sessions = BuildSessions(options, scelta);

                Pitwall.Weather.WeatherGenerator.GenerateWeekend(scelta.Circuit, round, championship.Rules.Weather);
                championship.Calendar.Add(round);
                data = data.AddDays(14);
            }
        }

        /// <summary>
        /// Le sessioni di un fine settimana, costruite sulle scelte invece che sui valori fissi.
        ///
        /// Il fine settimana sprint e diverso da quello normale e non per capriccio: ha una sola
        /// prova libera, una qualifica in piu per la sprint, e la sprint stessa. E il formato che
        /// usa la Formula 1, e il motivo per cui le due liste si costruiscono separate.
        /// </summary>
        private static List<SessionConfig> BuildSessions(Options options, RoundChoice scelta)
        {
            var sessioni = new List<SessionConfig>();

            int prove = Math.Max(0, Math.Min(3, options.PracticeSessions));
            if (scelta.Sprint) prove = Math.Min(prove, 1);   // nei weekend sprint ce n e una sola

            var tipi = new[] { SessionKind.Practice1, SessionKind.Practice2, SessionKind.Practice3 };
            var orari = new[] { new TimeSpan(13, 30, 0), new TimeSpan(17, 0, 0), new TimeSpan(12, 30, 0) };

            for (int i = 0; i < prove; i++)
            {
                sessioni.Add(new SessionConfig
                {
                    Kind = tipi[i],
                    LengthMode = SessionLengthMode.Minutes,
                    Length = Math.Max(5, options.PracticeMinutes),
                    StartTimeOfDay = orari[i]
                });
            }

            if (scelta.Sprint)
            {
                sessioni.Add(new SessionConfig
                {
                    Kind = SessionKind.SprintQualifying,
                    LengthMode = SessionLengthMode.Minutes,
                    Length = 44,
                    StartTimeOfDay = new TimeSpan(17, 30, 0),
                    QualifyingFormat = options.KnockoutQualifying ? QualifyingFormat.Knockout : QualifyingFormat.Single,
                    KnockoutSegmentMinutes = new List<int> { 12, 10, 8 }
                });

                sessioni.Add(new SessionConfig
                {
                    Kind = SessionKind.Sprint,
                    LengthMode = SessionLengthMode.DistancePercent,
                    Length = 33,
                    StartTimeOfDay = new TimeSpan(12, 0, 0),
                    MandatoryPitStops = 0,
                    TwoCompoundRule = false
                });
            }

            int minutiQ = Math.Max(5, options.QualifyingMinutes);
            sessioni.Add(new SessionConfig
            {
                Kind = SessionKind.Qualifying,
                LengthMode = SessionLengthMode.Minutes,
                Length = minutiQ,
                StartTimeOfDay = new TimeSpan(16, 0, 0),
                QualifyingFormat = options.KnockoutQualifying ? QualifyingFormat.Knockout : QualifyingFormat.Single,

                // Le tre manche si dividono i minuti scelti, come fa la Formula 1: la prima piu
                // lunga, l ultima piu corta.
                KnockoutSegmentMinutes = new List<int>
                {
                    Math.Max(3, (int)Math.Round(minutiQ * 0.40)),
                    Math.Max(3, (int)Math.Round(minutiQ * 0.33)),
                    Math.Max(3, (int)Math.Round(minutiQ * 0.27))
                }
            });

            // La gara: giri fissi se sono stati scelti per questo circuito, altrimenti una
            // percentuale della distanza vera.
            var gara = new SessionConfig
            {
                Kind = SessionKind.Race,
                StartTimeOfDay = new TimeSpan(15, 0, 0),
                MandatoryPitStops = 1
            };

            if (scelta.Laps > 0)
            {
                gara.LengthMode = SessionLengthMode.Laps;
                gara.Length = scelta.Laps;
            }
            else
            {
                gara.LengthMode = SessionLengthMode.DistancePercent;
                gara.Length = Math.Max(5, Math.Min(100, options.RaceDistancePercent));
            }

            sessioni.Add(gara);
            return sessioni;
        }
        /// <summary>
        /// Un campionato con le prime <paramref name="vetture"/> auto della griglia di partenza.
        ///
        /// Serve ai collaudi da riga di comando: fa le stesse scelte che farebbe qualcuno che apre
        /// il configuratore, sceglie quante vetture e preme Crea senza toccare altro.
        /// </summary>
        public static Championship Sample(string name, int vetture)
        {
            var tutteLeScuderie = SeedData.Teams();
            var tuttiIPiloti = SeedData.Drivers(tutteLeScuderie);

            int quante = Math.Max(1, Math.Min(tutteLeScuderie.Count, vetture / 2));
            var scuderie = tutteLeScuderie.Take(quante).ToList();
            var ammessi = new HashSet<string>(scuderie.Select(t => t.Id));
            var piloti = tuttiIPiloti.Where(d => ammessi.Contains(d.TeamId)).ToList();

            // Tutti i circuiti in calendario, con le sprint dove le mette la Formula 1.
            var sprint = new HashSet<int> { 2, 13, 19 };
            var circuiti = SeedData.Circuits();
            var gare = new List<RoundChoice>();
            for (int i = 0; i < circuiti.Count; i++)
                gare.Add(new RoundChoice { Circuit = circuiti[i], Sprint = sprint.Contains(i + 1) });

            return Build(new Options
            {
                Name = name,
                Description = "Campionato di prova creato dal configuratore.",
                Teams = scuderie,
                Drivers = piloti,
                PlayerDriverId = piloti.Count > 0 ? piloti[0].Id : "",
                Rounds = gare
            });
        }
    }
}
