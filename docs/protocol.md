# ROG Delta II（棱镜 2）电量 HID 协议

逆向日期：2026-07-26。适用设备：ROG Delta II 2.4GHz 接收器（`VID_0B05 & PID_1AFA`）。
同架构的 ROG Cetra SpeedNova（`PID_1AD3`）很可能通用（同样的 HID 布局，见 g-helper issue #2867）。

## 接口布局（MI_03，4 个 HID collection）

| Collection | Usage Page | 报告 | 用途 |
|---|---|---|---|
| COL01 | 0x000C (Consumer) | 媒体键 | 音量/播放控制 |
| COL02 | 0xFF13 (vendor) | Out 0x06 / In 0x07，61 字节 | Airoha RACE 通道（固件、EQ、ANC 等） |
| COL03 | 0x000B (Telephony) | 2 字节 | 耳机 Hook/Mute 状态 |
| COL04 | 0xFF00 (vendor) | 0xCC 双向，64 字节 | **电量等状态查询（ASUS 外设统一协议，同 ROG 鼠标）** |

## 电量查询（COL04，报告 0xCC）

请求（64 字节，其余补 0）：

```
CC 12 07 00 00 ... 
```

响应：

```
CC 12 07 00 00 05 <percent> <lowBattThreshold> 01 00 ...
```

- `[1]=0x12 [2]=0x07`：命令回显，用于校验
- `[5]`：睡眠定时器（分钟）。实测 0x05，对应驱动里的"5 分钟无操作休眠"设置（g-helper 同此解读）
- `[6]`：电量百分比（0–100）。实测 0x4F=79、0x4E=78 与驱动显示一致
- `[7]`：低电量警告阈值（实测 0x1E=30，对应驱动里的低电提醒设置）
- `[8]`：低电量语音提示开关（0x01=开；g-helper 称之为 lowBatteryPrompt，**不是**充电标志）
- 耳机关机/休眠时：查询超时或返回无效值 → 视为未连接
- 固件 NAK：响应 `[1]=0xFF [2]=0xAA`（或 `[5]=0xFF [6]=0xAA`）表示命令被拒绝
- 接收器会主动推异步事件包（`[1][2]` 不是命令回显），读取时需跳过

发送方式：对 COL04 接口 `WriteFile`（中断 OUT）后用 `ReadFile`（中断 IN）读响应，
**不需要** `HidD_GetInputReport`。查询/响应与 g-helper 中 ROG 鼠标的 `12 07` 电池查询同族。
发送前应先清空输入队列里的残留报告（g-helper 的 Drain），否则容易读到上一次的旧响应。

## 充电状态（已通过 g-helper 解决）

单独发送 `CC 12 08` 查询，响应 `[5]==1` 表示充电中（g-helper `AsusHeadset.ReadBattery` /
`ParseCharging` 在 Delta II 上量产验证）。`12 07` 响应本身确实不含充电标志。

实测（耳机开机、未插充电器）：`CC 12 08 00 00 00 00 ...`（`[5]=0`）；耳机关机时
`12 07` / `12 08` 均返回 NAK（`[5]=0xFF [6]=0xAA`），这是"接收器在线、耳机关机"的判别特征。

## 接口识别（g-helper 方式）

不依赖 Windows 路径里的 `col04` 后缀：按 VID/PID 枚举后，用
`GetMaxOutputReportLength() >= 64` 且报告描述符中含 usage page 0xFF00 的 collection 来定位。
插拔检测可用 HidSharp `DeviceList.Local.Changed`（底层 WM_DEVICECHANGE）事件驱动，无需纯轮询。

## RACE 通道（COL02，报告 0x06/0x07，备查）

- 发送：`[0x06][len_lo][len_hi][RACE 包]`；RACE 包头 `[head=0x05][type][len:2][cmd:2]`（len 含 cmd 两字节）
- 接收：需用 `HidD_GetInputReport(0x07)` 轮询（设备几乎不主动推中断 IN）
- type：0x5A 查询 / 0x5B 响应 / 0x5C 无响应命令 / 0x5D 指示
- 已确认命令：`0x1E08` 取固件版本（AB156x, AIoT_SDK_for_BT_Audio_V3.8.0）；
  `0x2C80` 状态查询；`0x2C92/93/94/95/97/98/B2/B3` 设备信息（名称、地址等）
- 电量**不在**已扫描的 0x2C70–0x2CCF 范围内

## 参考

- g-helper（ROG 鼠标 `12 07` 电池查询）：https://github.com/seerge/g-helper
- Airoha RACE 协议分析：https://insinuator.net/2025/12/bluetooth-headphone-jacking-full-disclosure-of-airoha-race-vulnerabilities/
- race-toolkit（RACE USB 传输实现）：https://github.com/auracast-research/race-toolkit
