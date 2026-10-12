# Pitwall — partire da qui

Campionato in stile Formula 1 per Assetto Corsa, con SimHub.

## Installazione

Doppio clic su **`INSTALLA.cmd`**.

Prima chiudi SimHub, altrimenti il plugin non può essere sostituito (i file sono bloccati
mentre il programma è aperto). Lo script copia il plugin dentro SimHub, l'app in-game dentro
Assetto Corsa, e mette un collegamento a Pitwall Manager sul desktop.

Poi due attivazioni da fare a mano, una volta sola:

- **SimHub** → `Settings` → `Plugins` → abilita **Pitwall** → riavvia SimHub.
- **Content Manager** → `Settings` → `Custom Shaders Patch` → `Lua apps` → abilita **Pitwall** e
  **Pitwall Garage**. Le trovi poi nella barra laterale di Assetto Corsa.

## Cosa gira dentro Assetto Corsa

| | |
|---|---|
| **Pitwall** | overlay di gara: direzione gara, delta safety car, radio. Guida anche l'IA e la safety car |
| **Pitwall Garage** | il computer del box, consultabile solo da fermi: radar meteo, **strategia**, tempi, vettura, telemetria, analisi |
| *controller meteo* | lo script che fa cambiare il tempo durante la sessione |

Il meteo dinamico si attiva dal gestionale, scheda **Circuiti** → **Attiva meteo dinamico**
(con ripristino). Dettagli in `docs\meteo-e-garage.md`.

**In gara il muretto ti propone fino a quattro scenari** — A, B, C, D — con quanto costa ognuno
e dove finiresti. Si scelgono dai pulsanti del volante (azioni `SelectStrategyA..D` da mappare
in SimHub), dalla scheda STRATEGIA del computer di garage, o dal gestionale. Con `AskStrategy`
su un pulsante l'ingegnere te li legge mentre guidi. Dettagli in `docs\strategia-e-tempi.md`.

## Prima di correre: tre cose

Apri **Pitwall Manager**. Se non trova campionati ne crea uno già pronto — venti piloti, dieci
team con le livree del pack `vrc_formula_alpha_2026_csp`, e il calendario con i 22 tracciati
installati su questo PC.

1. **Piloti** → spunta `Tu` sul pilota che guiderai.
2. **Piloti** → colonna `Nome AC`: deve coincidere **carattere per carattere** con il nome nella
   entry list di Assetto Corsa. È la chiave con cui la telemetria viene riconosciuta.
3. **Circuiti** → colonna `Giro rif. (s)`: il tuo tempo sul giro reale. Da lì si calcola il ritmo
   sotto safety car, quindi un valore sbagliato rende il delta impossibile da rispettare.

Premi **Salva**. Poi avvia SimHub, poi Assetto Corsa, e corri: il plugin riconosce da solo
circuito e sessione, li aggancia al calendario, e a fine sessione scrive la classifica.

## Come verificare che funzioni

`docs\collaudo.md` ha la lista in ordine delle sette cose da controllare la prima volta. La più
delicata è la terza: **il verso dei valori di usura gomme**. Esci dai box con gomme nuove e guarda
`usura` nel pannello Pitwall dentro SimHub — deve partire da 0% e salire. Se parte da 100% e
scende, nel pannello scegli `Telemetria → Usura gomme → Vita residua`.

Senza quella verifica l'ingegnere ti chiamerà ai box al primo giro.

## Preparare il campionato dentro Content Manager

Nella scheda **Calendario**, in basso a sinistra, c'è **Preset per Content Manager**: genera un
preset di Quick Drive per ogni gara, con pista, ora e temperatura già impostate. In Content
Manager li trovi raggruppati in una cartella col nome del campionato: scegli la gara e parti.

Non serve preparare niente a mano: la griglia viene costruita da Pitwall. Ogni preset usa la
modalità **Fine settimana** di Content Manager — prove libere, qualifiche e gara una dopo
l'altra, con la griglia di partenza che esce dalle qualifiche — e contiene l'auto, i diciannove
avversari con nome, livrea, abilità e aggressività di ciascuno, il tracciato con il layout
giusto, l'ora, la temperatura, la durata di libere e qualifiche e i giri di gara.

Le durate vengono prese dalla scheda Calendario: se lì le libere sono 60 minuti e le qualifiche
45, il preset userà quelle. Togliendo la spunta *Fine settimana* i preset partono direttamente
in gara.

Il menu **Base facoltativa** serve solo se vuoi partire da un tuo preset: in quel caso Pitwall
conserva assist, condizioni della pista e tutte le altre opzioni che avevi scelto, e sostituisce
soltanto pista, condizioni e griglia. Lasciandolo vuoto parte da una base predefinita.

Nella griglia c'è anche la **safety car** — la Mercedes AMG GT Black Series — come iscritta in
più. Resta parcheggiata ai box tutta la gara e scende in pista solo quando serve; in classifica
non compare.

Una cosa che il preset non imposta: la **livrea della tua auto**, perché il formato non la
prevede. La scegli in Quick Drive.

## Il passaggio che conta davvero

Scheda **Circuiti** → **Abilita script di physics**.

Senza quel permesso l'IA continua a correre per conto suo: niente soste strategiche, niente
degrado, e la safety car resta un overlay senza auto vera. È una misura di sicurezza di Custom
Shaders Patch, non un limite di Pitwall.

Il pulsante modifica `surfaces.ini` dei circuiti del campionato dopo averne messo da parte una
copia, e **Ripristina piste** rimette tutto com'era. Sappi però che è una modifica ai dati del
gioco, e che una pista con physics estesa non vale più per le classifiche online.

Il dettaglio di cosa fa l'IA e di come funziona la safety car è in `docs\ia-e-safety-car.md`.

Puoi anche rigenerarli da riga di comando, senza aprire la finestra:

```
installazione\manager\Pitwall.Manager.exe --export-cm
```

## Provare senza guidare

```
cd tools\sim
dotnet run -- --rounds 1 --laps 30 --verbose
```

Fa correre una gara intera con telemetria finta, senza gioco e senza SimHub, e stampa direzione
gara, radio dell'ingegnere, ordine d'arrivo, punti e classifiche. Serve per tarare degrado,
frequenza delle safety car e punteggi guardando una stagione in pochi secondi. Richiede l'SDK
.NET 8, che su questo PC è già installato.

## Cosa c'è in questa cartella

| | |
|---|---|
| `INSTALLA.cmd` | installa tutto |
| `AVVIA-MANAGER.cmd` | apre il gestionale senza installarlo |
| `installazione\` | i file già compilati: plugin, gestionale, app in-game |
| `src\` | i sorgenti C# |
| `ac-app\` | i sorgenti dell'app Lua |
| `tools\sim\` | il simulatore |
| `docs\` | safety car e VSC, lista di collaudo |
| `README.md` | documentazione completa del progetto |
| `build.ps1` | ricompila tutto da zero (serve l'SDK .NET 8) |

## Se qualcosa non va

- **Pitwall non compare in SimHub** → controlla che `User.Pitwall.dll` e `Pitwall.Core.dll` siano
  nella cartella di SimHub, e guarda il log di SimHub.
- **"pista non in calendario"** → nel Manager, scheda `Circuiti`, premi `Scansiona` e aggiungi il
  tracciato: i campi `Track AC` e `Layout` devono coincidere con quelli del gioco.
- **L'app in-game dice "Pitwall non collegato"** → SimHub non sta pubblicando. Verifica che il
  plugin sia abilitato e che esista `Documenti\Pitwall\live\race_control.txt`.
- **L'app in-game dice "IA non rallentabile"** → la pista non consente gli script di physics.
  L'overlay funziona lo stesso; il perché e come abilitarli è in `docs\safety-car.md`.

## Una cosa da sapere

Il progetto **non è ancora stato provato in pista**: compila, e la logica è stata verificata con
il simulatore, ma la telemetria vera è l'unica prova che conta. Aspettati qualche taratura da
sistemare alla prima sessione.

Assetto Corsa in single player non ha safety car né giro di formazione: Pitwall li gestisce e li
fa rispettare a te, e pubblica la classifica ufficiale con le penalità. Perché l'IA rallenti
davvero serve l'app in-game su una pista con gli script di physics abilitati.
