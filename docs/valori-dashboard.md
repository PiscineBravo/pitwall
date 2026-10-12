# Valori per la dashboard

Tutte le proprietà del plugin stanno sotto il prefisso `PitwallPlugin.`, e in una dashboard SimHub si
scrivono fra parentesi quadre: `[PitwallPlugin.SafetyCarOn]`.

I valori di questa prima tabella sono pensati per essere **scritti a mano** in un binding: nomi
corti, niente gerarchia da ricordare, e sempre booleani o numeri — mai stringhe da confrontare.

## Stato della gara

| Valore | Tipo | Quando è vero |
|---|---|---|
| `PitwallPlugin.GreenFlagOn` | bool | Si corre |
| `PitwallPlugin.YellowFlagOn` | bool | Bandiera gialla in almeno un settore |
| `PitwallPlugin.RedFlagOn` | bool | Bandiera rossa |
| `PitwallPlugin.ChequeredFlagOn` | bool | Bandiera a scacchi |
| `PitwallPlugin.NeutralisedOn` | bool | Safety car, VSC o giro di formazione |
| `PitwallPlugin.PitLaneClosedOn` | bool | Pit lane chiusa |
| `PitwallPlugin.PenaltyOn` | bool | Hai penalità da scontare |
| `PitwallPlugin.RaceControlCode` | int | 0 verde, 1 giallo, 2 VSC, 3 safety car, 4 rossa, 5 formazione, 6 scacchi |

Il codice numerico serve quando si vuole un solo valore da cui far dipendere tutto, per esempio
un colore di sfondo scelto con una tabella.

## Giro di formazione

| Valore | Tipo | Quando è vero |
|---|---|---|
| `PitwallPlugin.FormationLapOn` | bool | La procedura è in corso, in una qualsiasi delle sue fasi |
| `PitwallPlugin.FormationRolling` | bool | Giro di ricognizione |
| `PitwallPlugin.FormationOnGrid` | bool | Fermi in griglia |
| `PitwallPlugin.FormationLights` | bool | Semaforo acceso, verde imminente |
| `PitwallPlugin.FormationPhase` | string | `Idle`, `Rolling`, `Forming`, `Lights`, `Away` |
| `PitwallPlugin.LightsOut` | bool | Tre secondi dopo il verde — il momento buono per far lampeggiare i LED |

## Safety car e VSC

| Valore | Tipo | Quando è vero |
|---|---|---|
| `PitwallPlugin.SafetyCarOn` | bool | Safety car in pista, rientro compreso |
| `PitwallPlugin.SafetyCarDeploying` | bool | Sta uscendo dai box |
| `PitwallPlugin.SafetyCarBunching` | bool | Il gruppo si sta ricompattando |
| `PitwallPlugin.SafetyCarUnlapping` | bool | I doppiati si stanno sdoppiando |
| `PitwallPlugin.SafetyCarInThisLap` | bool | Rientra questo giro |
| `PitwallPlugin.SafetyCarPhase` | string | `Idle`, `Deploying`, `Bunching`, `LappedCarsUnlapping`, `InThisLap`, `Restarting` |
| `PitwallPlugin.VirtualSafetyCarOn` | bool | VSC attiva |
| `PitwallPlugin.DeltaValue` | double | Delta da rispettare; positivo vuol dire che sei a posto |
| `PitwallPlugin.DeltaOk` | bool | Il delta è rispettato |
| `PitwallPlugin.TargetSpeed` | double | Velocità obiettivo in km/h |

Una barra del delta si costruisce con `[PitwallPlugin.DeltaValue]` per la lunghezza e `[PitwallPlugin.DeltaOk]`
per il colore.

## Eventi

Si agganciano a LED, effetti o suoni:

`SafetyCarDeployed`, `VirtualSafetyCarDeployed`, `GreenFlag`, `YellowFlag`, `RedFlag`,
`FormationLapStarted`, `PenaltyIssued`, `EngineerMessage`, `SessionCompleted`.

## Azioni mappabili sui pulsanti

Radio e informazioni: `RepeatLastMessage`, `AskStrategy`, `AskGaps`, `AskTyres`,
`ToggleEngineer`, `ToggleSpeech`.

Strategia: `SelectStrategyA`, `SelectStrategyB`, `SelectStrategyC`, `SelectStrategyD`.

Computer di garage: `GarageNextPage`, `GaragePrevPage`, `GaragePageWeather`,
`GaragePageStrategy`, `GaragePageTiming`, `GaragePageCar`, `GaragePageTelemetry`,
`GaragePageAnalysis`.

Direzione gara, utili per provare una dashboard senza aspettare un incidente: `DeployVSC`,
`DeploySafetyCar`, `ResumeGreen`, `SaveSessionResult`.

## Il resto

Oltre a questi ci sono le proprietà per gomme, strategia, meteo, distacchi e monitor dei tempi,
elencate nel README sotto *Proprietà per dashboard e volante*. Le due famiglie convivono: queste
sono le scorciatoie, quelle la forma completa.
