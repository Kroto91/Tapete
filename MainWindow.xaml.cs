using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Input;
using Microsoft.VisualBasic.FileIO;
using Microsoft.Win32;

namespace Tapete;

public partial class MainWindow : Window
{
    private readonly ObservableCollection<VideoItem> _items = new();

    // Waehrend die Kachelliste aufgebaut wird, sind die Haken-Ereignisse stumm.
    // Sonst schriebe jeder programmatisch gesetzte Haken die Auswahl zurueck -
    // und zwar aus einer Liste, die noch gar nicht fertig gefuellt ist.
    private bool _laedt;
    private App Programm => (App)Application.Current;

    public MainWindow()
    {
        InitializeComponent();
        Liste.ItemsSource = _items;

        TitelText.Text = $"Tapete {Aktualisierung.Eigene}";

        DragEnter += (_, e) => { if (HatVideos(e)) ZiehFlaeche.Visibility = Visibility.Visible; };
        DragLeave += (_, _) => ZiehFlaeche.Visibility = Visibility.Collapsed;
        DragOver += (_, e) =>
        {
            e.Effects = HatVideos(e) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        };
        Drop += Abgelegt;

        Neuladen();

        // Gesucht wird in App, nicht hier. Sonst liefe die Abfrage zweimal, und
        // im Autostart haenge die Aktualisierung an einem Fenster, das nie
        // gezeigt wird.
        Programm.NeuigkeitGefunden += NeuigkeitZeigen;
        NeuigkeitZeigen();

        // Beim Ziehen mit der Maus meldet WPF jede einzelne neue Groesse. Ein
        // Neuaufbau je Meldung heisst zwanzig Textbloecke wegwerfen und mit
        // eigenen Storyboards neu anlegen, und das sechzigmal in der Sekunde.
        // Das Fenster ruckelte dabei und der Regen sprang staendig an den
        // Anfang zurueck; gemeldet vom Nutzer am 03.09.2026.
        //
        // Deshalb wird erst gebaut, wenn die Groesse eine Viertelsekunde ruhig
        // ist. Waehrend des Ziehens bleibt der alte Regen stehen, in der alten
        // Breite. Das faellt kaum auf und kostet nichts.
        _effektWacht = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(250)
        };
        _effektWacht.Tick += (_, _) => { _effektWacht.Stop(); EffektStarten(); };
        EffektFeld.SizeChanged += (_, _) => { _effektWacht.Stop(); _effektWacht.Start(); };
    }

    /// <summary>Wartet nach der letzten Groessenmeldung, bevor der Regen neu entsteht.</summary>
    private System.Windows.Threading.DispatcherTimer? _effektWacht;

    /// <summary>
    /// Der Zeichenregen des geladenen Themas.
    ///
    /// Er laeuft auf einer Ebene hinter den Kacheln und ist deshalb nur dort zu
    /// sehen, wo keine Kachel darueber liegt: rechts neben der letzten Spalte,
    /// unter der letzten Zeile und waehrend des Suchens. Ueber einem Vorschaubild
    /// liegt er nie, das war die Bedingung des Nutzers vom 02.09.2026.
    ///
    /// Zeichenvorrat, Tempo, Dichte, Deckkraft und Groesse kommen aus dem Thema.
    /// So ist es ein Bauteil und nicht vier, und jedes Aussehen bekommt trotzdem
    /// seinen eigenen Charakter.
    /// </summary>
    internal void EffektStarten()
    {
        EffektFeld.Children.Clear();
        if (!Programm.Einstellungen.Effekte) return;

        double breite = EffektFeld.ActualWidth, hoehe = EffektFeld.ActualHeight;
        if (breite < 40 || hoehe < 40) return;   // noch nicht gemessen

        string zeichen = TryFindResource("EffektZeichen") as string ?? "01";
        double tempo = TryFindResource("EffektTempo") as double? ?? 8;
        int dichte = TryFindResource("EffektDichte") as int? ?? 20;
        double deckkraft = TryFindResource("EffektDeckkraft") as double? ?? 0.25;
        double groesse = TryFindResource("EffektGroesse") as double? ?? 12;
        var farbe = TryFindResource("Accent") as Brush;
        var schrift = TryFindResource("TechFont") as FontFamily;
        if (zeichen.Length == 0 || farbe is null) return;

        // Nach unten ausblenden, damit die Spalten nicht abgeschnitten wirken,
        // sondern verschwinden.
        var maske = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(0, 1),
            GradientStops =
            {
                new GradientStop(Colors.Transparent, 0),
                new GradientStop(Colors.Black, 0.15),
                new GradientStop(Colors.Black, 0.75),
                new GradientStop(Colors.Transparent, 1)
            }
        };

        var zufall = new Random();
        double zeile = groesse * 1.15;
        int laenge = (int)(hoehe / zeile) + 10;

        for (int i = 0; i < dichte; i++)
        {
            var text = new System.Text.StringBuilder(laenge * 2);
            for (int j = 0; j < laenge; j++)
            {
                text.Append(zeichen[zufall.Next(zeichen.Length)]);
                if (j < laenge - 1) text.Append('\n');
            }

            var block = new TextBlock
            {
                Text = text.ToString(),
                FontFamily = schrift,
                FontSize = groesse,
                Foreground = farbe,
                Opacity = deckkraft * (0.6 + zufall.NextDouble() * 0.4),
                LineHeight = zeile,
                LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
                OpacityMask = maske
            };
            Canvas.SetLeft(block, i * (breite / dichte));
            EffektFeld.Children.Add(block);

            var schub = new TranslateTransform();
            block.RenderTransform = schub;

            // Jede Spalte eigene Dauer, sonst faellt alles im Gleichschritt.
            double dauer = tempo * (0.65 + zufall.NextDouble() * 0.7);
            var lauf = new DoubleAnimation(
                -hoehe * 1.1, hoehe, TimeSpan.FromSeconds(dauer))
            {
                RepeatBehavior = RepeatBehavior.Forever,
                // Negativ heisst: mittendrin anfangen. Ohne das waere die Flaeche
                // in den ersten Sekunden leer und fuellte sich erst von oben.
                BeginTime = TimeSpan.FromSeconds(-zufall.NextDouble() * dauer)
            };
            schub.BeginAnimation(TranslateTransform.YProperty, lauf);
        }
    }

    // ---------- Liste ----------

    public void Neuladen()
    {
        string ordner = Settings.VideoOrdner;
        var dateien = Directory.EnumerateFiles(ordner)
                               .Where(Hintergrund.IstUnterstuetzt)
                               .OrderBy(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase)
                               .ToList();

        var imKarussell = new HashSet<string>(Programm.Einstellungen.KarussellVideos,
                                              StringComparer.OrdinalIgnoreCase);
        _laedt = true;
        _items.Clear();
        foreach (var f in dateien)
            _items.Add(new VideoItem(f) { ImKarussell = imKarussell.Contains(Path.GetFileName(f)) });
        _laedt = false;

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

        int haken = _items.Count(i => i.ImKarussell);
        string karussell = Programm.Einstellungen.KarussellAn && haken > 1
            ? $" · Karussell: {haken} Videos, {Programm.Einstellungen.KarussellDauerText}"
            : "";

        Status.Text = spiel
            ? "Spielmodus · der Abspieler ist beendet"
            : aktiv is null
                ? $"Nichts aktiv · {_items.Count} Video{(_items.Count == 1 ? "" : "s")} im Ordner{karussell}"
                : $"Läuft: {Path.GetFileName(aktiv)}{karussell}";
    }

    /// <summary>
    /// Zeigt nur noch die Kacheln, deren Dateiname den eingetippten Text enthaelt.
    /// Gefiltert wird die Ansicht, nicht die Liste: Die Haken fuers Karussell und
    /// die laufende Kachel bleiben dadurch unberuehrt.
    /// </summary>
    private void SuchfeldGeaendert(object sender, TextChangedEventArgs e)
    {
        string suche = Suchfeld.Text.Trim();
        SuchePlatzhalter.Visibility = suche.Length == 0 ? Visibility.Visible : Visibility.Collapsed;

        var sicht = CollectionViewSource.GetDefaultView(_items);
        sicht.Filter = suche.Length == 0
            ? null
            : o => o is VideoItem v
                && Path.GetFileName(v.Pfad).Contains(suche, StringComparison.OrdinalIgnoreCase);

        int treffer = sicht.Cast<object>().Count();
        Unterzeile.Text = suche.Length == 0
            ? "Ein Video anklicken, mehr ist nicht nötig."
            : treffer switch
            {
                0 => $"Kein Video enthält „{suche}“.",
                1 => $"Ein Video enthält „{suche}“.",
                _ => $"{treffer} Videos enthalten „{suche}“."
            };
    }

    public void FehlerZeigen(string text) => Status.Text = "Geht nicht: " + text;

    /// <summary>Ein Hinweis ohne Fehlercharakter, etwa der Rueckfall auf ein anderes Video.</summary>
    public void MeldungZeigen(string text) => Status.Text = text;

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

        var menue = new ContextMenu { PlacementTarget = (UIElement)sender };

        // Je Bildschirm ein eigenes Video. Erst ab zwei Schirmen sinnvoll, deshalb
        // taucht der Punkt bei einem gar nicht erst auf.
        var schirme = Native.Bildschirme();
        if (schirme.Count > 1)
        {
            foreach (var s in schirme)
            {
                string name = s.Name;
                var zu = new MenuItem { Header = $"Auf {s} zeigen" };
                zu.Click += (_, _) => BildschirmZuweisen(item, name);
                menue.Items.Add(zu);
            }

            if (Programm.Einstellungen.VideoJeBildschirm.Count > 0)
            {
                var zurueck = new MenuItem { Header = "Zuweisungen aufheben" };
                zurueck.Click += (_, _) =>
                {
                    Programm.Einstellungen.VideoJeBildschirm.Clear();
                    Programm.Einstellungen.Speichern();
                    Programm.HintergrundNeuAufbauen();
                    Status.Text = "Auf allen Bildschirmen laeuft wieder dasselbe Video.";
                };
                menue.Items.Add(zurueck);
            }
            menue.Items.Add(new Separator());
        }

        var punkt = new MenuItem { Header = "In den Papierkorb" };
        punkt.Click += (_, _) => Entfernen(item);
        menue.Items.Add(punkt);

        menue.IsOpen = true;
        e.Handled = true;
    }

    /// <summary>
    /// Weist ein Video einem Bildschirm zu und schaltet dabei auf „jeder Bildschirm
    /// einzeln“ um. Ohne diese Umschaltung liefe die Zuweisung ins Leere, und der
    /// Nutzer saehe nur, dass sein Klick nichts bewirkt.
    /// </summary>
    private void BildschirmZuweisen(VideoItem item, string schirm)
    {
        Programm.Einstellungen.VideoJeBildschirm[schirm] = item.Pfad;
        Programm.Einstellungen.Bildschirm = "*";
        Programm.Einstellungen.Speichern();
        // Laeuft noch gar nichts, wuerde HintergrundNeuAufbauen nichts tun und die
        // Zuweisung bliebe unsichtbar bis zum naechsten Klick auf eine Kachel.
        if (Programm.AktuellesVideo is null) Programm.HintergrundSetzen(item.Pfad);
        else Programm.HintergrundNeuAufbauen();
        Status.Text = $"{Path.GetFileName(item.Pfad)} laeuft jetzt auf "
            + schirm.Replace(@"\\.\", "") + ".";
    }

    /// <summary>
    /// Legt ein Video in den Papierkorb, nicht endgueltig geloescht. Ein Fehlklick
    /// laesst sich damit zuruecknehmen.
    ///
    /// Laeuft das Video gerade, haelt mpv die Datei offen - deshalb erst abschalten.
    /// Und der Zwischenspeicher muss mit weg, sonst bleibt die gerechnete Fassung
    /// als Waise liegen.
    /// </summary>
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
    /// Der Haken oben links auf einer Kachel. Er entscheidet, ob das Video im
    /// Karussell mitlaeuft. Gespeichert wird die ganze Liste, nicht die einzelne
    /// Aenderung - das ist eine Zeile statt einer Buchfuehrung.
    ///
    /// An Checked und Unchecked, nicht an Click: Wird der Haken ueber die
    /// Windows-Bedienhilfen gesetzt, etwa von einer Sprachausgabe, kommt kein
    /// Click. Am 01.09.2026 beim Pruefen aufgefallen - der Haken sass, die
    /// Auswahl war trotzdem nicht gespeichert.
    /// </summary>
    private void KarussellHakenGeaendert(object sender, RoutedEventArgs e)
    {
        if (_laedt) return;
        Programm.Einstellungen.KarussellVideos =
            _items.Where(i => i.ImKarussell).Select(i => i.Name).ToList();
        Programm.Einstellungen.Speichern();
        Programm.KarussellNeuAufbauen();
        StandAktualisieren();
    }

    private void EinstellungenGeklickt(object sender, RoutedEventArgs e)
    {
        // Als Dialog, damit sich die Kachelhaken und das Fenster nicht gegenseitig
        // ins Wort fallen. Wer Videos ankreuzen will, schliesst es vorher.
        new EinstellungenFenster { Owner = this }.ShowDialog();
        Neuladen();
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

