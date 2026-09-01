using System.Drawing;
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

        // Zweiter Start bringt nur das vorhandene Fenster nach vorn.
        _einzelInstanz = new Mutex(initiallyOwned: true, "Tapete_EinzelInstanz_9f2c", out bool neu);
        if (!neu) { Hintergrund.Notiz("ENDE: schon eine Instanz da"); Shutdown(); return; }

        Einstellungen = Settings.Laden();
        Hintergrund.Notiz("Einstellungen geladen. LetztesVideo=" + (Einstellungen.LetztesVideo ?? "null"));

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
        if (_wallpaper is not { Laeuft: true }) NachreichenStarten();
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
        Hintergrund.Notiz("Karussell: weiter zu " + Path.GetFileName(naechstes));
        HintergrundSetzen(naechstes);
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
        _spielAutomatisch = false;
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
                Spielmodus = true;
            }
            else if (!spielt && Spielmodus && _spielAutomatisch)
            {
                Hintergrund.Notiz("Vollbildanwendung beendet");
                _spielAutomatisch = false;
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

    public void HintergrundSetzen(string pfad)
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
        _wallpaper = new Hintergrund(abspielen, Einstellungen.Bildschirm,
            text => { problem = text; _fenster?.FehlerZeigen(text); })
        {
            BeiVollbildPausieren = Einstellungen.BeiVollbildPausieren
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
        if (!string.IsNullOrWhiteSpace(pfad) && File.Exists(pfad)) HintergrundSetzen(pfad);
    }

    public void PauseRegelAnwenden(bool an)
    {
        if (_wallpaper is not null) _wallpaper.BeiVollbildPausieren = an;
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
