using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace Tapete;

/// <summary>
/// Holt Vorschaubilder ueber Windows selbst - dieselben, die der Explorer anzeigt.
/// Dadurch braucht das Programm keinen eigenen Video-Decoder fuer die Kacheln.
/// </summary>
internal static class Thumbs
{
    public static BitmapSource? Get(string file, int size)
    {
        try
        {
            if (!File.Exists(file)) return null;

            Guid iid = typeof(IShellItemImageFactory).GUID;
            int hr = SHCreateItemFromParsingName(file, IntPtr.Zero, ref iid, out IShellItemImageFactory factory);
            if (hr != 0 || factory is null) return null;

            IntPtr hBitmap = IntPtr.Zero;
            try
            {
                factory.GetImage(new SIZE { cx = size, cy = size }, SIIGBF.ResizeToFit, out hBitmap);
                if (hBitmap == IntPtr.Zero) return null;

                var src = Imaging.CreateBitmapSourceFromHBitmap(
                    hBitmap, IntPtr.Zero, System.Windows.Int32Rect.Empty,
                    BitmapSizeOptions.FromEmptyOptions());
                src.Freeze();
                return src;
            }
            finally
            {
                if (hBitmap != IntPtr.Zero) Native.DeleteObject(hBitmap);
                Marshal.ReleaseComObject(factory);
            }
        }
        catch
        {
            return null;
        }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    private static extern int SHCreateItemFromParsingName(string path, IntPtr pbc, ref Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory ppv);

    [StructLayout(LayoutKind.Sequential)]
    private struct SIZE { public int cx; public int cy; }

    [Flags]
    private enum SIIGBF
    {
        ResizeToFit = 0x00,
        BiggerSizeOk = 0x01,
        MemoryOnly = 0x02,
        IconOnly = 0x04,
        ThumbnailOnly = 0x08,
        InCacheOnly = 0x10,
    }

    [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemImageFactory
    {
        void GetImage(SIZE size, SIIGBF flags, out IntPtr phbm);
    }
}
