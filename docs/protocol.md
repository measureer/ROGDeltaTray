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
- `[6]`：电量百分比（0–100）。实测 0x4F=79、0x4E=78 与驱动显示一致
- `[7]`：低电量警告阈值（实测 0x1E=30，对应驱动里的低电提醒设置）
- `[8]`：恒为 0x01（含义未确定；插拔充电器不变，**不是**充电标志）
- 耳机关机/休眠时：查询超时或返回无效值 → 视为未连接

发送方式：对 COL04 接口 `WriteFile`（中断 OUT）后用 `ReadFile`（中断 IN）读响应，
**不需要** `HidD_GetInputReport`。查询/响应与 g-helper 中 ROG 鼠标的 `12 07` 电池查询同族。

## 充电状态（未解决）

`12 07` 响应在插/拔充电器时没有字节变化。充电状态可能走 COL02 的 RACE 指示
（ASUS 应用层事件 `CHARGING:13 / BATTERY_PERCENT:14`），待后续逆向。

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
