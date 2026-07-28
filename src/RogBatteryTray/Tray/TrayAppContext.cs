using Microsoft.Win32;
using RogBatteryTray.Audio;
using RogBatteryTray.Hid;

namespace RogBatteryTray.Tray;

public sealed class TrayAppContext : ApplicationContext
{
    private const int PollIntervalMs = 5_000;
    private const int DisconnectedPollIntervalMs = 2_000;
    private const int DisconnectAfterFailures = 3;
    private const int LowBatteryThreshold = 20;
    private const string RunKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValueName = "RogBatteryTray";

    private readonly NotifyIcon _notifyIcon;
    private readonly System.Windows.Forms.Timer _timer;
    private readonly IBatterySource _source;
    private readonly AudioDeviceManager _audio = new();
    private readonly ToolStripMenuItem _statusItem;
    private readonly ToolStripMenuItem _autoSwitchItem;
    private readonly ToolStripMenuItem _autostartItem;
    private Icon? _currentIcon;
    private bool _lowBatteryNotified;
    private bool _wasConnected;
    private string? _lastIconKey;
    private int _consecutiveFailures;
    private BatteryState? _lastGoodState;

    public TrayAppContext(IBatterySource? source = null)
    {
        _source = source ?? new HidBatterySource();

        _statusItem = new ToolStripMenuItem("电量: --") { Enabled = false };
        _autoSwitchItem = new ToolStripMenuItem("自动切换声音输入输出")
        {
            Checked = Settings.AutoSwitchAudio,
            CheckOnClick = true,
        };
        _autoSwitchItem.CheckedChanged += (_, _) => Settings.AutoSwitchAudio = _autoSwitchItem.Checked;
        _autostartItem = new ToolStripMenuItem("开机启动")
        {
            Checked = IsAutostartEnabled(),
            CheckOnClick = true,
        };
        _autostartItem.CheckedChanged += (_, _) => SetAutostart(_autostartItem.Checked);

        var menu = new ContextMenuStrip();
        menu.Items.Add(_statusItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("立即刷新", null, (_, _) => Refresh());
        menu.Items.Add(_autoSwitchItem);
        menu.Items.Add(_autostartItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("退出", null, (_, _) => Exit());

        _notifyIcon = new NotifyIcon
        {
            Text = "ROG 耳机电量",
            ContextMenuStrip = menu,
            Visible = true,
        };

        _timer = new System.Windows.Forms.Timer { Interval = PollIntervalMs };
        _timer.Tick += (_, _) => Refresh();
        _timer.Start();

        Refresh();
    }

    private void Refresh()
    {
        var reading = _source.Read();

        // Debounce: a single failed poll is usually a transient link hiccup, not a
        // real power-off. Only treat the headset as disconnected after several
        // consecutive failures; until then keep showing the last good reading.
        BatteryState state;
        if (reading.Connected)
        {
            _consecutiveFailures = 0;
            _lastGoodState = reading;
            state = reading;
        }
        else if (++_consecutiveFailures < DisconnectAfterFailures && _lastGoodState != null)
        {
            state = _lastGoodState;
        }
        else
        {
            state = reading;
        }

        _statusItem.Text = state switch
        {
            { Connected: false } => "耳机未连接",
            { Percent: { } p } => $"电量: {p}%" + (state.Charging ? "（充电中）" : ""),
            _ => "电量: --",
        };
        _notifyIcon.Text = _statusItem.Text;

        string iconKey = state.Connected ? $"p{state.Percent}" : "off";
        if (iconKey != _lastIconKey)
        {
            _lastIconKey = iconKey;
            SetIcon(IconRenderer.Render(state));
        }

        // Poll faster while disconnected so a power-on is detected quickly.
        int interval = state.Connected ? PollIntervalMs : DisconnectedPollIntervalMs;
        if (_timer.Interval != interval)
            _timer.Interval = interval;

        HandleConnectionTransition(state.Connected);
        HandleLowBattery(state);
    }

    private void HandleConnectionTransition(bool connected)
    {
        if (connected == _wasConnected)
            return;
        _wasConnected = connected;

        if (!Settings.AutoSwitchAudio)
            return;

        try
        {
            if (connected)
            {
                // remember current defaults (unless the headset is already the default)
                var (renderId, captureId) = _audio.GetDefaultIds();
                var headsetIds = _audio.EnumActiveEndpoints()
                    .Where(e => e.Name.Contains("ROG DELTA II", StringComparison.OrdinalIgnoreCase))
                    .Select(e => e.Id).ToHashSet();
                if (renderId != null && !headsetIds.Contains(renderId))
                    Settings.PrevRenderId = renderId;
                if (captureId != null && !headsetIds.Contains(captureId))
                    Settings.PrevCaptureId = captureId;

                _audio.SwitchToHeadset();
            }
            else
            {
                _audio.SwitchToSpeakers(Settings.PrevRenderId, Settings.PrevCaptureId);
            }
        }
        catch (Exception ex)
        {
            _notifyIcon.ShowBalloonTip(3000, "ROG 耳机", $"切换音频设备失败：{ex.Message}", ToolTipIcon.Error);
        }
    }

    private void HandleLowBattery(BatteryState state)
    {
        if (state is { Connected: true, Charging: false, Percent: <= LowBatteryThreshold })
        {
            if (!_lowBatteryNotified)
            {
                _notifyIcon.ShowBalloonTip(3000, "ROG 耳机电量低",
                    $"剩余电量 {state.Percent}%，请及时充电。", ToolTipIcon.Warning);
                _lowBatteryNotified = true;
            }
        }
        else
        {
            _lowBatteryNotified = false;
        }
    }

    private void SetIcon(Icon icon)
    {
        _currentIcon?.Dispose();
        _currentIcon = icon;
        _notifyIcon.Icon = icon;
    }

    private static bool IsAutostartEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
        return key?.GetValue(RunValueName) is string path &&
               string.Equals(path, Application.ExecutablePath, StringComparison.OrdinalIgnoreCase);
    }

    private static void SetAutostart(bool enable)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
        if (key == null) return;
        if (enable)
            key.SetValue(RunValueName, Application.ExecutablePath);
        else
            key.DeleteValue(RunValueName, throwOnMissingValue: false);
    }

    private void Exit()
    {
        _notifyIcon.Visible = false;
        Application.Exit();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _timer.Dispose();
            _notifyIcon.Dispose();
            _currentIcon?.Dispose();
            _source.Dispose();
        }
        base.Dispose(disposing);
    }
}
