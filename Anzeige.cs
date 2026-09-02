using System.Diagnostics;
using System.Globalization;
using System.IO.Pipes;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Text;
using System.Windows.Input;
using System.Windows.Threading;
using System.Windows;
using Microsoft.Win32;

namespace Tapete;

// aus Hintergrund.cs

/// <summary>
/// Spielt das Video hinter den Desktop-Symbolen ab, ueber den Abspieler mpv.
///
/// Warum nicht mit WPF: Ein HwndSource mit MediaElement sitzt zwar im Fensterbaum
/// an der richtigen Stelle, wird unter Progman aber nicht gezeichnet. Am 31.08.2026
/// gemessen — Fenster korrekt zwischen Symbolen und Hintergrundbild, Bildschirm
/// unveraendert: 0 von 10000 Bildpunkten in zwei Sekunden. Mit mpv an derselben
/// Stelle waren es 9123. WPF zeichnet ueber DirectComposition, und die setzt der
/// Fenstermanager innerhalb von Progman nicht zusammen. Ein einfaches Win32-Fenster
/// mit eigener Zeichenflaeche, wie mpv es mitbringt, wird gezeichnet.
/// </summary>
public sealed class Hintergrund : IDisposable
{
    private readonly DispatcherTimer _wacht = new() { Interval = TimeSpan.FromSeconds(2) };
    private readonly Action<string>? _fehler;
    /// <summary>Geraetename auf eigenes Video. Wo nichts steht, gilt <see cref="VideoPfad"/>.</summary>
    private readonly IReadOnlyDictionary<string, string>? _jeBildschirm;

    /// <summary>
    /// Weitere Schalter fuer mpv, vom Aufrufer aus den Einstellungen gebaut:
    /// Lautstaerke, Helligkeit, Saettigung, Tempo, HDR. Sie stehen hier bewusst
    /// fertig und nicht als Einzelwerte, damit diese Klasse nichts ueber die
    /// Einstellungen wissen muss.
    /// </summary>
    private readonly IReadOnlyList<string> _zusatz;
    /// <summary>Ein Eintrag je belegtem Bildschirm. Bei "*" mehrere, sonst einer.</summary>
    private readonly List<(Process Prozess, string Pipe)> _spieler = new();
    private bool _pausiert;
    private bool _entsorgt;

    public string VideoPfad { get; }
    public bool BeiVollbildPausieren { get; set; } = true;

    /// <summary>
    /// Anhalten, sobald der Rechner auf Akku laeuft. Wie
    /// <see cref="BeiVollbildPausieren"/> erst vom Takt gelesen und deshalb als
    /// Eigenschaft in Ordnung, nicht als Konstruktorparameter noetig.
    /// </summary>
    public bool BeiAkkuPausieren { get; set; } = true;

    /// <summary>
    /// Geraetename des Zielbildschirms, "*" fuer jeden einzeln, leer fuer den
    /// Hauptbildschirm.
    ///
    /// Kommt als Konstruktorparameter herein, nicht als Objektinitialisierer. Ein
    /// Initialisierer laeuft erst nach dem Konstruktor, und der ruft Aufbauen()
    /// auf - der Wert waere dort immer null gewesen. Genau das war der Fehler, an
    /// dem am 01.09.2026 ein ganzer Abend hing: Die Auswahl kam an, wurde
    /// gespeichert und weitergegeben, und der Aufbau sah sie trotzdem nie.
    /// </summary>
    public string? Bildschirm { get; }

    /// <summary>False, wenn der Aufbau schiefging. Dann laeuft kein Video.</summary>
    public bool Laeuft { get; private set; }

    /// <summary>
    /// Die Fehlermeldung kommt als Parameter herein, nicht als Ereignis. Ein Ereignis
    /// liesse sich erst nach dem Konstruktor abonnieren, und genau dort meldet
    /// Aufbauen() jeden Startfehler - er waere ins Leere gelaufen.
    /// </summary>
    public Hintergrund(string videoPfad, string? bildschirm = null,
        IReadOnlyDictionary<string, string>? videoJeBildschirm = null,
        IReadOnlyList<string>? mpvZusatz = null, Action<string>? fehler = null)
    {
        VideoPfad = videoPfad;
        Bildschirm = bildschirm;
        _jeBildschirm = videoJeBildschirm;
        // Ohne Angabe stumm, so wie es bis 1.3.7 fest verdrahtet war.
        _zusatz = mpvZusatz ?? ["--volume=0"];
        _fehler = fehler;
        Aufbauen();
        SystemEvents.DisplaySettingsChanged += BildschirmGeaendert;
        _wacht.Tick += (_, _) => SparenPruefen();
        _wacht.Start();
    }

    /// <summary>
    /// Sucht mpv.exe. Erst neben dem Programm, dann im eigenen Datenordner, zuletzt
    /// bei einer vorhandenen Lively-Installation.
    /// </summary>
    public static string? MpvSuchen()
    {
        string eigen = Path.GetDirectoryName(Environment.ProcessPath ?? "") ?? "";
        string[] orte =
        [
            Path.Combine(eigen, "mpv.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Tapete", "mpv.exe"),
            @"C:\Program Files\Lively Wallpaper\plugins\mpv\mpv.exe",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                         "Programs", "Lively Wallpaper", "plugins", "mpv", "mpv.exe"),
        ];
        string? treffer = orte.FirstOrDefault(File.Exists);
        Notiz("MpvSuchen: ProcessPath=" + (Environment.ProcessPath ?? "null") + " -> " + (treffer ?? "NICHTS"));
        return treffer;
    }

    /// <summary>
    /// Schreibt eine Zeile nach %APPDATA%\Tapete\protokoll.txt. Gebraucht, weil der
    /// Aufbau beim Anmelden zweimal fehlgeschlagen ist und ohne Fenster niemand die
    /// Fehlermeldung sieht. Haelt die letzten 200 Zeilen.
    /// </summary>
    /// <summary>
    /// Nimmt aus einer Protokollzeile den Namen des Benutzers heraus.
    ///
    /// Das Protokoll wandert per Mail zu mir; der Windows-Name des Testers hat
    /// darin nichts zu suchen. Die Dateinamen der Videos bleiben dagegen stehen,
    /// so entschieden vom Nutzer am 02.09.2026: Sie sagen ueber die Person nichts
    /// und sind bei mehreren Bildschirmen die halbe Diagnose.
    /// </summary>
    internal static string Entpersonalisieren(string text)
    {
        string heim = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return heim.Length == 0
            ? text
            : text.Replace(heim, "%USERPROFILE%", StringComparison.OrdinalIgnoreCase);
    }

    internal static void Notiz(string text)
    {
        try
        {
            text = Entpersonalisieren(text);
            string ordner = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Tapete");
            Directory.CreateDirectory(ordner);
            string datei = Path.Combine(ordner, "protokoll.txt");
            var zeilen = File.Exists(datei) ? File.ReadAllLines(datei).ToList() : new List<string>();
            zeilen.Add($"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {text}");
            if (zeilen.Count > 200) zeilen.RemoveRange(0, zeilen.Count - 200);
            File.WriteAllLines(datei, zeilen);
        }
        catch { }
    }

    private void Aufbauen()
    {
        Notiz($"Aufbauen startet fuer {Path.GetFileName(VideoPfad)}");
        string? mpv = MpvSuchen();
        if (mpv is null)
        {
            Notiz("ABBRUCH: mpv.exe nirgends gefunden");
            _fehler?.Invoke("mpv.exe fehlt. Sie gehoert neben Tapete.exe.");
            return;
        }

        IntPtr ziel = Native.BrauchtProgman() ? Native.FindProgman() : Native.FindWorkerW();
        if (ziel == IntPtr.Zero)
        {
            Notiz("ABBRUCH: Desktop-Ebene nicht gefunden (Progman fehlt)");
            _fehler?.Invoke("Desktop-Ebene nicht gefunden."); return;
        }

        // Waisen aufraeumen. Endet Tapete hart, laeuft Dispose nie und das alte mpv
        // haengt weiter unter Progman; beim naechsten Start kaeme ein zweites dazu.
        // Am 31.08.2026 mit zwei gleichzeitig erlebt. Getroffen wird nur die eigene
        // Kopie, ein vom Nutzer selbst gestartetes mpv bleibt unangetastet.
        foreach (var alt in Process.GetProcessesByName("mpv"))
        {
            try
            {
                if (string.Equals(alt.MainModule?.FileName, mpv, StringComparison.OrdinalIgnoreCase))
                    alt.Kill();
            }
            catch { }
            finally { alt.Dispose(); }
        }

        var (vx, vy, vBreite, vHoehe) = Native.VirtualScreen();
        var flaechen = ZielFlaechen(vx, vy, vBreite, vHoehe);

        // Die gewaehlte Einstellung gehoert mit ins Protokoll. Bei nur einem
        // Bildschirm sieht die Zeile "Zielflaeche" fuer "jeder einzeln" genauso aus
        // wie fuer einen namentlich gewaehlten Schirm; ohne diese Angabe liess sich
        // aus dem Protokoll nicht ablesen, was eingestellt war.
        string wahl = Bildschirm switch
        {
            "*" => "* (jeder Bildschirm einzeln)",
            null or "" => "(leer, also Hauptbildschirm)",
            _ => Bildschirm,
        };

        // Ein Kindfenster kann nicht ueber seinen Vater hinausragen. Deckt die
        // Desktop-Ebene nur den Hauptbildschirm ab, bleibt der zweite Schirm leer,
        // egal welche Masse hier stehen. Deshalb steht die Ebene im Protokoll.
        if (Native.GetWindowRect(ziel, out var ebene))
            Notiz($"Desktop-Ebene {ebene.Right - ebene.Left}x{ebene.Bottom - ebene.Top} " +
                  $"bei {ebene.Left},{ebene.Top} | Gesamtflaeche {vBreite}x{vHoehe} bei {vx},{vy} " +
                  $"| {Native.Bildschirme().Count} Bildschirm(e) | Einstellung {wahl} " +
                  $"| {flaechen.Count} Flaeche(n)");

        foreach (var (name, px, py, breite, hoehe) in flaechen)
        {
            // Hat der Schirm ein eigenes Video, laeuft dort das; sonst das gemeinsame.
            string video = _jeBildschirm?.GetValueOrDefault(name) ?? VideoPfad;
            EinenStarten(mpv, ziel, video, px, py, breite, hoehe);
        }

        Laeuft = _spieler.Count > 0;
        if (!Laeuft) _fehler?.Invoke("Kein Bildschirm liess sich belegen.");
    }

    /// <summary>
    /// Startet ein mpv fuer genau eine Flaeche und haengt es in die Desktop-Ebene.
    /// Je Bildschirm ein eigener Prozess mit eigener Pipe: mpv kann ein Video nicht
    /// auf zwei Fenster gleichzeitig legen, und ein einziges gespanntes Fenster war
    /// genau das, was der Nutzer am 01.09.2026 nicht wollte.
    /// </summary>
    private void EinenStarten(string mpv, IntPtr ziel, string video, int px, int py, int breite, int hoehe)
    {
        string pipe = "tapete_" + Guid.NewGuid().ToString("N")[..12];

        // Flags wie bei Lively 2.2.1.0, am 31.08.2026 aus dessen Kommandozeile gelesen.
        // Wichtig: kein Ton, Endlosschleife, keine Bedienelemente, keine Mausannahme,
        // Hardware-Dekodierung. --no-config haelt eine vorhandene mpv.conf des Nutzers raus.
        var start = new ProcessStartInfo(mpv)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
        };
        foreach (string a in new[]
        {
            // Fuellen statt einpassen. Ohne das laesst mpv das Seitenverhaeltnis
            // stehen und legt schwarze Balken daneben: ein 21:9-Video auf einem
            // 16:9-Schirm bekommt Balken oben und unten, ein 16:9-Video auf einem
            // Ultrawide welche links und rechts. Am 01.09.2026 von einem Nutzer
            // mit zwei Bildschirmen gemeldet, im Bildschirmfoto deutlich zu sehen.
            // panscan zoomt stattdessen bis zum Rand und schneidet den Ueberstand
            // ab. Verzerrt wird nichts, dafuer waere keepaspect=no noetig.
            "--panscan=1.0",
            "--loop-file", "--keep-open", "--media-controls=no",
            "--force-window=yes", "--no-window-dragging", "--cursor-autohide=no",
            "--stop-screensaver=no", "--input-default-bindings=no", "--no-border",
            "--input-cursor=no", "--no-osc", "--no-config",
            // d3d11va statt auto-safe: derselbe Weg, aber ohne Rueckfall auf eine
            // Kopiervariante, die Bilder in den Arbeitsspeicher zurueckholt.
            "--hwdec=d3d11va",
            // Sparprofil von mpv. Am 31.08.2026 gemessen: senkt den 3D-Anteil von
            // 3,85 auf 1,31 Prozent. Am Dekodierer aendert es nichts, der haengt
            // an Aufloesung und Bildrate des Videos.
            "--profile=fast",
            "--input-ipc-server=" + pipe, "--geometry=-9999:0",
        }) start.ArgumentList.Add(a);

        // Die Schalter aus den Einstellungen, danach erst die Datei. mpv nimmt
        // alles nach dem Dateinamen nicht mehr als Option an.
        foreach (string a in _zusatz) start.ArgumentList.Add(a);
        start.ArgumentList.Add(video);

        Process? prozess;
        try { prozess = Process.Start(start); }
        catch (Exception e) { Notiz("ABBRUCH: Process.Start warf " + e.GetType().Name + ": " + e.Message); return; }
        if (prozess is null) { Notiz("ABBRUCH: Process.Start lieferte null"); return; }

        const int maxWarte = 30000;
        var uhrAufbau = Stopwatch.StartNew();
        IntPtr fenster = FensterAbwarten(prozess.Id, maxWarte);
        Notiz($"Warten auf mpv-Fenster: {uhrAufbau.ElapsedMilliseconds} ms, gefunden: {fenster != IntPtr.Zero}");
        if (fenster == IntPtr.Zero)
        {
            Notiz($"ABBRUCH: mpv (PID {prozess.Id}) oeffnete in {maxWarte} ms kein Fenster. Prozess lebt noch: {!prozess.HasExited}");
            try { prozess.Kill(entireProcessTree: true); } catch { }
            prozess.Dispose();
            return;
        }

        Native.SetParent(fenster, ziel);
        // Hinter die Symbol-Ebene, damit die Symbole sichtbar und klickbar bleiben.
        //
        // Der Anker haengt am Fensterbaum, nicht am Windows-Build. Zwei Fallen:
        // SetWindowPos ignoriert einen Anker, der kein Geschwister ist, und ein
        // Anker von 0 heisst HWND_TOP, also VOR die Symbole. Beides endete am
        // 01.09.2026 bei einem Tester damit, dass die Symbole verdeckt waren und
        // sich nichts mehr auf dem Desktop anklicken liess.
        //
        // Bleibt nur HWND_BOTTOM, kann das Video hinter dem statischen
        // Hintergrundbild landen und ist dann unsichtbar. Das ist der bessere
        // Fehlerfall: ein unsichtbares Video sieht man, ein blockierter Desktop
        // kostet den Tester den Feierabend. Welcher Fall eintrat, sagt die Notiz.
        IntPtr defView = Native.FindDefView();
        bool geschwister = defView != IntPtr.Zero && Native.GetParent(defView) == ziel;
        IntPtr anker = geschwister ? defView : Native.HWND_BOTTOM;
        // Koordinaten zaehlen ab der linken oberen Ecke der Gesamtflaeche, nicht ab 0,0
        // des Hauptbildschirms. Bei einem Monitor links vom Hauptbildschirm ist vx negativ.
        bool gesetzt = Native.SetWindowPos(fenster, anker, px, py, breite, hoehe,
            Native.SWP_NOACTIVATE | Native.SWP_SHOWWINDOW);
        Notiz($"Z-Anker: Build {Environment.OSVersion.Version.Build}, DefView {defView}, " +
              $"Geschwister {geschwister}, benutzt {(geschwister ? "DefView" : "HWND_BOTTOM")}, " +
              $"SetWindowPos {gesetzt}");
        _spieler.Add((prozess, pipe));
        Notiz($"Aufbau geglueckt, mpv PID {prozess.Id}, Fenster {fenster}, " +
              $"{breite}x{hoehe} bei {px},{py}, {Path.GetFileName(video)}");
    }

    /// <summary>
    /// Wohin das Abspielfenster gehoert: auf einen Bildschirm oder ueber alle.
    /// Leerer Name heisst Hauptbildschirm, "*" heisst alle zusammen. Ist der
    /// gemerkte Bildschirm abgesteckt, faellt es auf den Hauptbildschirm zurueck.
    /// </summary>
    private List<(string Name, int X, int Y, int Breite, int Hoehe)> ZielFlaechen(
        int vx, int vy, int vBreite, int vHoehe)
    {
        if (Bildschirm == "*")
        {
            var schirme = Native.Bildschirme();
            if (schirme.Count == 0)
            {
                Notiz("Zielflaeche: kein Bildschirm gefunden, nehme die Gesamtflaeche");
                return [("", 0, 0, vBreite, vHoehe)];
            }
            // Je Bildschirm eine eigene Flaeche, nicht ein Fenster ueber alle. Ein
            // gespanntes Fenster zeigt das Video einmal quer ueber beide Schirme;
            // gewollt ist es auf jedem Schirm einzeln und vollstaendig.
            foreach (var s in schirme)
                Notiz($"Zielflaeche: {s.Name} {s.Breite}x{s.Hoehe} bei {s.Flaeche.Left},{s.Flaeche.Top}");
            return schirme
                .Select(s => (s.Name, s.Flaeche.Left - vx, s.Flaeche.Top - vy, s.Breite, s.Hoehe))
                .ToList();
        }

        var alle = Native.Bildschirme();
        var b = alle.FirstOrDefault(s => s.Name == Bildschirm) ?? alle.FirstOrDefault(s => s.Haupt);
        if (b is null)
        {
            Notiz("Zielflaeche: kein Bildschirm gefunden, nehme die Gesamtflaeche");
            return [("", 0, 0, vBreite, vHoehe)];
        }
        Notiz($"Zielflaeche: {b.Name} {b.Breite}x{b.Hoehe} bei {b.Flaeche.Left},{b.Flaeche.Top}");
        return [(b.Name, b.Flaeche.Left - vx, b.Flaeche.Top - vy, b.Breite, b.Hoehe)];
    }

    /// <summary>
    /// Wartet, bis mpv sein Fenster hat. Ohne das Warten laeuft SetParent ins Leere,
    /// das Fenster entsteht erst ein bis zwei Sekunden nach dem Prozessstart.
    ///
    /// Der Deckel muss grosszuegig sein. mpv.exe ist 115 MB gross und wird beim
    /// Anmelden kalt von der Platte geladen, waehrend ein Dutzend anderer
    /// Autostart-Programme dieselbe SSD belegen. Im Leerlauf mit warmem Dateicache
    /// steht das Fenster nach 241 ms; beim Anmelden am 31.08.2026 reichten 3000 ms
    /// nicht, der Zweig "kein Fenster" schlug zu und der Hintergrund blieb leer.
    ///
    /// ponytail: Wartet blockierend auf dem Oberflaechen-Faden. Das faellt nur im
    /// Fehlerfall auf, im Normalfall sind es Millisekunden, und beim Anmelden gibt
    /// es ohnehin kein Fenster zum Einfrieren. Wer es ganz weghaben will, muss
    /// Aufbauen() auf einen Hintergrundfaden legen - dann wird aber Laeuft erst
    /// spaeter wahr, und der Aufrufer in App.HintergrundSetzen fragt es sofort ab.
    /// </summary>
    private static IntPtr FensterAbwarten(int pid, int maxMs = 30000)
    {
        var uhr = Stopwatch.StartNew();
        while (uhr.ElapsedMilliseconds < maxMs)
        {
            IntPtr treffer = IntPtr.Zero;
            Native.EnumWindows((h, _) =>
            {
                Native.GetWindowThreadProcessId(h, out uint p);
                if (p != (uint)pid) return true;
                var c = new StringBuilder(64);
                Native.GetClassName(h, c, c.Capacity);
                if (c.ToString() == "mpv") { treffer = h; return false; }
                return true;
            }, IntPtr.Zero);
            if (treffer != IntPtr.Zero) return treffer;
            Thread.Sleep(150);
        }
        return IntPtr.Zero;
    }

    /// <summary>Schickt allen laufenden mpv denselben Befehl ueber ihre Named Pipe.</summary>
    private void AnMpv(string befehl)
    {
        foreach (var (_, pipe) in _spieler)
        {
            try
            {
                // InOut, nicht Out: mpv legt seine Pipe beidseitig an, eine reine
                // Schreibverbindung laeuft in den Zeitablauf. Am 31.08.2026 gemessen.
                using var rohr = new NamedPipeClientStream(".", pipe, PipeDirection.InOut);
                rohr.Connect(400);
                byte[] b = Encoding.UTF8.GetBytes(befehl + "\n");
                rohr.Write(b, 0, b.Length);
                rohr.Flush();
            }
            catch { /* mpv weg oder noch nicht bereit - beim naechsten Takt wieder */ }
        }
    }

    private void BildschirmGeaendert(object? sender, EventArgs e)
    {
        // Nach einem Aufloesungswechsel baut Windows die Desktop-Ebene neu auf,
        // deshalb mpv komplett neu anlegen.
        Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            if (_entsorgt) return;
            Abbauen();
            Aufbauen();
        }, DispatcherPriority.Background);
    }

    private void SparenPruefen()
    {
        if (_spieler.Count == 0 || _spieler.All(s => s.Prozess.HasExited)) return;

        bool soll = (BeiVollbildPausieren && Native.DesktopVerdeckt()) || AufAkku();
        if (soll == _pausiert) return;

        _pausiert = soll;
        Notiz($"Pause {(soll ? "an" : "aus")}, Akku: {AufAkku()}");
        AnMpv("{\"command\":[\"set_property\",\"pause\"," + (soll ? "true" : "false") + "]}");
    }

    /// <summary>
    /// Laeuft der Rechner gerade auf Akku? Ein Rechner ohne Akku meldet
    /// <c>NoSystemBattery</c> und faellt damit hier nie hinein; bei unbekanntem
    /// Zustand wird nicht pausiert, denn ein stehendes Bild ohne Grund faellt
    /// mehr auf als ein laufendes.
    /// </summary>
    private bool AufAkku()
    {
        if (!BeiAkkuPausieren) return false;
        try
        {
            var stand = System.Windows.Forms.SystemInformation.PowerStatus;
            return stand.PowerLineStatus == System.Windows.Forms.PowerLineStatus.Offline
                && stand.BatteryChargeStatus != System.Windows.Forms.BatteryChargeStatus.NoSystemBattery;
        }
        catch { return false; }
    }

    private void Abbauen()
    {
        foreach (var (prozess, _) in _spieler)
        {
            try
            {
                if (!prozess.HasExited) prozess.Kill(entireProcessTree: true);
                prozess.Dispose();
            }
            catch { }
        }
        _spieler.Clear();
        _pausiert = false;
        Laeuft = false;
    }

    public void Dispose()
    {
        if (_entsorgt) return;
        _entsorgt = true;
        _wacht.Stop();
        SystemEvents.DisplaySettingsChanged -= BildschirmGeaendert;
        Abbauen();
        Native.RefreshDesktopWallpaper();
    }

    /// <summary>
    /// Holt die Desktop-Symbole zurueck. Nur noch fuer Altlasten: Fassungen vor dem
    /// 31.08.2026 haben die Symbole ausgeblendet und konnten sie nach einem Absturz
    /// nicht zurueckholen.
    /// </summary>
    public static void SymboleWiederherstellen()
    {
        IntPtr liste = Native.FindSymbolListe(Native.FindDefView());
        if (liste != IntPtr.Zero && !Native.IsWindowVisible(liste))
            Native.ShowWindow(liste, Native.SW_SHOWNA);
    }

    /// <summary>
    /// Video-Endungen, die mpv abspielt. Nicht geraten, sondern aus mpvs eigenem
    /// Installationsskript uebernommen (installer\mpv-install.bat, Bau vom
    /// 31.08.2026): alle Zeilen, die dort als "video" eingetragen werden.
    /// </summary>
    private static readonly HashSet<string> Endungen = new(
        ("264 265 3g2 3gp 3gp2 3gpp 3iv asf avc avi divx dv dvr evo evob f4v flc fli flic "
        + "flv gxf h264 h265 hdmov hdv hevc m1v m2t m2ts m2v m4v mk3d mkv mod mov mp2v mp4 "
        + "mp4v mpe mpeg mpeg2 mpeg4 mpg mpg4 mpv mpv2 mts mtv mxf nsv nut ogm ogv ogx qt "
        + "rm rmvb tod trp ts tsa tsv tts vfw vob vro webm wm wmv wtv x264 x265 xvid y4m "
        + "yuv")
        .Split(' ', StringSplitOptions.RemoveEmptyEntries),
        StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Filterzeile fuer den Auswahldialog, aus derselben Liste wie IstUnterstuetzt.
    /// Getrennte Listen liefen auseinander: Der Dialog kannte sechs Endungen,
    /// Ziehen und Ablegen 75.
    /// </summary>
    public static string DialogFilter =>
        "Videos|" + string.Join(";", Endungen.OrderBy(e => e).Select(e => "*." + e))
        + "|Alle Dateien|*.*";

    public static bool IstUnterstuetzt(string pfad)
    {
        string e = Path.GetExtension(pfad);
        return e.Length > 1 && Endungen.Contains(e[1..]);
    }
}

// aus Verkleinern.cs

/// <summary>
/// Rechnet ein Video einmalig auf die Groesse des Zielbildschirms herunter.
///
/// Beim Abspielen zu skalieren bringt nichts: mpv dekodiert erst in voller
/// Aufloesung und skaliert danach. Der teure Teil passiert also vorher. Am
/// 31.08.2026 auf einer RX 7800 XT gemessen, jeweils bildschirmfuellend auf
/// 3440x1440, Mittel aus sechs Abtastungen:
///
///     3440x1440@60 (Original)    Dekoder 28,0 %   3D 2,4 %
///     1920x804@60  (gerechnet)   Dekoder  9,4 %   3D 2,2 %
///     1920x804@30  (gerechnet)   Dekoder  4,8 %   3D 1,1 %
///
/// Sichtbar kostet das nichts. mpv passt das Video ohnehin seitenverhaeltnistreu
/// in das Monitorrechteck ein; mehr Bildpunkte, als der Bildschirm hat, werden
/// nie dargestellt. Die Bildrate bleibt deshalb unangetastet - sie zu halbieren
/// spart noch einmal gut das Doppelte, ist aber im Gegensatz dazu zu sehen.
///
/// Kodiert wird mit derselben mpv.exe, die auch abspielt: sie bringt libavcodec
/// mit. Ein zweites Programm ist dafuer nicht noetig.
/// </summary>
internal static class Verkleinern
{
    /// <summary>Wo die gerechneten Fassungen liegen. Darf geloescht werden.</summary>
    internal static string Ordner => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Tapete", "klein");

    /// <summary>
    /// Encoder in der Reihenfolge, in der sie versucht werden: erst die drei
    /// Hardware-Wege der grossen Hersteller, dann MediaFoundation als breiter
    /// Windows-Weg, zuletzt libx264 in Software. Der erste, der eine brauchbare
    /// Datei liefert, gewinnt.
    ///
    /// Fest verdrahtet geht hier nicht: Welcher Encoder vorhanden ist, haengt an
    /// der Grafikkarte, und die ist bei einem weitergegebenen Programm unbekannt.
    /// libx264 am Ende laeuft ueberall, nur langsamer.
    /// </summary>
    private static readonly string[] Encoder =
        ["h264_amf", "h264_nvenc", "h264_qsv", "h264_mf", "libx264"];

    /// <summary>
    /// Zielmasse: decken den Bildschirm gerade noch ab, behalten das
    /// Seitenverhaeltnis und werden nie groesser als die Vorlage. Gerade Zahlen,
    /// weil H.264 in 2x2-Bloecken rechnet.
    ///
    /// Der groessere der beiden Faktoren, nicht der kleinere. Bis zum 01.09.2026
    /// stand hier Math.Min, also die Masse, die vollstaendig in den Bildschirm
    /// passen. Seit mpv mit panscan fuellt statt einzupassen, waere das falsch:
    /// Ein 3440x1440-Video fuer einen 1920x1080-Schirm waere auf 1920x804
    /// gerechnet worden, und mpv haette diese 804 Zeilen anschliessend wieder auf
    /// 1080 hochgezogen. Mit Math.Max sind es 2580x1080, senkrecht Punkt auf
    /// Punkt, und quer schneidet mpv ab.
    /// </summary>
    internal static (int Breite, int Hoehe) Ziel(int vb, int vh, int bb, int bh)
    {
        if (vb <= 0 || vh <= 0 || bb <= 0 || bh <= 0) return (vb, vh);
        double f = Math.Max((double)bb / vb, (double)bh / vh);
        if (f >= 1) return (vb, vh);
        return (Gerade(vb * f), Gerade(vh * f));
    }

    private static int Gerade(double x) => Math.Max(2, (int)Math.Round(x / 2) * 2);

    /// <summary>
    /// Lohnt das Rechnen? Erst ab einem Fuenftel weniger Bildpunkten. Darunter
    /// stuenden Aufwand und der Qualitaetsverlust des Neukodierens gegen fast nichts.
    /// </summary>
    private static bool Lohnt(int vb, int vh, int zb, int zh, bool halbieren) =>
        // Bei halber Bildrate lohnt es immer: die Ersparnis kommt dann nicht aus den
        // Bildpunkten, sondern aus der Zahl der Bilder. Auch ein Video, das ohnehin
        // auf den Schirm passt, wird dafuer neu gerechnet.
        halbieren || (long)zb * zh * 5 <= (long)vb * vh * 4;

    /// <summary>
    /// Masse der Flaeche, auf der gespielt wird. Dieselbe Wahl wie in
    /// Hintergrund.ZielFlaechen: "*" heisst je Bildschirm einzeln, sonst der
    /// benannte, und ohne Wahl der Hauptbildschirm.
    ///
    /// Bei "*" zaehlt der groesste Bildschirm, nicht die Gesamtflaeche. Seit ein
    /// Video je Schirm laeuft, wuerde die Gesamtflaeche eine viel zu breite Fassung
    /// erzeugen, von der jeder einzelne Schirm den Grossteil wegschneidet.
    ///
    /// ponytail: eine gemeinsame Fassung nach dem groessten Schirm, nicht eine je
    /// Schirm. Der kleinere rechnet beim Abspielen herunter. Eine Datei je Groesse
    /// erst, wenn das auf dem kleinen Schirm messbar Last kostet.
    /// </summary>
    internal static (int Breite, int Hoehe) Bildschirmmasse(string? bildschirm)
    {
        var alle = Native.Bildschirme();
        if (bildschirm == "*")
        {
            var groesster = alle.OrderByDescending(s => (long)s.Breite * s.Hoehe).FirstOrDefault();
            if (groesster is not null) return (groesster.Breite, groesster.Hoehe);
        }
        else
        {
            var b = alle.FirstOrDefault(s => s.Name == bildschirm) ?? alle.FirstOrDefault(s => s.Haupt);
            if (b is not null) return (b.Breite, b.Hoehe);
        }
        var (_, _, w, h) = Native.VirtualScreen();
        return (w, h);
    }

    /// <summary>
    /// Dateiname im Zwischenspeicher. Die Groesse der Vorlage steckt mit drin:
    /// wird ein Video durch ein gleichnamiges anderes ersetzt, passt der Name
    /// nicht mehr und es wird neu gerechnet.
    /// </summary>
    private static string Name(string video, int bb, int bh, bool halbieren)
    {
        long gr = 0;
        try { gr = new FileInfo(video).Length; } catch { }
        // Das "-h" haelt die halbierte Fassung von der vollen getrennt. Sonst bekaeme
        // man nach dem Umlegen des Schalters die alte Datei zurueck.
        //
        // Das "-d" steht fuer die Rechenregel, nicht fuer eine Eigenschaft der Datei.
        // Bis 1.2.0 passte Ziel() das Video in den Bildschirm ein, seit dem 01.09.2026
        // deckt es ihn ab. Wer aktualisiert, hat im Zwischenspeicher noch Dateien nach
        // der alten Regel liegen; ohne diesen Buchstaben wuerden sie weiterverwendet
        // und waeren zu klein. Aendert sich die Regel erneut, kommt der naechste
        // Buchstabe. Die alten Dateien stoeren nicht, der Ordner darf jederzeit weg.
        return $"{Path.GetFileNameWithoutExtension(video)}-{gr}-{bb}x{bh}{(halbieren ? "-h" : "")}-d.mp4";
    }

    /// <summary>
    /// Raeumt alles weg, was zu einem Video im Zwischenspeicher liegt: gerechnete
    /// Fassungen, halbe Dateien, Merkzettel. Fuer den Fall, dass das Video geht.
    /// </summary>
    internal static void Vergessen(string video)
    {
        try
        {
            if (!Directory.Exists(Ordner)) return;
            string stamm = Path.GetFileNameWithoutExtension(video) + "-";
            foreach (string f in Directory.EnumerateFiles(Ordner, stamm + "*"))
                try { File.Delete(f); } catch { }
        }
        catch { }
    }

    /// <summary>Die gerechnete Fassung, falls sie schon vorliegt.</summary>
    internal static string? Fertig(string video, int bb, int bh, bool halbieren)
    {
        string p = Path.Combine(Ordner, Name(video, bb, bh, halbieren));
        return File.Exists(p) ? p : null;
    }

    /// <summary>
    /// Breite, Hoehe und Bildrate eines Videos, bei mpv selbst erfragt. Null,
    /// wenn mpv fehlt oder nichts Brauchbares meldet.
    /// </summary>
    internal static (int Breite, int Hoehe, double Bilder)? Masse(string video)
    {
        string? mpv = Hintergrund.MpvSuchen();
        if (mpv is null) return null;

        string ausgabe = Laufen(mpv,
        [
            "--no-config", "--vo=null", "--ao=null", "--frames=1",
            "--term-playing-msg=TAPETEMASS ${width}x${height}x${container-fps}",
            video,
        ], 30_000, "Masse messen");

        var t = Regex.Match(ausgabe, @"TAPETEMASS (\d+)x(\d+)x(\S*)");
        if (!t.Success)
        {
            Hintergrund.Notiz("Verkleinern: mpv meldete keine Masse fuer " + Path.GetFileName(video));
            return null;
        }

        // Bildrate faellt auf 30 zurueck. Sie geht nur in die Datenrate ein; ein
        // ungenauer Wert macht die Datei etwas groesser oder kleiner, mehr nicht.
        double bilder = double.TryParse(t.Groups[3].Value, NumberStyles.Float,
            CultureInfo.InvariantCulture, out double f) && f > 0 ? f : 30;

        return (int.Parse(t.Groups[1].Value), int.Parse(t.Groups[2].Value), bilder);
    }

    /// <summary>
    /// Erzeugt die gerechnete Fassung und gibt ihren Pfad zurueck. Null heisst:
    /// nicht noetig oder nicht gelungen, dann bleibt es beim Original.
    ///
    /// Dauert je nach Encoder Sekunden bis Minuten und gehoert deshalb nicht in
    /// den Bedienfaden.
    /// </summary>
    internal static string? Erzeugen(string video, int bb, int bh, bool halbieren)
    {
        string? mpv = Hintergrund.MpvSuchen();
        if (mpv is null) return null;

        string? schon = Fertig(video, bb, bh, halbieren);
        if (schon is not null) return schon;

        string ziel = Path.Combine(Ordner, Name(video, bb, bh, halbieren));

        // Merkzettel fuer "lohnt nicht". Ohne ihn startet bei jedem Anzeigen und
        // bei jedem Programmstart ein mpv, nur um dieselbe Absage zu errechnen.
        // Der Name traegt Videogroesse und Bildschirmmasse, der Zettel gilt also
        // genau fuer diese Paarung und veraltet von selbst.
        string nein = ziel + ".nein";
        if (File.Exists(nein)) return null;

        var m = Masse(video);
        if (m is null) return null;

        var (zb, zh) = Ziel(m.Value.Breite, m.Value.Hoehe, bb, bh);
        if (!Lohnt(m.Value.Breite, m.Value.Hoehe, zb, zh, halbieren))
        {
            Hintergrund.Notiz($"Verkleinern: {m.Value.Breite}x{m.Value.Hoehe} auf {bb}x{bh} lohnt nicht, gemerkt");
            try { Directory.CreateDirectory(Ordner); File.WriteAllBytes(nein, []); } catch { }
            return null;
        }

        // Halbe Bildrate, auf ganze Bilder gerundet. 59,94 wird so zu 30.
        double zielBilder = halbieren
            ? Math.Max(1, Math.Round(m.Value.Bilder / 2))
            : m.Value.Bilder;

        // Bildpunkte mal Bilder mal 0,05 Bit. Ergibt fuer 1920x804 bei 60 Bildern
        // rund 4,6 Mbit - die Groessenordnung, mit der die Messung oben lief.
        long rate = Math.Clamp((long)(zb * (double)zh * zielBilder * 0.05), 1_000_000, 20_000_000);

        string halb = ziel + ".teil";
        try
        {
            Directory.CreateDirectory(Ordner);
            if (File.Exists(halb)) File.Delete(halb);
        }
        catch (Exception e)
        {
            Hintergrund.Notiz("Verkleinern, Ordner: " + e.GetType().Name + ": " + e.Message);
            return null;
        }

        var uhr = Stopwatch.StartNew();
        foreach (string enc in Encoder)
        {
            Laufen(mpv,
            [
                "--no-config", "--no-audio",
                halbieren
                    ? $"--vf=scale={zb}:{zh},fps={zielBilder.ToString(CultureInfo.InvariantCulture)}"
                    : $"--vf=scale={zb}:{zh}",
                "--ovc=" + enc,
                $"--ovcopts=b={rate}",
                "--of=mp4", "--o=" + halb, video,
            ], 15 * 60 * 1000, "Kodieren mit " + enc);

            // Nicht auf einen Rueckgabewert verlassen: massgeblich ist, ob eine
            // brauchbare Datei entstanden ist. Ein Encoder, den es nicht gibt,
            // hinterlaesst eine leere oder gar keine.
            try
            {
                if (File.Exists(halb) && new FileInfo(halb).Length > 100_000)
                {
                    File.Move(halb, ziel, overwrite: true);
                    Hintergrund.Notiz($"Verkleinern: {Path.GetFileName(video)} " +
                        $"{m.Value.Breite}x{m.Value.Hoehe} -> {zb}x{zh}" +
                        (halbieren ? $" bei {zielBilder:0} Bildern" : "") +
                        $" mit {enc} in {uhr.ElapsedMilliseconds} ms");
                    return ziel;
                }
                if (File.Exists(halb)) File.Delete(halb);
            }
            catch (Exception e)
            {
                Hintergrund.Notiz("Verkleinern, Ablegen: " + e.GetType().Name + ": " + e.Message);
                return null;
            }
            Hintergrund.Notiz("Verkleinern: " + enc + " lieferte nichts");
        }

        Hintergrund.Notiz("Verkleinern: kein Encoder hat funktioniert");
        return null;
    }

    /// <summary>
    /// Startet mpv, wartet und gibt zurueck, was auf beiden Ausgabekanaelen stand.
    ///
    /// Beide, weil nicht geprueft ist, auf welchem mpv welche Meldung ablegt, und
    /// eine Vermutung darueber hier nichts zu suchen hat. Gelesen wird nebenlaeufig:
    /// nacheinander mit ReadToEnd blockiert sich das Paar gegenseitig, sobald ein
    /// Kanal seinen Puffer fuellt - und beim Kodieren tut er das.
    /// </summary>
    private static string Laufen(string exe, string[] args, int maxMs, string was)
    {
        var start = new ProcessStartInfo(exe)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (string a in args) start.ArgumentList.Add(a);

        try
        {
            using var p = Process.Start(start);
            if (p is null) { Hintergrund.Notiz("Verkleinern: " + was + " liess sich nicht starten"); return ""; }

            // Der Rechner soll waehrenddessen bedienbar bleiben. Das Kodieren hat
            // es nicht eilig, es laeuft ja im Hintergrund.
            try { p.PriorityClass = ProcessPriorityClass.BelowNormal; } catch { }

            var aus = p.StandardOutput.ReadToEndAsync();
            var err = p.StandardError.ReadToEndAsync();
            if (!p.WaitForExit(maxMs))
            {
                try { p.Kill(entireProcessTree: true); } catch { }
                Hintergrund.Notiz($"Verkleinern: {was} lief laenger als {maxMs} ms, abgebrochen");
            }
            return aus.Result + "\n" + err.Result;
        }
        catch (Exception e)
        {
            Hintergrund.Notiz($"Verkleinern, {was}: {e.GetType().Name}: {e.Message}");
            return "";
        }
    }

    /// <summary>
    /// Rechenprobe fuer Ziel() und Lohnt(). Gibt zurueck, was nicht stimmt.
    ///
    /// Laeuft bei jedem Start mit und schreibt eine Zeile ins Protokoll. Kostet
    /// Mikrosekunden, faellt dafuer aber auf, ohne dass jemand zusehen muss - und
    /// es ist die einzige Stelle im Programm, an der gerechnet statt abgefragt wird.
    /// </summary>
    internal static List<string> Selbstpruefung()
    {
        var fehler = new List<string>();
        void Pruefe(string was, bool gut) { if (!gut) fehler.Add(was); }

        // 21:9 auf 16:9: senkrecht Punkt auf Punkt, quer schneidet mpv ab.
        Pruefe("3440x1440 auf 1920x1080 -> 2580x1080", Ziel(3440, 1440, 1920, 1080) == (2580, 1080));
        Pruefe("Ergebnis deckt den Schirm", Ziel(3440, 1440, 1920, 1080) is (>= 1920, >= 1080));
        Pruefe("gleich gross bleibt unveraendert", Ziel(3440, 1440, 3440, 1440) == (3440, 1440));
        Pruefe("kleiner als der Schirm wird nie vergroessert", Ziel(1920, 1080, 3440, 1440) == (1920, 1080));
        Pruefe("1920x1080 auf 1280x720", Ziel(1920, 1080, 1280, 720) == (1280, 720));
        Pruefe("gerade Zahlen", Ziel(1999, 1001, 1000, 1000).Breite % 2 == 0);
        Pruefe("Unsinn bleibt Unsinn", Ziel(0, 0, 1920, 1080) == (0, 0));
        Pruefe("Faktor zwei lohnt", Lohnt(3440, 1440, 2580, 1080, false));
        Pruefe("gleich gross lohnt nicht", !Lohnt(1920, 1080, 1920, 1080, false));
        Pruefe("ein Zehntel weniger lohnt nicht", !Lohnt(1920, 1080, 1820, 1024, false));
        Pruefe("halbe Bildrate lohnt immer", Lohnt(1920, 1080, 1920, 1080, true));

        return fehler;
    }
}

// aus Bildschirmschoner.cs

/// <summary>
/// Dasselbe Video als Bildschirmschoner.
///
/// Ein Windows-Bildschirmschoner ist eine gewoehnliche Programmdatei mit der
/// Endung .scr; die Systemsteuerung nimmt nur diese Endung an, und ausgewaehlt
/// wird sie ueber den Wert SCRNSAVE.EXE unter HKCU\Control Panel\Desktop.
/// Belegt in der Dokumentation von Microsoft, abgerufen am 02.09.2026:
/// learn.microsoft.com/windows/win32/lwef/screen-saver-library
///
/// Deshalb legt Tapete eine zweite Datei Tapete.scr an - als harte Verknuepfung
/// auf die eigene Programmdatei, nicht als Kopie. Eine Kopie waere 155 MB gross,
/// die Verknuepfung kostet nichts.
///
/// Bewusst nicht ueber <see cref="Hintergrund"/> gebaut: Der raeumt beim Aufbauen
/// alte mpv-Prozesse weg, und das waere hier ausgerechnet der laufende Desktop-
/// Hintergrund. Der Schoner macht seine eigenen Fenster auf und laesst alles
/// andere in Ruhe.
/// </summary>
internal sealed class Bildschirmschoner
{
    private readonly List<Window> _fenster = [];
    private readonly List<Process> _spieler = [];
    private bool _beendet;

    /// <summary>
    /// Wo die .scr liegt: neben der Programmdatei. Faellt das aus, etwa weil dort
    /// nicht geschrieben werden darf, nimmt <see cref="Anlegen"/> den eigenen
    /// Datenordner.
    /// </summary>
    private static string ScrNebenExe
    {
        get
        {
            string exe = Environment.ProcessPath ?? "";
            string ordner = Path.GetDirectoryName(exe) ?? "";
            return Path.Combine(ordner, "Tapete.scr");
        }
    }

    private static string ScrImDatenordner => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Tapete", "Tapete.scr");

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CreateHardLinkW(string neu, string vorhanden, IntPtr reserviert);

    /// <summary>
    /// Legt die .scr an und liefert ihren Pfad, oder null.
    ///
    /// Erst als harte Verknuepfung; geht das nicht, weil das Ziel auf einem anderen
    /// Laufwerk oder einem Dateisystem ohne Verknuepfungen liegt, wird kopiert.
    /// Eine vorhandene Datei mit abweichender Groesse wird ersetzt: Nach einer
    /// Aktualisierung zeigt die alte Verknuepfung sonst weiter auf die alte Fassung.
    /// </summary>
    internal static string? Anlegen()
    {
        string exe = Environment.ProcessPath ?? "";
        if (exe.Length == 0 || !File.Exists(exe)) return null;
        long groesse = new FileInfo(exe).Length;

        foreach (string ziel in new[] { ScrNebenExe, ScrImDatenordner })
        {
            try
            {
                if (File.Exists(ziel))
                {
                    if (new FileInfo(ziel).Length == groesse) return ziel;
                    File.Delete(ziel);
                }
                Directory.CreateDirectory(Path.GetDirectoryName(ziel)!);

                if (CreateHardLinkW(ziel, exe, IntPtr.Zero))
                {
                    Hintergrund.Notiz("Bildschirmschoner: Verknuepfung angelegt unter " + ziel);
                    return ziel;
                }
                File.Copy(exe, ziel, overwrite: true);
                Hintergrund.Notiz($"Bildschirmschoner: Verknuepfung ging nicht (Fehler "
                    + $"{Marshal.GetLastWin32Error()}), kopiert nach {ziel}");
                return ziel;
            }
            catch (Exception e)
            {
                Hintergrund.Notiz($"Bildschirmschoner: {ziel} ging nicht, {e.GetType().Name}: {e.Message}");
            }
        }
        return null;
    }

    private const string SchonerSchluessel = @"Control Panel\Desktop";

    /// <summary>Ist Tapete gerade als Bildschirmschoner eingetragen?</summary>
    internal static bool Eingetragen
    {
        get
        {
            try
            {
                using var k = Registry.CurrentUser.OpenSubKey(SchonerSchluessel);
                string wert = k?.GetValue("SCRNSAVE.EXE") as string ?? "";
                return wert.EndsWith("Tapete.scr", StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }
    }

    /// <summary>
    /// Traegt Tapete als Bildschirmschoner ein oder wieder aus. Liefert zurueck,
    /// was daraus geworden ist; false heisst, es hat nicht geklappt.
    ///
    /// Eine schon eingestellte Wartezeit bleibt stehen. Fehlt sie ganz, werden
    /// fuenf Minuten eingetragen, sonst ginge der Schoner nie an.
    /// </summary>
    internal static bool Eintragen(bool an)
    {
        try
        {
            using var k = Registry.CurrentUser.CreateSubKey(SchonerSchluessel, writable: true);
            if (k is null) return false;

            if (!an)
            {
                k.SetValue("SCRNSAVE.EXE", "");
                Hintergrund.Notiz("Bildschirmschoner: ausgetragen");
                return true;
            }

            string? scr = Anlegen();
            if (scr is null) return false;
            k.SetValue("SCRNSAVE.EXE", scr);
            k.SetValue("ScreenSaveActive", "1");

            // Ohne Wartezeit geht der Schoner nie an, und niemand kaeme auf den
            // Gedanken, dass daran liegt. Steht keine brauchbare da, werden fuenf
            // Minuten eingetragen; eine vorhandene bleibt unangetastet.
            if (WartezeitSekunden < 60) k.SetValue("ScreenSaveTimeOut", "300");
            Hintergrund.Notiz("Bildschirmschoner: eingetragen als " + scr);
            return true;
        }
        catch (Exception e)
        {
            Hintergrund.Notiz($"Bildschirmschoner eintragen gescheitert: {e.GetType().Name}: {e.Message}");
            return false;
        }
    }

    /// <summary>Die eingestellte Wartezeit in Sekunden, 0 wenn keine gesetzt ist.</summary>
    internal static int WartezeitSekunden
    {
        get
        {
            try
            {
                using var k = Registry.CurrentUser.OpenSubKey(SchonerSchluessel);
                return int.TryParse(k?.GetValue("ScreenSaveTimeOut") as string, out int s) ? s : 0;
            }
            catch { return 0; }
        }
    }

    // ---------- Anzeigen ----------

    /// <summary>
    /// Macht auf jedem Bildschirm ein schwarzes Vollbildfenster auf und laesst mpv
    /// darin spielen. Jede Taste, jeder Klick und jede Mausbewegung beendet.
    /// </summary>
    internal void Starten(string video, IReadOnlyList<string> schalter)
    {
        string? mpv = Hintergrund.MpvSuchen();
        if (mpv is null) { Hintergrund.Notiz("Schoner ABBRUCH: mpv nicht gefunden"); Beenden(); return; }

        var schirme = Native.Bildschirme();
        Hintergrund.Notiz($"Schoner: {schirme.Count} Bildschirm(e), {Path.GetFileName(video)}");
        if (schirme.Count == 0) { Beenden(); return; }

        foreach (var s in schirme)
        {
            var f = new Window
            {
                WindowStyle = WindowStyle.None,
                ResizeMode = ResizeMode.NoResize,
                ShowInTaskbar = false,
                Topmost = true,
                Background = System.Windows.Media.Brushes.Black,
                Cursor = Cursors.None,
                Title = "Tapete"
            };

            // Die Ereignisse an das Fenster haengen, bevor es zu sehen ist: Sonst
            // liefe der Schoner los und liesse sich nicht mehr wegklicken.
            f.KeyDown += (_, _) => Beenden();
            f.MouseDown += (_, _) => Beenden();
            f.MouseWheel += (_, _) => Beenden();
            f.MouseMove += MausBewegt;
            f.Deactivated += (_, _) => Beenden();

            f.Show();

            // Ueber Win32 setzen, nicht ueber Left/Top/Width/Height: WPF rechnet
            // dort in geraeteunabhaengigen Einheiten, und bei einer Skalierung von
            // 125 Prozent landet das Fenster sonst zu klein und verschoben.
            var quelle = new System.Windows.Interop.WindowInteropHelper(f);
            Native.SetWindowPos(quelle.Handle, IntPtr.Zero,
                s.Flaeche.Left, s.Flaeche.Top, s.Breite, s.Hoehe,
                Native.SWP_NOACTIVATE | Native.SWP_SHOWWINDOW);
            _fenster.Add(f);

            Spieler(mpv, quelle.Handle, video, s.Breite, s.Hoehe, schalter);
        }

        _fenster.FirstOrDefault()?.Activate();
    }

    /// <summary>
    /// Die erste Mausmeldung kommt oft sofort, weil der Zeiger beim Aufgehen des
    /// Fensters ohnehin irgendwo steht. Erst eine echte Bewegung zaehlt.
    /// </summary>
    private System.Windows.Point? _mausStart;

    private void MausBewegt(object sender, MouseEventArgs e)
    {
        var p = e.GetPosition((Window)sender);
        if (_mausStart is null) { _mausStart = p; return; }
        if (Math.Abs(p.X - _mausStart.Value.X) + Math.Abs(p.Y - _mausStart.Value.Y) > 8) Beenden();
    }

    private void Spieler(string mpv, IntPtr ziel, string video, int breite, int hoehe,
        IReadOnlyList<string> schalter)
    {
        var start = new ProcessStartInfo(mpv)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        };
        foreach (string a in new[]
        {
            "--panscan=1.0", "--loop-file", "--keep-open", "--media-controls=no",
            "--force-window=yes", "--no-window-dragging", "--cursor-autohide=no",
            "--input-default-bindings=no", "--no-border", "--input-cursor=no",
            "--no-osc", "--no-config", "--hwdec=d3d11va", "--profile=fast",
            // Der Schoner soll den Bildschirm gerade nicht schonen lassen, sonst
            // schaltet Windows mitten im Video ab.
            "--stop-screensaver=no",
            "--wid=" + ziel.ToInt64(),
        }) start.ArgumentList.Add(a);
        foreach (string a in schalter) start.ArgumentList.Add(a);
        start.ArgumentList.Add(video);

        try
        {
            var p = Process.Start(start);
            if (p is not null) { _spieler.Add(p); Hintergrund.Notiz($"Schoner: mpv PID {p.Id}, {breite}x{hoehe}"); }
        }
        catch (Exception e)
        {
            Hintergrund.Notiz($"Schoner: mpv ging nicht, {e.GetType().Name}: {e.Message}");
        }
    }

    /// <summary>
    /// Raeumt alles weg und beendet das Programm. Muss auch dann durchlaufen, wenn
    /// unterwegs etwas schiefgeht - ein Bildschirmschoner, der sich nicht schliessen
    /// laesst, sperrt den Rechner.
    /// </summary>
    private void Beenden()
    {
        if (_beendet) return;
        _beendet = true;
        Hintergrund.Notiz("Schoner: beendet");

        foreach (var p in _spieler)
        {
            try { if (!p.HasExited) p.Kill(entireProcessTree: true); } catch { }
            try { p.Dispose(); } catch { }
        }
        foreach (var f in _fenster) { try { f.Close(); } catch { } }
        Application.Current.Shutdown();
    }
}
