using System;
using System.ComponentModel;
using System.Windows.Data;
using System.Windows.Markup;
using Pitwall.Lingue;

namespace Pitwall.Manager
{
    /// <summary>
    /// Il ponte fra il vocabolario e l'interfaccia.
    ///
    /// Si usa come indice - <c>Traduzioni.Corrente["scheda.piloti"]</c> - e serve perche scritto
    /// cosi WPF lo puo collegare a un'etichetta. Quando la lingua cambia basta chiamare
    /// <see cref="Aggiorna"/> e tutte le scritte della finestra si riscrivono da sole, senza
    /// riaprire niente: dire a qualcuno "cambia lingua e riavvia" e un modo per far sembrare
    /// incompiuto un programma che non lo e.
    /// </summary>
    public sealed class Traduzioni : INotifyPropertyChanged
    {
        public static Traduzioni Corrente { get; } = new Traduzioni();

        private Traduzioni()
        {
        }

        public string this[string chiave] => Testi.T(chiave);

        public event PropertyChangedEventHandler PropertyChanged;

        /// <summary>Avvisa l'interfaccia che tutte le scritte vanno rilette.</summary>
        public void Aggiorna() =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
    }

    /// <summary>
    /// Scrive una frase tradotta dentro un file dell'interfaccia: <c>Text="{l:T scheda.piloti}"</c>.
    ///
    /// Non restituisce il testo e basta: restituisce un collegamento al vocabolario. La
    /// differenza si vede quando si cambia lingua con il programma aperto - con il testo secco
    /// resterebbe quello di prima fino alla riapertura, con il collegamento cambia subito.
    /// </summary>
    [MarkupExtensionReturnType(typeof(string))]
    public sealed class TExtension : MarkupExtension
    {
        public string Chiave { get; set; }

        public TExtension()
        {
        }

        public TExtension(string chiave)
        {
            Chiave = chiave;
        }

        public override object ProvideValue(IServiceProvider servizi)
        {
            if (string.IsNullOrWhiteSpace(Chiave)) return "";

            var collegamento = new Binding("[" + Chiave + "]")
            {
                Source = Traduzioni.Corrente,
                Mode = BindingMode.OneWay
            };

            return collegamento.ProvideValue(servizi);
        }
    }
}
