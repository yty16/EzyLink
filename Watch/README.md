# 易通遥 手表端（WearOS / Android）

通过 WiFi 直连电脑，远程控制 PPT 放映与系统基础操作。

> 本端为**纯 Java + Android 框架层**实现，**不依赖 AndroidX / Kotlin / Gradle / Maven**，
> 仅需 Android SDK（aapt2/d8/apksigner）与 JDK 17 即可出包，便于在任意环境构建与侧载。

## 功能
- 演示控制：开始 / 下一张 / 上一张 / 退出放映 / 黑屏 / 白屏
- 系统控制：锁定屏幕 / 睡眠 / 开机(WoL) / 关机 / 重启（关机重启带二次确认）
- 开机关机（Wake-on-LAN）：电脑关机状态下也能发魔术包唤醒

## 圆屏 / 方屏自适应
通过资源限定符实现，无需第三方控件：
- `res/values/dimens.xml`：默认（方屏）内边距 `scr_pad=8dp`
- `res/values-round/dimens.xml`：圆屏内边距 `scr_pad=40dp`，避免内容被圆形边缘裁切
- 主布局为 `ScrollView + LinearLayout`，内容超出时滚动，兼容不同尺寸手表

## 适配华为 Watch 4 Pro
- 华为 Watch 4 Pro 为**圆屏**（约 466×466），由 `values-round` 限定符自动应用更大内边距。
- 本端**不含 GMS（Google 移动服务）依赖**，仅用 Android 框架网络 API，可在 HarmonyOS 上侧载运行。
- 安装：手表开启「开发者模式 / 无线调试」，电脑执行
  `adb connect <手表IP>:5555` → `adb install watch-release.apk`

## 构建
需先安装 Android SDK（含 platform-tools / platforms;android-34 / build-tools;34.0.0）与 JDK 17。

```bash
# 设置 Android SDK 路径
set ANDROID_SDK_ROOT=E:/android-sdk
# 构建
python ../Tools/build_apk.py watch
# 产物：Watch/watch-release.apk
```
说明：aapt2 对中文路径的命令行参数支持不佳，构建脚本会先把本模块复制到
ASCII 临时目录 `C:/yty_build` 编译，再把 APK 拷回。版本号在 `Tools/build_apk.py`
的 `VERSIONS` 字典中统一管理（versionName + versionCode）。

## 使用
1. 电脑端（易通遥 Windows）启动服务，记下 **IP、端口、配对码**。
2. 手表端打开「易通遥」→「设置」，填写：
   - 电脑 IP（与电脑端显示的 IP 一致）
   - 端口（默认 7788）
   - 配对码（与电脑端一致）
   - MAC（仅使用「开机(WoL)」时才需要，填电脑端显示的 MAC）
3. 返回主界面即可点击各按钮。手表与电脑需处于**同一 WiFi / 热点**。
4. 关机 / 重启会弹出二次确认，避免误触。

## 说明
- 与 Windows 端共用文本 JSON、按行分隔的指令协议（见项目根 `Protocol.md`）。
- 每条指令单独建连（connect → auth → 指令 → 读响应），对间歇连接更健壮。
- 蓝牙（RFCOMM）通道为后续扩展，当前以 WiFi 为主。
- 版本号：**1.0.0.0**（遵循项目根 `VERSION` 的 `a.b.c.d` 规则，三端独立计数）。
- 图标使用**用户提供的原始图标**，未做任何重设计（仅转换为各密度 PNG）。
