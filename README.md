# Pitwall

Campionato in stile Formula 1 per **Assetto Corsa**, costruito attorno a **SimHub**.

Weekend di gara completi (prove libere, qualifiche, sprint, gara), gestione gomme con degrado e
finestra di sosta, ingegnere di pista che parla, direzione gara con bandiere, **virtual safety
car**, **safety car** e **giro di formazione**, meteo dinamico con previsioni, penalità
automatiche e classifiche piloti e costruttori che si aggiornano da sole a fine sessione.

---

## Le tre parti

| Componente | Dove gira | A cosa serve |
|---|---|---|
| **Plugin SimHub** (`User.Pitwall.dll`) | dentro SimHub | Legge la telemetria, fa girare direzione gara, gomme e ingegnere, espone tutto come proprietà SimHub per dashboard e volante, salva i risultati |
| **Pitwall Manager** (`Pitwall.Manager.exe`) | app desktop a parte | Campionati, calendario, piloti, team, circuiti, regolamento, classifiche, muretto box dal vivo |
| **App in-game** (Lua per CSP) | dentro Assetto Corsa | Overlay della direzione gara, barra del delta, radio; se la pista lo consente **rallenta davvero l'IA** sotto safety car |

Le tre parti si parlano attraverso file in `Documenti\Pitwall`. Nessun servizio, nessuna rete:
ognuna funziona anche se le altre non sono in esecuzione.

```
Documenti\Pitwall\
  championships\<id>.json     un campionato completo per file
  circuits.json               libreria circuiti
  live\state.json             stato pubblicato dal plugin (letto dal Manager)
  live\race_control.txt       versione compatta letta dall'app in-game
  live\command.json           comandi dal Manager al plugin
  exports\                    classifiche esportate in CSV
```

---

## Installazione

Serve l'**SDK .NET 8** (già installato su questa macchina), **SimHub** e **Assetto Corsa**.

```powershell
.\build.ps1
```

Lo script compila tutto e poi:

1. copia il plugin nella cartella di SimHub — **chiudi SimHub prima**, e se la copia viene negata
   riapri PowerShell come amministratore, perché SimHub sta in `Program Files`;
2. copia l'app Lua in `assettocorsa\apps\lua\pitwall`;
3. pubblica il gestionale in `dist\Manager\Pitwall.Manager.exe`.

Poi, dentro SimHub: **Settings → Plugins**, abilita **Pitwall** e riavvia SimHub. In Content
Manager, **Settings → Custom Shaders Patch → Lua apps**, abilita **Pitwall**.

---

## Primo avvio

Apri `Pitwall.Manager.exe`. Se non trova nessun campionato ne crea uno già pronto: venti piloti,
dieci team con le livree del pack `vrc_formula_alpha_2026_csp` e il calendario completo con i
tracciati installati su questa macchina. Tre cose da controllare subito:

1. **Piloti** — spunta `Tu` sul pilota che guiderai davvero.
2. **Piloti → Nome AC** — deve coincidere carattere per carattere con il nome nella entry list di
   Assetto Corsa. È la chiave con cui la telemetria viene riconosciuta: se non coincide, il
   pilota non compare nei risultati.
3. **Circuiti → Giro rif. (s)** — il tempo sul giro di riferimento. Da lì si calcola il ritmo
   sotto safety car e virtual safety car, quindi un valore sbagliato rende il delta impossibile
   da rispettare.

Poi, semplicemente, corri. Il plugin riconosce da solo circuito e sessione, li aggancia al
calendario e a fine sessione scrive la classifica nel campionato.

### Preset di Content Manager

Scheda **Calendario → Preset per Content Manager**: scrive un preset di Quick Drive per ogni gara
del calendario — griglia completa, tracciato con il layout giusto, ora, temperatura e giri —
raggruppati in una cartella col nome del campionato. Anche da riga di comando, con
`Pitwall.Manager.exe --export-cm`.

Il formato non è documentato ed è stato ricavato dall'eseguibile di Content Manager. Un preset è
un JSON su tre livelli, ognuno annidato nel precedente come stringa:

```
QuickDrive.SaveableData
  Mode = /Pages/Drive/QuickDrive_Weekend.xaml, CarId, TrackId, Time, Temperature ...
  ModeData ──► QuickDrive_Weekend.ViewModel.SaveableData
                 PracticeLength, QualificationLength,
                 Penalties, JumpStartPenalty, LapsNumber
                 RaceGridSerialized ──► RaceGridViewModel.SaveableData
                                          ModeId, CarIds[], Names[], SkinIds[],
                                          AiLevels[], AiAggressions[], OpponentsNumber ...
```

Si usa la modalità fine settimana e non quella gara: Content Manager fa girare libere, qualifiche
e gara una dopo l'altra, e la griglia esce dalle qualifiche invece di essere decisa a tavolino.
La modalità gara resta disponibile togliendo la spunta.

Le modalità di griglia previste da Content Manager sono `same_car`, `same_group`, `manual` e
`filtered_by_*`. Pitwall usa sempre `manual`: è l'unica in cui ogni vettura, nome e livrea sono
elencati uno per uno, che è esattamente quello che serve a un campionato.

Se si sceglie un preset esistente come base, ne vengono conservate assist, condizioni della pista
e ogni altra opzione, e Pitwall sostituisce solo pista, condizioni e griglia.

---

## Come funzionano safety car e giro di formazione

Assetto Corsa, in single player, **non ha** safety car né giro di formazione. Pitwall fa questo:

- **Giro di formazione** — ricognizione a ritmo ridotto con posizioni congelate, poi tutte le vetture
  vengono **riportate in griglia** e tenute ferme, semaforo con ritardo variabile, e partenza da
  fermo vera. Si disattiva per sessione dalla scheda Calendario.
- **Virtual safety car** — quando la direzione gara rileva un incidente calcola un ritmo obiettivo
  (60% del passo gara, configurabile) e ti mostra un **delta**: positivo vuol dire che stai
  rispettando la neutralizzazione, negativo che stai andando troppo forte. Se resti sotto il
  delta abbastanza a lungo arriva una penalità di 5 secondi — **una sola per periodo**, come fanno
  i commissari.
- **Safety car** — stesso meccanismo con un ritmo più lento, pit lane chiusa nei primi giri,
  fase "safety car rientra questo giro" e penalità per i sorpassi in regime di neutralizzazione.
- **Bandiera rossa** e **bandiere gialle di settore** per gli incidenti minori.

Gli incidenti vengono dedotti dal comportamento in pista, perché il gioco non li comunica: vetture
ferme lontano dai box, vetture sparite dal monitor, picchi di danni sulla tua auto. Se l'incidente
diventa VSC o safety car piena dipende dal profilo del circuito (`Prob. SC` nella scheda Circuiti):
Monaco e Baku escalano spesso, Barcellona quasi mai.

**La safety car è una vettura vera**: Pitwall inserisce in griglia una ventunesima iscritta — la
Mercedes AMG GT Black Series — la tiene ai box tutta la gara e la manda in pista quando serve,
con le cinque fasi del regolamento (uscita, ricompattamento, sdoppiaggio dei doppiati, ultimo
giro, ripartenza). In classifica non compare.

**Il direttore dell'IA** decide prima del via la strategia di ogni avversario — soste, mescole,
forma del weekend, ritiri — e poi la esegue con le funzioni `physics.*` di CSP: le gomme calano
davvero, le soste avvengono al giro previsto, sotto la pioggia si passa alle intermedie. È quello
che rende la strategia reale invece che decorativa.

**Il permesso che serve.** Tutto questo passa da `physics.*`, che la pista deve autorizzare. Il
gestionale lo fa per te dalla scheda Circuiti, con backup e ripristino. Senza, la procedura vale
per te e per la classifica, ma l'IA continua a correre per conto suo. Vedi
[docs/ia-e-safety-car.md](docs/ia-e-safety-car.md) e [docs/safety-car.md](docs/safety-car.md).

---

## Cosa dice l'ingegnere

Una trentina di regole in italiano e in inglese, con più varianti per riga in modo che non suoni
come un disco. Parla di: temperature e usura gomme, giri che restano prima del degrado, benzina,
finestra di sosta e mescola consigliata, minaccia di undercut quando un avversario vicino ferma,
distacchi e tendenza davanti e dietro, pioggia in arrivo con i minuti, cambio gomme per
intermedie, full wet o slick, neutralizzazioni e delta, penalità e track limits, danni, conto alla
rovescia finale.

La radio è governata da una coda con priorità e pausa minima fra i messaggi: le comunicazioni
urgenti passano davanti, il resto aspetta. Le frasi si leggono ad alta voce con la sintesi vocale
di Windows, e la voce si sceglie nel pannello SimHub.

---

## Proprietà per dashboard e volante

Il plugin espone un centinaio di proprietà sotto il prefisso `Pitwall.`, utilizzabili in qualsiasi
dashboard SimHub o mandate al display del volante:

```
PitwallPlugin.RaceControl.Status        Green, LocalYellow, VirtualSafetyCar, SafetyCar, RedFlag ...
PitwallPlugin.RaceControl.Text          etichetta pronta da mostrare
PitwallPlugin.RaceControl.Delta         delta da rispettare, positivo = a posto
PitwallPlugin.Penalty.PendingSeconds    secondi di penalità accumulati
PitwallPlugin.Penalty.TrackLimits       infrazioni ai limiti pista
PitwallPlugin.Tyres.Code                S, M, H, I, W
PitwallPlugin.Tyres.WearPercent         usura del pneumatico peggiore
PitwallPlugin.Tyres.LapsToCliff         giri prima del crollo di prestazione
PitwallPlugin.Strategy.Advice           la chiamata strategica in chiaro
PitwallPlugin.Strategy.WindowOpen       la finestra di sosta e aperta
PitwallPlugin.Engineer.Message          ultima radio
PitwallPlugin.Weather.MinutesToRain     minuti alla pioggia, -1 se non prevista
PitwallPlugin.Timing.1..24.*            monitor tempi, riga per riga
```

Eventi che puoi agganciare a LED o effetti: `SafetyCarDeployed`, `VirtualSafetyCarDeployed`,
`GreenFlag`, `PenaltyIssued`, `EngineerMessage`, `SessionCompleted`.

Azioni mappabili su pulsanti del volante: `RepeatLastMessage`, `AskStrategy`, `AskGaps`,
`AskTyres`, `ToggleEngineer`, `ToggleSpeech`, oltre ai comandi manuali di direzione gara.

---

## Il simulatore

```powershell
cd tools\sim
dotnet run -- --rounds 1 --laps 30 --verbose
```

Fa correre una gara intera con telemetria sintetica, senza Assetto Corsa e senza SimHub, e stampa
direzione gara, radio, ordine d'arrivo, punti e classifiche. Serve per due cose: verificare che
un cambiamento non abbia rotto niente, e tarare un campionato — degrado, frequenza delle safety
car, punteggi — guardando una stagione intera in pochi secondi.

---

## Struttura

```
src/Pitwall.Core        netstandard2.0, nessuna dipendenza dal gioco
  Model/                campionato, piloti, team, circuiti, calendario, sessioni
  Rules/                regolamento: punti, gomme, direzione gara, meteo, ingegnere
  Tyres/                mescole, degrado, finestra di sosta
  Weather/              generatore e script meteo con previsioni
  RaceControl/          incidenti, safety car, VSC, penalità
  Engineer/             regole radio, frasario italiano e inglese, coda messaggi
  Results/              classifiche di sessione, penalità applicate all'ordine d'arrivo
  Scoring/              punti e classifiche di campionato
  Live/                 telemetria normalizzata, stato live, orchestratore di sessione
  Storage/              file JSON, libreria circuiti, contenuti iniziali

src/User.Pitwall        net48, il plugin SimHub
src/Pitwall.Manager     net8.0-windows, il gestionale WPF
ac-app/pitwall          app Lua per Custom Shaders Patch
tools/sim               simulatore da riga di comando
```

`Pitwall.Core` non conosce né SimHub né Assetto Corsa: riceve un `TelemetryFrame` normalizzato.
È per questo che il simulatore può far girare la stessa identica logica senza il gioco, ed è
quello che renderà possibile la parte multiplayer senza riscrivere le regole.

---

## Stato e limiti

Funziona ed è compilato, ma **non è ancora stato provato in pista**. Cose da verificare alla prima
sessione reale, elencate in [docs/collaudo.md](docs/collaudo.md):

- l'orientamento dei valori di usura gomme (il plugin lo deduce da solo, ma il mod potrebbe
  riportare qualcosa di inatteso: c'è un'impostazione manuale nel pannello SimHub);
- la corrispondenza fra i nomi dei piloti nella entry list e quelli del campionato;
- i tempi sul giro di riferimento dei circuiti, da cui dipende il delta.

Non ancora implementato: qualifiche a eliminazione gestite segmento per segmento (la sessione
viene registrata come una qualifica unica), allocazione dei set di gomme per weekend, griglia di
partenza imposta al gioco dal risultato delle qualifiche, e il multiplayer su server dedicato.
