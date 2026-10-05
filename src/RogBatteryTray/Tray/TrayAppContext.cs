using Microsoft.Win32;
using RogBatteryTray.Audio;
using RogBatteryTray.Hid;

namespace RogBatteryTray.Tray;

public sealed class TrayAppContext : ApplicationContext
{
    private const int PollIntervalMs = 5_000;
    private const int DisconnectedPollIntervalMs = 2_000;
    private const int DisconnectAfterFailures = 3;
    private const int AudioSwitchMaxRetries = 3;
    private const int AudioSwitchRetryDelayMs = 1_500;
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
    private readonly ToolStripMenuItem _refreshItem;
    private readonly ToolStripMenuItem _autoSwitchItem;
    private readonly ToolStripMenuItem _autostartItem;
    private readonly ToolStripMenuItem _notifyConnItem;
    private readonly ToolStripMenuItem _thresholdItem;
    private readonly ToolStripMenuItem _renderFallbackItem;
    private readonly ToolStripMenuItem _captureFallbackItem;
    private readonly ToolStripMenuItem _languageItem;
    private readonly ToolStripMenuItem _languageAutoItem;
    private readonly ToolStripMenuItem _exitItem;
    private readonly HashSet<int> _notifiedTiers = new();
    private Icon? _currentIcon;
    private bool _wasConnected;
    private string? _lastIconKey;
    private int _consecutiveFailures;
    private DateTime _lastRefreshLocal;
    private BatteryState? _lastGoodState;
    private BatteryState _currentState = BatteryState.Disconnected;
    private TimeSpan? _lastRemainingEstimate;
    private bool _refreshInFlight;
    private bool _disposed;
    private int _audioSwitchRetries;

    public TrayAppContext(IBatterySource? source = null)
    {
        _source = source ?? new HidBatterySource();

        _statusItem = new ToolStripMenuItem { Enabled = false };
        _estimateItem = new ToolStripMenuItem { Enabled = false };
        _refreshItem = new ToolStripMenuItem();
        _refreshItem.Click += (_, _) => Refresh();
        _autoSwitchItem = new ToolStripMenuItem
        {
            Checked = Settings.AutoSwitchAudio,
            CheckOnClick = true,
        };
        _autoSwitchItem.CheckedChanged += (_, _) => Settings.AutoSwitchAudio = _autoSwitchItem.Checked;
        _autostartItem = new ToolStripMenuItem
        {
            Checked = IsAutostartEnabled(),
            CheckOnClick = true,
        };
        _autostartItem.CheckedChanged += (_, _) => SetAutostart(_autostartItem.Checked);
        _notifyConnItem = new ToolStripMenuItem
        {
            Checked = Settings.NotifyOnConnection,
            CheckOnClick = true,
        };
        _notifyConnItem.CheckedChanged += (_, _) => Settings.NotifyOnConnection = _notifyConnItem.Checked;

        _thresholdItem = new ToolStripMenuItem();
        foreach (int v in ThresholdOptions)
        {
            var item = new ToolStripMenuItem($"{v}%") { Tag = v, Checked = v == Settings.LowBatteryThreshold };
            item.Click += (_, _) => SetThreshold(v);
            _thresholdItem.DropDownItems.Add(item);
        }

        _renderFallbackItem = new ToolStripMenuItem();
        _captureFallbackItem = new ToolStripMenuItem();

        _languageItem = new ToolStripMenuItem();
        _languageAutoItem = new ToolStripMenuItem { Tag = "auto" };
        _languageAutoItem.Click += (_, _) => SetLanguage("auto");
        var zhItem = new ToolStripMenuItem("中文") { Tag = "zh" };
        zhItem.Click += (_, _) => SetLanguage("zh");
        var enItem = new ToolStripMenuItem("English") { Tag = "en" };
        enItem.Click += (_, _) => SetLanguage("en");
        _languageItem.DropDownItems.Add(_languageAutoItem);
        _languageItem.DropDownItems.Add(zhItem);
        _languageItem.DropDownItems.Add(enItem);

        _exitItem = new ToolStripMenuItem();
        _exitItem.Click += (_, _) => Exit();

        _menu = new ContextMenuStrip();
        _menu.Items.Add(_statusItem);
        _menu.Items.Add(_estimateItem);
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(_refreshItem);
        _menu.Items.Add(_thresholdItem);
        _menu.Items.Add(_notifyConnItem);
        _menu.Items.Add(_autoSwitchItem);
        _menu.Items.Add(_renderFallbackItem);
        _menu.Items.Add(_captureFallbackItem);
        _menu.Items.Add(_autostartItem);
        _menu.Items.Add(_languageItem);
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(_exitItem);
        // Device lists change as endpoints come and go, so rebuild them on every open.
        _menu.Opening += (_, _) => RebuildFallbackMenus();

        _notifyIcon = new NotifyIcon
        {
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

        ApplyTexts();
        SetIcon(IconRenderer.Render(_currentState, Settings.LowBatteryThreshold));
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

    /// <summary>
    /// Kicks off an asynchronous poll. The HID read can take several seconds when the
    /// link is flaky (retries × read timeout), so it runs on a background thread to
    /// keep the tray menu and balloons responsive; overlapping polls are skipped.
    /// </summary>
    private void Refresh()
    {
        if (_refreshInFlight || _disposed)
            return;
        _refreshInFlight = true;
        Task.Run(() =>
        {
            BatteryState reading;
            try { reading = _source.Read(); }
            catch { reading = BatteryState.Disconnected; }

            try
            {
                _invoker.BeginInvoke(() =>
                {
                    _refreshInFlight = false;
                    ApplyReading(reading);
                });
            }
            catch (ObjectDisposedException) { }
            catch (InvalidOperationException) { }   // handle gone during shutdown
        });
    }

    private void ApplyReading(BatteryState reading)
    {
        _lastRefreshLocal = DateTime.Now;

        // Debounce: a single failed poll is usually a transient link hiccup, not a
        // real power-off. Only treat the headset as disconnected after several
        // consecutive failures; until then keep showing the last good reading.
        // Two cases skip the debounce: the receiver itself is gone, or the dongle
        // NAKed the query — both are definitive, not hiccups.
        BatteryState state;
        if (reading.Connected)
        {
            _consecutiveFailures = 0;
            _lastGoodState = reading;
            state = reading;
        }
        else if (reading is { DonglePresent: true, HeadsetOff: false }
                 && ++_consecutiveFailures < DisconnectAfterFailures && _lastGoodState != null)
        {
            state = _lastGoodState;
        }
        else
        {
            state = reading;
        }

        if (reading is { Connected: true, Percent: { } sampled })
            _history.Add(sampled, reading.Charging);

        _statusItem.Text = StatusText(state);
        _estimateItem.Text = EstimateText(state);

        string tip = _statusItem.Text;
        if (state.Connected)
            tip += "\n" + _estimateItem.Text;
        _notifyIcon.Text = tip.Length <= 63 ? tip : tip[..63];

        string iconKey = state.Connected
            ? $"p{state.Percent}t{Settings.LowBatteryThreshold}c{state.Charging}"
            : state.DonglePresent ? "dongle" : "off";
        iconKey += $"s{SystemInformation.SmallIconSize.Width}";
        if (iconKey != _lastIconKey)
        {
            _lastIconKey = iconKey;
            SetIcon(IconRenderer.Render(state, Settings.LowBatteryThreshold));
        }

        StatusFile.Write(state, _lastRemainingEstimate);

        // Poll faster while disconnected so a power-on is detected quickly.
        int interval = state.Connected ? PollIntervalMs : DisconnectedPollIntervalMs;
        if (_timer.Interval != interval)
            _timer.Interval = interval;

        HandleConnectionTransition(state);
        HandleLowBattery(state);
        _currentState = state;
    }

    private static string StatusText(BatteryState state) => state switch
    {
        { Connected: true, Percent: { } p } => L.StatusBattery(p, state.Charging),
        { DonglePresent: true } => L.StatusHeadsetOff,
        _ => L.StatusDisconnected,
    };

    private string EstimateText(BatteryState state)
    {
        _lastRemainingEstimate = null;
        if (state is not { Connected: true, Percent: { } p })
            return L.EstimateNone;

        if (state.Charging)
        {
            var full = _history.EstimateTimeToFull(p);
            return full == null ? L.EstimateCharging : L.EstimateFullIn(L.FormatDuration(full.Value));
        }

        var remaining = _history.EstimateRemaining(p);
        _lastRemainingEstimate = remaining;
        return remaining == null
            ? L.EstimateCollecting
            : L.EstimateRemaining(L.FormatDuration(remaining.Value));
    }

    /// <summary>Re-applies every localizable label, e.g. after the language changed.</summary>
    private void ApplyTexts()
    {
        _refreshItem.Text = L.MenuRefresh;
        _thresholdItem.Text = L.MenuThreshold;
        _notifyConnItem.Text = L.MenuNotifyConnection;
        _autoSwitchItem.Text = L.MenuAutoSwitch;
        _renderFallbackItem.Text = L.MenuRenderFallback;
        _captureFallbackItem.Text = L.MenuCaptureFallback;
        _autostartItem.Text = L.MenuAutostart;
        _languageItem.Text = L.MenuLanguage;
        _languageAutoItem.Text = L.MenuLanguageAuto;
        foreach (ToolStripMenuItem item in _languageItem.DropDownItems)
            item.Checked = (string)item.Tag! == Settings.Language;
        _exitItem.Text = L.MenuExit;

        _statusItem.Text = StatusText(_currentState);
        _estimateItem.Text = EstimateText(_currentState);
        _notifyIcon.Text = _currentState.Connected
            ? $"{_statusItem.Text}\n{_estimateItem.Text}"
            : _statusItem.Text;
    }

    private void SetLanguage(string language)
    {
        Settings.Language = language;
        ApplyTexts();
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

        var auto = new ToolStripMenuItem(L.MenuAutoRemember) { Checked = getFixed() == null };
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
        string time = L.LastRefresh($"{_lastRefreshLocal:HH:mm:ss}");
        string text = _currentState switch
        {
            { Connected: true, Percent: { } p } =>
                $"{L.DetailsBattery(p, _currentState.Charging)}\n{_estimateItem.Text}\n{time}",
            { DonglePresent: true } => $"{L.StatusHeadsetOff}\n{time}",
            _ => $"{L.StatusDisconnected}\n{time}",
        };
        _notifyIcon.ShowBalloonTip(3000, L.AppTitle, text, ToolTipIcon.Info);
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
                ? state.Percent is { } p ? L.ConnectedWithBattery(p) : L.ConnectedPlain
                : L.Disconnected;
            _notifyIcon.ShowBalloonTip(3000, L.HeadsetTitle, text, ToolTipIcon.Info);
        }

        if (!Settings.AutoSwitchAudio)
            return;

        try
        {
            if (connected)
            {
                // remember current defaults (unless the headset is already the default)
                var d = _audio.GetDefaultIds();
                var headsetIds = _audio.EnumActiveEndpoints()
                    .Where(AudioDeviceManager.IsHeadset)
                    .Select(e => e.Id).ToHashSet();
                if (d.Render != null && !headsetIds.Contains(d.Render))
                    Settings.PrevRenderId = d.Render;
                if (d.Capture != null && !headsetIds.Contains(d.Capture))
                    Settings.PrevCaptureId = d.Capture;
                if (d.RenderComm != null && !headsetIds.Contains(d.RenderComm))
                    Settings.PrevRenderCommId = d.RenderComm;
                if (d.CaptureComm != null && !headsetIds.Contains(d.CaptureComm))
                    Settings.PrevCaptureCommId = d.CaptureComm;

                _audioSwitchRetries = 0;
                SwitchToHeadsetWithRetry();
            }
            else
            {
                _audioSwitchRetries = 0;
                // A fixed fallback device (if configured) takes precedence over the
                // auto-remembered one.
                _audio.SwitchToSpeakers(
                    Settings.FixedRenderId ?? Settings.PrevRenderId,
                    Settings.FixedCaptureId ?? Settings.PrevCaptureId,
                    Settings.PrevRenderCommId,
                    Settings.PrevCaptureCommId);
            }
        }
        catch (Exception ex)
        {
            _notifyIcon.ShowBalloonTip(3000, L.HeadsetTitle, L.SwitchFailed(ex.Message), ToolTipIcon.Error);
        }
    }

    /// <summary>
    /// Right after power-on the headset's USB audio endpoints may not be registered
    /// with Windows yet, so the first switch attempt can find nothing to switch to.
    /// Retry a few times while the headset stays connected.
    /// </summary>
    private void SwitchToHeadsetWithRetry()
    {
        bool switched;
        try
        {
            switched = _audio.SwitchToHeadset();
        }
        catch (Exception ex)
        {
            _notifyIcon.ShowBalloonTip(3000, L.HeadsetTitle, L.SwitchFailed(ex.Message), ToolTipIcon.Error);
            return;
        }
        if (switched || _audioSwitchRetries >= AudioSwitchMaxRetries)
            return;

        _audioSwitchRetries++;
        Task.Delay(AudioSwitchRetryDelayMs).ContinueWith(_ =>
        {
            if (_disposed)
                return;
            try
            {
                _invoker.BeginInvoke(() =>
                {
                    if (_wasConnected)
                        SwitchToHeadsetWithRetry();
                });
            }
            catch (ObjectDisposedException) { }
            catch (InvalidOperationException) { }
        });
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
                    _notifyIcon.ShowBalloonTip(3000, L.LowBatteryTitle,
                        L.LowBatteryBody(p), ToolTipIcon.Warning);
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
            _disposed = true;
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
