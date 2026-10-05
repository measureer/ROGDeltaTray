using System.Runtime.InteropServices;
using System.Text.Json;
using RogBatteryTray.Hid;
using RogBatteryTray.Tray;

namespace RogBatteryTray;

internal static class Program
{
    private const string MutexName = "RogBatteryTray.SingleInstance";
    private const int AttachParentProcess = -1;

    [STAThread]
    private static int Main(string[] args)
    {
        // One-shot query for scripts/tools: prints the state as JSON and exits.
        if (args.Contains("--query", StringComparer.OrdinalIgnoreCase))
            return QueryOnce();

        using var mutex = new Mutex(true, MutexName, out bool created);
        if (!created)
        {
            MessageBox.Show(L.AlreadyRunning, "RogBatteryTray",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return 0;
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new TrayAppContext());
        return 0;
    }

    /// <summary>
    /// Reads the battery state once and prints it as JSON (same shape as
    /// %APPDATA%\RogBatteryTray\status.json). Exit code: 0 connected, 1 error, 2 not connected.
    /// </summary>
    private static int QueryOnce()
    {
        // The app is a WinExe; attach to the parent's console so the JSON is visible.
        AttachConsole(AttachParentProcess);
        try
        {
            using var source = new HidBatterySource();
            var state = source.Read();
            var payload = new
            {
                state.Connected,
                state.Percent,
                state.Charging,
                state.DonglePresent,
                state.HeadsetOff,
            };
            Console.WriteLine(JsonSerializer.Serialize(payload,
                new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
            Console.Out.Flush();
            return state.Connected ? 0 : 2;
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.Message);
            Console.Out.Flush();
            return 1;
        }
    }

    [DllImport("kernel32.dll")]
    private static extern bool AttachConsole(int dwProcessId);
}
