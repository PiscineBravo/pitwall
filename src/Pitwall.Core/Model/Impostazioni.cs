using System;
using System.Globalization;
using System.IO;
using Pitwall.Storage;

namespace Pitwall.Model
{
    /// <summary>In che lingua parla Pitwall.</summary>
    public enum Lingua
    {
        /// <summary>Come Windows: italiano se il sistema e in italiano, inglese altrimenti.</summary>
        Automatica,
        Italiano,
        Inglese
    }

    /// <summary>
    /// Le impostazioni del programma, quelle che valgono sempre e non appartengono a un
    /// campionato.
    ///
    /// Stanno in un file solo, letto da tutti e tre i pezzi: il Manager le scrive, il plugin
    /// SimHub e le app in pista le leggono. E lo stesso modo in cui Pitwall fa comunicare le sue
    /// parti per tutto il resto - file, niente servizi e niente rete - cosi funzionano anche se
    /// uno dei tre e spento.
    /// </summary>
    public sealed class Impostazioni
    {
        /// <summary>La lingua scelta. Automatica vuol dire: quella di Windows.</summary>
        public Lingua Lingua { get; set; } = Lingua.Automatica;

        private static Impostazioni _correnti;

        /// <summary>Le impostazioni in uso adesso. Si leggono dal disco la prima volta.</summary>
        public static Impostazioni Correnti
        {
            get
            {
                if (_correnti == null) _correnti = Carica();
                return _correnti;
            }
        }

        public static Impostazioni Carica()
        {
            try
            {
                string percorso = PitwallPaths.SettingsFile;
                if (File.Exists(percorso))
                {
                    var lette = ChampionshipStore.Deserialize<Impostazioni>(File.ReadAllText(percorso));
                    if (lette != null) return lette;
                }
            }
            catch (IOException)
            {
            }
            catch (Exception)
            {
            }

            return new Impostazioni();
        }

        public void Salva()
        {
            try
            {
                PitwallPaths.EnsureFolders();
                File.WriteAllText(PitwallPaths.SettingsFile, ChampionshipStore.Serialize(this));
                _correnti = this;
            }
            catch (IOException)
            {
            }
        }

        /// <summary>
        /// La lingua davvero in uso, con l'automatico gia risolto.
        ///
        /// Si guarda la lingua dell'interfaccia di Windows, non quella dei formati: chi ha il
        /// sistema in inglese ma le date all'italiana vuole il programma in inglese.
        /// </summary>
        public Lingua LinguaEffettiva
        {
            get
            {
                if (Lingua != Lingua.Automatica) return Lingua;

                try
                {
                    string due = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
                    return string.Equals(due, "it", StringComparison.OrdinalIgnoreCase)
                        ? Lingua.Italiano
                        : Lingua.Inglese;
                }
                catch (Exception)
                {
                    return Lingua.Italiano;
                }
            }
        }
    }
}
