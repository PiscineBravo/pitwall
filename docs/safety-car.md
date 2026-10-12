# Safety car, VSC e giro di formazione

## Cosa succede davvero

Assetto Corsa in single player non ha né safety car né giro di formazione. Pitwall li ricostruisce
su due livelli.

### Livello 1 — sempre attivo, solo dal plugin

La direzione gara di Pitwall decide, comunica e fa rispettare la procedura **a te**:

- calcola il ritmo obiettivo dalla lunghezza della pista e dal giro di riferimento del circuito;
- mostra il **delta**: `delta = tempo trascorso − tempo minimo per la distanza percorsa`.
  Positivo significa che stai rispettando la neutralizzazione, negativo che stai andando troppo
  forte;
- accumula lo sforamento e, superata la soglia, assegna **una** penalità di 5 secondi per periodo;
- rileva i sorpassi in regime di neutralizzazione confrontando la tua posizione con quella al
  momento del dispiegamento, ignorando i guadagni dovuti alle soste altrui;
- chiude la pit lane nei primi giri di safety car;
- applica tutto alla classifica che pubblica a fine sessione.

L'IA, in questo livello, continua a correre normalmente. La gara che vedi a schermo e la classifica
ufficiale di Pitwall possono quindi divergere: è la classifica di Pitwall quella che conta per il
campionato, esattamente come nella realtà l'ordine d'arrivo non è l'ultima parola finché i
commissari non hanno finito.

### Livello 2 — con l'app in-game, l'IA rallenta davvero

L'app Lua in `apps/lua/pitwall` legge lo stato della direzione gara e, quando è attiva una
neutralizzazione, chiama `physics.setAITopSpeed` su ogni vettura guidata dall'IA. Le vetture
continuano a guidare normalmente — frenano, stanno in traiettoria — semplicemente non superano più
la velocità obiettivo, e il gruppo si ricompatta.

Lo stesso vale per il giro di formazione, limitato a 110 km/h.

## Abilitare gli script di physics

Le funzioni `physics.*` di CSP sono disponibili **solo se la pista le autorizza**. È una misura di
sicurezza di CSP, non un limite di Pitwall. L'app controlla `physics.allowed()` e, se non le sono
concesse, lo scrive nella finestra e resta un semplice overlay.

Per autorizzarle su una pista, il file `surfaces.ini` della pista deve contenere:

```ini
[_SCRIPTING_PHYSICS]
ALLOW_TOOLS=1
```

e la physics estesa va attivata con una superficie dedicata:

```ini
[SURFACE_0]
WAV_PITCH=extended-0
```

Nota bene: modificare i dati di una pista significa modificare contenuti del gioco. Fallo su una
copia della pista, non sull'originale, e tieni presente che una pista con physics estesa non è più
compatibile con i tempi sul giro delle classifiche online.

## Regolazioni

Tutto nella scheda **Regolamento** del Manager.

| Impostazione | Predefinito | Cosa fa |
|---|---|---|
| Durata minima VSC | 60 s | Quanto dura almeno una virtual safety car |
| Giri minimi SC | 2 | Quanti giri resta fuori la safety car |
| Ritmo VSC | 60% | Percentuale del passo gara sotto VSC |
| Ritmo SC | 55% | Percentuale del passo gara dietro la safety car |
| Niente SC negli ultimi giri | 3 | Nessuna nuova neutralizzazione nel finale |

Per circuito, nella scheda **Circuiti**, `Prob. SC` decide quanto spesso un incidente qui diventa
safety car piena invece che virtual safety car. I valori iniziali seguono la realtà: Monaco 0.75,
Baku e Singapore 0.80, Barcellona 0.25.

Se il delta risulta impossibile da rispettare, quasi sempre il colpevole è il **giro di riferimento**
del circuito: da lì si ricava il passo gara. Correggilo nella scheda Circuiti con il tuo tempo sul
giro reale.
