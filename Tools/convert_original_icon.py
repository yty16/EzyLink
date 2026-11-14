#!/usr/bin/env python3
# 图标格式转换与去白角，输出 ico/png 及各密度 launcher。
import os
import collections
from PIL import Image, ImageFilter, ImageDraw, ImageChops

SRC = r"C:/Users/Yin/.workbuddy/blobs/da/da0afa7a64f822ab0c5b108e2a184dec94f5749b23cc3092a93fb8d3c14b07bc.jpg"
ROOT = r"E:/workbuddy/易通遥"
WIN_ASSETS = os.path.join(ROOT, "Windows/YiTongYao.Windows/Assets")
WATCH_RES = os.path.join(ROOT, "Watch/res")
PHONE_RES = os.path.join(ROOT, "Phone/res")

assert os.path.exists(SRC), f"原始图标不存在: {SRC}"

WHITE_TH = 245        # 接近纯白判定阈值
CORNER_RATIO = 0.10   # 圆角半径占边长比例（略大于原图 ~5.3%，确保角落绝对透明）


def is_white(px):
    return px[0] > WHITE_TH and px[1] > WHITE_TH and px[2] > WHITE_TH


def remove_outer_white(img):
    """把与四角连通的白色区域透明化，返回 RGBA 图像。"""
    w, h = img.size
    src = img.load()
    white = bytearray(w * h)
    for y in range(h):
        for x in range(w):
            white[y * w + x] = 1 if is_white(src[x, y]) else 0
    transparent = bytearray(w * h)
    visited = bytearray(w * h)
    for (cx, cy) in [(0, 0), (w - 1, 0), (0, h - 1), (w - 1, h - 1)]:
        if not white[cy * w + cx]:
            continue
        dq = collections.deque([(cx, cy)])
        while dq:
            x, y = dq.popleft()
            idx = y * w + x
            if visited[idx]:
                continue
            visited[idx] = 1
            if not white[idx]:
                continue
            transparent[idx] = 1
            if x > 0:
                dq.append((x - 1, y))
            if x < w - 1:
                dq.append((x + 1, y))
            if y > 0:
                dq.append((x, y - 1))
            if y < h - 1:
                dq.append((x, y + 1))
    out = Image.new("RGBA", (w, h))
    opx = out.load()
    for y in range(h):
        for x in range(w):
            r, g, b = src[x, y]
            a = 0 if transparent[y * w + x] else 255
            opx[x, y] = (r, g, b, a)
    # alpha 边缘抗锯齿
    a = out.split()[3].filter(ImageFilter.GaussianBlur(0.6))
    out.putalpha(a.point(lambda v: 0 if v < 40 else (255 if v > 200 else v)))
    return out


def round_corner_mask(size, ratio=CORNER_RATIO):
    w = h = size
    r = max(1, int(round(size * ratio)))
    m = Image.new("L", (w, h), 0)
    ImageDraw.Draw(m).rounded_rectangle([0, 0, w - 1, h - 1], radius=r, fill=255)
    return m


def make_icon(base, size):
    """缩放到目标尺寸并施加圆角遮罩，保证角落绝对透明。"""
    resized = base.resize((size, size), Image.LANCZOS)
    # 二值化 alpha，消除半透明残留
    a = resized.split()[3].point(lambda v: 0 if v < 120 else 255)
    mask = round_corner_mask(size)
    resized.putalpha(ImageChops.multiply(a, mask))
    return resized


print("读取原始图标...")
base = remove_outer_white(Image.open(SRC).convert("RGB"))
print("源尺寸:", base.size, " 已去白角(圆角外透明)")

# Windows .ico / png
ico_path = os.path.join(WIN_ASSETS, "vty_icon.ico")
png_path = os.path.join(WIN_ASSETS, "vty_icon.png")
ico_sizes = [(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)]
masked = make_icon(base, 256)  # 角落纯透明的母图
masked.save(png_path)
masked.save(ico_path, sizes=ico_sizes)
print("已生成", ico_path, "与", png_path)

# Watch/Phone 各密度 launcher png
dpi = {"mdpi": 48, "hdpi": 72, "xhdpi": 96, "xxhdpi": 144, "xxxhdpi": 192}
for mod, res in [("watch", WATCH_RES), ("phone", PHONE_RES)]:
    for name, size in dpi.items():
        d = os.path.join(res, f"mipmap-{name}")
        os.makedirs(d, exist_ok=True)
        out = os.path.join(d, "ic_launcher.png")
        make_icon(base, size).save(out)
        print("已生成", out)

print("全部完成")
