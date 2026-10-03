using Microsoft.Win32;
using RogBatteryTray.Audio;
using RogBatteryTray.Hid;

namespace RogBatteryTray.Tray;

public sealed class TrayAppContext : ApplicationContext
{
    private const int PollIntervalMs = 5_000;
    private const int DisconnectedPollIntervalMs = 2_000;
    private const int DisconnectAfterFailures = 3;
    private const string RunKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValueName = "RogBatteryTray";
    private static readonly int[] ThresholdOptions = { 10, 15, 20, 25, 30 };

    private readonly NotifyIcon _notifyIcon;
    private readonly System.Windows.Forms.Timer _timer;
    private readonly IBatterySource _source;
    private readonly Control _invoker = new();   // marshals device-change events to the UI thread
    private readonly AudioDeviceManager _audio = new();
    private readonly BatteryHistory _history = new();
    private readonly ContextMenuStrip _menu;
    private readonly ToolStripMenuItem _statusItem;
    private readonly ToolStripMenuItem _estimateItem;
    private readonly ToolStripMenuItem _autoSwitchItem;
    private readonly ToolStripMenuItem _autostartItem;
    private readonly ToolStripMenuItem _notifyConnItem;
    private readonly ToolStripMenuItem _thresholdItem;
    private readonly ToolStripMenuItem _renderFallbackItem;
    private readonly ToolStripMenuItem _captureFallbackItem;
    private readonly HashSet<int> _notifiedTiers = new();
    private Icon? _currentIcon;
    private bool _wasConnected;
    private string? _lastIconKey;
    private int _consecutiveFailures;
    private DateTime _lastRefreshLocal;
    private BatteryState? _lastGoodState;
    private BatteryState _currentState = BatteryState.Disconnected;

    public TrayAppContext(IBatterySource? source = null)
    {
        _source = source ?? new HidBatterySource();

        _statusItem = new ToolStripMenuItem("电量: --") { Enabled = false };
        _estimateItem = new ToolStripMenuItem("耗电速率: 数据收集中") { Enabled = false };
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
        _notifyConnItem = new ToolStripMenuItem("连接/断开提示")
        {
            Checked = Settings.NotifyOnConnection,
            CheckOnClick = true,
        };
        _notifyConnItem.CheckedChanged += (_, _) => Settings.NotifyOnConnection = _notifyConnItem.Checked;

        _thresholdItem = new ToolStripMenuItem("低电量阈值");
        foreach (int v in ThresholdOptions)
        {
            var item = new ToolStripMenuItem($"{v}%") { Tag = v, Checked = v == Settings.LowBatteryThreshold };
            item.Click += (_, _) => SetThreshold(v);
            _thresholdItem.DropDownItems.Add(item);
        }

        _renderFallbackItem = new ToolStripMenuItem("回切播放设备");
        _captureFallbackItem = new ToolStripMenuItem("回切录音设备");

        _menu = new ContextMenuStrip();
        _menu.Items.Add(_statusItem);
        _menu.Items.Add(_estimateItem);
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add("立即刷新", null, (_, _) => Refresh());
        _menu.Items.Add(_thresholdItem);
        _menu.Items.Add(_notifyConnItem);
        _menu.Items.Add(_autoSwitchItem);
        _menu.Items.Add(_renderFallbackItem);
        _menu.Items.Add(_captureFallbackItem);
        _menu.Items.Add(_autostartItem);
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add("退出", null, (_, _) => Exit());
        // Device lists change as endpoints come and go, so rebuild them on every open.
        _menu.Opening += (_, _) => RebuildFallbackMenus();

        _notifyIcon = new NotifyIcon
        {
            Text = "ROG 耳机电量",
            ContextMenuStrip = _menu,
            Visible = true,
        };
        _notifyIcon.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
                ShowDetails();
        };

        _timer = new System.Windows.Forms.Timer { Interval = PollIntervalMs };
        _timer.Tick += (_, _) => Refresh();
        _timer.Start();

        _ = _invoker.Handle;   // force handle creation on the UI thread
        _source.DeviceChanged += OnDeviceChanged;

        Refresh();
    }

    private void OnDeviceChanged(object? sender, EventArgs e)
    {
        // Raised from HidSharp's watcher thread — refresh immediately on the UI
        // thread so plug/unplug is reflected without waiting for the next poll.
        if (_invoker.IsDisposed)
            return;
        try { _invoker.BeginInvoke(Refresh); }
        catch (ObjectDisposedException) { }
    }

    private void Refresh()
    {
        var reading = _source.Read();
        _lastRefreshLocal = DateTime.Now;

        // Debounce: a single failed poll is usually a transient link hiccup, not a
        // real power-off. Only treat the headset as disconnected after several
        // consecutive failures; until then keep showing the last good reading.
        // When the receiver itself is gone there is no ambiguity — disconnect at once.
        BatteryState state;
        if (reading.Connected)
        {
            _consecutiveFailures = 0;
            _lastGoodState = reading;
            state = reading;
        }
        else if (reading.DonglePresent && ++_consecutiveFailures < DisconnectAfterFailures && _lastGoodState != null)
        {
            state = _lastGoodState;
        }
        else
        {
            state = reading;
        }

        if (reading is { Connected: true, Percent: { } sampled })
            _history.Add(sampled);

        _statusItem.Text = state switch
        {
            { Connected: true, Percent: { } p } => $"电量: {p}%" + (state.Charging ? "（充电中）" : ""),
            { DonglePresent: true } => "耳机未开机（接收器已连接）",
            _ => "耳机未连接",
        };
        _notifyIcon.Text = _statusItem.Text;

        _estimateItem.Text = EstimateText(state);

        string iconKey = state.Connected ? $"p{state.Percent}t{Settings.LowBatteryThreshold}" : "off";
        if (iconKey != _lastIconKey)
        {
            _lastIconKey = iconKey;
            SetIcon(IconRenderer.Render(state, Settings.LowBatteryThreshold));
        }

        // Poll faster while disconnected so a power-on is detected quickly.
        int interval = state.Connected ? PollIntervalMs : DisconnectedPollIntervalMs;
        if (_timer.Interval != interval)
            _timer.Interval = interval;

        HandleConnectionTransition(state);
        HandleLowBattery(state);
        _currentState = state;
    }

    private string EstimateText(BatteryState state)
    {
        if (state is not { Connected: true, Percent: { } p })
            return "耗电速率: --";

        var remaining = _history.EstimateRemaining(p);
        return remaining == null
            ? "耗电速率: 数据收集中"
            : $"预计剩余: {FormatDuration(remaining.Value)}";
    }

    private static string FormatDuration(TimeSpan t)
    {
        if (t.TotalHours >= 1)
            return $"{(int)t.TotalHours} 小时 {t.Minutes} 分钟";
        return $"{Math.Max(t.Minutes, 1)} 分钟";
    }

    private void SetThreshold(int value)
    {
        Settings.LowBatteryThreshold = value;
        foreach (ToolStripMenuItem item in _thresholdItem.DropDownItems)
            item.Checked = (int)item.Tag! == value;
        _notifiedTiers.Clear();
        _lastIconKey = null;   // force icon re-render with the new threshold
        Refresh();
    }

    private void RebuildFallbackMenus()
    {
        List<AudioDeviceManager.Endpoint> endpoints;
        try
        {
            endpoints = _audio.EnumActiveEndpoints();
        }
        catch
        {
            return;   // keep the previous list if enumeration fails
        }

        RebuildFallbackMenu(_renderFallbackItem, endpoints.Where(e => e.IsRender),
            () => Settings.FixedRenderId, id => Settings.FixedRenderId = id);
        RebuildFallbackMenu(_captureFallbackItem, endpoints.Where(e => !e.IsRender),
            () => Settings.FixedCaptureId, id => Settings.FixedCaptureId = id);
    }

    private static void RebuildFallbackMenu(
        ToolStripMenuItem parent,
        IEnumerable<AudioDeviceManager.Endpoint> endpoints,
        Func<string?> getFixed,
        Action<string?> setFixed)
    {
        parent.DropDownItems.Clear();

        var auto = new ToolStripMenuItem("自动记忆") { Checked = getFixed() == null };
        auto.Click += (_, _) => setFixed(null);
        parent.DropDownItems.Add(auto);
        parent.DropDownItems.Add(new ToolStripSeparator());

        string? fixedId = getFixed();
        foreach (var e in endpoints)
        {
            var item = new ToolStripMenuItem(e.Name) { Checked = e.Id == fixedId };
            string id = e.Id;
            item.Click += (_, _) => setFixed(id);
            parent.DropDownItems.Add(item);
        }
    }

    private void ShowDetails()
    {
        string text = _currentState switch
        {
            { Connected: true, Percent: { } p } =>
                $"电量 {p}%" + (_currentState.Charging ? "（充电中）" : "")
                + $"\n{_estimateItem.Text}\n上次刷新: {_lastRefreshLocal:HH:mm:ss}",
            { DonglePresent: true } =>
                $"耳机未开机（接收器已连接）\n上次刷新: {_lastRefreshLocal:HH:mm:ss}",
            _ =>
                $"耳机未连接\n上次刷新: {_lastRefreshLocal:HH:mm:ss}",
        };
        _notifyIcon.ShowBalloonTip(3000, "ROG 耳机电量", text, ToolTipIcon.Info);
    }

    private void HandleConnectionTransition(BatteryState state)
    {
        bool connected = state.Connected;
        if (connected == _wasConnected)
            return;
        _wasConnected = connected;

        if (Settings.NotifyOnConnection)
        {
            string text = connected
                ? state.Percent is { } p ? $"耳机已连接，电量 {p}%" : "耳机已连接"
                : "耳机已断开";
            _notifyIcon.ShowBalloonTip(3000, "ROG 耳机", text, ToolTipIcon.Info);
        }

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
                // A fixed fallback device (if configured) takes precedence over the
                // auto-remembered one.
                _audio.SwitchToSpeakers(
                    Settings.FixedRenderId ?? Settings.PrevRenderId,
                    Settings.FixedCaptureId ?? Settings.PrevCaptureId);
            }
        }
        catch (Exception ex)
        {
            _notifyIcon.ShowBalloonTip(3000, "ROG 耳机", $"切换音频设备失败：{ex.Message}", ToolTipIcon.Error);
        }
    }

    private void HandleLowBattery(BatteryState state)
    {
        if (state is not { Connected: true, Charging: false, Percent: { } p })
        {
            _notifiedTiers.Clear();
            return;
        }

        // Tiered alerts: one notification per tier crossed; a tier re-arms once
        // the battery rises back above it (e.g. after charging).
        foreach (int tier in new[] { Settings.LowBatteryThreshold, 10, 5 }.Distinct().OrderByDescending(t => t))
        {
            if (p <= tier)
            {
                if (_notifiedTiers.Add(tier))
                {
                    _notifyIcon.ShowBalloonTip(3000, "ROG 耳机电量低",
                        $"剩余电量 {p}%，请及时充电。", ToolTipIcon.Warning);
                }
            }
            else
            {
                _notifiedTiers.Remove(tier);
            }
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
            _source.DeviceChanged -= OnDeviceChanged;
            _timer.Dispose();
            _notifyIcon.Dispose();
            _menu.Dispose();
            _currentIcon?.Dispose();
            _invoker.Dispose();
            _source.Dispose();
        }
        base.Dispose(disposing);
    }
}
