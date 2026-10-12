using System;
using System.Collections.Generic;

using Pitwall.Lingue;

namespace Pitwall.RaceControl
{
    /// <summary>
    /// Le fasi di una safety car, nell'ordine in cui le prevede il regolamento sportivo.
    /// </summary>
    public enum SafetyCarPhase
    {
        /// <summary>Non in pista.</summary>
        Idle,
        /// <summary>Esce dai box e si mette davanti al leader.</summary>
        Deploying,
        /// <summary>Il gruppo la raggiunge e si compatta dietro di lei.</summary>
        Bunching,
        /// <summary>I doppiati possono sdoppiarsi.</summary>
        LappedCarsUnlapping,
        /// <summary>Ultimo giro dietro la safety car.</summary>
        InThisLap,
        /// <summary>Rientra ai box, il leader comanda la ripartenza.</summary>
        Restarting
    }

    /// <summary>
    /// La vettura di sicurezza vera, non una regola astratta.
    ///
    /// In Assetto Corsa non esiste una safety car, ma una vettura in piu nella griglia si puo
    /// avere: Pitwall la inserisce come ventunesima iscritta, la tiene ai box per tutta la gara
    /// e la manda in pista solo quando serve. L'app in pista la guida con le funzioni physics.*
    /// di Custom Shaders Patch, e gli altri vengono limitati perche la seguano invece di
    /// provare a passarla.
    ///
    /// La procedura segue il regolamento: uscita, ricompattamento, sdoppiaggio dei doppiati,
    /// annuncio dell'ultimo giro, rientro ai box e ripartenza comandata dal leader.
    /// </summary>
    public static class SafetyCar
    {
        /// <summary>Nome con cui la vettura compare nella entry list e nella telemetria.</summary>
        public const string DriverName = "Safety Car";

        /// <summary>Come la chiama il commissario a bordo, per i messaggi.</summary>
        public const string MedicalCarName = "Medical Car";

        /// <summary>
        /// Auto preferite, in ordine: la prima presente fra i contenuti installati viene usata.
        /// La AMG GT Black Series e la safety car vera del campionato attuale, la SLS quella
        /// degli anni in cui la usavano.
        /// </summary>
        public static readonly IReadOnlyList<string> PreferredCarIds = new List<string>
        {
            "bg_mercedes_amg_gt_black_series",
            "ks_mercedes_amg_gt3",
            "mercedes_sls",
            "ks_bmw_m4",
            "bmw_m3_e92"
        };

        public static bool IsSafetyCar(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return false;
            string n = name.Trim();
            return n.Equals(DriverName, StringComparison.OrdinalIgnoreCase) ||
                   n.Equals(MedicalCarName, StringComparison.OrdinalIgnoreCase) ||
                   n.StartsWith("Safety Car", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Etichetta italiana della fase, per gli overlay.</summary>
        public static string Label(SafetyCarPhase phase)
        {
            switch (phase)
            {
                case SafetyCarPhase.Deploying: return Testi.T("gara.sc.pista");
                case SafetyCarPhase.Bunching: return Testi.T("gara.sc.sigla");
                case SafetyCarPhase.LappedCarsUnlapping: return Testi.T("gara.doppiati");
                case SafetyCarPhase.InThisLap: return Testi.T("gara.sc.rientra");
                case SafetyCarPhase.Restarting: return Testi.T("gara.ripartenza");
                default: return "";
            }
        }

        /// <summary>Messaggio radio della fase, quello che dice davvero il muretto.</summary>
        public static string Message(SafetyCarPhase phase)
        {
            switch (phase)
            {
                case SafetyCarPhase.Deploying:
                    return Testi.T("gara.sc.radio");
                case SafetyCarPhase.Bunching:
                    return Testi.T("gara.chiudi");
                case SafetyCarPhase.LappedCarsUnlapping:
                    return Testi.T("gara.doppiati.avviso");
                case SafetyCarPhase.InThisLap:
                    return Testi.T("gara.sc.questogiro");
                case SafetyCarPhase.Restarting:
                    return Testi.T("gara.sc.box");
                default:
                    return "";
            }
        }
    }

    /// <summary>
    /// Impostazioni della safety car reale, dentro il regolamento del campionato.
    /// </summary>
    public sealed class SafetyCarSettings
    {
        /// <summary>Inserisce la vettura di sicurezza nella griglia generata per Content Manager.</summary>
        public bool UseRealCar { get; set; } = true;

        /// <summary>Auto da usare; vuoto significa la prima disponibile fra quelle preferite.</summary>
        public string CarId { get; set; } = "";

        public string SkinId { get; set; } = "";

        /// <summary>Giri di ricompattamento prima di consentire lo sdoppiaggio.</summary>
        public int BunchingLaps { get; set; } = 1;

        /// <summary>
        /// Quanti secondi dal verde prima che una neutralizzazione sia possibile.
        ///
        /// Al via il gruppo e fermo, compatto e lentissimo: e la definizione di una partenza, ed e
        /// anche esattamente cio che il rilevatore di incidenti legge come assembramento di vetture
        /// bloccate. Senza questa attesa la safety car usciva due secondi dopo il semaforo, in ogni
        /// gara, e dietro di lei le venti vetture si infilavano tutte ai box.
        ///
        /// Non si perde niente di vero: anche in Formula 1 un incidente al via richiede qualche
        /// secondo per essere valutato, e la safety car esce dopo, non durante.
        /// </summary>
        public double StartSettleSeconds { get; set; } = 25;

        /// <summary>Consente ai doppiati di sdoppiarsi, come da regolamento.</summary>
        public bool AllowUnlapping { get; set; } = true;

        /// <summary>Giro dedicato allo sdoppiaggio.</summary>
        public int UnlappingLaps { get; set; } = 1;

        /// <summary>Velocita della safety car in pista, in km/h.</summary>
        public double PaceKmh { get; set; } = 120;

        /// <summary>Velocita nel tratto di rientro ai box.</summary>
        public double PitInKmh { get; set; } = 80;

        /// <summary>
        /// Quanto piu veloce puo andare un doppiato mentre si sdoppia, rispetto al ritmo
        /// della safety car.
        /// </summary>
        public double UnlapSpeedBonusKmh { get; set; } = 45;
    }
}
