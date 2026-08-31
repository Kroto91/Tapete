using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

namespace Tapete;

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
    /// Zielmasse: passen in den Bildschirm, behalten das Seitenverhaeltnis und
    /// werden nie groesser als die Vorlage. Gerade Zahlen, weil H.264 in
    /// 2x2-Bloecken rechnet.
    /// </summary>
    internal static (int Breite, int Hoehe) Ziel(int vb, int vh, int bb, int bh)
    {
        if (vb <= 0 || vh <= 0 || bb <= 0 || bh <= 0) return (vb, vh);
        double f = Math.Min((double)bb / vb, (double)bh / vh);
        if (f >= 1) return (vb, vh);
        return (Gerade(vb * f), Gerade(vh * f));
    }

    private static int Gerade(double x) => Math.Max(2, (int)Math.Round(x / 2) * 2);

    /// <summary>
    /// Lohnt das Rechnen? Erst ab einem Fuenftel weniger Bildpunkten. Darunter
    /// stuenden Aufwand und der Qualitaetsverlust des Neukodierens gegen fast nichts.
    /// </summary>
    private static bool Lohnt(int vb, int vh, int zb, int zh) =>
        (long)zb * zh * 5 <= (long)vb * vh * 4;

    /// <summary>
    /// Masse der Flaeche, auf der gespielt wird. Dieselbe Wahl wie in
    /// Hintergrund.ZielFlaeche: "*" heisst alle Bildschirme zusammen, sonst der
    /// benannte, und ohne Wahl der Hauptbildschirm.
    /// </summary>
    internal static (int Breite, int Hoehe) Bildschirmmasse(string? bildschirm)
    {
        if (bildschirm != "*")
        {
            var alle = Native.Bildschirme();
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
    private static string Name(string video, int bb, int bh)
    {
        long gr = 0;
        try { gr = new FileInfo(video).Length; } catch { }
        return $"{Path.GetFileNameWithoutExtension(video)}-{gr}-{bb}x{bh}.mp4";
    }

    /// <summary>Die gerechnete Fassung, falls sie schon vorliegt.</summary>
    internal static string? Fertig(string video, int bb, int bh)
    {
        try
        {
            string p = Path.Combine(Ordner, Name(video, bb, bh));
            return File.Exists(p) ? p : null;
        }
        catch { return null; }
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
    internal static string? Erzeugen(string video, int bb, int bh)
    {
        string? mpv = Hintergrund.MpvSuchen();
        if (mpv is null) return null;

        string? schon = Fertig(video, bb, bh);
        if (schon is not null) return schon;

        var m = Masse(video);
        if (m is null) return null;

        var (zb, zh) = Ziel(m.Value.Breite, m.Value.Hoehe, bb, bh);
        if (!Lohnt(m.Value.Breite, m.Value.Hoehe, zb, zh))
        {
            Hintergrund.Notiz($"Verkleinern: {m.Value.Breite}x{m.Value.Hoehe} auf {bb}x{bh} lohnt nicht");
            return null;
        }

        // Bildpunkte mal Bilder mal 0,05 Bit. Ergibt fuer 1920x804 bei 60 Bildern
        // rund 4,6 Mbit - die Groessenordnung, mit der die Messung oben lief.
        long rate = Math.Clamp((long)(zb * (double)zh * m.Value.Bilder * 0.05), 1_000_000, 20_000_000);

        string ziel = Path.Combine(Ordner, Name(video, bb, bh));
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
                $"--vf=scale={zb}:{zh}",
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
                        $"{m.Value.Breite}x{m.Value.Hoehe} -> {zb}x{zh} mit {enc} " +
                        $"in {uhr.ElapsedMilliseconds} ms");
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

        Pruefe("3440x1440 auf 1920x1080 -> 1920x804", Ziel(3440, 1440, 1920, 1080) == (1920, 804));
        Pruefe("gleich gross bleibt unveraendert", Ziel(3440, 1440, 3440, 1440) == (3440, 1440));
        Pruefe("kleiner als der Schirm wird nie vergroessert", Ziel(1920, 1080, 3440, 1440) == (1920, 1080));
        Pruefe("1920x1080 auf 1280x720", Ziel(1920, 1080, 1280, 720) == (1280, 720));
        Pruefe("gerade Zahlen", Ziel(1999, 1001, 1000, 1000).Breite % 2 == 0);
        Pruefe("Unsinn bleibt Unsinn", Ziel(0, 0, 1920, 1080) == (0, 0));
        Pruefe("Faktor drei lohnt", Lohnt(3440, 1440, 1920, 804));
        Pruefe("gleich gross lohnt nicht", !Lohnt(1920, 1080, 1920, 1080));
        Pruefe("ein Zehntel weniger lohnt nicht", !Lohnt(1920, 1080, 1820, 1024));

        return fehler;
    }
}
