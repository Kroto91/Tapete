using System.IO;

namespace Tapete;

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
