# Scenari di strategia e monitor dei tempi

## Il muretto propone, tu scegli

In gara, a ogni giro, Pitwall ricostruisce fino a quattro scenari e li mette sul tavolo. Un
esempio vero, preso da una gara simulata con la pioggia in arrivo:

```
A) Pioggia: Intermediate   Box al giro 30, quando arriva l'acqua   +  0.0s   P1    rischio 2  <- consigliato
B) Fino in fondo           Nessuna sosta, si arriva con queste     + 59.5s   P13   rischio 1
C) Una sosta, Soft         Box al giro 23, poi soft                + 64.7s   P14   rischio 2
D) Pioggia: Full Wet       Box al giro 30, quando arriva l'acqua   + 66.8s   P15   rischio 3
```

Quei 59 secondi fra A e B sono il costo di non fermarsi quando arriva l'acqua, e le nove
posizioni accanto sono quello che costa in classifica. È la differenza fra due scenari a far
decidere, non il numero in sé.

In una gara asciutta la stessa schermata diventa un'altra domanda:

```
A) Fino in fondo           Nessuna sosta                           +  0.0s   P1    rischio 1  <- consigliato
B) Una sosta, Soft         Box al giro 25, poi soft                +  7.5s   P1    rischio 2
C) Una sosta, Medium       Box al giro 23, poi medium              + 19.7s   P4    rischio 1
D) Una sosta, Hard         Box al giro 23, poi hard                + 31.5s   P6    rischio 1
```

### Come si sceglie

| Dove | Come |
|---|---|
| **Volante** | Azioni `SelectStrategyA`, `B`, `C`, `D` da mappare sui pulsanti in SimHub |
| **Computer di garage** | Scheda STRATEGIA, pulsante *Scegli* su ogni scenario |
| **Gestionale** | Scheda Live |

Con `AskStrategy` su un pulsante, l'ingegnere te li legge mentre guidi: *"Scenario A, pioggia
intermediate, box al giro 30, il più veloce. Scenario B, fino in fondo, 59 secondi in più."*

L'ingegnere annuncia gli scenari da solo quando il consiglio **cambia** — non a ogni giro, che
sarebbe rumore.

### Come sono calcolati

Il modello è quello che farebbe un ingegnere su un foglio, non una simulazione dell'intera gara:

- ogni giro costa il tempo di riferimento, più il ritardo della mescola, più quello che il
  degrado aggiunge mano a mano che lo stint invecchia;
- oltre il limite di usura la gomma non cala: crolla, e il modello lo riflette;
- una gomma appena montata è fredda per un giro o due;
- **la gomma sbagliata per l'acqua di quel momento costa carissimo** — le slick sul bagnato
  fino a tredici secondi al giro. Senza questo pezzo, "resta fuori" risulterebbe sempre la
  scelta più veloce, che è il contrario di quello che succede in pista;
- ogni sosta costa il transito in corsia più la fermata, presi dal profilo del circuito.

Il meteo viene campionato **lap per lap sul piano della sessione**: uno scenario che va bene
adesso e male fra dieci minuti viene valutato per quello che sarà, non per com'è.

La posizione prevista si ricava dai distacchi reali: quante vetture ti passano, o quante ne
passi, con il tempo che quello scenario costa. È una stima, e come tale va letta.

## Il monitor dei tempi

La scheda TEMPI del computer di garage è fatta come un monitor di gara vero:

| Colonna | Cosa dice |
|---|---|
| `+/-` | Posizioni guadagnate dal via, verde se in attivo |
| `DISTACCO` | Dal leader |
| `INTERV.` | Dalla vettura davanti — il numero che dice se puoi provarci |
| `ULT. GIRO` | Ultimo giro completo |
| `S1 S2 S3` | Settori dell'ultimo giro |
| `GOM` | Mescola |
| `ETA` | Giri percorsi su quel treno di gomme, ambra oltre i 25 |
| `ST` | Soste, oppure BOX mentre è in corsia |

I colori sono quelli dei monitor veri: **viola** per il miglior tempo della sessione, **verde**
per il proprio personale. Si legge il colore prima del numero, ed è il punto.

L'età delle gomme viene contata da Pitwall osservando i passaggi in corsia box, perché il gioco
non la espone: è l'informazione che dice se chi hai davanti sta per cedere.
