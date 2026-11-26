"""把 PNG 写成一个「全部使用未压缩 BMP 帧」的 .ico。

为什么不用 Pillow 的 save(format="ICO")：
  当 256x256 那一帧存在时，Pillow 会把它存成 PNG 压缩格式（Vista 图标规范允许），
  但 WPF 的 IconBitmapDecoder 不支持 PNG 帧，读 exe 图标时会抛
  NotImplementedException —— 表现就是程序启动后卡在异常、主窗口建不出来。

所以这里手写 ICO：每一帧都是 32bpp BGRA 的 BITMAPINFOHEADER + 像素 + AND 掩码。
"""
import struct
import sys

from PIL import Image

SIZES = [16, 24, 32, 48, 64, 128, 256]


def frame_bytes(im: Image.Image) -> bytes:
    w, h = im.size
    px = im.convert("RGBA").load()

    header = struct.pack(
        "<IiiHHIIiiII",
        40,          # biSize
        w,           # biWidth
        h * 2,       # biHeight（ICO 里是 2 倍：XOR + AND）
        1,           # biPlanes
        32,          # biBitCount
        0,           # biCompression = BI_RGB
        w * h * 4,   # biSizeImage
        0, 0, 0, 0,
    )

    rows = []
    for y in range(h - 1, -1, -1):          # BMP 自下而上
        row = bytearray()
        for x in range(w):
            r, g, b, a = px[x, y]
            row += bytes((b, g, r, a))       # BGRA
        rows.append(bytes(row))

    and_stride = ((w + 31) // 32) * 4
    and_mask = bytes(and_stride * h)         # 32bpp 有 alpha，掩码全 0

    return header + b"".join(rows) + and_mask


def write_ico(src_png: str, dst_ico: str) -> None:
    base = Image.open(src_png).convert("RGBA")
    if base.size != (256, 256):
        base = base.resize((256, 256), Image.LANCZOS)

    frames = [base.resize((s, s), Image.LANCZOS) for s in SIZES]
    blobs = [frame_bytes(f) for f in frames]

    out = bytearray(struct.pack("<HHH", 0, 1, len(frames)))
    offset = 6 + 16 * len(frames)

    for size, blob in zip(SIZES, blobs):
        out += struct.pack(
            "<BBBBHHII",
            0 if size >= 256 else size,
            0 if size >= 256 else size,
            0, 0, 1, 32, len(blob), offset,
        )
        offset += len(blob)

    for blob in blobs:
        out += blob

    with open(dst_ico, "wb") as f:
        f.write(out)

    print(f"ok -> {dst_ico}  sizes={SIZES}  bytes={len(out)}")


if __name__ == "__main__":
    src = sys.argv[1] if len(sys.argv) > 1 else "icon-square.png"
    dst = sys.argv[2] if len(sys.argv) > 2 else "app.ico"
    write_ico(src, dst)
