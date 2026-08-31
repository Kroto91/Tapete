using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;

namespace Tapete;

/// <summary>Eine neuere Fassung, die auf GitHub bereitliegt.</summary>
internal sealed record Neuigkeit(Version Version, string Titel, string SetupUrl, string Seite);

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

    private static readonly HttpClient Netz = Bauen();

    private static HttpClient Bauen()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        // GitHub weist Anfragen ohne Kennung ab.
        c.DefaultRequestHeaders.UserAgent.ParseAdd("Tapete");
        c.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return c;
    }

    /// <summary>Fragt die neueste Veroeffentlichung ab. Null heisst: nichts Neues.</summary>
    internal static async Task<Neuigkeit?> Suchen()
    {
        if (!Eingerichtet) return null;
        try
        {
            string url = $"https://api.github.com/repos/{Besitzer}/{Ablage}/releases/latest";
            using var doc = JsonDocument.Parse(await Netz.GetStringAsync(url));
            var w = doc.RootElement;

            string etikett = w.GetProperty("tag_name").GetString() ?? "";
            if (!Version.TryParse(etikett.TrimStart('v', 'V'), out var gefunden)) return null;
            gefunden = Dreistellig(gefunden);
            if (gefunden <= Eigene)
            {
                Hintergrund.Notiz($"Aktualisierung: {gefunden} ist nicht neuer als {Eigene}");
                return null;
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
                return null;
            }

            Hintergrund.Notiz($"Aktualisierung: {gefunden} gefunden, eigene {Eigene}");
            return new Neuigkeit(gefunden,
                w.GetProperty("name").GetString() ?? etikett,
                setup,
                w.GetProperty("html_url").GetString() ?? "");
        }
        catch (Exception e)
        {
            Hintergrund.Notiz($"Aktualisierung: {e.GetType().Name}: {e.Message}");
            return null;
        }
    }

    /// <summary>
    /// Laedt das Setup und startet es. Null heisst geglueckt, sonst der Grund.
    /// Tapete selbst muss nichts schliessen, das uebernimmt der Installer.
    /// </summary>
    internal static async Task<string?> HolenUndStarten(Neuigkeit n)
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
            byte[] daten = await Netz.GetByteArrayAsync(u);
            await File.WriteAllBytesAsync(ziel, daten);
            Hintergrund.Notiz($"Aktualisierung: {daten.Length} Bytes geladen nach {ziel}");
            Process.Start(new ProcessStartInfo(ziel) { UseShellExecute = true });
            return null;
        }
        catch (Exception e)
        {
            Hintergrund.Notiz($"Aktualisierung, Download: {e.GetType().Name}: {e.Message}");
            return e.Message;
        }
    }
}
