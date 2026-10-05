using System.Globalization;

namespace RogBatteryTray;

/// <summary>
/// UI strings in Chinese and English. The active language follows Settings.Language
/// ("auto" = Windows display language) and is evaluated per access, so changing the
/// setting takes effect without a restart.
/// </summary>
public static class L
{
    public static bool English => Settings.Language switch
    {
        "zh" => false,
        "en" => true,
        _ => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName != "zh",
    };

    public static string AppTitle => English ? "ROG Headset Battery" : "ROG 耳机电量";
    public static string HeadsetTitle => English ? "ROG Headset" : "ROG 耳机";

    // status line
    public static string StatusBattery(int p, bool charging) =>
        English ? $"Battery: {p}%" + (charging ? " (charging)" : "")
                : $"电量: {p}%" + (charging ? "（充电中）" : "");
    public static string StatusBatteryUnknown => English ? "Battery: --" : "电量: --";
    public static string StatusHeadsetOff =>
        English ? "Headset off (receiver connected)" : "耳机未开机（接收器已连接）";
    public static string StatusDisconnected => English ? "Headset not connected" : "耳机未连接";

    // runtime estimate
    public static string EstimateNone => English ? "Drain rate: --" : "耗电速率: --";
    public static string EstimateCollecting =>
        English ? "Drain rate: collecting data" : "耗电速率: 数据收集中";
    public static string EstimateCharging => English ? "Charging" : "充电中";
    public static string EstimateRemaining(string duration) =>
        English ? $"Est. remaining: {duration}" : $"预计剩余: {duration}";
    public static string EstimateFullIn(string duration) =>
        English ? $"Fully charged in: {duration}" : $"预计充满: {duration}";

    public static string FormatDuration(TimeSpan t)
    {
        if (t.TotalHours >= 1)
            return English ? $"{(int)t.TotalHours} h {t.Minutes} min" : $"{(int)t.TotalHours} 小时 {t.Minutes} 分钟";
        return English ? $"{Math.Max(t.Minutes, 1)} min" : $"{Math.Max(t.Minutes, 1)} 分钟";
    }

    // menu items
    public static string MenuRefresh => English ? "Refresh now" : "立即刷新";
    public static string MenuThreshold => English ? "Low-battery threshold" : "低电量阈值";
    public static string MenuNotifyConnection =>
        English ? "Connect/disconnect notifications" : "连接/断开提示";
    public static string MenuAutoSwitch =>
        English ? "Auto-switch audio in/out" : "自动切换声音输入输出";
    public static string MenuRenderFallback => English ? "Fallback playback device" : "回切播放设备";
    public static string MenuCaptureFallback => English ? "Fallback recording device" : "回切录音设备";
    public static string MenuAutoRemember => English ? "Auto (remember last)" : "自动记忆";
    public static string MenuAutostart => English ? "Run at startup" : "开机启动";
    public static string MenuLanguage => English ? "Language" : "语言";
    public static string MenuLanguageAuto => English ? "System default" : "跟随系统";
    public static string MenuExit => English ? "Exit" : "退出";

    // details balloon (left click)
    public static string DetailsBattery(int p, bool charging) =>
        English ? $"Battery {p}%" + (charging ? " (charging)" : "")
                : $"电量 {p}%" + (charging ? "（充电中）" : "");
    public static string LastRefresh(string time) =>
        English ? $"Last refresh: {time}" : $"上次刷新: {time}";

    // connection balloons
    public static string ConnectedWithBattery(int p) =>
        English ? $"Headset connected, battery {p}%" : $"耳机已连接，电量 {p}%";
    public static string ConnectedPlain => English ? "Headset connected" : "耳机已连接";
    public static string Disconnected => English ? "Headset disconnected" : "耳机已断开";
    public static string SwitchFailed(string message) =>
        English ? $"Failed to switch audio devices: {message}" : $"切换音频设备失败：{message}";

    // low battery balloon
    public static string LowBatteryTitle => English ? "ROG Headset battery low" : "ROG 耳机电量低";
    public static string LowBatteryBody(int p) =>
        English ? $"Battery at {p}% — please charge soon." : $"剩余电量 {p}%，请及时充电。";

    // Program.cs
    public static string AlreadyRunning =>
        English ? "RogBatteryTray is already running." : "RogBatteryTray 已在运行。";
}
