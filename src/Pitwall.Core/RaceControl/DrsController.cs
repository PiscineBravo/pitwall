using Pitwall.Rules;

namespace Pitwall.RaceControl
{
    /// <summary>
    /// Quando il DRS e concesso in gara.
    ///
    /// Assetto Corsa da se non applica nessuna regola: dentro le zone del tracciato l'ala si
    /// apre sempre, dal primo giro e senza guardare chi si ha davanti. In Formula 1 invece il
    /// DRS e bloccato fino all'inizio del terzo giro - due giri di gara completati dal leader -
    /// e resta chiuso quando la gara non e in bandiera verde.
    ///
    /// Qui c'e solo questa meta della regola, quella che si decide guardando la gara nel suo
    /// insieme. L'altra meta - il secondo di distacco al punto di rilevamento - la applica
    /// l'app in pista, perche i punti di rilevamento stanno nei dati del tracciato e i distacchi
    /// si misurano fotogramma per fotogramma.
    /// </summary>
    public sealed class DrsController
    {
        private DrsRules _rules = new DrsRules();

        /// <summary>Vero quando la direzione gara concede il DRS adesso.</summary>
        public bool EnabledNow { get; private set; }

        /// <summary>Perche non e concesso, da mostrare a muretto e HUD. Vuoto se e concesso.</summary>
        public string Reason { get; private set; } = "";

        /// <summary>Giri che mancano all'abilitazione. Zero quando e gia abilitato.</summary>
        public int LapsToEnable { get; private set; }

        public void Begin(DrsRules rules)
        {
            _rules = rules ?? new DrsRules();
            EnabledNow = false;
            Reason = "";
            LapsToEnable = 0;
        }

        /// <summary>
        /// Aggiorna il permesso. <paramref name="leaderLaps"/> sono i giri completati dal
        /// leader: e su quelli che la direzione gara annuncia "DRS abilitato", non sui giri di
        /// ciascuno.
        /// </summary>
        public void Update(bool isRace, int leaderLaps, RaceControlStatus status)
        {
            if (!_rules.Enabled)
            {
                // Regola spenta: il DRS torna come lo lascia Assetto Corsa, libero in zona.
                EnabledNow = true;
                Reason = "";
                LapsToEnable = 0;
                return;
            }

            if (!isRace)
            {
                // Fuori dalla gara il regolamento non chiede niente: in zona il DRS e libero.
                // Che le IA lo usino solo nel giro buono e una scelta di comportamento, e si
                // decide altrove, vettura per vettura.
                EnabledNow = true;
                Reason = "";
                LapsToEnable = 0;
                return;
            }

            // "Dal terzo giro" vuol dire con due giri completati: al terzo passaggio sul
            // traguardo il leader comincia il giro in cui il DRS si apre.
            int needed = System.Math.Max(1, _rules.EnabledFromLap) - 1;
            LapsToEnable = System.Math.Max(0, needed - leaderLaps);

            if (LapsToEnable > 0)
            {
                EnabledNow = false;
                Reason = "DRS dal giro " + System.Math.Max(1, _rules.EnabledFromLap);
                return;
            }

            if (_rules.SuspendUnderYellow && Suspended(status))
            {
                EnabledNow = false;
                Reason = "DRS sospeso";
                return;
            }

            EnabledNow = true;
            Reason = "";
        }

        /// <summary>
        /// Quando la gara non e in verde il DRS non si apre.
        ///
        /// Il giallo locale resta fuori da questo elenco di proposito. In Formula 1 il DRS si
        /// disabilita nel settore con la bandiera gialla, non su tutto il giro, e per farlo
        /// davvero servirebbe sapere in che settore cade ogni zona. Spegnerlo su tutto il
        /// tracciato per un giallo in un punto lontano sarebbe piu sbagliato che lasciarlo.
        /// </summary>
        private static bool Suspended(RaceControlStatus status)
        {
            switch (status)
            {
                case RaceControlStatus.SafetyCar:
                case RaceControlStatus.SafetyCarEnding:
                case RaceControlStatus.VirtualSafetyCar:
                case RaceControlStatus.RedFlag:
                case RaceControlStatus.FormationLap:
                case RaceControlStatus.Checkered:
                case RaceControlStatus.Inactive:
                    return true;
                default:
                    return false;
            }
        }
    }
}
