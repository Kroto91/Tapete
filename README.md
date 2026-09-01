# Tapete

Ein Video als Desktop-Hintergrund. Die Symbole bleiben sichtbar und anklickbar.

## Warum es das gibt

Windows 11 hat mit Build 26200 die Ebene dichtgemacht, in die Programme wie Lively ihr Video
hängen. Von einem Tag auf den anderen blieb der Desktop schwarz oder die Symbole
verschwanden. Also habe ich mir einen eigenen Weg gesucht: Tapete hängt das Video eine Ebene
tiefer ein, direkt unter die Symbole.

Ein privates Bastelprojekt. Es läuft auf meinem Rechner und auf dem eines Bekannten, mehr
Erfahrung gibt es damit nicht. Keine Gewähr.

## Loslegen

Setup unter [Releases](../../releases) holen, rund 95 MB, starten. Es installiert in dein
Benutzerprofil und fragt nicht nach Administratorrechten.

Videos sind keine dabei. Zieh sie einfach ins Fenster, dann landen sie im richtigen Ordner
(`Videos\Tapeten`). Kachel anklicken, fertig.

Fenster zu heißt nicht Programm zu: Tapete läuft neben der Uhr weiter, Doppelklick holt es
zurück. Und wenn ein Video wieder weg soll, Rechtsklick auf die Kachel — es wandert in den
Papierkorb, nicht ins Nichts.

## Karussell

Wenn ein Bild auf Dauer langweilig wird: Häkchen oben links auf jede Kachel, die mitlaufen
soll, dann im Zahnrad das Karussell einschalten. Ab da wechselt der Hintergrund von selbst.

Gemischt oder der Reihe nach, die Standzeit stellst du ein. Gemischt heißt dabei wirklich
gemischt und nicht gewürfelt — jedes Video kommt einmal dran, bevor sich etwas wiederholt.

Zwei Sachen, die dir sonst auffallen würden. Gewechselt wird nur, wenn der Desktop auch zu
sehen ist; sonst zöge dir ein Arbeitstag die halbe Sammlung ungesehen vorbei. Und wenn dir
das laufende Video gerade nicht passt, springt **Strg+Alt+W** sofort weiter.

## Spielmodus

**Strg+Alt+G**, und der Abspieler ist weg. Nicht angehalten, sondern beendet, samt seinen
200 MB und der Dekodiersitzung auf der Grafikkarte. Noch einmal drücken holt alles zurück.

Meistens brauchst du das gar nicht: Tapete merkt selbst, wenn etwas im Vollbild läuft, und
schaltet mit. Es fragt dafür dieselbe Auskunft bei Windows ab, nach der auch
Benachrichtigungen stumm bleiben. Keine Liste bekannter Spiele, kein Durchsuchen von
Prozessen.

Verlass dich aber nicht blind darauf. Ein Spiel im randlosen Fenster meldet Windows je nach
Spiel als Vollbild oder überhaupt nicht. Dafür ist der Knopf daneben da.

## Was es kostet

Auf meiner Radeon RX 7800 XT, gemessen über die Windows-Leistungsindikatoren:

| Video | Zeichnen | Dekodieren |
|---|---|---|
| 3440×1440, 60 fps, 30 Mbit | 3,9 % | 27,6 % |
| 1920×1080, 30 fps, 4 Mbit | 0,6 % | 6,3 % |
| 1920×1080, 24 fps, 7 Mbit | 0,5 % | 5,0 % |

Die Last steckt im Dekodieren, und die hängt am Video, nicht am Programm. Ein 1080p-Clip mit
30 Bildern kostet ungefähr ein Fünftel von dem, was 4K bei 60 kostet. Sobald der Desktop
verdeckt ist, fällt beides auf null.

Dazu rund 90 MB Arbeitsspeicher für Tapete und 200 MB für den Abspieler.

## Zu große Videos werden kleingerechnet

Ein 4K-Video auf einem 1080p-Schirm kostet den vollen 4K-Preis, obwohl du nichts davon
siehst. Beim Abspielen zu verkleinern hilft nichts, dekodiert wird trotzdem alles.

Tapete rechnet solche Videos deshalb einmalig herunter, im Hintergrund, während das Original
weiterläuft. Danach schaltet es um. Aus 28 Prozent Dekodierlast werden so 9.

Reicht dir das nicht, gibt es im Zahnrad noch „Halbe Bildrate“. Das spart weiter, ist aber
im Gegensatz zur Auflösung tatsächlich zu sehen, deshalb ist es ab Werk aus. Die gerechneten
Fassungen liegen unter `%APPDATA%\Tapete\klein`; der Ordner darf weg, was gebraucht wird
entsteht neu.

## Es hält sich selbst aktuell

Tapete sieht beim Start bei GitHub nach und spielt eine neue Fassung ohne Rückfrage ein. Der
Installer läuft ohne Assistent, das Programm startet sich neu, und du merkst nur einen kurzen
Aussetzer im Bild. Geladen wird ausschließlich von `github.com` über HTTPS.

Wer lieber selbst nachsieht: In der Titelzeile steht die laufende Fassung, daneben ein Knopf
dafür.

## Videoformate

75 Endungen, darunter MP4, MKV, MOV, AVI, WMV, WebM, FLV, M2TS, MPEG, VOB und OGV. Die Liste
stammt aus mpvs eigenem Installationsskript. Hardware-Dekodierung für H.264, HEVC, MPEG-2,
VC-1, VP9, WMV3 und AV1. Ton gibt es nie, das Video läuft immer stumm.

## Wie es funktioniert

Der interessante Teil, falls du so etwas selbst bauen willst.

Windows 11 nimmt ab Build 26200 in `WorkerW` kein fremdes Fenster mehr auf. Offen ist aber
`Progman`, das Desktop-Fenster selbst. Dort hängt Tapete sein Abspielfenster als Kind ein und
schiebt es in der Z-Reihenfolge direkt unter die Symbolebene.

Abgespielt wird über [mpv](https://mpv.io), nicht mit WPF-Bordmitteln. Das hat einen
handfesten Grund: Ein WPF-Fenster sitzt zwar an der richtigen Stelle im Fensterbaum, wird
unter `Progman` aber schlicht nicht gezeichnet. Gemessen: 0 von 10000 Bildpunkten
veränderten sich in zwei Sekunden. Mit mpv an derselben Stelle waren es 9123. WPF zeichnet
über DirectComposition, und die setzt der Fenstermanager innerhalb von `Progman` nicht
zusammen.

Auf Windows 10 und Windows 11 bis 24H2 nimmt das Programm automatisch den alten Weg über
`WorkerW`. Nachgeprüft ist das nicht, mir fehlt ein solcher Rechner.

## Wenn etwas nicht läuft

Bei jedem Start schreibt Tapete mit, wie weit es gekommen ist:

    %APPDATA%\Tapete\protokoll.txt

Welche Zeile fehlt, sagt dir, wo es hakt.

Eine Stolperstelle, die mich Stunden gekostet hat: Startest du Tapete aus einem Programm
heraus, das in einem MSIX-Container läuft, landen Einstellungen und Protokoll in dessen
Umleitung statt in deinem echten Profil. Der Autostart findet sie später nicht mehr. Also
direkt aus dem Explorer oder dem Startmenü starten.

## Selbst bauen

Braucht das .NET-10-SDK, für die Setups zusätzlich [Inno Setup 6](https://jrsoftware.org/isinfo.php).

    dotnet publish Tapete.csproj -c Release -o fertig
    ISCC.exe setup\Tapete.iss

`mpv.exe` und `d3dcompiler_43.dll` gehören in den Ordner `fertig`. Sie liegen nicht mit im
Repository, warum steht unten.

## Lizenzen

Der Code steht unter der MIT-Lizenz.

`mpv.exe` gehört nicht dazu. Es ist der Windows-Bau von
[shinchiro](https://github.com/shinchiro/mpv-winbuild-cmake), der erste Link, auf den
[mpv.io](https://mpv.io/installation/) für Windows verweist. mpv steht unter GPL
beziehungsweise LGPL, Quellcode und Lizenztexte liegen bei
[mpv-player/mpv](https://github.com/mpv-player/mpv). mpv.io selbst stellt keine
Windows-Dateien bereit und bezeichnet alle Binärpakete ausdrücklich als inoffizielle Bauten
Dritter.

Die Videos, mit denen ich entwickelt habe, stammen aus dem Netz und sind nicht meine. Auf
einem steht sogar ein Wasserzeichen. Deshalb liegt keines davon hier und keines im Setup.
