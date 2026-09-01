using System.IO;
using System.Text.Json;
using Microsoft.Win32;

namespace Tapete;

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
        catch { }
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
