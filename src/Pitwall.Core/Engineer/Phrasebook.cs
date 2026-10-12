using System;
using System.Collections.Generic;
using System.Globalization;

namespace Pitwall.Engineer
{
    /// <summary>
    /// What the engineer actually says. Every line has a few variants so the radio does not
    /// sound like a recording, and every line exists in Italian and English.
    /// </summary>
    public sealed class Phrasebook
    {
        private readonly Random _random = new Random();
        private readonly Dictionary<string, string[]> _lines;

        public string Language { get; }

        public Phrasebook(string language)
        {
            Language = string.IsNullOrWhiteSpace(language) ? "it" : language.ToLowerInvariant();
            _lines = Language.StartsWith("en") ? English() : Italian();
        }

        /// <summary>Formats a line, picking one of its variants at random.</summary>
        public string Say(string key, params object[] args)
        {
            if (!_lines.TryGetValue(key, out var variants) || variants.Length == 0)
                return key;

            string template = variants[_random.Next(variants.Length)];
            try
            {
                return args == null || args.Length == 0
                    ? template
                    : string.Format(CultureInfo.InvariantCulture, template, args);
            }
            catch (FormatException)
            {
                return template;
            }
        }

        public bool Has(string key) => _lines.ContainsKey(key);

        private static Dictionary<string, string[]> Italian() => new Dictionary<string, string[]>
        {
            // Session
            ["session.start.practice"] = new[] { "Radio check, si comincia. Prova libera, prenditi il tempo che serve.", "Radio ok. Libere: cerchiamo il bilanciamento." },
            ["session.start.qualifying"] = new[] { "Qualifica. Un giro pulito e basta.", "Radio check. È il momento del giro secco." },
            ["session.start.race"] = new[] { "Gara. {0} giri. Testa bassa e concentrazione.", "Si parte. {0} giri davanti a noi." },
            ["session.greenlight"] = new[] { "Semaforo verde, via!", "Si parte, tutto verde." },
            ["session.finalLap"] = new[] { "Ultimo giro. Portala a casa.", "Ultimo giro, dai." },
            ["session.checkered"] = new[] { "Bandiera a scacchi. P{0}. Ottimo lavoro.", "Finita. Chiudiamo P{0}." },
            ["session.lapsRemaining"] = new[] { "Mancano {0} giri.", "{0} giri alla fine." },

            // Tyres
            ["tyres.cold"] = new[] { "Gomme fredde, scaldale bene prima di spingere.", "Attento, le gomme non sono ancora in temperatura." },
            ["tyres.overheating"] = new[] { "Gomme in sovratemperatura, gestisci un paio di curve.", "Stai cuocendo le gomme, alza un attimo il ritmo." },
            ["tyres.wear.half"] = new[] { "Gomme al {0} percento di usura, siamo nella norma.", "Usura {0} percento, tutto sotto controllo." },
            ["tyres.wear.warning"] = new[] { "Le gomme iniziano a calare, {0} giri prima del degrado forte.", "Occhio alle gomme, ti restano circa {0} giri buoni." },
            ["tyres.wear.cliff"] = new[] { "Siamo oltre il limite delle gomme, il passo sta crollando.", "Gomme finite, perdiamo tanto per giro." },
            ["tyres.wear.critical"] = new[] { "Gomme al limite, box subito.", "Non possiamo più andare avanti così, box box." },
            ["tyres.newSet"] = new[] { "Set nuovo, gomma {0}. Due curve per scaldarla.", "Montate {0}. Scaldale con calma." },

            // Fuel
            ["fuel.low"] = new[] { "Benzina per {0} giri, inizia a gestire.", "Restano circa {0} giri di carburante." },
            ["fuel.critical"] = new[] { "Benzina critica, lift and coast in fondo ai rettilinei.", "Siamo corti di benzina, devi risparmiare." },
            ["fuel.fine"] = new[] { "Benzina ok, puoi spingere.", "Sul carburante siamo a posto." },

            // Strategy
            ["strategy.window.open"] = new[] { "Finestra pit aperta. Box quando vuoi.", "Siamo nella finestra, la sosta è disponibile." },
            ["strategy.boxThisLap"] = new[] { "Box box box, questo giro.", "Box questo giro, confermato." },
            ["strategy.stayOut"] = new[] { "Resta fuori, resta fuori.", "Non entriamo, continua." },
            ["strategy.plan"] = new[] { "Piano: una sosta, {0} al giro {1}.", "Strategia una sosta, {0} verso il giro {1}." },
            ["strategy.undercutThreat"] = new[] { "{0} è ai box, rischio undercut. Spingi o entriamo anche noi.", "Attenzione, {0} ha fermato. Due giri per rispondere." },
            ["strategy.overcut"] = new[] { "Resta fuori due giri, proviamo l'overcut.", "Allunga lo stint, tentiamo l'overcut." },
            ["strategy.mandatoryStop"] = new[] { "Ricorda la sosta obbligatoria, mancano {0} giri.", "Devi ancora fare la sosta obbligatoria." },
            ["strategy.twoCompounds"] = new[] { "Ti serve ancora l'altra mescola prima della fine.", "Regola delle due mescole: non l'abbiamo ancora rispettata." },
            ["strategy.pitUnderSc"] = new[] { "Safety car, è il momento giusto per fermarci. Box.", "Occasione sotto safety car, box box." },

            // Gaps
            ["gap.ahead"] = new[] { "{0} davanti a {1} secondi.", "Distacco da {0}: {1}." },
            ["gap.behind"] = new[] { "{0} dietro a {1} secondi.", "{0} ti segue a {1}." },
            ["gap.closing"] = new[] { "Stai recuperando su {0}, {1} al giro.", "Guadagni {1} al giro su {0}." },
            ["gap.losing"] = new[] { "{0} sta guadagnando, {1} al giro.", "Perdiamo {1} al giro da {0}." },
            ["gap.drsRange"] = new[] { "Sei in zona DRS.", "DRS disponibile, prova l'attacco." },
            ["gap.leading"] = new[] { "Sei al comando, {0} di vantaggio.", "Primo con {0} sul secondo." },

            // Weather
            ["weather.rainSoon"] = new[] { "Pioggia in arrivo tra {0} minuti.", "Il radar dà pioggia fra {0} minuti." },
            ["weather.rainNow"] = new[] { "Inizia a piovere, attenzione al grip.", "Sta piovendo, vai piano nei primi giri." },
            ["weather.boxForInters"] = new[] { "Box per intermedie, box box.", "Cambiamo, intermedie. Box questo giro." },
            ["weather.boxForWets"] = new[] { "Troppa acqua, box per full wet.", "Passiamo alle full wet, box box." },
            ["weather.drying"] = new[] { "La pista si sta asciugando, tra poco valutiamo le slick.", "Sta smettendo, prepariamoci alle slick." },
            ["weather.boxForSlicks"] = new[] { "Traiettoria asciutta, box per slick.", "Box per gomme slick, la pista tiene." },

            // Race control
            ["rc.yellow"] = new[] { "Bandiera gialla settore {0}, alza il piede.", "Giallo nel settore {0}." },
            ["rc.vsc"] = new[] { "Virtual safety car, rispetta il delta.", "VSC attiva, resta sopra il delta." },
            ["rc.sc"] = new[] { "Safety car in pista, safety car.", "Safety car, rallenta e mantieni la posizione." },
            ["rc.scEnding"] = new[] { "Safety car rientra questo giro, preparati alla ripartenza.", "SC ai box, si riparte." },
            ["rc.green"] = new[] { "Verde verde verde!", "Si riparte, via!" },
            ["rc.red"] = new[] { "Bandiera rossa, rientra ai box.", "Rossa, gara sospesa." },
            ["rc.deltaWarning"] = new[] { "Sei sotto il delta, rallenta.", "Delta negativo, alza il piede." },
            ["rc.blueFlags"] = new[] { "Bandiere blu, lascia passare.", "Doppiato in arrivo, blu." },

            // Penalties
            ["penalty.issued"] = new[] { "Penalità: {0}.", "Direzione gara ci ha dato {0}." },
            ["penalty.trackLimits"] = new[] { "Track limits, {0} di {1}. Stai dentro.", "Attenzione ai limiti pista: {0} su {1}." },

            // Damage
            ["damage.light"] = new[] { "Piccolo danno, i dati sono ancora buoni.", "Contatto leggero, tutto sotto controllo." },
            ["damage.heavy"] = new[] { "Danno importante, valuta il rientro ai box.", "Abbiamo perso carico, forse conviene fermarsi." },

            // Encouragement
            ["push.now"] = new[] { "Adesso spingi, tre giri al massimo.", "Modalità attacco, vai." },
            ["good.lap"] = new[] { "Bel giro, personale.", "Ottimo, è il tuo miglior giro." },
            ["position.gained"] = new[] { "Bella manovra, ora sei P{0}.", "Posizione guadagnata, P{0}." },
            ["position.lost"] = new[] { "Persa una posizione, ora P{0}. Riprendiamolo.", "Ti ha passato, sei P{0}." }
        };

        private static Dictionary<string, string[]> English() => new Dictionary<string, string[]>
        {
            ["session.start.practice"] = new[] { "Radio check. Free practice, take your time.", "Radio is good. Let us find the balance." },
            ["session.start.qualifying"] = new[] { "Qualifying. One clean lap, that is all.", "Radio check. Time for the flying lap." },
            ["session.start.race"] = new[] { "Race day. {0} laps. Head down.", "Here we go. {0} laps ahead of us." },
            ["session.greenlight"] = new[] { "Lights out and away we go!", "Green light, go go go." },
            ["session.finalLap"] = new[] { "Final lap. Bring it home.", "Last lap, keep it clean." },
            ["session.checkered"] = new[] { "Chequered flag. P{0}. Great job.", "That is it. P{0}." },
            ["session.lapsRemaining"] = new[] { "{0} laps to go.", "{0} laps remaining." },

            ["tyres.cold"] = new[] { "Cold tyres, build temperature before pushing.", "Careful, the tyres are not switched on yet." },
            ["tyres.overheating"] = new[] { "Tyres overheating, manage a couple of corners.", "You are cooking the tyres, back off slightly." },
            ["tyres.wear.half"] = new[] { "Tyres at {0} percent wear, all normal.", "Wear is {0} percent, under control." },
            ["tyres.wear.warning"] = new[] { "Tyres are going away, about {0} good laps left.", "Watch the tyres, roughly {0} laps before the drop." },
            ["tyres.wear.cliff"] = new[] { "We are past the cliff, pace is falling away.", "Tyres are done, we are losing a lot per lap." },
            ["tyres.wear.critical"] = new[] { "Tyres are critical, box now.", "We cannot carry on, box box." },
            ["tyres.newSet"] = new[] { "New set, {0} compound. Two corners to warm them.", "{0} tyres fitted. Warm them gently." },

            ["fuel.low"] = new[] { "Fuel for {0} laps, start managing.", "About {0} laps of fuel left." },
            ["fuel.critical"] = new[] { "Fuel critical, lift and coast at the end of the straights.", "We are short on fuel, you need to save." },
            ["fuel.fine"] = new[] { "Fuel is fine, you can push.", "No fuel concerns, keep pushing." },

            ["strategy.window.open"] = new[] { "Pit window is open. Box when you want.", "We are in the window, the stop is available." },
            ["strategy.boxThisLap"] = new[] { "Box box box, this lap.", "Box this lap, confirmed." },
            ["strategy.stayOut"] = new[] { "Stay out, stay out.", "We are not stopping, carry on." },
            ["strategy.plan"] = new[] { "Plan is one stop, {0} around lap {1}.", "One stop strategy, {0} near lap {1}." },
            ["strategy.undercutThreat"] = new[] { "{0} has pitted, undercut risk. Push or we respond.", "Careful, {0} stopped. Two laps to react." },
            ["strategy.overcut"] = new[] { "Stay out two more laps, we try the overcut.", "Extend the stint, going for the overcut." },
            ["strategy.mandatoryStop"] = new[] { "Remember the mandatory stop, {0} laps left.", "You still owe us the mandatory stop." },
            ["strategy.twoCompounds"] = new[] { "You still need the other compound before the end.", "Two compound rule is not satisfied yet." },
            ["strategy.pitUnderSc"] = new[] { "Safety car, this is our chance. Box.", "Free stop under the safety car, box box." },

            ["gap.ahead"] = new[] { "{0} ahead by {1} seconds.", "Gap to {0} is {1}." },
            ["gap.behind"] = new[] { "{0} behind by {1} seconds.", "{0} is {1} back." },
            ["gap.closing"] = new[] { "You are catching {0}, {1} per lap.", "Gaining {1} a lap on {0}." },
            ["gap.losing"] = new[] { "{0} is pulling away, {1} per lap.", "Losing {1} a lap to {0}." },
            ["gap.drsRange"] = new[] { "You are within DRS range.", "DRS available, have a look." },
            ["gap.leading"] = new[] { "You are leading by {0}.", "P1 with {0} in hand." },

            ["weather.rainSoon"] = new[] { "Rain expected in {0} minutes.", "Radar shows rain in {0} minutes." },
            ["weather.rainNow"] = new[] { "Rain is starting, mind the grip.", "It is raining, take the first laps carefully." },
            ["weather.boxForInters"] = new[] { "Box for intermediates, box box.", "Switching to inters. Box this lap." },
            ["weather.boxForWets"] = new[] { "Too much water, box for full wets.", "Full wets now, box box." },
            ["weather.drying"] = new[] { "Track is drying, we will look at slicks soon.", "Rain is stopping, get ready for slicks." },
            ["weather.boxForSlicks"] = new[] { "Dry line is there, box for slicks.", "Box for slick tyres, the track is ready." },

            ["rc.yellow"] = new[] { "Yellow flag sector {0}, lift.", "Yellow in sector {0}." },
            ["rc.vsc"] = new[] { "Virtual safety car, respect the delta.", "VSC deployed, stay above the delta." },
            ["rc.sc"] = new[] { "Safety car, safety car.", "Safety car out, slow down and hold position." },
            ["rc.scEnding"] = new[] { "Safety car in this lap, get ready.", "SC coming in, prepare for the restart." },
            ["rc.green"] = new[] { "Green green green!", "We are racing again." },
            ["rc.red"] = new[] { "Red flag, return to the pits.", "Red flag, session suspended." },
            ["rc.deltaWarning"] = new[] { "You are under the delta, slow down.", "Negative delta, lift off." },
            ["rc.blueFlags"] = new[] { "Blue flags, let them through.", "Leader coming, blue flags." },

            ["penalty.issued"] = new[] { "Penalty: {0}.", "Race control gave us {0}." },
            ["penalty.trackLimits"] = new[] { "Track limits, {0} of {1}. Stay inside.", "Mind the limits: {0} of {1}." },

            ["damage.light"] = new[] { "Light damage, the data still looks fine.", "Small contact, all under control." },
            ["damage.heavy"] = new[] { "Significant damage, consider coming in.", "We lost downforce, a stop may be worth it." },

            ["push.now"] = new[] { "Push now, three laps flat out.", "Attack mode, go." },
            ["good.lap"] = new[] { "Good lap, personal best.", "Nice, that is your quickest." },
            ["position.gained"] = new[] { "Great move, you are P{0}.", "Position gained, P{0}." },
            ["position.lost"] = new[] { "Lost a place, you are P{0}. Let us get it back.", "He got you, P{0} now." }
        };
    }
}
