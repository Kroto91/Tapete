using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;

namespace Tapete;

/// <summary>Eine Kachel in der Uebersicht.</summary>
public sealed class VideoItem : INotifyPropertyChanged
{
    public string Pfad { get; }
    public string Name { get; }

    public VideoItem(string pfad)
    {
        Pfad = pfad;
        // Mit Endung: Sonst sind gleichnamige Dateien in unterschiedlichen Formaten
        // in der Kachelliste nicht zu unterscheiden.
        Name = Path.GetFileName(pfad);
    }

    private BitmapSource? _bild;
    public BitmapSource? Bild
    {
        get => _bild;
        set { _bild = value; Melde(nameof(Bild)); Melde(nameof(PlatzhalterSichtbar)); }
    }

    private bool _laeuft;
    public bool Laeuft
    {
        get => _laeuft;
        set { _laeuft = value; Melde(nameof(Laeuft)); }
    }

    public Visibility PlatzhalterSichtbar => _bild is null ? Visibility.Visible : Visibility.Collapsed;

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Melde(string n) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}
