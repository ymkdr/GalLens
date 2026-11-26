using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;

namespace GalLens;

public sealed class OcrService
{
    private readonly OcrEngine _engine;

    private OcrService(OcrEngine engine, string tag) { _engine = engine; Tag = tag; }

    public string Tag { get; }

    public static IReadOnlyList<string> Available =>
        OcrEngine.AvailableRecognizerLanguages.Select(l => l.LanguageTag).ToList();

    public static OcrService? TryCreate(params string[] tags)
    {
        foreach (var tag in tags)
        {
            try
            {
                var eng = OcrEngine.TryCreateFromLanguage(new Language(tag));
                if (eng != null) return new OcrService(eng, tag);
            }
            catch { }
        }
        return null;
    }

    public async Task<string> RecognizeAsync(Frame frame)
    {
        var sb = SoftwareBitmap.CreateCopyFromBuffer(
            frame.Bgra.AsBuffer(),
            BitmapPixelFormat.Bgra8,
            frame.Width,
            frame.Height,
            BitmapAlphaMode.Premultiplied);

        var result = await _engine.RecognizeAsync(sb);

        var lines = new List<(double y, string text)>();
        foreach (var line in result.Lines)
        {
            double top = double.MaxValue;
            foreach (var word in line.Words)
                top = Math.Min(top, word.BoundingRect.Y);
            if (top == double.MaxValue) top = lines.Count;
            lines.Add((top, line.Text));
        }

        lines.Sort((a, b) => a.y.CompareTo(b.y));
        var sb2 = new StringBuilder();
        foreach (var (_, text) in lines)
        {
            if (sb2.Length > 0) sb2.Append('\n');
            sb2.Append(text);
        }
        return sb2.ToString();
    }
}
