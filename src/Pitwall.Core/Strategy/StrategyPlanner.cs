using System;
using System.Collections.Generic;
using System.Linq;
using Pitwall.Live;
using Pitwall.Model;
using Pitwall.Tyres;
using Pitwall.Weather;

using Pitwall.Lingue;

namespace Pitwall.Strategy
{
    /// <summary>Uno stint di una strategia proposta.</summary>
    public sealed class StrategyStint
    {
        public CompoundKind Compound { get; set; }
        public int FromLap { get; set; }
        public int ToLap { get; set; }
        public int Laps => Math.Max(0, ToLap - FromLap);

        public string Describe() => Compounds.Get(Compound).Code + " x" + Laps;
    }

    /// <summary>
    /// Uno scenario che il muretto mette sul tavolo: quante soste, con quali gomme, e cosa
    /// costa. I numeri sono stime, non promesse, e la differenza fra due scenari conta piu del
    /// valore assoluto di ciascuno.
    /// </summary>
    public sealed class StrategyOption
    {
        /// <summary>Lettera dello scenario: A, B, C, D.</summary>
        public string Id { get; set; } = "A";

        public string Name { get; set; } = "";

        /// <summary>Riga leggibile: cosa comporta, in parole.</summary>
        public string Summary { get; set; } = "";

        public List<StrategyStint> Stints { get; set; } = new List<StrategyStint>();

        public int Stops { get; set; }

        /// <summary>Giro previsto della prossima sosta, zero se non se ne prevedono.</summary>
        public int NextStopLap { get; set; }

        /// <summary>Tempo stimato per arrivare in fondo, in secondi.</summary>
        public double EstimatedSeconds { get; set; }

        /// <summary>Secondi persi rispetto allo scenario piu veloce. Zero per il migliore.</summary>
        public double DeltaSeconds { get; set; }

        /// <summary>Posizione prevista all'arrivo, stimata dai distacchi attuali.</summary>
        public int ProjectedPosition { get; set; }

        /// <summary>Rischio: 1 sicuro, 2 da tenere d'occhio, 3 scommessa.</summary>
        public int Risk { get; set; } = 1;

        public string RiskNote { get; set; } = "";

        /// <summary>Lo scenario che il muretto consiglia.</summary>
        public bool Recommended { get; set; }

        /// <summary>Lo scenario scelto dal pilota.</summary>
        public bool Selected { get; set; }

        public override string ToString() => Id + ") " + Name + " - " + Summary;
    }

    /// <summary>
    /// Il pianificatore di strategia: quello che in gara propone due, tre o quattro scenari e
    /// lascia scegliere.
    ///
    /// Il modello e semplice ma onesto: ogni giro costa il tempo sul giro di riferimento, piu
    /// il ritardo della mescola, piu quello che il degrado aggiunge mano a mano che lo stint
    /// invecchia; ogni sosta costa il transito in corsia piu la fermata. Confrontando i totali
    /// si vede quale scenario conviene e di quanto. Non e una simulazione della gara degli
    /// altri: e il calcolo che farebbe un ingegnere su un foglio, che e esattamente quello che
    /// serve per decidere.
    /// </summary>
    public sealed class StrategyPlanner
    {
        private Circuit _circuit = new Circuit();
        private SessionConfig _session = new SessionConfig();
        private double _baseLapSeconds = 95;

        public List<StrategyOption> Options { get; private set; } = new List<StrategyOption>();

        /// <summary>Lettera dello scenario scelto, vuota finche non si sceglie.</summary>
        public string SelectedId { get; private set; } = "";

        /// <summary>Lo scenario scelto, o quello consigliato se non si e scelto.</summary>
        public StrategyOption Active =>
            Options.FirstOrDefault(o => o.Selected) ??
            Options.FirstOrDefault(o => o.Recommended) ??
            Options.FirstOrDefault();

        /// <summary>Giro in cui le opzioni sono state ricalcolate l'ultima volta.</summary>
        public int LastBuiltLap { get; private set; } = -1;

        public void Reset()
        {
            Options = new List<StrategyOption>();
            SelectedId = "";
            LastBuiltLap = -1;
        }

        public void Begin(Circuit circuit, SessionConfig session)
        {
            Reset();
            _circuit = circuit ?? new Circuit();
            _session = session ?? new SessionConfig();
            _baseLapSeconds = _circuit.ReferenceLapSeconds > 5 ? _circuit.ReferenceLapSeconds : 95;
        }

        /// <summary>Sceglie uno scenario. La scelta resta finche non se ne fa un'altra.</summary>
        public bool Select(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return false;
            id = id.Trim().ToUpperInvariant();

            var option = Options.FirstOrDefault(o => o.Id == id);
            if (option == null) return false;

            SelectedId = id;
            foreach (var o in Options) o.Selected = o.Id == id;
            return true;
        }

        /// <summary>
        /// Ricostruisce gli scenari. Va chiamata a ogni giro: le condizioni cambiano, e una
        /// strategia calcolata dieci giri fa non vale piu.
        /// </summary>
        public void Rebuild(TelemetryFrame frame, StintTracker tyres, WeatherPlan weather,
                            double measuredPaceSeconds = 0)
        {
            if (frame == null || tyres == null) return;

            int totalLaps = frame.TotalLaps > 0 ? frame.TotalLaps : _session.ResolveLaps(_circuit);
            if (totalLaps <= 0) return;

            int lap = frame.CompletedLaps;
            int lapsLeft = totalLaps - lap;
            if (lapsLeft <= 1) { Options = new List<StrategyOption>(); return; }

            if (measuredPaceSeconds > 20) _baseLapSeconds = measuredPaceSeconds;

            double severity = 0.7 + 0.15 * _circuit.TyreSeverity;
            double pitLoss = _circuit.PitLaneLossSeconds + _circuit.PitStopStationarySeconds;
            double wetness = weather != null ? weather.SampleAt(frame.SessionElapsed.TotalMinutes).Wetness : 0;

            var candidates = new List<StrategyOption>();

            // ---- Restare fuori -------------------------------------------------------------
            bool mustStop = _session.MandatoryPitStops > tyres.PitStops;
            if (!mustStop && wetness < 0.1)
            {
                var stayOut = Build(Testi.T("strategia.fino.in.fondo"), new List<StrategyStint>
                {
                    new StrategyStint { Compound = tyres.Compound.Kind, FromLap = lap, ToLap = totalLaps }
                }, lap, tyres.WearPercent, severity, pitLoss, weather, frame.SessionElapsed.TotalMinutes);

                stayOut.Summary = Testi.T("strategia.nessuna.sosta");
                double endWear = tyres.WearPercent + lapsLeft * tyres.MeasuredWearPerLap;
                stayOut.Risk = endWear > 95 ? 3 : endWear > tyres.Rules.CliffWearPercent ? 2 : 1;
                stayOut.RiskNote = endWear > tyres.Rules.CliffWearPercent
                    ? Testi.T("strategia.gomme.oltre")
                    : Testi.T("strategia.gomme.entro");
                candidates.Add(stayOut);
            }

            // ---- Una sosta, per ogni mescola sensata ------------------------------------------
            foreach (var compound in new[] { Compounds.Soft, Compounds.Medium, Compounds.Hard })
            {
                if (wetness >= 0.1) break;

                double rate = compound.WearPerLapPercent * severity;
                double maxLaps = tyres.Rules.CliffWearPercent / Math.Max(0.05, rate);

                // Si entra il piu tardi possibile, purche la gomma nuova arrivi in fondo.
                int stopLap = Math.Max(lap + 1, (int)Math.Ceiling(totalLaps - maxLaps));
                stopLap = Math.Min(stopLap, totalLaps - 2);
                if (stopLap <= lap) stopLap = lap + 1;

                var stints = new List<StrategyStint>
                {
                    new StrategyStint { Compound = tyres.Compound.Kind, FromLap = lap, ToLap = stopLap },
                    new StrategyStint { Compound = compound.Kind, FromLap = stopLap, ToLap = totalLaps }
                };

                var option = Build(Testi.T("strategia.una.sosta", compound.Name), stints, lap, tyres.WearPercent, severity, pitLoss, weather, frame.SessionElapsed.TotalMinutes);
                option.Summary = Testi.T("strategia.box.giro.poi", stopLap, compound.Name.ToLowerInvariant());
                option.NextStopLap = stopLap;

                double lapsOnNew = totalLaps - stopLap;
                option.Risk = lapsOnNew > maxLaps ? 3 : lapsOnNew > maxLaps * 0.9 ? 2 : 1;
                option.RiskNote = option.Risk == 1
                    ? Testi.T("strategia.stint.comodo")
                    : Testi.T("strategia.stint.lungo");
                candidates.Add(option);
            }

            // ---- Due soste, quando la gara e lunga ---------------------------------------------
            if (lapsLeft > 30 && wetness < 0.1)
            {
                int first = lap + Math.Max(3, lapsLeft / 3);
                int second = lap + Math.Max(6, (lapsLeft * 2) / 3);

                var stints = new List<StrategyStint>
                {
                    new StrategyStint { Compound = tyres.Compound.Kind, FromLap = lap, ToLap = first },
                    new StrategyStint { Compound = CompoundKind.Soft, FromLap = first, ToLap = second },
                    new StrategyStint { Compound = CompoundKind.Soft, FromLap = second, ToLap = totalLaps }
                };

                var option = Build(Testi.T("strategia.due.soste"), stints, lap, tyres.WearPercent, severity, pitLoss, weather, frame.SessionElapsed.TotalMinutes);
                option.Summary = Testi.T("strategia.box.giri.soft", first, second);
                option.NextStopLap = first;
                option.Risk = 2;
                option.RiskNote = Testi.T("strategia.due.transiti");
                candidates.Add(option);
            }

            // ---- Pioggia -----------------------------------------------------------------------
            var rain = weather != null
                ? weather.Forecast(frame.SessionElapsed.TotalMinutes, 40).FirstOrDefault(f => f.StartsRain)
                : null;

            if (wetness >= 0.1 || rain != null)
            {
                double minutesToRain = wetness >= 0.1 ? 0 : rain.MinutesFromNow;
                int stopLap = lap + Math.Max(1, (int)Math.Round(minutesToRain * 60 / _baseLapSeconds));
                stopLap = Math.Min(stopLap, Math.Max(lap + 1, totalLaps - 1));

                // Intermedie e full wet sono due scommesse diverse, non una sola: una sbaglia se
                // piove piu del previsto, l'altra se piove meno. Vanno messe entrambe sul tavolo.
                foreach (var wet in new[] { Compounds.Intermediate, Compounds.Wet })
                {
                    var stints = new List<StrategyStint>
                    {
                        new StrategyStint { Compound = tyres.Compound.Kind, FromLap = lap, ToLap = stopLap },
                        new StrategyStint { Compound = wet.Kind, FromLap = stopLap, ToLap = totalLaps }
                    };

                    var option = Build(Testi.T("strategia.pioggia", wet.Name), stints, lap, tyres.WearPercent, severity, pitLoss, weather, frame.SessionElapsed.TotalMinutes);
                    option.Summary = stopLap <= lap + 1
                        ? Testi.T("strategia.box.subito", wet.Name.ToLowerInvariant())
                        : Testi.T("strategia.box.acqua", stopLap);
                    option.NextStopLap = stopLap;

                    bool matchesConditions = wet.Kind == Compounds.ForConditions(
                        Math.Max(wetness, rain != null ? rain.Wetness : 0.2)).Kind;
                    option.Risk = matchesConditions ? 2 : 3;
                    option.RiskNote = matchesConditions
                        ? Testi.T("strategia.gomma.giusta")
                        : (wet.Kind == CompoundKind.Wet ? Testi.T("strategia.troppa.gomma") : Testi.T("strategia.poca.gomma"));
                    candidates.Add(option);
                }

                // Restare fuori come sono: la scommessa opposta, che a volte paga. Si aggiunge
                // solo se non c'e gia uno scenario senza soste, altrimenti sarebbe lo stesso
                // piano con due nomi diversi.
                if (!candidates.Any(c => c.Stops == 0))
                {
                var gamble = Build(Testi.T("strategia.resta.fuori"), new List<StrategyStint>
                {
                    new StrategyStint { Compound = tyres.Compound.Kind, FromLap = lap, ToLap = totalLaps }
                }, lap, tyres.WearPercent, severity, pitLoss, weather, frame.SessionElapsed.TotalMinutes);

                gamble.Summary = tyres.Compound.IsWetWeather
                    ? Testi.T("strategia.niente.sosta", tyres.Compound.Name.ToLowerInvariant())
                    : Testi.T("strategia.slick.bagnato");
                gamble.Risk = tyres.Compound.IsWetWeather ? 2 : 3;
                gamble.RiskNote = tyres.Compound.IsWetWeather
                    ? Testi.T("strategia.si.perde")
                    : Testi.T("strategia.scommessa");
                candidates.Add(gamble);
                }
            }

            Finalise(candidates, frame);
        }

        /// <summary>
        /// Calcola quanto costa uno scenario: ogni giro il suo tempo, ogni sosta il suo prezzo.
        /// </summary>
        private StrategyOption Build(string name, List<StrategyStint> stints, int currentLap,
                                     double currentWear, double severity, double pitLoss,
                                     WeatherPlan weather = null, double startMinute = 0)
        {
            double total = 0;
            bool first = true;
            int lapsDone = 0;

            foreach (var stint in stints)
            {
                var spec = Compounds.Get(stint.Compound);
                double wear = first ? currentWear : 0;
                double rate = spec.WearPerLapPercent * severity;

                for (int i = 0; i < stint.Laps; i++)
                {
                    double lapTime = _baseLapSeconds + spec.LapDeltaSeconds;

                    // Degrado: lineare fino al limite, poi la gomma cede in fretta.
                    lapTime += Math.Min(wear, 78) * 0.012;
                    if (wear > 78) lapTime += (wear - 78) * spec.CliffPenaltyPerPercent;

                    // Gomma fredda appena montata.
                    if (!first && i < spec.WarmupLaps) lapTime += 0.8;

                    // La gomma sbagliata per le condizioni di quel momento. Senza questo, restare
                    // fuori con le slick sotto la pioggia risulterebbe la scelta piu veloce, che
                    // e esattamente il contrario di quello che succede in pista.
                    if (weather != null)
                    {
                        double minute = startMinute + lapsDone * _baseLapSeconds / 60.0;
                        double wet = weather.SampleAt(minute).Wetness;
                        lapTime += MismatchPenalty(spec, wet);
                    }

                    total += lapTime;
                    wear += rate;
                    lapsDone++;
                }

                if (!first) total += pitLoss;
                first = false;
            }

            return new StrategyOption
            {
                Name = name,
                Stints = stints,
                Stops = Math.Max(0, stints.Count - 1),
                EstimatedSeconds = total
            };
        }

        /// <summary>
        /// Quanto costa avere la gomma sbagliata per l'acqua che c'e in quel momento. Le slick
        /// sul bagnato perdono moltissimo; le gomme da pioggia sull'asciutto perdono parecchio e
        /// si consumano in fretta, ma restano guidabili.
        /// </summary>
        private static double MismatchPenalty(CompoundSpec spec, double wetness)
        {
            if (spec.IsWetWeather) return wetness < 0.05 ? 3.0 : 0;
            if (wetness < 0.05) return 0;
            return 2.0 + wetness * 12.0;
        }

        /// <summary>
        /// Ordina, assegna le lettere, calcola i distacchi fra scenari e stima dove si
        /// finirebbe. Oltre quattro scenari non si tengono: a quel punto non si sceglie piu.
        /// </summary>
        private void Finalise(List<StrategyOption> candidates, TelemetryFrame frame)
        {
            if (candidates.Count == 0) { Options = new List<StrategyOption>(); return; }

            var ordered = candidates.OrderBy(o => o.EstimatedSeconds).Take(4).ToList();
            double best = ordered[0].EstimatedSeconds;

            string letters = "ABCD";
            for (int i = 0; i < ordered.Count; i++)
            {
                var option = ordered[i];
                option.Id = letters[i].ToString();
                option.DeltaSeconds = Math.Round(option.EstimatedSeconds - best, 1);
                option.Recommended = i == 0;
                option.Selected = option.Id == SelectedId;
                option.ProjectedPosition = ProjectPosition(frame, option.DeltaSeconds);
            }

            // Se lo scenario scelto prima non esiste piu, la scelta decade.
            if (!ordered.Any(o => o.Selected)) SelectedId = "";

            Options = ordered;
            LastBuiltLap = frame.CompletedLaps;
        }

        /// <summary>
        /// Dove si finirebbe: si parte dalla posizione attuale e si guarda quanti avversari si
        /// scavalcano, o si fanno scavalcare, con il tempo che quello scenario costa.
        /// </summary>
        private int ProjectPosition(TelemetryFrame frame, double deltaSeconds)
        {
            int position = frame.Position > 0 ? frame.Position : 1;
            if (Math.Abs(deltaSeconds) < 0.1) return position;

            var rivals = frame.Opponents
                .Where(o => !o.IsPlayer && o.GapToPlayer.HasValue && !RaceControl.SafetyCar.IsSafetyCar(o.Name))
                .ToList();

            if (deltaSeconds > 0)
            {
                // Si perde tempo: passano quelli che stanno dietro entro quel margine.
                int lost = rivals.Count(o => o.GapToPlayer.Value > 0 && o.GapToPlayer.Value < deltaSeconds);
                return position + lost;
            }

            int gained = rivals.Count(o => o.GapToPlayer.Value < 0 && Math.Abs(o.GapToPlayer.Value) < -deltaSeconds);
            return Math.Max(1, position - gained);
        }
    }
}
