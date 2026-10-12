# Da provare

Quello che è stato scritto ma non è ancora acceso, e quello che resta aperto. Aggiornato
dopo la sessione di collaudo del 5 ottobre 2026, in cui la partenza ha funzionato per la
prima volta dall'inizio alla fine.

---

## Pronto ma spento

Due comportamenti del giro di formazione. Sono scritti, funzionano, e sono quelli giusti:
in Formula 1 si lascia la griglia scaglionati, e in ricognizione non si tiene una velocità
fissa — si accelera, si frena, si zigzaga per portare in temperatura gomme, freni e motore.

Accesi **insieme** però hanno prodotto contatti all'arrivo in griglia: il gruppo si allunga
troppo, le vetture arrivano alla spicciolata e si tamponano mentre si schierano.

Stanno in [`ac-app/pitwall/pitwall.lua`](../ac-app/pitwall/pitwall.lua), vicino all'inizio:

| Costante | Adesso | Da provare | Cosa fa |
|---|---|---|---|
| `STAGGER_SECONDS` | `0` | `0.6` | Secondi di attesa per posizione in griglia prima di partire |
| `WARMUP_SWING` | `0` | `0.20` | Quanto respira l'andatura: 0.20 = dal 80% al 120% del ritmo |

**Come provarli: uno alla volta.** Prima lo scaglionamento da solo, poi — se la griglia si
riforma pulita — il respiro dell'andatura, partendo da metà di quello che avevo messo la
prima volta. Il segnale da guardare è l'arrivo in griglia: se le vetture si tamponano
mentre si schierano, il valore è troppo alto.

---

## Acceso e confermato in pista

- **Motori su al semaforo.** All'accensione delle luci rosse le vetture salgono di giri
  invece di partire col motore al minimo: i giri crescono con le luci, da una a cinque.
  Si ottiene staccando la frizione e tenendo il freno, come una partenza vera.

---

## Aperto

- **Il giro di formazione è lento.** Circa quattro minuti per un giro di Melbourne, contro
  i due e mezzo che servirebbero a 110 km/h. Il regolatore del distacco rallenta più del
  dovuto quando il gruppo è compatto. Da guardare: il pavimento del regolatore
  (`formationPace`, oggi al 75% del ritmo) e la soglia di distacco.

- **Bandiera gialla che non si spegne.** Dopo la partenza resta un giallo di settore anche
  quando non c'è più niente in pista. Da capire se è un incidente vero rilevato male o se
  manca la condizione di rientro.

- **La griglia non usa l'ordine delle qualifiche.** Q3 → Q2 → Q1 viene calcolato
  correttamente ma non viene imposto ad Assetto Corsa.

- **Consumo misurato ancora basso.** Dopo il filtro sui giri limitati siamo passati da 0,85
  a 1,56 litri al giro, ma una Formula 1 ne consuma circa 2,5. Da verificare su una
  sessione di prove lunga, dove i giri a ritmo vero sono molti.

---

## Il banco di prova

`prova.ps1` nello scratchpad avvia una gara vera senza Content Manager, accende il pilota
automatico e registra una riga al secondo in `cronologia.csv`.

```
prova.ps1 -Tipo gara -Giri 3 -SecondiMax 420
prova.ps1 -Tipo prove -Giri 20
```

Riscrive `race.ini` con la sola sessione richiesta e lo rimette com'era alla fine. Riavvia
SimHub prima di partire: senza quello si misura sullo stato della sessione precedente, ed è
già successo di correggere difetti che non esistevano.

I numeri che contano, in `diagnostica.txt`:

| Riga | Cosa dice |
|---|---|
| `girate_adesso` | Vetture rivolte dalla parte sbagliata rispetto alla piazzola. **Deve essere 0** |
| `rilasci_falliti` | Quante volte il rilascio al verde ha rinunciato. **Deve essere 0** |
| `alla_rinuncia_ferme` | Quante erano ferme quando ha rinunciato (distingue il difetto) |
| `richiesti_fermi_dal_core` | Vetture che il core vuole ferme ai box. **In gara deve essere 0** |
| `sessione_secondo_ac` | Gara / prove / non ancora nota, secondo il gioco |
| `piazzole_sconosciute` | Vetture senza piazzola: non vengono schierate invece di essere messe a caso |

---

## Come si è trovato il difetto della partenza

Vale la pena ricordarlo, perché è il metodo che ha funzionato dopo giorni di tentativi.

Il sintomo era "le auto restano ferme al verde". Le cause si sono rivelate **quattro**,
sovrapposte, e ognuna nascondeva la successiva:

1. La procedura di partenza si armava sulla telemetria che Assetto Corsa lascia in memoria
   dopo essersi chiuso — partiva prima che la sessione esistesse.
2. La fotografia della griglia veniva rifatta per tutto il giro di formazione, e l'ultima
   coglieva le vetture sparse in pista. Quella diventava "la griglia".
3. `setAICarPosition` intende la direzione al contrario di come la espone `car.look`: venti
   vetture su venti finivano girate.
4. `setAINoInput` mette la vettura in *parked state*, e da lì non si esce passandogli
   `false`. Il motore saliva di giri e la vettura non si muoveva.

Nessuna delle quattro si sarebbe trovata a forza di ipotesi. Le prime due le ha trovate la
cronologia secondo per secondo; la terza un contatore che misurava l'angolo invece di
guardare; la quarta un dettaglio osservato in pista — *"alzano i giri ma non partono"* — che
è la firma di una frizione staccata, non di un freno.

**Misurare, non indovinare.** Quando un difetto resiste a due tentativi, il terzo non deve
essere un'altra ipotesi: deve essere uno strumento che lo misura.
