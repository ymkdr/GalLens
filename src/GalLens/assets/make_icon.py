"""生成原创图标：深色圆角底 + 白色对话气泡 + 两行文字条。
全部是几何图形，不使用任何字体或第三方素材。"""
import os
from PIL import Image, ImageDraw

S = 1024
OUT_DIR = os.path.dirname(os.path.abspath(__file__))

c1 = (78, 99, 216)    # 靛蓝
c2 = (123, 91, 214)   # 紫

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

# 气泡主体
bx0, by0, bx1, by1 = int(S * 0.16), int(S * 0.27), int(S * 0.84), int(S * 0.64)
radius = int(S * 0.085)
d.rounded_rectangle([bx0, by0, bx1, by1], radius=radius, fill=(255, 255, 255, 255))

# 左下角小尾巴
tail = [
    (int(S * 0.30), by1 - int(S * 0.012)),
    (int(S * 0.26), int(S * 0.76)),
    (int(S * 0.44), by1 - int(S * 0.012)),
]
d.polygon(tail, fill=(255, 255, 255, 255))

# 两行“文字条”
bar = (38, 42, 92, 255)
d.rounded_rectangle([int(S * 0.25), int(S * 0.355), int(S * 0.75), int(S * 0.415)], radius=int(S * 0.03), fill=bar)
d.rounded_rectangle([int(S * 0.25), int(S * 0.475), int(S * 0.60), int(S * 0.535)], radius=int(S * 0.03), fill=bar)

img.resize((256, 256), Image.LANCZOS).save(os.path.join(OUT_DIR, "icon-square.png"))

preview = Image.new("RGBA", (512, 512), (243, 243, 246, 255))
preview.alpha_composite(img.resize((512, 512), Image.LANCZOS))
preview.convert("RGB").save(os.path.join(OUT_DIR, "..", "..", "..", "docs", "icon.png"))
print("ok")
