using System.Runtime.InteropServices;
using System.Text;

namespace Tapete;

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

    // ---------- Virtuelle Desktops ----------

    /// <summary>
    /// Die einzige von Microsoft dokumentierte Schnittstelle zu den virtuellen
    /// Desktops. Sie kann zwei Dinge: die Kennung des Desktops nennen, auf dem ein
    /// Fenster liegt, und sagen, ob ein Fenster auf dem gerade offenen liegt.
    ///
    /// "Welcher Desktop ist offen" gibt es nicht direkt. Die undokumentierte
    /// Schwesterschnittstelle koennte das, wird aber zwischen Windows-Fassungen
    /// umgebaut; ein Programm, das sie benutzt, geht nach jedem grossen Update
    /// kaputt. Deshalb der Umweg ueber das Vordergrundfenster, siehe unten.
    /// </summary>
    [ComImport, Guid("a5cd92ff-29be-454c-8d04-d82879fb3f1b"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IVirtualDesktopManager
    {
        [PreserveSig] int IsWindowOnCurrentVirtualDesktop(IntPtr fenster, out bool aufAktuellem);
        [PreserveSig] int GetWindowDesktopId(IntPtr fenster, out Guid kennung);
        [PreserveSig] int MoveWindowToDesktop(IntPtr fenster, ref Guid kennung);
    }

    [ComImport, Guid("aa509086-5ca9-4c25-8f95-589d3c07b48a")]
    private class VirtualDesktopManagerKlasse { }

    private static IVirtualDesktopManager? _desktopVerwalter;

    /// <summary>
    /// Die Kennung des gerade offenen virtuellen Desktops, oder null.
    ///
    /// Gefragt wird das Fenster im Vordergrund, denn das liegt immer auf dem
    /// offenen Desktop. Fenster, die auf allen Desktops zugleich liegen - der
    /// Desktop selbst zum Beispiel, oder ein angeheftetes Fenster - antworten mit
    /// 0x8002802B; dann laesst sich nichts sagen und der Aufrufer behaelt, was er
    /// hat. Am 03.09.2026 auf dem Rechner des Nutzers geprueft: Das
    /// Vordergrundfenster lieferte eine Kennung, das Shell-Fenster den Fehler.
    /// </summary>
    internal static Guid? AktuellerDesktop()
    {
        try
        {
            _desktopVerwalter ??= (IVirtualDesktopManager)new VirtualDesktopManagerKlasse();
            IntPtr fenster = GetForegroundWindow();
            if (fenster == IntPtr.Zero) return null;
            return _desktopVerwalter.GetWindowDesktopId(fenster, out Guid kennung) == 0
                   && kennung != Guid.Empty
                ? kennung
                : null;
        }
        catch
        {
            // Aeltere Windows-Fassungen ohne virtuelle Desktops, oder COM sperrt sich.
            return null;
        }
    }

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
