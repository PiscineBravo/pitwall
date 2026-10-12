using System.Collections.Generic;

namespace Pitwall.Lingue
{
    /// <summary>
    /// Il vocabolario di Pitwall: ogni frase in italiano e in inglese.
    ///
    /// Una riga per frase, nell'ordine chiave - italiano - inglese. Scritto cosi invece che in
    /// file separati per lingua perche le due versioni si leggono una accanto all'altra: quando
    /// si cambia una frase si vede subito l'altra, e non si finisce con l'inglese che dice una
    /// cosa diversa dall'italiano senza che nessuno se ne accorga.
    ///
    /// Le chiavi sono raggruppate per argomento. Quelle della direzione gara in maiuscolo sono
    /// cartelli che si leggono di sfuggita mentre si guida: restano corti in tutte e due le
    /// lingue, anche a costo di non essere una traduzione letterale.
    /// </summary>
    internal static class Dizionari
    {
        internal static IEnumerable<KeyValuePair<string, string[]>> Tutte()
        {
            foreach (var voce in Blocco(DirezioneGara)) yield return voce;
            foreach (var voce in Blocco(Sessioni)) yield return voce;
            foreach (var voce in Blocco(Interfaccia)) yield return voce;
            foreach (var voce in Blocco(Finestre)) yield return voce;
            foreach (var voce in Blocco(Meteo)) yield return voce;
            foreach (var voce in Blocco(Strategia)) yield return voce;
        }

        /// <summary>Spezza l'elenco piatto in chiave, italiano e inglese.</summary>
        private static IEnumerable<KeyValuePair<string, string[]>> Blocco(string[] righe)
        {
            for (int i = 0; i + 2 < righe.Length; i += 3)
                yield return new KeyValuePair<string, string[]>(
                    righe[i], new[] { righe[i + 1], righe[i + 2] });
        }

        // =============================================================================
        // Direzione gara: cartelli, bandiere, penalita, procedura di partenza.
        // =============================================================================
        private static readonly string[] DirezioneGara =
        {
            "gara.verde",              "VERDE",                        "GREEN",
            "gara.verde.annuncio",     "VERDE, VERDE, VERDE",          "GREEN, GREEN, GREEN",
            "gara.pista.libera",       "Pista libera, bandiera verde",  "Track clear, green flag",
            "gara.scacchi",            "BANDIERA A SCACCHI",            "CHEQUERED FLAG",
            "gara.rossa",              "BANDIERA ROSSA",                "RED FLAG",
            "gara.giallo.settore",     "GIALLO S",                      "YELLOW S",
            "gara.giallo.esposta",     "Bandiera gialla settore {0}",   "Yellow flag in sector {0}",
            "gara.giallo.anche",       "Bandiera gialla anche nel settore {0}", "Yellow flag also in sector {0}",
            "gara.sc.pista",           "SAFETY CAR IN PISTA",           "SAFETY CAR DEPLOYED",
            "gara.sc.rientra",         "SAFETY CAR RIENTRA QUESTO GIRO", "SAFETY CAR IN THIS LAP",
            "gara.sc.box",             "Safety car ai box. Il leader comanda la ripartenza.",
                                       "Safety car in the pits. The leader controls the restart.",
            "gara.ripartenza",         "RIPARTENZA",                    "RESTART",
            "gara.doppiati",           "DOPPIATI: SDOPPIARSI",          "LAPPED CARS MAY UNLAP",
            "gara.doppiati.avviso",    "I doppiati possono sdoppiarsi.", "Lapped cars may now overtake.",
            "gara.chiudi",             "Chiudi sulla vettura davanti, niente sorpassi.",
                                       "Close up on the car ahead, no overtaking.",
            "gara.sessione.avviata",   "Sessione avviata",              "Session started",

            "formazione.giro",         "GIRO DI FORMAZIONE",            "FORMATION LAP",
            "formazione.istruzioni",   "Giro di formazione. Scalda gomme e freni, mantieni la posizione.",
                                       "Formation lap. Warm up tyres and brakes, hold position.",
            "formazione.griglia",      "IN GRIGLIA",                    "ON THE GRID",
            "formazione.griglia.fermati", "In griglia. Fermati nella tua piazzola.",
                                       "On the grid. Stop in your slot.",
            "formazione.piazzola",     "Portati nella tua piazzola e fermati.",
                                       "Get to your grid slot and stop.",
            "formazione.piazzola.distanza", "Portati nella piazzola {0}: {1:0.0} m",
                                       "Move to slot {0}: {1:0.0} m",
            "formazione.semaforo",     "SEMAFORO",                      "LIGHTS",
            "formazione.semaforo.acceso", "Semaforo acceso.",           "Lights on.",
            "formazione.corsia.box",   "Non hai preso la tua posizione: partirai dalla corsia box.",
                                       "You did not take your grid slot: you will start from the pit lane.",
            "formazione.corsia.avviso", "Partenza dalla corsia box: piazzola non occupata in tempo.",
                                       "Pit lane start: grid slot not taken in time.",
            "formazione.sorpasso",     "Sorpasso in ricognizione: restituisci la posizione, sei {0}",
                                       "Overtake on the formation lap: give the position back, you are {0}",
            "formazione.restituita",   "Posizione restituita, grazie.", "Position given back, thank you.",

            "penalita",                "Penalità",                      "Penalty",
            "penalita.secondi",        "Penalità {0} secondi: {1}",     "{0} second penalty: {1}",
            "penalita.delta",          "delta non rispettato",          "delta not respected",
            "penalita.sorpasso",       "sorpasso sotto neutralizzazione", "overtaking under neutralisation",
            "penalita.pitlane.chiusa", "Pit lane chiusa: penalità 10 secondi",
                                       "Pit lane closed: 10 second penalty",
            "penalita.sosta.corta",    "Sosta più corta del minimo consentito",
                                       "Pit stop shorter than the minimum allowed",
            "penalita.velocita.box",   "Velocità in pit lane",          "Pit lane speeding",
            "penalita.track.limits",   "Track limits",                  "Track limits",
            "penalita.giro.cancellato", "Giro cancellato: track limits.", "Lap deleted: track limits.",
            "incidente",               "Incidente",                     "Incident",
            "incidente.grave.radio",   "Incidente grave: {0}",          "Serious incident: {0}",
            "gara.vsc.sigla",          "VSC",                           "VSC",
            "gara.vsc.fine",           "VSC IN CONCLUSIONE",            "VSC ENDING",
            "gara.vsc.continua",       "VSC confermata: pericolo in pista", "VSC maintained: hazard on track",
            "gara.sc.sigla",           "SAFETY CAR",                    "SAFETY CAR",
            "gara.vsc.motivo",         "VIRTUAL SAFETY CAR - {0}",      "VIRTUAL SAFETY CAR - {0}",
            "gara.sc.motivo",          "SAFETY CAR IN PISTA - {0}",     "SAFETY CAR DEPLOYED - {0}",
            "gara.rossa.motivo",       "BANDIERA ROSSA - {0}",          "RED FLAG - {0}",
            "gara.sc.radio",           "Safety car, safety car. Rallenta e mantieni la posizione.",
                                       "Safety car, safety car. Slow down and hold position.",
            "gara.sc.questogiro",      "Safety car rientra questo giro. Preparati.",
                                       "Safety car in this lap. Get ready.",
            "gara.sc.rientra.avviso",  "Safety car rientra ai box questo giro",
                                       "Safety car returning to the pits this lap",
            "gara.scacchi.radio",      "Bandiera a scacchi",            "Chequered flag",
            "formazione.via",          "Via!",                          "Go!",
            "formazione.sorpasso.penalita", "Penalità {0} secondi: posizione non restituita in ricognizione",
                                       "{0} second penalty: position not given back on the formation lap",
            "formazione.sorpasso.motivo", "Sorpasso nel giro di formazione",
                                       "Overtaking on the formation lap",
            "penalita.sorpasso.motivo", "Sorpasso in regime di neutralizzazione",
                                       "Overtaking under neutralisation",
            "penalita.delta.motivo",   "Delta non rispettato in regime di {0}",
                                       "Delta not respected under {0}",
            "penalita.track.limits.conta", "Track limits: {0}/{1}",     "Track limits: {0}/{1}",
            "penalita.track.limits.secondi", "Penalità {0} secondi: track limits",
                                       "{0} second penalty: track limits",
            "penalita.richiamo.testo", "Richiamo: {0}",                 "Warning: {0}",
            "penalita.velocita.box.testo", "Penalità: velocità in pit lane",
                                       "Penalty: pit lane speeding",
            "penalita.sosta.breve",    "Penalità: sosta troppo breve",  "Penalty: pit stop too short",
            "penalita.box.chiusa",     "Ingresso ai box con pit lane chiusa",
                                       "Entering the pits with the pit lane closed",
            "penalita.griglia.mancata", "Mancata presa della posizione in griglia",
                                       "Failure to take the grid slot",
        };

        // =============================================================================
        // Nomi delle sessioni e del fine settimana.
        // =============================================================================
        private static readonly string[] Sessioni =
        {
            "sessione.prove1",         "Prove libere 1",                "Practice 1",
            "sessione.prove2",         "Prove libere 2",                "Practice 2",
            "sessione.prove3",         "Prove libere 3",                "Practice 3",
            "sessione.qualifica",      "Qualifiche",                    "Qualifying",
            "sessione.qualifica.sprint", "Qualifiche sprint",           "Sprint qualifying",
            "sessione.sprint",         "Sprint",                        "Sprint",
            "sessione.gara",           "Gara",                          "Race",
            "sessione.giri",           "{0} giri",                      "{0} laps",
            "sessione.minuti",         "{0} min",                       "{0} min",
            "sessione.distanza",       "{0}% distanza",                 "{0}% distance",
            "qualifica.chiusa",        "{0} chiusa: eliminati {1}",     "{0} over: eliminated {1}",
            "qualifica.conclusa",      "Qualifiche concluse.",          "Qualifying over.",
            "qualifica.chiusa.nessuno", "{0} chiusa.",                  "{0} over.",

            // Una gara alla volta in Content Manager, e il conto lo tiene il Manager.
            "risultato.gia.registrato", "{0} e gia stata corsa: il risultato non si sovrascrive.",
                                       "{0} has already been run: the result is not overwritten.",
            "cm.prossima.gara",        "In Content Manager ora c'e: {0}.",
                                       "Content Manager now shows: {0}.",
            "cm.campionato.concluso",  "Campionato concluso: nessuna gara da correre.",
                                       "Championship over: no race left to run.",
            "cm.aggiornamento.fallito", "Content Manager non aggiornato: {0}",
                                       "Content Manager not updated: {0}",
        };

        // =============================================================================
        // Interfaccia del Manager.
        // =============================================================================
        private static readonly string[] Interfaccia =
        {

            "scheda.piloti",           "Piloti",                        "Drivers",




        };
        // =============================================================================
        // Le scritte delle finestre del Manager.
        //
        // Estratte dai file dell'interfaccia e tradotte una per una. Le intestazioni delle
        // colonne restano corte anche in inglese: una tabella con le intestazioni che vanno a
        // capo si legge peggio di una con qualche parola abbreviata.
        // =============================================================================
        private static readonly string[] Finestre =
        {
            "ui.2.mescole", "2 mescole", "Two compounds",
            "ui.abilita.ia", "Abilita IA", "AI skill",
            "ui.abilita.script.di.physics", "Abilita script di physics", "Enable physics scripts",
            "ui.aggiorna", "Aggiorna", "Refresh",
            "ui.aggiorna.elenco", "Aggiorna elenco", "Refresh list",
            "ui.aggiungi.al.calendario", "Aggiungi al calendario", "Add to calendar",
            "ui.aggiungi.al.campionato", "Aggiungi al campionato", "Add to championship",
            "ui.aggiungi.pilota", "Aggiungi pilota", "Add driver",
            "ui.aggiungi.team", "Aggiungi team", "Add team",
            "ui.aggressivita", "Aggressivita", "Aggression",
            "ui.al.posto.di", "Al posto di", "Replacing",
            "ui.apri.la.cartella.dati", "Apri la cartella dati", "Open the data folder",
            "ui.assetto.corsa", "Assetto Corsa", "Assetto Corsa",
            "ui.attiva", "Attiva", "Enable",
            "ui.attiva.meteo.dinamico", "Attiva meteo dinamico", "Enable dynamic weather",
            "ui.attivo", "Attivo", "Active",
            "ui.auto.ac", "Auto (AC)", "Car (AC)",
            "ui.auto.indica.la.cartella.del.mod", "Auto indica la cartella del mod in content/cars. Le livree sono le cartelle dentro skins, una per vettura.", "Car is the mod folder inside content/cars. Liveries are the folders inside skins, one per car.",
            "ui.avvertimenti.consentiti", "Avvertimenti consentiti", "Warnings allowed",
            "ui.azzera.penalita", "Azzera penalita", "Clear penalties",
            "ui.bandiera.rossa", "Bandiera rossa", "Red flag",
            "ui.base.facoltativa.se.scegli.un.preset", "Base facoltativa: se scegli un preset esistente, ne vengono conservate assist, condizioni pista e opzioni, e Pitwall sostituisce solo pista, meteo e griglia.", "Base is optional: if you pick an existing preset, its assists, track state and options are kept, and Pitwall only replaces track, weather and grid.",
            "ui.calendario", "Calendario", "Calendar",
            "ui.campionati", "Campionati", "Championships",
            "ui.campionati.salvati", "Campionati salvati", "Saved championships",
            "ui.campionato.per.assetto.corsa", "CAMPIONATO PER ASSETTO CORSA", "CHAMPIONSHIP FOR ASSETTO CORSA",
            "ui.cartella.dati", "Cartella dati", "Data folder",
            "ui.cartelle", "Cartelle", "Folders",
            "ui.circuiti", "Circuiti", "Circuits",
            "ui.circuiti.del.campionato", "Circuiti del campionato", "Championship circuits",
            "ui.classifiche", "Classifiche", "Standings",
            "ui.cognome", "Cognome", "Surname",
            "ui.colore", "Colore", "Colour",
            "ui.comincia.da.qui", "Comincia da qui", "Start here",
            "ui.consumo", "Consumo", "Fuel use",
            "ui.controlli.direzione.gara", "Controlli direzione gara", "Race control checks",
            "ui.costruttori", "Costruttori", "Constructors",
            "ui.danni", "Danni", "Damage",
            "ui.data", "Data", "Date",
            "ui.dati.del.campionato", "Dati del campionato", "Championship details",
            "ui.descrizione", "Descrizione", "Description",
            "ui.direzione.gara", "Direzione gara", "Race control",
            "ui.direzione.gara.attiva", "Direzione gara attiva", "Race control active",
            "ui.distacco", "Distacco", "Gap",
            "ui.durata.laps.per.un.numero.fisso", "Durata: Laps per un numero fisso di giri, Minutes per una sessione a tempo, DistancePercent per una percentuale della distanza reale del gran premio.", "Length: Laps for a fixed number of laps, Minutes for a timed session, DistancePercent for a share of the real grand prix distance.",
            "ui.durata.minima.vsc.s", "Durata minima VSC (s)", "Minimum VSC length (s)",
            "ui.elimina", "Elimina", "Delete",
            "ui.entro.la.posizione", "entro la posizione", "within position",
            "ui.esporta.gara",       "Prepara la prossima gara", "Prepare the next race",

            // Una gara conclusa non si modifica e non si rifa.
            "ui.gara.conclusa",     "Gara conclusa, vinta da {0}. Non si modifica e non si rifa.",
                                    "Race completed, won by {0}. It cannot be edited or run again.",
            "ui.gara.conclusa.anonima", "Gara conclusa. Non si modifica e non si rifa.",
                                    "Race completed. It cannot be edited or run again.",
            "ui.riapri.gara",       "Riapri gara", "Reopen race",
            "ui.riapri.conferma",   "Riaprire {0}?\n\nI risultati di questo fine settimana vengono cancellati e la gara torna da correre. La classifica si ricalcola senza di essa.",
                                    "Reopen {0}?\n\nThis weekend's results are deleted and the race goes back to being run. The standings are recalculated without it.",
            "ui.riapri.fatto",      "{0} riaperta: torna a essere la gara da correre.",
                                    "{0} reopened: it is the race to run again.",
            "ui.cm.gara.pronta",    "In Content Manager ora c'e una gara sola:\n\n{0}\n\nLa trovi in Quick Drive, menu dei preset, nella cartella \"{1}\". Appena ne registri il risultato, al suo posto compare la successiva.",
                                    "Content Manager now shows one race only:\n\n{0}\n\nYou find it in Quick Drive, presets menu, in the \"{1}\" folder. As soon as you record its result, the next one takes its place.",
            "ui.esporta.csv", "Esporta CSV", "Export CSV",
            "ui.filtra", "Filtra", "Filter",
            "ui.fine.neutralizzazione", "Fine neutralizzazione", "End of neutralisation",
            "ui.fine.settimana.libere.qualifiche.e.gara", "Fine settimana: libere, qualifiche e gara di seguito", "Weekend: practice, qualifying and race in sequence",
            "ui.formato", "Formato", "Format",
            "ui.formazione", "Formazione", "Formation",
            "ui.gare", "Gare", "Races",
            "ui.giri", "Giri", "Laps",
            "ui.giri.gp", "Giri GP", "GP laps",
            "ui.giri.minimi.sc", "Giri minimi SC", "Minimum SC laps",
            "ui.giro.rif.s", "Giro rif. (s)", "Reference lap (s)",
            "ui.giro.veloce", "Giro veloce", "Fastest lap",
            "ui.giu", "Giu", "Down",
            "ui.gomma", "Gomma", "Tyre",
            "ui.gomme", "Gomme", "Tyres",
            "ui.gv", "GV", "FL",
            "ui.il.plugin.simhub.segue.il.campionato", "Il plugin SimHub segue il campionato selezionato qui e vi scrive dentro i risultati appena una sessione finisce.", "The SimHub plugin follows the championship selected here and writes the results into it as soon as a session ends.",
            "ui.imposta.come.mio.pilota", "Imposta come mio pilota", "Set as my driver",
            "ui.impostazioni", "Impostazioni", "Settings",
            "ui.in.attesa.del.plugin.simhub", "In attesa del plugin SimHub", "Waiting for the SimHub plugin",
            "ui.ingegnere.di.pista", "Ingegnere di pista", "Race engineer",
            "ui.la.lingua.cambia.subito.qui.dentro", "La lingua cambia subito qui. Dentro Assetto Corsa cambia alla prossima sessione.", "The language changes here straight away. Inside Assetto Corsa it changes from the next session.",
            "ui.layout", "Layout", "Layout",
            "ui.limite.pit.km.h", "Limite pit (km/h)", "Pit limit (km/h)",
            "ui.lingua", "Lingua", "Language",
            "ui.live", "Live", "Live",
            "ui.loquacita.0.2", "Loquacita 0-2", "Talkativeness 0-2",
            "ui.meteo", "Meteo", "Weather",
            "ui.meteo.dinamico", "Meteo dinamico", "Dynamic weather",
            "ui.meteo.dinamico.stato.sconosciuto", "Meteo dinamico: stato sconosciuto.", "Dynamic weather: state unknown.",
            "ui.meteo.previsto", "Meteo previsto", "Weather forecast",
            "ui.metri", "Metri", "Metres",
            "ui.miglior.giro", "Miglior giro", "Best lap",
            "ui.moltiplicatore.pioggia", "Moltiplicatore pioggia", "Rain multiplier",
            "ui.motore", "Motore", "Power unit",
            "ui.muretto.box", "Muretto box", "Pit wall",
            "ui.n", "N.", "No.",
            "ui.nazione", "Nazione", "Nationality",
            "ui.nessun.campionato.aperto", "Nessun campionato aperto", "No championship open",
            "ui.nessuna.gara.selezionata", "Nessuna gara selezionata", "No race selected",
            "ui.niente.sc.negli.ultimi.giri", "Niente SC negli ultimi giri", "No SC in the final laps",
            "ui.nome", "Nome", "Name",
            "ui.nome.ac", "Nome AC", "AC name",
            "ui.nome.ac.deve.coincidere.con.il", "Nome AC deve coincidere con il nome nella entry list di Assetto Corsa: e la chiave con cui la telemetria viene riconosciuta. Spunta Tu sul pilota che guidi.", "AC name must match the name in the Assetto Corsa entry list: it is the key the telemetry is recognised by. Tick You on the driver you race.",
            "ui.non.c.e.nessun.campionato.premi", "Non c e nessun campionato. Premi Nuovo campionato e scegli quante vetture, quali scuderie, chi guidi tu e come si contano i punti: le gare finiscono da sole dentro Content Manager.", "There is no championship yet. Press New championship and choose how many cars, which teams, who you drive and how points are scored: the races end up inside Content Manager on their own.",
            "ui.nuovo.campionato", "Nuovo campionato...", "New championship...",
            "ui.nuovo.vuoto", "Nuovo vuoto", "New empty",
            "ui.ora.inizio", "Ora inizio", "Start time",
            "ui.ordine.d.arrivo", "Ordine d'arrivo", "Finishing order",
            "ui.paese", "Paese", "Country",
            "ui.parc.ferme", "Parc ferme", "Parc ferme",
            "ui.parla.nella.lingua.scelta.in.impostazioni", "Parla nella lingua scelta in Impostazioni.", "Speaks the language chosen in Settings.",
            "ui.parten.ferma", "Parten. ferma", "Standing start",
            "ui.pausa.minima.s", "Pausa minima (s)", "Minimum gap (s)",
            "ui.penalita", "Penalita", "Penalties",
            "ui.penalita.s", "Penalita (s)", "Penalty (s)",
            "ui.perdita.pit.s", "Perdita pit (s)", "Pit loss (s)",
            "ui.pilota", "Pilota", "Driver",
            "ui.piloti", "Piloti", "Drivers",
            "ui.piste.installate.in.assetto.corsa", "Piste installate in Assetto Corsa", "Tracks installed in Assetto Corsa",
            "ui.pitwall", "PITWALL", "PITWALL",
            "ui.pitwall.tiene.tutto.in.file.senza", "Pitwall tiene tutto in file, senza servizi e senza rete: ogni pezzo funziona anche se gli altri sono spenti.", "Pitwall keeps everything in files, with no services and no network: each part works even when the others are off.",
            "ui.pod", "Pod", "Pod",
            "ui.pole", "Pole", "Pole",
            "ui.pos", "Pos", "Pos",
            "ui.preset", "Preset", "Preset",
            "ui.preset.per.content.manager", "Preset per Content Manager", "Presets for Content Manager",
            "ui.prob.pioggia", "Prob. pioggia", "Rain prob.",
            "ui.prob.sc", "Prob. SC", "SC prob.",
            "ui.pronto", "Pronto", "Ready",
            "ui.punteggi", "Punteggi", "Points schemes",
            "ui.punteggio.ridotto.se.la.gara.finisce", "Punteggio ridotto se la gara finisce prima della distanza", "Reduced points if the race ends short of the distance",
            "ui.punti", "Punti", "Points",
            "ui.punti.gara.dal.primo.in.giu", "Punti gara (dal primo in giu, separati da virgola)", "Race points (from first down, separated by commas)",
            "ui.punti.iniziali", "Punti iniziali", "Starting points",
            "ui.punti.sprint", "Punti sprint", "Sprint points",
            "ui.radio", "Radio", "Radio",
            "ui.realismo", "Realismo", "Realism",
            "ui.regolamento", "Regolamento", "Regulations",
            "ui.rigenera.meteo.weekend", "Rigenera meteo weekend", "Regenerate weekend weather",
            "ui.rileva", "Rileva", "Detect",
            "ui.rimuovi", "Rimuovi", "Remove",
            "ui.rimuovi.selezionato", "Rimuovi selezionato", "Remove selected",
            "ui.ripristina.meteo", "Ripristina meteo", "Restore weather",
            "ui.ripristina.piste", "Ripristina piste", "Restore tracks",
            "ui.risultati.gara", "Risultati gara", "Race results",
            "ui.rit", "Rit", "Ret",
            "ui.ritmo.sc", "Ritmo SC (%)", "SC pace (%)",
            "ui.ritmo.vsc", "Ritmo VSC (%)", "VSC pace (%)",
            "ui.safety.car", "Safety car", "Safety car",
            "ui.safety.car.2", "Safety Car", "Safety Car",
            "ui.salva", "Salva", "Save",
            "ui.salva.risultato", "Salva risultato", "Save result",
            "ui.scansiona", "Scansiona", "Scan",
            "ui.scarti.peggiori.risultati", "Scarti peggiori risultati", "Dropped worst results",
            "ui.script.di.physics.stato.sconosciuto", "Script di physics: stato sconosciuto.", "Physics scripts: state unknown.",
            "ui.scrive.un.preset.di.quick.drive", "Scrive il preset di Quick Drive della prossima gara, con griglia, pista, ora, temperatura e giri. In Content Manager si vede una gara alla volta: appena ne registri il risultato, al suo posto compare la successiva.", "Writes the Quick Drive preset for the next race, with grid, track, time, temperature and laps. Content Manager shows one race at a time: as soon as you record its result, the next one takes its place.",
            "ui.senza.la.spunta.parte.direttamente.la", "Senza la spunta parte direttamente la gara, con la griglia decisa dal preset.", "Without the tick the race starts straight away, with the grid decided by the preset.",
            "ui.sessione", "Sessione", "Session",
            "ui.set.s.m.h", "Set S / M / H", "Sets S / M / H",
            "ui.sev.gomme", "Sev. gomme", "Tyre sev.",
            "ui.severita.gomme.1.5.governa.il", "Severita gomme 1-5 governa il degrado, Sorpassi 1-5 quanto e difficile passare, Prob. SC quanto spesso un incidente qui diventa safety car.", "Tyre severity 1-5 drives degradation, Overtaking 1-5 how hard it is to pass, SC prob. how often an incident here becomes a safety car.",
            "ui.sigla", "Sigla", "Code",
            "ui.sorpassi", "Sorpassi", "Overtaking",
            "ui.soste", "Soste", "Stops",
            "ui.soste.obbl", "Soste obbl.", "Mand. stops",
            "ui.stagione", "Stagione", "Season",
            "ui.stato", "Stato", "State",
            "ui.su", "Su", "Up",
            "ui.team", "Team", "Teams",
            "ui.tempi.in.tempo.reale", "Tempi in tempo reale", "Live timing",
            "ui.tipo.durata", "Tipo durata", "Length type",
            "ui.track.ac", "Track AC", "AC track",
            "ui.track.limits", "Track limits", "Track limits",
            "ui.tu", "Tu", "You",
            "ui.usura.al.degrado", "Usura al degrado (%)", "Wear at degradation (%)",
            "ui.usura.critica", "Usura critica (%)", "Critical wear (%)",
            "ui.usura.gomme", "Usura gomme", "Tyre wear",
            "ui.vale.per.tutto.pitwall.questa.applicazione", "Vale per tutto Pitwall: questa applicazione, il plugin di SimHub e le finestre dentro Assetto Corsa.", "Applies to all of Pitwall: this application, the SimHub plugin and the windows inside Assetto Corsa.",
            "ui.valore", "Valore", "Value",
            "ui.variabilita", "Variabilita", "Variability",
            "ui.velocita.in.pit.lane", "Velocita in pit lane", "Pit lane speeding",
            "ui.verde", "Verde", "Green",
            "ui.verifica", "Verifica", "Check",
            "ui.virtual.safety.car", "Virtual safety car", "Virtual safety car",
            "ui.virtual.safety.car.2", "Virtual Safety Car", "Virtual Safety Car",
            "cfg.1.campionato", "1. Campionato", "1. Championship",
            "cfg.2.griglia", "2. Griglia", "2. Grid",
            "cfg.3.piloti", "3. Piloti", "3. Drivers",
            "cfg.4.calendario", "4. Calendario", "4. Calendar",
            "cfg.5.punti", "5. Punti", "5. Points",
            "cfg.a.eliminazione.q1.q2.q3", "a eliminazione Q1 / Q2 / Q3", "knockout Q1 / Q2 / Q3",
            "cfg.abilita.ia", "Abilita IA", "AI skill",
            "cfg.aggressivita", "Aggressivita", "Aggression",
            "cfg.al.volante.ci.sei.tu", "Al volante ci sei tu", "You are at the wheel of",
            "cfg.annulla", "Annulla", "Cancel",
            "cfg.avanti", "Avanti", "Next",
            "cfg.bonus", "Bonus", "Bonuses",
            "cfg.calendario", "Calendario", "Calendar",
            "cfg.cognome", "Cognome", "Surname",
            "cfg.da", "da", "of",
            "cfg.della.distanza.vera", "della distanza vera", "of the real distance",
            "cfg.descrizione", "Descrizione", "Description",
            "cfg.due.piloti.per.scuderia.come.in", "Due piloti per scuderia, come in Formula 1. Scegliendo il numero di vetture si spuntano le prime scuderie dell'elenco; puoi poi aggiungerne o toglierne a mano.", "Two drivers per team, as in Formula 1. Choosing the number of cars ticks the first teams on the list; you can then add or remove them by hand.",
            "cfg.genera.il.calendario.completo.con.i", "Genera il calendario completo con i circuiti di Pitwall", "Generate the full calendar with the Pitwall circuits",
            "cfg.giri", "Giri", "Laps",
            "cfg.giro.veloce", "Giro veloce", "Fastest lap",
            "cfg.gran.premio", "Gran Premio", "Grand Prix",
            "cfg.in.calendario", "In calendario", "On calendar",
            "cfg.in.gara", "In gara", "Racing",
            "cfg.indietro", "Indietro", "Back",
            "cfg.lunghezza.gara", "Lunghezza gara", "Race length",
            "cfg.minuti", "minuti", "minutes",
            "cfg.motore", "Motore", "Power unit",
            "cfg.n", "N.", "No.",
            "cfg.nazione", "Nazione", "Nationality",
            "cfg.nei.fine.settimana.con.la.sprint", "Nei fine settimana con la sprint resta una sola prova libera e si aggiunge una qualifica apposta, come in Formula 1. I giri scritti a mano nella tabella hanno la precedenza sulla percentuale.", "On sprint weekends only one practice session is left and a dedicated qualifying is added, as in Formula 1. Laps typed by hand in the table take priority over the percentage.",
            "cfg.nessuna", "Nessuna", "None",
            "cfg.nome", "Nome", "Name",
            "cfg.paese", "Paese", "Country",
            "cfg.piloti", "Piloti", "Drivers",
            "cfg.pole.position", "Pole position", "Pole position",
            "cfg.posto", "posto", "place",
            "cfg.prendi.il.posto.di.questo.pilota", "Prendi il posto di questo pilota: la sua vettura diventa la tua, e il suo nome resta nei risultati come il pilota che hai sostituito.", "You take this driver's place: his car becomes yours, and his name stays in the results as the driver you replaced.",
            "cfg.prove.libere", "Prove libere", "Practice",
            "cfg.punti", "punti", "points",
            "cfg.punti.solo.se.arrivi.entro.il", "punti, solo se arrivi entro il", "points, only if you finish within",
            "cfg.qualifica", "Qualifica", "Qualifying",
            "cfg.quante.vetture", "Quante vetture", "How many cars",
            "cfg.riepilogo", "Riepilogo", "Summary",
            "cfg.scuderia", "Scuderia", "Team",
            "cfg.senza.la.spunta.il.campionato.nasce", "Senza la spunta il campionato nasce senza gare e le aggiungi tu dalla scheda Circuiti.", "Without the tick the championship is created with no races and you add them from the Circuits tab.",
            "cfg.sigla", "Sigla", "Code",
            "cfg.sistema.punti", "Sistema punti", "Points scheme",
            "cfg.sprint", "Sprint", "Sprint",
            "cfg.stagione", "Stagione", "Season",
            "cfg.tutte.le.gare", "Tutte le gare", "All races",
        };
        // =============================================================================
        // Il cielo e la pista.
        // =============================================================================
        private static readonly string[] Meteo =
        {
            "meteo.sereno",            "Sereno",                        "Clear",
            "meteo.poco.nuvoloso",     "Poco nuvoloso",                 "Few clouds",
            "meteo.nuvoloso",          "Nuvoloso",                      "Cloudy",
            "meteo.coperto",           "Coperto",                       "Overcast",
            "meteo.pioggia.leggera",   "Pioggia leggera",               "Light rain",
            "meteo.pioggia",           "Pioggia",                       "Rain",
            "meteo.pioggia.forte",     "Pioggia forte",                 "Heavy rain",
        };

        // =============================================================================
        // Gli scenari che il muretto propone.
        //
        // Sono frasi che si leggono di sfuggita mentre si guida, quindi restano corte anche
        // in inglese: uno scenario che va a capo sul pannello non lo legge nessuno.
        // =============================================================================
        private static readonly string[] Strategia =
        {
            "strategia.fino.in.fondo", "Fino in fondo",                 "To the end",
            "strategia.nessuna.sosta", "Nessuna sosta, si arriva con queste",
                                       "No stop, these tyres go all the way",
            "strategia.gomme.oltre",   "Gomme oltre il degrado negli ultimi giri",
                                       "Tyres past the cliff in the closing laps",
            "strategia.gomme.entro",   "Gomme entro il limite",         "Tyres inside the limit",
            "strategia.una.sosta",     "Una sosta, {0}",                "One stop, {0}",
            "strategia.box.giro.poi",  "Box al giro {0}, poi {1}",      "Box on lap {0}, then {1}",
            "strategia.stint.comodo",  "Stint finale comodo",           "Comfortable final stint",
            "strategia.stint.lungo",   "Stint finale lungo per questa mescola",
                                       "Final stint long for this compound",
            "strategia.due.soste",     "Due soste, aggressiva",         "Two stops, aggressive",
            "strategia.box.giri.soft", "Box ai giri {0} e {1}, sempre soft",
                                       "Box on laps {0} and {1}, soft throughout",
            "strategia.due.transiti",  "Due transiti in corsia, ma gomma sempre fresca",
                                       "Two trips down the pit lane, but always fresh rubber",
            "strategia.pioggia",       "Pioggia: {0}",                  "Rain: {0}",
            "strategia.box.subito",    "Box subito per {0}",            "Box now for {0}",
            "strategia.box.acqua",     "Box al giro {0}, quando arriva l'acqua",
                                       "Box on lap {0}, when the rain arrives",
            "strategia.gomma.giusta",  "La gomma giusta per l'acqua prevista",
                                       "The right tyre for the rain expected",
            "strategia.troppa.gomma",  "Troppa gomma se piove poco",    "Too much tyre if it rains little",
            "strategia.poca.gomma",   "Poca gomma se piove forte",      "Not enough tyre if it rains hard",
            "strategia.resta.fuori",   "Resta fuori",                   "Stay out",
            "strategia.niente.sosta",  "Niente sosta, si resta su {0}", "No stop, staying on {0}",
            "strategia.slick.bagnato", "Niente sosta, slick anche sul bagnato",
                                       "No stop, slicks even in the wet",
            "strategia.si.perde",      "Si perde se asciuga",           "Loses out if it dries",
            "strategia.scommessa",     "Scommessa secca: paga solo se l'acqua resta poca",
                                       "A pure gamble: it pays only if the rain stays light",
        };
    }
}
