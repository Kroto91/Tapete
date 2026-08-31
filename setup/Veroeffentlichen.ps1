# Baut eine neue Fassung von Tapete: Version setzen, uebersetzen, beide Setups
# erzeugen. Das Hochladen zu GitHub macht der Nutzer selbst - dafuer braucht es
# einen Zugang, den dieses Skript bewusst nicht anfasst.
#
# Aufruf:  .\Veroeffentlichen.ps1 -Version 1.1.0

param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version
)

$ErrorActionPreference = 'Stop'
$wurzel = Split-Path $PSScriptRoot -Parent
$iscc = "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"

if (-not (Test-Path $iscc)) { throw "Inno Setup fehlt: $iscc" }

Write-Host ""
Write-Host "  Tapete $Version bauen"
Write-Host "  ====================="
Write-Host ""

# --- 1. Version in die Projektdatei schreiben

$csproj = Join-Path $wurzel 'Tapete.csproj'
$inhalt = Get-Content $csproj -Raw
$neu = $inhalt -replace '<Version>[\d.]+</Version>', "<Version>$Version</Version>"
if ($neu -eq $inhalt) { Write-Host "  Version stand schon auf $Version" }
else {
    [System.IO.File]::WriteAllText($csproj, $neu, (New-Object System.Text.UTF8Encoding($false)))
    Write-Host "  Tapete.csproj auf $Version gesetzt"
}

# Das Setup-Skript zieht seine Version aus derselben Zahl.
$iss = Join-Path $PSScriptRoot 'Tapete.iss'
$i = Get-Content $iss -Raw
$i2 = $i -replace '#define Version   "[\d.]+"', "#define Version   `"$Version`""
if ($i2 -ne $i) {
    [System.IO.File]::WriteAllText($iss, $i2, (New-Object System.Text.UTF8Encoding($false)))
    Write-Host "  Tapete.iss auf $Version gesetzt"
}

# --- 2. Uebersetzen

Write-Host ""
Write-Host "  Uebersetzen ..."
Push-Location $wurzel
try {
    dotnet publish Tapete.csproj -c Release -o fertig --nologo | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish ist fehlgeschlagen" }
} finally { Pop-Location }

foreach ($n in 'Tapete.exe', 'mpv.exe', 'd3dcompiler_43.dll') {
    $f = Join-Path $wurzel "fertig\$n"
    if (-not (Test-Path $f)) { throw "Im Ordner fertig fehlt $n" }
    Write-Host ("     {0,-22} {1,6:N1} MB" -f $n, ((Get-Item $f).Length / 1MB))
}

# --- 3. Beide Setups

Write-Host ""
Write-Host "  Setup ohne Videos ..."
& $iscc $iss | Out-Null
if ($LASTEXITCODE -ne 0) { throw "ISCC ist fehlgeschlagen" }

Write-Host "  Setup mit Videos ..."
& $iscc /DMitVideos $iss | Out-Null
if ($LASTEXITCODE -ne 0) { throw "ISCC mit Videos ist fehlgeschlagen" }

# --- 4. Ergebnis

Write-Host ""
Write-Host "  Fertig:"
Get-ChildItem "E:\Claude\Tapete-Setup-$Version*.exe" |
    ForEach-Object { Write-Host ("     {0,-44} {1,6:N1} MB" -f $_.Name, ($_.Length / 1MB)) }

Write-Host ""
Write-Host "  Naechste Schritte von Hand:"
Write-Host "    git add -A; git commit -m `"Fassung $Version`"; git tag v$Version; git push --follow-tags"
Write-Host "    https://github.com/Kroto91/Tapete/releases/new?tag=v$Version"
Write-Host "    Dort NUR Tapete-Setup-$Version.exe anhaengen, nichts weiter."
Write-Host ""
Write-Host "    Das Setup mit den Videos bleibt hier. Die Videos stammen aus dem"
Write-Host "    Netz und sind nicht zur Weiterverbreitung gedacht; auf einem steht"
Write-Host "    ein Wasserzeichen von moewalls.com. Fuer die Update-Funktion wird"
Write-Host "    es auch nicht gebraucht - Tapete sucht ausdruecklich das Setup"
Write-Host "    OHNE `"Videos`" im Namen."
Write-Host ""
