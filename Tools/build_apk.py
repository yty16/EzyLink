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
# 发布签名密钥（专用，替代调试密钥；便于商店更新签名一致、且非 debug 签名）
KEYSTORE = os.path.join(ROOT, "Tools", "release.keystore")
KEY_ALIAS = "ezykey"
_KEYPASS_FILE = os.path.join(ROOT, "Tools", ".release_key_pass")


def read_keypass():
    # 口令存于本地未提交文件，避免明文进入仓库
    if not os.path.exists(_KEYPASS_FILE):
        raise SystemExit("缺少签名口令文件：%s（请重新生成 release.keystore）" % _KEYPASS_FILE)
    with open(_KEYPASS_FILE, "r", encoding="ascii") as f:
        return f.read().strip()


# 三端版本号独立计数（a.b.c.d）：d=小改+1，b=更新+1
VERSIONS = {
    "watch": ("1.6.0.2", 1060002),
    "phone": ("1.4.0.2", 1040002),
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
    raise SystemExit("缺少发布签名密钥：%s（请运行 keytool 重新生成，别名 %s）" % (KEYSTORE, KEY_ALIAS))


def sync_tree(source, target):
    # 模块：增量同步源码/资源到 ASCII 临时目录
    os.makedirs(target, exist_ok=True)
    wanted = set()
    for root, _, files in os.walk(source):
        rel = os.path.relpath(root, source)
        dst = target if rel == "." else os.path.join(target, rel)
        os.makedirs(dst, exist_ok=True)
        for f in files:
            d = os.path.join(dst, f)
            shutil.copy2(os.path.join(root, f), d)
            wanted.add(os.path.normcase(os.path.abspath(d)))
    for root, _, files in os.walk(target):
        for f in files:
            p = os.path.join(root, f)
            if os.path.normcase(os.path.abspath(p)) not in wanted:
                os.remove(p)


def clean_outputs(out):
    # 模块：仅清理会被覆盖的中间产物，避免整目录删除
    for name in ("res.zip", "unsigned.apk", "withdex.apk", "aligned.apk",
                 "classes.jar", "classes.dex", "watch-release.apk", "phone-release.apk"):
        p = os.path.join(out, name)
        if os.path.isfile(p):
            os.remove(p)
    dex = os.path.join(out, "dex", "classes.dex")
    if os.path.isfile(dex):
        os.remove(dex)


def build(mod):
    vname, vcode = VERSIONS[mod]
    base = os.path.join(ROOT, mod)
    src = os.path.join(base, "src")
    res = os.path.join(base, "res")
    man = os.path.join(base, "AndroidManifest.xml")

    # aapt2 等原生工具对中文路径参数支持不佳，先复制到 ASCII 临时目录构建，再拷回产物
    stage = os.path.join("C:/yty_build", mod)
    os.makedirs(stage, exist_ok=True)
    stage_src = os.path.join(stage, "src")
    stage_res = os.path.join(stage, "res")
    stage_man = os.path.join(stage, "AndroidManifest.xml")
    sync_tree(src, stage_src)
    sync_tree(res, stage_res)
    shutil.copy2(man, stage_man)

    out = os.path.join(stage, "out")
    os.makedirs(out, exist_ok=True)
    clean_outputs(out)

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
    dexdir = os.path.join(out, "dex")
    os.makedirs(dexdir, exist_ok=True)

    # Windows 命令行上限约 8191 字符，直接把上百个 .class 绝对路径铺开会失败。
    # 先把本次编译产物打成 jar 再交给 d8；按源文件名筛掉历史遗留的 class。
    stems = set()
    for p in java_files:
        stems.add(os.path.splitext(os.path.basename(p))[0])
    jarfile = os.path.join(out, "classes.jar")
    with zipfile.ZipFile(jarfile, "w", zipfile.ZIP_DEFLATED) as z:
        for root, _, files in os.walk(classes):
            for f in files:
                if not f.endswith(".class") or f[:-6].split("$")[0] not in stems:
                    continue
                p = os.path.join(root, f)
                z.write(p, os.path.relpath(p, classes).replace("\\", "/"))
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

    # 7) apksigner 签名（发布密钥）
    staged_final = os.path.join(out, mod + "-release.apk")
    keypass = read_keypass()
    run([APKSIGNER, "sign", "--ks", KEYSTORE, "--ks-key-alias", KEY_ALIAS,
         "--ks-pass", "pass:" + keypass, "--key-pass", "pass:" + keypass,
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
