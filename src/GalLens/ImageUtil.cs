using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace GalLens;

public static class ImageUtil
{
    /// <summary>把一帧 BGRA 像素编码成 PNG，供视觉模型使用。</summary>
    public static byte[] EncodePng(Frame frame)
    {
        var bmp = BitmapSource.Create(
            frame.Width, frame.Height, 96, 96,
            PixelFormats.Bgra32, null, frame.Bgra, frame.Width * 4);
        bmp.Freeze();

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bmp));

        using var ms = new MemoryStream();
        encoder.Save(ms);
        return ms.ToArray();
    }
}
