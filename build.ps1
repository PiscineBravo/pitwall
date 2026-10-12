<#
.SYNOPSIS
    Compila Pitwall e lo installa dove serve.

.DESCRIPTION
    Tre destinazioni:
      - il plugin va nella cartella di SimHub (serve di solito un prompt di amministratore,
        perche SimHub sta in Program Files);
      - l'app in-game va in assettocorsa\apps\lua\pitwall;
      - il gestionale viene pubblicato in dist\Manager, da dove si avvia con un doppio clic.

.EXAMPLE
    .\build.ps1
    .\build.ps1 -SkipPlugin          # solo gestionale e app in-game
    .\build.ps1 -AcPath "D:\SteamLibrary\steamapps\common\assettocorsa"
#>
[CmdletBinding()]
param(
    [string] $Configuration = 'Release',
    [string] $SimHubPath,
    [string] $AcPath,
    [switch] $SkipPlugin,
    [switch] $SkipManager,
    [switch] $SkipAcApp
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$dist = Join-Path $root 'dist'

function Write-Step($text) { Write-Host "`n== $text" -ForegroundColor Cyan }
function Write-Ok($text)   { Write-Host "   $text" -ForegroundColor Green }
function Write-Warn2($text){ Write-Host "   $text" -ForegroundColor Yellow }

# ---- Percorsi ---------------------------------------------------------------------------------

if (-not $SimHubPath) {
    $SimHubPath = $env:SIMHUB_INSTALL_PATH
    if (-not $SimHubPath) { $SimHubPath = 'C:\Program Files (x86)\SimHub\' }
}
$SimHubPath = $SimHubPath.TrimEnd('\') + '\'

if (-not $AcPath) {
    $candidates = @(
        'C:\Program Files (x86)\Steam\steamapps\common\assettocorsa',
        'C:\Program Files\Steam\steamapps\common\assettocorsa',
        'D:\SteamLibrary\steamapps\common\assettocorsa'
    )
    $AcPath = $candidates | Where-Object { Test-Path (Join-Path $_ 'content\tracks') } | Select-Object -First 1
}

# ---- Compilazione ------------------------------------------------------------------------------

Write-Step "Compilazione ($Configuration)"
& dotnet build (Join-Path $root 'Pitwall.sln') -c $Configuration -v minimal --nologo -p:SkipSimHubInstall=true
if ($LASTEXITCODE -ne 0) { throw 'Compilazione non riuscita.' }
Write-Ok 'Compilato.'

$buildRoot = Join-Path $env:LOCALAPPDATA 'PitwallBuild\bin'

# ---- Plugin SimHub ------------------------------------------------------------------------------

if (-not $SkipPlugin) {
    Write-Step 'Plugin SimHub'
    if (-not (Test-Path (Join-Path $SimHubPath 'SimHub.Plugins.dll'))) {
        Write-Warn2 "SimHub non trovato in $SimHubPath. Usa -SimHubPath per indicarlo."
    } else {
        $pluginDir = Join-Path $buildRoot "User.Pitwall\$Configuration\net48"
        $files = @('User.Pitwall.dll', 'User.Pitwall.pdb', 'Pitwall.Core.dll', 'Pitwall.Core.pdb') |
                 ForEach-Object { Join-Path $pluginDir $_ } |
                 Where-Object { Test-Path $_ }

        $simhubRunning = Get-Process -Name 'SimHubWPF' -ErrorAction SilentlyContinue
        if ($simhubRunning) {
            Write-Warn2 'SimHub e in esecuzione: chiudilo prima di installare il plugin.'
        } else {
            try {
                Copy-Item $files -Destination $SimHubPath -Force
                Write-Ok "Plugin copiato in $SimHubPath"
            } catch {
                Write-Warn2 'Copia negata. SimHub sta in Program Files e serve un prompt di amministratore.'
                Write-Warn2 'Riapri PowerShell come amministratore ed esegui di nuovo questo script.'
            }
        }
    }
}

# ---- App in-game ---------------------------------------------------------------------------------

if (-not $SkipAcApp) {
    Write-Step 'App in-game per Assetto Corsa'
    if (-not $AcPath) {
        Write-Warn2 'Assetto Corsa non trovato. Usa -AcPath per indicarlo.'
    } else {
        $target = Join-Path $AcPath 'apps\lua\pitwall'
        try {
            if (-not (Test-Path $target)) { New-Item -ItemType Directory -Path $target -Force | Out-Null }
            Copy-Item (Join-Path $root 'ac-app\pitwall\*') -Destination $target -Recurse -Force
            Write-Ok "App copiata in $target"
            Write-Ok 'Attivala in Content Manager: Settings, Custom Shaders Patch, Lua apps.'
        } catch {
            Write-Warn2 "Copia non riuscita: $($_.Exception.Message)"
        }
    }
}

# ---- Gestionale -------------------------------------------------------------------------------------

if (-not $SkipManager) {
    Write-Step 'Pitwall Manager'
    $target = Join-Path $dist 'Manager'
    & dotnet publish (Join-Path $root 'src\Pitwall.Manager\Pitwall.Manager.csproj') `
        -c $Configuration -o $target --nologo -v minimal
    if ($LASTEXITCODE -ne 0) { throw 'Pubblicazione del gestionale non riuscita.' }
    Write-Ok "Pubblicato in $target"
    Write-Ok 'Avvialo con Pitwall.Manager.exe.'
}

Write-Host "`nFatto." -ForegroundColor Cyan
