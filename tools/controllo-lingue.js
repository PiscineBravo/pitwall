// Controlla il vocabolario delle lingue.
//
// Su settecento frasi una chiave sbagliata non si vede: il programma non si pianta, mostra la
// chiave al posto della frase, e magari se ne accorge un cliente. Questo controllo guarda tre
// cose, e sono tutte e tre cose che e gia successo di sbagliare altrove:
//
//   1. chiavi usate nel codice che nel vocabolario non esistono;
//   2. frasi senza la traduzione inglese;
//   3. chiavi scritte due volte, dove la seconda vince in silenzio sulla prima.
//
// Uso: node tools/controllo-lingue.js

const fs = require('fs');
const path = require('path');

const radice = path.resolve(__dirname, '..');
const vocabolario = path.join(radice, 'src', 'Pitwall.Core', 'Lingue', 'Dizionari.cs');

// ---------------------------------------------------------------------------
// Il vocabolario: si legge dal file sorgente, a tre stringhe per riga logica.
// ---------------------------------------------------------------------------
function leggiVocabolario() {
  const testo = fs.readFileSync(vocabolario, 'utf8');

  // Via i commenti, altrimenti una frase dentro un commento sembra una voce vera.
  const pulito = testo.replace(/\/\/[^\n]*/g, '').replace(/\/\*[\s\S]*?\*\//g, '');

  // Tutte le stringhe, nell'ordine in cui compaiono dentro gli elenchi.
  const dentroElenchi = pulito.split('private static readonly string[]').slice(1).join('\n');
  const stringhe = [...dentroElenchi.matchAll(/"((?:[^"\\]|\\.)*)"/g)].map(m => m[1]);

  const voci = [];
  for (let i = 0; i + 2 < stringhe.length; i += 3) {
    voci.push({ chiave: stringhe[i], it: stringhe[i + 1], en: stringhe[i + 2] });
  }
  return voci;
}

// ---------------------------------------------------------------------------
// Le chiavi usate nel codice.
// ---------------------------------------------------------------------------
function chiaviUsate(noteChiavi) {
  const trovate = new Map();

  function percorri(cartella) {
    for (const voce of fs.readdirSync(cartella, { withFileTypes: true })) {
      const completo = path.join(cartella, voce.name);
      if (voce.isDirectory()) {
        if (voce.name === 'obj' || voce.name === 'bin' || voce.name === '.git') continue;
        percorri(completo);
      } else if (/\.(cs|lua|xaml)$/i.test(voce.name)) {
        const testo = fs.readFileSync(completo, 'utf8');
        // Due forme: una nel codice, una nei file dell interfaccia.
        const forme = [/Testi\.T\(\s*"([^"]+)"/g, /\{l:T\s+([^}\s]+)\s*\}/g];
        for (const forma of forme) {
          for (const m of testo.matchAll(forma)) {
            if (!trovate.has(m[1])) trovate.set(m[1], []);
            trovate.get(m[1]).push(path.relative(radice, completo));
          }
        }

        // Terza forma: qualunque stringa che sia gia una chiave del vocabolario.
        //
        // Serve perche una chiave puo passare per un aiutante - Voce("muretto.giro", ...) invece
        // di Testi.T("muretto.giro") - e cercando solo le due forme esplicite quelle chiavi
        // risultavano "mai usate" pur essendo in uso, nascondendo fra i falsi allarmi quelli veri.
        //
        // Non puo sbagliare per eccesso: si confronta con le chiavi che esistono gia, quindi una
        // stringa che coincide con una di quelle la sta nominando davvero.
        if (noteChiavi) {
          for (const m of testo.matchAll(/"([^"\n]+)"/g)) {
            if (!noteChiavi.has(m[1])) continue;
            if (!trovate.has(m[1])) trovate.set(m[1], []);
            trovate.get(m[1]).push(path.relative(radice, completo));
          }
        }
      }
    }
  }

  percorri(path.join(radice, 'src'));
  return trovate;
}

// ---------------------------------------------------------------------------
const voci = leggiVocabolario();
const usate = chiaviUsate(new Set(voci.map(v => v.chiave)));

const perChiave = new Map();
for (const v of voci) {
  if (!perChiave.has(v.chiave)) perChiave.set(v.chiave, []);
  perChiave.get(v.chiave).push(v);
}

let problemi = 0;

console.log('Vocabolario: ' + voci.length + ' frasi, ' + perChiave.size + ' chiavi');
console.log('Usate nel codice: ' + usate.size + ' chiavi');
console.log('');

const doppie = [...perChiave.entries()].filter(([, v]) => v.length > 1);
if (doppie.length) {
  console.log('CHIAVI SCRITTE PIU DI UNA VOLTA (' + doppie.length + '):');
  for (const [k, v] of doppie) console.log('  ' + k + '  (' + v.length + ' volte)');
  problemi += doppie.length;
  console.log('');
}

const senzaInglese = voci.filter(v => !v.en || v.en.trim() === '');
if (senzaInglese.length) {
  console.log('SENZA TRADUZIONE INGLESE (' + senzaInglese.length + '):');
  for (const v of senzaInglese) console.log('  ' + v.chiave + '  = ' + v.it);
  problemi += senzaInglese.length;
  console.log('');
}

const inventate = [...usate.keys()].filter(k => !perChiave.has(k));
if (inventate.length) {
  console.log('USATE NEL CODICE MA NON NEL VOCABOLARIO (' + inventate.length + '):');
  for (const k of inventate) console.log('  ' + k + '  <- ' + usate.get(k)[0]);
  problemi += inventate.length;
  console.log('');
}

const inutilizzate = [...perChiave.keys()].filter(k => !usate.has(k));
if (inutilizzate.length) {
  console.log('Nel vocabolario ma non ancora usate (' + inutilizzate.length + '):');
  console.log('  ' + inutilizzate.join(', '));
  console.log('  Non e un errore: sono le frasi pronte per i pezzi non ancora tradotti.');
  console.log('');
}

console.log(problemi === 0 ? 'Vocabolario coerente.' : problemi + ' PROBLEMI DA SISTEMARE');
process.exit(problemi === 0 ? 0 : 1);
