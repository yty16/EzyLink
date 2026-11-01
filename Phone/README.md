# 易通遥 手机端（Android）

通过 WiFi 直连电脑，远程控制 PPT 放映与系统基础操作（竖屏大屏 UI）。

> 与手表端相同，本端为**纯 Java + Android 框架层**实现，不依赖 AndroidX / Kotlin /
> Gradle / Maven，仅需 Android SDK（aapt2/d8/apksigner）与 JDK 17 即可出包。

## 功能
- 演示控制：开始 / 下一张 / 上一张 / 退出放映 / 黑屏 / 白屏
- 系统控制：锁定屏幕 / 睡眠 / 开机(WoL) / 关机 / 重启（关机重启带二次确认）

## 构建
需先安装 Android SDK（含 platform-tools / platforms;android-34 / build-tools;34.0.0）与 JDK 17。

```bash
set ANDROID_SDK_ROOT=E:/android-sdk
python ../Tools/build_apk.py phone
# 产物：Phone/phone-release.apk
```
说明：aapt2 对中文路径参数支持不佳，构建脚本会先复制到 ASCII 临时目录 `C:/yty_build`
编译，再把 APK 拷回。版本号在 `Tools/build_apk.py` 的 `VERSIONS` 字典统一管理。

## 安装与使用
```bash
adb install Phone/phone-release.apk
```
1. 电脑端（易通遥 Windows）启动服务，记下 **IP、端口、配对码**。
2. 手机端打开「易通遥」→「设置」，填写 IP / 端口 / 配对码 / MAC（MAC 仅开机需要）。
3. 返回主界面点击各按钮。手机与电脑需处于**同一 WiFi / 热点**。

## 说明
- 与 Windows 端共用文本 JSON、按行分隔的指令协议（见项目根 `Protocol.md`）。
- 版本号：**1.0.0.0**（遵循项目根 `VERSION` 的 `a.b.c.d` 规则，三端独立计数）。
- 图标使用**用户提供的原始图标**，未做任何重设计（仅转换为各密度 PNG）。
