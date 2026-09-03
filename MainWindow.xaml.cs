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

        // Waehrend des Ziehens ist der Regen ganz weg, danach kommt er zurueck.
        //
        // Zwei Fassungen waren noetig. In 1.13.70 wurde nur der Neuaufbau
        // verzoegert, weil er vorher bei jeder Groessenmeldung von Windows lief,
        // also etwa sechzigmal in der Sekunde. Das reichte nicht: Die
        // Aufzeichnung des Nutzers vom 03.09.2026 zeigt, dass die Fensterkante
        // beim Ziehen in nur sieben von fuenfundvierzig Bildern wanderte, mit
        // Pausen bis zu siebenhundert Millisekunden.
        //
        // Der Grund ist, dass der Regen weiterlief. Zwanzig Textbloecke mit je
        // rund fuenfzig Zeilen, jeder mit einer Deckkraftmaske und einer
        // Daueranimation, und das Fenster steht auf TextFormattingMode="Ideal".
        // Diese Zeichenarbeit faellt in jedem Bild an, auch waehrend WPF das
        // Layout neu rechnet. Weggenommen bleibt dem Ziehen die ganze Zeit.
        _effektWacht = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(250)
        };
        _effektWacht.Tick += (_, _) => { _effektWacht.Stop(); EffektStarten(); };
        EffektFeld.SizeChanged += (_, _) =>
        {
            EffektFeld.Children.Clear();
            _effektWacht.Stop();
            _effektWacht.Start();
        };
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
        if (breite < 40 || hoehe < 40) return;

        // Jedes Erscheinungsbild bekommt seinen eigenen Effekt, nicht denselben
        // Fall mit anderen Zeichen. Der Nutzer hat das am 03.09.2026 beanstandet,
        // und er hatte recht: Vier Themen liefen durch dieselbe Mechanik, nur mit
        // anderem Zeichenvorrat, Tempo und Dichte.
        switch (TryFindResource("EffektArt") as string ?? "regen")
        {
            case "blitze":   BlitzeBauen(breite, hoehe);        return;
            case "spektrum": SpektrumBauen(breite, hoehe);      return;
            case "sonne":    SonnenaufgangBauen(breite, hoehe); return;
        }

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

    // ---------- Blitze, fuer Alarm ----------

    /// <summary>
    /// Zwei Einschlaege im Wechsel, alle drei Sekunden einer.
    ///
    /// Der erste Entwurf war ein Strich, der nach unten fuhr, und sah nach
    /// fallendem Dreck aus. Ein Blitz faellt aber nicht: Er ist in Millisekunden
    /// ganz da, knickt alle paar Meter ab, weil die Luft ungleich leitet, und
    /// spaltet sich in Nebenaeste. Und er leuchtet mehrfach kurz auf, das sind
    /// die Rueckstroeme durch denselben Kanal. Alle drei Sachen stecken hier drin.
    ///
    /// Das Neon entsteht aus drei Lagen mit abnehmender Breite statt aus einem
    /// Weichzeichner: Der kostet in WPF Zeichenzeit, und davon hatten wir am
    /// 03.09.2026 genug.
    /// </summary>
    private void BlitzeBauen(double breite, double hoehe)
    {
        var farbe = TryFindResource("Accent") as SolidColorBrush;
        if (farbe is null) return;

        double masstab = hoehe / 300.0;
        string[] kanaele =
        {
            "M3,0 L-7,38 L11,58 L-3,98 L19,124 L3,166 L25,196 L9,236 L27,272 L17,300",
            "M2,0 L16,34 L-4,62 L12,92 L-12,128 L4,162 L-16,202 L0,236 L-14,300"
        };
        string[][] aeste =
        {
            new[] { "M11,58 L37,82 L28,106", "M3,166 L-27,186 L-18,210",
                    "M25,196 L51,216", "M-3,98 L-23,116" },
            new[] { "M-4,62 L-32,80 L-23,104", "M4,162 L34,182 L25,206",
                    "M-12,128 L-38,142" }
        };
        // Vier Stellen statt zwei: Wo im Fenster freie Flaeche liegt, haengt
        // von der Kachelbreite und der Zahl der Videos ab. Am 03.09.2026 lagen
        // beide Blitze vollstaendig hinter Kacheln und waren nie zu sehen.
        double[] stellen = { 0.18, 0.42, 0.68, 0.90 };

        // Die Aufhellung der ganzen Flaeche im Moment des Einschlags. Sehr
        // schwach, sie soll nur den Eindruck stuetzen, nicht blenden.
        for (int i = 0; i < 4; i++)
        {
            var hell = new System.Windows.Shapes.Rectangle
            {
                Width = breite, Height = hoehe, Fill = Brushes.White, Opacity = 0
            };
            EffektFeld.Children.Add(hell);
            hell.BeginAnimation(OpacityProperty, Aufblitzen(i * 1.5, 0.045, 0.03));
        }

        for (int i = 0; i < stellen.Length; i++)
        {
            int form = i % kanaele.Length;
            // Der Canvas braucht eine Groesse, sonst arrangiert er seine Kinder
            // nicht und der Blitz bleibt unsichtbar. Am 03.09.2026 im eigenen
            // Bildschirmfoto gesehen: Von den Einschlaegen war nur die Aufhellung
            // der Flaeche zu bemerken, der Kanal selbst fehlte ganz.
            var gruppe = new Canvas { Opacity = 0, Width = breite, Height = hoehe };
            double versatz = breite * stellen[i];

            void Strich(string daten, Brush pinsel, double dicke, double deckung)
            {
                var weg = new System.Windows.Shapes.Path
                {
                    Data = Geometry.Parse(daten),
                    Stroke = pinsel,
                    StrokeThickness = dicke,
                    StrokeStartLineCap = PenLineCap.Round,
                    StrokeEndLineCap = PenLineCap.Round,
                    StrokeLineJoin = PenLineJoin.Round,
                    Opacity = deckung,
                    RenderTransform = new TransformGroup
                    {
                        Children =
                        {
                            new ScaleTransform(masstab, masstab),
                            new TranslateTransform(versatz, 0)
                        }
                    }
                };
                gruppe.Children.Add(weg);
            }

            // Von aussen nach innen: roter Hof, zwei weisse Lagen, harter Kern.
            Strich(kanaele[form], farbe, 9, 0.16);
            Strich(kanaele[form], Brushes.White, 5, 0.18);
            Strich(kanaele[form], Brushes.White, 3, 0.45);
            foreach (string ast in aeste[form]) Strich(ast, Brushes.White, 1.4, 0.9);
            Strich(kanaele[form], Brushes.White, 2.2, 1.0);

            EffektFeld.Children.Add(gruppe);
            gruppe.BeginAnimation(OpacityProperty, Aufblitzen(i * 1.5, 1.0, 0.7));
        }
    }

    /// <summary>
    /// Der Verlauf eines Einschlags: schlagartig hell, zweimal kurz nach, dann
    /// sechs Sekunden nichts. Ohne die Nachschlaege sieht es aus wie eine Lampe,
    /// die an- und ausgeht.
    /// </summary>
    private static DoubleAnimationUsingKeyFrames Aufblitzen(double startVerzoegerung,
                                                            double voll, double nach)
    {
        var bilder = new DoubleAnimationUsingKeyFrames
        {
            Duration = TimeSpan.FromSeconds(6),
            RepeatBehavior = RepeatBehavior.Forever,
            BeginTime = TimeSpan.FromSeconds(startVerzoegerung)
        };
        void Punkt(double sekunde, double wert) =>
            bilder.KeyFrames.Add(new DiscreteDoubleKeyFrame(wert, KeyTime.FromTimeSpan(
                TimeSpan.FromSeconds(sekunde))));

        Punkt(0.00, 0);
        Punkt(0.07, voll);
        Punkt(0.14, voll * 0.12);
        Punkt(0.19, voll);
        Punkt(0.28, voll * 0.08);
        Punkt(0.32, nach);
        Punkt(0.48, 0);
        return bilder;
    }

    // ---------- Spektrum, fuer HUD ----------

    /// <summary>
    /// Achtzehn Saeulen am unteren Rand, jede mit eigenem Takt und einem
    /// Spitzenwerthalter darueber.
    ///
    /// Der Halter ist das, woran man ein echtes Messgeraet erkennt: ein Strich,
    /// der oben stehenbleibt und langsam absinkt. Und jede Saeule braucht ihren
    /// eigenen Takt - laufen alle durch dieselbe Bewegung mit Zeitversatz, sieht
    /// man nach ein paar Sekunden die Welle durchlaufen, und es wirkt wie eine
    /// Girlande.
    /// </summary>
    private void SpektrumBauen(double breite, double hoehe)
    {
        var farbe = TryFindResource("Accent") as SolidColorBrush;
        var linie = TryFindResource("Line") as Brush;
        var blass = TryFindResource("Dim") as Brush;
        if (farbe is null) return;

        const int anzahl = 18;
        double rand = breite * 0.03;
        double nutzbar = breite - rand * 2;
        double luecke = 3;
        double saeulenbreite = (nutzbar - luecke * (anzahl - 1)) / anzahl;
        // Die Saeulen stehen auf dem unteren Rand auf. Bis 1.13.75 lagen sie
        // 26 Punkte darueber, weil dort eine Frequenzskala stehen sollte; die
        // war hinter den Kacheln ohnehin kaum zu sehen, und das Spektrum sah
        // aus, als schwebe es. Vom Nutzer am 03.09.2026 gemeldet.
        double boden = hoehe;
        double vollhoehe = Math.Min(80, hoehe * 0.26);

        // Die Grundlinie und drei Marken dahinter, wie die dB-Skala eines Geraets.
        void Waagerecht(double y, Brush pinsel, double deckung)
        {
            var l = new System.Windows.Shapes.Rectangle
            {
                Width = nutzbar, Height = 1, Fill = pinsel, Opacity = deckung
            };
            Canvas.SetLeft(l, rand);
            Canvas.SetTop(l, y);
            EffektFeld.Children.Add(l);
        }
        if (linie is not null)
        {
            Waagerecht(boden - vollhoehe * 0.95, linie, 0.5);
            Waagerecht(boden - vollhoehe * 0.62, linie, 0.5);
            Waagerecht(boden - vollhoehe * 0.30, linie, 0.5);
        }
        if (blass is not null) Waagerecht(boden, blass, 0.35);

        var zufall = new Random();
        for (int i = 0; i < anzahl; i++)
        {
            // Tiefe Frequenzen links schlagen kraeftiger aus als hohe rechts,
            // so verhaelt sich Musik wirklich.
            double staerke = 0.95 - (i / (double)anzahl) * 0.55;
            double hoch = (0.30 + zufall.NextDouble() * 0.62) * staerke * vollhoehe;
            double tief = hoch * (0.20 + zufall.NextDouble() * 0.20);
            double dauer = 0.9 + zufall.NextDouble() * 1.4;
            double links = rand + i * (saeulenbreite + luecke);

            var saeule = new System.Windows.Shapes.Rectangle
            {
                Width = saeulenbreite,
                Fill = new LinearGradientBrush(farbe.Color,
                    Color.FromArgb(56, farbe.Color.R, farbe.Color.G, farbe.Color.B), 90)
            };
            Canvas.SetLeft(saeule, links);
            EffektFeld.Children.Add(saeule);

            var wachsen = new DoubleAnimationUsingKeyFrames
            {
                Duration = TimeSpan.FromSeconds(dauer),
                RepeatBehavior = RepeatBehavior.Forever,
                BeginTime = TimeSpan.FromSeconds(-zufall.NextDouble() * dauer)
            };
            void Hoehe(double anteil, double wert) =>
                wachsen.KeyFrames.Add(new LinearDoubleKeyFrame(wert,
                    KeyTime.FromTimeSpan(TimeSpan.FromSeconds(dauer * anteil))));
            Hoehe(0.00, tief);
            Hoehe(0.32, hoch);
            Hoehe(0.55, hoch * 0.55);
            Hoehe(0.78, hoch * 0.85);
            Hoehe(1.00, tief);
            saeule.BeginAnimation(HeightProperty, wachsen);

            // Die Saeule waechst nach oben, deshalb muss ihr oberer Rand mitwandern.
            var obenhin = new DoubleAnimationUsingKeyFrames
            {
                Duration = wachsen.Duration,
                RepeatBehavior = RepeatBehavior.Forever,
                BeginTime = wachsen.BeginTime
            };
            foreach (LinearDoubleKeyFrame k in wachsen.KeyFrames)
                obenhin.KeyFrames.Add(new LinearDoubleKeyFrame(boden - k.Value, k.KeyTime));
            saeule.BeginAnimation(Canvas.TopProperty, obenhin);

            var halter = new System.Windows.Shapes.Rectangle
            {
                Width = saeulenbreite, Height = 1, Fill = farbe, Opacity = 0.85
            };
            Canvas.SetLeft(halter, links);
            EffektFeld.Children.Add(halter);

            var sinken = new DoubleAnimationUsingKeyFrames
            {
                Duration = TimeSpan.FromSeconds(dauer * 1.6),
                RepeatBehavior = RepeatBehavior.Forever,
                BeginTime = wachsen.BeginTime
            };
            sinken.KeyFrames.Add(new DiscreteDoubleKeyFrame(boden - hoch, KeyTime.FromPercent(0)));
            sinken.KeyFrames.Add(new DiscreteDoubleKeyFrame(boden - hoch, KeyTime.FromPercent(0.4)));
            sinken.KeyFrames.Add(new LinearDoubleKeyFrame(boden - tief, KeyTime.FromPercent(1)));
            halter.BeginAnimation(Canvas.TopProperty, sinken);
        }
    }

    // ---------- Sonnenaufgang, fuer Retro ----------

    /// <summary>
    /// Sonne rechts unten, halb hinter der Kimm, darunter Wasser mit ihrem
    /// Lichtband und einem Gitter.
    ///
    /// Alles sitzt im unteren Drittel. Dort liegt bei einer vollen Kachelliste
    /// der freie Platz, und nur dort ist der Hintergrund ueberhaupt zu sehen.
    ///
    /// Die Streifen der Sonne sind einzelne Rechtecke, deren Breite aus dem
    /// Kreis gerechnet wird, keine Deckkraftmaske. Eine Maske zwingt WPF, das
    /// Element in einen Zwischenpuffer zu zeichnen; genau das hat am 03.09.2026
    /// das Fenster ruckeln lassen.
    /// </summary>
    private void SonnenaufgangBauen(double breite, double hoehe)
    {
        // Die drei Sonnenfarben gehoeren zu diesem Stil und stehen deshalb hier
        // und nicht im Thema: Gelb oben, Orange in der Mitte, Pink unten.
        var oben = Color.FromRgb(0xFF, 0xE0, 0x66);
        var mitte = Color.FromRgb(0xFF, 0xA2, 0x3D);
        var unten = Color.FromRgb(0xE0, 0x2E, 0x8C);
        var akzent = (TryFindResource("Accent") as SolidColorBrush)?.Color
                     ?? Color.FromRgb(0xFF, 0x3D, 0x9A);

        double kimm = hoehe * 0.76;
        // Der freie Platz liegt rechts neben der letzten Kachelspalte, nicht
        // unten: Bei der Kachelbreite des Nutzers passen nur drei Spalten in die
        // Zeile. Am 03.09.2026 im eigenen Bildschirmfoto gesehen, vorher stand
        // die Sonne zur Haelfte hinter den Kacheln und sah aus wie eine Treppe.
        double radius = Math.Min(hoehe * 0.26, breite * 0.13);
        double mx = breite * 0.86;
        double my = kimm;

        void Setze(UIElement e, double x, double y)
        {
            Canvas.SetLeft(e, x); Canvas.SetTop(e, y); EffektFeld.Children.Add(e);
        }

        // Himmel
        var himmel = new System.Windows.Shapes.Rectangle
        {
            Width = breite, Height = kimm - hoehe * 0.40,
            Fill = new LinearGradientBrush(
                Color.FromArgb(0, 0x3A, 0x12, 0x47), Color.FromRgb(0x6B, 0x1E, 0x68), 90)
        };
        Setze(himmel, 0, hoehe * 0.40);

        // Der Schein um die Sonne, eine grosse weiche Scheibe ohne Streifen
        var schein = new System.Windows.Shapes.Ellipse
        {
            Width = radius * 3.2, Height = radius * 3.2,
            Fill = new RadialGradientBrush(
                new GradientStopCollection
                {
                    new(Color.FromArgb(107, 0xFF, 0x96, 0x5F), 0),
                    new(Color.FromArgb(41, akzent.R, akzent.G, akzent.B), 0.44),
                    new(Color.FromArgb(0, akzent.R, akzent.G, akzent.B), 0.66)
                })
        };
        Setze(schein, mx - radius * 1.6, my - radius * 1.6);

        // Die Sonne aus waagerechten Streifen. Zehn Punkte hoch, fuenf Luecke.
        for (double y = -radius; y < radius; y += 11)
        {
            double halb = Math.Sqrt(Math.Max(0, radius * radius - y * y));
            if (halb < 1) continue;
            double streifenhoehe = Math.Min(7, radius - y);
            double anteil = (y + radius) / (radius * 2);
            Color ton = anteil < 0.45
                ? Mischen(oben, mitte, anteil / 0.45)
                : Mischen(mitte, unten, (anteil - 0.45) / 0.55);

            var streifen = new System.Windows.Shapes.Rectangle
            {
                Width = halb * 2, Height = streifenhoehe,
                Fill = new SolidColorBrush(ton), Opacity = 0.95
            };
            Setze(streifen, mx - halb, my + y);
        }

        // Wasser, danach gezeichnet: es verdeckt die untere Sonnenhaelfte
        var wasser = new System.Windows.Shapes.Rectangle
        {
            Width = breite, Height = hoehe - kimm,
            Fill = new LinearGradientBrush(
                Color.FromRgb(0x2A, 0x0E, 0x33), Color.FromRgb(0x0D, 0x07, 0x16), 90)
        };
        Setze(wasser, 0, kimm);

        // Das Lichtband: Streifen unter der Sonne, die seitlich schwingen
        var band = new Canvas { Width = radius * 2, Height = hoehe - kimm, ClipToBounds = true };
        for (double y = 0; y < hoehe - kimm; y += 7)
        {
            double schwund = 1 - y / (hoehe - kimm);
            var streifen = new System.Windows.Shapes.Rectangle
            {
                Width = radius * 2, Height = 2,
                Fill = new SolidColorBrush(Color.FromArgb(
                    (byte)(200 * schwund * schwund), 0xFF, 0xBE, 0x6E))
            };
            Canvas.SetTop(streifen, y);
            band.Children.Add(streifen);
        }
        var dehnen = new ScaleTransform(1, 1) { CenterX = radius };
        band.RenderTransform = dehnen;
        Setze(band, mx - radius, kimm);
        dehnen.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1, 1.22,
            TimeSpan.FromSeconds(2.1))
        { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever });

        // Gitter im Wasser: senkrecht in die Tiefe, dazu drei Querlinien.
        // Es steht still. Ein fahrendes Gitter liest sich als Strasse, und eine
        // Strasse im Wasser ergibt kein Bild.
        var gitterfarbe = new SolidColorBrush(Color.FromArgb(76, akzent.R, akzent.G, akzent.B));
        for (double x = 0; x < breite; x += 46)
        {
            var senkrecht = new System.Windows.Shapes.Rectangle
            {
                Width = 1, Height = hoehe - kimm, Fill = gitterfarbe
            };
            Setze(senkrecht, x, kimm);
        }
        foreach (double anteil in new[] { 0.16, 0.38, 0.66 })
        {
            var quer = new System.Windows.Shapes.Rectangle
            {
                Width = breite, Height = 1,
                Fill = new SolidColorBrush(Color.FromArgb(56, akzent.R, akzent.G, akzent.B))
            };
            Setze(quer, 0, kimm + (hoehe - kimm) * anteil);
        }

        // Die Kimm zuletzt, sie liegt ueber allem
        var linie = new System.Windows.Shapes.Rectangle
        {
            Width = breite, Height = 1,
            Fill = new SolidColorBrush(Color.FromRgb(0xFF, 0x6F, 0xB5))
        };
        Setze(linie, 0, kimm);

        var zufall = new Random();
        for (int i = 0; i < 3; i++)
        {
            var wolke = new System.Windows.Shapes.Rectangle
            {
                Width = 54 + zufall.Next(70), Height = 4,
                Fill = new LinearGradientBrush(new GradientStopCollection
                {
                    new(Color.FromArgb(0, 0xFF, 0x96, 0xB4), 0),
                    new(Color.FromArgb(66, 0xFF, 0x96, 0xB4), 0.22),
                    new(Color.FromArgb(66, 0xFF, 0x96, 0xB4), 0.78),
                    new(Color.FromArgb(0, 0xFF, 0x96, 0xB4), 1)
                }, 0)
            };
            double y = kimm - (hoehe * 0.06 + i * hoehe * 0.05);
            Setze(wolke, breite * (0.06 + i * 0.14), y);
            var schub = new TranslateTransform();
            wolke.RenderTransform = schub;
            schub.BeginAnimation(TranslateTransform.XProperty,
                new DoubleAnimation(-40, 60, TimeSpan.FromSeconds(26))
                {
                    RepeatBehavior = RepeatBehavior.Forever,
                    BeginTime = TimeSpan.FromSeconds(-i * 9)
                });
        }

        for (int i = 0; i < 7; i++)
        {
            var stern = new System.Windows.Shapes.Rectangle
            {
                Width = 2, Height = 2,
                Fill = new SolidColorBrush(Color.FromRgb(0xE9, 0xCB, 0xFA))
            };
            Setze(stern, breite * (0.06 + i * 0.135), hoehe * (0.05 + zufall.NextDouble() * 0.12));
            stern.BeginAnimation(OpacityProperty,
                new DoubleAnimation(0.18, 0.85, TimeSpan.FromSeconds(2))
                {
                    AutoReverse = true,
                    RepeatBehavior = RepeatBehavior.Forever,
                    BeginTime = TimeSpan.FromSeconds(-zufall.NextDouble() * 4)
                });
        }
    }

    /// <summary>Zwei Farben anteilig mischen, fuer den Verlauf der Sonnenstreifen.</summary>
    private static Color Mischen(Color a, Color b, double anteil)
    {
        anteil = Math.Clamp(anteil, 0, 1);
        return Color.FromRgb(
            (byte)(a.R + (b.R - a.R) * anteil),
            (byte)(a.G + (b.G - a.G) * anteil),
            (byte)(a.B + (b.B - a.B) * anteil));
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

