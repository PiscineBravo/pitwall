using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Pitwall.ContentManager;
using Pitwall.Live;
using Pitwall.Manager.Services;
using Pitwall.Model;
using Pitwall.Results;
using Pitwall.Rules;
using Pitwall.Scoring;
using Pitwall.Lingue;
using Pitwall.Storage;
using Pitwall.Weather;

namespace Pitwall.Manager
{
    /// <summary>
    /// The whole manager in one window. Everything edits the in-memory championship directly and
    /// is written back to disk on save, which keeps the code small enough to follow and means
    /// the file on disk is always a complete, readable document.
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly DispatcherTimer _liveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        private Championship _championship;
        private List<ChampionshipSummary> _summaries = new List<ChampionshipSummary>();
        private List<AcTrack> _tracks = new List<AcTrack>();
        private string _acRoot;
        private bool _loading;

        public MainWindow()
        {
            InitializeComponent();
            Loaded += OnLoaded;
            Closing += (s, e) => Save(silent: true);
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            PitwallPaths.EnsureFolders();

            // La lingua si legge prima di disegnare qualunque cosa.
            Pitwall.Lingue.Testi.Applica(Impostazioni.Correnti);

            // ...e le scritte gia disegnate vanno rilette.
            //
            // InitializeComponent() gira nel costruttore e risolve li tutte le traduzioni dell interfaccia, quindi
            // quando arriviamo qui l interfaccia esiste gia nella lingua di partenza. Applica cambia
            // il vocabolario ma non avvisa nessuno: senza questa riga l impostazione salvata non
            // sopravvive alla chiusura del programma, e funziona solo finche la si cambia a mano dal
            // menu - che e esattamente come sembrava che non funzionasse affatto.
            Traduzioni.Corrente.Aggiorna();
            BindSettings();

            RoundFormat.ItemsSource = Enum.GetValues(typeof(WeekendFormat));
            LengthModeColumn.ItemsSource = Enum.GetValues(typeof(SessionLengthMode));
            PointsPreset.ItemsSource = PointsScheme.Presets().ToList();
            PointsPreset.DisplayMemberPath = "Name";

            DriverTeamColumn.SelectedValuePath = "Id";
            DriverTeamColumn.DisplayMemberPath = "Name";

            _acRoot = AcContentScanner.FindInstallation();
            AcPath.Text = _acRoot ?? Testi.T("msg.ac.non.trovato");
            if (_acRoot != null) TeamCarColumn.ItemsSource = AcContentScanner.Cars(_acRoot);

            ReloadChampionshipList();
            if (_summaries.Count == 0)
            {
                _championship = SeedData.DefaultChampionship();
                ChampionshipStore.Save(_championship);
                ReloadChampionshipList();
                Status(Testi.T("msg.campionato.iniziale"));
            }

            ChampionshipList.SelectedIndex = 0;

            RefreshCmTemplates(false);

            _liveTimer.Tick += OnLiveTick;
            _liveTimer.Start();
        }

        private void Status(string text) => StatusText.Text = text;

        // ================= Championship =================

        private void ReloadChampionshipList()
        {
            _summaries = ChampionshipStore.List();
            ChampionshipList.ItemsSource = _summaries;

            // Le gare rimaste in Content Manager senza il loro campionato se ne vanno qui.
            //
            // L'eliminazione le toglie gia, ma se qualcosa va storto - Content Manager aperto
            // che tiene la cartella occupata, un errore di scrittura - restavano li per
            // sempre: un campionato visibile nel gioco e sparito dal Manager, e nessuno che
            // capisse da dove arrivava. Questo controllo rimette i conti a posto da solo.
            try { ContentManagerExporter.RemoveOrphans(_summaries.Select(s => s.Name)); }
            catch (Exception) { }

            UpdateTabsEnabled();
        }

        /// <summary>
        /// Accende le schede del campionato solo quando un campionato c'e davvero.
        ///
        /// Senza questo, con nessun campionato aperto, si potevano aprire Team, Piloti,
        /// Circuiti e tutto il resto: tabelle vuote che sembravano un programma rotto invece
        /// di un programma che aspetta. La prima cosa da fare e creare un campionato, e
        /// l'interfaccia deve dirlo da sola.
        /// </summary>
        /// <summary>Riempie la scheda Impostazioni e mostra la lingua in uso adesso.</summary>
        private void BindSettings()
        {
            _loadingSettings = true;

            LanguageChoice.Items.Clear();
            LanguageChoice.Items.Add("Come Windows");
            LanguageChoice.Items.Add("Italiano");
            LanguageChoice.Items.Add("English");

            var impostazioni = Impostazioni.Correnti;
            LanguageChoice.SelectedIndex = impostazioni.Lingua == Lingua.Italiano ? 1
                                         : impostazioni.Lingua == Lingua.Inglese ? 2 : 0;

            LanguageNow.Text = "In uso adesso: " +
                (impostazioni.LinguaEffettiva == Lingua.Inglese ? "inglese" : "italiano");

            DataFolderPath.Text = PitwallPaths.Root;
            _loadingSettings = false;
        }

        private void OnLanguageChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loadingSettings) return;

            var impostazioni = Impostazioni.Correnti;
            impostazioni.Lingua = LanguageChoice.SelectedIndex == 1 ? Lingua.Italiano
                                : LanguageChoice.SelectedIndex == 2 ? Lingua.Inglese
                                : Lingua.Automatica;
            impostazioni.Salva();
            Pitwall.Lingue.Testi.Applica(impostazioni);
            Traduzioni.Corrente.Aggiorna();

            LanguageNow.Text = "In uso adesso: " +
                (impostazioni.LinguaEffettiva == Lingua.Inglese ? "inglese" : "italiano");

            Status(impostazioni.LinguaEffettiva == Lingua.Inglese
                ? "Language set to English."
                : "Lingua impostata su italiano.");
        }

        private bool _loadingSettings;

        private void UpdateTabsEnabled()
        {
            bool aperto = _championship != null;

            TabTeam.IsEnabled = aperto;
            TabPiloti.IsEnabled = aperto;
            TabCircuiti.IsEnabled = aperto;
            TabCalendario.IsEnabled = aperto;
            TabRegolamento.IsEnabled = aperto;
            TabClassifiche.IsEnabled = aperto;
            TabLive.IsEnabled = aperto;

            // Se si era su una scheda che ora e spenta si torna ai campionati, invece di
            // restare davanti a una pagina che non risponde piu.
            if (!aperto) Tabs.SelectedIndex = 0;

            NoChampionshipHint.Visibility = aperto
                ? System.Windows.Visibility.Collapsed
                : System.Windows.Visibility.Visible;
        }

        private void OnChampionshipSelected(object sender, SelectionChangedEventArgs e)
        {
            var summary = ChampionshipList.SelectedItem as ChampionshipSummary;
            if (summary == null) return;

            // Si mette al sicuro quello aperto prima di aprirne un altro.
            //
            // Qui si caricava il nuovo campionato e basta: l oggetto in memoria veniva sostituito e
            // tutto quello che era stato modificato senza salvare spariva senza un avviso. Nel
            // Manager quasi tutte le modifiche vivono in memoria - si cambia un pilota in tabella,
            // si sposta una gara - e il salvataggio e un gesto a parte che e facile non fare.
            //
            // Si salva invece di chiedere. Una domanda "vuoi salvare?" a ogni clic sull elenco
            // diventa un rumore che si impara a scacciare senza leggerlo, e la volta che conta si
            // risponde no per abitudine. Qui non c e niente da perdere: lo stesso stato sarebbe
            // stato salvato comunque dal primo salvataggio successivo.
            if (_championship != null && _championship.Id != summary.Id) Save(true);

            var loaded = ChampionshipStore.Load(summary.Id);
            if (loaded == null)
            {
                Status(Testi.T("msg.campionato.apertura.fallita", summary.Name));
                return;
            }

            _championship = loaded;
            BindChampionship();
            UpdateTabsEnabled();
            AllineaContentManager();
            Status(Testi.T("ui.aperto", _championship.Name));
        }

        /// <summary>
        /// Mette Content Manager d'accordo con il campionato: una gara sola, quella che tocca.
        ///
        /// Si fa da soli, aprendo il campionato, e non con un pulsante. La regola "si vede solo la
        /// prossima gara" vale quanto il momento in cui viene applicata: finche l'esportazione non
        /// gira, in cartella restano i preset di prima. Dopo aver scritto tutta la regola, in
        /// Content Manager c'erano ancora ventidue gare selezionabili - il codice c'era e non era
        /// mai passato di li. Una regola che bisogna ricordarsi di far valere non e una regola.
        ///
        /// Il plugin fa lo stesso appena registra un risultato, cosi la gara appena corsa sparisce
        /// senza bisogno di riaprire questa applicazione.
        ///
        /// In silenzio: se Content Manager non c'e, o la cartella non si lascia scrivere, non e un
        /// motivo per interrompere chi sta solo aprendo un campionato.
        /// </summary>
        private void AllineaContentManager()
        {
            if (_championship == null) return;

            try
            {
                var cars = _acRoot != null ? AcContentScanner.Cars(_acRoot) : null;
                ContentManagerExporter.Export(_championship, null, true, cars);
            }
            catch (Exception)
            {
            }
        }

        private void BindChampionship()
        {
            if (_championship == null) return;
            _loading = true;

            HeaderChampionship.Text = Testi.T("ui.intestazione", _championship.Name,
                _championship.SeasonYear, _championship.Drivers.Count, _championship.Calendar.Count);

            ChampionshipName.Text = _championship.Name;
            ChampionshipYear.Text = _championship.SeasonYear.ToString();
            ChampionshipDescription.Text = _championship.Description;

            DriverTeamColumn.ItemsSource = _championship.Teams;

            RebindTeams();
            RebindDrivers();
            RebindCircuits();
            RebindCalendar();
            BindRules();
            RefreshStandings();
            RebindResultRounds();
            EnsurePhysics();
            SurveyWeather();

            _loading = false;
        }

        private void RebindTeams()
        {
            TeamsGrid.ItemsSource = null;
            TeamsGrid.ItemsSource = _championship.Teams;
        }

        private void RebindDrivers()
        {
            DriversGrid.ItemsSource = null;
            DriversGrid.ItemsSource = _championship.Drivers;
        }

        private void RebindCircuits()
        {
            CircuitsGrid.ItemsSource = null;
            CircuitsGrid.ItemsSource = _championship.Circuits;
        }

        private void RebindCalendar()
        {
            _championship.ResequenceCalendar();
            int selected = RoundList.SelectedIndex;

            RoundList.ItemsSource = null;
            RoundList.ItemsSource = _championship.Calendar
                .Select(r => new RoundListItem(r, _championship.CircuitOf(r)))
                .ToList();

            if (selected >= 0 && selected < _championship.Calendar.Count)
                RoundList.SelectedIndex = selected;
            else if (_championship.Calendar.Count > 0)
                RoundList.SelectedIndex = 0;
        }

        private sealed class RoundListItem
        {
            public RoundListItem(Round round, Circuit circuit)
            {
                Round = round;
                Text = round.Order + ". " + round.Name +
                       (round.Format == WeekendFormat.Sprint ? "   [sprint]" : "") +
                       (round.Status == RoundStatus.Completed ? "   (conclusa)" : "");
                Circuit = circuit;
            }

            public Round Round { get; }
            public Circuit Circuit { get; }
            public string Text { get; }
            public override string ToString() => Text;
        }

        /// <summary>
        /// Apre il configuratore e, se si arriva in fondo, salva quello che ne esce.
        ///
        /// Prima questo pulsante creava venti piloti e un calendario completo senza chiedere
        /// niente. Era comodo per provare e inutile per davvero: la prima cosa che si vuole
        /// decidere di un campionato e quante vetture schierare e chi si guida.
        /// </summary>
        private void OnNewSeeded(object sender, RoutedEventArgs e)
        {
            var configuratore = new NewChampionshipWindow { Owner = this };
            if (configuratore.ShowDialog() != true || configuratore.Result == null) return;

            var created = configuratore.Result;
            ChampionshipStore.Save(created);
            ReloadChampionshipList();
            ChampionshipList.SelectedIndex = _summaries.FindIndex(s => s.Id == created.Id);

            // Le gare finiscono subito dentro Content Manager, senza che nessuno debba aprirlo.
            //
            // Era il passaggio che restava a carico di chi usa Pitwall: creare il campionato qui
            // e poi andare di la a comporre la griglia gara per gara. Chi compra questo programma
            // deve trovare il campionato gia pronto nel menu di Content Manager.
            var esportazione = ExportToContentManager(created);

            Status("Campionato \"" + created.Name + "\" creato: " + created.Drivers.Count +
                   " piloti, " + created.Teams.Count + " scuderie, " + created.Calendar.Count +
                   " gare. " + esportazione);
        }

        private void OnNewEmpty(object sender, RoutedEventArgs e)
        {
            var created = new Championship { Name = "Nuovo campionato" };
            ChampionshipStore.Save(created);
            ReloadChampionshipList();
            ChampionshipList.SelectedIndex = _summaries.FindIndex(s => s.Id == created.Id);
            Status(Testi.T("msg.campionato.vuoto"));
        }

        private void OnDeleteChampionship(object sender, RoutedEventArgs e)
        {
            var summary = ChampionshipList.SelectedItem as ChampionshipSummary;
            if (summary == null) return;

            var answer = MessageBox.Show(
                "Eliminare definitivamente \"" + summary.Name + "\"?\n" +
                "Verranno persi calendario, piloti e tutti i risultati registrati,\n" +
                "e le sue gare spariranno anche da Content Manager.",
                "Pitwall", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (answer != MessageBoxResult.Yes) return;

            ChampionshipStore.Delete(summary.Id);

            // Le gare se ne vanno con il campionato.
            //
            // Pitwall le ha scritte dentro Content Manager alla creazione, quindi e Pitwall a
            // doverle togliere: lasciarle li vorrebbe dire un menu che si riempie di campionati
            // che non esistono piu, e nessuno che capisca da dove arrivano.
            string rimosse = ContentManagerExporter.RemovePresets(summary.Name)
                ? Testi.T("msg.campionato.gare.tolte")
                : "";

            _championship = null;

            // Il plugin tiene il campionato in memoria: se non lo si avvisa continua a seguire
            // uno che non esiste piu, e prima o poi lo riscrive su disco.
            LiveBridge.SendCommand(CommandNames.Reload);

            ReloadChampionshipList();
            Status(Testi.T("msg.campionato.eliminato") + rimosse);
        }

        private void OnChampionshipNameChanged(object sender, TextChangedEventArgs e)
        {
            if (_loading || _championship == null) return;
            _championship.Name = ChampionshipName.Text;
            HeaderChampionship.Text = Testi.T("ui.intestazione.corta", _championship.Name, _championship.SeasonYear);
        }

        private void OnChampionshipYearChanged(object sender, RoutedEventArgs e)
        {
            if (_loading || _championship == null) return;
            if (int.TryParse(ChampionshipYear.Text, out int year)) _championship.SeasonYear = year;
        }

        private void OnChampionshipDescriptionChanged(object sender, TextChangedEventArgs e)
        {
            if (_loading || _championship == null) return;
            _championship.Description = ChampionshipDescription.Text;
        }

        private void OnSave(object sender, RoutedEventArgs e) => Save(false);

        private void Save(bool silent)
        {
            if (_championship == null) return;
            try
            {
                ChampionshipStore.Save(_championship);
                LiveBridge.SendCommand(CommandNames.Reload);
                if (!silent)
                {
                    ReloadChampionshipList();
                    Status(Testi.T("msg.salvato.alle", DateTime.Now.ToString("HH:mm:ss")));
                }
            }
            catch (Exception ex)
            {
                if (!silent) MessageBox.Show(Testi.T("msg.salvataggio.fallito", ex.Message), "Pitwall");
            }
        }

        private void OnOpenDataFolder(object sender, RoutedEventArgs e)
        {
            PitwallPaths.EnsureFolders();
            Process.Start(new ProcessStartInfo { FileName = PitwallPaths.Root, UseShellExecute = true });
        }

        private void OnDetectAc(object sender, RoutedEventArgs e)
        {
            _acRoot = AcContentScanner.FindInstallation();
            AcPath.Text = _acRoot ?? Testi.T("msg.ac.non.trovato");
            if (_acRoot != null)
            {
                TeamCarColumn.ItemsSource = AcContentScanner.Cars(_acRoot);
                Status("Assetto Corsa trovato in " + _acRoot);
            }
        }

        // ================= Teams and drivers =================

        private void OnAddTeam(object sender, RoutedEventArgs e)
        {
            if (_championship == null) return;
            _championship.Teams.Add(new Team { Name = "Nuovo team", ShortName = "NEW", Color = "#888888" });
            RebindTeams();
            DriverTeamColumn.ItemsSource = _championship.Teams;
        }

        private void OnRemoveTeam(object sender, RoutedEventArgs e)
        {
            var team = TeamsGrid.SelectedItem as Team;
            if (team == null || _championship == null) return;

            int drivers = _championship.Drivers.Count(d => d.TeamId == team.Id);
            if (drivers > 0 &&
                MessageBox.Show(drivers + " piloti sono assegnati a questo team e resteranno senza team. Continuare?",
                                "Pitwall", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;

            _championship.Teams.Remove(team);
            foreach (var d in _championship.Drivers.Where(d => d.TeamId == team.Id)) d.TeamId = "";
            RebindTeams();
            RebindDrivers();
        }

        private void OnAddDriver(object sender, RoutedEventArgs e)
        {
            if (_championship == null) return;
            _championship.Drivers.Add(new Driver
            {
                FirstName = "Nuovo",
                LastName = "Pilota",
                Abbreviation = "NEW",
                Number = _championship.Drivers.Count + 1,
                TeamId = _championship.Teams.FirstOrDefault()?.Id ?? ""
            });
            RebindDrivers();
        }

        private void OnRemoveDriver(object sender, RoutedEventArgs e)
        {
            var driver = DriversGrid.SelectedItem as Driver;
            if (driver == null || _championship == null) return;
            _championship.Drivers.Remove(driver);
            RebindDrivers();
        }

        /// <summary>
        /// Spuntare "Tu" direttamente in tabella vale quanto il pulsante: il sedile diventa il
        /// tuo, gli altri lo perdono e la riga si ridisegna, cosi abilita e aggressivita IA
        /// spariscono subito da quella riga invece di restare li a far ridere.
        /// </summary>
        private void OnDriverCellEdited(object sender, DataGridCellEditEndingEventArgs e)
        {
            if (_championship == null || e.EditAction != DataGridEditAction.Commit) return;
            var driver = e.Row != null ? e.Row.Item as Driver : null;
            var box = e.EditingElement as System.Windows.Controls.Primitives.ToggleButton;
            if (driver == null || box == null) return;

            bool mine = box.IsChecked == true;
            foreach (var d in _championship.Drivers) d.IsPlayer = mine && d.Id == driver.Id;

            // La griglia e ancora in modifica: il rebind va rimandato a fine evento.
            Dispatcher.BeginInvoke(new Action(RebindDrivers), DispatcherPriority.Background);
            Status(mine ? Testi.T("msg.guiderai.come", driver.FullName)
                        : Testi.T("msg.nessun.pilota.tuo"));
        }

        private void OnSetPlayer(object sender, RoutedEventArgs e)
        {
            var driver = DriversGrid.SelectedItem as Driver;
            if (driver == null || _championship == null) return;
            foreach (var d in _championship.Drivers) d.IsPlayer = false;
            driver.IsPlayer = true;
            RebindDrivers();
            Status(Testi.T("msg.guiderai.come", driver.FullName));
        }

        // ================= Circuits =================

        private void OnScanTracks(object sender, RoutedEventArgs e)
        {
            if (_acRoot == null) _acRoot = AcContentScanner.FindInstallation();
            if (_acRoot == null)
            {
                MessageBox.Show(Testi.T("msg.ac.indica.percorso"), "Pitwall");
                return;
            }

            _tracks = AcContentScanner.Tracks(_acRoot);
            ApplyTrackFilter();
            Status(_tracks.Count + " tracciati trovati.");
        }

        private void OnTrackFilterChanged(object sender, TextChangedEventArgs e) => ApplyTrackFilter();

        private void ApplyTrackFilter()
        {
            string filter = (TrackFilter.Text ?? "").Trim();
            ScannedTracks.ItemsSource = string.IsNullOrEmpty(filter)
                ? _tracks
                : _tracks.Where(t => t.ToString().IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
        }

        private void OnAddScannedTrack(object sender, RoutedEventArgs e)
        {
            var track = ScannedTracks.SelectedItem as AcTrack;
            if (track == null || _championship == null) return;

            bool exists = _championship.Circuits.Any(c =>
                string.Equals(c.AcTrackId, track.TrackId, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(c.AcTrackConfig ?? "", track.Layout ?? "", StringComparison.OrdinalIgnoreCase));
            if (exists)
            {
                Status(Testi.T("msg.tracciato.gia.presente"));
                return;
            }

            _championship.Circuits.Add(AcContentScanner.ToCircuit(track));
            RebindCircuits();
            Status(track.DisplayName + " aggiunto. Controlla giri, giro di riferimento e severita gomme.");
        }

        private void OnRemoveCircuit(object sender, RoutedEventArgs e)
        {
            var circuit = CircuitsGrid.SelectedItem as Circuit;
            if (circuit == null || _championship == null) return;

            int rounds = _championship.Calendar.Count(r => r.CircuitId == circuit.Id);
            if (rounds > 0 &&
                MessageBox.Show("Il circuito e usato in " + rounds + " gare del calendario, che verranno rimosse. Continuare?",
                                "Pitwall", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;

            _championship.Calendar.RemoveAll(r => r.CircuitId == circuit.Id);
            _championship.Circuits.Remove(circuit);
            RebindCircuits();
            RebindCalendar();
        }

        private void OnAddRoundFromCircuit(object sender, RoutedEventArgs e)
        {
            var circuit = CircuitsGrid.SelectedItem as Circuit;
            if (circuit == null || _championship == null) return;

            var round = Round.Create("GP " + circuit.Name, circuit.Id, WeekendFormat.Standard,
                                     _championship.Calendar.Count + 1);
            round.Date = _championship.Calendar.Count > 0
                ? _championship.Calendar.Max(r => r.Date).AddDays(14)
                : DateTime.Today;
            WeatherGenerator.GenerateWeekend(circuit, round, _championship.Rules.Weather);

            _championship.Calendar.Add(round);
            RebindCalendar();
            Tabs.SelectedIndex = 4;
            RoundList.SelectedIndex = _championship.Calendar.Count - 1;
            Status(round.Name + " aggiunta al calendario.");
        }

        // ================= Scriptable physics =================

        private void OnSurveyPhysics(object sender, RoutedEventArgs e) => SurveyPhysics();

        private void SurveyPhysics()
        {
            if (_championship == null || _acRoot == null)
            {
                PhysicsStatus.Text = Testi.T("msg.piste.non.controllabili");
                return;
            }

            TrackPhysicsEnabler.Survey(_acRoot, _championship, out int ready, out int missing, out int notFound);
            PhysicsStatus.Text =
                "Script di physics: " + ready + " piste pronte, " + missing + " da abilitare" +
                (notFound > 0 ? ", " + notFound + " senza surfaces.ini accessibile" : "") +
                ". Senza, niente safety car reale ne strategia dell'IA.";
        }

        /// <summary>
        /// Abilita gli script di physics sulle piste che ancora non li consentono, da sola.
        ///
        /// Senza quel permesso Custom Shaders Patch non lascia toccare la physics agli script, e
        /// allora safety car, virtual safety car, comportamento degli avversari sotto bandiera e
        /// giro di formazione restano scritte sullo schermo senza succedere davvero. Era un
        /// pulsante da ricordarsi di premere: adesso Pitwall ci pensa quando apre un campionato
        /// e quando prepara i preset, e "Ripristina piste" rimette ogni file com'era.
        /// </summary>
        private void EnsurePhysics()
        {
            if (_championship == null || _acRoot == null) { SurveyPhysics(); return; }

            TrackPhysicsEnabler.Survey(_acRoot, _championship, out _, out int missing, out _);
            if (missing == 0) { SurveyPhysics(); return; }

            var result = TrackPhysicsEnabler.Enable(_acRoot, _championship);
            SurveyPhysics();

            if (result.Patched > 0)
                Status("Script di physics abilitati su " + result.Patched +
                       " file: safety car, bandiere e giro di formazione sono operativi.");
            else if (result.Notes.Count > 0)
                Status("Script di physics non abilitati: " + result.Notes[0]);
        }

        private void OnEnablePhysics(object sender, RoutedEventArgs e)
        {
            if (_championship == null || _acRoot == null) { SurveyPhysics(); return; }

            var answer = MessageBox.Show(
                "Questa operazione modifica i dati delle piste del campionato.\n\n" +
                "Serve perché Custom Shaders Patch consente agli script di toccare la physics solo " +
                "se la pista lo autorizza: senza, la safety car non può rallentare l'IA e la " +
                "strategia degli avversari non esiste.\n\n" +
                "Di ogni file viene messa da parte una copia, e il pulsante Ripristina rimette " +
                "tutto com'era. Tieni presente che una pista con physics estesa non vale più per " +
                "le classifiche online.\n\nProcedo?",
                "Pitwall", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (answer != MessageBoxResult.Yes) return;

            var result = TrackPhysicsEnabler.Enable(_acRoot, _championship);
            SurveyPhysics();

            string message = result.Patched + " file abilitati, " + result.AlreadyDone + " già a posto.";
            if (result.Notes.Count > 0) message += "\n\n" + string.Join("\n", result.Notes.Take(10));
            MessageBox.Show(message, "Pitwall", MessageBoxButton.OK, MessageBoxImage.Information);
            Status("Script di physics abilitati su " + result.Patched + " file.");
        }

        private void OnRestorePhysics(object sender, RoutedEventArgs e)
        {
            if (_championship == null || _acRoot == null) return;

            var result = TrackPhysicsEnabler.Restore(_acRoot, _championship);
            SurveyPhysics();
            MessageBox.Show(result.Restored + " file rimessi com'erano.", "Pitwall");
            Status(result.Restored + " piste ripristinate.");
        }

        // ================= Dynamic weather =================

        private void SurveyWeather()
        {
            if (_acRoot == null)
            {
                WeatherStatus.Text = "Assetto Corsa non trovato: impossibile controllare il meteo.";
                return;
            }

            bool installed = WeatherControllerInstaller.ControllerInstalled(_acRoot);
            bool selected = WeatherControllerInstaller.IsSelected(_acRoot);

            WeatherStatus.Text = !installed
                ? "Controller meteo non installato: esegui INSTALLA.cmd."
                : selected
                    ? "Meteo dinamico attivo: il tempo segue il piano del weekend, qualunque sia " +
                      "il tempo selezionato in Content Manager."
                    : "Controller installato ma non selezionato: premi Attiva meteo dinamico.";
        }

        private void OnEnableWeather(object sender, RoutedEventArgs e)
        {
            if (_acRoot == null) { SurveyWeather(); return; }

            var answer = MessageBox.Show(
                "Attivo il meteo dinamico di Pitwall.\n\n" +
                "Lo script viene messo anche al posto del controller meteo predefinito di Custom " +
                "Shaders Patch. Serve perché la scelta del controller non la fa il gioco: la fa " +
                "Content Manager a ogni avvio, e un preset del campionato non può portarsela " +
                "dietro. Mettendolo dove Content Manager guarda comunque, il meteo funziona senza " +
                "che tu debba sceglierlo ogni volta.\n\n" +
                "Nelle sessioni che con Pitwall non c'entrano si comporta come l'originale: " +
                "applica il tempo che hai scelto e basta.\n\n" +
                "L'originale viene messo da parte, e Ripristina meteo lo rimette. Procedo?",
                "Pitwall", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (answer != MessageBoxResult.Yes) return;

            // Se il gestionale gira dal pacchetto, il controller è lì accanto.
            string source = Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
                                         "..", "weather-controller", "pitwall");
            if (!WeatherControllerInstaller.CopyController(_acRoot, Path.GetFullPath(source)))
            {
                MessageBox.Show("Controller meteo non trovato nel pacchetto. Esegui INSTALLA.cmd.", "Pitwall");
                return;
            }

            WeatherControllerInstaller.Select(_acRoot);
            SurveyWeather();
            Status("Meteo dinamico attivato.");
        }

        private void OnRestoreWeather(object sender, RoutedEventArgs e)
        {
            bool done = WeatherControllerInstaller.Restore(_acRoot);
            SurveyWeather();
            Status(done ? "Configurazione meteo ripristinata." : "Non c'era nulla da ripristinare.");
        }

        // ================= Calendar =================

        private Round SelectedRound => (RoundList.SelectedItem as RoundListItem)?.Round;

        private void OnRoundSelected(object sender, SelectionChangedEventArgs e)
        {
            var round = SelectedRound;
            if (round == null)
            {
                RoundHeader.Text = "Nessuna gara selezionata";
                SessionsGrid.ItemsSource = null;
                ShowRoundLock(null);
                return;
            }

            _loading = true;
            var circuit = _championship.CircuitOf(round);
            RoundHeader.Text = round.Order + ". " + round.Name +
                               (circuit != null ? "   -   " + circuit.Name : "   -   circuito mancante");
            RoundName.Text = round.Name;
            RoundFormat.SelectedItem = round.Format;
            RoundDate.SelectedDate = round.Date;
            SessionsGrid.ItemsSource = round.Sessions;
            ShowRoundLock(round);
            _loading = false;
        }

        /// <summary>
        /// Blocca una gara che e stata corsa fino in fondo.
        ///
        /// Il risultato resta visibile ma niente di questo fine settimana si modifica: cambiare
        /// i giri o le sessioni di una gara gia corsa non vorrebbe dire niente, e il risultato
        /// registrato non tornerebbe piu con il regolamento con cui e stato ottenuto. In Content
        /// Manager quella gara non compare nemmeno piu.
        ///
        /// Si puo tornare indietro, ma di proposito e da qui: il pulsante accanto all'avviso.
        /// </summary>
        private void ShowRoundLock(Round round)
        {
            bool conclusa = round != null && round.Status == RoundStatus.Completed;

            RoundLockedBox.Visibility = conclusa ? Visibility.Visible : Visibility.Collapsed;

            RoundName.IsReadOnly = conclusa;
            RoundFormat.IsEnabled = !conclusa;
            RoundDate.IsEnabled = !conclusa;
            SessionsGrid.IsReadOnly = conclusa;

            if (!conclusa) return;

            var gara = _championship.ResultFor(round.Id, SessionKind.Race)
                    ?? _championship.ResultFor(round.Id, SessionKind.Sprint);

            var vincitore = gara != null ? gara.Winner : null;

            RoundLockedText.Text = vincitore != null && !string.IsNullOrWhiteSpace(vincitore.DriverName)
                ? Testi.T("ui.gara.conclusa", vincitore.DriverName)
                : Testi.T("ui.gara.conclusa.anonima");
        }

        private void OnReopenRound(object sender, RoutedEventArgs e)
        {
            var round = SelectedRound;
            if (_championship == null || round == null) return;

            var risposta = MessageBox.Show(
                Testi.T("ui.riapri.conferma", round.Order + ". " + round.Name),
                "Pitwall", MessageBoxButton.YesNo, MessageBoxImage.Warning);

            if (risposta != MessageBoxResult.Yes) return;

            if (!_championship.ReopenRound(round.Id)) return;

            // La classifica non va ricalcolata: si ricava dai risultati ogni volta che la si
            // guarda, quindi cancellare quelli di questa gara e tutto quello che serve.
            Save(true);

            RebindCalendar();
            RefreshStandings();
            OnRoundSelected(null, null);
            Status(Testi.T("ui.riapri.fatto", round.Name));
        }

        private void OnRoundFieldChanged(object sender, RoutedEventArgs e)
        {
            var round = SelectedRound;
            if (_loading || round == null) return;
            round.Name = RoundName.Text;
            if (RoundDate.SelectedDate.HasValue) round.Date = RoundDate.SelectedDate.Value;
            RebindCalendar();
        }

        private void OnRoundFieldChanged(object sender, SelectionChangedEventArgs e) =>
            OnRoundFieldChanged(sender, (RoutedEventArgs)e);

        private void OnRoundFormatChanged(object sender, SelectionChangedEventArgs e)
        {
            var round = SelectedRound;
            if (_loading || round == null || !(RoundFormat.SelectedItem is WeekendFormat format)) return;
            if (round.Format == format) return;

            if (MessageBox.Show("Cambiare formato ricostruisce le sessioni del weekend con i valori predefiniti. Continuare?",
                                "Pitwall", MessageBoxButton.YesNo) != MessageBoxResult.Yes)
            {
                _loading = true;
                RoundFormat.SelectedItem = round.Format;
                _loading = false;
                return;
            }

            round.Format = format;
            round.Sessions = SessionConfig.DefaultsFor(format);
            WeatherGenerator.GenerateWeekend(_championship.CircuitOf(round), round, _championship.Rules.Weather);
            SessionsGrid.ItemsSource = null;
            SessionsGrid.ItemsSource = round.Sessions;
            RebindCalendar();
        }

        private void OnSessionEdited(object sender, DataGridCellEditEndingEventArgs e)
        {
            // Re-running the generator keeps the forecast consistent with a changed session length.
            Dispatcher.BeginInvoke(new Action(() =>
            {
                var round = SelectedRound;
                if (round == null) return;
                SessionsGrid.Items.Refresh();
            }), DispatcherPriority.Background);
        }

        private void OnMoveRoundUp(object sender, RoutedEventArgs e) => MoveRound(-1);

        private void OnMoveRoundDown(object sender, RoutedEventArgs e) => MoveRound(1);

        private void MoveRound(int delta)
        {
            var round = SelectedRound;
            if (round == null) return;

            var ordered = _championship.Calendar.OrderBy(r => r.Order).ToList();
            int index = ordered.IndexOf(round);
            int target = index + delta;
            if (target < 0 || target >= ordered.Count) return;

            ordered.RemoveAt(index);
            ordered.Insert(target, round);
            for (int i = 0; i < ordered.Count; i++) ordered[i].Order = i + 1;
            _championship.Calendar = ordered;

            RebindCalendar();
            RoundList.SelectedIndex = target;
        }

        private void OnRemoveRound(object sender, RoutedEventArgs e)
        {
            var round = SelectedRound;
            if (round == null) return;

            int results = _championship.Results.Count(r => r.RoundId == round.Id);
            if (results > 0 &&
                MessageBox.Show("Questa gara ha " + results + " risultati registrati che verranno cancellati. Continuare?",
                                "Pitwall", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;

            _championship.Results.RemoveAll(r => r.RoundId == round.Id);
            _championship.Calendar.Remove(round);
            RebindCalendar();
            RefreshStandings();
        }

        private void OnRegenerateWeather(object sender, RoutedEventArgs e)
        {
            var round = SelectedRound;
            if (round == null) return;

            round.Seed = Environment.TickCount;
            WeatherGenerator.GenerateWeekend(_championship.CircuitOf(round), round, _championship.Rules.Weather);
            SessionsGrid.Items.Refresh();
            Status("Meteo rigenerato per " + round.Name + ".");
        }

        // ================= Content Manager presets =================

        private void OnRefreshCmTemplates(object sender, RoutedEventArgs e) => RefreshCmTemplates(true);

        private void RefreshCmTemplates(bool report)
        {
            // Nessuna selezione automatica: senza base, la griglia viene generata da zero, che e
            // il comportamento voluto nella maggior parte dei casi.
            CmTemplate.ItemsSource = ContentManagerExporter.Templates();
            var templates = (List<CmPreset>)CmTemplate.ItemsSource;

            if (!report) return;
            Status(templates.Count == 0
                ? "Nessun preset esistente: l'esportazione partira da una base predefinita."
                : templates.Count + " preset trovati, utilizzabili come base.");
        }

        /// <summary>
        /// Scrive le gare del campionato dentro Content Manager e racconta com e andata.
        ///
        /// Si chiama da sola alla creazione: chi usa Pitwall non deve sapere che Content Manager
        /// esiste, deve solo trovarci dentro il campionato gia pronto.
        /// </summary>
        private string ExportToContentManager(Championship championship)
        {
            try
            {
                var cars = _acRoot != null ? AcContentScanner.Cars(_acRoot) : null;
                var result = ContentManagerExporter.Export(championship, null, true, cars);

                if (result.Written > 0)
                    return "Pronto in Content Manager: " + result.Written + " gare.";

                return result.Notes.Count > 0
                    ? "Non esportato in Content Manager: " + result.Notes[0]
                    : "Non esportato in Content Manager.";
            }
            catch (Exception ex)
            {
                return "Esportazione in Content Manager non riuscita: " + ex.Message;
            }
        }

        private void OnExportCmPresets(object sender, RoutedEventArgs e)
        {
            if (_championship == null) return;

            // Si sta per andare in pista: e il momento buono per assicurarsi che le piste del
            // campionato lascino lavorare la direzione gara.
            EnsurePhysics();

            // Il preset di partenza e facoltativo: senza, la griglia viene costruita da zero.
            var template = CmTemplate.SelectedItem as CmPreset;
            var cars = _acRoot != null ? AcContentScanner.Cars(_acRoot) : null;
            var result = ContentManagerExporter.Export(_championship, template,
                                                       CmWeekend.IsChecked == true, cars);
            if (result.Written == 0)
            {
                MessageBox.Show("Nessun preset scritto.\n\n" + string.Join("\n", result.Notes), "Pitwall");
                return;
            }

            var prossima = _championship.NextRound();
            string quale = prossima != null ? prossima.Order + ". " + prossima.Name : "";

            string message = Testi.T("ui.cm.gara.pronta", quale, _championship.Name);
            if (result.Notes.Count > 0) message += "\n\n" + string.Join("\n", result.Notes);

            MessageBox.Show(message, "Pitwall", MessageBoxButton.OK, MessageBoxImage.Information);
            Status(Testi.T("cm.prossima.gara", quale));
        }

        // ================= Rules =================

        private void BindRules()
        {
            var rules = _championship.Rules;
            var points = rules.Points;

            RacePoints.Text = string.Join(", ", points.Race);
            SprintPoints.Text = string.Join(", ", points.Sprint);
            FastestLapPoints.Text = points.FastestLap.ToString();
            FastestLapMax.Text = points.FastestLapMaxPosition.ToString();
            PolePoints.Text = points.PolePosition.ToString();
            DroppedScores.Text = points.DroppedScores.ToString();
            ShortenedScale.IsChecked = points.UseShortenedRaceScale;

            SoftSets.Text = rules.Tyres.SoftSets.ToString();
            MediumSets.Text = rules.Tyres.MediumSets.ToString();
            HardSets.Text = rules.Tyres.HardSets.ToString();
            CliffWear.Text = Num(rules.Tyres.CliffWearPercent);
            CriticalWear.Text = Num(rules.Tyres.CriticalWearPercent);

            DynamicWeather.IsChecked = rules.Weather.DynamicWeather;
            RainMultiplier.Text = Num(rules.Weather.RainMultiplier);
            WeatherVolatility.Text = Num(rules.Weather.Volatility);

            RcEnabled.IsChecked = rules.RaceControl.Enabled;
            VscEnabled.IsChecked = rules.RaceControl.VirtualSafetyCarEnabled;
            ScEnabled.IsChecked = rules.RaceControl.SafetyCarEnabled;
            RedFlagEnabled.IsChecked = rules.RaceControl.RedFlagEnabled;
            VscSeconds.Text = rules.RaceControl.VscMinimumSeconds.ToString();
            ScLaps.Text = rules.RaceControl.SafetyCarMinimumLaps.ToString();
            VscPace.Text = Num(rules.RaceControl.VscPacePercent);
            ScPace.Text = Num(rules.RaceControl.SafetyCarPacePercent);

            TrackLimitsEnabled.IsChecked = rules.RaceControl.TrackLimitsEnabled;
            TrackLimitsWarnings.Text = rules.RaceControl.TrackLimitsWarnings.ToString();
            TrackLimitsPenalty.Text = Num(rules.RaceControl.TrackLimitsPenaltySeconds);
            PitSpeedEnabled.IsChecked = rules.RaceControl.PitLaneSpeedingEnabled;
            PitSpeedLimit.Text = Num(rules.RaceControl.PitLaneSpeedLimitKmh);
            PitSpeedPenalty.Text = Num(rules.RaceControl.PitLaneSpeedingPenaltySeconds);

            EngineerEnabled.IsChecked = rules.Engineer.Enabled;
            EngineerVerbosity.Text = rules.Engineer.Verbosity.ToString();
            EngineerGap.Text = Num(rules.Engineer.MinimumSecondsBetweenMessages);

            FuelRate.Text = Num(rules.FuelRate);
            TyreWearRate.Text = Num(rules.TyreWearRate);
            DamageRate.Text = Num(rules.DamageRate);
        }

        private void OnRulesChanged(object sender, RoutedEventArgs e)
        {
            if (_loading || _championship == null) return;
            var rules = _championship.Rules;
            var points = rules.Points;

            points.Race = ParseInts(RacePoints.Text, points.Race);
            points.Sprint = ParseInts(SprintPoints.Text, points.Sprint);
            points.FastestLap = ParseInt(FastestLapPoints.Text, points.FastestLap);
            points.FastestLapMaxPosition = ParseInt(FastestLapMax.Text, points.FastestLapMaxPosition);
            points.PolePosition = ParseInt(PolePoints.Text, points.PolePosition);
            points.DroppedScores = ParseInt(DroppedScores.Text, points.DroppedScores);
            points.UseShortenedRaceScale = ShortenedScale.IsChecked == true;

            rules.Tyres.SoftSets = ParseInt(SoftSets.Text, rules.Tyres.SoftSets);
            rules.Tyres.MediumSets = ParseInt(MediumSets.Text, rules.Tyres.MediumSets);
            rules.Tyres.HardSets = ParseInt(HardSets.Text, rules.Tyres.HardSets);
            rules.Tyres.CliffWearPercent = ParseDouble(CliffWear.Text, rules.Tyres.CliffWearPercent);
            rules.Tyres.CriticalWearPercent = ParseDouble(CriticalWear.Text, rules.Tyres.CriticalWearPercent);

            rules.Weather.DynamicWeather = DynamicWeather.IsChecked == true;
            rules.Weather.RainMultiplier = ParseDouble(RainMultiplier.Text, rules.Weather.RainMultiplier);
            rules.Weather.Volatility = ParseDouble(WeatherVolatility.Text, rules.Weather.Volatility);

            rules.RaceControl.Enabled = RcEnabled.IsChecked == true;
            rules.RaceControl.VirtualSafetyCarEnabled = VscEnabled.IsChecked == true;
            rules.RaceControl.SafetyCarEnabled = ScEnabled.IsChecked == true;
            rules.RaceControl.RedFlagEnabled = RedFlagEnabled.IsChecked == true;
            rules.RaceControl.VscMinimumSeconds = ParseInt(VscSeconds.Text, rules.RaceControl.VscMinimumSeconds);
            rules.RaceControl.SafetyCarMinimumLaps = ParseInt(ScLaps.Text, rules.RaceControl.SafetyCarMinimumLaps);
            rules.RaceControl.VscPacePercent = ParseDouble(VscPace.Text, rules.RaceControl.VscPacePercent);
            rules.RaceControl.SafetyCarPacePercent = ParseDouble(ScPace.Text, rules.RaceControl.SafetyCarPacePercent);

            rules.RaceControl.TrackLimitsEnabled = TrackLimitsEnabled.IsChecked == true;
            rules.RaceControl.TrackLimitsWarnings = ParseInt(TrackLimitsWarnings.Text, rules.RaceControl.TrackLimitsWarnings);
            rules.RaceControl.TrackLimitsPenaltySeconds = ParseDouble(TrackLimitsPenalty.Text, rules.RaceControl.TrackLimitsPenaltySeconds);
            rules.RaceControl.PitLaneSpeedingEnabled = PitSpeedEnabled.IsChecked == true;
            rules.RaceControl.PitLaneSpeedLimitKmh = ParseDouble(PitSpeedLimit.Text, rules.RaceControl.PitLaneSpeedLimitKmh);
            rules.RaceControl.PitLaneSpeedingPenaltySeconds = ParseDouble(PitSpeedPenalty.Text, rules.RaceControl.PitLaneSpeedingPenaltySeconds);

            rules.Engineer.Enabled = EngineerEnabled.IsChecked == true;
            rules.Engineer.Verbosity = Math.Max(0, Math.Min(2, ParseInt(EngineerVerbosity.Text, rules.Engineer.Verbosity)));
            rules.Engineer.MinimumSecondsBetweenMessages = ParseDouble(EngineerGap.Text, rules.Engineer.MinimumSecondsBetweenMessages);

            rules.FuelRate = ParseDouble(FuelRate.Text, rules.FuelRate);
            rules.TyreWearRate = ParseDouble(TyreWearRate.Text, rules.TyreWearRate);
            rules.DamageRate = ParseDouble(DamageRate.Text, rules.DamageRate);

            Status("Regolamento aggiornato. Ricorda di salvare.");
        }

        private void OnRulesChanged(object sender, SelectionChangedEventArgs e) =>
            OnRulesChanged(sender, (RoutedEventArgs)e);

        private void OnPointsPresetChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loading || _championship == null) return;
            if (!(PointsPreset.SelectedItem is PointsScheme preset)) return;

            _championship.Rules.Points = preset.Clone();
            _loading = true;
            BindRules();
            _loading = false;
            Status("Schema punti impostato su " + preset.Name + ".");
        }

        // ================= Standings =================

        private void OnRefreshStandings(object sender, RoutedEventArgs e) => RefreshStandings();

        private void RefreshStandings()
        {
            if (_championship == null) return;
            DriverStandings.ItemsSource = StandingsCalculator.Drivers(_championship);
            TeamStandings.ItemsSource = StandingsCalculator.Constructors(_championship);
        }

        private void RebindResultRounds()
        {
            ResultRound.ItemsSource = _championship.Calendar
                .OrderBy(r => r.Order)
                .Select(r => new RoundListItem(r, _championship.CircuitOf(r)))
                .ToList();

            ResultSession.ItemsSource = Enum.GetValues(typeof(SessionKind));
            ResultSession.SelectedItem = SessionKind.Race;
        }

        private void OnResultRoundChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_championship == null) return;
            var round = (ResultRound.SelectedItem as RoundListItem)?.Round;
            if (round == null || !(ResultSession.SelectedItem is SessionKind kind))
            {
                ResultGrid.ItemsSource = null;
                return;
            }

            var result = _championship.ResultFor(round.Id, kind);
            ResultGrid.ItemsSource = result?.Entries;
            Status(result == null
                ? "Nessun risultato registrato per questa sessione."
                : result.Entries.Count + " vetture classificate" +
                  (result.SafetyCarPeriods > 0 ? ", " + result.SafetyCarPeriods + " safety car" : "") +
                  (result.VirtualSafetyCarPeriods > 0 ? ", " + result.VirtualSafetyCarPeriods + " VSC" : "") + ".");
        }

        private void OnExportStandings(object sender, RoutedEventArgs e)
        {
            if (_championship == null) return;

            PitwallPaths.EnsureFolders();
            string path = Path.Combine(PitwallPaths.Exports,
                PitwallPaths.SanitiseFileName(_championship.Name) + "-classifica.csv");

            var sb = new StringBuilder();
            sb.AppendLine("Posizione;Pilota;Team;Punti;Vittorie;Podi;Pole;GiriVeloci;Ritiri");
            foreach (var row in StandingsCalculator.Drivers(_championship))
                sb.AppendLine(string.Join(";", row.Position, row.Name, row.TeamName,
                    row.Points.ToString(CultureInfo.InvariantCulture), row.Wins, row.Podiums,
                    row.Poles, row.FastestLaps, row.Retirements));

            sb.AppendLine();
            sb.AppendLine("Posizione;Team;Punti;Vittorie;Podi");
            foreach (var row in StandingsCalculator.Constructors(_championship))
                sb.AppendLine(string.Join(";", row.Position, row.Name,
                    row.Points.ToString(CultureInfo.InvariantCulture), row.Wins, row.Podiums));

            File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
            Status("Classifica esportata in " + path);
            Process.Start(new ProcessStartInfo { FileName = PitwallPaths.Exports, UseShellExecute = true });
        }

        // ================= Live =================

        /// <summary>Una riga del muretto: etichetta tradotta e valore.</summary>
        private static KeyValuePair<string, string> Voce(string chiave, string valore) =>
            new KeyValuePair<string, string>(Testi.T(chiave), valore ?? "");

        /// <summary>Una riga vuota fra un gruppo e l'altro.</summary>
        private static KeyValuePair<string, string> Stacco() =>
            new KeyValuePair<string, string>(null, null);

        private void OnLiveTick(object sender, EventArgs e)
        {
            // Si confronta la scheda, non il suo numero.
            //
            // Qui c era "indice diverso da 7". Poi e stata aggiunta la scheda Impostazioni prima di
            // Live, Live e diventata l ottava, e da quel momento il muretto non si e piu aggiornato:
            // il cronometro si attivava guardando le Impostazioni e restava fermo guardando la gara.
            // Nessun errore, nessun messaggio, semplicemente dei numeri che non cambiavano mai.
            //
            // Con il riferimento all elemento il difetto non puo tornare: le schede si possono
            // spostare, aggiungere e togliere, e questa riga resta giusta.
            if (!ReferenceEquals(Tabs.SelectedItem, TabLive)) return;

            var state = LiveBridge.ReadState();
            if (state == null || !state.PluginRunning)
            {
                LiveConnection.Text = "Plugin SimHub non attivo. Avvia SimHub con Pitwall abilitato.";
                return;
            }

            LiveConnection.Text = state.GameRunning
                ? Testi.T("muretto.collegato", state.ChampionshipName, state.SessionLabel)
                : Testi.T("muretto.gioco.fermo");

            // Il pannello si incolonna da solo.
            //
            // Prima le etichette erano scritte con gli spazi dentro - "Dir. gara  " - e quindi
            // allineate a mano su una larghezza decisa una volta per tutte in italiano. Tradotte,
            // quelle larghezze non tornavano piu: era il motivo per cui questo pannello era rimasto
            // l'unico pezzo non tradotto dell'applicazione. Qui si scrive solo la parola, e la
            // colonna si calcola sulla piu lunga del momento: vale in qualunque lingua, anche in una
            // che non abbiamo ancora.
            var voci = new List<KeyValuePair<string, string>>
            {
                Voce("muretto.round", state.RoundName),
                Voce("muretto.circuito", state.CircuitName),
                Voce("muretto.giro", state.LapCurrent + (state.LapsTotal > 0 ? " / " + state.LapsTotal : "")),
                Voce("muretto.posizione", state.Position > 0 ? "P" + state.Position : "-"),
                Stacco(),
                Voce("muretto.direzione", state.RaceControlText),
                Voce("muretto.delta", state.Delta.ToString("+0.00;-0.00")),
                Voce("muretto.penalita", state.PendingPenaltySeconds > 0
                        ? "+" + state.PendingPenaltySeconds + " s"
                        : Testi.T("muretto.nessuna")),
                Voce("muretto.limiti", state.TrackLimitStrikes.ToString()),
                Stacco(),
                Voce("muretto.gomme", state.TyreCompound + "  " + state.TyreWearPercent.ToString("0.0") + "%"),
                Voce("muretto.degrado", Testi.T("muretto.fra.giri", state.LapsToCliff.ToString("0.0"))),
                Voce("muretto.strategia", state.StrategyAdvice),
                Voce("muretto.finestra", state.PitWindowEarliest + " - " + state.PitWindowLatest +
                        " (" + Testi.T("muretto.ideale", state.PitWindowIdeal) + ")"),
                Stacco(),
                Voce("muretto.meteo", state.WeatherNow + (state.MinutesToRain >= 0
                        ? Testi.T("muretto.pioggia.fra", state.MinutesToRain) : "")),
                Voce("muretto.temperature", state.AirTemperature + " / " + state.RoadTemperature)
            };

            int colonna = 0;
            foreach (var voce in voci)
            {
                if (voce.Key != null && voce.Key.Length > colonna) colonna = voce.Key.Length;
            }
            colonna += 2;

            var lines = new List<string>();
            foreach (var voce in voci)
            {
                lines.Add(voce.Key == null ? "" : voce.Key.PadRight(colonna) + voce.Value);
            }

            LiveSummary.Text = string.Join(Environment.NewLine, lines);
            LiveTiming.ItemsSource = state.Timing;

            var radio = new List<string>(state.EngineerHistory);
            radio.AddRange(state.RaceControlFeed);
            LiveRadio.ItemsSource = radio;
        }

        private void OnLiveVsc(object sender, RoutedEventArgs e) => LiveBridge.SendCommand(CommandNames.DeployVsc);
        private void OnLiveSc(object sender, RoutedEventArgs e) => LiveBridge.SendCommand(CommandNames.DeploySafetyCar);
        private void OnLiveEnd(object sender, RoutedEventArgs e) => LiveBridge.SendCommand(CommandNames.EndNeutralisation);
        private void OnLiveGreen(object sender, RoutedEventArgs e) => LiveBridge.SendCommand(CommandNames.ResumeGreen);
        private void OnLiveRed(object sender, RoutedEventArgs e) => LiveBridge.SendCommand(CommandNames.RedFlag);
        private void OnLiveClearPenalties(object sender, RoutedEventArgs e) => LiveBridge.SendCommand(CommandNames.ClearPenalties);

        private void OnLiveSaveResult(object sender, RoutedEventArgs e)
        {
            LiveBridge.SendCommand(CommandNames.SaveResult);
            Status("Richiesto il salvataggio del risultato. Ricarica il campionato fra qualche secondo.");
        }

        // ================= Parsing helpers =================

        private static string Num(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);

        private static int ParseInt(string text, int fallback) =>
            int.TryParse((text ?? "").Trim(), out int value) ? value : fallback;

        private static double ParseDouble(string text, double fallback)
        {
            string cleaned = (text ?? "").Trim().Replace(',', '.');
            return double.TryParse(cleaned, NumberStyles.Any, CultureInfo.InvariantCulture, out double value)
                ? value : fallback;
        }

        private static List<int> ParseInts(string text, List<int> fallback)
        {
            try
            {
                var parsed = (text ?? "")
                    .Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(p => int.Parse(p.Trim(), CultureInfo.InvariantCulture))
                    .ToList();
                return parsed.Count > 0 ? parsed : fallback;
            }
            catch (Exception)
            {
                return fallback;
            }
        }
    }
}
