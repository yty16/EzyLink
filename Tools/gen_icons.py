#!/usr/bin/env python3
# 易通遥 / EzyLink 应用图标生成器（手机 squircle + 手表 squircle/round + 商店素材）
# 依赖 Pillow 与系统 CJK 字体（SimHei / Microsoft YaHei）。
import os
import math
from PIL import Image, ImageDraw, ImageFont

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))

# 品牌色
NAVY_TOP = (17, 19, 38)      # #111326 顶部深蓝
NAVY_BOT = (27, 42, 90)      # #1B2A5A 底部靛蓝
CYAN = (54, 226, 232)        # #36E2E8 品牌青
WHITE = (255, 255, 255)
FONT = r"C:/Windows/Fonts/simhei.ttf"   # 黑体，粗且清晰


def load_font(px):
    return ImageFont.truetype(FONT, px)


def vgrad(size, top, bot):
    img = Image.new("RGBA", (size, size), top + (255,))
    d = ImageDraw.Draw(img)
    for y in range(size):
        t = y / max(1, size - 1)
        c = tuple(int(top[i] + (bot[i] - top[i]) * t) for i in range(3))
        d.line([(0, y), (size, y)], fill=c + (255,))
    return img


def glyph_centered(size, text, color, ratio=0.56, dy=0.0):
    """在透明图层上绘制居中文案，返回图层。"""
    layer = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    d = ImageDraw.Draw(layer)
    fs = int(size * ratio)
    f = load_font(fs)
    bbox = d.textbbox((0, 0), text, font=f)
    w = bbox[2] - bbox[0]
    h = bbox[3] - bbox[1]
    x = (size - w) / 2 - bbox[0]
    y = (size - h) / 2 - bbox[1] + size * dy
    # 阴影
    sd = int(size * 0.012) + 1
    d.text((x + sd, y + sd), text, font=f, fill=(0, 0, 0, 110))
    d.text((x, y), text, font=f, fill=color + (255,))
    return layer


def accent_bar(size):
    """底部品牌青短横，象征「连接」。"""
    layer = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    d = ImageDraw.Draw(layer)
    bw = int(size * 0.40)
    bh = max(2, int(size * 0.045))
    x0 = (size - bw) / 2
    y0 = size * 0.80
    d.rounded_rectangle([x0, y0, x0 + bw, y0 + bh], radius=bh / 2, fill=CYAN + (255,))
    return layer


def make_squircle(size):
    img = vgrad(size, NAVY_TOP, NAVY_BOT)
    d = ImageDraw.Draw(img)
    r = int(size * 0.22)
    m = int(size * 0.04)
    d.rounded_rectangle([m, m, size - m, size - m], radius=r,
                        outline=CYAN + (70,), width=max(1, int(size * 0.012)))
    base = glyph_centered(size, "遥", WHITE, ratio=0.56, dy=-0.02)
    img = Image.alpha_composite(img, base)
    img = Image.alpha_composite(img, accent_bar(size))
    return img


def make_circle(size):
    img = vgrad(size, NAVY_TOP, NAVY_BOT)
    d = ImageDraw.Draw(img)
    m = int(size * 0.03)
    d.ellipse([m, m, size - m, size - m], outline=CYAN + (80,), width=max(1, int(size * 0.012)))
    base = glyph_centered(size, "遥", WHITE, ratio=0.54, dy=-0.01)
    img = Image.alpha_composite(img, base)
    img = Image.alpha_composite(img, accent_bar(size))
    return img


def save(img, path):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    img.save(path, "PNG")
    print("saved", path, img.size)


# 五档密度
DENS = {"mdpi": 48, "hdpi": 72, "xhdpi": 96, "xxhdpi": 144, "xxxhdpi": 192}

# 手机：squircle（各密度）
for name, s in DENS.items():
    save(make_squircle(s), os.path.join(ROOT, "Phone", "res", "mipmap-" + name, "ic_launcher.png"))

# 手表：squircle + round
for name, s in DENS.items():
    save(make_squircle(s), os.path.join(ROOT, "Watch", "res", "mipmap-" + name, "ic_launcher.png"))
    save(make_circle(s), os.path.join(ROOT, "Watch", "res", "mipmap-" + name, "ic_launcher_round.png"))

# 商店 512 图标（squircle，高分辨率）
save(make_squircle(512), os.path.join(ROOT, "dist", "store-icon-512.png"))

# 特色图 1024x500
FW, FH = 1024, 500
feat = Image.new("RGBA", (FW, FH), NAVY_TOP + (255,))
cx, cy = 250, FH // 2
# 柔光
glow = Image.new("RGBA", (FW, FH), (0, 0, 0, 0))
gd = ImageDraw.Draw(glow)
for r in range(300, 0, -8):
    a = int(44 * (1 - r / 300.0))
    if a <= 0:
        continue
    gd.ellipse([cx - r, cy - r, cx + r, cy + r], fill=CYAN + (a,))
feat = Image.alpha_composite(feat, glow)
# 之后所有绘制都基于合成后的 feat
fd = ImageDraw.Draw(feat)
# 圆形徽标：实心圆（略亮于背景）+ 青色环 + 白色「遥」
fd.ellipse([cx - 150, cy - 150, cx + 150, cy + 150], fill=NAVY_BOT + (255,))
fd.ellipse([cx - 150, cy - 150, cx + 150, cy + 150], outline=CYAN + (200,), width=8)
glyph = glyph_centered(500, "遥", WHITE, ratio=0.46, dy=0.0)
feat.alpha_composite(glyph, (0, 0))
# 分隔线
fd.line([(505, 130), (505, 370)], fill=CYAN + (70,), width=2)
# 右侧文字块（垂直居中）
fd.text((545, 130), "易通遥", font=load_font(110), fill=WHITE + (255,))
fd.text((549, 262), "EzyLink", font=load_font(58), fill=CYAN + (255,))
fd.text((551, 350), "手机·手表 远程控制电脑", font=load_font(38), fill=(210, 225, 255, 255))
feat.save(os.path.join(ROOT, "dist", "store-feature-1024x500.png"), "PNG")
print("saved store-feature-1024x500.png")

print("DONE")
