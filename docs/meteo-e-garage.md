# Meteo dinamico e computer di garage

Due componenti che girano dentro Assetto Corsa, installate in `apps\lua` e in
`extension\weather-controllers`, visibili nella barra laterale del gioco.

## Il meteo che cambia davvero

Assetto Corsa, da solo, tiene il tempo che hai scelto in Quick Drive dall'inizio alla fine.
Custom Shaders Patch però usa un **controller meteo**: uno script che decide, fotogramma per
fotogramma, che tempo fa. Pitwall ne installa uno suo.

Il piano nasce nel generatore meteo del campionato — clima del circuito, mese, seme del
weekend — ed è una serie di punti:

```
min   0   Coperto            pioggia  0%   17.7 C
min  34   Coperto            pioggia  0%   16.7 C
min  39   Pioggia leggera    pioggia 15%   15.7 C
min  48   Pioggia forte      pioggia 70%   14.7 C
```

Il plugin SimHub lo pubblica, il controller lo esegue: trova dove sei nella serie, interpola
tipo di cielo, pioggia, temperature e vento, e passa il risultato a CSP con `ac.setConditionsSet2`.
Il passaggio fra due punti non è uno scatto — `transition` segue una curva morbida, così il
cielo si chiude piano e la pioggia arriva come arriva davvero.

Tre livelli di bagnato, perché sono tre cose diverse:

| Valore | Cosa rappresenta | Come si muove |
|---|---|---|
| `rainIntensity` | quanto piove **adesso** | segue il piano |
| `rainWetness` | pista bagnata | insegue la pioggia in fretta |
| `rainWater` | pozzanghere | si forma e si asciuga **piano** |

È l'ultima che fa la differenza fra una pista che si bagna e una che resta allagata mezz'ora
dopo che ha smesso.

### Attivarlo

`INSTALLA.cmd` copia il controller fra quelli disponibili, ma CSP ne usa uno alla volta.
Nel gestionale, scheda **Circuiti** → **Attiva meteo dinamico**: scrive la scelta nella tua
configurazione utente di CSP, mettendo da parte quella attuale. **Ripristina meteo** la rimette.

In alternativa a mano: Content Manager → Settings → Custom Shaders Patch → Weather FX →
Controller → *Pitwall*.

Se Pitwall non sta girando, il controller non combina guai: dopo quindici secondi senza dati
torna alle condizioni scelte in Quick Drive. Si regola in
`extension\weather-controllers\pitwall\settings.ini`.

## Il computer di garage

L'app **Pitwall Garage** è il monitor che in Formula 1 sta appoggiato al muretto e che il pilota
guarda quando rientra. Per la stessa ragione per cui esiste nella realtà, qui si consulta **solo
da fermi ai box**: in pista hai l'overlay piccolo, non un cruscotto di dati. Fuori dal garage
mostra solo round, sessione e stato della direzione gara.

Cinque pagine.

**METEO** — il radar. Al centro ci sei adesso, gli anelli sono +15, +30 e +45 minuti: le fasce
azzurre sono la pioggia in arrivo, tanto più vicine al centro quanto prima arriva. Sotto, la
striscia dell'intera sessione colorata per tipo di tempo, con il segnalino su adesso, e i
prossimi punti del piano in chiaro.

È una previsione sul tempo, non una mappa: Pitwall sa *quando* pioverà, non *dove* — in una
sessione di Assetto Corsa piove su tutta la pista insieme.

**AUTO** — usura delle quattro gomme con la loro barra, pressioni, temperature, giri allo stint
e quanti ne restano prima del degrado; benzina, consumo al giro e autonomia; acqua, olio, freni,
ERS, danni.

**TEMPI** — il monitor completo: posizione, distacco, miglior giro, mescola, soste.

**TELEMETRIA** — gas e freno, marcia, giri motore, DRS, e il grafico degli ultimi dieci giri.

**ANALISI** — passo medio contro miglior giro (quanto stai gestendo), la chiamata strategica e la
finestra di sosta, lo stato della direzione gara con penalità e track limits, e la strategia
prevista per ogni avversario.

Tutti i dati arrivano dal plugin attraverso `Documents\Pitwall\live\state.json`, letto due volte
al secondo. L'app non calcola nulla: tutto è stato deciso da Pitwall, questa è la vetrina.

## Le tre app in pista

| App | Quando | Cosa |
|---|---|---|
| **Pitwall** | sempre | Direzione gara, delta safety car, radio, penalità. Guida anche l'IA e la safety car |
| **Pitwall Garage** | ai box | Radar meteo, vettura, tempi, telemetria, analisi |
| *controller meteo* | sempre | Non è un'app: è lo script che fa cambiare il tempo |

Si abilitano in Content Manager → Settings → Custom Shaders Patch → Lua apps, e poi si aprono
dalla barra laterale di Assetto Corsa.
