; Setup fuer Tapete. Uebersetzt mit Inno Setup 6.
;
; Bewusst eine Benutzerinstallation (PrivilegesRequired=lowest): Sie landet unter
; %LOCALAPPDATA%\Programs\Tapete und fragt nicht nach Administratorrechten. Fuer
; einen animierten Hintergrund gibt es keinen Grund, ins Systemverzeichnis zu
; schreiben, und der Autostart-Ordner ist ohnehin benutzerbezogen.
;
; Bauen:  ISCC.exe Tapete.iss

#define Name      "Tapete"
#define Version   "1.13.75"
#define Autor     "Timm-Fabian Krotofil"
#define Exe       "Tapete.exe"
; Beide Pfade werden beim Uebersetzen ausgerechnet, nicht fest eingetragen.
; Vorher stand hier der Benutzername des Entwicklers und sein Laufwerk; in
; einem oeffentlichen Repository hat beides nichts zu suchen, und ein fremder
; Rechner haette damit ohnehin nicht bauen koennen.
#define Quelle    SourcePath + "..\fertig"
#define Videos    GetEnv("USERPROFILE") + "\Videos\Tapeten"

; Mit  ISCC /DMitVideos Tapete.iss  kommen die zwoelf Beispielvideos mit ins Setup.
; Ohne bleibt es beim Programm allein, rund 95 statt 640 MB.

[Setup]
AppId={{7C4B1E2A-9D33-4F16-A8C5-2E0B6D41F9A3}
AppName={#Name}
AppVersion={#Version}
AppVerName={#Name} {#Version}
AppPublisher={#Autor}
VersionInfoVersion={#Version}

DefaultDirName={autopf}\{#Name}
DefaultGroupName={#Name}
DisableProgramGroupPage=yes
DisableDirPage=no
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

OutputDir={#SourcePath}..\..
#ifdef MitVideos
OutputBaseFilename=Tapete-Setup-{#Version}-mit-Videos
#else
OutputBaseFilename=Tapete-Setup-{#Version}
#endif
UninstallDisplayIcon={app}\{#Exe}
UninstallDisplayName={#Name} {#Version}
WizardStyle=modern
SetupIconFile={#SourcePath}..\app.ico

; Das Nutzlast besteht fast nur aus einer gepackten .NET-Datei und einem
; Videoabspieler - beides laesst sich kaum weiter verdichten. Starke
; Kompression wuerde Minuten kosten und wenige Prozent bringen.
Compression=lzma2/fast
SolidCompression=no

; Laeuft Tapete noch, fragt das Setup nach dem Schliessen, statt Dateien
; im Zugriff liegen zu lassen.
CloseApplications=yes
RestartApplications=no

#ifdef MitVideos
[Types]
Name: "voll";  Description: "Programm und Beispielvideos"
Name: "klein"; Description: "Nur das Programm"

[Components]
Name: "prog";   Description: "Tapete";                          Types: voll klein; Flags: fixed
Name: "videos"; Description: "Zwoelf Beispielvideos (578 MB)";  Types: voll
#endif

[Languages]
Name: "de"; MessagesFile: "compiler:Languages\German.isl"

[Tasks]
Name: "desktopicon"; Description: "Verknuepfung auf dem Desktop anlegen"; Flags: unchecked
Name: "autostart";   Description: "Tapete mit Windows starten (ohne Fenster)"; Flags: unchecked

[Files]
Source: "{#Quelle}\{#Exe}";              DestDir: "{app}"; Flags: ignoreversion
Source: "{#Quelle}\mpv.exe";             DestDir: "{app}"; Flags: ignoreversion
Source: "{#Quelle}\d3dcompiler_43.dll";  DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourcePath}LIESMICH.txt";     DestDir: "{app}"; Flags: ignoreversion isreadme
#ifdef MitVideos
; onlyifdoesntexist: eine gleichnamige eigene Datei des Nutzers wird nicht ueberschrieben.
; uninsneveruninstall: beim Deinstallieren bleiben sie liegen, es sind Videodateien
; im Videos-Ordner und keine Programmbestandteile.
Source: "{#Videos}\*.mp4"; DestDir: "{code:VideoOrdner}"; Components: videos;     Flags: ignoreversion onlyifdoesntexist uninsneveruninstall
#endif

[Icons]
Name: "{group}\{#Name}";                 Filename: "{app}\{#Exe}"
Name: "{group}\{#Name} deinstallieren";  Filename: "{uninstallexe}"
Name: "{autodesktop}\{#Name}";           Filename: "{app}\{#Exe}"; Tasks: desktopicon
Name: "{userstartup}\{#Name}";           Filename: "{app}\{#Exe}"; Parameters: "--versteckt"; Tasks: autostart

[Run]
Filename: "{app}\{#Exe}"; Description: "Tapete jetzt starten"; Flags: nowait postinstall skipifsilent
; Bei einer stillen Installation gibt es kein Haekchen zum Anklicken. Ohne
; diese Zeile bliebe der Hintergrund nach einer automatischen Aktualisierung
; weg, bis jemand das Programm von Hand oeffnet.
Filename: "{app}\{#Exe}"; Parameters: "--versteckt"; Flags: nowait; Check: StilleInstallation

[UninstallDelete]
; Die Autostart-Verknuepfung legt auch das Programm selbst an, ueber seinen
; Schalter. Dann kennt das Setup sie nicht und wuerde sie stehen lassen.
Type: files; Name: "{userstartup}\{#Name}.lnk"

; Einstellungen und Protokoll unter %APPDATA%\Tapete bleiben absichtlich liegen.
; Wer neu installiert, hat seine Videowahl wieder. Wer sie wirklich loswerden
; will, loescht den Ordner von Hand - so haelt es auch der Rest der Welt.

[Code]

{ Wahr, wenn ohne Assistent installiert wird. Siehe [Run]: Nur dann startet
  Tapete am Ende von selbst wieder. }
function StilleInstallation: Boolean;
begin
  Result := WizardSilent;
end;
{ Wohin die Beispielvideos gehoeren. Inno kennt keine Konstante fuer den
  Videos-Ordner, deshalb aus der Registry gelesen - so greift auch eine
  Umleitung, etwa durch OneDrive. Faellt auf %USERPROFILE%\Videos zurueck. }
function VideoOrdner(Param: String): String;
var
  S: String;
begin
  if not RegQueryStringValue(HKEY_CURRENT_USER,
      'Software\Microsoft\Windows\CurrentVersion\Explorer\Shell Folders',
      'My Video', S) then
    S := ExpandConstant('{%USERPROFILE}') + '\Videos';
  Result := S + '\Tapeten';
end;
