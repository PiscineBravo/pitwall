using System;
using System.Collections.Generic;
using System.Globalization;
using Pitwall.Model;

namespace Pitwall.Lingue
{
    /// <summary>
    /// I testi di Pitwall nelle due lingue.
    ///
    /// Funziona per chiavi: nel codice si scrive <c>Testi.T("gara.verde")</c> e qui dentro c'e
    /// la frase in italiano e in inglese. Le chiavi sono in italiano perche in italiano e
    /// scritto tutto il resto del programma, e due convenzioni diverse nello stesso file le si
    /// sbaglia.
    ///
    /// Una chiave che non esiste torna indietro com'e, invece di sparire o far saltare il
    /// programma: un testo sbagliato si vede e si corregge, un programma che si pianta mentre
    /// sei in pista no.
    /// </summary>
    public static class Testi
    {
        private static readonly Dictionary<string, string[]> Dizionario =
            new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);

        private static Lingua _lingua = Lingua.Automatica;

        /// <summary>La lingua in uso, con l'automatico gia risolto.</summary>
        public static Lingua Lingua
        {
            get => _lingua == Lingua.Automatica ? DaSistema() : _lingua;
            set => _lingua = value;
        }

        /// <summary>Vero quando si sta parlando inglese.</summary>
        public static bool Inglese => Lingua == Lingua.Inglese;

        /// <summary>Il codice breve della lingua, da pubblicare per le app in pista.</summary>
        public static string Codice => Inglese ? "en" : "it";

        static Testi()
        {
            foreach (var voce in Dizionari.Tutte()) Dizionario[voce.Key] = voce.Value;
        }

        /// <summary>Prende la lingua dalle impostazioni salvate.</summary>
        public static void Applica(Impostazioni impostazioni)
        {
            _lingua = impostazioni != null ? impostazioni.Lingua : Lingua.Automatica;
        }

        private static Lingua DaSistema()
        {
            try
            {
                return string.Equals(CultureInfo.CurrentUICulture.TwoLetterISOLanguageName, "it",
                                     StringComparison.OrdinalIgnoreCase)
                    ? Lingua.Italiano
                    : Lingua.Inglese;
            }
            catch (Exception)
            {
                return Lingua.Italiano;
            }
        }

        /// <summary>Il testo di una chiave nella lingua in uso.</summary>
        public static string T(string chiave)
        {
            if (string.IsNullOrEmpty(chiave)) return "";

            string[] coppia;
            if (!Dizionario.TryGetValue(chiave, out coppia)) return chiave;

            string testo = Inglese ? coppia[1] : coppia[0];
            return string.IsNullOrEmpty(testo) ? coppia[0] : testo;
        }

        /// <summary>
        /// Il testo di una chiave con dei valori da inserire, come "Box al giro {0}".
        ///
        /// Se la frase e i valori non si incastrano - un segnaposto di troppo, uno di meno - si
        /// torna la frase cosi com'e invece di far saltare tutto: in mezzo a una gara un testo
        /// storto e un fastidio, un errore e un disastro.
        /// </summary>
        public static string T(string chiave, params object[] valori)
        {
            string testo = T(chiave);
            if (valori == null || valori.Length == 0) return testo;

            try
            {
                return string.Format(CultureInfo.InvariantCulture, testo, valori);
            }
            catch (FormatException)
            {
                return testo;
            }
        }

        /// <summary>Quante frasi conosce. Serve ai controlli.</summary>
        public static int Quante => Dizionario.Count;

        /// <summary>Le chiavi senza traduzione inglese. Serve ai controlli.</summary>
        public static List<string> SenzaInglese()
        {
            var mancanti = new List<string>();
            foreach (var voce in Dizionario)
                if (string.IsNullOrEmpty(voce.Value[1])) mancanti.Add(voce.Key);
            return mancanti;
        }
    }
}
