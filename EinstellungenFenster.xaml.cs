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

        StufenFuellen(HelligkeitWahl, Bildstufen, Programm.Einstellungen.Helligkeit, 0);
        StufenFuellen(SaettigungWahl, Bildstufen, Programm.Einstellungen.Saettigung, 0);
        StufenFuellen(TempoWahl, Tempostufen, Programm.Einstellungen.TempoProzent, 100);
        StufenFuellen(LautstaerkeWahl, Lautstufen, Programm.Einstellungen.Lautstaerke, 0);

        ProfileFuellen();
        HdrSchalter.IsChecked = Programm.Einstellungen.Hdr;
        AkkuSchalter.IsChecked = Programm.Einstellungen.BeiAkkuPausieren;
        PauseSchalter.IsChecked = Programm.Einstellungen.BeiVollbildPausieren;
        BildrateSchalter.IsChecked = Programm.Einstellungen.BildrateHalbieren;
        KarussellSchalter.IsChecked = Programm.Einstellungen.KarussellAn;
        AutostartSchalter.IsChecked = Settings.Autostart;
        SchonerSchalter.IsChecked = Bildschirmschoner.Eingetragen;
        SchonerStandZeigen();

        StandZeigen();
        _laedt = false;
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
    private static void StufenFuellen(ComboBox liste, Stufe[] stufen, int wert, int normal)
    {
        foreach (var st in stufen) liste.Items.Add(st);
        liste.SelectedItem = stufen.FirstOrDefault(st => st.Wert == wert)
            ?? stufen.First(st => st.Wert == normal);
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

    private void HelligkeitGewaehlt(object sender, SelectionChangedEventArgs e)
        => BildwertUebernehmen(HelligkeitWahl, w => Programm.Einstellungen.Helligkeit = w);

    private void SaettigungGewaehlt(object sender, SelectionChangedEventArgs e)
        => BildwertUebernehmen(SaettigungWahl, w => Programm.Einstellungen.Saettigung = w);

    private void TempoGewaehlt(object sender, SelectionChangedEventArgs e)
        => BildwertUebernehmen(TempoWahl, w => Programm.Einstellungen.TempoProzent = w);

    private void LautstaerkeGewaehlt(object sender, SelectionChangedEventArgs e)
        => BildwertUebernehmen(LautstaerkeWahl, w => Programm.Einstellungen.Lautstaerke = w);

    /// <summary>
    /// Alle vier Werte gehen als Schalter an mpv und wirken erst beim Aufbau,
    /// deshalb jedes Mal ein Neuaufbau. Der kostet rund eine halbe Sekunde
    /// schwarzes Bild, weshalb es Stufen sind und kein Regler.
    /// </summary>
    private void BildwertUebernehmen(ComboBox liste, Action<int> setzen)
    {
        if (_laedt || liste.SelectedItem is not Stufe st) return;
        setzen(st.Wert);
        Programm.Einstellungen.Speichern();
        Programm.HintergrundNeuAufbauen();
    }

    private void HdrGeaendert(object sender, RoutedEventArgs e)
    {
        if (_laedt) return;
        Programm.Einstellungen.Hdr = HdrSchalter.IsChecked == true;
        Programm.Einstellungen.Speichern();
        Programm.HintergrundNeuAufbauen();
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
