using Pitwall.Lingue;

namespace Pitwall.RaceControl
{
    /// <summary>Le fasi del giro di formazione, nell'ordine in cui si susseguono.</summary>
    public enum FormationPhase
    {
        /// <summary>Non in corso.</summary>
        Idle,
        /// <summary>Giro di ricognizione a ritmo ridotto, posizioni congelate.</summary>
        Rolling,
        /// <summary>Tutti riportati in griglia e tenuti fermi.</summary>
        Forming,
        /// <summary>Semaforo: le luci si accendono e poi si spengono.</summary>
        Lights,
        /// <summary>Partiti.</summary>
        Away
    }

    /// <summary>
    /// Il giro di formazione.
    ///
    /// Assetto Corsa non ce l'ha: la gara parte con le vetture gia in griglia e il semaforo
    /// verde. Pitwall lo ricostruisce in due tempi. Prima un giro a ritmo ridotto con le
    /// posizioni congelate, che e la ricognizione. Poi, quando il giro e completo, tutte le
    /// vetture vengono riportate nelle loro piazzole e tenute ferme, e da li parte un vero via
    /// da fermo.
    ///
    /// Il riporto in griglia e la parte che lo rende reale invece di un semplice primo giro
    /// lento, ed e anche quella che si puo disattivare: e un intervento forte, e su qualche
    /// pista potrebbe non funzionare.
    /// </summary>
    public sealed class FormationLapSettings
    {
        /// <summary>Velocita massima durante la ricognizione, in km/h.</summary>
        /// <summary>
        /// Il ritmo della ricognizione, in km/h.
        ///
        /// Era 110, ed era troppo poco: a quella velocita una Formula 1 sta in seconda per
        /// tutto il giro, il motore gira basso e nelle gomme non entra energia. Il giro di
        /// ricognizione serve proprio al contrario - a scaldare gomme e freni - quindi un
        /// ritmo che non le scalda lo rende una passeggiata inutile.
        ///
        /// Centocinquanta e una media, non un limite: sopra ci lavora l'ondeggiamento
        /// dell'andatura, che e quello che le scalda davvero.
        /// </summary>
        public double PaceKmh { get; set; } = 170;

        /// <summary>
        /// Riporta le vetture in griglia a fine ricognizione per una partenza da fermo. Senza,
        /// il primo giro resta semplicemente neutralizzato.
        /// </summary>
        public bool ReturnToGrid { get; set; } = true;

        /// <summary>Secondi di attesa in griglia prima che si accendano le luci.</summary>
        public double GridHoldSeconds { get; set; } = 6;

        /// <summary>
        /// Divieto di sorpasso durante la ricognizione, come da regolamento. Una posizione
        /// guadagnata non e subito una penalita: prima si viene avvisati e si ha il tempo di
        /// restituirla, che e come funziona davvero in Formula 1.
        /// </summary>
        public bool NoOvertaking { get; set; } = true;

        /// <summary>Secondi concessi per restituire una posizione prima che diventi penalita.</summary>
        public double GiveBackSeconds { get; set; } = 12;

        /// <summary>Secondi di penalita se la posizione non torna indietro.</summary>
        public double OvertakePenaltySeconds { get; set; } = 5;

        /// <summary>
        /// Il pilota si sistema da solo nella sua piazzola invece di esserci portato. E la parte
        /// che rende la procedura vera: la piazzola viene illuminata in pista e la direzione gara
        /// aspetta che la vettura sia ferma dentro, con la tolleranza qui sotto.
        /// </summary>
        public bool PlayerParksOnGrid { get; set; } = true;

        /// <summary>Margine accettato lungo la pista, in metri. Mezza vettura abbondante.</summary>
        public double GridToleranceAlong { get; set; } = 2.5;

        /// <summary>Margine accettato di lato, in metri.</summary>
        public double GridToleranceAcross { get; set; } = 1.4;

        /// <summary>
        /// Dopo quanti secondi la direzione gara procede comunque. Senza, un pilota che non
        /// trova la piazzola bloccherebbe la partenza per sempre.
        /// </summary>
        public double GridTimeoutSeconds { get; set; } = 45;

        /// <summary>
        /// Quanto si resta trattenuti nella corsia box dopo il verde, prima di potersi
        /// accodare. Serve a far sfilare il gruppo: uscire insieme a loro sarebbe pericoloso e
        /// non e quello che succede in Formula 1.
        /// </summary>
        public double PitLaneStartDelaySeconds { get; set; } = 12;

        /// <summary>Quanto passa fra l'accensione di una luce e la successiva.</summary>
        public double LightsIntervalSeconds { get; set; } = 1.0;

        /// <summary>Quante luci ha il semaforo. In Formula 1 sono cinque.</summary>
        public int LightsCount { get; set; } = 5;

        /// <summary>Secondi minimi e massimi fra l'ultima luce accesa e il verde.</summary>
        public double LightsMinSeconds { get; set; } = 0.4;

        public double LightsMaxSeconds { get; set; } = 3.0;

        /// <summary>
        /// Il giro di ricognizione consuma un giro del conteggio di Assetto Corsa. Con questa
        /// attiva, i preset generati per Content Manager ne aggiungono uno, cosi la distanza di
        /// gara resta quella voluta.
        /// </summary>
        public bool AddLapToRaceDistance { get; set; } = true;
    }

    public static class FormationLap
    {
        public static string Label(FormationPhase phase)
        {
            switch (phase)
            {
                case FormationPhase.Rolling: return Testi.T("formazione.giro");
                case FormationPhase.Forming: return Testi.T("formazione.griglia");
                case FormationPhase.Lights: return Testi.T("formazione.semaforo");
                default: return "";
            }
        }

        public static string Message(FormationPhase phase)
        {
            switch (phase)
            {
                case FormationPhase.Rolling:
                    return Testi.T("formazione.istruzioni");
                case FormationPhase.Forming:
                    return Testi.T("formazione.griglia.fermati");
                case FormationPhase.Lights:
                    return Testi.T("formazione.semaforo.acceso");
                case FormationPhase.Away:
                    return Testi.T("formazione.via");
                default:
                    return "";
            }
        }
    }
}
