using System.Collections.Generic;
using Pitwall.Model;

namespace Pitwall.Storage
{
    /// <summary>
    /// Porta avanti i campionati salvati quando cambiano i valori di partenza di Pitwall.
    ///
    /// Serve per una ragione che non e ovvia e che si paga cara: cambiare un valore predefinito
    /// nel codice non cambia i campionati gia salvati. Quei numeri sono stati scritti dentro il
    /// file il giorno in cui il campionato e nato, e alla rilettura vincono loro. Senza questa
    /// classe si corregge un comportamento, si prova, e non cambia niente - e si finisce a
    /// cercare il difetto nel posto sbagliato.
    ///
    /// Si tocca solo quello che e rimasto **identico** al vecchio valore di partenza, cioe quello
    /// che nessuno ha mai cambiato di proposito. Chi aveva messo un numero suo se lo tiene.
    /// </summary>
    public static class Migrazioni
    {
        // I valori con cui Pitwall e nato, e che si sono rivelati sbagliati.
        // Il ritmo della ricognizione e stato alzato due volte: 110, poi 150, e nessuno dei due
        // bastava. Il numero non era il problema: un tetto fisso tiene la vettura a giri costanti
        // e non scalda niente. Adesso l'andatura respira molto intorno a questo valore, e il
        // valore stesso e piu alto.
        private static readonly double[] VecchiRitmiRicognizione = { 110, 150 };
        private const double VecchiaUscita = 170;
        private const double VecchioLancio = 240;
        private const double VecchioRientro = 190;

        /// <summary>
        /// Aggiorna un campionato appena letto. Restituisce vero se qualcosa e cambiato, cosi
        /// chi chiama puo decidere se vale la pena riscriverlo su disco.
        /// </summary>
        public static bool Applica(Championship campionato)
        {
            if (campionato == null || campionato.Rules == null) return false;

            bool cambiato = false;

            // Il giro di ricognizione a 110 km/h teneva le vetture in seconda per tutto il giro:
            // il motore gira basso, nelle gomme non entra energia, e un giro che serve a
            // scaldarle diventa una passeggiata. Centocinquanta e il nuovo valore di partenza.
            var formazione = campionato.Rules.RaceControl != null
                ? campionato.Rules.RaceControl.Formation
                : null;

            if (formazione != null && System.Array.IndexOf(VecchiRitmiRicognizione, formazione.PaceKmh) >= 0)
            {
                formazione.PaceKmh = 170;
                cambiato = true;
            }

            // Stessa ragione per i giri di uscita dai box in prove e qualifica.
            var uscita = campionato.Rules.OutLap;
            if (uscita != null)
            {
                if (uscita.OutLapSpeedKmh == VecchiaUscita) { uscita.OutLapSpeedKmh = 205; cambiato = true; }
                if (uscita.BuildUpSpeedKmh == VecchioLancio) { uscita.BuildUpSpeedKmh = 275; cambiato = true; }
                if (uscita.InLapSpeedKmh == VecchioRientro) { uscita.InLapSpeedKmh = 200; cambiato = true; }
            }

            cambiato |= PotaLeListe(campionato);

            return cambiato;
        }

        /// <summary>
        /// Taglia le liste che sono cresciute da sole nei campionati salvati.
        ///
        /// Newtonsoft riempiva le liste invece di sostituirle, quindi ogni lista con un valore di
        /// partenza non vuoto si allungava a ogni lettura e riscrittura. La causa e corretta in
        /// ChampionshipStore, ma i file gia scritti restano lunghi: qui si riportano alla misura
        /// giusta. Si tengono i primi elementi, che sono quelli veri - la crescita avveniva in
        /// coda.
        /// </summary>
        private static bool PotaLeListe(Championship campionato)
        {
            bool cambiato = false;

            foreach (var gara in campionato.Calendar ?? new List<Round>())
            {
                foreach (var sessione in gara.Sessions ?? new List<SessionConfig>())
                {
                    cambiato |= Pota(sessione.KnockoutSegmentMinutes);
                    cambiato |= Pota(sessione.KnockoutEliminations);
                }
            }

            // Anche i punteggi, che sono la lista cresciuta piu vistosa: in un campionato aperto e
            // salvato molte volte, 25-18-15... era ripetuto circa centocinquanta volte. Non si era
            // mai notato perche si legge solo la posizione, e nessuno arriva alla millesima.
            var punti = campionato.Rules.Points;
            if (punti != null)
            {
                cambiato |= Pota(punti.Race);
                cambiato |= Pota(punti.Sprint);
            }

            return cambiato;
        }

        /// <summary>
        /// Toglie le ripetizioni da una lista che si e allungata da sola.
        ///
        /// Non si taglia a una lunghezza fissa, perche quella giusta non esiste: un punteggio puo
        /// avere dieci posizioni o quindici, e troncare a dieci rovinerebbe chi ne ha definite
        /// quindici. Si cerca invece il **periodo**: se la lista e esattamente lo stesso blocco
        /// ripetuto due o piu volte, si tiene un blocco solo.
        ///
        /// Cosi si tocca soltanto cio che e cresciuto per il difetto di lettura, e una lista scritta
        /// a mano da qualcuno resta comunque intera - a meno che non sia, per sua scelta, una
        /// ripetizione perfetta, nel qual caso il valore e lo stesso.
        /// </summary>
        private static bool Pota(List<int> lista)
        {
            if (lista == null || lista.Count < 2) return false;

            for (int periodo = 1; periodo <= lista.Count / 2; periodo++)
            {
                if (lista.Count % periodo != 0) continue;

                bool uguale = true;
                for (int i = periodo; i < lista.Count && uguale; i++)
                {
                    if (lista[i] != lista[i % periodo]) uguale = false;
                }

                if (uguale)
                {
                    lista.RemoveRange(periodo, lista.Count - periodo);
                    return true;
                }
            }

            return false;
        }
    }
}
