using System.Drawing.Drawing2D;
using RogBatteryTray.Hid;

namespace RogBatteryTray.Tray;

/// <summary>Renders the tray icon as a big battery-percentage number badge.</summary>
public static class IconRenderer
{
    private static readonly Color OkColor = Color.FromArgb(46, 125, 50);      // green
    private static readonly Color LowColor = Color.FromArgb(198, 40, 40);     // red
    private static readonly Color OfflineColor = Color.FromArgb(117, 117, 117); // gray

    public static Icon Render(BatteryState state)
    {
        const int size = 32;
        using var bmp = new Bitmap(size, size);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
            g.Clear(Color.Transparent);

            bool online = state.Connected && state.Percent != null;
            Color bg = !online ? OfflineColor : state.Percent!.Value <= 20 ? LowColor : OkColor;
            string text = online ? state.Percent!.Value.ToString() : "×";

            using (var brush = new SolidBrush(bg))
                g.FillRoundedRectangle(brush, 0, 0, size, size, 7);

            float fontSize = text.Length switch
            {
                1 => 20f,
                2 => 17f,
                _ => 12.5f,   // "100"
            };
            using var font = new Font("Segoe UI", fontSize, FontStyle.Bold, GraphicsUnit.Pixel);
            using var textBrush = new SolidBrush(Color.White);
            var fmt = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            g.DrawString(text, font, textBrush, new RectangleF(0, 0, size, size), fmt);
        }

        IntPtr hIcon = bmp.GetHicon();
        var icon = (Icon)Icon.FromHandle(hIcon).Clone();
        DestroyIcon(hIcon);
        return icon;
    }

    private static void FillRoundedRectangle(this Graphics g, Brush brush, float x, float y, float w, float h, float r)
    {
        using var path = new GraphicsPath();
        float d = r * 2;
        path.AddArc(x, y, d, d, 180, 90);
        path.AddArc(x + w - d, y, d, d, 270, 90);
        path.AddArc(x + w - d, y + h - d, d, d, 0, 90);
        path.AddArc(x, y + h - d, d, d, 90, 90);
        path.CloseFigure();
        g.FillPath(brush, path);
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr handle);
}
