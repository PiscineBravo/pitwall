# Collaudo alla prima sessione

Pitwall compila e la logica è stata verificata con il simulatore, ma la telemetria vera è l'unica
prova che conta. Questa è la lista delle cose da guardare la prima volta, in ordine.

## 1. Il plugin si carica

SimHub → **Settings → Plugins**: deve comparire **Pitwall** ed essere abilitato. Nel menù di
sinistra appare la voce Pitwall con il riquadro **Stato attuale**.

Se non compare: controlla che `User.Pitwall.dll` e `Pitwall.Core.dll` siano nella cartella di
SimHub, e guarda il log di SimHub per un errore di caricamento.

## 2. Il circuito viene riconosciuto

Entra in una sessione qualsiasi. Nel riquadro Stato attuale, la riga `Round` deve mostrare il nome
della gara del calendario, non "Sessione libera" né "(pista non in calendario)".

Se non lo riconosce: nella scheda **Circuiti** del Manager, `Track AC` e `Layout` devono coincidere
con quello che usa il gioco. Il modo più rapido è **Scansiona** nella colonna di destra, che legge
direttamente la cartella `content\tracks`.

## 3. L'usura gomme ha il verso giusto

È la cosa più delicata. Ogni gioco e ogni mod riportano l'usura in modo diverso: alcuni danno la
vita residua (100 = gomma nuova), altri il consumo (0 = gomma nuova), su scala 0-1 oppure 0-100.
Il plugin lo deduce dal primo campione della sessione, quando le gomme sono nuove.

**Controlla così:** esci dai box con gomme nuove e guarda `Gomme ... usura` nel pannello SimHub.
Deve partire da circa 0% e salire girando. Se parte da 100% e scende, apri il pannello Pitwall,
**Telemetria → Usura gomme** e scegli a mano `Vita residua` invece di `Auto`.

Se questo valore è sbagliato, l'ingegnere ti chiamerà ai box al giro uno e la finestra di sosta
sarà senza senso.

## 4. I piloti vengono riconosciuti

Finisci una sessione breve e guarda la scheda **Classifiche → Ordine d'arrivo**. Tutti i nomi
devono essere quelli del campionato.

Se compaiono nomi diversi, o mancano piloti, il campo **Nome AC** nella scheda Piloti non coincide
con la entry list di Assetto Corsa. Copialo da Content Manager, dalla schermata di impostazione
degli avversari.

## 5. Il delta è rispettabile

Fai una gara breve e lascia che la direzione gara chiami una VSC, oppure forzala dal pulsante
**Virtual Safety Car** nel pannello SimHub o nella scheda Live del Manager.

Rallenta e guarda il delta: deve diventare positivo a un'andatura simile a quella di una VSC vera,
cioè sensibilmente più lenta del passo gara ma non a passo d'uomo. Se resta negativo anche andando
piano, il **Giro rif. (s)** del circuito è troppo basso: mettici il tuo tempo sul giro reale.

## 6. Il risultato viene salvato

A bandiera a scacchi, il log di SimHub deve riportare `Pitwall: risultato registrato`. Nel Manager,
premi **Aggiorna** nella scheda Classifiche: i punti devono comparire.

Se non succede, si può forzare con **Salva risultato** nella scheda Live, finché la sessione è
ancora aperta.

## 7. L'app in-game

Attivala in Content Manager, **Settings → Custom Shaders Patch → Lua apps**, poi aprila in pista
dal menù delle app. Deve mostrare lo stato della direzione gara entro un secondo.

Se dice "Pitwall non collegato", SimHub non sta pubblicando: verifica che il plugin sia attivo e
che esista `Documenti\Pitwall\live\race_control.txt`.

Se dice "IA non rallentabile", la pista non consente gli script di physics: vedi
[safety-car.md](safety-car.md). L'overlay funziona comunque.
