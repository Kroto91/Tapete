using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;

namespace Tapete;

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
    /// <summary>Ein Eintrag je belegtem Bildschirm. Bei "*" mehrere, sonst einer.</summary>
    private readonly List<(Process Prozess, string Pipe)> _spieler = new();
    private bool _pausiert;
    private bool _entsorgt;

    public string VideoPfad { get; }
    public bool BeiVollbildPausieren { get; set; } = true;

    /// <summary>Geraetename des Zielbildschirms, "*" fuer alle, leer fuer den Hauptbildschirm.</summary>
    public string? Bildschirm { get; init; }

    /// <summary>False, wenn der Aufbau schiefging. Dann laeuft kein Video.</summary>
    public bool Laeuft { get; private set; }

    /// <summary>
    /// Die Fehlermeldung kommt als Parameter herein, nicht als Ereignis. Ein Ereignis
    /// liesse sich erst nach dem Konstruktor abonnieren, und genau dort meldet
    /// Aufbauen() jeden Startfehler - er waere ins Leere gelaufen.
    /// </summary>
    public Hintergrund(string videoPfad, Action<string>? fehler = null)
    {
        VideoPfad = videoPfad;
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
    internal static void Notiz(string text)
    {
        try
        {
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

        foreach (var (px, py, breite, hoehe) in flaechen)
            EinenStarten(mpv, ziel, px, py, breite, hoehe);

        Laeuft = _spieler.Count > 0;
        if (!Laeuft) _fehler?.Invoke("Kein Bildschirm liess sich belegen.");
    }

    /// <summary>
    /// Startet ein mpv fuer genau eine Flaeche und haengt es in die Desktop-Ebene.
    /// Je Bildschirm ein eigener Prozess mit eigener Pipe: mpv kann ein Video nicht
    /// auf zwei Fenster gleichzeitig legen, und ein einziges gespanntes Fenster war
    /// genau das, was der Nutzer am 01.09.2026 nicht wollte.
    /// </summary>
    private void EinenStarten(string mpv, IntPtr ziel, int px, int py, int breite, int hoehe)
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
            "--volume=0", "--loop-file", "--keep-open", "--media-controls=no",
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
            "--input-ipc-server=" + pipe, "--geometry=-9999:0", VideoPfad,
        }) start.ArgumentList.Add(a);

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
              $"{breite}x{hoehe} bei {px},{py}");
    }

    /// <summary>
    /// Wohin das Abspielfenster gehoert: auf einen Bildschirm oder ueber alle.
    /// Leerer Name heisst Hauptbildschirm, "*" heisst alle zusammen. Ist der
    /// gemerkte Bildschirm abgesteckt, faellt es auf den Hauptbildschirm zurueck.
    /// </summary>
    private List<(int X, int Y, int Breite, int Hoehe)> ZielFlaechen(int vx, int vy, int vBreite, int vHoehe)
    {
        if (Bildschirm == "*")
        {
            var schirme = Native.Bildschirme();
            if (schirme.Count == 0)
            {
                Notiz("Zielflaeche: kein Bildschirm gefunden, nehme die Gesamtflaeche");
                return [(0, 0, vBreite, vHoehe)];
            }
            // Je Bildschirm eine eigene Flaeche, nicht ein Fenster ueber alle. Ein
            // gespanntes Fenster zeigt das Video einmal quer ueber beide Schirme;
            // gewollt ist es auf jedem Schirm einzeln und vollstaendig.
            foreach (var s in schirme)
                Notiz($"Zielflaeche: {s.Name} {s.Breite}x{s.Hoehe} bei {s.Flaeche.Left},{s.Flaeche.Top}");
            return schirme
                .Select(s => (s.Flaeche.Left - vx, s.Flaeche.Top - vy, s.Breite, s.Hoehe))
                .ToList();
        }

        var alle = Native.Bildschirme();
        var b = alle.FirstOrDefault(s => s.Name == Bildschirm) ?? alle.FirstOrDefault(s => s.Haupt);
        if (b is null)
        {
            Notiz("Zielflaeche: kein Bildschirm gefunden, nehme die Gesamtflaeche");
            return [(0, 0, vBreite, vHoehe)];
        }
        Notiz($"Zielflaeche: {b.Name} {b.Breite}x{b.Hoehe} bei {b.Flaeche.Left},{b.Flaeche.Top}");
        return [(b.Flaeche.Left - vx, b.Flaeche.Top - vy, b.Breite, b.Hoehe)];
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

        bool soll = BeiVollbildPausieren && Native.DesktopVerdeckt();
        if (soll == _pausiert) return;

        _pausiert = soll;
        AnMpv("{\"command\":[\"set_property\",\"pause\"," + (soll ? "true" : "false") + "]}");
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
