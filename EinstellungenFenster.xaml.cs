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

        PauseSchalter.IsChecked = Programm.Einstellungen.BeiVollbildPausieren;
        BildrateSchalter.IsChecked = Programm.Einstellungen.BildrateHalbieren;
        KarussellSchalter.IsChecked = Programm.Einstellungen.KarussellAn;
        AutostartSchalter.IsChecked = Settings.Autostart;

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
        if (_laedt) return;
        if (BildschirmWahl.SelectedItem is not BildschirmEintrag b) return;
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
