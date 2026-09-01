# Tapete

Animierter Desktop-Hintergrund für Windows. Ein Video läuft hinter den
Desktop-Symbolen, die Symbole bleiben sichtbar und anklickbar.

Ein privates Bastelprojekt, kein fertiges Produkt. Keine Gewähr.

## Herunterladen

Unter [Releases](../../releases) liegt `Tapete-Setup-X.Y.Z.exe` mit rund 95 MB. Es ist eine
Benutzerinstallation nach `%LOCALAPPDATA%\Programs\Tapete` und fragt nicht nach
Administratorrechten.

Videos sind nicht dabei. Die zwölf, mit denen entwickelt wurde, stammen aus dem Netz und
gehören nicht zu diesem Projekt; auf einem steht ein Wasserzeichen von moewalls.com. Eigene
Videos kommen nach `Videos\Tapeten`, siehe unten.

## Bedienen

Videos gehören nach `%USERPROFILE%\Videos\Tapeten`. Der Knopf „Video hinzufügen“ oder
Ziehen und Ablegen ins Fenster kopiert sie dorthin. Eine Kachel anklicken macht das
Video zum Hintergrund, „Aus“ stellt den normalen Windows-Hintergrund wieder her.

Ein Rechtsklick auf eine Kachel bietet „In den Papierkorb“ an. Nach einer Rückfrage wandert
das Video in den Papierkorb, nicht ins Nichts. Ein Fehlklick lässt sich von dort zurückholen.

Schließt man das Fenster, läuft Tapete im Infobereich neben der Uhr weiter. Doppelklick
holt es zurück, Rechtsklick hat „Beenden“.

Unten stehen vier Schalter.

„Bildschirm“ bestimmt, auf welchem Monitor das Video läuft, oder ob es über alle
zusammen geht. Ohne eigene Wahl ist es der Hauptbildschirm.

„Pause wenn verdeckt“ hält das Video an, sobald kein Stück Desktop mehr zu sehen ist.
Geprüft wird die tatsächlich freie Fläche, nicht nur das Vordergrundfenster: Zwei
Fenster nebeneinander lösen die Pause also aus.

„Halbe Bildrate“ rechnet das Video einmalig auf die halbe Bildrate herunter. Ab Werk aus,
weil man das im Gegensatz zur Auflösung sieht. Wie viel es spart, hängt stark vom Video ab;
Zahlen stehen weiter unten.

„Mit Windows starten“ legt eine Verknüpfung im Autostart-Ordner an. Tapete startet
dann ohne Fenster und setzt den zuletzt gewählten Hintergrund.

## Spielmodus

Der Knopf „Spielmodus“ oben im Fenster beendet den Abspieler ganz. Nicht angehalten,
sondern beendet: der Prozess verschwindet samt seiner rund 200 MB Arbeitsspeicher und seiner
Dekodiersitzung auf der Grafikkarte. Ein zweiter Klick holt dasselbe Video zurück.

Schneller geht es über das Symbol neben der Uhr. Rechtsklick, „Spielmodus“ anklicken. Dafür
muss das Fenster nicht offen sein, und wer gleich spielen will, hat es nicht offen.

Tapete merkt außerdem selbst, wann etwas den Bildschirm für sich beansprucht, schaltet den
Spielmodus dann ein und danach wieder aus. Dafür fragt es alle drei Sekunden eine einzige
Auskunft bei Windows ab, dieselbe, nach der auch Benachrichtigungen zurückgehalten werden.
Es wird keine Liste bekannter Spiele geführt und keine Prozessliste durchsucht. Was von Hand
geschaltet wurde, lässt die Automatik stehen.

Ein Spiel im randlosen Fenster meldet Windows je nach Spiel als Vollbild oder gar nicht;
deshalb bleibt der Knopf daneben stehen. Ein Video im Vollbild zählt auch als Vollbild, der
Hintergrund ruht dann ebenfalls.

Strg+Alt+G schaltet um, auch mitten aus einem Spiel heraus. Ist die Tastenfolge schon
vergeben, steht das im Protokoll und alles andere läuft weiter.

Der Schalter „Pause wenn verdeckt“ macht etwas Ähnliches, aber nicht dasselbe. Er hält das
Video an, solange ein Fenster den Desktop verdeckt, und mpv läuft dabei weiter. Bei einem
Spiel im randlosen Fenster, oder einem auf dem zweiten Monitor, bleibt ein Stück Desktop
sichtbar; dann greift die Pause gar nicht.

Die Einstellung bleibt über einen Neustart hinweg stehen. Steht der Spielmodus beim Start auf
an, bleibt der Hintergrund aus, bis er wieder abgeschaltet wird.

## Aktualisierungen

Tapete fragt beim Start einmal bei GitHub nach, ob eine neuere Fassung vorliegt. Wenn ja,
lädt es das Setup und spielt es ohne Rückfrage ein: Der Installer läuft ohne Assistent,
Tapete beendet sich dafür kurz und wird vom Setup wieder gestartet. Zu sehen ist davon
nichts außer einem kurzen Aussetzer im Bild.

Das geht nur ohne Nachfrage, weil die Installation unter `%LOCALAPPDATA%\Programs\Tapete`
liegt und deshalb keine Administratorrechte braucht.

Ist das Fenster offen, erscheint zusätzlich der Knopf „Neu: x.y.z“. Wer ihn drückt,
bekommt den Installer mit Assistent zu sehen. Klappt die Abfrage nicht, etwa ohne Netz,
bleibt der Knopf weg und es kommt keine Meldung.

Geht eine Aktualisierung nicht durch, wird dieselbe Fassung nicht von allein ein zweites
Mal versucht; sonst lüde Tapete bei jedem Start 95 MB für nichts. Der Knopf bleibt.

Heruntergeladen wird nur von `github.com` und nur über HTTPS. Die Adresse stammt aus einer
Antwort aus dem Netz und wird vor dem Laden auf ihre Herkunft geprüft.

Bis Fassung 1.2.1 hing das allein am Knopf. Im Autostart wird das Fenster nie gezeigt, den
Knopf sah dort also niemand. Und das Zeitlimit für den Download stand auf den zwanzig
Sekunden der kurzen Abfrage, weshalb die 95 MB jedes Mal abbrachen. Beides ist behoben;
wer auf 1.2.1 oder älter sitzt, muss einmal von Hand aktualisieren.

## Videoformate

75 Endungen, unter anderem MP4, MKV, MOV, AVI, WMV, WebM, FLV, M2TS, MPEG, VOB und OGV.
Die Liste stammt aus mpvs eigenem Installationsskript. Hardware-Dekodierung für H.264,
HEVC, MPEG-2, VC-1, VP9, WMV3 und AV1. Kein Ton, das Video läuft immer stumm.

## Was es kostet

Gemessen auf einer Radeon RX 7800 XT über die Windows-Leistungsindikatoren:

| Video | Zeichnen | Dekodieren |
|---|---|---|
| 3440×1440, 60 fps, 30 Mbit | 3,9 % | 27,6 % |
| 1920×1080, 30 fps, 4 Mbit | 0,6 % | 6,3 % |
| 1920×1080, 24 fps, 7 Mbit | 0,5 % | 5,0 % |

Die Last steckt im Dekodieren und hängt an Auflösung und Bildrate des Videos, nicht am
Programm. Ein 1080p-Video mit 30 Bildern kostet etwa ein Fünftel. Bei verdecktem Desktop
fällt beides auf null.

Arbeitsspeicher: rund 90 MB für Tapete, rund 200 MB für mpv.

## Das Bild füllt den Schirm

Das Video wird so vergrößert, dass es den Bildschirm ganz ausfüllt, und der Überstand wird
abgeschnitten. Verzerrt wird nichts, das Seitenverhältnis bleibt.

Bis Fassung 1.2.0 wurde stattdessen eingepasst. Ein Video im Verhältnis 21:9 bekam auf einem
16:9-Bildschirm dadurch schwarze Balken oben und unten, ein 16:9-Video auf einem Ultrawide
welche links und rechts. Gemeldet am 01.09.2026 von einem Nutzer mit zwei Bildschirmen.

## Zu große Videos werden angepasst

Ist ein Video größer als der Bildschirm, auf dem es laufen soll, rechnet Tapete es einmalig
herunter und spielt danach die kleinere Fassung. Das läuft von selbst im Hintergrund und
dauert mit Hardware-Kodierung wenige Sekunden; solange spielt das Original weiter, danach
schaltet Tapete kurz um.

Gerechnet wird auf das kleinste Maß, das den Bildschirm noch abdeckt, nicht auf das größte,
das hineinpasst. Ein 3440×1440-Video für einen 1920×1080-Schirm wird also 2580×1080, nicht
1920×804. Sonst müsste mpv die fehlenden Zeilen beim Füllen wieder hochziehen.

Gemessen am 31. August 2026 auf einer Radeon RX 7800 XT, jeweils bildschirmfüllend auf
3440×1440, Mittel aus sechs Abtastungen. Die 1920×804 sind das, was die damalige Regel
ergab; heute wäre es an dieser Stelle mehr:

| Fassung | Zeichnen | Dekodieren |
|---|---|---|
| 3440×1440, 60 fps (Original) | 2,4 % | 28,0 % |
| 1920×804, 60 fps (gerechnet) | 2,2 % | 9,4 % |
| 1920×804, 30 fps (gerechnet) | 1,1 % | 4,8 % |

Von allein lässt Tapete die Bildrate unangetastet, denn man sieht sie, anders als die
Auflösung: mehr Bildpunkte, als der Bildschirm hat, kann er ohnehin nicht darstellen,
weniger Bilder je Sekunde dagegen schon. Wer sie trotzdem senken will, hat dafür den Schalter
„Halbe Bildrate“.

Was der bringt, schwankt stark. Bei 1920×804 halbierte sich die Dekodierlast, 9,4 gegen
4,8 Prozent. Bei 3440×1440 waren am 01.09.2026 nur 20,0 gegen 14,9 Prozent zu messen. Der
zweite Vergleich hinkt allerdings: dort steht ein Original mit 30 Mbit gegen eine gerechnete
Fassung mit 7 Mbit, es ändert sich also nicht nur die Bildrate. Und die Messwerte streuen
zwischen zwei Durchgängen um einige Prozentpunkte.

Kodiert wird mit derselben `mpv.exe`, die auch abspielt. Sie bringt libavcodec mit, ein
zweites Programm ist dafür nicht nötig. Versucht werden nacheinander die Hardware-Encoder
von AMD, Nvidia und Intel, dann MediaFoundation, zuletzt libx264 in Software; der erste,
der eine brauchbare Datei liefert, gewinnt.

Die gerechneten Fassungen liegen unter `%APPDATA%\Tapete\klein`. Der Ordner darf gelöscht
werden, was gebraucht wird entsteht neu. Deckt ein Video den Bildschirm schon ab, bleibt es
unangetastet: auf einem 3440×1440-Schirm mit einem 3440×1440-Video passiert nichts.

Im Dateinamen steckt die Rechenregel mit drin. Nach einer Aktualisierung, die diese Regel
ändert, werden vorhandene Dateien deshalb nicht weiterverwendet, sondern neu gerechnet.

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
