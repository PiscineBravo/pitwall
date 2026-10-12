using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Pitwall.Live;
using Pitwall.Storage;

namespace User.Pitwall.Ui
{
    /// <summary>
    /// Il pannello di Pitwall dentro SimHub, diviso in sezioni come le altre pagine del
    /// programma: una barra di navigazione in alto e una pagina alla volta sotto.
    ///
    /// E costruito in codice e non in XAML: l'SDK moderno non compila il markup WPF per .NET
    /// Framework, e un controllo programmatico tiene il progetto compilabile con il solo SDK
    /// .NET installato.
    /// </summary>
    public sealed class SettingsControl : UserControl
    {
        private readonly PitwallPlugin _plugin;

        // Navigazione
        private readonly StackPanel _nav = new StackPanel { Orientation = Orientation.Horizontal };
        private readonly Border _host = new Border();
        private readonly List<Tuple<string, Button, Func<UIElement>>> _pages =
            new List<Tuple<string, Button, Func<UIElement>>>();

        /// <summary>
        /// Le pagine si costruiscono una volta sola e si conservano. Ricostruirle a ogni cambio
        /// significherebbe mettere gli stessi controlli sotto un secondo genitore, e WPF non lo
        /// permette: la seconda visita alla pagina lancerebbe un'eccezione.
        /// </summary>
        private readonly Dictionary<string, UIElement> _built = new Dictionary<string, UIElement>();

        private string _current = "";

        // Impostazioni
        private readonly ComboBox _championship = new ComboBox();
        private readonly ComboBox _voice = new ComboBox();
        private readonly ComboBox _language = new ComboBox();
        private readonly ComboBox _wearMode = new ComboBox();
        private readonly CheckBox _engineer = new CheckBox { Content = "Ingegnere di pista attivo" };
        private readonly CheckBox _speech = new CheckBox { Content = "Leggi i messaggi ad alta voce" };
        private readonly CheckBox _raceControl = new CheckBox { Content = "Direzione gara: bandiere, safety car, VSC, penalità" };
        private readonly CheckBox _autoSave = new CheckBox { Content = "Salva il risultato a fine sessione" };
        private readonly CheckBox _autoDriver = new CheckBox { Content = "Riconosci da solo quale pilota sto guidando" };
        private readonly Slider _rate = new Slider { Minimum = -6, Maximum = 8, TickFrequency = 1, IsSnapToTickEnabled = true, Width = 220 };
        private readonly Slider _volume = new Slider { Minimum = 0, Maximum = 100, TickFrequency = 5, IsSnapToTickEnabled = true, Width = 220 };

        // Meteo deciso dalla direzione gara
        private readonly Slider _weatherTransition = new Slider
        {
            Minimum = 0, Maximum = 15, Value = 4,
            TickFrequency = 1, IsSnapToTickEnabled = true, Width = 180,
            VerticalAlignment = VerticalAlignment.Center
        };
        private readonly TextBlock _weatherTransitionLabel = new TextBlock
        {
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 0, 0)
        };

        // Valori
        private readonly TextBox _search = new TextBox();
        private readonly StackPanel _catalogue = new StackPanel();
        private readonly TextBlock _catalogueCount = new TextBlock { Opacity = 0.75 };
        private readonly List<Tuple<PitwallProperty, TextBlock>> _liveValues =
            new List<Tuple<PitwallProperty, TextBlock>>();

        // Stato
        private readonly TextBlock _status = new TextBlock { TextWrapping = TextWrapping.Wrap, FontFamily = new FontFamily("Consolas") };

        private readonly DispatcherTimer _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        private List<ChampionshipSummary> _summaries = new List<ChampionshipSummary>();
        private bool _loading;

        private static readonly Brush Accent = new SolidColorBrush(Color.FromRgb(0xE1, 0x06, 0x00));
        private static readonly Brush Muted = new SolidColorBrush(Color.FromRgb(0x93, 0xA0, 0xB0));

        public SettingsControl(PitwallPlugin plugin)
        {
            _plugin = plugin;
            Content = BuildShell();
            LoadFromSettings();
            BuildCatalogue();
            Show("Campionato");

            _timer.Tick += (s, e) => Refresh();
            Loaded += (s, e) => _timer.Start();
            Unloaded += (s, e) => _timer.Stop();
        }

        // ---- Guscio e navigazione ---------------------------------------------------------------

        private UIElement BuildShell()
        {
            var root = new DockPanel { Margin = new Thickness(16) };

            var head = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
            head.Children.Add(new TextBlock
            {
                Text = "Pitwall",
                FontSize = 24,
                FontWeight = FontWeights.Bold
            });
            head.Children.Add(new TextBlock
            {
                Text = "Campionato in stile Formula 1 per Assetto Corsa",
                Foreground = Muted,
                Margin = new Thickness(0, 0, 0, 8)
            });
            DockPanel.SetDock(head, Dock.Top);
            root.Children.Add(head);

            AddPage("Campionato", BuildChampionshipPage);
            AddPage("Ingegnere", BuildEngineerPage);
            AddPage("Direzione gara", BuildRaceControlPage);
            AddPage("Telemetria", BuildTelemetryPage);
            AddPage("Valori", BuildValuesPage);
            AddPage("Stato", BuildStatusPage);

            DockPanel.SetDock(_nav, Dock.Top);
            root.Children.Add(_nav);

            var separator = new Border
            {
                Height = 2,
                Background = Accent,
                Margin = new Thickness(0, 4, 0, 12)
            };
            DockPanel.SetDock(separator, Dock.Top);
            root.Children.Add(separator);

            root.Children.Add(_host);
            return root;
        }

        private void AddPage(string name, Func<UIElement> builder)
        {
            var button = new Button
            {
                Content = name,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Foreground = Muted,
                FontSize = 16,
                Padding = new Thickness(0, 2, 22, 2),
                Cursor = System.Windows.Input.Cursors.Hand
            };
            button.Click += (s, e) => Show(name);

            _nav.Children.Add(button);
            _pages.Add(Tuple.Create(name, button, builder));
        }

        /// <summary>
        /// Mostra una pagina. Il contenuto viene costruito ogni volta: sono pannelli piccoli, e
        /// cosi riflettono sempre lo stato corrente senza doverlo risincronizzare a mano.
        /// </summary>
        private void Show(string name)
        {
            var page = _pages.FirstOrDefault(p => p.Item1 == name);
            if (page == null) return;

            _current = name;
            foreach (var p in _pages)
            {
                bool active = p.Item1 == name;
                p.Item2.Foreground = active ? Brushes.White : Muted;
                p.Item2.FontWeight = active ? FontWeights.SemiBold : FontWeights.Normal;
            }

            UIElement content;
            if (!_built.TryGetValue(name, out content))
            {
                content = new ScrollViewer
                {
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    Content = page.Item3()
                };
                _built[name] = content;
            }

            _host.Child = content;
            Refresh();
        }

        private void Refresh()
        {
            if (_current == "Stato") RefreshStatus();
            else if (_current == "Valori") RefreshCatalogueValues();
        }

        // ---- Pagina: Campionato -------------------------------------------------------------------

        private UIElement BuildChampionshipPage()
        {
            var panel = new StackPanel();
            panel.Children.Add(Note("Il plugin segue il campionato selezionato qui e vi scrive dentro i risultati " +
                                    "appena una sessione finisce. Calendario, piloti e regolamento si modificano " +
                                    "nell'applicazione Pitwall Manager."));

            var row = Row();
            _championship.Width = 340;
            row.Children.Add(_championship);
            row.Children.Add(Button("Aggiorna elenco", (s, e) => ReloadChampionships()));
            row.Children.Add(Button("Cartella dati", (s, e) => OpenDataFolder()));
            panel.Children.Add(row);

            panel.Children.Add(Heading("Il tuo pilota"));
            panel.Children.Add(_autoDriver);
            panel.Children.Add(Note("Il nome che usi in Assetto Corsa non compare nel campionato, quindi Pitwall " +
                                    "deve capire al posto di chi stai correndo. Lo deduce dalla griglia: se dei venti " +
                                    "piloti del campionato diciannove sono in pista e uno manca, quello che manca è " +
                                    "il sedile che occupi tu. Il sedile conserva scuderia, numero e sigla — cambia " +
                                    "il pilota, non la macchina — e il nome di chi lo occupava resta annotato."));

            panel.Children.Add(Heading("Dove sono i dati"));
            panel.Children.Add(new TextBlock
            {
                Text = PitwallPaths.Root,
                FontFamily = new FontFamily("Consolas"),
                Opacity = 0.85,
                TextWrapping = TextWrapping.Wrap
            });
            panel.Children.Add(Note("Una seconda copia dello stato viene scritta in Documenti\\Assetto Corsa\\Pitwall, " +
                                    "perché è da lì che le app in pista hanno il permesso di leggere."));

            return panel;
        }

        // ---- Pagina: Ingegnere ----------------------------------------------------------------------

        private UIElement BuildEngineerPage()
        {
            var panel = new StackPanel();
            panel.Children.Add(Note("L'ingegnere commenta gomme, benzina, strategia, meteo, distacchi e penalità. " +
                                    "Parla solo quando ha qualcosa da dire: una coda con priorità tiene la radio pulita."));

            panel.Children.Add(_engineer);
            panel.Children.Add(_speech);

            var languageRow = Row();
            languageRow.Children.Add(Label("Lingua"));
            _language.Width = 110;
            languageRow.Children.Add(_language);
            languageRow.Children.Add(Label("Voce"));
            _voice.Width = 280;
            languageRow.Children.Add(_voice);
            panel.Children.Add(languageRow);

            var rateRow = Row();
            rateRow.Children.Add(Label("Velocità"));
            rateRow.Children.Add(_rate);
            rateRow.Children.Add(Label("Volume"));
            rateRow.Children.Add(_volume);
            panel.Children.Add(rateRow);

            panel.Children.Add(Note("Le voci disponibili sono quelle installate in Windows. Se manca una voce italiana, " +
                                    "si aggiunge dalle impostazioni di sistema, sezione Lingua e data."));
            return panel;
        }

        // ---- Pagina: Direzione gara -------------------------------------------------------------------

        private UIElement BuildRaceControlPage()
        {
            var panel = new StackPanel();
            panel.Children.Add(Note("Bandiere, safety car, virtual safety car, giro di formazione e penalità. " +
                                    "I pulsanti qui sotto servono a provare una dashboard senza aspettare un incidente."));

            panel.Children.Add(_raceControl);
            panel.Children.Add(_autoSave);

            var row = Row();
            row.Children.Add(Button("Virtual Safety Car", (s, e) => LiveBridge.SendCommand(CommandNames.DeployVsc)));
            row.Children.Add(Button("Safety Car", (s, e) => LiveBridge.SendCommand(CommandNames.DeploySafetyCar)));
            row.Children.Add(Button("Verde", (s, e) => LiveBridge.SendCommand(CommandNames.ResumeGreen)));
            panel.Children.Add(row);

            var row2 = Row();
            row2.Children.Add(Button("Bandiera rossa", (s, e) => LiveBridge.SendCommand(CommandNames.RedFlag)));
            row2.Children.Add(Button("Azzera penalità", (s, e) => LiveBridge.SendCommand(CommandNames.ClearPenalties)));
            row2.Children.Add(Button("Salva risultato", (s, e) => LiveBridge.SendCommand(CommandNames.SaveResult)));
            panel.Children.Add(row2);

            panel.Children.Add(Heading("Meteo"));
            panel.Children.Add(Note("Cambia il tempo da adesso in poi. Il cielo ci arriva nei minuti scelti qui " +
                                    "sotto, perché un cambio istantaneo si vede. Richiede il controller meteo di " +
                                    "Pitwall attivo in Custom Shaders Patch: senza, cambia solo la previsione."));

            var transitionRow = Row();
            transitionRow.Children.Add(new TextBlock
            {
                Text = "Transizione",
                Foreground = Muted,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0)
            });
            transitionRow.Children.Add(_weatherTransition);
            transitionRow.Children.Add(_weatherTransitionLabel);
            panel.Children.Add(transitionRow);

            var sky1 = Row();
            foreach (var choice in WeatherChoices.Take(4)) sky1.Children.Add(WeatherButton(choice));
            panel.Children.Add(sky1);

            var sky2 = Row();
            foreach (var choice in WeatherChoices.Skip(4)) sky2.Children.Add(WeatherButton(choice));
            panel.Children.Add(sky2);

            return panel;
        }

        /// <summary>Le condizioni che la direzione gara puo imporre, dalla piu asciutta alla peggiore.</summary>
        private static readonly Tuple<string, string>[] WeatherChoices =
        {
            Tuple.Create("Clear", "Sereno"),
            Tuple.Create("FewClouds", "Poche nuvole"),
            Tuple.Create("Scattered", "Nuvoloso"),
            Tuple.Create("Overcast", "Coperto"),
            Tuple.Create("LightRain", "Pioggia leggera"),
            Tuple.Create("Rain", "Pioggia"),
            Tuple.Create("HeavyRain", "Diluvio")
        };

        private void ShowTransition()
        {
            int minutes = (int)_weatherTransition.Value;
            _weatherTransitionLabel.Text = minutes == 0 ? "subito" : minutes + " min";
            _weatherTransitionLabel.Foreground = minutes == 0 ? Accent : Muted;
        }

        private Button WeatherButton(Tuple<string, string> choice)
        {
            return Button(choice.Item2, (s, e) =>
                LiveBridge.SendCommand(CommandNames.SetWeather, choice.Item1, _weatherTransition.Value));
        }

        // ---- Pagina: Telemetria -----------------------------------------------------------------------

        private UIElement BuildTelemetryPage()
        {
            var panel = new StackPanel();
            panel.Children.Add(Note("Ogni gioco, e ogni mod, riporta l'usura delle gomme a modo suo: alcuni danno la " +
                                    "vita residua, altri il consumo. Pitwall lo deduce dal primo campione della " +
                                    "sessione, quando le gomme sono nuove."));

            var row = Row();
            row.Children.Add(Label("Usura gomme"));
            _wearMode.Width = 200;
            row.Children.Add(_wearMode);
            panel.Children.Add(row);

            panel.Children.Add(Note("Come verificare: esci dai box con gomme nuove e guarda l'usura nella pagina Stato. " +
                                    "Deve partire da circa zero e salire girando. Se parte da cento e scende, metti " +
                                    "Vita residua. Se è sbagliata, l'ingegnere ti chiamerà ai box al primo giro."));
            return panel;
        }

        // ---- Pagina: Valori ----------------------------------------------------------------------------

        private UIElement BuildValuesPage()
        {
            var panel = new StackPanel();
            panel.Children.Add(Note("Tutto quello che il plugin espone, con la spiegazione e il valore in questo " +
                                    "momento. Il pulsante Copia mette negli appunti il nome già fra parentesi, pronto " +
                                    "da incollare in una dashboard."));

            var row = Row();
            row.Children.Add(Label("Cerca"));
            _search.Width = 280;
            row.Children.Add(_search);
            row.Children.Add(Button("Copia tutto l'elenco", (s, e) => CopyCatalogue()));
            row.Children.Add(_catalogueCount);
            panel.Children.Add(row);

            panel.Children.Add(new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(30, 255, 255, 255)),
                Padding = new Thickness(10),
                CornerRadius = new CornerRadius(4),
                Margin = new Thickness(0, 6, 0, 0),
                Child = _catalogue
            });

            return panel;
        }

        // ---- Pagina: Stato -------------------------------------------------------------------------------

        private UIElement BuildStatusPage()
        {
            var panel = new StackPanel();
            panel.Children.Add(Note("Quello che il plugin sta vedendo adesso. È il posto da guardare quando " +
                                    "qualcosa non torna."));
            panel.Children.Add(new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)),
                Padding = new Thickness(12),
                CornerRadius = new CornerRadius(4),
                Child = _status
            });
            return panel;
        }

        // ---- Impostazioni --------------------------------------------------------------------------------

        private void LoadFromSettings()
        {
            _loading = true;
            var settings = _plugin.Settings;

            _engineer.IsChecked = settings.EngineerEnabled;
            _speech.IsChecked = settings.SpeechEnabled;
            _raceControl.IsChecked = settings.RaceControlEnabled;
            _autoSave.IsChecked = settings.AutoSaveResults;
            _autoDriver.IsChecked = settings.AutoDetectDriver;
            _rate.Value = settings.SpeechRate;
            _volume.Value = settings.SpeechVolume;

            _language.Items.Add("it");
            _language.Items.Add("en");
            _language.SelectedItem = settings.Language == "en" ? "en" : "it";

            foreach (var item in new[] { "Auto", "Usura", "Vita residua" }) _wearMode.Items.Add(item);
            _wearMode.SelectedItem = _wearMode.Items.Cast<string>()
                .FirstOrDefault(i => string.Equals(i, settings.TyreWearMode, StringComparison.OrdinalIgnoreCase)) ?? "Auto";

            _voice.Items.Add("(automatica)");
            foreach (var name in new Runtime.RadioVoice().AvailableVoices) _voice.Items.Add(name);
            _voice.SelectedItem = string.IsNullOrEmpty(settings.SpeechVoice)
                ? "(automatica)"
                : _voice.Items.Cast<string>().FirstOrDefault(v => v == settings.SpeechVoice) ?? "(automatica)";

            _engineer.Checked += (s, e) => Apply();
            _engineer.Unchecked += (s, e) => Apply();
            _speech.Checked += (s, e) => Apply();
            _speech.Unchecked += (s, e) => Apply();
            _raceControl.Checked += (s, e) => Apply();
            _raceControl.Unchecked += (s, e) => Apply();
            _autoDriver.Checked += (s, e) => Apply();
            _autoDriver.Unchecked += (s, e) => Apply();
            _autoSave.Checked += (s, e) => Apply();
            _autoSave.Unchecked += (s, e) => Apply();
            _language.SelectionChanged += (s, e) => Apply();
            _voice.SelectionChanged += (s, e) => Apply();
            _wearMode.SelectionChanged += (s, e) => Apply();
            _rate.ValueChanged += (s, e) => Apply();
            _volume.ValueChanged += (s, e) => Apply();
            // La transizione non e un'impostazione da salvare, serve solo al prossimo pulsante.
            _weatherTransition.ValueChanged += (s, e) => ShowTransition();
            ShowTransition();
            _championship.SelectionChanged += OnChampionshipChanged;
            _search.TextChanged += (s, e) => BuildCatalogue();

            ReloadChampionships();
            _loading = false;
        }

        private void Apply()
        {
            if (_loading) return;
            var settings = _plugin.Settings;

            settings.EngineerEnabled = _engineer.IsChecked == true;
            settings.SpeechEnabled = _speech.IsChecked == true;
            settings.RaceControlEnabled = _raceControl.IsChecked == true;
            settings.AutoSaveResults = _autoSave.IsChecked == true;
            settings.AutoDetectDriver = _autoDriver.IsChecked == true;
            settings.SpeechRate = (int)_rate.Value;
            settings.SpeechVolume = (int)_volume.Value;
            settings.Language = _language.SelectedItem as string ?? "it";
            settings.TyreWearMode = _wearMode.SelectedItem as string ?? "Auto";

            string voice = _voice.SelectedItem as string;
            settings.SpeechVoice = string.IsNullOrEmpty(voice) || voice == "(automatica)" ? "" : voice;

            _plugin.ApplySettings();
        }

        private void ReloadChampionships()
        {
            bool previous = _loading;
            _loading = true;

            _summaries = ChampionshipStore.List();
            _championship.Items.Clear();
            foreach (var summary in _summaries)
                _championship.Items.Add(summary.Name + "  -  " + summary.SeasonYear +
                                        "  (" + summary.CompletedRounds + "/" + summary.Rounds + " gare)");

            string currentId = _plugin.Engine.Championship != null
                ? _plugin.Engine.Championship.Id
                : _plugin.Settings.ChampionshipId;

            int index = _summaries.FindIndex(s => s.Id == currentId);
            if (index >= 0) _championship.SelectedIndex = index;
            else if (_championship.Items.Count > 0) _championship.SelectedIndex = 0;

            _loading = previous;
        }

        private void OnChampionshipChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loading) return;
            int index = _championship.SelectedIndex;
            if (index < 0 || index >= _summaries.Count) return;

            _plugin.Settings.ChampionshipId = _summaries[index].Id;
            _plugin.LoadChampionship();
        }

        private void OpenDataFolder()
        {
            try
            {
                PitwallPaths.EnsureFolders();
                Process.Start(new ProcessStartInfo { FileName = PitwallPaths.Root, UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show("Impossibile aprire la cartella: " + ex.Message, "Pitwall");
            }
        }

        // ---- Catalogo dei valori -----------------------------------------------------------------------

        /// <summary>
        /// Costruisce l'elenco, filtrato da quello che si scrive nella casella di ricerca. Il
        /// filtro guarda nome, descrizione e gruppo insieme: cercando "safety" escono sia i
        /// valori della safety car sia le azioni che la richiamano.
        /// </summary>
        private void BuildCatalogue()
        {
            _catalogue.Children.Clear();
            _liveValues.Clear();

            string filter = (_search.Text ?? "").Trim();
            Func<string, string, string, bool> matches = (name, description, section) =>
                filter.Length == 0 ||
                name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0 ||
                description.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0 ||
                section.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0;

            string lastSection = null;
            int shown = 0;

            foreach (var p in _plugin.Properties)
            {
                if (!matches(p.Name, p.Description, p.Section)) continue;
                if (shown >= 400) break;

                if (p.Section != lastSection)
                {
                    lastSection = p.Section;
                    _catalogue.Children.Add(GroupHeader(p.Section));
                }

                _catalogue.Children.Add(PropertyRow(p));
                shown++;
            }

            int properties = shown;
            lastSection = null;

            foreach (var t in _plugin.Triggers)
            {
                if (!matches(t.Name, t.Description, t.Section)) continue;

                if (t.Section != lastSection)
                {
                    lastSection = t.Section;
                    _catalogue.Children.Add(GroupHeader(t.Section == "Eventi"
                        ? "Eventi, da agganciare a LED ed effetti"
                        : "Azioni da mappare sui pulsanti: " + t.Section));
                }

                _catalogue.Children.Add(TriggerRow(t));
                shown++;
            }

            _catalogueCount.Text = string.Format("   {0} proprietà, {1} fra azioni ed eventi",
                properties, shown - properties);

            if (shown == 0)
                _catalogue.Children.Add(new TextBlock { Text = "Nessun valore corrisponde.", Opacity = 0.7 });
        }

        private static TextBlock GroupHeader(string text) => new TextBlock
        {
            Text = text.ToUpperInvariant(),
            FontWeight = FontWeights.Bold,
            Opacity = 0.85,
            Margin = new Thickness(0, 14, 0, 5)
        };

        private UIElement PropertyRow(PitwallProperty property)
        {
            var row = new Grid { Margin = new Thickness(0, 1, 0, 1) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(330) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });

            row.Children.Add(Cell(property.FullName, 0, true, 1));
            row.Children.Add(Cell(property.Description, 1, false, 0.75));

            var value = Cell("-", 2, true, 0.95);
            row.Children.Add(value);
            _liveValues.Add(Tuple.Create(property, value));

            var copy = new Button
            {
                Content = "Copia",
                Padding = new Thickness(6, 1, 6, 1),
                Margin = new Thickness(4, 0, 0, 0)
            };
            copy.Click += (s, e) => { try { Clipboard.SetText(property.Binding); } catch (Exception) { } };
            Grid.SetColumn(copy, 3);
            row.Children.Add(copy);

            return row;
        }

        private static UIElement TriggerRow(PitwallTrigger trigger)
        {
            var row = new Grid { Margin = new Thickness(0, 1, 0, 1) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(330) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            row.Children.Add(Cell(trigger.Name, 0, true, 1));
            row.Children.Add(Cell(trigger.Description, 1, false, 0.75));
            return row;
        }

        private static TextBlock Cell(string text, int column, bool monospace, double opacity)
        {
            var block = new TextBlock
            {
                Text = text,
                Opacity = opacity,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center
            };
            if (monospace) block.FontFamily = new FontFamily("Consolas");
            Grid.SetColumn(block, column);
            return block;
        }

        /// <summary>Copia l'elenco intero come testo, comodo da tenere aperto mentre si disegna.</summary>
        private void CopyCatalogue()
        {
            var text = new StringBuilder();
            text.AppendLine("Pitwall - valori per la dashboard");

            string section = null;
            foreach (var p in _plugin.Properties)
            {
                if (p.Section != section) { section = p.Section; text.AppendLine(); text.AppendLine("== " + section); }
                text.AppendLine(string.Format("[{0}]    {1}", p.FullName, p.Description));
            }

            section = null;
            foreach (var t in _plugin.Triggers)
            {
                if (t.Section != section) { section = t.Section; text.AppendLine(); text.AppendLine("== " + section); }
                text.AppendLine(string.Format("{0}    {1}", t.Name, t.Description));
            }

            try
            {
                Clipboard.SetText(text.ToString());
                MessageBox.Show(_plugin.Properties.Count + " proprietà e " + _plugin.Triggers.Count +
                                " fra azioni ed eventi copiati negli appunti.", "Pitwall");
            }
            catch (Exception ex)
            {
                MessageBox.Show("Copia non riuscita: " + ex.Message, "Pitwall");
            }
        }

        private void RefreshCatalogueValues()
        {
            foreach (var pair in _liveValues)
                pair.Item2.Text = pair.Item1.CurrentValue();
        }

        // ---- Stato -----------------------------------------------------------------------------------------

        private void RefreshStatus()
        {
            if (_plugin.Engine.Championship == null)
            {
                _status.Text =
                    "Nessun campionato." + Environment.NewLine + Environment.NewLine +
                    "Il plugin non ne crea: aprilo in Pitwall Manager, dove stanno calendario," + Environment.NewLine +
                    "piloti e regolamento. Poi torna qui e premi Aggiorna elenco." + Environment.NewLine + Environment.NewLine +
                    "Cartella dati: " + PitwallPaths.Championships;
                return;
            }

            var state = _plugin.Engine.State;
            var lines = new List<string>
            {
                "Campionato : " + state.ChampionshipName,
                "Tu        : " + (string.IsNullOrEmpty(state.PlayerName) ? "-" : state.PlayerName) +
                                 (string.IsNullOrEmpty(state.PlayerTeam) ? "" : "  (" + state.PlayerTeam + ")") +
                                 (string.IsNullOrEmpty(state.PlayerReplaced) ? "" : "  al posto di " + state.PlayerReplaced),
                "Round      : " + state.RoundName + (state.RoundMatched ? "" : "  (pista non in calendario)"),
                "Sessione   : " + (string.IsNullOrEmpty(state.SessionLabel) ? "-" : state.SessionLabel),
                "Gioco      : " + (state.GameRunning ? "in esecuzione" : "fermo"),
                "Giro       : " + state.LapCurrent + (state.LapsTotal > 0 ? " / " + state.LapsTotal : ""),
                "Posizione  : " + (state.Position > 0 ? "P" + state.Position : "-"),
                "Dir. gara  : " + state.RaceControlText +
                                  (Math.Abs(state.Delta) > 0.001 ? "   delta " + state.Delta.ToString("+0.00;-0.00") : ""),
                "Gomme      : " + state.TyreCompound + "  usura " + state.TyreWearPercent.ToString("0.0") + "%" +
                                  "  cliff fra " + state.LapsToCliff.ToString("0.0") + " giri",
                "Strategia  : " + (string.IsNullOrEmpty(state.StrategyAdvice) ? "-" : state.StrategyAdvice),
                "Scenari    : " + (state.StrategyOptions.Count > 0
                                   ? string.Join("  ", state.StrategyOptions.Select(o => o.Id + (o.Recommended ? "*" : "")))
                                   : "-"),
                "Meteo      : " + state.WeatherNow + (state.MinutesToRain >= 0 ? "  pioggia fra " + state.MinutesToRain + " min" : ""),
                "Penalità   : " + (state.PendingPenaltySeconds > 0 ? "+" + state.PendingPenaltySeconds + "s" : "nessuna"),
                "Radio      : " + (string.IsNullOrEmpty(state.EngineerMessage) ? "-" : state.EngineerMessage)
            };

            _status.Text = string.Join(Environment.NewLine, lines);
        }

        // ---- Pezzi di interfaccia ------------------------------------------------------------------------------

        private static TextBlock Heading(string text) => new TextBlock
        {
            Text = text,
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 16, 0, 6)
        };

        private static TextBlock Note(string text) => new TextBlock
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.75,
            MaxWidth = 760,
            Margin = new Thickness(0, 0, 0, 10)
        };

        private static TextBlock Label(string text) => new TextBlock
        {
            Text = text,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0),
            Opacity = 0.75
        };

        private static StackPanel Row() => new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 4, 0, 4)
        };

        private static Button Button(string text, RoutedEventHandler onClick)
        {
            var button = new Button
            {
                Content = text,
                Padding = new Thickness(12, 4, 12, 4),
                Margin = new Thickness(8, 0, 0, 0)
            };
            button.Click += onClick;
            return button;
        }
    }
}
