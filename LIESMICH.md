# Tapete

Animierter Desktop-Hintergrund für Windows. Ein Fenster, ein Klick, fertig.

Stand 31.08.2026: läuft, startet mit Windows, 12 Videos im Ordner.

## Starten

`fertig\Tapete.exe` doppelklicken. Das Programm braucht keine Installation und
kein zusätzliches .NET.

Im Ordner `fertig` liegen drei Dateien, alle drei werden gebraucht:

| Datei | Wofür |
|---|---|
| `Tapete.exe` | das Programm |
| `mpv.exe` | der Abspieler, siehe unten |
| `d3dcompiler_43.dll` | braucht mpv für manche Direct3D-Pfade |

Tapete spielt Videos nicht selbst ab, sondern über mpv. Gesucht wird in dieser
Reihenfolge: neben `Tapete.exe`, dann `%LOCALAPPDATA%\Tapete\mpv.exe`, zuletzt eine
vorhandene Lively-Installation. Der dritte Weg greift seit dem 31.08.2026 nicht mehr,
Lively ist an diesem Tag deinstalliert worden.

Die beiliegende `mpv.exe` ist Version v0.41.0-1012-ge8673660a vom 31.08.2026, mit
eingebautem FFmpeg N-126337-g818cecc6e. Geladen am 31.08.2026 von shinchiro, dem
ersten Windows-Bau, auf den mpv.io verweist. **mpv.io selbst stellt keine
Windows-Dateien bereit**, dort steht ausdrücklich, alle Binärpakete seien
inoffizielle Bauten Dritter. Eine vom mpv-Projekt signierte Prüfsumme gibt es
deshalb nicht; die Datei kam über HTTPS von GitHub und hatte die Größe, die die
Release-Seite angibt (32,2 MB gepackt).

## Bedienen

- **Kachel anklicken** → das Video wird zum Hintergrund
- **Aus** → zurück zum normalen Windows-Hintergrund
- **Video hinzufügen** oder Datei ins Fenster ziehen → landet im Video-Ordner
- **Ordner** → öffnet `Videos\Tapeten`, dort liegen alle Kacheln
- **Fenster schließen** → Programm läuft im Infobereich weiter (Symbol neben der Uhr,
  Doppelklick holt das Fenster zurück, Rechtsklick hat *Beenden*)

Zwei Schalter unten:

- **Pause wenn verdeckt** — hält das Video an, sobald kein Stück Desktop mehr zu
  sehen ist. Nicht nur bei Vollbild: Auch ein maximiertes Fenster oder zwei
  nebeneinander liegende Fenster zählen, wenn sie zusammen alles zudecken.
- **Mit Windows starten** — trägt das Programm in den Autostart ein, es startet dann
  ohne Fenster und setzt den zuletzt gewählten Hintergrund.

## Videoformate

75 Endungen, unter anderem MP4, MKV, MOV, AVI, WMV, WebM, FLV, M2TS, MPEG, VOB,
OGV, RMVB, 3GP, MXF und die rohen Ströme H.264 und H.265. Die Liste ist nicht
geschätzt, sondern aus mpvs eigenem Installationsskript übernommen: alle Einträge,
die dort als „video" registriert werden.

Hardware-Dekodierung steht für H.264, HEVC, MPEG-2, VC-1, VP9, WMV3 und AV1 bereit,
über D3D11VA, DXVA2 und Vulkan. Auf der Radeon RX 7800 XT greift `--hwdec=auto-safe`
darauf zu, die CPU bleibt weitgehend unbeteiligt.

Kein Ton, das Video läuft immer stumm.

## Desktop-Symbole bleiben sichtbar

Seit dem 31.08.2026 liegt das Video **hinter** den Symbolen. Sie bleiben sichtbar
und anklickbar, es wird nichts mehr ausgeblendet.

Der Weg dorthin: Windows 11 ab Build 26200 nimmt in `WorkerW` kein fremdes Fenster
mehr auf, dieser klassische Weg ist zu. Offen ist aber `Progman`, das Desktop-Fenster
selbst. Tapete hängt sich dort als Kind ein und setzt sich in der Z-Reihenfolge
direkt unter die Symbol-Ebene.

Abgeschaut ist das bei Lively 2.2.1.0, das auf demselben Rechner läuft. Am
31.08.2026 nachgemessen, so sieht die Reihenfolge unter `Progman` aus, wenn beide
laufen:

    Z0  SHELLDLL_DefView   Symbole, ganz oben
    Z1  Tapete             unser Video
    Z2  mpv                Livelys Video
    Z3  WorkerW            statisches Hintergrundbild

## Warum mpv und nicht WPF

Der erste Versuch am 31.08.2026 lief mit einem WPF-Fenster und `MediaElement`. Das
Fenster saß im Fensterbaum an der richtigen Stelle, zwischen Symbolen und
Hintergrundbild, und trotzdem blieb der Bildschirm unverändert: 0 von 10000
Bildpunkten in zwei Sekunden. Dasselbe mit mpv an derselben Stelle: 9123 von 10000.

WPF zeichnet über DirectComposition, und die setzt der Fenstermanager innerhalb von
`Progman` nicht zusammen. Ein einfaches Win32-Fenster mit eigener Zeichenfläche,
wie mpv es mitbringt, wird dagegen gezeichnet. Deshalb steuert Tapete jetzt einen
mpv-Prozess, statt selbst abzuspielen.

Angehalten und fortgesetzt wird mpv über seine Named Pipe. Die legt mpv beidseitig
an; eine Verbindung nur zum Schreiben läuft in den Zeitablauf.

Rechtsklick auf den Desktop funktioniert weiterhin, das Video lässt Mausklicks durch.

Auf Windows 10 und Windows 11 bis 24H2 nimmt das Programm automatisch den alten Weg
über die WorkerW-Ebene. Dort bleiben die Symbole ebenfalls sichtbar.

Die Reparaturfunktion beim Start ist geblieben: Wer noch eine ältere Fassung von
Tapete laufen hatte und bei der die Symbole ausgeblendet zurückblieben, bekommt sie
beim nächsten Start zurück.

## Was das Programm verbraucht

Gemessen am 31.08.2026 auf Build 26200, 3440x1440, mit einem 31-MB-Video:

| | Tapete | Lively 2.2.1.0 |
|---|---|---|
| Prozesse | 2 (Tapete + mpv) | 4 |
| Arbeitsspeicher | 286 MB (90 + 196) | 595 MB |
| CPU, Desktop sichtbar | 2,5 bis 3,1 % | 6,3 % |
| CPU, Desktop verdeckt | 0 % | nicht gemessen |

Das Fortsetzen dauert bis zu zwei Sekunden. Die Prüfung läuft im Zwei-Sekunden-Takt,
und erst der nächste Takt nach dem Freiwerden schickt mpv das Weiter. Wer den Desktop
freiräumt und sofort hinschaut, sieht also kurz ein stehendes Bild. Das ist kein
Fehler, sondern der Preis dafür, nicht ständig zu messen.

Die 0 Prozent sind der Punkt. Sieht niemand den Hintergrund, wird auch nichts
dekodiert. Geprüft wird das nicht am Vordergrundfenster allein, sondern über die
tatsächlich freie Fläche: Windows zieht von der Bildschirmfläche jedes sichtbare
Fenster ab, bleibt nichts übrig, pausiert das Video. Zwei Fenster nebeneinander
lösen die Pause also aus, ein einzelnes halbes Fenster nicht.

Zwei Fallen stecken darin, beide beim Messen aufgefallen:

Ausgeblendete Fenster müssen übersprungen werden. Der Explorer hält ein
unsichtbares `ApplicationFrameWindow` über die volle Bildschirmbreite offen; ohne
diese Prüfung wäre das Video dauerhaft pausiert.

Die Taskleiste muss dagegen als deckend zählen. Nimmt man sie aus, bleibt unten
immer ein Streifen stehen und die Pause löst nie aus.

## Wo liegt was

| Was | Wo |
|---|---|
| Videos | `%USERPROFILE%\Videos\Tapeten` |
| Einstellungen | `%APPDATA%\Tapete\einstellungen.json` |
| Autostart | Verknüpfung `Tapete.lnk` im Autostart-Ordner (`shell:startup`) |

Zum Deinstallieren: *Aus* drücken, *Beenden*, die drei Sachen oben löschen, Ordner weg.

## Neu bauen

Braucht das .NET-10-SDK.

```
dotnet publish Tapete.csproj -c Release -o fertig
```

## Aufbau des Codes

| Datei | Zweck |
|---|---|
| `App.xaml.cs` | Start, Infobereich-Symbol, schaltet den Hintergrund an und aus |
| `MainWindow.xaml(.cs)` | Das Fenster mit den Kacheln |
| `Hintergrund.cs` | Das Abspielfenster, das im Desktop hängt |
| `Native.cs` | Windows-Aufrufe, Suche nach der richtigen Desktop-Ebene |
| `Thumbs.cs` | Vorschaubilder über Windows selbst |
| `Settings.cs` | Einstellungen und Autostart |
| `VideoItem.cs` | Eine Kachel: Pfad, Name mit Endung, Vorschaubild |

## Autostart

Der Schalter „Mit Windows starten" legt eine Verknüpfung im Autostart-Ordner an:

    C:\Users\<Name>\AppData\Roaming\Microsoft\Windows\Start Menu\Programs\Startup\Tapete.lnk

Vorher lief das über einen Run-Eintrag in der Registry. Der hat am 31.08.2026 versagt:
Der Eintrag stand technisch einwandfrei da, richtiger Werttyp, vorhandener Pfad,
dasselbe Format wie bei einem Programm, das an demselben Anmeldevorgang gestartet ist.
Ausgeführt wurde er trotzdem nicht. Kein Defender-Fund, keine SmartScreen-Sperre, keine
Internet-Markierung an der Datei, kein Absturz im Ereignisprotokoll, keine
Startverzögerung gesetzt. **Die Ursache ist offen.** Der Autostart-Ordner hat den
Vorteil, dass man ihn im Explorer öffnen und selbst nachsehen kann.

## Was am 31.08.2026 an Fehlern behoben wurde

Sechs Befunde aus einem Durchgang mit `code-review`, alle mit konkretem Fehlerfall:

Ein hart beendetes Tapete ließ seine mpv als Waise unter `Progman` weiterlaufen, beim
nächsten Start kam eine zweite dazu. Jetzt räumt `Aufbauen()` alte mpv-Prozesse der
eigenen Kopie ab, bevor es eine neue startet.

Startfehler verpufften. Das Fehler-Ereignis ließ sich erst nach dem Konstruktor
abonnieren, gemeldet wurde aber im Konstruktor. Die Meldung kommt jetzt als Parameter
herein. Zwei Folgefehler steckten dahinter: `StandAktualisieren()` überschrieb die
Fehlerzeile sofort wieder, und `AktuellesVideo` meldete ein Video als laufend, obwohl
der Aufbau gescheitert war.

Der Schalter „Mit Windows starten" las den Run-Eintrag, während der Autostart längst
über die Verknüpfung lief. Er stand auf aus, obwohl das Programm mitstartete.

`LetztesVideo` wurde auch dann gespeichert, wenn nichts lief. Jetzt nur noch bei Erfolg.

Der Auswahldialog kannte sechs Endungen, Ziehen und Ablegen 75. Beide benutzen jetzt
dieselbe Liste.

Das Warten auf mpvs Fenster blockiert die Oberfläche. Ich hatte den Deckel erst von
acht auf drei Sekunden gesenkt und damit den Autostart zerschossen: Beim Anmelden am
31.08.2026 wird `mpv.exe` mit ihren 115 MB kalt von der SSD geladen, während ein Dutzend
anderer Autostart-Programme dieselbe Platte belegt. Drei Sekunden reichten nicht, der
Zweig „kein Fenster" schlug zu, mpv wurde gleich wieder beendet und der Hintergrund
blieb leer. Im Leerlauf mit warmem Dateicache steht dasselbe Fenster nach 241 ms — daher
die falsche Einschätzung. Der Deckel liegt jetzt bei 30 Sekunden.

Dass dabei blockiert wird, fällt nur im Fehlerfall auf: Im Normalfall sind es
Millisekunden, und beim Anmelden gibt es kein Fenster, das einfrieren könnte.

## Wenn beim Anmelden kein Hintergrund kommt

Tapete schreibt bei jedem Start ein Protokoll:

    %APPDATA%\Tapete\protokoll.txt

Darin steht Zeile für Zeile, wie weit der Start gekommen ist. So sieht ein
geglückter Lauf aus:

    OnStartup, Argumente: [--versteckt]
    Einstellungen geladen. LetztesVideo=...solo-leveling-sung-jin-woo.mp4
    Startvideo: gesetzt=True vorhanden=True
    Aufbauen startet fuer solo-leveling-sung-jin-woo.mp4
    MpvSuchen: ProcessPath=...\Tapete.exe -> ...\mpv.exe
    Warten auf mpv-Fenster: 318 ms, gefunden: True
    Aufbau geglueckt, mpv PID 4176, Fenster 1705238

Welche Zeile fehlt, sagt, wo es hakt. Fehlt schon die erste, lief das Programm
selbst nicht an.

## Der ungelöste Fehler beim Anmelden

Am 31.08.2026 kam der Hintergrund fünfmal in Folge nach dem Anmelden nicht hoch,
obwohl derselbe Start von Hand jedes Mal funktionierte. Das Protokoll hat es beim
fünften Anlauf eingegrenzt:

    OnStartup, Argumente: [--versteckt]
    Einstellungen geladen. LetztesVideo=null
    Startvideo: gesetzt=False vorhanden=False

`Settings.Laden()` liefert beim Anmelden eine leere Einstellung. Die Datei lag dabei
unverändert auf der Platte, gültiges JSON, kein BOM, richtiger Pfad — und die
Protokollzeile davor wurde in denselben Ordner geschrieben, der Ordner war also
erreichbar.

Ausgeschlossen wurden: umgeleiteter Videos-Ordner, ungültiges JSON, OneDrive (lief
eine Minute vorher), falsche Verknüpfung, veraltete Programmdatei, zu knapper
Zeitdeckel und ein noch nicht fertiger Desktop. Progman stand 82 Sekunden vor dem
Programmstart bereit.

**Warum das Lesen scheitert, ist offen.** Die Netzrecherche hat für genau diesen
Fall nichts hergegeben.

Solange das so ist, greift ein zweiter Anlauf: Kommt der Hintergrund beim Start
nicht hoch, versucht Tapete es alle fünf Sekunden erneut, höchstens sechsmal. Jeder
Versuch steht im Protokoll. Nachgestellt geprüft, indem die Einstellungsdatei
weggenommen und nach acht Sekunden zurückgelegt wurde: Versuch 1 fand nichts,
Versuch 2 fand sie, der Hintergrund lief.

Das behebt die Ursache nicht, es umgeht sie. Sobald das Protokoll beim nächsten
Anmelden den Grund nennt, gehört er an seiner Stelle behoben.

## Bildschirmwahl

Unten im Fenster steht ein Auswahlfeld. Es listet „Alle Bildschirme" und danach jeden
angeschlossenen Monitor mit seiner Auflösung.

Vorher gab es das nicht: Das Abspielfenster wurde immer auf die Gesamtfläche aller
Bildschirme gesetzt. Auf zwei Monitoren lief das Video dadurch über beide und wurde in
der Mitte zerschnitten. Gemeldet am 31.08.2026 von einem Nutzer mit zwei Bildschirmen.

Ohne gemerkte Wahl nimmt Tapete den Hauptbildschirm, nicht mehr alle. Ist der gemerkte
Monitor abgesteckt, fällt es ebenfalls auf den Hauptbildschirm zurück.

## Was die Grafikkarte kostet

Am 31.08.2026 gemessen, RX 7800 XT, über die Windows-Leistungsindikatoren:

| Video | 3D | Dekoder |
|---|---|---|
| 3440x1440, 60 fps, 30 Mbit | 3,9 % | 27,6 % |
| 1920x1080, 30 fps, 4 Mbit | 0,6 % | 6,3 % |
| 1920x1080, 24 fps, 7 Mbit | 0,5 % | 5,0 % |

Die Last steckt im Dekodieren, nicht im Zeichnen, und sie hängt an Auflösung und
Bildrate des Videos. Ein 1080p-Video mit 30 Bildern kostet etwa ein Fünftel.

Am Programm ließ sich der 3D-Anteil drücken: `--profile=fast` von mpv brachte 3,85 auf
1,31 Prozent, und ein fest gesetztes `--hwdec=d3d11va` statt `auto-safe` halbierte die
CPU-Last von 6,6 auf 3,1 Prozent. Der Dekodierer blieb bei beidem unverändert.

**Wer weniger Grafiklast will, braucht ein sparsameres Video.** Ein Monitor statt aller
spart zusätzlich beim Zeichnen, weil weniger Fläche skaliert wird.

## Setup bauen

Das Setup-Skript liegt in `setup\Tapete.iss`, gebaut wird es mit Inno Setup 6:

    ISCC.exe setup\Tapete.iss

Heraus kommt `E:\Claude\Tapete-Setup-1.0.0.exe`, rund 95 MB. Aus 267 MB Nutzlast,
LZMA2 packt die .NET-Datei deutlich besser als das ZIP mit seiner schnellen Stufe.

Mit den zwölf Beispielvideos:

    ISCC.exe /DMitVideos setup\Tapete.iss

Das ergibt `Tapete-Setup-1.0.0-mit-Videos.exe`, 668 MB, und braucht 42 statt 10
Sekunden. Beim Installieren stehen dann zwei Umfänge zur Wahl, „Programm und
Beispielvideos" oder „Nur das Programm".

Die Videos landen in `Videos\Tapeten`. Inno Setup kennt keine Konstante für diesen
Ordner — `{uservideos}` gibt es nicht, ausprobiert und mit „Unknown constant"
abgelehnt. Der Pfad kommt deshalb aus `Shell Folders` in der Registry, damit greift
auch eine Umleitung, etwa durch OneDrive.

Gleichnamige Dateien werden **nicht** überschrieben (`onlyifdoesntexist`), und beim
Deinstallieren bleiben die Videos liegen (`uninsneveruninstall`). Es sind Dateien im
Videos-Ordner des Nutzers, keine Programmbestandteile.


Es ist bewusst eine **Benutzerinstallation**: Ziel ist `%LOCALAPPDATA%\Programs\Tapete`,
und es fragt nicht nach Administratorrechten. Für einen animierten Hintergrund gibt es
keinen Grund, ins Systemverzeichnis zu schreiben.

Zwei Häkchen zur Auswahl, beide standardmäßig aus: Verknüpfung auf dem Desktop und
Start mit Windows. Das zweite legt dieselbe `Tapete.lnk` im Autostart-Ordner an, die
auch der Schalter im Programm setzt — beide Wege vertragen sich.

Beim Deinstallieren gehen Programmordner, Startmenü, Eintrag in der Programmliste und
die Autostart-Verknüpfung. **Einstellungen und Protokoll unter `%APPDATA%\Tapete`
bleiben liegen**, damit eine Neuinstallation die Videowahl wiederfindet.

Am 31.08.2026 durchgespielt: still in einen Testordner installiert, alle vier Dateien
plus Deinstallierer da, Startmenü und Programmliste richtig, die vorhandene
Autostart-Verknüpfung unangetastet. Danach still deinstalliert: alles weg bis auf die
Einstellungen.

## Versionsverwaltung

Seit dem 31.08.2026 liegt hier ein Git-Repository. Vorher gab es keins, und ein
Fehlgriff hätte die Arbeit gelöscht.

Nicht mitversioniert werden `bin`, `obj` und `fertig`. Der Ordner `fertig` enthält
neben `Tapete.exe` auch `mpv.exe` mit 115 MB; beides lässt sich wiederherstellen,
gehört aber nicht in die Geschichte. Woher mpv kommt, steht oben unter „Starten".

`core.autocrlf` ist auf `false` gesetzt. Ohne das schreibt Git unter Windows die
Zeilenenden auf CRLF um, und jede kleine Änderung sieht im Vergleich aus, als wäre
die ganze Datei neu.

## Was am 31.08.2026 geändert wurde

Das Video hängt nicht mehr in `WorkerW`, sondern als Kind unter `Progman`, direkt
unter der Symbol-Ebene. Damit bleiben die Desktop-Symbole sichtbar und klickbar; das
Ausblenden ist ersatzlos entfallen.

Abgespielt wird über mpv statt über WPF. Ein WPF-Fenster saß an der richtigen Stelle
und wurde trotzdem nicht gezeichnet.

Der Schalter heißt jetzt „Pause wenn verdeckt" und prüft die tatsächlich freie
Fläche statt nur das Vordergrundfenster.

Kacheln zeigen den Dateinamen mit Endung. Vorher waren `probe.mkv` und `probe.webm`
beide nur „probe".

Behoben: Das Setzen der Schalter im Konstruktor löste deren Ereignis aus und
speicherte sofort zurück. Dadurch stand „Pause wenn verdeckt" stillschweigend auf
aus, und der Autostart-Eintrag konnte sich von selbst setzen. Ein `_laedt`-Wächter
in `MainWindow` hält die Ereignisse während des Ladens still.

## Videos aus Lively übernommen

Am 31.08.2026 wurden 11 Videos aus Lively nach `Videos\Tapeten` kopiert, zusammen
546 MB. Sie lagen nicht in Livelys Bibliothek, sondern in
`%LOCALAPPDATA%\Lively Wallpaper\Library\SaveData\wptmp`, jedes in einem eigenen
Zufallsordner. In der Bibliothek selbst stehen nur Livelys mitgelieferte HTML- und
WebGL-Hintergründe, kein einziges Video.

`wptmp` ist ein Zwischenordner. Ob Lively ihn irgendwann aufräumt, ist offen; die
Kopien bei Tapete sind davon unabhängig.

Sieben der Videos sind nativ 3440x1440 bei 60 Bildern und passen damit Pixel auf
Pixel auf einen Ultrawide-Bildschirm, die übrigen fünf sind 1080p und werden
hochskaliert. Alle zwölf im Ordner wurden einzeln von mpv geöffnet, alle lesbar,
alle H.264.
