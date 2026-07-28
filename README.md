# ROGDeltaTray

在 Windows 任务栏托盘直接显示 **ROG Delta II（棱镜 2）** 耳机的实时电量——无需安装/运行 Armoury Crate。

A lightweight Windows tray app that shows your **ASUS ROG Delta II** headset's battery level right in the system tray — no Armoury Crate required.

![.NET 8](https://img.shields.io/badge/.NET-8-blue)
![Platform](https://img.shields.io/badge/platform-Windows%2010%2F11-lightgrey)
![License](https://img.shields.io/badge/license-MIT-green)

<!-- Screenshot: docs/screenshot.png -->

---

## 这个项目是干什么的 / What this project is

ROG Delta II 耳机本身没有屏幕，想看电量只能打开 Armoury Crate（奥创中心）——一个庞大、常驻后台、启动缓慢的软件。本项目通过逆向其 2.4G 无线接收器的 HID 协议，用一个 **几 MB 内存占用、单文件、即开即用** 的托盘小程序取代这个需求：电量数字直接画在托盘图标上，开机挂着就行，看一眼任务栏就知道还剩多少电。

除了看电量，它还顺手解决了一个日常痛点：**耳机开机/关机时自动切换 Windows 默认音频设备**。戴上耳机声音就过去，摘下耳机声音回到音箱，不用再手动进声音设置。

The ROG Delta II has no display of its own, and the only official way to check its battery is Armoury Crate — a heavy, slow, always-running suite. This project reverse-engineered the HID protocol of the headset's 2.4 GHz dongle and replaces that need with a **tiny single-file tray app**: the battery percentage is rendered directly onto the tray icon, so a glance at the taskbar tells you the level.

As a bonus, it also fixes a daily annoyance: **automatically switching the Windows default audio devices** when the headset powers on/off. Headset on → sound goes to the headset; headset off → sound returns to your speakers.

## 功能 / Features

- **托盘图标实时显示电量数字** / Battery percentage rendered into the tray icon
  - 连接时每 5 秒刷新；断开后改为每 2 秒探测，开机立刻能被发现（连接状态下轮询间隔 5s，断开状态 2s）
  - 电量 ≤20% 图标变红；耳机关机/断开显示灰色 ✕
  - 连续 3 次查询失败才判定为断开，单次链路抖动不会误报（去抖）
  - Refreshes every 5 s while connected (2 s while disconnected, so power-on is detected quickly); icon turns red at ≤ 20 % and shows a grey ✕ when disconnected; 3 consecutive failed polls are required before treating the headset as disconnected (debounce against transient link hiccups)
- **自动切换声音输入输出** / Automatic audio device switching
  - 耳机开机 → 默认播放 + 录音设备切到 ROG Delta II
  - 耳机关机 → 切回之前的音箱/麦克风（切换前的默认设备会被记住）
  - 通过 `IPolicyConfig` 设置 Windows 默认音频端点；可在右键菜单关闭此功能
  - Headset on → default playback *and* recording endpoints switch to ROG Delta II; headset off → switch back to your previously remembered devices (via `IPolicyConfig`); can be disabled from the tray menu
- **右键菜单** / Tray menu：查看电量、立即刷新、开关自动切换、开关开机自启、退出
- **低电量气泡提醒** / Low-battery balloon notification at ≤ 20 %（电量回升后重置，会再次提醒）
- **可选开机自启** / Optional auto-start with Windows（写注册表 `Run` 键，仅当前用户）

## 下载与使用 / Download & Usage

1. 到 [Releases](https://github.com/measureer/ROGDeltaTray/releases) 页面下载 `ROGDeltaTray-v0.1.0-win-x64.zip`
2. 解压，双击 `RogBatteryTray.exe` 即可（单文件自包含，**无需安装 .NET 运行时**）
3. 建议配合右键菜单里的「开机启动」使用

Download the zip from [Releases](https://github.com/measureer/ROGDeltaTray/releases), extract, and double-click `RogBatteryTray.exe`. It is a single self-contained file — no .NET runtime installation needed. Enabling "开机启动" (auto-start) from the tray menu is recommended.

## 系统要求 / Requirements

- Windows 10 / 11（x64）
- ASUS ROG Delta II 耳机（接收器 `VID 0B05 / PID 1AFA`），通过 **2.4G 无线接收器** 连接（蓝牙模式下无法查询）

## 自行构建 / Build from source

需要 .NET 8 SDK：

```
dotnet publish src/RogBatteryTray -c Release -r win-x64 --self-contained -p:PublishSingleFile=true
```

产物 / Output: `src/RogBatteryTray/bin/Release/net8.0-windows/win-x64/publish/RogBatteryTray.exe`

## 原理 / How it works

不依赖 Armoury Crate，直接通过 USB HID 与 2.4G 接收器通信：向接收器的 vendor-defined 接口（COL04，报告 `0xCC`）发送 ASUS 外设统一查询命令 `CC 12 07`（与 ROG 鼠标的 `12 07` 电量查询同族），响应第 6 字节即电量百分比（0–100），第 7 字节是驱动里设置的低电量警告阈值。耳机关机时查询超时，据此判断连接状态。

音频切换部分使用 Windows Core Audio API 枚举端点，并通过未公开但广泛使用的 `IPolicyConfig` COM 接口设置默认设备。

完整逆向过程与协议细节（含 RACE 通道、各 HID collection 布局）见 [docs/protocol.md](docs/protocol.md)。

The app talks directly to the 2.4 GHz dongle over USB HID instead of relying on Armoury Crate: it sends the ASUS peripheral unified query `CC 12 07` (same family as the ROG mouse battery query) to the dongle's vendor-defined interface (COL04, report `0xCC`); byte 6 of the response is the battery percentage (0–100) and byte 7 is the low-battery warning threshold configured in the driver. Queries time out when the headset is off, which is how the connection state is detected.

Audio switching enumerates endpoints with the Windows Core Audio API and sets the defaults through the undocumented but widely used `IPolicyConfig` COM interface.

See [docs/protocol.md](docs/protocol.md) for the full reverse-engineering notes (RACE channel, HID collection layout, etc.).

## 项目结构 / Project structure

- `src/RogBatteryTray/` — 托盘应用本体（.NET 8 WinForms + HidSharp）。程序集名仍为 `RogBatteryTray`，`ROGDeltaTray` 是仓库/项目名
- `tools/HidProbe/` — HID 探测/协议验证工具（枚举设备、监听、RACE 查询/扫描），逆向时用
- `tools/WsProbe/` — ASUS 本地 WebSocket 协议探测工具（调研用，最终未采用该方案）
- `docs/protocol.md` — 逆向出的协议文档

## 已知限制 / Known limitations

- **充电状态标志未破解**：`12 07` 响应在插/拔充电器时没有字节变化，托盘暂不显示「充电中」（推测走 RACE 指示，见协议文档，欢迎 PR）
- **仅适配 ROG Delta II**（`VID 0B05 / PID 1AFA`）；同架构的 ROG Cetra SpeedNova（`PID 1AD3`）很可能通用但未经实测，其他型号需按 [docs/protocol.md](docs/protocol.md) 的方法重新确认
- 蓝牙耳机模式下无法查询电量（协议走 2.4G 接收器）

## 致谢 / Acknowledgements

- [g-helper](https://github.com/seerge/g-helper) — ROG 鼠标 `12 07` 电量查询的参考实现
- [race-toolkit](https://github.com/auracast-research/race-toolkit) 与 [Airoha RACE 协议分析](https://insinuator.net/2025/12/bluetooth-headphone-jacking-full-disclosure-of-airoha-race-vulnerabilities/)

## License

[MIT](LICENSE)
