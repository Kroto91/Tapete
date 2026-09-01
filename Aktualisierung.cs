using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;

namespace Tapete;

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
            if (still) start.Arguments = "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART";
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
