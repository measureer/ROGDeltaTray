using System.Drawing;
using RogBatteryTray.Hid;
using RogBatteryTray.Tray;

// Preview sheet: 128px enlarged + real tray sizes (32/16) on a dark taskbar-like strip.
var states = new (string Label, BatteryState State)[]
{
    ("85%", new BatteryState(85, false, true, true, false)),
    ("29%", new BatteryState(29, false, true, true, false)),
    ("15% low", new BatteryState(15, false, true, true, true)),
    ("100%", new BatteryState(100, false, true, true, false)),
    ("charging", new BatteryState(45, true, true, true, false)),
    ("offline", BatteryState.Disconnected),
};

const int cell = 160, labelH = 24, stripH = 40;
using var sheet = new Bitmap(cell * states.Length, cell + labelH + stripH);
using var g = Graphics.FromImage(sheet);
g.Clear(Color.White);
for (int i = 0; i < states.Length; i++)
{
    using var big = IconRenderer.RenderBitmap(states[i].State, 20, 128);
    g.DrawImage(big, i * cell + 16, 16, 128, 128);
    using var font = new Font("Segoe UI", 10);
    g.DrawString(states[i].Label, font, Brushes.Black, i * cell + 16, cell + 2);
}
// real-size strip on dark background (like the taskbar)
using (var stripBrush = new SolidBrush(Color.FromArgb(32, 32, 32)))
    g.FillRectangle(stripBrush, 0, cell + labelH, sheet.Width, stripH);
for (int i = 0; i < states.Length; i++)
{
    using var s32 = IconRenderer.RenderBitmap(states[i].State, 20, 32);
    g.DrawImage(s32, i * cell + 16, cell + labelH + 4, 32, 32);
    using var s16 = IconRenderer.RenderBitmap(states[i].State, 20, 16);
    g.DrawImage(s16, i * cell + 60, cell + labelH + 12, 16, 16);
}
sheet.Save("C:/Code/icon_preview.png");
Console.WriteLine("saved");
