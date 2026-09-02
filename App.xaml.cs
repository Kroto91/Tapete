using System.Drawing;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Interop;
using Forms = System.Windows.Forms;

namespace Tapete;

public partial class App : Application
{
    private static Mutex? _einzelInstanz;

    public Settings Einstellungen { get; private set; } = new();
    // Nur melden, was tatsaechlich spielt. Sonst zeigt die Statuszeile "Laeuft: x.mp4"
    // und die Kachel einen Punkt, obwohl der Aufbau fehlgeschlagen ist.
    //
    // Gemeldet wird die Vorlage, nicht die tatsaechlich abgespielte Datei. Laeuft
    // eine auf Bildschirmgroesse gerechnete Fassung, ist das eine andere Datei im
    // Zwischenspeicher; hervorheben soll die Kachel aber das gewaehlte Video.
    public string? AktuellesVideo => _wallpaper is { Laeuft: true } ? _original : null;

    private Hintergrund? _wallpaper;
    private MainWindow? _fenster;
    private Forms.NotifyIcon? _tray;
    private Forms.ToolStripMenuItem? _spielEintrag;

    /// <summary>True, wenn die Automatik eingeschaltet hat und nicht der Nutzer.</summary>
    private bool _spielAutomatisch;

    /// <summary>Merkt den letzten Stand, damit nur der Wechsel zaehlt. Siehe SpielAutomatikStarten.</summary>
    private bool _zuletztVollbild;

    /// <summary>Die gefundene neuere Fassung, oder null. Das Fenster liest sie hier.</summary>
    internal Neuigkeit? Neuigkeit { get; private set; }

    /// <summary>Wird gemeldet, sobald eine neuere Fassung gefunden ist.</summary>
    internal event Action? NeuigkeitGefunden;

    private System.Windows.Threading.DispatcherTimer? _spielWacht;

    private readonly Karussell _karussell = new();
    private System.Windows.Threading.DispatcherTimer? _karussellUhr;
    private DateTime _letzterWechsel = DateTime.Now;
    private HwndSource? _quelle;
    private const int HotkeyKennung = 0xA71;
    private const int HotkeyWechseln = 0xA72;

    /// <summary>Das gewaehlte Video. Siehe AktuellesVideo.</summary>
    private string? _original;

    /// <summary>Damit nicht zwei Umrechnungen gleichzeitig laufen.</summary>
    private bool _rechnet;

    /// <summary>
    /// Die Verteilung des Karussells auf die Bildschirme: Geraetename auf Video.
    /// Nur gesetzt, solange das Karussell selbst verteilt hat; ein Klick auf eine
    /// Kachel raeumt sie weg, sonst klebte die alte Verteilung an der neuen Wahl.
    /// </summary>
    private IReadOnlyDictionary<string, string>? _karussellJeSchirm;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        Hintergrund.Notiz("OnStartup, Argumente: [" + string.Join(" ", e.Args) + "]");

        var probe = Verkleinern.Selbstpruefung();
        Hintergrund.Notiz(probe.Count == 0
            ? "Rechenprobe Verkleinern: in Ordnung"
            : "Rechenprobe Verkleinern FEHLER: " + string.Join("; ", probe));

        var probe2 = Karussell.Selbstpruefung();
        Hintergrund.Notiz(probe2.Count == 0
            ? "Rechenprobe Karussell: in Ordnung"
            : "Rechenprobe Karussell FEHLER: " + string.Join("; ", probe2));

        var probe3 = SchalterProbe();
        Hintergrund.Notiz(probe3.Count == 0
            ? "Rechenprobe Schalter und Verteilung: in Ordnung"
            : "Rechenprobe Schalter und Verteilung FEHLER: " + string.Join("; ", probe3));

        // Als Bildschirmschoner aufgerufen? Windows uebergibt /s zum Anzeigen,
        // /c fuer die Einstellungen und /p mit einem Fensterhandle fuer die kleine
        // Vorschau. Das muss vor der Einzelinstanz-Sperre geprueft werden: Sonst
        // beendet sich der Schoner sofort wieder, weil das normale Tapete laeuft.
        string schalterArg = e.Args.FirstOrDefault(a => a.StartsWith("/")) ?? "";
        if (schalterArg.StartsWith("/s", StringComparison.OrdinalIgnoreCase))
        {
            SchonerStarten();
            return;
        }
        if (schalterArg.StartsWith("/p", StringComparison.OrdinalIgnoreCase))
        {
            // Keine Vorschau im kleinen Fenster der Systemsteuerung. Sie kostet
            // einen zweiten mpv fuer ein briefmarkengrosses Bild; das Feld bleibt
            // schwarz, und das ist kein Fehler.
            Hintergrund.Notiz("Schoner: Vorschau angefragt, wird nicht angeboten");
            Shutdown();
            return;
        }

        // Zweiter Start bringt nur das vorhandene Fenster nach vorn.
        _einzelInstanz = new Mutex(initiallyOwned: true, "Tapete_EinzelInstanz_9f2c", out bool neu);
        if (!neu) { Hintergrund.Notiz("ENDE: schon eine Instanz da"); Shutdown(); return; }

        Einstellungen = Settings.Laden();
        Hintergrund.Notiz("Einstellungen geladen. LetztesVideo=" + (Einstellungen.LetztesVideo ?? "null"));

        // Vor dem ersten Fenster, sonst baut es sich mit dem Standardthema auf und
        // wechselt gleich danach sichtbar um.
        ThemaAnwenden(Einstellungen.Thema);

        // Reparatur: Wurde das Programm beim letzten Mal abgewuergt, koennen die
        // Desktop-Symbole noch ausgeblendet sein. Erst einmal wieder einschalten.
        Hintergrund.SymboleWiederherstellen();

        _fenster = new MainWindow();
        TrayAufbauen();
        TastenkuerzelAnmelden();
        SpielAutomatikStarten();
        KarussellStarten();
        _ = NachAktualisierungSehen();

        bool versteckt = e.Args.Any(a => a.Equals("--versteckt", StringComparison.OrdinalIgnoreCase));
        if (!versteckt) _fenster.Show();

        // /c heisst: der Nutzer hat in Windows auf "Einstellungen" geklickt.
        if (schalterArg.StartsWith("/c", StringComparison.OrdinalIgnoreCase))
            new EinstellungenFenster { Owner = _fenster }.Show();

        // Ein Spielmodus, den nur die Automatik eingeschaltet hat, gilt nicht ueber
        // einen Neustart hinweg. Sonst bleibt der Hintergrund weg, wenn der Rechner
        // waehrend eines Spiels oder des Bildschirmschoners hart ausgegangen ist.
        if (Einstellungen.Spielmodus && Einstellungen.SpielmodusAutomatisch)
        {
            Hintergrund.Notiz("Spielmodus stand nur automatisch an, wird zurueckgenommen");
            Einstellungen.Spielmodus = false;
            Einstellungen.SpielmodusAutomatisch = false;
            Einstellungen.Speichern();
        }

        // Zuletzt gewaehltes Video wieder anwerfen.
        if (Einstellungen.Spielmodus)
        {
            Hintergrund.Notiz("Spielmodus war zuletzt an, der Hintergrund bleibt aus");
            SpielmodusAnzeigen();
            return;
        }

        bool gesetzt = !string.IsNullOrWhiteSpace(Einstellungen.LetztesVideo);
        bool da = gesetzt && File.Exists(Einstellungen.LetztesVideo);
        Hintergrund.Notiz($"Startvideo: gesetzt={gesetzt} vorhanden={da}");
        if (gesetzt && da) HintergrundSetzen(Einstellungen.LetztesVideo!);
        else if (gesetzt) Ersatzvideo();
        if (_wallpaper is not { Laeuft: true }) NachreichenStarten();
    }

    /// <summary>
    /// Faellt auf ein anderes Video zurueck, wenn das zuletzt gespielte fehlt.
    ///
    /// Bis 1.4.1 blieb der Hintergrund in dem Fall einfach leer, ohne dass jemand
    /// den Grund erfuhr. Am 01.09.2026 hatte ein Tester seine Videodatei von Hand
    /// geloescht; im Protokoll stand nur `vorhanden=False`, und eine Stunde ging
    /// dafuer drauf, das fuer einen Fehler der Selbstaktualisierung zu halten.
    ///
    /// Genommen wird das alphabetisch erste Video im Videoordner. Eine klügere
    /// Wahl braucht es nicht: Es geht darum, dass ueberhaupt etwas laeuft und die
    /// Meldung erscheint.
    /// </summary>
    private void Ersatzvideo()
    {
        string? ersatz = null;
        try
        {
            ersatz = Directory.EnumerateFiles(Settings.VideoOrdner)
                .Where(Hintergrund.IstUnterstuetzt)
                .OrderBy(p => Path.GetFileName(p), StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
        }
        catch (Exception e) { Hintergrund.Notiz($"Ersatzvideo suchen gescheitert: {e.GetType().Name}: {e.Message}"); }

        string fehlt = Path.GetFileName(Einstellungen.LetztesVideo ?? "");
        if (ersatz is null)
        {
            Hintergrund.Notiz($"Startvideo {fehlt} fehlt, und im Videoordner liegt kein Ersatz");
            _fenster?.MeldungZeigen($"{fehlt} ist nicht mehr da, und der Videoordner ist leer.");
            return;
        }

        Hintergrund.Notiz($"Startvideo {fehlt} fehlt, Ersatz: {Path.GetFileName(ersatz)}");
        HintergrundSetzen(ersatz);
        _fenster?.MeldungZeigen($"{fehlt} ist nicht mehr da. Es laeuft jetzt {Path.GetFileName(ersatz)}.");
    }

    /// <summary>
    /// Zweiter Anlauf fuer den Hintergrund, alle fuenf Sekunden, hoechstens sechsmal.
    ///
    /// Beim Anmelden schlug der erste Versuch am 31.08.2026 fuenfmal in Folge fehl,
    /// zuletzt weil Settings.Laden() eine leere Einstellung lieferte, obwohl die
    /// Datei unveraendert dalag. Von Hand gestartet klappt derselbe Aufruf immer.
    /// Die Ursache ist ungeklaert; ein begrenzter zweiter Anlauf deckt sie ab, egal
    /// woran es liegt, und kostet im Normalfall nichts, weil er dann gar nicht laeuft.
    ///
    /// ponytail: Wiederholen statt Ursache beheben. Sobald das Protokoll die Ursache
    /// nennt, gehoert die an ihrer Stelle behoben und das hier kann weg.
    /// </summary>
    private void NachreichenStarten()
    {
        int versuch = 0;
        var takt = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(5)
        };
        takt.Tick += (_, _) =>
        {
            versuch++;
            Einstellungen = Settings.Laden();
            string? v = Einstellungen.LetztesVideo;
            bool brauchbar = !string.IsNullOrWhiteSpace(v) && File.Exists(v);
            Hintergrund.Notiz($"Nachreichen {versuch}: brauchbar={brauchbar}");

            if (brauchbar) HintergrundSetzen(v!);
            if (_wallpaper is { Laeuft: true })
            {
                Hintergrund.Notiz($"Nachreichen {versuch}: geglueckt");
                takt.Stop();
                return;
            }
            if (versuch >= 6)
            {
                Hintergrund.Notiz("Nachreichen aufgegeben nach 6 Versuchen");
                takt.Stop();
            }
        };
        takt.Start();
        Hintergrund.Notiz("Erster Versuch fehlgeschlagen, Nachreichen laeuft an");
    }

    // ---------- Hintergrund ----------

    /// <summary>
    /// Spielmodus. Anders als "Pause wenn verdeckt" wird mpv nicht angehalten,
    /// sondern beendet: der Prozess ist weg, samt seiner rund 200 MB und seiner
    /// Dekodiersitzung auf der Grafikkarte.
    ///
    /// Warum beides nebeneinander steht: Die Pause greift nur, solange der Desktop
    /// wirklich verdeckt ist. Ein Spiel im randlosen Fenster, oder eines auf dem
    /// zweiten Monitor, laesst ein Stueck Desktop sichtbar - dann laeuft das Video
    /// weiter. Der Spielmodus fragt nicht, er beendet.
    /// </summary>
    public bool Spielmodus
    {
        get => Einstellungen.Spielmodus;
        set
        {
            if (Einstellungen.Spielmodus == value) return;
            Einstellungen.Spielmodus = value;
            Einstellungen.Speichern();
            Hintergrund.Notiz("Spielmodus: " + (value ? "an" : "aus"));

            // merken: false laesst _original stehen, damit dasselbe Video zurueckkommt.
            if (value) HintergrundAus(merken: false);
            else HintergrundNeuAufbauen();

            SpielmodusAnzeigen();
        }
    }

    /// <summary>
    /// Sucht beim Start nach einer neueren Fassung und spielt sie gleich ein.
    ///
    /// Bis 1.2.1 hing das allein an einem Knopf im Fenster. Im Autostart wird das
    /// Fenster nie gezeigt, den Knopf sieht dort also niemand - das Programm hat
    /// sich nie aktualisiert, obwohl es die Pruefung schon gab.
    ///
    /// Der Knopf bleibt trotzdem: Wer das Fenster offen hat, soll sehen, was
    /// passiert, und es notfalls selbst ausloesen koennen.
    /// </summary>
    private async Task NachAktualisierungSehen()
    {
        var neu = (await AktualisierungPruefen()).Neu;
        if (neu is null) return;

        // Nur die installierte Kopie bringt sich selbst auf den neuen Stand.
        // Sonst legte das Setup eine zweite Kopie an anderer Stelle an, und die
        // hier laufende bliebe alt - samt Autostart, der auf sie zeigt.
        if (!Aktualisierung.AusInstallation)
        {
            Hintergrund.Notiz("Aktualisierung: laeuft nicht aus der Installation ("
                + (Aktualisierung.InstallationsOrdner ?? "keine eingetragen")
                + "), deshalb nur der Knopf");
            return;
        }

        // Zweimal dieselbe Fassung nicht von selbst versuchen. Geht eine
        // Aktualisierung nicht durch, laedt Tapete sonst bei jedem Start
        // 95 MB und beendet sich anschliessend fuer nichts.
        string kennung = neu.Version.ToString();
        if (Einstellungen.AktualisierungVersucht == kennung)
        {
            Hintergrund.Notiz($"Aktualisierung: {kennung} war schon einmal dran, nur der Knopf");
            return;
        }
        Einstellungen.AktualisierungVersucht = kennung;
        Einstellungen.Speichern();

        // mpv zuerst wegnehmen. Es liegt im selben Ordner und haelt seine eigene
        // Programmdatei fest; je weniger der Neustartmanager abraeumen muss,
        // desto weniger kann daran scheitern.
        HintergrundAus(merken: false);

        string? fehler = await Aktualisierung.HolenUndStarten(neu, still: true);
        if (fehler is not null)
        {
            // Der Hintergrund war schon weg, also zurueckholen.
            HintergrundNeuAufbauen();
            return;
        }

        // Dem Installer Zeit geben, sich in den Temp-Ordner zu kopieren, danach
        // den Weg frei machen: Eine laufende Programmdatei laesst sich nicht
        // ersetzen. Kommt der Neustartmanager zuerst, beendet er uns; dann laeuft
        // das hier nie zu Ende, was in Ordnung ist. Gestartet wird Tapete danach
        // vom Setup selbst, siehe den Abschnitt [Run] in Tapete.iss.
        await Task.Delay(2000);
        Hintergrund.Notiz("Aktualisierung: beende mich fuer den Installer");
        Beenden();
    }

    /// <summary>
    /// Fragt bei GitHub nach und merkt sich das Ergebnis. Auch vom Knopf in der
    /// Titelzeile aufgerufen, deshalb kommt das ganze Ergebnis zurueck und nicht
    /// nur die Neuigkeit: Das Fenster muss "alles aktuell" von "Abfrage
    /// gescheitert" unterscheiden koennen.
    /// </summary>
    internal async Task<Pruefung> AktualisierungPruefen()
    {
        var ergebnis = await Aktualisierung.Suchen();
        Neuigkeit = ergebnis.Neu;
        if (ergebnis.Neu is not null) NeuigkeitGefunden?.Invoke();
        return ergebnis;
    }

    // ---------- Karussell ----------

    /// <summary>
    /// Baut die Reihenfolge neu auf. Wird beim Start gerufen und immer dann, wenn
    /// sich in den Einstellungen die Auswahl oder die Reihenfolge geaendert hat.
    /// </summary>
    internal void KarussellNeuAufbauen()
    {
        _karussell.Neu(Einstellungen.KarussellPfade, Einstellungen.KarussellZufaellig, _original);
        Hintergrund.Notiz($"Karussell: {_karussell.Anzahl} Videos, "
            + (Einstellungen.KarussellZufaellig ? "gemischt" : "der Reihe nach")
            + (Einstellungen.KarussellAn ? ", " + Einstellungen.KarussellDauerText : ", aus"));
        VorbereitenAnstossen();
    }

    /// <summary>
    /// Prueft alle zwanzig Sekunden, ob gewechselt werden soll.
    ///
    /// Nicht im Minutentakt, weil die Standzeit sonst um bis zu eine Minute
    /// daneben laege. Ein Durchgang kostet einen Vergleich und - nur wenn die
    /// Zeit reif ist - eine Abfrage der freien Desktopflaeche.
    /// </summary>
    private void KarussellStarten()
    {
        KarussellNeuAufbauen();
        _karussellUhr = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(20)
        };
        _karussellUhr.Tick += (_, _) =>
        {
            if (!Einstellungen.KarussellAn || _karussell.Leer) return;
            if (Einstellungen.Spielmodus) return;
            if ((DateTime.Now - _letzterWechsel).TotalMinutes < Einstellungen.KarussellMinuten) return;

            // Verdeckten Desktop sieht niemand. Die Uhr laeuft weiter, gewechselt
            // wird erst, wenn wieder etwas zu sehen ist - sonst zieht ein
            // Arbeitstag am Bildschirm die halbe Sammlung ungesehen vorbei.
            if (Native.DesktopVerdeckt()) return;

            KarussellWeiter();
        };
        _karussellUhr.Start();
    }

    /// <summary>Sofort zum naechsten Video. Aus dem Menue, per Tastenkuerzel oder von der Uhr.</summary>
    internal void KarussellWeiter()
    {
        string? naechstes = _karussell.Weiter();
        if (naechstes is null)
        {
            Hintergrund.Notiz("Karussell: kein Video angekreuzt");
            return;
        }

        // Auf mehreren Bildschirmen zieht jeder ein eigenes naechstes Video, statt
        // ueberall dasselbe zu zeigen. Nur bei "jeder Bildschirm einzeln" und nur,
        // wenn ueberhaupt genug Videos angekreuzt sind - sonst liefe zweimal
        // dasselbe und der Umweg brächte nichts.
        Dictionary<string, string>? jeSchirm = null;
        var schirme = Einstellungen.Bildschirm == "*" ? Native.Bildschirme() : [];
        if (schirme.Count > 1 && _karussell.Anzahl >= schirme.Count)
        {
            jeSchirm = new Dictionary<string, string> { [schirme[0].Name] = naechstes };
            foreach (var s in schirme.Skip(1))
                if (_karussell.Weiter() is string weiteres) jeSchirm[s.Name] = weiteres;
            Hintergrund.Notiz("Karussell: " + string.Join(", ",
                jeSchirm.Select(p => p.Key.Replace(@"\\.\", "") + " -> " + Path.GetFileName(p.Value))));
        }
        else
        {
            Hintergrund.Notiz("Karussell: weiter zu " + Path.GetFileName(naechstes));
        }

        HintergrundSetzen(naechstes, jeSchirm);
        VorbereitenAnstossen();
    }

    /// <summary>
    /// Rechnet die verkleinerte Fassung des naechsten Videos schon aus, waehrend
    /// das aktuelle laeuft. Ohne das haengt der erste Wechsel zu einem neuen Video
    /// so lange, wie das Umrechnen dauert, und man sieht dabei das Original.
    /// </summary>
    private void VorbereitenAnstossen()
    {
        if (!Einstellungen.KarussellAn || _rechnet) return;
        string? naechstes = _karussell.Vorschau;
        if (naechstes is null) return;

        var (bb, bh) = Verkleinern.Bildschirmmasse(Einstellungen.Bildschirm);
        bool halb = Einstellungen.BildrateHalbieren;
        if (Verkleinern.Fertig(naechstes, bb, bh, halb) is not null) return;

        _rechnet = true;
        Task.Run(() =>
        {
            try { Verkleinern.Erzeugen(naechstes, bb, bh, halb); }
            catch (Exception e) { Hintergrund.Notiz($"Vorbereiten warf {e.GetType().Name}: {e.Message}"); }
            Dispatcher.Invoke(() => _rechnet = false);
        });
    }

    /// <summary>
    /// Von Hand umgeschaltet. Danach haelt sich die Automatik heraus, bis das naechste
    /// Spiel anfaengt oder aufhoert - wer selbst schaltet, will nicht ueberstimmt werden.
    /// </summary>
    public void SpielmodusUmschalten()
    {
        // Von Hand geschaltet zaehlt als Entscheidung des Nutzers und ueberdauert
        // deshalb einen Neustart.
        _spielAutomatisch = false;
        Einstellungen.SpielmodusAutomatisch = false;
        Spielmodus = !Spielmodus;
    }

    /// <summary>
    /// Fragt Windows alle drei Sekunden, ob eine Vollbildanwendung laeuft. Das ist ein
    /// einziger Aufruf je Takt, keine Prozessliste und keine Liste bekannter Spiele.
    ///
    /// Gehandelt wird nur beim Wechsel, nicht bei jedem Takt. Sonst schaltete die
    /// Automatik einen von Hand beendeten Spielmodus drei Sekunden spaeter wieder an,
    /// solange das Spiel laeuft.
    ///
    /// _spielAutomatisch trennt die beiden Wege: Was die Automatik eingeschaltet hat,
    /// nimmt sie auch zurueck. Was von Hand gesetzt wurde, bleibt stehen.
    /// </summary>
    private void SpielAutomatikStarten()
    {
        _zuletztVollbild = Native.VollbildAnwendungLaeuft();
        _spielWacht = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(3)
        };
        _spielWacht.Tick += (_, _) =>
        {
            bool spielt = Native.VollbildAnwendungLaeuft();
            if (spielt == _zuletztVollbild) return;
            _zuletztVollbild = spielt;

            if (spielt && !Spielmodus)
            {
                Hintergrund.Notiz("Vollbildanwendung erkannt");
                _spielAutomatisch = true;
                // Vor dem Setzen vermerken: Der Setter speichert, und danach soll
                // in der Datei stehen, dass hier die Automatik am Werk war.
                Einstellungen.SpielmodusAutomatisch = true;
                Spielmodus = true;
            }
            else if (!spielt && Spielmodus && _spielAutomatisch)
            {
                Hintergrund.Notiz("Vollbildanwendung beendet");
                _spielAutomatisch = false;
                Einstellungen.SpielmodusAutomatisch = false;
                Spielmodus = false;
            }
        };
        _spielWacht.Start();
    }

    /// <summary>
    /// Strg+Alt+G schaltet den Spielmodus um, auch waehrend ein Spiel im Vordergrund ist.
    /// Genau dafuer ist es da: An das Symbol neben der Uhr kommt man aus einem Vollbild
    /// schlecht heran.
    ///
    /// EnsureHandle statt Show: Mit --versteckt wird das Fenster nie gezeigt und haette
    /// sonst gar kein Handle, an dem der Tastendruck ankommen koennte.
    ///
    /// Ist die Tastenfolge schon vergeben, lehnt Windows sie ab. Dann steht das im
    /// Protokoll und alles andere laeuft weiter; ein Fehlerdialog beim Start waere
    /// laestiger als das fehlende Kuerzel.
    /// </summary>
    private void TastenkuerzelAnmelden()
    {
        try
        {
            IntPtr h = new WindowInteropHelper(_fenster!).EnsureHandle();
            _quelle = HwndSource.FromHwnd(h);
            _quelle?.AddHook(Fensternachricht);

            bool ok = Native.RegisterHotKey(h, HotkeyKennung,
                Native.MOD_CONTROL | Native.MOD_ALT | Native.MOD_NOREPEAT, 'G');
            Hintergrund.Notiz("Tastenkuerzel Strg+Alt+G: " + (ok ? "angemeldet" : "abgelehnt, vermutlich belegt"));

            bool ok2 = Native.RegisterHotKey(h, HotkeyWechseln,
                Native.MOD_CONTROL | Native.MOD_ALT | Native.MOD_NOREPEAT, 'W');
            Hintergrund.Notiz("Tastenkuerzel Strg+Alt+W: " + (ok2 ? "angemeldet" : "abgelehnt, vermutlich belegt"));
        }
        catch (Exception e)
        {
            Hintergrund.Notiz($"Tastenkuerzel: {e.GetType().Name}: {e.Message}");
        }
    }

    private IntPtr Fensternachricht(IntPtr hwnd, int nachricht, IntPtr wp, IntPtr lp, ref bool behandelt)
    {
        if (nachricht != Native.WM_HOTKEY) return IntPtr.Zero;
        if (wp.ToInt32() == HotkeyKennung) { SpielmodusUmschalten(); behandelt = true; }
        else if (wp.ToInt32() == HotkeyWechseln) { KarussellWeiter(); behandelt = true; }
        return IntPtr.Zero;
    }

    private void SpielmodusAnzeigen()
    {
        if (_spielEintrag is not null) _spielEintrag.Checked = Einstellungen.Spielmodus;
        _fenster?.StandAktualisieren();
        TrayTextSetzen();
    }

    public void HintergrundSetzen(string pfad,
        IReadOnlyDictionary<string, string>? karussellJeSchirm = null)
    {
        if (!File.Exists(pfad)) { Hintergrund.Notiz("HintergrundSetzen: Datei fehlt, " + pfad); return; }

        // Eine angeklickte Kachel heisst: zeig mir das jetzt. Der Spielmodus endet
        // damit - aber ohne den Umweg ueber HintergrundNeuAufbauen, denn gebaut wird
        // gleich hier unten. Sonst liefe der Aufbau zweimal.
        if (Einstellungen.Spielmodus)
        {
            Einstellungen.Spielmodus = false;
            Einstellungen.Speichern();
            Hintergrund.Notiz("Spielmodus: aus, weil ein Video gewaehlt wurde");
            SpielmodusAnzeigen();
        }

        HintergrundAus(merken: false);
        _original = pfad;

        // Die auf Bildschirmgroesse gerechnete Fassung nehmen, wenn es sie schon gibt.
        // Sonst faengt es mit dem Original an und reicht sie nach: Kodieren dauert
        // Sekunden bis Minuten und darf den Start nicht aufhalten.
        var (bb, bh) = Verkleinern.Bildschirmmasse(Einstellungen.Bildschirm);
        bool halb = Einstellungen.BildrateHalbieren;
        string abspielen = Verkleinern.Fertig(pfad, bb, bh, halb) ?? pfad;

        // problem faengt die Meldung aus dem Konstruktor ab. Sie wird ganz zum Schluss
        // noch einmal gesetzt, weil StandAktualisieren() dieselbe Zeile beschreibt und
        // den Fehler sonst sofort wieder ueberdeckt.
        string? problem = null;
        // Der Bildschirm gehoert in den Konstruktor, nicht in den Initialisierer: Der
        // laeuft erst danach, und der Konstruktor baut den Hintergrund schon auf.
        // BeiVollbildPausieren darf bleiben, das liest erst der Takt zwei Sekunden
        // spaeter, und der Standardwert stimmt.

        // Je Bildschirm ein eigenes Video, soweit eines zugewiesen ist. Nur bei
        // "jeder Bildschirm einzeln" sinnvoll, sonst laeuft ohnehin ueberall dasselbe.
        //
        // ponytail: Alle Fassungen werden auf den groessten Schirm gerechnet, nicht
        // je Schirm einzeln. Eine Datei je Bildschirmgroesse erst, wenn das auf dem
        // kleineren messbar Last kostet.
        //
        // Zwei Quellen koennen etwas dazu sagen: das Karussell, das beim Wechsel
        // auf jeden Schirm ein anderes Video legt, und die feste Zuweisung per
        // Rechtsklick. Die feste gewinnt - wer ein Video ausdruecklich auf einen
        // Schirm legt, will es dort behalten, auch waehrend das Karussell laeuft.
        _karussellJeSchirm = karussellJeSchirm;
        Dictionary<string, string>? jeSchirm = null;
        if (Einstellungen.Bildschirm == "*")
        {
            var roh = VerteilungMischen(karussellJeSchirm, Einstellungen.VideoJeBildschirm);
            if (roh.Count > 0)
            {
                jeSchirm = [];
                foreach (var (schirm, eigenes) in roh)
                    if (File.Exists(eigenes))
                        jeSchirm[schirm] = Verkleinern.Fertig(eigenes, bb, bh, halb) ?? eigenes;
                Hintergrund.Notiz($"Eigene Videos je Bildschirm: {jeSchirm.Count}");
            }
        }

        _wallpaper = new Hintergrund(abspielen, Einstellungen.Bildschirm, jeSchirm,
            SchalterMerken(), text => { problem = text; _fenster?.FehlerZeigen(text); })
        {
            BeiVollbildPausieren = Einstellungen.BeiVollbildPausieren,
            BeiAkkuPausieren = Einstellungen.BeiAkkuPausieren
        };

        // Nur merken, was auch wirklich laeuft. Sonst versucht Tapete bei jedem Start
        // dieselbe kaputte Datei und wartet dabei jedes Mal auf ein mpv-Fenster,
        // das nie kommt - ohne Fenster, also im Autostart, unbemerkt.
        if (_wallpaper.Laeuft)
        {
            Einstellungen.LetztesVideo = pfad;
            Einstellungen.Speichern();
        }

        _fenster?.StandAktualisieren();
        TrayTextSetzen();
        if (problem is not null) _fenster?.FehlerZeigen(problem);

        // Nach jedem Wechsel faengt die Standzeit von vorn an, auch wenn der Wechsel
        // von Hand kam. Sonst spraenge das Karussell gleich nach einem Klick weiter.
        _letzterWechsel = DateTime.Now;

        // Nur wenn wirklich das Original spielt. Lief schon die gerechnete Fassung,
        // gibt es nichts zu tun.
        if (_wallpaper is { Laeuft: true } && abspielen == pfad) VerkleinernAnstossen(pfad, bb, bh, halb);
    }

    /// <summary>
    /// Rechnet das Video im Hintergrund auf Bildschirmgroesse herunter und baut den
    /// Hintergrund danach mit der kleinen Fassung neu auf. Je Video und Bildschirm-
    /// groesse genau einmal, danach liegt die Datei im Zwischenspeicher.
    ///
    /// Der Neuaufbau ist ein kurzer Aussetzer im Bild. Der faellt einmal an, gegen
    /// dauerhaft ein Drittel der Dekodierlast - gemessen, siehe Verkleinern.
    /// </summary>
    private void VerkleinernAnstossen(string pfad, int bb, int bh, bool halbieren)
    {
        if (_rechnet) return;
        _rechnet = true;

        Task.Run(() =>
        {
            string? klein = null;
            try { klein = Verkleinern.Erzeugen(pfad, bb, bh, halbieren); }
            catch (Exception e) { Hintergrund.Notiz($"Verkleinern warf {e.GetType().Name}: {e.Message}"); }

            Dispatcher.Invoke(() =>
            {
                _rechnet = false;
                // Nur, wenn immer noch dasselbe Video laeuft. In der Zwischenzeit kann
                // laengst ein anderes gewaehlt worden sein.
                if (klein is not null && _original == pfad) HintergrundNeuAufbauen();
            });
        });
    }

    public void HintergrundAus() => HintergrundAus(merken: true);

    private void HintergrundAus(bool merken)
    {
        if (_wallpaper is not null)
        {
            _wallpaper.Dispose();
            _wallpaper = null;
        }
        if (merken)
        {
            _original = null;
            Einstellungen.LetztesVideo = null;
            Einstellungen.Speichern();
        }
        _fenster?.StandAktualisieren();
        TrayTextSetzen();
    }

    /// <summary>Setzt denselben Hintergrund neu, etwa nach einem Bildschirmwechsel.</summary>
    public void HintergrundNeuAufbauen()
    {
        // Die Vorlage, nicht die gerechnete Fassung. HintergrundSetzen sucht sich die
        // passende Fassung selbst, und nach einem Bildschirmwechsel ist das eine andere.
        string? pfad = _original ?? Einstellungen.LetztesVideo;
        // Die Verteilung des Karussells mitgeben, sonst liefe nach jeder
        // Einstellungsaenderung wieder ueberall dasselbe.
        if (!string.IsNullOrWhiteSpace(pfad) && File.Exists(pfad))
            HintergrundSetzen(pfad, _karussellJeSchirm);
    }

    public void PauseRegelAnwenden(bool an)
    {
        if (_wallpaper is not null) _wallpaper.BeiVollbildPausieren = an;
    }

    /// <summary>Die Schalter fuer den laufenden Aufbau, dazu die Zeile im Protokoll.</summary>
    private List<string> SchalterMerken()
    {
        var schalter = MpvSchalter(Einstellungen);
        Hintergrund.Notiz("mpv-Schalter: " + string.Join(" ", schalter));
        return schalter;
    }

    /// <summary>
    /// Laeuft als Bildschirmschoner. Eigener, kurzer Weg: kein Symbol neben der Uhr,
    /// kein Karussell, keine Aktualisierungsabfrage, keine Einzelinstanz-Sperre.
    /// Nur ein Video auf schwarzem Grund, bis jemand eine Taste drueckt.
    /// </summary>
    private void SchonerStarten()
    {
        Einstellungen = Settings.Laden();
        string? video = Einstellungen.LetztesVideo;
        if (string.IsNullOrWhiteSpace(video) || !File.Exists(video))
        {
            try
            {
                video = Directory.EnumerateFiles(Settings.VideoOrdner)
                    .Where(Hintergrund.IstUnterstuetzt)
                    .OrderBy(p => Path.GetFileName(p), StringComparer.OrdinalIgnoreCase)
                    .FirstOrDefault();
            }
            catch { video = null; }
        }
        if (video is null)
        {
            Hintergrund.Notiz("Schoner ABBRUCH: kein Video da");
            Shutdown();
            return;
        }

        var schoner = new Bildschirmschoner();
        schoner.Starten(video, MpvSchalter(Einstellungen));
    }

    // ---------- Erscheinungsbild ----------

    /// <summary>
    /// Die Themen, die es gibt: Dateiname unter Themen/ und der Name, der im
    /// Einstellungsfenster steht.
    /// </summary>
    internal static readonly (string Datei, string Name)[] Themen =
    [
        ("Cyber2077", "Cyberpunk, Gelb auf Schwarz"),
        ("RotSchwarz", "Alarm, Rot auf Schwarz"),
        ("Hud", "HUD, Cyan und feine Linien"),
        ("Retro", "Retro-Neon, Pink und Rundungen"),
    ];

    /// <summary>
    /// Tauscht das Erscheinungsbild im laufenden Programm.
    ///
    /// Ausgetauscht wird das erste zusammengefuehrte Woerterbuch. Weil alle Stile
    /// mit DynamicResource darauf zugreifen, zeichnen sich die offenen Fenster von
    /// selbst neu; ein Neustart ist nicht noetig.
    /// </summary>
    internal void ThemaAnwenden(string datei)
    {
        if (!Themen.Any(t => t.Datei == datei)) datei = "Hud";
        try
        {
            var neu = new ResourceDictionary
            {
                Source = new Uri($"Themen/{datei}.xaml", UriKind.Relative)
            };
            var alt = Resources.MergedDictionaries.FirstOrDefault();
            Resources.MergedDictionaries.Add(neu);
            if (alt is not null) Resources.MergedDictionaries.Remove(alt);
            Hintergrund.Notiz("Erscheinungsbild: " + datei);
            // Der Effekt gehoert zum Thema und muss mit ihm wechseln; der alte
            // liefe sonst mit den Zeiten des vorherigen weiter.
            _fenster?.EffektStarten();
        }
        catch (Exception e)
        {
            Hintergrund.Notiz($"Erscheinungsbild {datei} ging nicht: {e.GetType().Name}: {e.Message}");
        }
    }

    internal void EffektNeuStarten() => _fenster?.EffektStarten();

    // ---------- Profile ----------

    /// <summary>
    /// Schreibt den jetzigen Zustand unter einem Namen fest: Modus, gemeinsames
    /// Video und die Zuweisungen je Bildschirm. Ein vorhandener Name wird
    /// ueberschrieben.
    /// </summary>
    public void ProfilSpeichern(string name)
    {
        Einstellungen.Profile[name] = new Settings.Profil
        {
            Bildschirm = Einstellungen.Bildschirm,
            Video = _original ?? Einstellungen.LetztesVideo,
            JeBildschirm = new Dictionary<string, string>(Einstellungen.VideoJeBildschirm)
        };
        Einstellungen.Speichern();
        Hintergrund.Notiz($"Profil gespeichert, {Einstellungen.Profile[name].JeBildschirm.Count} Zuweisung(en), "
            + $"{Einstellungen.Profile.Count} Profil(e) insgesamt");
    }

    /// <summary>
    /// Holt ein Profil zurueck. Fehlende Videodateien werden dabei uebergangen,
    /// sonst stuende ein Profil nach dem Aufraeumen des Videoordners fuer immer
    /// auf einer Datei, die es nicht mehr gibt.
    /// </summary>
    public bool ProfilLaden(string name)
    {
        if (!Einstellungen.Profile.TryGetValue(name, out var p)) return false;

        Einstellungen.Bildschirm = p.Bildschirm;
        Einstellungen.VideoJeBildschirm = p.JeBildschirm
            .Where(e => File.Exists(e.Value))
            .ToDictionary(e => e.Key, e => e.Value);
        Einstellungen.Speichern();

        int weg = p.JeBildschirm.Count - Einstellungen.VideoJeBildschirm.Count;
        Hintergrund.Notiz($"Profil geladen, {Einstellungen.VideoJeBildschirm.Count} Zuweisung(en)"
            + (weg > 0 ? $", {weg} uebergangen weil die Datei fehlt" : ""));

        // Das gemeinsame Video mitnehmen, wenn es noch da ist; sonst nur neu
        // aufbauen, damit wenigstens die Zuweisungen greifen.
        if (!string.IsNullOrWhiteSpace(p.Video) && File.Exists(p.Video)) HintergrundSetzen(p.Video!);
        else HintergrundNeuAufbauen();
        return true;
    }

    public void ProfilLoeschen(string name)
    {
        if (!Einstellungen.Profile.Remove(name)) return;
        Einstellungen.Speichern();
        Hintergrund.Notiz($"Profil geloescht, {Einstellungen.Profile.Count} uebrig");
    }

    public void AkkuRegelAnwenden(bool an)
    {
        if (_wallpaper is not null) _wallpaper.BeiAkkuPausieren = an;
    }

    /// <summary>
    /// Die Einstellungen fuer Bild und Ton als mpv-Schalter. Zahlen immer mit
    /// englischem Punkt: Auf einem deutschen Windows machte ToString() sonst
    /// "1,25" daraus, und mpv nimmt das nicht an.
    /// </summary>
    internal static List<string> MpvSchalter(Settings Einstellungen)
    {
        var schalter = new List<string>
        {
            // Ohne Ton gar keine Tonausgabe oeffnen. Mit --volume=0 meldete sich mpv
            // trotzdem bei WASAPI an, laut Protokoll vom 02.09.2026: eine
            // Tonsitzung je Bildschirm, die im Lautstaerkemischer auftaucht und
            // nichts tut.
            Einstellungen.Lautstaerke == 0 ? "--no-audio" : "--volume=" + Einstellungen.Lautstaerke,
            "--speed=" + (Einstellungen.TempoProzent / 100.0).ToString(CultureInfo.InvariantCulture),
        };
        if (Einstellungen.Helligkeit != 0) schalter.Add("--brightness=" + Einstellungen.Helligkeit);
        if (Einstellungen.Saettigung != 0) schalter.Add("--saturation=" + Einstellungen.Saettigung);
        if (Einstellungen.Hdr)
        {
            // Ob Windows die HDR-Ausgabe fuer ein Fenster unter dem Desktop
            // annimmt, weiss niemand. Deshalb schreibt mpv hier sein eigenes
            // Protokoll mit: Darin steht, was die Ausgabe tatsaechlich gemacht
            // hat, und nicht nur, dass der Schalter gesetzt war.
            schalter.Add("--target-colorspace-hint=yes");
            // Zehn Bit je Farbe. Ohne das waehlte mpv R8G8B8A8, und acht Bit mit
            // der HDR-Kennlinie ergeben sichtbare Stufen in Verlaeufen. Belegt am
            // 02.09.2026 im mpv-Protokoll: "Selected swapchain format
            // R8G8B8A8_UNORM".
            schalter.Add("--d3d11-output-format=rgb10_a2");
            schalter.Add("--msg-level=vo=v");
            schalter.Add("--log-file=" + HdrProtokoll);
        }
        return schalter;
    }

    /// <summary>
    /// Fuehrt die beiden Quellen fuer "welches Video auf welchem Schirm" zusammen.
    /// Die feste Zuweisung per Rechtsklick gewinnt gegen die Verteilung des
    /// Karussells: Wer ein Video ausdruecklich auf einen Schirm legt, will es dort
    /// behalten, auch waehrend das Karussell laeuft.
    /// </summary>
    internal static Dictionary<string, string> VerteilungMischen(
        IReadOnlyDictionary<string, string>? karussell,
        IReadOnlyDictionary<string, string> fest)
    {
        var roh = new Dictionary<string, string>();
        if (karussell is not null)
            foreach (var (schirm, video) in karussell) roh[schirm] = video;
        foreach (var (schirm, video) in fest) roh[schirm] = video;
        return roh;
    }

    /// <summary>
    /// Das eigene Protokoll von mpv, das nur bei eingeschaltetem HDR entsteht.
    /// An einer Stelle festgelegt, weil sonst der Pfad zum Schreiben und der zum
    /// Aufraeumen auseinanderlaufen koennen.
    /// </summary>
    internal static string HdrProtokoll => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Tapete", "mpv-hdr.txt");

    /// <summary>
    /// Raeumt das HDR-Protokoll weg, wenn HDR ausgeschaltet wird. Sonst bleibt es
    /// als Waise liegen und verwirrt beim naechsten Hineinschauen: eine
    /// HDR-Datei, obwohl HDR aus ist.
    ///
    /// Erst nach dem Neuaufbau aufrufen, sonst haelt der alte mpv sie noch offen.
    /// </summary>
    internal static void HdrProtokollAufraeumen()
    {
        try
        {
            if (!File.Exists(HdrProtokoll)) return;
            File.Delete(HdrProtokoll);
            Hintergrund.Notiz("HDR aus, mpv-hdr.txt weggeraeumt");
        }
        catch (Exception e)
        {
            Hintergrund.Notiz($"mpv-hdr.txt liess sich nicht loeschen: {e.GetType().Name}: {e.Message}");
        }
    }

    /// <summary>
    /// Rechenprobe fuer die Schalterliste. Der eigentliche Grund ist das
    /// Dezimalzeichen: Auf einem deutschen Windows macht ToString() aus 1,25 ein
    /// "1,25", und mpv lehnt das ab - ein Fehler, den man erst am schwarzen Bild
    /// merkt. Laeuft beim Start mit den beiden anderen Proben mit.
    /// </summary>
    internal static List<string> SchalterProbe()
    {
        var fehler = new List<string>();
        var s = MpvSchalter(new Settings
        {
            TempoProzent = 125, Lautstaerke = 40, Helligkeit = -20, Saettigung = 0
        });
        if (!s.Contains("--speed=1.25")) fehler.Add("Tempo falsch: " + string.Join(" ", s));
        if (!s.Contains("--volume=40")) fehler.Add("Lautstaerke fehlt");
        if (!MpvSchalter(new Settings()).Contains("--no-audio")) fehler.Add("Ohne Ton fehlt --no-audio");
        if (!s.Contains("--brightness=-20")) fehler.Add("Helligkeit fehlt");
        if (s.Any(x => x.StartsWith("--saturation"))) fehler.Add("Saettigung 0 sollte gar nicht auftauchen");

        // Die Rangfolge der beiden Verteilungsquellen. Kippt sie, wandert ein fest
        // zugewiesenes Video beim naechsten Karussellwechsel unbemerkt weg.
        var gemischt = VerteilungMischen(
            new Dictionary<string, string> { ["A"] = "karussell.mp4", ["B"] = "zwei.mp4" },
            new Dictionary<string, string> { ["A"] = "fest.mp4" });
        if (gemischt.GetValueOrDefault("A") != "fest.mp4") fehler.Add("Feste Zuweisung verliert gegen das Karussell");
        if (gemischt.GetValueOrDefault("B") != "zwei.mp4") fehler.Add("Karussell-Zuweisung ohne feste ging verloren");

        // Der Filter fuers Protokoll. Geht er kaputt, wandert der Windows-Name
        // des Testers in eine Datei, die er mir schickt.
        string heim = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string probe = Hintergrund.Entpersonalisieren(
            "Startvideo: " + heim + @"\Videos\Tapeten\urlaub-am-see.mp4 fehlt");
        if (probe.Contains(heim, StringComparison.OrdinalIgnoreCase)) fehler.Add("Heimatordner steht noch im Protokoll");
        if (!probe.Contains("urlaub-am-see.mp4")) fehler.Add("Der Videoname sollte stehen bleiben");
        if (!probe.StartsWith("Startvideo: ") || !probe.EndsWith(" fehlt"))
            fehler.Add("Der Filter frisst zu viel: " + probe);

        return fehler;
    }

    // ---------- Infobereich ----------

    private void TrayAufbauen()
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Fenster anzeigen", null, (_, _) => FensterZeigen());

        // Der eigentliche Weg zum Spielmodus: Rechtsklick neben der Uhr, ein Klick.
        // Dafuer muss das Fenster nicht geoeffnet werden - und wer gleich spielen
        // will, hat es nicht offen.
        _spielEintrag = new Forms.ToolStripMenuItem("Spielmodus", null,
            (_, _) => SpielmodusUmschalten())
        { Checked = Einstellungen.Spielmodus };
        menu.Items.Add(_spielEintrag);

        menu.Items.Add("Naechstes Video", null, (_, _) => KarussellWeiter());
        menu.Items.Add("Hintergrund aus", null, (_, _) => HintergrundAus());
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Beenden", null, (_, _) => Beenden());

        _tray = new Forms.NotifyIcon
        {
            Icon = EigenesIcon(),
            Visible = true,
            Text = "Tapete",
            ContextMenuStrip = menu
        };
        _tray.DoubleClick += (_, _) => FensterZeigen();
    }

    private static Icon EigenesIcon()
    {
        try
        {
            string? exe = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(exe))
            {
                var ico = Icon.ExtractAssociatedIcon(exe);
                if (ico is not null) return ico;
            }
        }
        catch { }
        return SystemIcons.Application;
    }

    private void TrayTextSetzen()
    {
        if (_tray is null) return;
        string t = Einstellungen.Spielmodus ? "Tapete – Spielmodus"
            : AktuellesVideo is null ? "Tapete – aus"
            : "Tapete – " + Path.GetFileName(AktuellesVideo);
        _tray.Text = t.Length > 63 ? t[..60] + "..." : t;   // Windows erlaubt nur 63 Zeichen
    }

    private void FensterZeigen()
    {
        if (_fenster is null) return;
        _fenster.Show();
        _fenster.WindowState = WindowState.Normal;
        _fenster.Activate();
        _fenster.Neuladen();
    }

    private void Beenden()
    {
        HintergrundAus(merken: false);
        if (_tray is not null) { _tray.Visible = false; _tray.Dispose(); }
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _spielWacht?.Stop();
        _karussellUhr?.Stop();
        if (_quelle is not null)
        {
            try { Native.UnregisterHotKey(_quelle.Handle, HotkeyKennung); } catch { }
            try { Native.UnregisterHotKey(_quelle.Handle, HotkeyWechseln); } catch { }
            _quelle.RemoveHook(Fensternachricht);
        }
        if (_tray is not null) { _tray.Visible = false; _tray.Dispose(); }
        base.OnExit(e);
    }
}
