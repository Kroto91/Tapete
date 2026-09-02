using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;

namespace Tapete;

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
