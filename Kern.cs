using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using System.Windows;
using Microsoft.Win32;

namespace Tapete;

// aus Settings.cs

/// <summary>Merkt sich das zuletzt gewaehlte Video und die zwei Schalter.</summary>
public sealed class Settings
{
    public string? LetztesVideo { get; set; }
    public bool BeiVollbildPausieren { get; set; } = true;

    /// <summary>
    /// Spielmodus. Bleibt ueber einen Neustart hinweg stehen: wer ihn abends
    /// einschaltet und den Rechner neu startet, will morgens nicht ueberrascht
    /// werden, dass er sich von selbst geloest hat.
    /// </summary>
    public bool Spielmodus { get; set; }

    /// <summary>
    /// Stand der Spielmodus nur an, weil die Automatik ihn eingeschaltet hat?
    /// Dann wird er beim naechsten Start zurueckgenommen.
    ///
    /// Grund: Die Automatik schaltet bei jeder Vollbildanwendung ein, auch beim
    /// eigenen Bildschirmschoner - und der laeuft genau dann, wenn niemand am
    /// Rechner sitzt. Geht die Maschine in dem Moment hart aus, bliebe der
    /// Spielmodus stehen und Tapete zeigte am naechsten Tag gar nichts mehr.
    /// Am 02.09.2026 im Protokoll gesehen, zwei Sekunden nach dem Schoner:
    /// "Vollbildanwendung beendet" und "Spielmodus: aus".
    /// </summary>
    public bool SpielmodusAutomatisch { get; set; }

    /// <summary>
    /// Halbe Bildrate. Wirkt nicht beim Abspielen, sondern beim einmaligen Umrechnen:
    /// mpv wuerde sonst trotzdem jedes Bild dekodieren und erst danach welche weglassen.
    /// Ab Werk aus, weil man es im Gegensatz zur Aufloesung sieht.
    /// </summary>
    public bool BildrateHalbieren { get; set; }

    /// <summary>
    /// Welche Fassung zuletzt von selbst eingespielt werden sollte. Verhindert,
    /// dass eine Aktualisierung, die nicht durchgeht, bei jedem Start erneut
    /// 95 MB laedt. Schlaegt sie fehl, bleibt der Knopf im Fenster.
    /// </summary>
    public string? AktualisierungVersucht { get; set; }

    // ---------- Karussell ----------

    /// <summary>Wechselt der Hintergrund nach einer Weile von selbst?</summary>
    public bool KarussellAn { get; set; }

    /// <summary>
    /// Wie lange ein Video stehen bleibt. Untergrenze eine Minute: Ein Wechsel
    /// beendet mpv und startet es neu, das kostet rund eine halbe Sekunde mit
    /// schwarzem Bild. Alle paar Minuten faellt das nicht auf, alle zehn
    /// Sekunden schon.
    /// </summary>
    public int KarussellMinuten { get; set; } = 15;

    /// <summary>Gemischt statt der Reihe nach.</summary>
    public bool KarussellZufaellig { get; set; } = true;

    /// <summary>
    /// Welche Videos mitlaufen, als Dateinamen ohne Pfad. Ohne Pfad, damit die
    /// Auswahl einen Umzug des Videoordners uebersteht.
    /// </summary>
    public List<string> KarussellVideos { get; set; } = [];

    /// <summary>
    /// Die Standzeit in lesbarem Deutsch. "alle 1 Minuten" liest sich falsch.
    ///
    /// JsonIgnore, weil das ein ausgerechneter Wert ist. Ohne die Kennzeichnung
    /// schreibt der Serialisierer jeden oeffentlichen Lesezugriff mit in die Datei.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string KarussellDauerText => KarussellMinuten switch
    {
        1 => "jede Minute",
        60 => "jede Stunde",
        120 => "alle zwei Stunden",
        240 => "alle vier Stunden",
        _ => $"alle {KarussellMinuten} Minuten"
    };

    /// <summary>
    /// Die angekreuzten Videos als vollstaendige Pfade, nur vorhandene. Ebenfalls
    /// ausgerechnet und deshalb nicht in der Datei; sonst stuende jeder Pfad zweimal
    /// darin, einmal als Name und einmal ganz.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public IEnumerable<string> KarussellPfade =>
        KarussellVideos.Select(n => Path.Combine(VideoOrdner, n)).Where(File.Exists);

    /// <summary>
    /// Auf welchen Bildschirm das Video gehoert. Geraetename wie "\.\DISPLAY1",
    /// oder "*" fuer alle zusammen. Leer heisst Hauptbildschirm.
    ///
    /// Vorher lief es immer ueber alle Bildschirme, weil das Fenster auf die
    /// Gesamtflaeche gesetzt wurde. Auf zwei Monitoren wurde das Video dadurch
    /// mittig zerschnitten. Gemeldet am 31.08.2026.
    /// </summary>
    public string? Bildschirm { get; set; }

    /// <summary>
    /// Geraetename des Bildschirms auf sein eigenes Video. Wirkt nur bei
    /// <see cref="Bildschirm"/> gleich "*", sonst laeuft ohnehin ueberall dasselbe.
    /// Wo nichts eingetragen ist, gilt das zuletzt angeklickte Video.
    /// </summary>
    public Dictionary<string, string> VideoJeBildschirm { get; set; } = [];

    // ---------- Bild und Ton ----------
    //
    // Alle vier gehen als Schalter an mpv und wirken erst beim naechsten Aufbau.
    // Die Wertebereiche stammen aus `mpv.exe --list-options`, abgefragt am
    // 01.09.2026 bei der ausgelieferten Fassung: Helligkeit und Saettigung -100
    // bis 100 mit 0 als Normalwert, Tempo 0,01 bis 100 mit 1, Lautstaerke in
    // Prozent. Tapete bietet davon nur grobe Stufen an, feiner braucht es auf
    // einem Hintergrundvideo niemand.

    /// <summary>Helligkeit, -100 bis 100. 0 heisst unveraendert.</summary>
    public int Helligkeit { get; set; }

    /// <summary>Farbsaettigung, -100 bis 100. 0 heisst unveraendert.</summary>
    public int Saettigung { get; set; }

    /// <summary>Wiedergabetempo in Prozent. 100 ist die Normalgeschwindigkeit.</summary>
    public int TempoProzent { get; set; } = 100;

    /// <summary>
    /// Lautstaerke in Prozent, 0 heisst stumm. Ab Werk stumm: Bis Fassung 1.3.7
    /// lief jedes Video zwangsweise ohne Ton, und ein Hintergrund, der nach dem
    /// Aktualisieren ploetzlich Krach macht, waere eine unangenehme Ueberraschung.
    /// </summary>
    public int Lautstaerke { get; set; }

    /// <summary>
    /// HDR-Ausgabe versuchen. Ob Windows das fuer ein Fenster annimmt, das als
    /// Kind unter dem Desktop haengt, ist offen; deshalb schreibt mpv bei
    /// eingeschaltetem Schalter zusaetzlich ein eigenes Protokoll mit.
    /// </summary>
    public bool Hdr { get; set; }

    /// <summary>
    /// Video anhalten, sobald der Rechner auf Akku laeuft. Ohne Akku wirkungslos.
    /// Ab Werk an, weil ein Hintergrundvideo im Akkubetrieb niemandem nuetzt.
    /// </summary>
    public bool BeiAkkuPausieren { get; set; } = true;

    /// <summary>
    /// Welches Erscheinungsbild geladen wird: der Dateiname unter Themen/ ohne
    /// Endung. Faellt der Wert aus, nimmt das Programm Hud.
    /// </summary>
    public string Thema { get; set; } = "Cyber2077";

    /// <summary>
    /// Bewegte Effekte am Fensterrand. Ab Werk an; wer sie stoerend findet oder
    /// auf Bewegung empfindlich reagiert, schaltet sie hier aus.
    /// </summary>
    public bool Effekte { get; set; } = true;

    // ---------- Profile ----------

    /// <summary>
    /// Benannte Momentaufnahmen: welcher Modus, welches gemeinsame Video und
    /// welche Zuweisungen je Bildschirm. Gedacht zum Umschalten zwischen
    /// Zusammenstellungen, ohne jedes Mal neu zuzuweisen.
    /// </summary>
    public Dictionary<string, Profil> Profile { get; set; } = [];

    public sealed class Profil
    {
        public string? Bildschirm { get; set; }
        public string? Video { get; set; }
        public Dictionary<string, string> JeBildschirm { get; set; } = [];
    }

    private static string Ordner =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Tapete");

    private static string Datei => Path.Combine(Ordner, "einstellungen.json");

    /// <summary>Ordner, aus dem die Kacheln kommen: Videos\Tapeten</summary>
    public static string VideoOrdner
    {
        get
        {
            string p = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "Tapeten");
            Directory.CreateDirectory(p);
            return p;
        }
    }

    public static Settings Laden()
    {
        // Sagt ins Protokoll, warum es schiefging. Am 31.08.2026 lieferte Laden()
        // beim Anmelden fuenfmal in Folge eine leere Einstellung, obwohl die Datei
        // unveraendert auf der Platte lag und Notiz() in denselben Ordner schreiben
        // konnte. Ohne Begruendung war das nicht weiter einzugrenzen.
        string pfad = Datei;
        try
        {
            if (!File.Exists(pfad))
            {
                Hintergrund.Notiz("Settings.Laden: Datei nicht gefunden unter " + pfad);
                return new Settings();
            }
            string roh = File.ReadAllText(pfad);
            var s = JsonSerializer.Deserialize<Settings>(roh);
            if (s is null)
            {
                Hintergrund.Notiz($"Settings.Laden: Deserialize lieferte null, {roh.Length} Zeichen gelesen");
                return new Settings();
            }
            return s;
        }
        catch (Exception e)
        {
            Hintergrund.Notiz($"Settings.Laden: {e.GetType().Name}: {e.Message}");
            return new Settings();
        }
    }

    public void Speichern()
    {
        try
        {
            Directory.CreateDirectory(Ordner);
            File.WriteAllText(Datei, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        // Schlug das Speichern fehl, verschwand die Einstellung bisher lautlos: kein
        // Hinweis im Fenster, keine Zeile im Protokoll. Gescheitert wird weiter still,
        // aber nicht mehr unsichtbar.
        catch (Exception e) { Hintergrund.Notiz($"Einstellungen speichern gescheitert: {e.GetType().Name}: {e.Message}"); }
    }

    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunName = "Tapete";

    /// <summary>
    /// Der Autostart laeuft ueber eine Verknuepfung im Autostart-Ordner, nicht mehr
    /// ueber den Run-Eintrag der Registry.
    ///
    /// Grund: Am 31.08.2026 stand der Run-Eintrag technisch einwandfrei da - richtiger
    /// Werttyp, vorhandener Pfad, gleiches Format wie der Eintrag eines Programms, das
    /// startete - und wurde beim Anmelden trotzdem nicht ausgefuehrt. Die Ursache liess
    /// sich nicht klaeren. Der Autostart-Ordner ist dagegen sichtbar und im Explorer
    /// nachpruefbar.
    /// </summary>
    private static string Verknuepfung => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.Startup), "Tapete.lnk");

    public static bool Autostart
    {
        get
        {
            try { return File.Exists(Verknuepfung); }
            catch { return false; }
        }
        set
        {
            try
            {
                // Einen etwaigen alten Registry-Eintrag immer raeumen, egal in welche
                // Richtung geschaltet wird. Sonst startet Tapete ueber zwei Wege.
                using (var k = Registry.CurrentUser.OpenSubKey(RunKey, writable: true))
                    k?.DeleteValue(RunName, throwOnMissingValue: false);

                if (!value)
                {
                    if (File.Exists(Verknuepfung)) File.Delete(Verknuepfung);
                    return;
                }

                string exe = Environment.ProcessPath ?? string.Empty;
                if (exe.Length == 0) return;

                // Verknuepfung ueber den Windows-Skripthost, spaet gebunden. Spart eine
                // COM-Verweisdatei fuer sechs Zeilen.
                Type? typ = Type.GetTypeFromProgID("WScript.Shell");
                if (typ is null) return;
                dynamic shell = Activator.CreateInstance(typ)!;
                var lnk = shell.CreateShortcut(Verknuepfung);
                lnk.TargetPath = exe;
                lnk.Arguments = "--versteckt";
                lnk.WorkingDirectory = Path.GetDirectoryName(exe);
                lnk.Description = "Animierter Desktop-Hintergrund, startet ohne Fenster";
                lnk.Save();
            }
            catch { }
        }
    }
}

// aus VideoItem.cs

/// <summary>Eine Kachel in der Uebersicht.</summary>
public sealed class VideoItem : INotifyPropertyChanged
{
    public string Pfad { get; }
    public string Name { get; }

    public VideoItem(string pfad)
    {
        Pfad = pfad;
        // Mit Endung: Sonst sind gleichnamige Dateien in unterschiedlichen Formaten
        // in der Kachelliste nicht zu unterscheiden.
        Name = Path.GetFileName(pfad);
    }

    private BitmapSource? _bild;
    public BitmapSource? Bild
    {
        get => _bild;
        set { _bild = value; Melde(nameof(Bild)); Melde(nameof(PlatzhalterSichtbar)); }
    }

    /// <summary>Laeuft dieses Video im Karussell mit? Der Haken auf der Kachel.</summary>
    private bool _imKarussell;
    public bool ImKarussell
    {
        get => _imKarussell;
        set { _imKarussell = value; Melde(nameof(ImKarussell)); }
    }

    private bool _laeuft;
    public bool Laeuft
    {
        get => _laeuft;
        set { _laeuft = value; Melde(nameof(Laeuft)); }
    }

    public Visibility PlatzhalterSichtbar => _bild is null ? Visibility.Visible : Visibility.Collapsed;

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Melde(string n) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}

// aus Thumbs.cs

/// <summary>
/// Holt Vorschaubilder ueber Windows selbst - dieselben, die der Explorer anzeigt.
/// Dadurch braucht das Programm keinen eigenen Video-Decoder fuer die Kacheln.
/// </summary>
internal static class Thumbs
{
    public static BitmapSource? Get(string file, int size)
    {
        try
        {
            if (!File.Exists(file)) return null;

            Guid iid = typeof(IShellItemImageFactory).GUID;
            int hr = SHCreateItemFromParsingName(file, IntPtr.Zero, ref iid, out IShellItemImageFactory factory);
            if (hr != 0 || factory is null) return null;

            IntPtr hBitmap = IntPtr.Zero;
            try
            {
                factory.GetImage(new SIZE { cx = size, cy = size }, SIIGBF.ResizeToFit, out hBitmap);
                if (hBitmap == IntPtr.Zero) return null;

                var src = Imaging.CreateBitmapSourceFromHBitmap(
                    hBitmap, IntPtr.Zero, System.Windows.Int32Rect.Empty,
                    BitmapSizeOptions.FromEmptyOptions());
                src.Freeze();
                return src;
            }
            finally
            {
                if (hBitmap != IntPtr.Zero) Native.DeleteObject(hBitmap);
                Marshal.ReleaseComObject(factory);
            }
        }
        catch
        {
            return null;
        }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    private static extern int SHCreateItemFromParsingName(string path, IntPtr pbc, ref Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory ppv);

    [StructLayout(LayoutKind.Sequential)]
    private struct SIZE { public int cx; public int cy; }

    [Flags]
    private enum SIIGBF
    {
        ResizeToFit = 0x00,
        BiggerSizeOk = 0x01,
        MemoryOnly = 0x02,
        IconOnly = 0x04,
        ThumbnailOnly = 0x08,
        InCacheOnly = 0x10,
    }

    [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemImageFactory
    {
        void GetImage(SIZE size, SIIGBF flags, out IntPtr phbm);
    }
}

// aus Karussell.cs

/// <summary>
/// Haelt die Reihenfolge, in der die Videos wechseln.
///
/// Zufaellig heisst hier nicht gewuerfelt, sondern gemischt: Jedes Video kommt
/// einmal an die Reihe, danach wird neu gemischt. Wuerfeln waere einfacher, wuerde
/// dasselbe Video aber regelmaessig zweimal hintereinander ziehen, und das faellt
/// unangenehm auf. Beim Neumischen wird zusaetzlich verhindert, dass ausgerechnet
/// das eben gelaufene wieder vorn steht.
/// </summary>
internal sealed class Karussell
{
    private readonly Random _zufall = new();
    private List<string> _reihe = [];
    private int _stelle = -1;
    private bool _zufaellig;

    internal bool Leer => _reihe.Count == 0;
    internal int Anzahl => _reihe.Count;

    /// <summary>Was als naechstes kaeme, ohne weiterzuruecken. Fuer das Vorrechnen.</summary>
    internal string? Vorschau => _reihe.Count == 0 ? null : _reihe[(_stelle + 1) % _reihe.Count];

    /// <summary>
    /// Setzt die Liste neu. Laeuft gerade eines der Videos, wird die Reihe so
    /// gedreht, dass es als aktuelles gilt - sonst spraenge das Karussell nach
    /// jeder Aenderung in den Einstellungen wieder an den Anfang.
    /// </summary>
    internal void Neu(IEnumerable<string> pfade, bool zufaellig, string? laeuftGerade)
    {
        _zufaellig = zufaellig;
        _reihe = [.. pfade.Where(File.Exists)];
        if (zufaellig) Mischen();
        else _reihe.Sort((a, b) => string.Compare(Path.GetFileName(a), Path.GetFileName(b),
                                                  StringComparison.OrdinalIgnoreCase));

        _stelle = laeuftGerade is null
            ? -1
            : _reihe.FindIndex(p => string.Equals(p, laeuftGerade, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Rueckt vor und liefert das naechste Video. Null, wenn keines da ist.</summary>
    internal string? Weiter()
    {
        if (_reihe.Count == 0) return null;
        _stelle++;
        if (_stelle >= _reihe.Count)
        {
            _stelle = 0;
            if (_zufaellig && _reihe.Count > 1)
            {
                string zuletzt = _reihe[^1];
                Mischen();
                // Nicht dasselbe zweimal hintereinander: steht es wieder vorn,
                // tauscht es mit einem anderen Platz.
                if (_reihe[0] == zuletzt)
                    (_reihe[0], _reihe[^1]) = (_reihe[^1], _reihe[0]);
            }
        }
        return _reihe[_stelle];
    }

    private void Mischen()
    {
        for (int i = _reihe.Count - 1; i > 0; i--)
        {
            int j = _zufall.Next(i + 1);
            (_reihe[i], _reihe[j]) = (_reihe[j], _reihe[i]);
        }
    }

    /// <summary>
    /// Rechenprobe. Laeuft wie die von Verkleinern bei jedem Start mit und kostet
    /// Mikrosekunden; die Reihenfolge ist die einzige Stelle hier, an der gerechnet
    /// statt abgefragt wird.
    /// </summary>
    internal static List<string> Selbstpruefung()
    {
        var fehler = new List<string>();
        void Pruefe(string was, bool gut) { if (!gut) fehler.Add(was); }

        string[] drei = ["a.mp4", "b.mp4", "c.mp4"];

        var k = new Karussell();
        k.Neu([], zufaellig: false, laeuftGerade: null);
        Pruefe("leere Liste liefert nichts", k.Weiter() is null && k.Leer);

        // Ohne File.Exists laesst sich die Reihenfolge nicht mit erfundenen Namen
        // pruefen. Deshalb hier mit Dateien, die es wirklich gibt.
        string ordner = Path.Combine(Path.GetTempPath(), "tapete-karussellprobe");
        try
        {
            Directory.CreateDirectory(ordner);
            var pfade = drei.Select(n => Path.Combine(ordner, n)).ToArray();
            foreach (var p in pfade) if (!File.Exists(p)) File.WriteAllText(p, "");

            k = new Karussell();
            k.Neu(pfade, zufaellig: false, laeuftGerade: null);
            Pruefe("drei Videos angekommen", k.Anzahl == 3);
            Pruefe("der Reihe nach: erst a", Path.GetFileName(k.Weiter() ?? "") == "a.mp4");
            Pruefe("der Reihe nach: dann b", Path.GetFileName(k.Weiter() ?? "") == "b.mp4");
            Pruefe("der Reihe nach: dann c", Path.GetFileName(k.Weiter() ?? "") == "c.mp4");
            Pruefe("danach wieder a", Path.GetFileName(k.Weiter() ?? "") == "a.mp4");

            k = new Karussell();
            k.Neu(pfade, zufaellig: false, laeuftGerade: pfade[1]);
            Pruefe("laufendes Video wird gefunden", Path.GetFileName(k.Weiter() ?? "") == "c.mp4");

            // Gemischt: In zwei vollen Runden muss jedes Video genau zweimal kommen,
            // und keines darf zweimal hintereinander laufen.
            k = new Karussell();
            k.Neu(pfade, zufaellig: true, laeuftGerade: null);
            var gezogen = new List<string>();
            for (int i = 0; i < 6; i++) gezogen.Add(Path.GetFileName(k.Weiter() ?? ""));
            Pruefe("jedes Video zweimal in zwei Runden",
                   drei.All(n => gezogen.Count(g => g == n) == 2));
            Pruefe("nie zweimal hintereinander dasselbe",
                   !gezogen.Zip(gezogen.Skip(1)).Any(p => p.First == p.Second));

            Pruefe("Vorschau nennt das naechste", k.Vorschau is not null);
        }
        catch (Exception e)
        {
            fehler.Add("Probe warf " + e.GetType().Name + ": " + e.Message);
        }
        finally
        {
            try { Directory.Delete(ordner, recursive: true); } catch { }
        }

        return fehler;
    }
}
