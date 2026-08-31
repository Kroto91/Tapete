# Tapete

Animierter Desktop-Hintergrund für Windows. Ein Video läuft hinter den
Desktop-Symbolen, die Symbole bleiben sichtbar und anklickbar.

Ein privates Bastelprojekt, kein fertiges Produkt. Keine Gewähr.

## Herunterladen

Unter [Releases](../../releases) liegen zwei Setups:

`Tapete-Setup-X.Y.Z.exe` — nur das Programm, rund 95 MB.

`Tapete-Setup-X.Y.Z-mit-Videos.exe` — zusätzlich zwölf Beispielvideos, rund 668 MB.
Beim Installieren lassen sie sich abwählen.

Beides sind Benutzerinstallationen nach `%LOCALAPPDATA%\Programs\Tapete`. Sie fragen
nicht nach Administratorrechten.

## Bedienen

Videos gehören nach `%USERPROFILE%\Videos\Tapeten`. Der Knopf „Video hinzufügen" oder
Ziehen und Ablegen ins Fenster kopiert sie dorthin. Eine Kachel anklicken macht das
Video zum Hintergrund, „Aus" stellt den normalen Windows-Hintergrund wieder her.

Schließt man das Fenster, läuft Tapete im Infobereich neben der Uhr weiter. Doppelklick
holt es zurück, Rechtsklick hat „Beenden".

Unten stehen drei Schalter:

**Bildschirm** — auf welchem Monitor das Video läuft, oder über alle zusammen. Ohne
eigene Wahl der Hauptbildschirm.

**Pause wenn verdeckt** — hält das Video an, sobald kein Stück Desktop mehr zu sehen
ist. Geprüft wird die tatsächlich freie Fläche, nicht nur das Vordergrundfenster: Zwei
Fenster nebeneinander lösen die Pause also aus.

**Mit Windows starten** — legt eine Verknüpfung im Autostart-Ordner an. Tapete startet
dann ohne Fenster und setzt den zuletzt gewählten Hintergrund.

## Videoformate

75 Endungen, unter anderem MP4, MKV, MOV, AVI, WMV, WebM, FLV, M2TS, MPEG, VOB und OGV.
Die Liste stammt aus mpvs eigenem Installationsskript. Hardware-Dekodierung für H.264,
HEVC, MPEG-2, VC-1, VP9, WMV3 und AV1. Kein Ton, das Video läuft immer stumm.

## Was es kostet

Gemessen auf einer Radeon RX 7800 XT über die Windows-Leistungsindikatoren:

| Video | Zeichnen | Dekodieren |
|---|---|---|
| 3440x1440, 60 fps, 30 Mbit | 3,9 % | 27,6 % |
| 1920x1080, 30 fps, 4 Mbit | 0,6 % | 6,3 % |
| 1920x1080, 24 fps, 7 Mbit | 0,5 % | 5,0 % |

Die Last steckt im Dekodieren und hängt an Auflösung und Bildrate des Videos, nicht am
Programm. Ein 1080p-Video mit 30 Bildern kostet etwa ein Fünftel. Bei verdecktem Desktop
fällt beides auf null.

Arbeitsspeicher: rund 90 MB für Tapete, rund 200 MB für mpv.

## Wie es funktioniert

Windows 11 ab Build 26200 nimmt in der alten Desktop-Ebene `WorkerW` kein fremdes
Fenster mehr auf. Offen ist aber `Progman`, das Desktop-Fenster selbst. Tapete hängt das
Abspielfenster dort als Kind ein und setzt es in der Z-Reihenfolge direkt unter die
Symbol-Ebene.

Abgespielt wird über [mpv](https://mpv.io), nicht mit Bordmitteln von WPF. Ein
WPF-Fenster sitzt zwar an der richtigen Stelle im Fensterbaum, wird unter `Progman` aber
nicht gezeichnet: gemessen 0 von 10000 veränderten Bildpunkten in zwei Sekunden, mit mpv
an derselben Stelle 9123. WPF zeichnet über DirectComposition, und die setzt der
Fenstermanager innerhalb von `Progman` nicht zusammen.

Auf Windows 10 und Windows 11 bis 24H2 nimmt das Programm automatisch den älteren Weg
über `WorkerW`. Das ist nicht nachgeprüft, weil kein solcher Rechner zur Hand war.

## Wenn etwas nicht läuft

Tapete schreibt bei jedem Start ein Protokoll nach `%APPDATA%\Tapete\protokoll.txt`.
Darin steht Zeile für Zeile, wie weit der Start gekommen ist. Welche Zeile fehlt, sagt,
wo es hakt.

Eine bekannte Stolperstelle: Wird Tapete aus einem Programm heraus gestartet, das in
einem MSIX-Container läuft, landen Einstellungen und Protokoll in dessen Umleitung statt
im echten Benutzerprofil. Der Autostart findet sie später nicht. Tapete direkt aus dem
Explorer oder dem Startmenü starten.

## Selbst bauen

Braucht das .NET-10-SDK und für die Setups [Inno Setup 6](https://jrsoftware.org/isinfo.php).

    dotnet publish Tapete.csproj -c Release -o fertig
    ISCC.exe setup\Tapete.iss
    ISCC.exe /DMitVideos setup\Tapete.iss

`mpv.exe` und `d3dcompiler_43.dll` müssen im Ordner `fertig` liegen. Sie sind nicht Teil
dieses Repositorys, siehe unten.

## Lizenzen

Der Code hier steht unter der MIT-Lizenz.

`mpv.exe` stammt nicht aus diesem Projekt. Es ist der Windows-Bau von
[shinchiro](https://github.com/shinchiro/mpv-winbuild-cmake), der erste Link, auf den
[mpv.io](https://mpv.io/installation/) für Windows verweist. mpv steht unter der GPL
beziehungsweise LGPL; Quellcode und Lizenztexte liegen bei
[mpv-player/mpv](https://github.com/mpv-player/mpv). mpv.io selbst stellt keine
Windows-Dateien bereit und bezeichnet alle Binärpakete ausdrücklich als inoffizielle
Bauten Dritter.

Die Beispielvideos im großen Setup stammen aus dem Netz und sind nicht selbst gemacht.
Auf einem steht ein Wasserzeichen von moewalls.com.
