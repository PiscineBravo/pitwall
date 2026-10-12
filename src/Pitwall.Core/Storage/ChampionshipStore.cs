using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Pitwall.Model;

namespace Pitwall.Storage
{
    /// <summary>A championship file as seen from the outside, without loading the whole thing.</summary>
    public sealed class ChampionshipSummary
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public int SeasonYear { get; set; }
        public int Rounds { get; set; }
        public int CompletedRounds { get; set; }
        public int Drivers { get; set; }
        public DateTime ModifiedUtc { get; set; }
        public string FilePath { get; set; } = "";

        public override string ToString() => Name + " (" + SeasonYear + ")";
    }

    /// <summary>Reads and writes championships as plain JSON files, one per championship.</summary>
    public static class ChampionshipStore
    {
        private static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
        {
            Formatting = Formatting.Indented,
            NullValueHandling = NullValueHandling.Ignore,
            DateTimeZoneHandling = DateTimeZoneHandling.Utc,

            // Le liste si SOSTITUISCONO, non si riempiono.
            //
            // Senza questa riga Newtonsoft fa una cosa che non ci si aspetta: se una proprieta e
            // una lista gia creata dal suo valore di partenza, alla rilettura ci **aggiunge** gli
            // elementi salvati invece di rimpiazzarla. Ogni lista con un valore di partenza non
            // vuoto cresce quindi a ogni giro di lettura e riscrittura, per sempre.
            //
            // Non e teoria: KnockoutSegmentMinutes, che parte da { 18, 15, 12 }, era arrivata a
            // trecento voci - [18,15,12,18,15,12,...] - in un campionato aperto e salvato molte
            // volte. Nessun sintomo visibile, perche si leggono solo i primi tre numeri, e il file
            // intanto cresceva senza limite. Se un giorno qualcuno leggesse quella lista per
            // intero, troverebbe una qualifica di novanta manche.
            ObjectCreationHandling = ObjectCreationHandling.Replace,

            Converters = new List<JsonConverter> { new StringEnumConverter() }
        };

        public static string Serialize(object value) => JsonConvert.SerializeObject(value, Settings);

        public static T Deserialize<T>(string json) => JsonConvert.DeserializeObject<T>(json, Settings);

        public static void Save(Championship championship)
        {
            if (championship == null) return;
            PitwallPaths.EnsureFolders();
            championship.ModifiedUtc = DateTime.UtcNow;

            string path = PitwallPaths.ChampionshipFile(championship.Id);
            string temp = path + ".tmp";
            File.WriteAllText(temp, Serialize(championship));

            // Write to a temporary file first: a crash halfway through a save must not destroy
            // a season that took weeks to race.
            if (File.Exists(path)) File.Delete(path);
            File.Move(temp, path);
        }

        public static Championship Load(string id)
        {
            string path = PitwallPaths.ChampionshipFile(id);
            return LoadFile(path);
        }

        public static Championship LoadFile(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
            try
            {
                var campionato = Deserialize<Championship>(File.ReadAllText(path));
                if (campionato == null) return null;

                // Un campionato salvato porta dentro i valori del giorno in cui e nato, e alla
                // rilettura quelli vincono su qualunque valore di partenza nuovo. Qui si
                // aggiorna quello che nessuno ha mai cambiato di proposito, e si riscrive
                // subito: altrimenti la correzione varrebbe per questa sessione e si
                // perderebbe alla prossima, che e il modo migliore per cercare un difetto
                // inesistente.
                if (Migrazioni.Applica(campionato))
                {
                    try { Save(campionato); } catch (IOException) { }
                }

                return campionato;
            }
            catch (Exception)
            {
                return null;
            }
        }

        public static bool Delete(string id)
        {
            string path = PitwallPaths.ChampionshipFile(id);
            if (!File.Exists(path)) return false;
            File.Delete(path);
            return true;
        }

        /// <summary>Lists every championship in the data folder, newest first.</summary>
        public static List<ChampionshipSummary> List()
        {
            PitwallPaths.EnsureFolders();
            var result = new List<ChampionshipSummary>();

            foreach (var file in Directory.GetFiles(PitwallPaths.Championships, "*.json"))
            {
                var championship = LoadFile(file);
                if (championship == null) continue;
                result.Add(new ChampionshipSummary
                {
                    Id = championship.Id,
                    Name = championship.Name,
                    SeasonYear = championship.SeasonYear,
                    Rounds = championship.Calendar.Count,
                    CompletedRounds = championship.Calendar.Count(r => r.Status == RoundStatus.Completed),
                    Drivers = championship.Drivers.Count,
                    ModifiedUtc = championship.ModifiedUtc,
                    FilePath = file
                });
            }

            return result.OrderByDescending(s => s.ModifiedUtc).ToList();
        }

        // ---- Circuit library ----------------------------------------------------------------

        public static CircuitLibrary LoadCircuitLibrary()
        {
            PitwallPaths.EnsureFolders();
            if (!File.Exists(PitwallPaths.CircuitLibraryFile))
            {
                var seeded = new CircuitLibrary { Circuits = SeedData.Circuits() };
                SaveCircuitLibrary(seeded);
                return seeded;
            }

            try
            {
                return Deserialize<CircuitLibrary>(File.ReadAllText(PitwallPaths.CircuitLibraryFile))
                       ?? new CircuitLibrary { Circuits = SeedData.Circuits() };
            }
            catch (Exception)
            {
                return new CircuitLibrary { Circuits = SeedData.Circuits() };
            }
        }

        public static void SaveCircuitLibrary(CircuitLibrary library)
        {
            PitwallPaths.EnsureFolders();
            File.WriteAllText(PitwallPaths.CircuitLibraryFile, Serialize(library));
        }
    }
}
