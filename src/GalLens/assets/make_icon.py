"""生成 GalLens 图标：深色圆角底 + 白色「汉」字。

字体：Ma Shan Zheng（马善政毛笔楷书），SIL Open Font License 1.1
      https://fonts.google.com/specimen/Ma+Shan+Zheng

font 文件不随仓库分发。重新生成图标前，先下载并设置环境变量：
    set GALLENS_ICON_FONT=D:\path\MaShanZheng-Regular.ttf
    python make_icon.py
"""
import os
import sys

from PIL import Image, ImageDraw, ImageFont

S = 1024
CHAR = "汉"
OUT_DIR = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(OUT_DIR, "..", "..", ".."))

c1 = (66, 84, 196)     # 靛蓝
c2 = (112, 76, 200)    # 紫

def find_font():
    env = os.environ.get("GALLENS_ICON_FONT")
    if env and os.path.isfile(env):
        return env
    for p in [
        os.path.join(ROOT, "_fonts", "MaShanZheng-Regular.ttf"),
        os.path.join(os.path.expanduser("~"), "MaShanZheng-Regular.ttf"),
    ]:
        if os.path.isfile(p):
            return p
    sys.exit("找不到字体，请设置 GALLENS_ICON_FONT 指向 MaShanZheng-Regular.ttf")

font_path = find_font()

grad = Image.new("RGB", (S, S))
gd = ImageDraw.Draw(grad)
for y in range(S):
    t = y / (S - 1)
    gd.line([(0, y), (S, y)], fill=(
        int(c1[0] + (c2[0] - c1[0]) * t),
        int(c1[1] + (c2[1] - c1[1]) * t),
        int(c1[2] + (c2[2] - c1[2]) * t),
    ))

mask = Image.new("L", (S, S), 0)
ImageDraw.Draw(mask).rounded_rectangle([0, 0, S - 1, S - 1], radius=int(S * 0.23), fill=255)

img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
img.paste(grad, (0, 0), mask)

d = ImageDraw.Draw(img)
size = int(S * 0.86)
font = ImageFont.truetype(font_path, size)
d.text((S / 2, S / 2 + S * 0.015), CHAR, font=font, anchor="mm", fill=(255, 255, 255, 255))

img.resize((256, 256), Image.LANCZOS).save(os.path.join(OUT_DIR, "icon-square.png"))

preview = Image.new("RGBA", (512, 512), (243, 243, 246, 255))
preview.alpha_composite(img.resize((512, 512), Image.LANCZOS))
preview.convert("RGB").save(os.path.join(ROOT, "docs", "icon.png"))

small = Image.new("RGB", (250, 70), (243, 243, 246))
x = 10
for px in (64, 48, 32, 16):
    im = img.resize((px, px), Image.LANCZOS)
    small.paste(im, (x, (70 - px) // 2), im)
    x += px + 12
small.save(os.path.join(ROOT, "docs", "icon-sizes.png"))
print("ok", os.path.basename(font_path))