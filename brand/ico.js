// Mette i PNG dentro un'icona di Windows (.ico).
//
// Il formato e semplice: un'intestazione, una voce di indice per ogni misura, e poi i file PNG
// uno dietro l'altro. Windows dalla versione Vista accetta i PNG dentro le icone, quindi non
// serve convertire niente in bitmap.
//
// Si scrive a mano invece di aggiungere una libreria: sono quaranta righe e tolgono una
// dipendenza da un progetto che di dipendenze ne ha gia abbastanza.

const fs = require('fs');
const path = require('path');

const cartella = __dirname;
const misure = [256, 128, 64, 48, 32, 16];

const immagini = misure.map(m => ({
  lato: m,
  dati: fs.readFileSync(path.join(cartella, `pitwall-${m}.png`))
}));

const INTESTAZIONE = 6;
const VOCE = 16;

const testa = Buffer.alloc(INTESTAZIONE);
testa.writeUInt16LE(0, 0);                 // riservato
testa.writeUInt16LE(1, 2);                 // 1 = icona
testa.writeUInt16LE(immagini.length, 4);

let offset = INTESTAZIONE + VOCE * immagini.length;
const indice = [];

for (const img of immagini) {
  const voce = Buffer.alloc(VOCE);
  // 256 si scrive come zero: in un byte non ci sta.
  voce.writeUInt8(img.lato >= 256 ? 0 : img.lato, 0);
  voce.writeUInt8(img.lato >= 256 ? 0 : img.lato, 1);
  voce.writeUInt8(0, 2);                   // colori nella tavolozza: nessuna
  voce.writeUInt8(0, 3);                   // riservato
  voce.writeUInt16LE(1, 4);                // piani
  voce.writeUInt16LE(32, 6);               // bit per pixel
  voce.writeUInt32LE(img.dati.length, 8);
  voce.writeUInt32LE(offset, 12);
  indice.push(voce);
  offset += img.dati.length;
}

const ico = Buffer.concat([testa, ...indice, ...immagini.map(i => i.dati)]);
fs.writeFileSync(path.join(cartella, 'pitwall.ico'), ico);

console.log('scritto pitwall.ico  (' + misure.join(', ') + ')  ' + ico.length + ' byte');
