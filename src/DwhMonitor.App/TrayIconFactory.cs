using System.Runtime.InteropServices;

namespace DwhMonitor.App;

internal static class TrayIconFactory
{
    public static Icon Create(Color color)
    {
        using var bitmap = new Bitmap(32, 32);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(Color.Transparent);
            graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using var borderBrush = new SolidBrush(Color.FromArgb(55, 65, 81));
            using var stateBrush = new SolidBrush(color);
            graphics.FillEllipse(borderBrush, 2, 2, 28, 28);
            graphics.FillEllipse(stateBrush, 6, 6, 20, 20);
        }

        var iconHandle = bitmap.GetHicon();
        try
        {
            using var icon = Icon.FromHandle(iconHandle);
            return (Icon)icon.Clone();
        }
        finally
        {
            DestroyIcon(iconHandle);
        }
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(nint handle);
}
