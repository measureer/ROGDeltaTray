using RogBatteryTray.Tray;

namespace RogBatteryTray;

internal static class Program
{
    private const string MutexName = "RogBatteryTray.SingleInstance";

    [STAThread]
    private static void Main()
    {
        using var mutex = new Mutex(true, MutexName, out bool created);
        if (!created)
        {
            MessageBox.Show("RogBatteryTray 已在运行。", "RogBatteryTray",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new TrayAppContext());
    }
}
