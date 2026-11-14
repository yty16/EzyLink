# 易通遥 (YiTongYao) 远控协议 v1

软件分两端：

- **Windows 端**（主机，运行在被控制的电脑上）：启动一个本地 TCP 服务，接收并执行指令。
- **手表 APK 端**（WearOS，控制端）：通过 WiFi 直连（热点 / 同一局域网）连接 Windows 端并发送指令。

> 传输方式：WiFi（热点或局域网）TCP 为主通道，满足「WiFi 热点或蓝牙」中的 WiFi 直连需求。
> 蓝牙（RFCOMM）作为 secondary 通道的扩展方案见文末说明，本次交付以 WiFi 为主。

---

## 传输层

- 协议：TCP，文本 `UTF-8`，**每行一条 JSON**，以 `\n` 结尾（newline-delimited JSON）。
- 默认端口：`7788`（Windows 端可在界面修改）。
- 长连接：鉴权后可连续发送多条指令；服务端单连接串行处理。
- 编码示例：
  ```
  {"cmd":"auth","token":"12345678"}
  {"cmd":"ppt_next"}
  {"cmd":"lock"}
  ```

## 鉴权（握手）

连接建立后，**建议**首先发送 auth，否则部分高危指令（关机/重启）会被拒绝：

```json
{"cmd":"auth","token":"<配对码>"}
```

- 成功：`{"ok":true}`
- 失败：`{"ok":false,"err":"auth failed"}` 并断开连接。

配对码在 Windows 端界面显示/可修改，默认每次安装随机生成。

## 指令表

### 演示控制（PPT / 通用幻灯片）

| cmd | 含义 | 实现 |
|-----|------|------|
| `ppt_start` | 从当前幻灯片开始放映 | `F5` |
| `ppt_begin` | 从头开始放映 | `F5`（先回到首页） |
| `ppt_next` | 下一张 | `→` / `PageDown` / `Space` |
| `ppt_prev` | 上一张 | `←` / `PageUp` |
| `ppt_exit` | 退出放映 | `Esc` |
| `ppt_black` | 黑屏切换 | `B` |
| `ppt_white` | 白屏切换 | `W` |

> 通过 Win32 `keybd_event` 将按键发送到前台 PowerPoint 窗口，兼容 WPS 演示、Keynote(Windows) 等同样使用标准快捷键的软件，**无需安装 Office 互操作组件**。

### 系统控制

| cmd | 含义 | 实现 |
|-----|------|------|
| `lock` | 锁定工作站 | `LockWorkStation()` |
| `sleep` | 睡眠（待机） | `SetSuspendState(false,...)` |
| `hibernate` | 休眠 | `SetSuspendState(true,...)` |
| `shutdown` | 关机 | `shutdown.exe /s /f /t 0`（高危，手表端需二次确认） |
| `restart` | 重启 | `shutdown.exe /r /f /t 0`（高危，手表端需二次确认） |

### 元指令

| cmd | 响应 |
|-----|------|
| `ping` | `{"ok":true,"t":<ms>}` |
| `info` | `{"ok":true,"ip":"...","mac":"...","hostname":"..."}` |

## 响应格式

所有指令统一返回：

```json
{"ok":true}
{"ok":false,"err":"human readable reason"}
```

## 开机（Wake-on-LAN）

关机状态下电脑无法监听 TCP，因此「开机」由**手表端主动发送 WoL 魔术包**到电脑 MAC：

- 魔术包 = `FF FF FF FF FF FF` + 目标 MAC ×16，UDP 广播到 `255.255.255.255:9` 或 `:7`。
- 前置条件：电脑 BIOS/UEFI 开启 WoL，网卡属性允许「允许此设备唤醒计算机」并开启 WoL。
- 手表端需在设置里填写电脑 MAC（Windows 端界面会显示本机 MAC 方便抄写）。

## 自动发现（UDP 广播，端口 7789）

用于免去手填 IP。电脑端 TCP 服务启动后同时监听 UDP **7789**。

**请求（移动端 → 广播 / 单播）**

```
YTY_DISCOVER
```

**响应（电脑端 → 请求来源）**

```
{"type":"yty_here","host":"DESKTOP-XXX","port":7788,"version":"1.9.1.5","ips":["192.168.137.1","192.168.1.23"],"bt_available":true,"bt_mac":"4C:0F:3E:27:D4:AD","bt_uuid":"6E8F3A21-5C7B-4D9E-A1F2-0B3C4D5E6F70"}
```

| 字段 | 说明 |
|------|------|
| `type` | 固定 `yty_here`，移动端据此过滤其它 UDP 包 |
| `host` / `version` | 电脑名与电脑端版本号 |
| `port` | TCP 服务端口，默认 `7788` |
| `ips` | 电脑全部可用 IPv4 地址，**按「与请求方同网段优先」排序**，多网卡 / 多网段机器也能连对 |
| `bt_available` | 电脑端蓝牙 RFCOMM 通道是否已开启 |
| `bt_mac` / `bt_uuid` | 电脑蓝牙地址与服务 UUID，`bt_available=false` 时忽略 |

- 移动端拿到 `port` 与 `ips[0]` 后即可发起 TCP 连接并走 `auth` 握手。
- 移动端会自动保存 `bt_mac` / `bt_uuid` 作为蓝牙直连目标（无需手填）。
- 发现走 UDP，**完全离线可用**（电脑开移动热点、手机连该热点时同样能发现）。

## 扫码连接（二维码载荷格式）

用于免去手填 IP 与配对码。Windows 端生成二维码，手机 / 手表端扫描后自动写入设置并连接。

**载荷格式（UTF-8 字节模式）**

```
YTY1|<IP>|<端口>|<配对码>[|<电脑蓝牙 MAC>]
```

| 字段 | 说明 |
|------|------|
| `YTY1` | 固定前缀，标识易通遥配对码（版本号 1） |
| `<IP>` | 电脑 IPv4 地址，如 `192.168.137.1`（移动热点网关） |
| `<端口>` | TCP 服务端口，默认 `7788` |
| `<配对码>` | Windows 端显示的配对码，可为空（服务端未启用鉴权时） |
| `<电脑蓝牙 MAC>` | 可选第 5 段，电脑端开启蓝牙通道后自动附加；形如 `4C:0F:3E:27:D4:AD` |

示例：

```
YTY1|192.168.137.1|7788|12345678
YTY1|192.168.137.1|7788|12345678|4C:0F:3E:27:D4:AD
```

**多网卡处理**：电脑端对每个可用 IPv4 各生成一张二维码（热点网段 `192.168.137.x` 优先），
移动端扫到哪张就用哪个地址，避免多网段机器连错网卡。

**移动端容错解析**（超出标准格式也尽量识别）：

| 输入 | 解析结果 |
|------|----------|
| `YTY1|192.168.1.2|7788|abc123` | IP=192.168.1.2，端口=7788，配对码=abc123 |
| `192.168.1.2:7788|abc123` | IP=192.168.1.2，端口=7788，配对码=abc123 |
| `192.168.1.2:7788` | IP=192.168.1.2，端口=7788，配对码为空 |
| `YTY1|192.168.1.2|token=abc123` | IP=192.168.1.2，端口默认 7788，配对码=abc123 |
| `YTY1|192.168.1.2|7788|abc123|4C:0F:3E:27:D4:AD` | 同上，并写入电脑蓝牙 MAC 作为蓝牙直连目标 |

无法识别为易通遥配对码的内容，移动端会明确提示「这不是易通遥的配对二维码」，不会静默写入。

> 二维码生成：Windows 端 `Core/QrEncoder.cs`（自带实现，无第三方库）。
> 二维码识别：移动端内嵌 ZXing core（`com.google.zxing`，Apache-2.0，纯 Java，无外部依赖）。
> 第三方组件完整清单与许可证全文见 `THIRD_PARTY_NOTICES.md`。

## 连接保持

- 移动端与电脑端之间维持 **一条长连接**（TCP 或蓝牙 RFCOMM）：一次 `auth` 成功后可反复发送指令，无需每次握手。
- 连接断开后移动端按固定间隔自动重连（优先上次成功地址，再试蓝牙，最后走 UDP 发现）。
- 发送指令时若处于未连接状态，移动端会**明确提示并立即触发重连**，不会静默丢弃指令。

## 蓝牙直连（RFCOMM / SPP）

在没有 WiFi、也无法开热点的场景下，可改用蓝牙点对点通道。**应用层协议与 TCP 完全一致**（同一套按行 JSON + `auth` 配对码鉴权 + `SessionRunner` 会话层），仅底层传输从 TCP socket 换成 RFCOMM 流。

- Windows 端：`Core/WinRtBluetoothServer.cs` 用 WinRT `RfcommServiceProvider` + `StreamSocketListener` 起服务并广播 SDP 记录；`Core/WinRtSocketStream.cs` 把 WinRT `StreamSocket` 适配成 .NET `Stream`。
- 服务 UUID：`6E8F3A21-5C7B-4D9E-A1F2-0B3C4D5E6F70`（两端必须一致）。
- 移动端：`BtTransport` 用 `BluetoothSocket.createRfcommSocketToServiceRecord` 连接，失败后回退 insecure 方式；`Transport` 抽象让 `ConnectionManager` 在 TCP / 蓝牙之间自动择优。
- 连接方式 `conn_mode`：`auto`（保存的 IP 直连 → 蓝牙 → UDP 发现）/ `wifi`（仅 TCP）/ `bt`（仅蓝牙）。
- 蓝牙连接设 8 秒超时，设备不可达时不会卡住自动重连。
- 前提：电脑与手机 / 手表需先在操作系统中完成蓝牙配对；Android 12+ 需授予 `BLUETOOTH_CONNECT`。
- 链路加密：`SocketProtectionLevel.BluetoothEncryptionAllowNullAuthentication`，应用层配对码仍单独校验。
- 蓝牙通道与 TCP 通道可在电脑端同时开启，互不影响；关闭蓝牙不影响 WiFi 连接。

## Wi-Fi Direct 直连（Legacy AP）

与蓝牙不同，Wi-Fi Direct **不改变传输层**：它只是用 Wi-Fi Direct 硬件造出一条普通 IP 链路，链路建立后仍然走原有的 TCP `7788` / UDP `7789`，指令协议、`auth` 配对码鉴权和 `SessionRunner` 会话层完全不变。因此**移动端无需任何改动或新增权限**。

- Windows 端：`Core/WinRtWiFiDirectServer.cs` 用 WinRT `WiFiDirectAdvertisementPublisher` 广播 P2P 组，并开启 `Advertisement.LegacySettings`（Legacy AP 模式），把该组同时广播成普通 WiFi 网络。
- 网络名称固定为 `EzyLink-Direct`；WPA2 密码**就是电脑端配对码本身**（默认 8 位纯数字），界面可一键复制。重新生成或手改配对码后，热点密码同步变化。
- 配对码由加密安全随机数生成，长度 8 位，满足 WPA2 对密码长度的最低要求；从旧版（6 位）升级时自动换新码，移动端需重新扫码或更新。手动把配对码改短仍可能被设备拒绝。
- `IsAutonomousGroupOwnerEnabled = true` 让电脑作为组主并承担 DHCP；实测虚拟网卡 `Microsoft Wi-Fi Direct Virtual Adapter #2` 为 Up，地址 `192.168.137.1`。
- 移动端连上该 WiFi 后，UDP 广播发现（7789）可直接使用；发现不通时手填电脑在该网段地址，或用「扫码连接」切换网卡地址后扫码。
- 同时保留 `WiFiDirectConnectionListener` 接受标准 P2P 连接请求，连接建立后由 `WiFiDirectDevice.GetConnectionEndpointPairs()` 取得端点地址。
- **互斥限制**：微软文档明确 Windows 移动热点与 Wi-Fi Direct 不能同时运行，移动热点优先；开启本通道前需确认系统移动热点已关闭。
- 该热点不提供互联网接入，部分 Android / 手表系统会提示「无互联网」，需选择保持连接，否则可能自动回落到其它网络。
