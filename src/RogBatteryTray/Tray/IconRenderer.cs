using System.Drawing.Drawing2D;
using RogBatteryTray.Hid;

namespace RogBatteryTray.Tray;

/// <summary>Renders the tray icon: navy tile, big percentage number, battery bar.</summary>
public static class IconRenderer
{
    private static readonly Color BgTop = Color.FromArgb(35, 43, 77);       // #232B4D
    private static readonly Color BgBottom = Color.FromArgb(18, 23, 46);    // #12172E
    private static readonly Color BgOfflineTop = Color.FromArgb(66, 71, 82);
    private static readonly Color BgOfflineBottom = Color.FromArgb(40, 43, 50);
    private static readonly Color OkColor = Color.FromArgb(88, 214, 141);   // soft green
    private static readonly Color LowColor = Color.FromArgb(255, 90, 90);   // soft red
    private static readonly Color TrackColor = Color.FromArgb(90, 255, 255, 255);
    private static readonly Color OfflineText = Color.FromArgb(154, 160, 166);
    private static readonly Color BoltColor = Color.FromArgb(255, 213, 79); // gold

    public static Icon Render(BatteryState state, int lowThreshold = 20)
    {
        // Render at the real tray icon size for the current DPI instead of a fixed
        // 32px, so Windows doesn't have to rescale the icon itself.
        int target = Math.Clamp(SystemInformation.SmallIconSize.Width, 16, 64);

        // Supersample at 4x, then downscale — much smoother edges at tray size.
        using var big = RenderBitmap(state, lowThreshold, 128);
        using var small = new Bitmap(target, target);
        using (var g = Graphics.FromImage(small))
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.DrawImage(big, 0, 0, target, target);
        }

        IntPtr hIcon = small.GetHicon();
        var icon = (Icon)Icon.FromHandle(hIcon).Clone();
        DestroyIcon(hIcon);
        return icon;
    }

    /// <summary>Renders the icon artwork at an arbitrary size (also used for previews).</summary>
    public static Bitmap RenderBitmap(BatteryState state, int lowThreshold, int size)
    {
        var bmp = new Bitmap(size, size);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
        g.Clear(Color.Transparent);

        bool online = state.Connected && state.Percent != null;
        float s = size / 128f;   // all coordinates below are designed on a 128px canvas

        // background tile with a subtle vertical gradient
        using (var bgBrush = new LinearGradientBrush(new Rectangle(0, 0, size, size),
                   online ? BgTop : BgOfflineTop, online ? BgBottom : BgOfflineBottom, 90f))
            g.FillRoundedRectangle(bgBrush, 2 * s, 2 * s, size - 4 * s, size - 4 * s, 26 * s);

        if (online)
        {
            int p = state.Percent!.Value;

            // battery bar along the bottom: track + fill proportional to the percentage
            float barX = 14 * s, barW = size - 28 * s, barH = 8 * s, barY = size - 18 * s;
            using (var trackBrush = new SolidBrush(TrackColor))
                g.FillRoundedRectangle(trackBrush, barX, barY, barW, barH, barH / 2);
            Color barColor = p <= lowThreshold ? LowColor : OkColor;
            float fillW = Math.Max(barW * p / 100f, barH);   // keep a dot even at 1%
            using (var fillBrush = new SolidBrush(barColor))
                g.FillRoundedRectangle(fillBrush, barX, barY, fillW, barH, barH / 2);

            // percentage number, sized to fill the tile
            string text = p.ToString();
            float fontSize = text.Length switch
            {
                1 => 78f,
                2 => 68f,
                _ => 50f,   // "100"
            } * s;
            using var font = new Font("Segoe UI", fontSize, FontStyle.Bold, GraphicsUnit.Pixel);
            using var textBrush = new SolidBrush(Color.White);
            var fmt = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            var textArea = new RectangleF(0, 2 * s, size, barY - 4 * s);
            g.DrawString(text, font, textBrush, textArea, fmt);

            if (state.Charging)
                DrawBolt(g, size - 36 * s, 10 * s, 26 * s);
        }
        else
        {
            string text = "×";
            float fontSize = 64f * s;
            using var font = new Font("Segoe UI", fontSize, FontStyle.Bold, GraphicsUnit.Pixel);
            using var textBrush = new SolidBrush(OfflineText);
            var fmt = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            g.DrawString(text, font, textBrush, new RectangleF(0, 0, size, size), fmt);

            // Receiver plugged in but headset off: amber "standby" dot top-right,
            // so this state is distinguishable from "receiver unplugged" at a glance.
            if (state.DonglePresent)
            {
                float d = 18 * s;
                using var dotBrush = new SolidBrush(BoltColor);
                g.FillEllipse(dotBrush, size - 30 * s, 12 * s, d, d);
            }
        }

        return bmp;
    }

    /// <summary>Small charging bolt in the top-right corner.</summary>
    private static void DrawBolt(Graphics g, float x, float y, float h)
    {
        float w = h * 0.62f;
        PointF[] bolt =
        {
            new(x + w * 0.62f, y),
            new(x, y + h * 0.58f),
            new(x + w * 0.38f, y + h * 0.58f),
            new(x + w * 0.30f, y + h),
            new(x + w, y + h * 0.40f),
            new(x + w * 0.56f, y + h * 0.40f),
        };
        using var brush = new SolidBrush(BoltColor);
        g.FillPolygon(brush, bolt);
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
