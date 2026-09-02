using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text;
using Microsoft.Win32;

namespace Tapete;

// aus Native.cs

/// <summary>
/// Alle Windows-Aufrufe an einer Stelle. Der interessante Teil ist <see cref="FindWorkerW"/>:
/// Windows versteckt hinter den Desktop-Symbolen ein Fenster der Klasse "WorkerW".
/// Haengt man dort ein eigenes Fenster ein, laeuft es als Hintergrund.
/// </summary>
internal static class Native
{
    internal delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern IntPtr FindWindowEx(IntPtr hwndParent, IntPtr hwndChildAfter, string? lpszClass, string? lpszWindow);

    [DllImport("user32.dll")]
    internal static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam,
        uint fuFlags, uint uTimeout, out IntPtr lpdwResult);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern IntPtr SetParent(IntPtr hWndChild, IntPtr hWndNewParent);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern IntPtr GetParent(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    internal static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    internal static extern int GetSystemMetrics(int nIndex);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)]
    private static extern int GetWindowLong32(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
    private static extern int SetWindowLong32(IntPtr hWnd, int nIndex, int dwNewLong);

    internal static long GetStyle(IntPtr hWnd, int index) =>
        IntPtr.Size == 8 ? GetWindowLongPtr64(hWnd, index).ToInt64() : GetWindowLong32(hWnd, index);

    internal static void SetStyle(IntPtr hWnd, int index, long value)
    {
        if (IntPtr.Size == 8) SetWindowLongPtr64(hWnd, index, new IntPtr(value));
        else SetWindowLong32(hWnd, index, (int)value);
    }

    [DllImport("user32.dll")]
    internal static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    internal static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    internal static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    internal delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdc, ref RECT rect, IntPtr daten);

    [DllImport("user32.dll")]
    internal static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc proc, IntPtr daten);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetMonitorInfoW", SetLastError = true)]
    internal static extern bool GetMonitorInfoEx(IntPtr hMonitor, ref MONITORINFOEX lpmi);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern bool SystemParametersInfo(uint uiAction, uint uiParam, string? pvParam, uint fWinIni);

    [DllImport("gdi32.dll")]
    internal static extern bool DeleteObject(IntPtr hObject);

    [DllImport("gdi32.dll")]
    internal static extern IntPtr CreateRectRgn(int left, int top, int right, int bottom);

    [DllImport("gdi32.dll")]
    internal static extern int CombineRgn(IntPtr dst, IntPtr a, IntPtr b, int mode);

    [DllImport("user32.dll")]
    internal static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    internal static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);

    [DllImport("dwmapi.dll")]
    internal static extern int DwmGetWindowAttribute(IntPtr hWnd, int attr, out int value, int size);

    private const int RGN_DIFF = 4;
    private const int RGN_COPY = 5;
    private const int NULLREGION = 1;
    private const int DWMWA_CLOAKED = 14;

    [StructLayout(LayoutKind.Sequential)]
    internal struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct MONITORINFOEX
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string szDevice;
    }

    internal const int SM_XVIRTUALSCREEN = 76;
    internal const int SM_YVIRTUALSCREEN = 77;
    internal const int SM_CXVIRTUALSCREEN = 78;
    internal const int SM_CYVIRTUALSCREEN = 79;

    internal const uint SWP_NOZORDER = 0x0004;
    internal const uint SWP_NOACTIVATE = 0x0010;
    internal const uint SWP_SHOWWINDOW = 0x0040;
    internal const uint SWP_FRAMECHANGED = 0x0020;

    internal static readonly IntPtr HWND_BOTTOM = new(1);

    internal const int SW_HIDE = 0;
    internal const int SW_SHOWNA = 8;

    internal const int GWL_STYLE = -16;
    internal const int GWL_EXSTYLE = -20;

    internal const long WS_CHILD = 0x40000000L;
    internal const long WS_POPUP = 0x80000000L;
    internal const long WS_VISIBLE = 0x10000000L;
    internal const long WS_CLIPSIBLINGS = 0x04000000L;
    internal const long WS_CLIPCHILDREN = 0x02000000L;
    internal const long WS_CAPTION = 0x00C00000L;
    internal const long WS_THICKFRAME = 0x00040000L;
    internal const long WS_SYSMENU = 0x00080000L;
    internal const long WS_MINIMIZEBOX = 0x00020000L;
    internal const long WS_MAXIMIZEBOX = 0x00010000L;

    internal const long WS_EX_TOOLWINDOW = 0x00000080L;
    internal const long WS_EX_NOACTIVATE = 0x08000000L;
    internal const long WS_EX_APPWINDOW = 0x00040000L;
    internal const long WS_EX_TRANSPARENT = 0x00000020L;

    internal const uint SPI_SETDESKWALLPAPER = 0x0014;
    internal const uint SPIF_UPDATEINIFILE = 0x01;
    internal const uint SPIF_SENDCHANGE = 0x02;

    /// <summary>Das Desktop-Fenster selbst. Ab Build 26200 das einzige brauchbare Elternfenster.</summary>
    internal static IntPtr FindProgman() => FindWindow("Progman", null);

    /// <summary>Die Symbol-Ebene des Desktops (SHELLDLL_DefView), oder 0.</summary>
    internal static IntPtr FindDefView()
    {
        IntPtr progman = FindWindow("Progman", null);

        // Windows 11: DefView haengt direkt unter Progman.
        if (progman != IntPtr.Zero)
        {
            IntPtr d = FindWindowEx(progman, IntPtr.Zero, "SHELLDLL_DefView", null);
            if (d != IntPtr.Zero) return d;
        }

        // Windows 8 bis 10: DefView wurde in einen eigenen WorkerW ausgelagert.
        IntPtr gefunden = IntPtr.Zero;
        EnumWindows((hwnd, _) =>
        {
            IntPtr d = FindWindowEx(hwnd, IntPtr.Zero, "SHELLDLL_DefView", null);
            if (d != IntPtr.Zero) { gefunden = d; return false; }
            return true;
        }, IntPtr.Zero);
        return gefunden;
    }

    /// <summary>Die Liste mit den Desktop-Symbolen innerhalb der Symbol-Ebene.</summary>
    internal static IntPtr FindSymbolListe(IntPtr defView) =>
        defView == IntPtr.Zero ? IntPtr.Zero : FindWindowEx(defView, IntPtr.Zero, "SysListView32", null);

    /// <summary>
    /// Ab Windows 11 25H2 (Build 26200) zeichnet die alte Desktop-Ebene WorkerW keine
    /// fremden Fenster mehr. Dann haengt das Video direkt unter Progman, in der
    /// Z-Reihenfolge zwischen den Symbolen und dem Hintergrundbild.
    ///
    /// Gemessen am 31.08.2026 auf Build 26200 an Lively 2.2.1.0, das genau so vorgeht:
    ///   Progman
    ///     Z0  SHELLDLL_DefView   Symbole
    ///     Z1  mpv                Livelys Video
    ///     Z2  WorkerW            statisches Hintergrundbild
    /// </summary>
    internal static bool BrauchtProgman() => Environment.OSVersion.Version.Build >= 26200;

    /// <summary>
    /// Der klassische Weg (Windows 8 bis 11 24H2): Progman bitten, die WorkerW-Ebene
    /// abzuspalten, und diese zurueckgeben. Nur noch Rueckfallebene.
    /// </summary>
    internal static IntPtr FindWorkerW()
    {
        IntPtr progman = FindWindow("Progman", null);
        if (progman == IntPtr.Zero) return IntPtr.Zero;

        // 0x052C ist undokumentiert und weist Progman an, die WorkerW-Ebene abzuspalten.
        // Zwei Varianten, weil neuere Windows-Builds unterschiedlich reagieren.
        SendMessageTimeout(progman, 0x052C, IntPtr.Zero, IntPtr.Zero, 0x0000, 1000, out _);
        SendMessageTimeout(progman, 0x052C, new IntPtr(0x0000000D), new IntPtr(0x00000001), 0x0000, 1000, out _);

        // Weg A - Windows 11 (auch Build 26xxx): Die Symbole haengen direkt unter Progman,
        // und gleich dahinter liegt ein WorkerW als weiteres Kind von Progman. Genau dort hinein.
        if (FindWindowEx(progman, IntPtr.Zero, "SHELLDLL_DefView", null) != IntPtr.Zero)
        {
            IntPtr child = FindWindowEx(progman, IntPtr.Zero, "WorkerW", null);
            while (child != IntPtr.Zero)
            {
                if (FindWindowEx(child, IntPtr.Zero, "SHELLDLL_DefView", null) == IntPtr.Zero)
                    return child;
                child = FindWindowEx(progman, child, "WorkerW", null);
            }
        }

        // Weg B - klassisch (Windows 8 bis 10): Die Symbol-Ebene wurde in einen eigenen
        // WorkerW ausgelagert; der richtige ist das Geschwisterfenster direkt dahinter.
        IntPtr worker = IntPtr.Zero;
        EnumWindows((hwnd, _) =>
        {
            if (FindWindowEx(hwnd, IntPtr.Zero, "SHELLDLL_DefView", null) != IntPtr.Zero)
            {
                IntPtr candidate = FindWindowEx(IntPtr.Zero, hwnd, "WorkerW", null);
                if (candidate != IntPtr.Zero) worker = candidate;
            }
            return true;
        }, IntPtr.Zero);
        if (worker != IntPtr.Zero) return worker;

        // Weg C - WorkerW unter Progman, auch wenn die Symbole woanders sitzen.
        IntPtr c = FindWindowEx(progman, IntPtr.Zero, "WorkerW", null);
        if (c != IntPtr.Zero) return c;

        // Notnagel: direkt an Progman. Liegt dann vor dem Bild, aber hinter den Symbolen.
        return progman;
    }

    /// <summary>Ein angeschlossener Bildschirm mit seiner Lage im Gesamtbild.</summary>
    internal sealed record Bildschirm(string Name, RECT Flaeche, bool Haupt)
    {
        public int Breite => Flaeche.Right - Flaeche.Left;
        public int Hoehe => Flaeche.Bottom - Flaeche.Top;

        /// <summary>Was im Auswahlfeld steht.</summary>
        public override string ToString() =>
            (Haupt ? "Hauptbildschirm" : Name.Replace(@"\\.\", "")) + $" - {Breite} x {Hoehe}";
    }

    /// <summary>
    /// Alle angeschlossenen Bildschirme mit ihren echten Pixelmassen. Gebraucht, seit
    /// das Video wahlweise auf einen davon soll statt ueber alle gespannt.
    /// </summary>
    internal static List<Bildschirm> Bildschirme()
    {
        var liste = new List<Bildschirm>();
        int gemeldet = 0;
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr h, IntPtr hdc, ref RECT r, IntPtr d) =>
        {
            gemeldet++;
            var mi = new MONITORINFOEX { cbSize = Marshal.SizeOf<MONITORINFOEX>() };
            if (GetMonitorInfoEx(h, ref mi))
                liste.Add(new Bildschirm(mi.szDevice, mi.rcMonitor, (mi.dwFlags & 1) != 0));
            else
                // Ohne diese Zeile faellt ein Bildschirm spurlos aus der Liste, und
                // hinterher sieht es aus, als haette Windows ihn nie gemeldet. Am
                // 01.09.2026 gesucht, als bei einem Tester mit zwei Monitoren nur
                // einer in der Auswahl stand.
                Hintergrund.Notiz($"Bildschirm uebersprungen: GetMonitorInfoW scheiterte fuer "
                    + $"{r.Right - r.Left}x{r.Bottom - r.Top} bei {r.Left},{r.Top}, "
                    + $"Fehler {Marshal.GetLastWin32Error()}");
            return true;
        }, IntPtr.Zero);
        // Nur melden, wenn etwas fehlt. Die Aufzaehlung laeuft bei jedem Aufbau.
        if (gemeldet != liste.Count)
            Hintergrund.Notiz($"Bildschirme: {gemeldet} von Windows gemeldet, {liste.Count} brauchbar");
        return liste;
    }

    /// <summary>Groesse aller Bildschirme zusammen, in echten Pixeln.</summary>
    internal static (int X, int Y, int W, int H) VirtualScreen() => (
        GetSystemMetrics(SM_XVIRTUALSCREEN),
        GetSystemMetrics(SM_YVIRTUALSCREEN),
        GetSystemMetrics(SM_CXVIRTUALSCREEN),
        GetSystemMetrics(SM_CYVIRTUALSCREEN));

    /// <summary>
    /// Ist der Desktop gerade vollstaendig verdeckt? Windows rechnet selbst: Von der
    /// Gesamtflaeche wird jedes sichtbare Fenster abgezogen, bleibt nichts uebrig, sieht
    /// niemand das Video und es muss auch nicht dekodiert werden.
    ///
    /// Das Vordergrundfenster allein zu pruefen reicht nicht: Zwei Fenster
    /// nebeneinander verdecken den Desktop zusammen, jedes einzelne aber nicht.
    /// Am 31.08.2026 genau so gemessen, das Video lief unsichtbar weiter.
    ///
    /// ponytail: Rechtecke, keine echten Fensterformen. Abgerundete Ecken und
    /// Schatten zaehlen als deckend. Fuer die Frage "lohnt sich Dekodieren" genau genug.
    /// </summary>
    internal static bool DesktopVerdeckt()
    {
        var (x, y, w, h) = VirtualScreen();
        IntPtr rest = CreateRectRgn(x, y, x + w, y + h);
        IntPtr puffer = CreateRectRgn(0, 0, 0, 0);
        try
        {
            EnumWindows((hwnd, _) =>
            {
                if (!IsWindowVisible(hwnd) || IsIconic(hwnd)) return true;

                // Ausgeblendete UWP-Fenster melden volle Groesse, sind aber unsichtbar.
                // Ohne diese Pruefung bliebe das Video dauerhaft pausiert.
                if (DwmGetWindowAttribute(hwnd, DWMWA_CLOAKED, out int versteckt, sizeof(int)) == 0
                    && versteckt != 0) return true;

                // Nur der Desktop selbst zaehlt nicht als deckend. Die Taskleiste dagegen
                // schon: Sie deckt ihren Streifen wirklich ab. Nimmt man sie aus, bleibt
                // unten immer ein Rest stehen und die Pause loest nie aus.
                // Am 31.08.2026 genau so gemessen.
                var cls = new StringBuilder(64);
                GetClassName(hwnd, cls, cls.Capacity);
                if (cls.ToString() is "Progman" or "WorkerW" or "SHELLDLL_DefView") return true;

                // Das eigene Abspielfenster deckt sich nicht selbst zu.
                if ((GetStyle(hwnd, GWL_EXSTYLE) & WS_EX_TRANSPARENT) != 0) return true;

                if (!GetWindowRect(hwnd, out RECT r)) return true;
                if (r.Right - r.Left < 40 || r.Bottom - r.Top < 40) return true;

                IntPtr fenster = CreateRectRgn(r.Left, r.Top, r.Right, r.Bottom);
                CombineRgn(puffer, rest, fenster, RGN_DIFF);
                CombineRgn(rest, puffer, puffer, RGN_COPY);
                DeleteObject(fenster);
                return true;
            }, IntPtr.Zero);

            return CombineRgn(puffer, rest, rest, RGN_COPY) == NULLREGION;
        }
        finally
        {
            DeleteObject(rest);
            DeleteObject(puffer);
        }
    }

    // ---------- Spielt gerade jemand? ----------

    [DllImport("shell32.dll")]
    private static extern int SHQueryUserNotificationState(out int zustand);

    // Die Zustaende aus shellapi.h. Was Windows hier meldet, ist dieselbe Auskunft,
    // nach der es entscheidet, ob es Benachrichtigungen zurueckhaelt.
    private const int QUNS_BUSY = 2;                      // Vollbildanwendung
    private const int QUNS_RUNNING_D3D_FULL_SCREEN = 3;   // Spiel im echten Vollbild
    private const int QUNS_PRESENTATION_MODE = 4;         // Praesentation
    private const int QUNS_APP = 7;                       // Store-App im Vollbild

    /// <summary>
    /// Meldet, ob gerade etwas den Bildschirm fuer sich beansprucht: ein Spiel im
    /// Vollbild, eine Praesentation, eine Vollbildanwendung.
    ///
    /// Ein einziger Aufruf, keine Prozessliste, keine Liste bekannter Spiele. Was
    /// er nicht sicher erwischt, ist ein Spiel im randlosen Fenster - Windows
    /// meldet das je nach Spiel als Vollbild oder gar nicht. Deshalb bleibt der
    /// Knopf daneben stehen und wird davon nicht angefasst.
    ///
    /// Bei einem Fehler kommt false zurueck. Lieber laeuft das Video weiter, als
    /// dass der Hintergrund grundlos verschwindet.
    /// </summary>
    internal static bool VollbildAnwendungLaeuft()
    {
        try
        {
            if (SHQueryUserNotificationState(out int z) != 0) return false;
            return z is QUNS_BUSY or QUNS_RUNNING_D3D_FULL_SCREEN
                     or QUNS_PRESENTATION_MODE or QUNS_APP;
        }
        catch { return false; }
    }

    // ---------- Tastenkuerzel ----------

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    internal const uint MOD_ALT = 0x0001;
    internal const uint MOD_CONTROL = 0x0002;
    internal const uint MOD_NOREPEAT = 0x4000;   // Halten wiederholt nicht
    internal const int WM_HOTKEY = 0x0312;

    /// <summary>Zeichnet das normale Windows-Hintergrundbild neu.</summary>
    internal static void RefreshDesktopWallpaper()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop");
            string path = key?.GetValue("WallPaper") as string ?? string.Empty;
            SystemParametersInfo(SPI_SETDESKWALLPAPER, 0, path, SPIF_UPDATEINIFILE | SPIF_SENDCHANGE);
        }
        catch { /* nicht schlimm, der Desktop zeichnet sich ohnehin bald neu */ }
    }
}

// aus Aktualisierung.cs

/// <summary>Eine neuere Fassung, die auf GitHub bereitliegt.</summary>
internal sealed record Neuigkeit(Version Version, string Titel, string SetupUrl, string Seite);

/// <summary>
/// Was bei einer Suche herauskam. Beide Felder null heisst: alles aktuell.
///
/// Die Unterscheidung ist noetig, seit der Nutzer selbst nach Aktualisierungen
/// fragen kann. Ohne sie meldete der Knopf "Sie haben die neueste Fassung" auch
/// dann, wenn nur das Netz weg war.
/// </summary>
internal sealed record Pruefung(Neuigkeit? Neu, string? Fehler);

/// <summary>
/// Sucht neue Fassungen ueber die Releases von GitHub und startet das Setup.
///
/// Absichtlich ohne Bibliothek: HttpClient und System.Text.Json stecken beide in
/// .NET. Eine Update-Bibliothek waere fuer sechzig Zeilen ein schlechter Tausch.
/// </summary>
internal static class Aktualisierung
{
    /// <summary>
    /// Die einzige Stelle, an der das Repository steht. Solange der Besitzer leer
    /// ist, tut die Pruefung gar nichts und meldet auch nichts - besser als ein
    /// Platzhalter, der bei jedem Start ins Leere laeuft.
    /// </summary>
    internal const string Besitzer = "Kroto91";
    internal const string Ablage = "Tapete";

    internal static bool Eingerichtet => Besitzer.Length > 0;

    /// <summary>
    /// Wo das Setup zuletzt hin installiert hat, oder null. Den Eintrag legt
    /// Inno Setup selbst an; die Kennung stammt aus AppId in Tapete.iss.
    /// </summary>
    internal static string? InstallationsOrdner
    {
        get
        {
            try
            {
                using var k = Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Uninstall\"
                    + "{7C4B1E2A-9D33-4F16-A8C5-2E0B6D41F9A3}_is1");
                return k?.GetValue("InstallLocation") as string;
            }
            catch { return null; }
        }
    }

    /// <summary>
    /// Laeuft dieses Tapete aus der Installation?
    ///
    /// Wichtig fuer die Selbstaktualisierung: Das Setup installiert immer in den
    /// Ordner, den es sich gemerkt hat. Laeuft Tapete woanders - etwa direkt aus
    /// dem Bauordner, wie beim Entwickeln -, entstuende beim Aktualisieren eine
    /// zweite Kopie an anderer Stelle, waehrend die laufende auf dem alten Stand
    /// bliebe. Eine Autostart-Verknuepfung zeigte dann weiter auf die alte.
    ///
    /// Deshalb aktualisiert sich nur die installierte Kopie von selbst. Der Knopf
    /// im Fenster bleibt fuer alle, fragt aber vorher nach.
    /// </summary>
    internal static bool AusInstallation
    {
        get
        {
            string? ordner = InstallationsOrdner;
            string? eigener = Path.GetDirectoryName(Environment.ProcessPath ?? "");
            if (string.IsNullOrEmpty(ordner) || string.IsNullOrEmpty(eigener)) return false;
            try
            {
                return string.Equals(
                    Path.TrimEndingDirectorySeparator(Path.GetFullPath(ordner)),
                    Path.TrimEndingDirectorySeparator(Path.GetFullPath(eigener)),
                    StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }
    }

    /// <summary>
    /// Eigene Fassung auf drei Stellen gebracht. Ohne das vergleicht sich das
    /// 1.0.0.0 der Assembly mit dem 1.0.0 aus dem Etikett als groesser.
    /// </summary>
    internal static Version Eigene => Dreistellig(
        typeof(Aktualisierung).Assembly.GetName().Version ?? new Version(0, 0, 0));

    private static Version Dreistellig(Version v) =>
        new(v.Major, v.Minor, Math.Max(v.Build, 0));

    /// <summary>Fuer die kurze Abfrage. Zwanzig Sekunden reichen dafuer reichlich.</summary>
    private static readonly HttpClient Netz = Bauen(TimeSpan.FromSeconds(20));

    /// <summary>
    /// Fuer den Download des Setups. Braucht ein eigenes Zeitlimit, weil
    /// HttpClient.Timeout fuer den ganzen Vorgang gilt, nicht je Verbindung.
    ///
    /// Am 01.09.2026 beim Probelauf aufgefallen: Mit den zwanzig Sekunden der
    /// Abfrage brach der Download der 95 MB jedes Mal ab. Damit hat sich Tapete
    /// nie aktualisieren koennen, auch nicht ueber den Knopf im Fenster.
    /// </summary>
    private static readonly HttpClient Laden = Bauen(TimeSpan.FromMinutes(30));

    private static HttpClient Bauen(TimeSpan grenze)
    {
        var c = new HttpClient { Timeout = grenze };
        // GitHub weist Anfragen ohne Kennung ab.
        c.DefaultRequestHeaders.UserAgent.ParseAdd("Tapete");
        c.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return c;
    }

    /// <summary>Fragt die neueste Veroeffentlichung ab. Null heisst: nichts Neues.</summary>
    internal static async Task<Pruefung> Suchen()
    {
        if (!Eingerichtet) return new Pruefung(null, "Es ist kein Repository eingetragen.");
        try
        {
            string url = $"https://api.github.com/repos/{Besitzer}/{Ablage}/releases/latest";
            using var doc = JsonDocument.Parse(await Netz.GetStringAsync(url));
            var w = doc.RootElement;

            string etikett = w.GetProperty("tag_name").GetString() ?? "";
            if (!Version.TryParse(etikett.TrimStart('v', 'V'), out var gefunden))
                return new Pruefung(null, $"Die Fassungsnummer \"{etikett}\" ist nicht zu lesen.");
            gefunden = Dreistellig(gefunden);
            if (gefunden <= Eigene)
            {
                Hintergrund.Notiz($"Aktualisierung: {gefunden} ist nicht neuer als {Eigene}");
                return new Pruefung(null, null);
            }

            // Die kleine Fassung nehmen. Die grosse traegt "Videos" im Namen und
            // waere ein 668-MB-Download fuer Dateien, die schon da sind.
            string? setup = null;
            foreach (var a in w.GetProperty("assets").EnumerateArray())
            {
                string name = a.GetProperty("name").GetString() ?? "";
                if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                    && !name.Contains("Videos", StringComparison.OrdinalIgnoreCase))
                {
                    setup = a.GetProperty("browser_download_url").GetString();
                    break;
                }
            }
            if (setup is null)
            {
                Hintergrund.Notiz($"Aktualisierung: {etikett} hat kein Setup ohne Videos");
                return new Pruefung(null, $"Die Veroeffentlichung {etikett} hat kein Setup.");
            }

            Hintergrund.Notiz($"Aktualisierung: {gefunden} gefunden, eigene {Eigene}");
            return new Pruefung(new Neuigkeit(gefunden,
                w.GetProperty("name").GetString() ?? etikett,
                setup,
                w.GetProperty("html_url").GetString() ?? ""), null);
        }
        catch (Exception e)
        {
            Hintergrund.Notiz($"Aktualisierung: {e.GetType().Name}: {e.Message}");
            return new Pruefung(null, e.Message);
        }
    }

    /// <summary>
    /// Laedt das Setup und startet es. Null heisst geglueckt, sonst der Grund.
    ///
    /// still=true installiert ohne Assistent und ohne Rueckfragen. Das geht nur,
    /// weil die Installation unter %LOCALAPPDATA% liegt und deshalb keine
    /// Administratorrechte braucht; sonst kaeme trotzdem eine Abfrage.
    /// </summary>
    internal static async Task<string?> HolenUndStarten(Neuigkeit n, bool still = false)
    {
        // Nur von GitHub, und nur ueber HTTPS. Die Adresse kommt aus einer Antwort
        // aus dem Netz; ohne diese Pruefung koennte sie auf einen beliebigen Server
        // zeigen und Tapete wuerde das Herunterladen und Ausfuehren uebernehmen.
        if (!Uri.TryCreate(n.SetupUrl, UriKind.Absolute, out var u)
            || u.Scheme != Uri.UriSchemeHttps
            || !(u.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase)
                 || u.Host.EndsWith(".github.com", StringComparison.OrdinalIgnoreCase)
                 || u.Host.EndsWith(".githubusercontent.com", StringComparison.OrdinalIgnoreCase)))
        {
            Hintergrund.Notiz("Aktualisierung: fremde Adresse abgelehnt, " + n.SetupUrl);
            return "Der Download zeigt nicht auf GitHub.";
        }

        try
        {
            string ziel = Path.Combine(Path.GetTempPath(), $"Tapete-Setup-{n.Version}.exe");

            // Durchreichen statt erst in den Arbeitsspeicher. Die Datei ist rund
            // 95 MB gross; sie am Stueck zu halten waere fuer nichts.
            var uhr = Stopwatch.StartNew();
            await using (var quelle = await Laden.GetStreamAsync(u))
            await using (var datei = File.Create(ziel))
                await quelle.CopyToAsync(datei);

            long groesse = new FileInfo(ziel).Length;
            Hintergrund.Notiz($"Aktualisierung: {groesse / 1048576} MB geladen in " +
                              $"{uhr.ElapsedMilliseconds / 1000} s nach {ziel}");
            if (groesse < 1_000_000)
            {
                Hintergrund.Notiz("Aktualisierung: Datei zu klein, wird nicht gestartet");
                return "Der Download ist unvollstaendig.";
            }

            var start = new ProcessStartInfo(ziel) { UseShellExecute = true };
            if (still)
            {
                // FORCECLOSEAPPLICATIONS ist noetig, nicht schmueckend. Der
                // Windows-Neustartmanager schickt laufenden Programmen ein
                // WM_CLOSE; Tapete faengt das ab und versteckt nur sein Fenster,
                // damit "Fenster zu" nicht "Programm zu" heisst. Der Manager
                // meldete deshalb "Some applications could not be shut down",
                // und mit unterdrueckten Dialogen bricht Inno dann ab.
                // Am 01.09.2026 im Inno-Protokoll nachgelesen, nicht geraten.
                //
                // NORESTARTAPPLICATIONS, weil das Setup Tapete am Ende ohnehin
                // selbst wieder startet, siehe [Run] in Tapete.iss. Sonst kaeme
                // es zweimal hoch.
                start.Arguments = "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART "
                                + "/FORCECLOSEAPPLICATIONS /NORESTARTAPPLICATIONS";
            }
            Process.Start(start);
            Hintergrund.Notiz("Aktualisierung: Installer gestartet" + (still ? " (still)" : ""));
            return null;
        }
        catch (Exception e)
        {
            Hintergrund.Notiz($"Aktualisierung, Download: {e.GetType().Name}: {e.Message}");
            return e.Message;
        }
    }
}
