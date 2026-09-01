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
