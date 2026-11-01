#!/usr/bin/env python3
# 易通遥 APK 构建脚本（免 Gradle / 免 Maven / 纯框架层）
# 仅依赖 Android SDK（platform + build-tools）与 JDK，可在任意装有 Android SDK 的机器上出包。
# 用法：python Tools/build_apk.py [watch|phone]   （缺省构建 watch + phone）
import os
import sys
import subprocess
import shutil
import zipfile

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))

SDK = (os.environ.get("ANDROID_SDK_ROOT") or os.environ.get("ANDROID_HOME")
       or r"E:/android-sdk")


def find_build_tools():
    bt_root = os.path.join(SDK, "build-tools")
    if not os.path.isdir(bt_root):
        raise SystemExit("找不到 Android SDK build-tools：%s" % bt_root)
    # 取版本号最大的 build-tools 目录
    best = None
    for name in os.listdir(bt_root):
        p = os.path.join(bt_root, name, "aapt2.exe")
        if os.path.exists(p):
            best = (name, os.path.join(bt_root, name))
    if best is None:
        raise SystemExit("build-tools 中未找到 aapt2.exe")
    return best[1]


BT = find_build_tools()
PLAT_JAR = os.path.join(SDK, "platforms", "android-34", "android.jar")
AAPT2 = os.path.join(BT, "aapt2.exe")
D8 = os.path.join(BT, "d8.bat")
ZIPALIGN = os.path.join(BT, "zipalign.exe")
APKSIGNER = os.path.join(BT, "apksigner.bat")
JAVA_HOME = os.environ.get("JAVA_HOME", r"C:/Program Files/Microsoft/jdk-17.0.19.10-hotspot")
KEYTOOL = os.path.join(JAVA_HOME, "bin", "keytool.exe")
KEYSTORE = os.path.join(ROOT, "Tools", "debug.keystore")

# 三端版本号独立计数（a.b.c.d）：d=小改+1，b=更新+1
VERSIONS = {
    "watch": ("1.5.1.2", 1050102),
    "phone": ("1.3.1.1", 1030101),
}

ENV = dict(os.environ)
ENV["JAVA_HOME"] = JAVA_HOME


def run(cmd, cwd=None):
    print(">>", " ".join(cmd))
    # 直接继承标准流，避免中文路径导致的 UTF-8 解码异常
    r = subprocess.run(cmd, cwd=cwd, env=ENV)
    if r.returncode != 0:
        raise SystemExit("命令失败（%s）：%s" % (r.returncode, " ".join(cmd)))
    return r


def ensure_keystore():
    if os.path.exists(KEYSTORE):
        return
    print("生成调试签名密钥：", KEYSTORE)
    run([KEYTOOL, "-genkeypair", "-v", "-keystore", KEYSTORE, "-alias", "android",
         "-keyalg", "RSA", "-keysize", "2048", "-validity", "10000",
         "-storepass", "android", "-keypass", "android",
         "-dname", "CN=YiTongYao,O=YiTongYao,C=CN"])


def build(mod):
    vname, vcode = VERSIONS[mod]
    base = os.path.join(ROOT, mod)
    src = os.path.join(base, "src")
    res = os.path.join(base, "res")
    man = os.path.join(base, "AndroidManifest.xml")

    # aapt2 等原生工具对中文路径参数支持不佳，先复制到 ASCII 临时目录构建，再拷回产物
    stage = os.path.join("C:/yty_build", mod)
    if os.path.isdir(stage):
        shutil.rmtree(stage)
    os.makedirs(stage, exist_ok=True)
    stage_src = os.path.join(stage, "src")
    stage_res = os.path.join(stage, "res")
    stage_man = os.path.join(stage, "AndroidManifest.xml")
    shutil.copytree(src, stage_src)
    shutil.copytree(res, stage_res)
    shutil.copy2(man, stage_man)

    out = os.path.join(stage, "out")
    os.makedirs(out, exist_ok=True)

    # 1) aapt2 编译资源
    reszip = os.path.join(out, "res.zip")
    run([AAPT2, "compile", "--dir", stage_res, "-o", reszip])

    # 2) aapt2 链接（注入版本号、生成 R.java）
    gen = os.path.join(out, "gen")
    unsign = os.path.join(out, "unsigned.apk")
    run([AAPT2, "link", "-o", unsign, "-I", PLAT_JAR, "--manifest", stage_man,
         "-R", reszip, "--java", gen,
         "--version-code", str(vcode), "--version-name", vname,
         "--auto-add-overlay"])

    # 3) javac 编译（源码 + 生成的 R.java）
    classes = os.path.join(out, "classes")
    os.makedirs(classes, exist_ok=True)
    java_files = []
    for d in (stage_src, gen):
        for root, _, files in os.walk(d):
            for f in files:
                if f.endswith(".java"):
                    java_files.append(os.path.join(root, f))
    # 使用 @argfile + 相对路径，避免源码数量增长后再次触发命令行长度限制
    argfile = os.path.join(stage, "sources.txt")
    with open(argfile, "w", encoding="utf-8") as af:
        for p in java_files:
            af.write(os.path.relpath(p, stage).replace("\\", "/") + "\n")

    run(["javac", "-encoding", "UTF-8", "--release", "8",
         "-cp", PLAT_JAR, "-d", os.path.relpath(classes, stage).replace("\\", "/"),
         "@sources.txt"], cwd=stage)

    # 4) d8 生成 classes.dex
    class_files = []
    for root, _, files in os.walk(classes):
        for f in files:
            if f.endswith(".class"):
                class_files.append(os.path.join(root, f))
    dexdir = os.path.join(out, "dex")
    os.makedirs(dexdir, exist_ok=True)

    # Windows 命令行上限约 8191 字符，直接把上百个 .class 绝对路径铺开会失败。
    # 这里先把 classes 打成 jar 再交给 d8，命令行长度与源码数量无关。
    jarfile = os.path.join(out, "classes.jar")
    if os.path.exists(jarfile):
        os.remove(jarfile)
    jar = os.path.join(JAVA_HOME, "bin", "jar.exe")
    run([jar, "cf", os.path.relpath(jarfile, classes).replace("\\", "/"), "."], cwd=classes)
    run([D8, "--release", "--min-api", "24", "--output",
         os.path.relpath(dexdir, stage).replace("\\", "/"),
         os.path.relpath(jarfile, stage).replace("\\", "/")], cwd=stage)

    # 5) 将 classes.dex 以「不压缩」方式塞入 apk
    dexfile = os.path.join(dexdir, "classes.dex")
    withdex = os.path.join(out, "withdex.apk")
    with zipfile.ZipFile(unsign, "r") as zin:
        with zipfile.ZipFile(withdex, "w", zipfile.ZIP_DEFLATED) as zout:
            for item in zin.infolist():
                zout.writestr(item, zin.read(item.filename))
            with open(dexfile, "rb") as df:
                dexdata = df.read()
            zi = zipfile.ZipInfo("classes.dex")
            zi.compress_type = zipfile.ZIP_STORED
            zout.writestr(zi, dexdata)

    # 6) zipalign 对齐
    aligned = os.path.join(out, "aligned.apk")
    run([ZIPALIGN, "-p", "4", withdex, aligned])

    # 7) apksigner 签名
    staged_final = os.path.join(out, mod + "-release.apk")
    run([APKSIGNER, "sign", "--ks", KEYSTORE, "--ks-key-alias", "android",
         "--ks-pass", "pass:android", "--key-pass", "pass:android",
         "--out", staged_final, aligned])

    # 拷回项目目录
    final = os.path.join(base, mod + "-release.apk")
    shutil.copy2(staged_final, final)
    print("已构建：%s （%d 字节）" % (final, os.path.getsize(final)))


if __name__ == "__main__":
    ensure_keystore()
    mods = sys.argv[1:]
    if not mods:
        mods = ["watch", "phone"]
    for m in mods:
        if m not in VERSIONS:
            print("未知模块：%s（可选: %s）" % (m, ", ".join(VERSIONS)))
            continue
        build(m)
