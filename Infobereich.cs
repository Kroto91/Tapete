using System.Runtime.InteropServices;

namespace Tapete;

/// <summary>
/// Das Symbol neben der Uhr, ohne WinForms.
///
/// Angelegt am 06.09.2026. Vorher machte das <c>System.Windows.Forms.NotifyIcon</c>.
/// WinForms war damit der einzige Grund, warum das Paket beim Veroeffentlichen
/// nicht zugeschnitten werden durfte (NETSDK1175); ohne WinForms steht dieser Weg
/// offen. Hintergrund in wiki/themen/tapete.md.
///
/// Nach aussen sind es vier Dinge: anzeigen, Text setzen, Nachrichten annehmen,
/// wieder verschwinden. Alles Uebrige, von der Struktur bis zum Symbolhandle,
/// bleibt hier drin.
/// </summary>
internal sealed class Infobereich : IDisposable
{
    /// <summary>Windows unterscheidet mehrere Symbole eines Programms an dieser Zahl.</summary>
    private const int Kennung = 1;

    /// <summary>Die Struktur fasst 128 Zeichen samt Abschluss.</summary>
    private const int TextGrenze = 127;

    private IntPtr _fenster;
    private IntPtr _icon;
    private bool _steht;
    private string _text = "Tapete";

    /// <summary>Doppelklick mit links, in Tapete: Fenster zeigen.</summary>
    internal event Action? Doppelklick;

    /// <summary>Klick mit rechts, in Tapete: Menue oeffnen.</summary>
    internal event Action? Rechtsklick;

    /// <summary>
    /// Haengt das Symbol an ein vorhandenes Fenster. Dessen Nachrichtenschleife
    /// muss <see cref="Nachricht"/> aufrufen, sonst kommen die Klicks nicht an.
    /// </summary>
    internal bool Anzeigen(IntPtr fenster, string text)
    {
        _fenster = fenster;
        _text = Kuerzen(text);
        _icon = IconHolen();

        var daten = Bauen(Native.NIF_MESSAGE | Native.NIF_ICON | Native.NIF_TIP);
        _steht = Native.Shell_NotifyIcon(Native.NIM_ADD, ref daten);

        if (!_steht)
            Hintergrund.Notiz("Infobereich: Symbol liess sich nicht anlegen, Fehler "
                              + Marshal.GetLastWin32Error());

        return _steht;
    }

    /// <summary>Aendert den Text, der beim Zeigen erscheint.</summary>
    internal void TextSetzen(string text)
    {
        if (!_steht) return;
        _text = Kuerzen(text);
        var daten = Bauen(Native.NIF_TIP);
        Native.Shell_NotifyIcon(Native.NIM_MODIFY, ref daten);
    }

    /// <summary>
    /// Nimmt eine Fensternachricht entgegen. Gibt true zurueck, wenn sie zum Symbol
    /// gehoerte und behandelt wurde.
    /// </summary>
    internal bool Nachricht(int nachricht, IntPtr lp)
    {
        if (nachricht != Native.WM_TRAY) return false;

        // Im unteren Wort von lParam steht, was der Nutzer getan hat.
        switch ((int)(lp.ToInt64() & 0xFFFF))
        {
            case Native.WM_LBUTTONDBLCLK:
                Doppelklick?.Invoke();
                return true;
            case Native.WM_RBUTTONUP:
                Rechtsklick?.Invoke();
                return true;
            default:
                return false;
        }
    }

    /// <summary>Baut die Struktur. cbSize muss stimmen, sonst lehnt Windows ab.</summary>
    private Native.NOTIFYICONDATA Bauen(int flags) => new()
    {
        cbSize = Marshal.SizeOf<Native.NOTIFYICONDATA>(),
        hWnd = _fenster,
        uID = Kennung,
        uFlags = flags,
        uCallbackMessage = Native.WM_TRAY,
        hIcon = _icon,
        szTip = _text,
        szInfo = string.Empty,
        szInfoTitle = string.Empty,
    };

    /// <summary>
    /// Das Symbol der eigenen exe, sonst das Standardsymbol von Windows. Ein
    /// fehlendes Symbol ist kein Grund, ganz auf den Infobereich zu verzichten.
    /// </summary>
    private static IntPtr IconHolen()
    {
        try
        {
            string? exe = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(exe)
                && Native.ExtractIconEx(exe, 0, out IntPtr gross, out IntPtr klein, 1) > 0)
            {
                // Im Infobereich zeigt Windows das kleine Symbol. Das grosse wird
                // hier nicht gebraucht und muss trotzdem freigegeben werden, sonst
                // bleibt ein GDI-Handle liegen.
                if (gross != IntPtr.Zero) Native.DestroyIcon(gross);
                if (klein != IntPtr.Zero) return klein;
            }
        }
        catch (Exception e)
        {
            Hintergrund.Notiz($"Infobereich, Symbol: {e.GetType().Name}: {e.Message}");
        }

        return Native.LoadIcon(IntPtr.Zero, Native.IDI_APPLICATION);
    }

    private static string Kuerzen(string t) =>
        t.Length > TextGrenze ? t[..(TextGrenze - 3)] + "..." : t;

    /// <summary>
    /// Nimmt das Symbol weg und gibt das Handle frei. Ohne das Entfernen bliebe
    /// ein toter Platz neben der Uhr stehen, bis jemand mit der Maus darueberfaehrt.
    /// </summary>
    public void Dispose()
    {
        if (_steht)
        {
            var daten = Bauen(0);
            Native.Shell_NotifyIcon(Native.NIM_DELETE, ref daten);
            _steht = false;
        }

        if (_icon != IntPtr.Zero)
        {
            Native.DestroyIcon(_icon);
            _icon = IntPtr.Zero;
        }
    }
}
