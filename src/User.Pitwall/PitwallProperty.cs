using System;

namespace User.Pitwall
{
    /// <summary>
    /// Una proprieta esposta a SimHub, con la sua descrizione.
    ///
    /// Le proprieta si dichiarano una volta sola, in un catalogo: lo stesso elenco viene
    /// registrato nel plugin e mostrato nel pannello. Cosi la documentazione non puo divergere
    /// dal codice, che e il modo piu comune in cui una lista di valori diventa inaffidabile.
    /// </summary>
    public sealed class PitwallProperty
    {
        public PitwallProperty(string section, string name, string description, Func<object> provider)
        {
            Section = section;
            Name = name;
            Description = description;
            Provider = provider;
        }

        /// <summary>Gruppo nel pannello: Sessione, Safety car, Gomme ...</summary>
        public string Section { get; }

        /// <summary>Nome come registrato nel plugin.</summary>
        public string Name { get; }

        public string Description { get; }

        public Func<object> Provider { get; }

        /// <summary>
        /// Nome completo. SimHub antepone da solo il nome della classe del plugin, quindi in
        /// dashboard la proprieta si chiama cosi.
        /// </summary>
        public string FullName => "PitwallPlugin." + Name;

        /// <summary>La forma da incollare in una dashboard.</summary>
        public string Binding => "[" + FullName + "]";

        /// <summary>Il valore adesso, gia pronto da mostrare.</summary>
        public string CurrentValue()
        {
            try
            {
                var value = Provider();
                if (value == null) return "-";
                if (value is bool b) return b ? "vero" : "falso";
                if (value is double d) return d.ToString("0.###");
                string text = value.ToString();
                return text.Length > 60 ? text.Substring(0, 57) + "..." : text;
            }
            catch (Exception)
            {
                return "errore";
            }
        }

        public override string ToString() => FullName;
    }

    /// <summary>Un'azione mappabile su un pulsante, o un evento agganciabile a un LED.</summary>
    public sealed class PitwallTrigger
    {
        public PitwallTrigger(string section, string name, string description)
        {
            Section = section;
            Name = name;
            Description = description;
        }

        public string Section { get; }
        public string Name { get; }
        public string Description { get; }

        public override string ToString() => Name;
    }
}
