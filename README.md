# ROGDeltaTray

Show your ASUS ROG Delta II headset battery level right in the Windows system tray — no Armoury Crate required.

![.NET 8](https://img.shields.io/badge/.NET-8-blue)
![Platform](https://img.shields.io/badge/platform-Windows%2010%2F11-lightgrey)
![License](https://img.shields.io/badge/license-MIT-green)

<!-- Screenshot: docs/screenshot.png -->

## Features

- **Battery percentage in the tray icon** — the icon itself renders the current level as digits, refreshed every 5 seconds. Turns red at ≤ 20 %, and shows a grey ✕ when the headset disconnects.
- **Automatic audio device switching** — when the headset powers on, the default playback *and* recording devices are switched to ROG Delta II; when it powers off, Windows switches back to your previous speakers / microphone (via `IPolicyConfig`). The previous defaults are remembered, and the feature can be disabled from the tray menu.
- **Battery details on hover / right-click menu**, with manual refresh.
- **Low-battery balloon notification** at ≤ 20 %.
- **Optional auto-start with Windows** (right-click menu toggle, writes the registry `Run` key).

## Requirements

- Windows 10 / 11
- ASUS ROG Delta II headset (VID `0B05` / PID `1AFA`) connected through its **2.4 GHz wireless dongle**

## Download

Grab the ready-to-use single-file exe from the [Releases](https://github.com/measureer/ROGDeltaTray/releases) page — no .NET runtime installation needed.

## Build from source

```
dotnet publish src/RogBatteryTray -c Release -r win-x64 --self-contained -p:PublishSingleFile=true
```

Output: `src/RogBatteryTray/bin/Release/net8.0-windows/win-x64/publish/RogBatteryTray.exe`. Double-click to run.

## How it works

The app talks directly to the 2.4 GHz dongle over USB HID (no dependency on Armoury Crate running): it sends the ASUS peripheral unified query `CC 12 07` to the dongle's vendor-defined interface (COL04, report `0xCC`), and byte 6 of the response is the battery percentage. The fully reverse-engineered protocol is documented in [docs/protocol.md](docs/protocol.md).

## Project structure

- `src/RogBatteryTray/` — the tray app (.NET 8 WinForms + HidSharp). The assembly is still named `RogBatteryTray`; `ROGDeltaTray` is the project/repository name.
- `tools/HidProbe/` — HID probing / protocol verification tool (enumerate, listen, RACE query/scan)
- `tools/WsProbe/` — ASUS local WebSocket protocol probe (research only, that approach was not adopted)
- `docs/protocol.md` — reverse-engineered protocol documentation

## Known limitations

- Charging-state flag has not been cracked yet, so the tray does not indicate charging (see the protocol doc).
- Only the ROG Delta II (VID `0B05` / PID `1AFA`) is supported; other models would need re-verification using the method in [docs/protocol.md](docs/protocol.md).

---

## 中文说明

在 Windows 任务栏托盘直接显示 ROG Delta II（棱镜 2）耳机的电量百分比，不用打开 Armoury Crate。

- 托盘图标直接显示电量数字（每 5 秒刷新，≤20% 变红，断开变灰 ✕）
- 检测到耳机开/关机时，自动在耳机与之前的音箱/麦克风之间切换 Windows 默认播放和录音设备（可在右键菜单关闭）
- 悬停/右键菜单查看电量、手动刷新；低电量气泡提醒；可选开机自启

**使用**：到 [Releases](https://github.com/measureer/ROGDeltaTray/releases) 页面下载单文件 exe，双击即用，无需安装 .NET 运行时。自行构建见上方 *Build from source*。

**原理**：通过 USB HID 直接向 2.4G 接收器发送查询 `CC 12 07`，响应第 6 字节即电量百分比，完整协议见 [docs/protocol.md](docs/protocol.md)。

**限制**：充电状态标志未破解，暂不显示充电中；仅适配 ROG Delta II。

## License

[MIT](LICENSE)
