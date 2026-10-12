using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Pitwall.Model;
using Pitwall.Rules;
using Pitwall.Storage;

namespace Pitwall.Manager
{
    /// <summary>
    /// Il configuratore di un campionato nuovo.
    ///
    /// Prima c'erano due pulsanti: "griglia completa", che creava venti piloti e un calendario
    /// senza chiedere niente, e "vuoto", che lasciava davanti a tabelle da riempire a mano. Ne
    /// mancava uno in mezzo, che e quello che serve davvero: quante vetture, quali scuderie,
    /// chi sei tu e come si contano i punti.
    ///
    /// Quattro passi, nell'ordine in cui le domande vengono in mente.
    /// </summary>
    public partial class NewChampionshipWindow : Window
    {
        /// <summary>Una scuderia nell'elenco del secondo passo, con la spunta.</summary>
        public sealed class TeamRow : INotifyPropertyChanged
        {
            private bool _selected;

            public Team Source { get; set; }
            public string Name => Source.Name;
            public string ShortName => Source.ShortName;
            public string PowerUnit => Source.PowerUnit;
            public string DriverNames { get; set; } = "";

            public bool Selected
            {
                get => _selected;
                set
                {
                    if (_selected == value) return;
                    _selected = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Selected)));
                    SelectionChanged?.Invoke();
                }
            }

            public event PropertyChangedEventHandler PropertyChanged;

            /// <summary>Avvisa la finestra che una spunta e cambiata, per riaggiornare i conti.</summary>
            public Action SelectionChanged;
        }

        /// <summary>Un pilota nell'elenco del terzo passo.</summary>
        public sealed class DriverRow
        {
            public Driver Source { get; set; }
            public string TeamShortName { get; set; } = "";
            public string TeamName { get; set; } = "";

            public int Number { get => Source.Number; set => Source.Number = value; }
            public string FirstName { get => Source.FirstName; set => Source.FirstName = value; }
            public string LastName { get => Source.LastName; set => Source.LastName = value; }
            public string Abbreviation { get => Source.Abbreviation; set => Source.Abbreviation = value; }
            public string Nationality { get => Source.Nationality; set => Source.Nationality = value; }
            public int AiSkill { get => Source.AiSkill; set => Source.AiSkill = value; }
            public int AiAggression { get => Source.AiAggression; set => Source.AiAggression = value; }

            public string Label => Source.FirstName + " " + Source.LastName + "  (" + TeamName + ")";
        }

        /// <summary>Una gara nell elenco del calendario.</summary>
        public sealed class RoundRow : INotifyPropertyChanged
        {
            private bool _selected = true;
            private bool _sprint;

            public Circuit Source { get; set; }
            public string Name => Source.Name;
            public string Country => Source.Country;

            public bool Selected
            {
                get => _selected;
                set { _selected = value; Changed(nameof(Selected)); Touched?.Invoke(); }
            }

            public bool Sprint
            {
                get => _sprint;
                set { _sprint = value; Changed(nameof(Sprint)); Touched?.Invoke(); }
            }

            /// <summary>Giri scritti a mano. Vuoto vuol dire: usa la percentuale.</summary>
            public int Laps { get; set; }

            public string LapsText
            {
                get => Laps > 0 ? Laps.ToString() : "";
                set
                {
                    int n;
                    Laps = int.TryParse(value, out n) && n > 0 ? n : 0;
                    Changed(nameof(LapsText));
                }
            }

            public event PropertyChangedEventHandler PropertyChanged;
            private void Changed(string nome) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nome));

            public Action Touched;
        }

        /// <summary>Un sistema di punteggio fra cui scegliere.</summary>
        private sealed class PointsOption
        {
            public string Name { get; set; } = "";
            public List<int> Race { get; set; } = new List<int>();
            public List<int> Sprint { get; set; } = new List<int>();
            public override string ToString() => Name;
        }

        private readonly ObservableCollection<TeamRow> _teams = new ObservableCollection<TeamRow>();
        private readonly ObservableCollection<DriverRow> _drivers = new ObservableCollection<DriverRow>();
        private readonly ObservableCollection<RoundRow> _rounds = new ObservableCollection<RoundRow>();
        private readonly List<Team> _allTeams;
        private readonly List<Driver> _allDrivers;
        private bool _loading = true;

        /// <summary>Il campionato costruito, valorizzato solo quando si preme Crea.</summary>
        public Championship Result { get; private set; }

        public NewChampionshipWindow()
        {
            InitializeComponent();

            _allTeams = SeedData.Teams();
            _allDrivers = SeedData.Drivers(_allTeams);

            FieldName.Text = "Campionato " + DateTime.Today.Year;
            FieldYear.Text = DateTime.Today.Year.ToString();
            FieldDescription.Text = "";

            BuildTeamRows();
            BuildGridSizes();
            BuildPointsOptions();
            BuildCalendarOptions();

            TeamGrid.ItemsSource = _teams;
            DriverGrid.ItemsSource = _drivers;
            RoundGrid.ItemsSource = _rounds;

            _loading = false;

            ApplyGridSize(20);
            RefreshStep();
        }

        // ---- Costruzione degli elenchi ------------------------------------------------------

        private void BuildTeamRows()
        {
            foreach (var team in _allTeams)
            {
                var piloti = _allDrivers.Where(d => d.TeamId == team.Id)
                                        .Select(d => d.FirstName + " " + d.LastName);

                var row = new TeamRow { Source = team, DriverNames = string.Join(", ", piloti) };
                row.SelectionChanged = OnTeamToggled;
                _teams.Add(row);
            }
        }

        private void BuildGridSizes()
        {
            // Solo numeri pari: due piloti per scuderia.
            for (int vetture = 4; vetture <= _allTeams.Count * 2; vetture += 2)
                FieldGridSize.Items.Add(vetture + " vetture");

            FieldGridSize.SelectedItem = "20 vetture";
        }

        private void BuildPointsOptions()
        {
            FieldPoints.Items.Add(new PointsOption
            {
                Name = "Formula 1 moderno (25-18-15...)",
                Race = new List<int> { 25, 18, 15, 12, 10, 8, 6, 4, 2, 1 },
                Sprint = new List<int> { 8, 7, 6, 5, 4, 3, 2, 1 }
            });
            FieldPoints.Items.Add(new PointsOption
            {
                Name = "Formula 1 2003-2009 (10-8-6...)",
                Race = new List<int> { 10, 8, 6, 5, 4, 3, 2, 1 },
                Sprint = new List<int>()
            });
            FieldPoints.Items.Add(new PointsOption
            {
                Name = "Formula 1 1991-2002 (10-6-4...)",
                Race = new List<int> { 10, 6, 4, 3, 2, 1 },
                Sprint = new List<int>()
            });
            FieldPoints.Items.Add(new PointsOption
            {
                Name = "Tutti a punti (20-19-18...)",
                Race = Enumerable.Range(0, 20).Select(i => 20 - i).ToList(),
                Sprint = new List<int>()
            });

            FieldPoints.SelectedIndex = 0;
        }

        private void BuildCalendarOptions()
        {
            foreach (var n in new[] { "Nessuna", "1 prova libera", "2 prove libere", "3 prove libere" })
                FieldPracticeCount.Items.Add(n);
            FieldPracticeCount.SelectedIndex = 3;

            foreach (var p in new[] { "100%", "75%", "50%", "35%", "25%" })
                FieldRaceDistance.Items.Add(p);
            FieldRaceDistance.SelectedIndex = 0;

            // Le sprint dove le mette la Formula 1: tre nella stagione.
            var sprint = new HashSet<int> { 2, 13, 19 };
            var circuiti = SeedData.Circuits();

            for (int i = 0; i < circuiti.Count; i++)
            {
                var row = new RoundRow { Source = circuiti[i], Sprint = sprint.Contains(i + 1) };
                row.Touched = RefreshCounts;
                _rounds.Add(row);
            }
        }

        private void OnCalendarChanged(object sender, SelectionChangedEventArgs e) => RefreshCounts();

        private void OnAllRounds(object sender, RoutedEventArgs e)
        {
            foreach (var r in _rounds) r.Selected = true;
            RefreshCounts();
        }

        private void OnNoRounds(object sender, RoutedEventArgs e)
        {
            foreach (var r in _rounds) r.Selected = false;
            RefreshCounts();
        }

        /// <summary>Quante prove libere sono state scelte, da zero a tre.</summary>
        private int PracticeCount => Math.Max(0, FieldPracticeCount.SelectedIndex);

        /// <summary>La percentuale di distanza scelta per la gara.</summary>
        private int RaceDistance
        {
            get
            {
                var testo = FieldRaceDistance.SelectedItem as string;
                int n;
                return testo != null && int.TryParse(testo.TrimEnd('%'), out n) ? n : 100;
            }
        }

        // ---- Il numero di vetture -----------------------------------------------------------

        private void OnGridSizeChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loading || FieldGridSize.SelectedItem == null) return;

            string testo = FieldGridSize.SelectedItem.ToString();
            int vetture = int.Parse(testo.Split(' ')[0]);
            ApplyGridSize(vetture);
        }

        /// <summary>Spunta le prime scuderie dell'elenco fino ad arrivare al numero di vetture.</summary>
        private void ApplyGridSize(int vetture)
        {
            _loading = true;
            int quante = Math.Max(1, vetture / 2);
            for (int i = 0; i < _teams.Count; i++) _teams[i].Selected = i < quante;
            _loading = false;

            RebuildDrivers();
        }

        private void OnTeamToggled()
        {
            if (_loading) return;
            RebuildDrivers();
        }

        /// <summary>Rifa l'elenco dei piloti partendo dalle scuderie spuntate.</summary>
        private void RebuildDrivers()
        {
            // Si tiene da parte chi era al volante, cosi una spunta cambiata non lo perde.
            var scelto = (FieldSeat.SelectedItem as DriverRow)?.Source?.Id;

            _drivers.Clear();
            foreach (var team in _teams.Where(t => t.Selected))
            {
                foreach (var driver in _allDrivers.Where(d => d.TeamId == team.Source.Id))
                {
                    _drivers.Add(new DriverRow
                    {
                        Source = driver,
                        TeamShortName = team.ShortName,
                        TeamName = team.Name
                    });
                }
            }

            FieldSeat.ItemsSource = null;
            FieldSeat.ItemsSource = _drivers;

            var ritrovato = _drivers.FirstOrDefault(d => d.Source.Id == scelto);
            FieldSeat.SelectedItem = ritrovato ?? _drivers.FirstOrDefault();

            RefreshCounts();
        }

        private void OnSeatChanged(object sender, SelectionChangedEventArgs e) => RefreshCounts();

        private void OnPointsChanged(object sender, SelectionChangedEventArgs e)
        {
            var scelta = FieldPoints.SelectedItem as PointsOption;
            if (scelta == null) return;

            PointsPreview.Text = "Primi posti: " + string.Join("  ", scelta.Race.Take(10)) +
                                 (scelta.Sprint.Count > 0
                                      ? "\nSprint: " + string.Join("  ", scelta.Sprint)
                                      : "\nNessun punto nelle sprint.");
            RefreshCounts();
        }

        // ---- Conti e riepiloghi -------------------------------------------------------------

        private void RefreshCounts()
        {
            int vetture = _drivers.Count;
            int scuderie = _teams.Count(t => t.Selected);

            GridCount.Text = vetture + " vetture, " + scuderie + " scuderie";

            int gare = _rounds.Count(r => r.Selected);
            int sprint = _rounds.Count(r => r.Selected && r.Sprint);
            if (RoundCount != null)
                RoundCount.Text = gare + " gare in calendario, di cui " + sprint + " con la sprint";

            Summary.Text = vetture + " vetture  ·  " + scuderie + " scuderie  ·  " + gare + " gare";

            var seat = FieldSeat.SelectedItem as DriverRow;
            var punti = FieldPoints.SelectedItem as PointsOption;

            FinalSummary.Text =
                "Campionato: " + FieldName.Text + "   (stagione " + FieldYear.Text + ")\n" +
                "Griglia: " + vetture + " vetture, " + scuderie + " scuderie\n" +
                "Al volante: " + (seat != null ? seat.Label : "nessuno scelto") + "\n" +
                "Punti: " + (punti != null ? punti.Name : "-") + "\n" +
                "Calendario: " + (FieldCalendar.IsChecked != true
                                      ? "vuoto, da comporre a mano"
                                      : gare + " gare, " + sprint + " con la sprint") + "\n" +
                "Sessioni: " + (PracticeCount == 0 ? "niente prove" : PracticeCount + " prove da " + FieldPracticeMinutes.Text + "'") +
                ", qualifica " + FieldQualiMinutes.Text + "'" +
                (FieldKnockout.IsChecked == true ? " a eliminazione" : " unica") +
                ", gara al " + RaceDistance + "% della distanza";
        }

        // ---- Navigazione --------------------------------------------------------------------

        private void OnBack(object sender, RoutedEventArgs e)
        {
            if (Steps.SelectedIndex > 0) Steps.SelectedIndex--;
            RefreshStep();
        }

        private void OnNext(object sender, RoutedEventArgs e)
        {
            if (Steps.SelectedIndex < Steps.Items.Count - 1)
            {
                Steps.SelectedIndex++;
                RefreshStep();
                return;
            }

            Create();
        }

        private void OnCancel(object sender, RoutedEventArgs e) { DialogResult = false; Close(); }

        /// <summary>Titolo, spiegazione e pulsanti del passo in cui siamo.</summary>
        private void RefreshStep()
        {
            bool ultimo = Steps.SelectedIndex == Steps.Items.Count - 1;

            switch (Steps.SelectedIndex)
            {
                case 0:
                    StepTitle.Text = "Come si chiama";
                    StepNote.Text = "Nome e stagione del campionato. Il calendario completo si puo " +
                                    "generare adesso e cambiare dopo.";
                    break;
                case 1:
                    StepTitle.Text = "Chi corre";
                    StepNote.Text = "Scegli quante vetture schierare e quali scuderie. Due piloti " +
                                    "per scuderia, come in Formula 1.";
                    break;
                case 2:
                    StepTitle.Text = "Chi sei tu";
                    StepNote.Text = "Prendi il posto di un pilota della griglia. Nomi, numeri e " +
                                    "livello delle IA si possono correggere qui, uno per uno.";
                    break;
                case 3:
                    StepTitle.Text = "Quante gare e come sono fatte";
                    StepNote.Text = "Scegli i Gran Premi, quanto durano prove e qualifica, quanto " +
                                    "e lunga la gara e dove mettere le sprint.";
                    break;
                default:
                    StepTitle.Text = "Come si contano i punti";
                    StepNote.Text = "Un sistema di punteggio fra quelli storici, piu gli eventuali " +
                                    "bonus. Si cambia anche dopo, dalla scheda Regolamento.";
                    break;
            }

            BackButton.IsEnabled = Steps.SelectedIndex > 0;
            NextButton.Content = ultimo ? "Crea" : "Avanti";
            RefreshCounts();
        }

        // ---- La creazione vera e propria ----------------------------------------------------

        private void Create()
        {
            if (_drivers.Count < 2)
            {
                MessageBox.Show(this, "Serve almeno una scuderia in gara.", "Nuovo campionato",
                                MessageBoxButton.OK, MessageBoxImage.Warning);
                Steps.SelectedIndex = 1;
                RefreshStep();
                return;
            }

            int anno;
            if (!int.TryParse(FieldYear.Text, out anno)) anno = DateTime.Today.Year;

            var punti = FieldPoints.SelectedItem as PointsOption;
            var seat = FieldSeat.SelectedItem as DriverRow;

            int valore;
            var opzioni = new ChampionshipBuilder.Options
            {
                Name = FieldName.Text,
                SeasonYear = anno,
                Description = FieldDescription.Text ?? "",
                Teams = _teams.Where(t => t.Selected).Select(t => t.Source).ToList(),
                Drivers = _drivers.Select(d => d.Source).ToList(),
                PlayerDriverId = seat != null ? seat.Source.Id : "",
                WithCalendar = FieldCalendar.IsChecked == true,

                Rounds = _rounds.Where(r => r.Selected)
                                .Select(r => new ChampionshipBuilder.RoundChoice
                                {
                                    Circuit = r.Source,
                                    Sprint = r.Sprint,
                                    Laps = r.Laps
                                }).ToList(),

                PracticeSessions = PracticeCount,
                KnockoutQualifying = FieldKnockout.IsChecked == true,
                RaceDistancePercent = RaceDistance
            };

            opzioni.PracticeMinutes = int.TryParse(FieldPracticeMinutes.Text, out valore) ? valore : 60;
            opzioni.QualifyingMinutes = int.TryParse(FieldQualiMinutes.Text, out valore) ? valore : 45;

            if (punti != null)
            {
                opzioni.PointsName = punti.Name;
                opzioni.PointsRace = new List<int>(punti.Race);
                opzioni.PointsSprint = new List<int>(punti.Sprint);
            }

            opzioni.FastestLap = int.TryParse(FieldFastestLap.Text, out valore) ? valore : 0;
            opzioni.FastestLapMaxPosition = int.TryParse(FieldFastestLapMax.Text, out valore) ? valore : 10;
            opzioni.PolePosition = int.TryParse(FieldPole.Text, out valore) ? valore : 0;

            if (opzioni.WithCalendar && opzioni.Rounds.Count == 0)
            {
                MessageBox.Show(this, "Nessuna gara in calendario: scegline almeno una.",
                                "Nuovo campionato", MessageBoxButton.OK, MessageBoxImage.Warning);
                Steps.SelectedIndex = 3;
                RefreshStep();
                return;
            }

            Result = ChampionshipBuilder.Build(opzioni);
            DialogResult = true;
            Close();
        }
    }
}
