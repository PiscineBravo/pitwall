# Intelligenza artificiale e safety car

Questa è la parte che trasforma una gara contro l'IA di Assetto Corsa in un gran premio.

## Il problema

L'IA di Assetto Corsa gira sempre allo stesso ritmo, non consuma le gomme e si ferma ai box
quando le pare. In un campionato in stile Formula 1 questo toglie senso a tutto: se gli
avversari non degradano, la sosta è solo tempo perso, l'undercut non esiste e la strategia è
una parola vuota. E non esiste né safety car né giro di formazione.

## Cosa fa Pitwall

Prima del via, il **direttore dell'IA** decide che gara farà ogni avversario:

- **quante soste e con quali mescole**, scelte dalla lunghezza della gara e dalla severità del
  circuito: una sosta se la distanza sta in due stint, due se non ci sta, con chi parte morbido
  e chi parte duro;
- **la forma del weekend**, fino a quattro decimi al giro in un senso o nell'altro, così il
  campionato non è sempre lo stesso ordine d'arrivo;
- **il destino**: qualcuno si ritira, qualcuno commette un errore. È da lì che nascono le
  safety car, invece che da un sorteggio.

Poi, giro dopo giro, traduce quel piano in comandi che l'app in pista applica con le funzioni
`physics.*` di Custom Shaders Patch:

| Cosa succede | Come |
|---|---|
| Le gomme calano | `setAILevel` scende man mano che lo stint invecchia, e crolla dopo il degrado |
| Rientrano ai box | `setAIPitStopRequest` al giro previsto, con `setAITyres` per la mescola |
| Piove e sono su slick | perdono molto, e vengono richiamati sulle intermedie |
| Safety car e VSC | `setAITopSpeed` per tutti, il gruppo si ricompatta davvero |
| Errore o ritiro | `setAIStopCounter`: la vettura resta ferma in pista |

Tutto nasce dal seme del round: la gara è riproducibile. Se perdi per un undercut, quell'undercut
era già scritto nei dati — non è stato inventato al momento per farti perdere.

## La safety car vera

Non è una regola astratta: è una **vettura in più nella griglia**. Pitwall la inserisce come
ventunesima iscritta — la Mercedes AMG GT Black Series, quella vera del campionato attuale — la
tiene parcheggiata ai box con il motore spento per tutta la gara, e la manda in pista solo
quando serve.

La procedura segue il regolamento sportivo, una fase per giro:

1. **Safety car in pista** — esce dai box e recupera sul gruppo, che intanto è stato rallentato.
2. **Ricompattamento** — la fila si chiude dietro di lei, niente sorpassi, pit lane chiusa.
3. **I doppiati possono sdoppiarsi** — come dice l'articolo 55.13.
4. **Safety car rientra questo giro** — l'annuncio, e la pit lane riapre.
5. **Ripartenza** — lei va ai box, il leader comanda il verde.

Nella classifica non compare: per il gioco è un'iscritta come le altre, per Pitwall no.

Se la vettura non è disponibile fra i contenuti installati, la procedura resta comunque —
delta, penalità, ordine congelato — semplicemente senza un'auto vera davanti.

## Il giro di formazione

Assetto Corsa parte con le vetture già in griglia e il semaforo verde: di ricognizione non c'è
traccia. Pitwall la ricostruisce in due tempi.

1. **Giro di formazione** — tutti limitati a 110 km/h, posizioni congelate, delta mostrato ma
   senza penalità: non è ancora gara. Serve a scaldare gomme e freni, come nella realtà.
2. **In griglia** — completata la ricognizione, ogni vettura viene **riportata nella sua
   piazzola** con `teleportCarTo` e tenuta ferma: l'IA con `setAINoInput`, tu con il freno
   forzato, che è meno brusco che bloccarti i comandi e ti lascia ingranare la marcia.
3. **Semaforo** — le luci si accendono, e fra l'ultima e il verde passa un tempo variabile fra
   1,5 e 4 secondi: altrimenti la partenza si impara a memoria.
4. **Via** — tutti liberati insieme. Partenza da fermo vera.

È il ritorno in griglia che la rende una procedura invece di un semplice primo giro lento. Ed è
anche la parte che si può disattivare (`ReturnToGrid`), perché è un intervento forte: senza, il
primo giro resta solo neutralizzato.

Il giro di ricognizione consuma un giro del conteggio di Assetto Corsa, quindi i preset generati
per Content Manager ne aggiungono uno: a Monza trovi 54 giri invece di 53, e la distanza di gara
resta quella voluta.

| Impostazione | Predefinito | Cosa fa |
|---|---|---|
| `PaceKmh` | 110 | Ritmo della ricognizione |
| `ReturnToGrid` | true | Riporta in griglia per la partenza da fermo |
| `GridHoldSeconds` | 6 | Attesa in griglia prima delle luci |
| `LightsMinSeconds` / `LightsMaxSeconds` | 1,5 / 4 | Intervallo variabile prima del verde |
| `AddLapToRaceDistance` | true | Aggiunge il giro di ricognizione al preset |

Si attiva o disattiva per sessione, nella colonna *Formazione* della scheda Calendario.

## Il permesso che serve

Le funzioni `physics.*` sono disponibili **solo se la pista le autorizza**. È una misura di
sicurezza di Custom Shaders Patch, non un limite di Pitwall.

Nel gestionale, scheda **Circuiti**, il pulsante **Abilita script di physics** lo fa per tutti i
circuiti del campionato: aggiunge una sezione a `surfaces.ini` e cambia un valore, dopo aver
messo da parte una copia dell'originale. **Ripristina piste** rimette tutto com'era.

Due cose da sapere prima di premerlo:

- è una **modifica ai dati del gioco**, per quanto minima e reversibile;
- una pista con physics estesa **non vale più per le classifiche online**.

Senza quel permesso l'app in pista te lo dice e resta un overlay: vedi safety car, delta e radio,
ma l'IA continua a correre per conto suo.

## Regolazioni

Nel file del campionato, sotto `Rules.RaceControl.SafetyCar`:

| Impostazione | Predefinito | Cosa fa |
|---|---|---|
| `UseRealCar` | true | Inserisce la vettura in griglia |
| `CarId` | vuoto | Auto da usare; vuoto sceglie la prima disponibile |
| `PaceKmh` | 120 | Ritmo della safety car in pista |
| `BunchingLaps` | 1 | Giri di ricompattamento |
| `AllowUnlapping` | true | Consente lo sdoppiaggio |
| `UnlappingLaps` | 1 | Giro dedicato allo sdoppiaggio |

Il direttore dell'IA si regola da codice con `DegradationStrength` (1 è realistico, 0 disattiva
il degrado), `RetirementProbability` e `MistakeProbability`.

## Provarlo senza guidare

```
cd tools\sim
dotnet run -- --rounds 1 --laps 35
```

Stampa la strategia decisa per ogni avversario prima del via, e poi la cronaca della direzione
gara. Una corsa tipo:

```
[giro 23] SAFETY CAR IN PISTA
[giro 24] SAFETY CAR
[giro 25] DOPPIATI: SDOPPIARSI
[giro 26] SAFETY CAR RIENTRA QUESTO GIRO
[giro 27] VERDE
```
