<#
.SYNOPSIS
    Installa Pitwall: plugin SimHub, app in-game per Assetto Corsa, gestionale.

.DESCRIPTION
    Copia i file gia compilati nella cartella "installazione" al loro posto. Non compila nulla e
    non serve l'SDK .NET: quello serve solo per ricompilare i sorgenti con build.ps1.

    Come si usa: tasto destro su questo file, "Esegui con PowerShell".
    Se Windows blocca lo script, apri PowerShell in questa cartella ed esegui:
        powershell -ExecutionPolicy Bypass -File .\INSTALLA.ps1

.EXAMPLE
    .\INSTALLA.ps1
    .\INSTALLA.ps1 -AcPath "D:\SteamLibrary\steamapps\common\assettocorsa"
    .\INSTALLA.ps1 -SoloPlugin
#>
[CmdletBinding()]
param(
    [string] $SimHubPath,
    [string] $AcPath,
    [switch] $SoloPlugin,
    [switch] $SenzaCollegamento
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$pacchetto = Join-Path $root 'installazione'

function Titolo($t) { Write-Host "`n== $t" -ForegroundColor Cyan }
function Ok($t)     { Write-Host "   $t" -ForegroundColor Green }
function Avviso($t) { Write-Host "   $t" -ForegroundColor Yellow }
function Errore($t) { Write-Host "   $t" -ForegroundColor Red }

Write-Host "Pitwall - installazione" -ForegroundColor Cyan

if (-not (Test-Path $pacchetto)) {
    Errore "Cartella 'installazione' non trovata accanto a questo script."
    exit 1
}

# ---- 1. Plugin SimHub ---------------------------------------------------------------------------

Titolo 'Plugin SimHub'

if (-not $SimHubPath) {
    $SimHubPath = $env:SIMHUB_INSTALL_PATH
    if (-not $SimHubPath) { $SimHubPath = 'C:\Program Files (x86)\SimHub\' }
}
$SimHubPath = $SimHubPath.TrimEnd('\') + '\'

if (-not (Test-Path (Join-Path $SimHubPath 'SimHub.Plugins.dll'))) {
    Avviso "SimHub non trovato in $SimHubPath"
    Avviso "Riesegui indicando il percorso:  .\INSTALLA.ps1 -SimHubPath 'D:\SimHub'"
}
elseif (Get-Process -Name 'SimHubWPF' -ErrorAction SilentlyContinue) {
    Avviso 'SimHub e aperto: i file sono bloccati e non posso sostituirli.'
    Avviso 'Chiudi SimHub e riesegui questo script.'
}
else {
    try {
        # Solo questi due file. Tutto il resto (Newtonsoft, log4net...) e gia dentro SimHub e
        # duplicarlo provoca conflitti di caricamento difficili da diagnosticare.
        Copy-Item (Join-Path $pacchetto 'plugin-simhub\User.Pitwall.dll') -Destination $SimHubPath -Force
        Copy-Item (Join-Path $pacchetto 'plugin-simhub\Pitwall.Core.dll') -Destination $SimHubPath -Force
        Ok "Plugin copiato in $SimHubPath"
        Ok 'Apri SimHub, vai in Settings > Plugins, abilita Pitwall e riavvia SimHub.'
    }
    catch {
        Avviso 'Copia negata: SimHub sta in Program Files e serve un prompt di amministratore.'
        Avviso 'Apri PowerShell come amministratore e riesegui questo script.'
    }
}

if ($SoloPlugin) { Write-Host "`nFatto." -ForegroundColor Cyan; exit 0 }

# ---- 2. App in-game -------------------------------------------------------------------------------

Titolo 'App in-game per Assetto Corsa'

if (-not $AcPath) {
    $candidati = @(
        'C:\Program Files (x86)\Steam\steamapps\common\assettocorsa',
        'C:\Program Files\Steam\steamapps\common\assettocorsa',
        'D:\SteamLibrary\steamapps\common\assettocorsa',
        'E:\SteamLibrary\steamapps\common\assettocorsa'
    )
    $AcPath = $candidati | Where-Object { Test-Path (Join-Path $_ 'content\tracks') } | Select-Object -First 1
}

if (-not $AcPath) {
    Avviso 'Assetto Corsa non trovato.'
    Avviso "Riesegui indicando il percorso:  .\INSTALLA.ps1 -AcPath 'D:\...\assettocorsa'"
}
else {
    # Due app distinte: l'overlay di gara, piccolo, e il computer di garage, che si consulta
    # solo da fermi. Tenerle separate vuol dire poterle aprire e chiudere indipendentemente.
    foreach ($app in 'pitwall', 'pitwall_garage') {
        $source = Join-Path $pacchetto "app-assetto-corsa\$app"
        if (-not (Test-Path $source)) { continue }
        $target = Join-Path $AcPath "apps\lua\$app"
        try {
            if (-not (Test-Path $target)) { New-Item -ItemType Directory -Path $target -Force | Out-Null }
            Copy-Item (Join-Path $source '*') -Destination $target -Force
            Ok "App $app copiata in $target"
        }
        catch {
            Avviso "Copia di $app non riuscita: $($_.Exception.Message)"
        }
    }
    Ok 'In Content Manager: Settings > Custom Shaders Patch > Lua apps, abilita Pitwall e Pitwall Garage.'

    # Controller meteo: lo si mette fra quelli disponibili. Selezionarlo e un'altra cosa, e la
    # fa il gestionale su richiesta, perche cambia una impostazione di Custom Shaders Patch.
    $wsource = Join-Path $pacchetto 'weather-controller\pitwall'
    if (Test-Path $wsource) {
        $wtarget = Join-Path $AcPath 'extension\weather-controllers\pitwall'
        try {
            if (-not (Test-Path $wtarget)) { New-Item -ItemType Directory -Path $wtarget -Force | Out-Null }
            Copy-Item (Join-Path $wsource '*') -Destination $wtarget -Force
            Ok "Controller meteo copiato in $wtarget"
            Ok 'Per attivarlo: Pitwall Manager, scheda Circuiti, Attiva meteo dinamico.'
        }
        catch {
            Avviso "Copia del controller meteo non riuscita: $($_.Exception.Message)"
        }
    }
}

# ---- 3. Gestionale -----------------------------------------------------------------------------------

Titolo 'Pitwall Manager'

$exe = Join-Path $pacchetto 'manager\Pitwall.Manager.exe'
if (-not (Test-Path $exe)) {
    Avviso 'Pitwall.Manager.exe non trovato nel pacchetto.'
}
else {
    Ok "Pronto: $exe"

    # Il gestionale gira su .NET 8 Desktop. Se manca, l'exe si apre e si chiude subito.
    $runtime = & dotnet --list-runtimes 2>$null | Select-String 'Microsoft.WindowsDesktop.App 8.'
    if (-not $runtime) {
        Avviso 'Manca il runtime .NET 8 Desktop. Scaricalo da:'
        Avviso 'https://dotnet.microsoft.com/download/dotnet/8.0  (Desktop Runtime x64)'
    }

    if (-not $SenzaCollegamento) {
        try {
            $desktop = [Environment]::GetFolderPath('Desktop')
            $link = Join-Path $desktop 'Pitwall Manager.lnk'
            $shell = New-Object -ComObject WScript.Shell
            $shortcut = $shell.CreateShortcut($link)
            $shortcut.TargetPath = $exe
            $shortcut.WorkingDirectory = Split-Path $exe
            $shortcut.Description = 'Pitwall Manager - campionato Assetto Corsa'
            $shortcut.Save()
            Ok "Collegamento creato sul desktop: Pitwall Manager"
        }
        catch {
            Avviso "Collegamento non creato: $($_.Exception.Message)"
        }
    }
}

# ---- Riepilogo ---------------------------------------------------------------------------------------

Write-Host "`n== Cosa fare adesso" -ForegroundColor Cyan
Write-Host '   1. Apri Pitwall Manager dal collegamento sul desktop.'
Write-Host '   2. Scheda Piloti: spunta "Tu" sul pilota che guidi, e controlla "Nome AC".'
Write-Host '   3. Scheda Circuiti: controlla "Giro rif. (s)" dei tracciati che userai.'
Write-Host '   4. Premi Salva.'
Write-Host '   5. Avvia SimHub (con Pitwall abilitato) e poi Assetto Corsa. Corri.'
Write-Host ''
Write-Host '   La lista di collaudo completa e in docs\collaudo.md' -ForegroundColor DarkGray
Write-Host ''
