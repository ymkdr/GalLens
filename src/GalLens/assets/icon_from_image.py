"""把任意图片（png/jpg/webp）转成 exe 用的多尺寸 .ico。

用法：
    python icon_from_image.py 你的图.png
    python icon_from_image.py 你的图.png app.ico

规则：
  - 自动居中裁成正方形
  - 生成 16/24/32/48/64/128/256 七个尺寸
  - 保留透明通道
"""
import sys
from PIL import Image

SIZES = [16, 24, 32, 48, 64, 128, 256]


def main() -> None:
    if len(sys.argv) < 2:
        print(__doc__)
        raise SystemExit(1)

    src = sys.argv[1]
    dst = sys.argv[2] if len(sys.argv) > 2 else "app.ico"

    im = Image.open(src).convert("RGBA")

    # 居中裁成正方形，避免非正方形图片被拉伸
    w, h = im.size
    side = min(w, h)
    left = (w - side) // 2
    top = (h - side) // 2
    im = im.crop((left, top, left + side, top + side))

    # 先缩到 256，再让 Pillow 生成各尺寸
    im = im.resize((256, 256), Image.LANCZOS)
    im.save(dst, format="ICO", sizes=[(s, s) for s in SIZES])

    print(f"ok -> {dst}  ({', '.join(str(s) for s in SIZES)})")


if __name__ == "__main__":
    main()
