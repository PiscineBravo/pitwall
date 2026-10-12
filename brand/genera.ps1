# Genera i PNG e l'icona di Windows dal marchio di Pitwall.
#
# Il disegno e ripetuto qui invece di convertire l'SVG perche su questa macchina non c'e un
# convertitore, e aggiungerne uno vorrebbe dire una dipendenza in piu per una cosa che si fa
# una volta ogni tanto. Le misure sono le stesse di brand/pitwall.svg: se si cambia una, va
# cambiata anche la.

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$cartella = $PSScriptRoot
$misure = 512, 256, 128, 64, 48, 32, 16

function Disegna([int]$lato) {
  $bmp = New-Object System.Drawing.Bitmap($lato, $lato)
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
  $g.Clear([System.Drawing.Color]::Transparent)

  # Tutto il disegno e pensato su una griglia 64x64 e scalato al lato richiesto.
  $k = $lato / 64.0

  $sfondo  = New-Object System.Drawing.SolidBrush ([System.Drawing.ColorTranslator]::FromHtml('#14171C'))
  $chiaro  = New-Object System.Drawing.SolidBrush ([System.Drawing.ColorTranslator]::FromHtml('#E8ECF2'))
  $spento  = New-Object System.Drawing.SolidBrush ([System.Drawing.ColorTranslator]::FromHtml('#93A0B0'))
  $rosso   = New-Object System.Drawing.SolidBrush ([System.Drawing.ColorTranslator]::FromHtml('#E10600'))
  $asfalto = New-Object System.Drawing.SolidBrush ([System.Drawing.ColorTranslator]::FromHtml('#333A45'))

  # Lo sfondo con gli angoli smussati.
  $raggio = [int]([Math]::Max(2, 12 * $k))
  $p = New-Object System.Drawing.Drawing2D.GraphicsPath
  $d = $raggio * 2
  $p.AddArc(0, 0, $d, $d, 180, 90)
  $p.AddArc(($lato - $d), 0, $d, $d, 270, 90)
  $p.AddArc(($lato - $d), ($lato - $d), $d, $d, 0, 90)
  $p.AddArc(0, ($lato - $d), $d, $d, 90, 90)
  $p.CloseFigure()
  $g.FillPath($sfondo, $p)

  # Gli schermi e il muretto su cui sono appoggiati.
  $g.FillRectangle($chiaro, (New-Object System.Drawing.RectangleF(($k*14), ($k*24), ($k*13), ($k*10))))
  $g.FillRectangle($chiaro, (New-Object System.Drawing.RectangleF(($k*29), ($k*19), ($k*13), ($k*15))))
  $g.FillRectangle($spento, (New-Object System.Drawing.RectangleF(($k*44), ($k*27), ($k*9), ($k*7))))
  $g.FillRectangle($rosso, (New-Object System.Drawing.RectangleF(($k*8), ($k*34), ($k*48), ($k*8))))

  $g.Dispose()
  return $bmp
}

foreach ($m in $misure) {
  $bmp = Disegna $m
  $percorso = Join-Path $cartella "pitwall-$m.png"
  $bmp.Save($percorso, [System.Drawing.Imaging.ImageFormat]::Png)
  $bmp.Dispose()
  "scritto pitwall-$m.png"
}

"fatto"
