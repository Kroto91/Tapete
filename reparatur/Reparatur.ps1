# Tapete: Reparatur der echten Benutzereinstellungen
#
# Warum es diese Datei gibt: Claude Code laeuft in einem MSIX-Container. Alles,
# was dort unter %APPDATA% oder in HKCU geschrieben wird, landet in dessen
# Umleitung statt im echten Benutzerprofil. Am 31.08.2026 sind deshalb drei
# Sachen nie angekommen. Dieses Skript holt sie nach - es muss aus dem Explorer
# gestartet werden, nicht aus Claude heraus.
#
# Administratorrechte werden nicht gebraucht, alles liegt unter HKCU und %APPDATA%.

$ErrorActionPreference = 'Continue'

Write-Host ""
Write-Host "  Tapete - Reparatur der echten Benutzereinstellungen"
Write-Host "  ==================================================="
Write-Host ""

# ---------- Sicherung zuerst ----------

$stempel = Get-Date -Format 'yyyy-MM-dd-HHmm'
$sicherung = Join-Path ([Environment]::GetFolderPath('Desktop')) "Tapete-Reparatur-Sicherung-$stempel"
New-Item -ItemType Directory -Force -Path $sicherung | Out-Null
reg export "HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run" "$sicherung\StartupApproved-Run.reg" /y | Out-Null
reg export "HKCU\Software\Microsoft\Windows\CurrentVersion\Run" "$sicherung\Run.reg" /y | Out-Null
Write-Host "  Sicherung liegt auf dem Desktop:"
Write-Host "    $sicherung"
Write-Host ""

# ---------- 1. Einstellungen fuer Tapete ----------

Write-Host "  1. Tapete-Einstellungen"
$ordner = Join-Path $env:APPDATA 'Tapete'
New-Item -ItemType Directory -Force -Path $ordner | Out-Null
$datei = Join-Path $ordner 'einstellungen.json'
$video = Join-Path $env:USERPROFILE 'Videos\Tapeten\solo-leveling-sung-jin-woo.mp4'

if (Test-Path $video) {
    $json = @{ LetztesVideo = $video; BeiVollbildPausieren = $true } | ConvertTo-Json
    # Ohne Bytemarke schreiben, damit der JSON-Leser nichts zu meckern hat.
    [System.IO.File]::WriteAllText($datei, $json, (New-Object System.Text.UTF8Encoding($false)))
    Write-Host "     geschrieben: $datei"
    Write-Host "     Video      : $(Split-Path $video -Leaf)"
} else {
    Write-Host "     UEBERSPRUNGEN - Video nicht gefunden:"
    Write-Host "     $video"
}
Write-Host ""

# ---------- 2. Autostart-Programme abschalten ----------

Write-Host "  2. Autostart abschalten"
Write-Host "     (nur ein Anzeigezustand, im Task-Manager jederzeit umkehrbar)"

$schluessel = 'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run'
$aus = @(
    'Discord', 'Spotify', 'Steam', 'EADM', 'Battle.net', 'RiotClient',
    'electron.app.CurseForge', 'Medal', 'FF Logs Uploader', 'Archon App',
    'Advanced SystemCare',
    'MicrosoftEdgeAutoLaunch_95BFFEA16DB23E0488694EEF76CBA43A'
)

if (-not (Test-Path $schluessel)) { New-Item -Path $schluessel -Force | Out-Null }
$vorhanden = Get-ItemProperty $schluessel

foreach ($name in $aus) {
    $alt = $vorhanden.PSObject.Properties[$name]
    if ($alt) {
        $wert = [byte[]]$alt.Value
        if ($wert[0] -eq 3 -or $wert[0] -eq 7) {
            Write-Host ("     {0,-42} war schon aus" -f $name)
            continue
        }
        $wert[0] = 3
    } else {
        $wert = [byte[]](3,0,0,0,0,0,0,0,0,0,0,0)
    }
    Set-ItemProperty -Path $schluessel -Name $name -Value $wert -Type Binary
    Write-Host ("     {0,-42} abgeschaltet" -f $name)
}
Write-Host ""
Write-Host "     Angelassen: OneDrive, Proton Drive, AMD-Rauschunterdrueckung,"
Write-Host "     Bloody2 und iCUE. iCUE steuert die Luefter, das bleibt an."
Write-Host ""

# ---------- 3. Toter Lively-Eintrag ----------

Write-Host "  3. Lively-Reste"
$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$lively = (Get-ItemProperty $runKey -ErrorAction SilentlyContinue).PSObject.Properties['Lively']
if ($lively) {
    if (Test-Path 'C:\Program Files\Lively Wallpaper\Lively.exe') {
        Write-Host "     Lively ist noch installiert - Eintrag bleibt unangetastet."
    } else {
        Remove-ItemProperty -Path $runKey -Name 'Lively' -ErrorAction SilentlyContinue
        Write-Host "     Autostart-Eintrag entfernt (das Programm ist deinstalliert)."
    }
} else {
    Write-Host "     kein Eintrag vorhanden, nichts zu tun."
}
Write-Host ""

# ---------- Kontrolle ----------

Write-Host "  Kontrolle"
Write-Host "  ---------"
Write-Host "  Einstellungsdatei da : $(Test-Path $datei)"
$lnk = Join-Path ([Environment]::GetFolderPath('Startup')) 'Tapete.lnk'
Write-Host "  Autostart-Verknuepfung: $(Test-Path $lnk)"
$k = Get-ItemProperty $schluessel -ErrorAction SilentlyContinue
$offen = @($aus | Where-Object {
    $p = $k.PSObject.Properties[$_]
    $p -and $p.Value[0] -ne 3 -and $p.Value[0] -ne 7
})
Write-Host "  Noch aktive aus der Liste: $($offen.Count)"
if ($offen.Count -gt 0) { $offen | ForEach-Object { Write-Host "    $_" } }
Write-Host ""
Write-Host "  Fertig. Die Autostart-Aenderungen greifen beim naechsten Anmelden."
Write-Host "  Der Hintergrund kommt schon beim naechsten Start von Tapete."
Write-Host ""
Read-Host "  Zum Schliessen die Eingabetaste druecken"
