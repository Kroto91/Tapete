using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.VisualBasic.FileIO;
using Microsoft.Win32;

namespace Tapete;

public partial class MainWindow : Window
{
    private readonly ObservableCollection<VideoItem> _items = new();
    private App Programm => (App)Application.Current;

    // Solange true, sind die Schalter-Ereignisse stumm. Sonst schreibt schon das
    // Setzen des Anfangszustands im Konstruktor die Einstellung und die Registry
    // zurueck - und wenn dabei etwas schiefgeht, steht der Schalter stillschweigend
    // auf aus. Genau so war die Pause am 31.08.2026 abgeschaltet.
    private bool _laedt = true;

    public MainWindow()
    {
        InitializeComponent();
        Liste.ItemsSource = _items;

        BildschirmeFuellen();
        PauseSchalter.IsChecked = Programm.Einstellungen.BeiVollbildPausieren;
        BildrateSchalter.IsChecked = Programm.Einstellungen.BildrateHalbieren;
        TitelText.Text = $"Tapete {Aktualisierung.Eigene}";
        AutostartSchalter.IsChecked = Settings.Autostart;

        DragEnter += (_, e) => { if (HatVideos(e)) ZiehFlaeche.Visibility = Visibility.Visible; };
        DragLeave += (_, _) => ZiehFlaeche.Visibility = Visibility.Collapsed;
        DragOver += (_, e) =>
        {
            e.Effects = HatVideos(e) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        };
        Drop += Abgelegt;

        Neuladen();
        _laedt = false;

        // Gesucht wird in App, nicht hier. Sonst liefe die Abfrage zweimal, und
        // im Autostart haenge die Aktualisierung an einem Fenster, das nie
        // gezeigt wird.
        Programm.NeuigkeitGefunden += NeuigkeitZeigen;
        NeuigkeitZeigen();
    }

    // ---------- Liste ----------

    public void Neuladen()
    {
        string ordner = Settings.VideoOrdner;
        var dateien = Directory.EnumerateFiles(ordner)
                               .Where(Hintergrund.IstUnterstuetzt)
                               .OrderBy(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase)
                               .ToList();

        _items.Clear();
        foreach (var f in dateien) _items.Add(new VideoItem(f));

        Leer.Visibility = _items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        VorschauBilderLaden();
        StandAktualisieren();
    }

    /// <summary>Vorschaubilder auf einem eigenen Faden holen, damit die Oberflaeche fluessig bleibt.</summary>
    private void VorschauBilderLaden()
    {
        var kopie = _items.ToList();
        var faden = new Thread(() =>
        {
            foreach (var item in kopie)
            {
                var bild = Thumbs.Get(item.Pfad, 480);
                if (bild is null) continue;
                Dispatcher.BeginInvoke(() => item.Bild = bild);
            }
        });
        faden.SetApartmentState(ApartmentState.STA);
        faden.IsBackground = true;
        faden.Start();
    }

    public void StandAktualisieren()
    {
        string? aktiv = Programm.AktuellesVideo;
        foreach (var i in _items) i.Laeuft = aktiv is not null &&
            string.Equals(i.Pfad, aktiv, StringComparison.OrdinalIgnoreCase);

        AusKnopf.IsEnabled = aktiv is not null;

        // Die Aufschrift sagt, was ein Klick tut, nicht wie der Zustand heisst.
        bool spiel = Programm.Spielmodus;
        SpielKnopf.Content = spiel ? "Spielmodus beenden" : "Spielmodus";

        Status.Text = spiel
            ? "Spielmodus · der Abspieler ist beendet"
            : aktiv is null
                ? $"Nichts aktiv · {_items.Count} Video{(_items.Count == 1 ? "" : "s")} im Ordner"
                : $"Läuft: {Path.GetFileName(aktiv)}";
    }

    public void FehlerZeigen(string text) => Status.Text = "Geht nicht: " + text;

    // ---------- Knoepfe ----------

    private void KachelGeklickt(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is VideoItem item)
            Programm.HintergrundSetzen(item.Pfad);
    }

    private void Ausschalten(object sender, RoutedEventArgs e) => Programm.HintergrundAus();

    private void SpielmodusGeklickt(object sender, RoutedEventArgs e) =>
        Programm.SpielmodusUmschalten();

    /// <summary>
    /// Legt ein Video in den Papierkorb, nicht endgueltig geloescht. Ein Fehlklick
    /// laesst sich damit zuruecknehmen.
    ///
    /// Laeuft das Video gerade, haelt mpv die Datei offen - deshalb erst abschalten.
    /// Und der Zwischenspeicher muss mit weg, sonst bleibt die gerechnete Fassung
    /// als Waise liegen.
    /// </summary>
    /// <summary>
    /// Baut das Kontextmenue beim Rechtsklick und merkt sich dabei die Kachel.
    ///
    /// Im XAML ging das zweimal schief. Ein ContextMenu haengt in einem eigenen Baum;
    /// erst nannte die Rueckfrage am 01.09.2026 das falsche Video - Rechtsklick auf
    /// zzz-probe-zwei.mp4, gefragt wurde nach yama-no-kami.mp4 -, dann loeste der
    /// Menuepunkt gar nicht mehr aus. Ohne Probe waere das falsche Video im Papierkorb
    /// gelandet.
    ///
    /// Hier ist sender die angeklickte Kachel selbst, und das Video steckt fest im
    /// Menuepunkt. Da kann nichts mehr danebengreifen.
    /// </summary>
    private void KachelRechtsklick(object sender, MouseButtonEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is not VideoItem item) return;

        var punkt = new MenuItem { Header = "In den Papierkorb" };
        punkt.Click += (_, _) => Entfernen(item);

        var menue = new ContextMenu { PlacementTarget = (UIElement)sender };
        menue.Items.Add(punkt);
        menue.IsOpen = true;
        e.Handled = true;
    }

    private void Entfernen(VideoItem item)
    {
        string name = Path.GetFileName(item.Pfad);
        if (MessageBox.Show(this, $"„{name}“ in den Papierkorb legen?", "Video entfernen",
                MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK)
            return;

        if (string.Equals(Programm.AktuellesVideo, item.Pfad, StringComparison.OrdinalIgnoreCase))
            Programm.HintergrundAus();

        try
        {
            Verkleinern.Vergessen(item.Pfad);
            FileSystem.DeleteFile(item.Pfad, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
        }
        catch (Exception ex) { FehlerZeigen(ex.Message); }

        Neuladen();
    }

    private void OrdnerOeffnen(object sender, RoutedEventArgs e) =>
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{Settings.VideoOrdner}\"") { UseShellExecute = true });

    private void VideoHinzufuegen(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = "Video auswählen",
            Filter = Hintergrund.DialogFilter,
            Multiselect = true
        };
        if (dlg.ShowDialog(this) == true) Kopieren(dlg.FileNames);
    }

    // ---------- Ziehen und Ablegen ----------

    private static bool HatVideos(DragEventArgs e) =>
        e.Data.GetDataPresent(DataFormats.FileDrop) &&
        (e.Data.GetData(DataFormats.FileDrop) as string[])?.Any(Hintergrund.IstUnterstuetzt) == true;

    private void Abgelegt(object sender, DragEventArgs e)
    {
        ZiehFlaeche.Visibility = Visibility.Collapsed;
        if (e.Data.GetData(DataFormats.FileDrop) is string[] pfade) Kopieren(pfade);
    }

    private void Kopieren(IEnumerable<string> pfade)
    {
        string ziel = Settings.VideoOrdner;
        string? letztes = null;
        foreach (var p in pfade.Where(Hintergrund.IstUnterstuetzt))
        {
            try
            {
                string neu = Path.Combine(ziel, Path.GetFileName(p));
                if (!string.Equals(Path.GetFullPath(p), Path.GetFullPath(neu), StringComparison.OrdinalIgnoreCase)
                    && !File.Exists(neu))
                    File.Copy(p, neu);
                letztes = neu;
            }
            catch (Exception ex) { FehlerZeigen(ex.Message); }
        }
        Neuladen();
        if (letztes is not null) Programm.HintergrundSetzen(letztes);
    }

    // ---------- Schalter ----------

    /// <summary>
    /// Fuellt das Auswahlfeld. Erster Eintrag spannt ueber alle Bildschirme, danach
    /// jeder einzelne. Bei nur einem Monitor bleibt das Feld trotzdem stehen - es
    /// auszublenden waere eine Sonderregel fuer nichts.
    /// </summary>
    private void BildschirmeFuellen()
    {
        BildschirmWahl.Items.Clear();
        BildschirmWahl.Items.Add(new BildschirmEintrag("*", "Alle Bildschirme"));
        foreach (var b in Native.Bildschirme())
            BildschirmWahl.Items.Add(new BildschirmEintrag(b.Name, b.ToString()));

        string? gewaehlt = Programm.Einstellungen.Bildschirm;
        var treffer = BildschirmWahl.Items.Cast<BildschirmEintrag>()
                        .FirstOrDefault(x => x.Name == gewaehlt);
        // Ohne Merkposten der Hauptbildschirm, nicht "alle" - so war es bis zum
        // 31.08.2026, und auf zwei Monitoren zerschnitt es das Video.
        BildschirmWahl.SelectedItem = treffer
            ?? BildschirmWahl.Items.Cast<BildschirmEintrag>().Skip(1).FirstOrDefault()
            ?? BildschirmWahl.Items.Cast<BildschirmEintrag>().First();
    }

    private sealed record BildschirmEintrag(string Name, string Anzeige)
    {
        public override string ToString() => Anzeige;
    }

    private void BildschirmGewaehlt(object sender, SelectionChangedEventArgs e)
    {
        if (_laedt) return;
        if (BildschirmWahl.SelectedItem is not BildschirmEintrag b) return;
        Programm.Einstellungen.Bildschirm = b.Name;
        Programm.Einstellungen.Speichern();
        Programm.HintergrundNeuAufbauen();
    }

    /// <summary>
    /// Zeigt den Knopf, sobald App eine neuere Fassung gefunden hat. Faellt die
    /// Suche aus - kein Netz, GitHub nicht erreichbar -, bleibt er unsichtbar.
    /// Ein Fehlerdialog beim Programmstart waere laestiger als der ausgebliebene
    /// Hinweis.
    /// </summary>
    private void NeuigkeitZeigen()
    {
        if (Programm.Neuigkeit is null) return;
        UpdateKnopf.Content = $"Neu: {Programm.Neuigkeit.Version}";
        UpdateKnopf.Visibility = Visibility.Visible;
    }

    /// <summary>
    /// Der Knopf in der Titelzeile. Fragt nach und sagt, was herauskam.
    ///
    /// Drei Antworten, nicht zwei: Eine gescheiterte Abfrage darf nicht als
    /// "alles aktuell" durchgehen.
    /// </summary>
    private async void SucheGeklickt(object sender, RoutedEventArgs e)
    {
        object vorher = SucheKnopf.Content;
        SucheKnopf.IsEnabled = false;
        SucheKnopf.Content = "sucht ...";

        var ergebnis = await Programm.AktualisierungPruefen();

        SucheKnopf.Content = vorher;
        SucheKnopf.IsEnabled = true;

        if (ergebnis.Fehler is not null)
            MessageBox.Show(this,
                "Die Abfrage bei GitHub hat nicht geklappt. Ob es eine neuere Fassung "
                + "gibt, ist damit offen.\n\n" + ergebnis.Fehler,
                "Nach Aktualisierung gesucht", MessageBoxButton.OK, MessageBoxImage.Warning);
        else if (ergebnis.Neu is null)
            MessageBox.Show(this,
                $"Tapete {Aktualisierung.Eigene} ist die neueste Fassung.",
                "Nach Aktualisierung gesucht", MessageBoxButton.OK, MessageBoxImage.Information);
        else
            MessageBox.Show(this,
                $"Es gibt eine neuere Fassung: {ergebnis.Neu.Version}.\n\n"
                + $"Der Knopf \u201eNeu: {ergebnis.Neu.Version}\u201c unten rechts lädt sie "
                + "und startet die Installation.",
                "Nach Aktualisierung gesucht", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    /// <summary>
    /// Von Hand ausgeloest. Hier mit Assistent, nicht still: Wer klickt, schaut
    /// zu und soll sehen, was geschieht.
    /// </summary>
    private async void UpdateGeklickt(object sender, RoutedEventArgs e)
    {
        var n = Programm.Neuigkeit;
        if (n is null) return;

        // Wer aus dem Bauordner heraus aktualisiert, bekommt eine zweite Kopie
        // an anderer Stelle. Das ist kein Fehler, aber eine Ueberraschung, wenn
        // man es nicht weiss.
        if (!Aktualisierung.AusInstallation)
        {
            string hier = Path.GetDirectoryName(Environment.ProcessPath ?? "") ?? "unbekannt";
            string dorthin = Aktualisierung.InstallationsOrdner
                ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                                "Programs", "Tapete");
            if (MessageBox.Show(this,
                    $"Dieses Tapete l\u00e4uft aus\n{hier}\n\n"
                    + $"Das Setup installiert nach\n{dorthin}\n\n"
                    + "Danach gibt es zwei Kopien. Die hier laufende bleibt auf dem alten "
                    + "Stand, und eine Autostart-Verkn\u00fcpfung zeigt weiter auf sie.\n\n"
                    + "Trotzdem fortfahren?",
                    "Nicht aus der Installation gestartet",
                    MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK)
                return;
        }

        UpdateKnopf.IsEnabled = false;
        UpdateKnopf.Content = "Wird geladen...";
        string? fehler = await Aktualisierung.HolenUndStarten(n);
        if (fehler is null) return;   // Der Installer uebernimmt, Tapete wird gleich beendet.
        FehlerZeigen(fehler);
        UpdateKnopf.Content = $"Neu: {n.Version}";
        UpdateKnopf.IsEnabled = true;
    }

    private void PauseGeaendert(object sender, RoutedEventArgs e)
    {
        if (_laedt) return;
        bool an = PauseSchalter.IsChecked == true;
        Programm.Einstellungen.BeiVollbildPausieren = an;
        Programm.Einstellungen.Speichern();
        Programm.PauseRegelAnwenden(an);
    }

    /// <summary>
    /// Der Schalter wirkt beim Umrechnen, nicht beim Abspielen. Deshalb den Hintergrund
    /// neu aufbauen: Tapete sucht dann die Fassung, die zur neuen Wahl passt, und legt
    /// sie an, falls es sie noch nicht gibt.
    /// </summary>
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

    // ---------- Fenster ----------

    private void TitelZiehen(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2) WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal : WindowState.Maximized;
        else if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    private void Minimieren(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void Verstecken(object sender, RoutedEventArgs e) => Hide();

    /// <summary>Fenster zu heisst nicht Programm zu: der Hintergrund laeuft weiter.</summary>
    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        e.Cancel = true;
        Hide();
    }
}

