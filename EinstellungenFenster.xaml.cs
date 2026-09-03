using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Tapete;

/// <summary>
/// Alle Einstellungen an einer Stelle. Vorher standen sie in der Fusszeile des
/// Hauptfensters, wo mit dem Karussell kein Platz mehr war.
/// </summary>
public partial class EinstellungenFenster : Window
{
    private App Programm => (App)Application.Current;

    // Solange true, sind die Ereignisse stumm. Sonst schreibt schon das Setzen des
    // Anfangszustands die Einstellung zurueck. Dieselbe Falle wie im Hauptfenster,
    // siehe MainWindow._laedt.
    private bool _laedt = true;

    /// <summary>
    /// Standzeiten zur Auswahl statt eines Eingabefelds. Eine Liste kann man nicht
    /// falsch ausfuellen, ein Feld schon - und die Untergrenze von einer Minute
    /// hat einen Grund: Jeder Wechsel beendet mpv und startet es neu, was rund
    /// eine halbe Sekunde schwarzes Bild kostet.
    /// </summary>
    private static readonly int[] Minuten = [1, 2, 5, 10, 15, 30, 60, 120, 240];

    public EinstellungenFenster()
    {
        InitializeComponent();

        BildschirmeFuellen();

        foreach (int m in Minuten) MinutenWahl.Items.Add(new MinutenEintrag(m));
        MinutenWahl.SelectedItem = MinutenWahl.Items.Cast<MinutenEintrag>()
            .FirstOrDefault(e => e.Wert == Programm.Einstellungen.KarussellMinuten)
            ?? MinutenWahl.Items.Cast<MinutenEintrag>().First(e => e.Wert == 15);

        ReihenfolgeWahl.Items.Add("Gemischt");
        ReihenfolgeWahl.Items.Add("Der Reihe nach");
        ReihenfolgeWahl.SelectedIndex = Programm.Einstellungen.KarussellZufaellig ? 0 : 1;

        ReglerRichten(HelligkeitRegler, HelligkeitWert, Bildstufen,
                      Programm.Einstellungen.Helligkeit, w => Programm.Einstellungen.Helligkeit = w);
        ReglerRichten(SaettigungRegler, SaettigungWert, Bildstufen,
                      Programm.Einstellungen.Saettigung, w => Programm.Einstellungen.Saettigung = w);
        ReglerRichten(KontrastRegler, KontrastWert, Bildstufen,
                      Programm.Einstellungen.Kontrast, w => Programm.Einstellungen.Kontrast = w);
        ReglerRichten(GammaRegler, GammaWert, Bildstufen,
                      Programm.Einstellungen.Gamma, w => Programm.Einstellungen.Gamma = w);
        ReglerRichten(TempoRegler, TempoWert, Tempostufen,
                      Programm.Einstellungen.TempoProzent, w => Programm.Einstellungen.TempoProzent = w);
        ReglerRichten(LautstaerkeRegler, LautstaerkeWert, Lautstufen,
                      Programm.Einstellungen.Lautstaerke, w => Programm.Einstellungen.Lautstaerke = w);

        KopfFassung.Text = "v" + Aktualisierung.Eigene;
        string? laeuft = Programm.AktuellesVideo;
        KopfVideo.Text = laeuft is null ? "kein Video" : "laeuft \u00b7 " + Path.GetFileName(laeuft);
        NavifussFuellen();

        foreach (var t in App.Themen) ThemaWahl.Items.Add(new ThemaEintrag(t.Datei, t.Name));
        ThemaWahl.SelectedItem = ThemaWahl.Items.Cast<ThemaEintrag>()
            .FirstOrDefault(t => t.Datei == Programm.Einstellungen.Thema)
            ?? ThemaWahl.Items.Cast<ThemaEintrag>().First();

        for (int st = 0; st < 24; st++) ZeitWahl.Items.Add($"{st:00}");
        for (int mi = 0; mi < 60; mi++) MinuteWahl.Items.Add($"{mi:00}");
        ZeitWahl.SelectedIndex = DateTime.Now.Hour;
        MinuteWahl.SelectedIndex = DateTime.Now.Minute;
        VideosFuellen(ZeitVideoWahl);
        ZeitplanStandZeigen();
        VideosFuellen(DesktopVideoWahl);
        DesktopStandZeigen();

        EffektSchalter.IsChecked = Programm.Einstellungen.Effekte;
        ProfileFuellen();
        HdrSchalter.IsChecked = Programm.Einstellungen.Hdr;
        AkkuSchalter.IsChecked = Programm.Einstellungen.BeiAkkuPausieren;
        PauseSchalter.IsChecked = Programm.Einstellungen.BeiVollbildPausieren;
        BildrateSchalter.IsChecked = Programm.Einstellungen.BildrateHalbieren;
        KarussellSchalter.IsChecked = Programm.Einstellungen.KarussellAn;
        ZeitplanSchalter.IsChecked = Programm.Einstellungen.ZeitplanAn;
        ProDesktopSchalter.IsChecked = Programm.Einstellungen.ProDesktopAn;
        AutostartSchalter.IsChecked = Settings.Autostart;
        SchonerSchalter.IsChecked = Bildschirmschoner.Eingetragen;
        SchonerStandZeigen();

        StandZeigen();
        _laedt = false;

        // Escape schliesst. Das Fenster hat keine Titelleiste von Windows, also
        // auch kein Alt+F4-Menue; ohne diese Zeile bleibt nur der Mausklick.
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { Close(); e.Handled = true; }
        };
    }

    /// <summary>Sagt, wie viele Videas angekreuzt sind. Ohne Haken tut das Karussell nichts.</summary>
    internal void StandZeigen()
    {
        int n = Programm.Einstellungen.KarussellPfade.Count();
        KarussellStand.Text = n switch
        {
            0 => "Zurzeit ist kein Video angekreuzt, es wechselt also nichts.",
            1 => "Ein Video ist angekreuzt. Zum Wechseln braucht es mindestens zwei.",
            _ => $"{n} Videos sind angekreuzt."
        };
    }

    // ---------- Bildschirm ----------

    private void BildschirmeFuellen()
    {
        BildschirmWahl.Items.Clear();
        BildschirmWahl.Items.Add(new BildschirmEintrag("*", "Jeder Bildschirm einzeln"));
        foreach (var b in Native.Bildschirme())
            BildschirmWahl.Items.Add(new BildschirmEintrag(b.Name, b.ToString()));

        // Eine leere Einstellung heisst Hauptbildschirm, und genau der muss auch in
        // der Liste stehen. Vorher fiel der Fall durch und die Liste zeigte einfach
        // den erstaufgezaehlten Schirm als gewaehlt an - der ist nicht der
        // Hauptbildschirm. Damit log die Liste zweimal: Sie behauptete einen
        // Schirm, der gar nicht lief, und wer genau diesen Eintrag anklickte,
        // loeste kein Ereignis aus, weil er schon ausgewaehlt war. Am 01.09.2026
        // von einem Tester als "egal welchen ich waehle, es bleibt der
        // Hauptmonitor" gemeldet.
        string? gewaehlt = Programm.Einstellungen.Bildschirm;
        if (string.IsNullOrEmpty(gewaehlt))
            gewaehlt = Native.Bildschirme().FirstOrDefault(s => s.Haupt)?.Name;

        var treffer = BildschirmWahl.Items.Cast<BildschirmEintrag>().FirstOrDefault(x => x.Name == gewaehlt);
        BildschirmWahl.SelectedItem = treffer
            ?? BildschirmWahl.Items.Cast<BildschirmEintrag>().Skip(1).FirstOrDefault()
            ?? BildschirmWahl.Items.Cast<BildschirmEintrag>().First();
    }

    private sealed record BildschirmEintrag(string Name, string Anzeige)
    {
        public override string ToString() => Anzeige;
    }

    /// <summary>
    /// Grobe Stufen statt eines Schiebereglers. Ein Regler wuerde bei jeder
    /// Zwischenstellung einen mpv-Neustart ausloesen, und feiner als das hier
    /// stellt auf einem Hintergrundvideo ohnehin niemand.
    /// </summary>
    private sealed record Stufe(int Wert, string Text)
    {
        public override string ToString() => Text;
    }

    private static readonly Stufe[] Bildstufen =
    [
        new(-40, "viel weniger"), new(-20, "weniger"), new(0, "normal"),
        new(20, "mehr"), new(40, "viel mehr")
    ];

    private static readonly Stufe[] Tempostufen =
    [
        new(50, "halbes Tempo"), new(75, "75 %"), new(100, "normal"),
        new(150, "anderthalbfach"), new(200, "doppeltes Tempo")
    ];

    private static readonly Stufe[] Lautstufen =
    [
        new(0, "aus"), new(25, "leise"), new(50, "mittel"),
        new(75, "laut"), new(100, "voll")
    ];

    /// <summary>
    /// Fuellt eine Liste und waehlt den gespeicherten Wert aus. Passt keiner,
    /// gilt der Normalwert; sonst zeigte die Liste etwas anderes an, als
    /// tatsaechlich laeuft - genau der Fehler aus Fassung 1.3.4.
    /// </summary>
    /// <summary>Was an einem Regler haengt: Stufen, Zahlenfeld, Ziel in den Einstellungen.</summary>
    private sealed record Reglerband(Stufe[] Stufen, TextBlock Anzeige, Action<int> Setzen);

    private readonly Dictionary<Slider, Reglerband> _regler = [];

    /// <summary>
    /// Wartet nach der letzten Reglerbewegung, bevor der Wert wirklich gesetzt wird.
    ///
    /// Bis 1.13.73 standen hier Auswahllisten, mit der Begruendung, ein Regler
    /// wuerde bei jeder Zwischenstellung einen mpv-Neustart ausloesen. Der Einwand
    /// stimmt, und hier ist er beantwortet: Der Regler rastet auf denselben fuenf
    /// Stufen ein wie die Liste vorher, und gesetzt wird erst, wenn er eine halbe
    /// Sekunde stillsteht. Wer von ganz links nach ganz rechts zieht, loest damit
    /// einen Neustart aus statt vier. Dasselbe Muster wie beim Zeichenregen, siehe
    /// MainWindow.
    /// </summary>
    private System.Windows.Threading.DispatcherTimer? _reglerWacht;
    private Slider? _reglerOffen;

    /// <summary>
    /// Setzt einen Regler auf den gespeicherten Wert und merkt sich, was an ihm haengt.
    ///
    /// Gestellt wird ueber den Index der Stufe, nicht ueber den Zahlenwert: Beim
    /// Tempo liegen die Stufen ungleich weit auseinander (50, 75, 100, 150, 200),
    /// und ein gleichmaessiges Raster kaeme damit nicht hin.
    /// </summary>
    private void ReglerRichten(Slider regler, TextBlock anzeige, Stufe[] stufen,
                               int wert, Action<int> setzen)
    {
        _regler[regler] = new Reglerband(stufen, anzeige, setzen);
        regler.Maximum = stufen.Length - 1;

        int stelle = Array.FindIndex(stufen, st => st.Wert == wert);
        if (stelle < 0) stelle = Array.FindIndex(stufen, st => st.Wert == 0);
        if (stelle < 0) stelle = stufen.Length / 2;

        regler.Value = stelle;
        anzeige.Text = Wertetext(stufen, stufen[stelle]);
    }

    /// <summary>
    /// Zahl und Wort nebeneinander: die Zahl fuer die Genauigkeit, das Wort fuer
    /// die Bedeutung.
    ///
    /// Ein Pluszeichen bekommt nur, wer auch negativ werden kann. Bei Helligkeit
    /// heisst "+20" etwas, bei Tempo stand am 03.09.2026 "+100 normal" im Fenster,
    /// und das ist Unsinn: Hundert Prozent sind dort der Normalwert, kein Zuschlag.
    /// </summary>
    private static string Wertetext(Stufe[] stufen, Stufe stufe)
    {
        bool kannNegativ = stufen.Any(s => s.Wert < 0);
        string zahl = kannNegativ && stufe.Wert > 0
            ? "+" + stufe.Wert
            : stufe.Wert.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return zahl + "  " + stufe.Text;
    }

    /// <summary>
    /// Ein Regler wurde bewegt. Die Zahl daneben folgt sofort, der Wert erst nach
    /// einer halben Sekunde Ruhe.
    /// </summary>
    private void ReglerGeaendert(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (sender is not Slider regler || !_regler.TryGetValue(regler, out var band)) return;

        int stelle = Math.Clamp((int)Math.Round(regler.Value), 0, band.Stufen.Length - 1);
        band.Anzeige.Text = Wertetext(band.Stufen, band.Stufen[stelle]);
        if (_laedt) return;

        _reglerOffen = regler;
        _reglerWacht ??= Wachtbauen();
        _reglerWacht.Stop();
        _reglerWacht.Start();
    }

    private System.Windows.Threading.DispatcherTimer Wachtbauen()
    {
        var uhr = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(500)
        };
        uhr.Tick += (_, _) =>
        {
            uhr.Stop();
            if (_reglerOffen is not Slider r || !_regler.TryGetValue(r, out var band)) return;

            int stelle = Math.Clamp((int)Math.Round(r.Value), 0, band.Stufen.Length - 1);
            band.Setzen(band.Stufen[stelle].Wert);
            Programm.Einstellungen.Speichern();
            Programm.HintergrundNeuAufbauen();
        };
        return uhr;
    }

    /// <summary>Die drei Zeilen unten in der Bereichsspalte. Echte Zahlen, keine Zierschrift.</summary>
    private void NavifussFuellen()
    {
        int videos = 0;
        try
        {
            videos = Directory.EnumerateFiles(Settings.VideoOrdner)
                              .Count(Hintergrund.IstUnterstuetzt);
        }
        catch { /* Ordner fehlt, dann bleibt es bei null */ }

        int schirme = Math.Max(1, Native.Bildschirme().Count);
        Bereiche.Tag = $"{videos} Videos\n{schirme} Bildschirm" + (schirme == 1 ? "" : "e")
                     + $"\n{Programm.Einstellungen.Zeitplan.Count} Zeitpunkte";
    }

    private sealed record MinutenEintrag(int Wert)
    {
        public override string ToString() => Wert switch
        {
            1 => "1 Minute",
            60 => "1 Stunde",
            120 => "2 Stunden",
            240 => "4 Stunden",
            _ => $"{Wert} Minuten"
        };
    }

    private void BildschirmGewaehlt(object sender, SelectionChangedEventArgs e)
    {
        // Bis 1.3.4 meldete diese Stelle gar nichts. Bei einem Tester blieb die
        // Einstellung ueber Stunden auf dem Hauptbildschirm stehen, und aus dem
        // Protokoll war nicht zu sehen, ob er ueberhaupt etwas angeklickt hat oder
        // ob das Ereignis ausbleibt. Genau das sagen die drei Zeilen jetzt.
        if (_laedt) { Hintergrund.Notiz("Bildschirmwahl: waehrend des Aufbaus, ignoriert"); return; }
        if (BildschirmWahl.SelectedItem is not BildschirmEintrag b)
        {
            Hintergrund.Notiz("Bildschirmwahl: nichts ausgewaehlt");
            return;
        }
        Hintergrund.Notiz($"Bildschirmwahl: {b.Name} gewaehlt");
        Programm.Einstellungen.Bildschirm = b.Name;
        Programm.Einstellungen.Speichern();
        Programm.HintergrundNeuAufbauen();
    }

    // ---------- Schalter ----------

    private void PauseGeaendert(object sender, RoutedEventArgs e)
    {
        if (_laedt) return;
        bool an = PauseSchalter.IsChecked == true;
        Programm.Einstellungen.BeiVollbildPausieren = an;
        Programm.Einstellungen.Speichern();
        Programm.PauseRegelAnwenden(an);
    }

    private void BildrateGeaendert(object sender, RoutedEventArgs e)
    {
        if (_laedt) return;
        Programm.Einstellungen.BildrateHalbieren = BildrateSchalter.IsChecked == true;
        Programm.Einstellungen.Speichern();
        Programm.HintergrundNeuAufbauen();
    }

    private void AutostartGeaendert(object sender, RoutedEventArgs e)
    {
        if (_laedt) return;
        Settings.Autostart = AutostartSchalter.IsChecked == true;
    }

    // ---------- Karussell ----------

    private void KarussellGeaendert(object sender, RoutedEventArgs e)
    {
        if (_laedt) return;
        Programm.Einstellungen.KarussellAn = KarussellSchalter.IsChecked == true;
        Programm.Einstellungen.Speichern();
        Programm.KarussellNeuAufbauen();
    }

    private void MinutenGewaehlt(object sender, SelectionChangedEventArgs e)
    {
        if (_laedt) return;
        if (MinutenWahl.SelectedItem is not MinutenEintrag m) return;
        Programm.Einstellungen.KarussellMinuten = m.Wert;
        Programm.Einstellungen.Speichern();
        Programm.KarussellNeuAufbauen();
    }

    // Die sechs Einzelhandler sind am 03.09.2026 entfallen; alle Regler laufen
    // jetzt durch ReglerGeaendert, das den Zusammenhang aus _regler holt.

    // ---------- Zeitplan ----------

    /// <summary>Alle Videos aus dem Ordner zur Auswahl, nicht nur die angekreuzten.</summary>
    private static void VideosFuellen(ComboBox liste)
    {
        liste.Items.Clear();
        foreach (string name in Directory.EnumerateFiles(Settings.VideoOrdner)
                                         .Where(Hintergrund.IstUnterstuetzt)
                                         .Select(Path.GetFileName)
                                         .OfType<string>()
                                         .OrderBy(n => n, StringComparer.OrdinalIgnoreCase))
            liste.Items.Add(name);
        if (liste.Items.Count > 0) liste.SelectedIndex = 0;
    }

    /// <summary>Der ganze Zeitplan in einer Zeile, nach Uhrzeit sortiert.</summary>
    private void ZeitplanStandZeigen()
    {
        var plan = Programm.Einstellungen.Zeitplan;
        ZeitplanStand.Text = plan.Count == 0
            ? "Noch kein Eintrag."
            : string.Join("   ", plan.OrderBy(p => p.Key, StringComparer.Ordinal)
                                     .Select(p => p.Key + " " + p.Value));
    }

    private void ZeitplanGeaendert(object sender, RoutedEventArgs e)
    {
        if (_laedt) return;
        Programm.Einstellungen.ZeitplanAn = ZeitplanSchalter.IsChecked == true;
        Programm.Einstellungen.Speichern();
        if (Programm.Einstellungen.ZeitplanAn) Programm.ZeitplanAnwenden();
    }

    /// <summary>Die eingestellte Uhrzeit als "HH:mm", oder null.</summary>
    private string? GewaehlteZeit() =>
        ZeitWahl.SelectedItem is string st && MinuteWahl.SelectedItem is string mi
            ? st + ":" + mi
            : null;

    private void ZeitpunktEintragen(object sender, RoutedEventArgs e)
    {
        if (GewaehlteZeit() is not string zeit) return;
        if (ZeitVideoWahl.SelectedItem is not string video)
        {
            Hintergrund.Notiz($"Zeitplan eintragen: kein Video gewaehlt "
                            + $"({ZeitVideoWahl.Items.Count} in der Liste)");
            ZeitplanStand.Text = "Erst rechts ein Video auswählen.";
            return;
        }

        Programm.Einstellungen.Zeitplan[zeit] = video;
        Programm.Einstellungen.Speichern();
        Hintergrund.Notiz($"Zeitplan: {zeit} auf {video} gesetzt, "
                        + $"jetzt {Programm.Einstellungen.Zeitplan.Count} Eintraege");
        ZeitplanStandZeigen();

        // Bewusst nicht sofort anwenden. Ein Eintrag fuer elf Uhr soll um elf
        // greifen und nicht in dem Moment, in dem er angelegt wird.
    }

    private void ZeitpunktEntfernen(object sender, RoutedEventArgs e)
    {
        if (GewaehlteZeit() is not string zeit) return;
        if (!Programm.Einstellungen.Zeitplan.Remove(zeit))
        {
            ZeitplanStand.Text = "Für " + zeit + " ist nichts eingetragen.";
            return;
        }

        Programm.Einstellungen.Speichern();
        ZeitplanStandZeigen();
    }

    // ---------- Virtuelle Desktops ----------

    /// <summary>
    /// Was fuer den gerade offenen Desktop hinterlegt ist. Das Einstellungsfenster
    /// liegt selbst auf diesem Desktop, deshalb nennt Windows hier die richtige
    /// Kennung.
    /// </summary>
    private void DesktopStandZeigen()
    {
        var zuweisungen = Programm.Einstellungen.DesktopVideos;
        Guid? jetzt = Native.AktuellerDesktop();

        string hier;
        if (jetzt is null)
            hier = "Windows nennt für dieses Fenster gerade keinen Desktop.";
        else if (zuweisungen.TryGetValue(jetzt.Value.ToString(), out string? name))
            hier = "Diesem Desktop ist " + name + " zugewiesen.";
        else
            hier = "Diesem Desktop ist noch nichts zugewiesen.";

        DesktopStand.Text = zuweisungen.Count switch
        {
            0 => hier,
            1 => hier + " Insgesamt ein Desktop belegt.",
            _ => hier + $" Insgesamt {zuweisungen.Count} Desktops belegt."
        };
    }

    private void ProDesktopGeaendert(object sender, RoutedEventArgs e)
    {
        if (_laedt) return;
        Programm.Einstellungen.ProDesktopAn = ProDesktopSchalter.IsChecked == true;
        Programm.Einstellungen.Speichern();
    }

    /// <summary>
    /// Weist dem offenen Desktop das gewaehlte Video zu.
    ///
    /// Jeder Ausgang wird ins Protokoll geschrieben. Am 03.09.2026 meldete der
    /// Nutzer, beim Druecken passiere nichts, und die Einstellungsdatei enthielt
    /// danach trotzdem keine Zuweisung, obwohl das Fenster eine anzeigte. Ohne
    /// Protokoll liess sich nicht sagen, an welcher der drei Stellen es
    /// aussteigt.
    /// </summary>
    private void DesktopZuweisen(object sender, RoutedEventArgs e)
    {
        if (DesktopVideoWahl.SelectedItem is not string video)
        {
            Hintergrund.Notiz($"Desktop zuweisen: kein Video gewaehlt "
                            + $"({DesktopVideoWahl.Items.Count} in der Liste)");
            DesktopStand.Text = "Erst oben ein Video auswählen.";
            return;
        }
        if (Native.AktuellerDesktop() is not Guid jetzt)
        {
            Hintergrund.Notiz("Desktop zuweisen: kein Desktop ermittelbar");
            DesktopStand.Text = "Windows nennt gerade keinen virtuellen Desktop.";
            return;
        }

        Programm.Einstellungen.DesktopVideos[jetzt.ToString()] = video;
        Programm.Einstellungen.Speichern();
        Hintergrund.Notiz($"Desktop {jetzt.ToString()[..8]}: {video} zugewiesen, "
                        + $"jetzt {Programm.Einstellungen.DesktopVideos.Count} Zuweisungen");
        DesktopStandZeigen();

        // Sofort zeigen, was zugewiesen wurde. Vorher geschah bis zum naechsten
        // Desktopwechsel nichts sichtbares, und das sah nach einem Fehler aus.
        Programm.DesktopSofortAnwenden();
    }

    private void DesktopLoesen(object sender, RoutedEventArgs e)
    {
        if (Native.AktuellerDesktop() is not Guid jetzt) return;
        if (!Programm.Einstellungen.DesktopVideos.Remove(jetzt.ToString())) return;

        Programm.Einstellungen.Speichern();
        DesktopStandZeigen();
    }

    private void HdrGeaendert(object sender, RoutedEventArgs e)
    {
        if (_laedt) return;
        bool an = HdrSchalter.IsChecked == true;
        Programm.Einstellungen.Hdr = an;
        Programm.Einstellungen.Speichern();
        Programm.HintergrundNeuAufbauen();
        // Nach dem Neuaufbau, nicht davor: Der alte Abspieler haelt die Datei offen.
        if (!an) App.HdrProtokollAufraeumen();
    }

    /// <summary>
    /// Anders als die Bildwerte liest der Takt das erst zwei Sekunden spaeter,
    /// ein Neuaufbau ist dafuer nicht noetig.
    /// </summary>
    private void AkkuGeaendert(object sender, RoutedEventArgs e)
    {
        if (_laedt) return;
        bool an = AkkuSchalter.IsChecked == true;
        Programm.Einstellungen.BeiAkkuPausieren = an;
        Programm.Einstellungen.Speichern();
        Programm.AkkuRegelAnwenden(an);
    }

    private void SchonerStandZeigen()
    {
        if (!Bildschirmschoner.Eingetragen) { SchonerStand.Text = ""; return; }
        int s = Bildschirmschoner.WartezeitSekunden;
        SchonerStand.Text = s <= 0
            ? "Eingetragen, aber in Windows ist keine Wartezeit gesetzt."
            : $"Geht nach {s / 60} Minuten ohne Eingabe an.";
    }

    private void SchonerGeaendert(object sender, RoutedEventArgs e)
    {
        if (_laedt) return;
        bool an = SchonerSchalter.IsChecked == true;
        if (!Bildschirmschoner.Eintragen(an))
        {
            _laedt = true;
            SchonerSchalter.IsChecked = !an;
            _laedt = false;
            SchonerStand.Text = "Hat nicht geklappt, der Grund steht im Protokoll.";
            return;
        }
        SchonerStandZeigen();
    }

    private sealed record ThemaEintrag(string Datei, string Name)
    {
        public override string ToString() => Name;
    }

    private void EffekteGeaendert(object sender, RoutedEventArgs e)
    {
        if (_laedt) return;
        Programm.Einstellungen.Effekte = EffektSchalter.IsChecked == true;
        Programm.Einstellungen.Speichern();
        Programm.EffektNeuStarten();
    }

    private void ThemaGewaehlt(object sender, SelectionChangedEventArgs e)
    {
        if (_laedt || ThemaWahl.SelectedItem is not ThemaEintrag t) return;
        Programm.Einstellungen.Thema = t.Datei;
        Programm.Einstellungen.Speichern();
        Programm.ThemaAnwenden(t.Datei);
    }

    // ---------- Profile ----------

    private void ProfileFuellen()
    {
        bool vorher = _laedt;
        _laedt = true;
        ProfilWahl.Items.Clear();
        foreach (string n in Programm.Einstellungen.Profile.Keys.OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase))
            ProfilWahl.Items.Add(n);
        _laedt = vorher;

        int n2 = ProfilWahl.Items.Count;
        ProfilStand.Text = n2 switch
        {
            0 => "Noch kein Profil gespeichert.",
            1 => "Ein Profil gespeichert.",
            _ => $"{n2} Profile gespeichert."
        };
    }

    /// <summary>
    /// Der eingetippte oder gewaehlte Name. Bei einer editierbaren Liste steht der
    /// getippte Text in Text, der angeklickte Eintrag in SelectedItem; wer nur eines
    /// von beiden liest, verliert je nach Bedienweg die Eingabe.
    /// </summary>
    private string ProfilName =>
        (ProfilWahl.SelectedItem as string ?? ProfilWahl.Text ?? "").Trim();

    private void ProfilGewaehlt(object sender, SelectionChangedEventArgs e)
    {
        if (_laedt || ProfilWahl.SelectedItem is not string name) return;
        if (!Programm.ProfilLaden(name)) return;
        BildschirmeFuellen();
        ProfilStand.Text = $"Profil „{name}“ geladen.";
    }

    private void ProfilSpeichern(object sender, RoutedEventArgs e)
    {
        string name = ProfilName;
        if (name.Length == 0) { ProfilStand.Text = "Erst einen Namen eintippen."; return; }
        Programm.ProfilSpeichern(name);
        ProfileFuellen();
        ProfilWahl.Text = name;
        ProfilStand.Text = $"Profil „{name}“ gespeichert.";
    }

    private void ProfilLoeschen(object sender, RoutedEventArgs e)
    {
        string name = ProfilName;
        if (name.Length == 0 || !Programm.Einstellungen.Profile.ContainsKey(name))
        {
            ProfilStand.Text = "Kein Profil dieses Namens.";
            return;
        }
        Programm.ProfilLoeschen(name);
        ProfileFuellen();
        ProfilWahl.Text = "";
        ProfilStand.Text = $"Profil „{name}“ gelöscht.";
    }

    private void ReihenfolgeGewaehlt(object sender, SelectionChangedEventArgs e)
    {
        if (_laedt) return;
        Programm.Einstellungen.KarussellZufaellig = ReihenfolgeWahl.SelectedIndex == 0;
        Programm.Einstellungen.Speichern();
        Programm.KarussellNeuAufbauen();
    }

    // ---------- Fenster ----------

    private void TitelZiehen(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    private void Schliessen(object sender, RoutedEventArgs e) => Close();
}
