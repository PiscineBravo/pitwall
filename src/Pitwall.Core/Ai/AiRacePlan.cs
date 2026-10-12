using System;
using System.Collections.Generic;
using System.Linq;
using Pitwall.Tyres;

namespace Pitwall.Ai
{
    /// <summary>Uno stint previsto: da che giro a che giro, e con quale mescola.</summary>
    public sealed class PlannedStint
    {
        public CompoundKind Compound { get; set; }
        public int StartLap { get; set; }
        public int EndLap { get; set; }
        public int Laps => Math.Max(0, EndLap - StartLap);

        public override string ToString() => Compound + " " + StartLap + "-" + EndLap;
    }

    /// <summary>
    /// Il piano gara di un avversario: strategia, forma del weekend e destino.
    ///
    /// Viene deciso una volta sola prima del via, da un seme legato al round, e poi eseguito.
    /// Cosi la gara e riproducibile: se perdi per un undercut, quell'undercut era scritto nei
    /// dati, non inventato al momento per farti perdere.
    /// </summary>
    public sealed class AiRacePlan
    {
        public string DriverName { get; set; } = "";

        public string DriverId { get; set; } = "";

        /// <summary>Stint previsti, in ordine.</summary>
        public List<PlannedStint> Stints { get; set; } = new List<PlannedStint>();

        /// <summary>
        /// Forma del weekend, in secondi al giro. Negativo vuol dire che il pilota e in palla.
        /// Serve a evitare che il campionato sia sempre lo stesso ordine d'arrivo.
        /// </summary>
        public double FormSeconds { get; set; }

        /// <summary>Giro in cui si ritira, o zero se arriva in fondo.</summary>
        public int RetirementLap { get; set; }

        /// <summary>Giro in cui commette un errore serio, o zero.</summary>
        public int MistakeLap { get; set; }

        /// <summary>Livello IA di partenza, dal pilota del campionato.</summary>
        public double BaseAiLevel { get; set; } = 95;

        public double BaseAggression { get; set; } = 50;

        // ---- Stato in corsa -------------------------------------------------------------

        /// <summary>Stint attualmente in corso, indice dentro <see cref="Stints"/>.</summary>
        public int CurrentStint { get; set; }

        public int StintStartLap { get; set; }

        /// <summary>Soste effettivamente fatte.</summary>
        public int StopsDone { get; set; }

        public bool Retired { get; set; }

        /// <summary>Vero quando la sosta e gia stata richiesta e si aspetta che rientri.</summary>
        public bool PitRequested { get; set; }

        /// <summary>Mescola richiesta, mantenuta finche la telemetria conferma la sosta.</summary>
        public string RequestedCompound { get; set; } = "";

        public CompoundKind CurrentCompound =>
            CurrentStint < Stints.Count ? Stints[CurrentStint].Compound : CompoundKind.Medium;

        public CompoundKind NextCompound =>
            CurrentStint + 1 < Stints.Count ? Stints[CurrentStint + 1].Compound : CompoundKind.Medium;

        /// <summary>Giro previsto per la prossima sosta, o zero se non ne restano.</summary>
        public int NextStopLap =>
            CurrentStint < Stints.Count && CurrentStint + 1 < Stints.Count
                ? Stints[CurrentStint].EndLap
                : 0;

        public override string ToString() =>
            DriverName + ": " + string.Join(" / ", Stints.Select(s => s.ToString()));
    }

    /// <summary>
    /// Quello che il direttore chiede a una singola vettura in questo istante. Viene tradotto
    /// dall'app in pista nelle chiamate a physics.* di Custom Shaders Patch.
    /// </summary>
    public sealed class AiCommand
    {
        public string Name { get; set; } = "";

        /// <summary>Livello IA da applicare adesso, 70-100.</summary>
        public double AiLevel { get; set; } = 95;

        public double Aggression { get; set; } = 50;

        /// <summary>Limite di velocita in km/h. Zero significa nessun limite.</summary>
        public double TopSpeedKmh { get; set; }

        /// <summary>Chiede il rientro ai box a questo giro.</summary>
        public bool Pit { get; set; }

        /// <summary>Mescola da montare alla sosta, come codice: S, M, H, I, W.</summary>
        public string Compound { get; set; } = "";

        /// <summary>Scostamento laterale dalla traiettoria, -1 tutto a sinistra, 1 tutto a destra.</summary>
        public double SplineOffset { get; set; }

        /// <summary>Secondi di arresto forzato, per errori e ritiri.</summary>
        public double StopSeconds { get; set; }

        /// <summary>Vero per la vettura che fa da safety car.</summary>
        public bool IsSafetyCar { get; set; }

        /// <summary>Fase della safety car, valorizzata solo per la vettura di sicurezza.</summary>
        public string Phase { get; set; } = "";

        /// <summary>
        /// Vero quando la vettura sta scaldando le gomme invece di spingere: giro di uscita dai
        /// box, oppure ricognizione. L'app in pista la fa zigzagare.
        /// </summary>
        public bool Warmup { get; set; }

        /// <summary>Cosa sta facendo: Out, Lanciato, Rientro. Vuoto in gara.</summary>
        public string LapKind { get; set; } = "";

        /// <summary>
        /// Vero quando la vettura deve restare ferma ai box perche il suo turno non e ancora
        /// arrivato. E cosi che la pista resta libera invece di avere venti vetture in giro
        /// tutte insieme per un'ora.
        /// </summary>
        public bool HoldInPit { get; set; }

        /// <summary>
        /// Vero quando a questa vettura il DRS e concesso adesso. E un permesso, non un
        /// comando: l'ala si apre comunque solo dentro le zone della pista, e solo se la
        /// vettura ce l'ha. Falso significa ala chiusa anche in zona.
        /// </summary>
        public bool DrsAllowed { get; set; } = true;

        /// <summary>
        /// Per quanti giri va rifornita questa vettura. Zero vuol dire non toccare la
        /// benzina. I litri li calcola l'app in pista: il consumo e la capienza del
        /// serbatoio li sa solo lei.
        /// </summary>
        public double FuelLaps { get; set; }

        /// <summary>Una riga compatta, per il file che legge l'app in pista.</summary>
        public string Serialise()
        {
            var c = System.Globalization.CultureInfo.InvariantCulture;
            return string.Join("|",
                Name.Replace('|', ' '),
                AiLevel.ToString("0.#", c),
                Aggression.ToString("0.#", c),
                TopSpeedKmh.ToString("0.#", c),
                Pit ? "1" : "0",
                Compound,
                SplineOffset.ToString("0.##", c),
                StopSeconds.ToString("0.#", c),
                IsSafetyCar ? "1" : "0",
                Phase,
                Warmup ? "1" : "0",
                LapKind,
                HoldInPit ? "1" : "0",
                DrsAllowed ? "1" : "0",
                FuelLaps.ToString("0.##", c));
        }
    }
}
