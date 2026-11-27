using System.IO;
using System.Text.Json;

namespace GalLens;

public sealed class SubtitleStyle
{
    public string FontFamily { get; set; } = "Microsoft YaHei UI";
    public double FontSize { get; set; } = 28;
    public string TextColor { get; set; } = "#FFFFFF";
    public bool Outline { get; set; } = true;
    public string OutlineColor { get; set; } = "#000000";
    public double BackgroundOpacity { get; set; } = 0.66;
    public double CornerRadius { get; set; } = 8;
    public int OffsetX { get; set; }
    public int OffsetY { get; set; }
}

public sealed class RegionRect
{
    public int X { get; set; }
    public int Y { get; set; }
    public int W { get; set; }
    public int H { get; set; }
}

public sealed class AppSettings
{
    public string BaseUrl { get; set; } = "https://api.deepseek.com/v1";
    public string ApiKeyEnc { get; set; } = "";
    public string Model { get; set; } = "deepseek-flash";
    public string Glossary { get; set; } = "";

    /// <summary>OCR 结果看起来不可靠时，改用图像（视觉）翻译。</summary>
    public bool VisionFallback { get; set; } = true;

    /// <summary>自动从译文里积累术语表。</summary>
    public bool AutoGlossary { get; set; } = true;
    public string VisionDetail { get; set; } = "auto";

    public string TargetProcess { get; set; } = "";
    public string TargetTitle { get; set; } = "";

    /// <summary>每个游戏记住自己的台词区域，键是进程名。</summary>
    public Dictionary<string, RegionRect> Regions { get; set; } = new();

    public int RoiX { get; set; } = -1;
    public int RoiY { get; set; } = -1;
    public int RoiW { get; set; } = 0;
    public int RoiH { get; set; } = 0;

    public SubtitleStyle Style { get; set; } = new();

    /// <summary>每个游戏一份术语表，键是进程名。</summary>
    public Dictionary<string, string> Glossaries { get; set; } = new();

    // 兼容旧配置的投影
    public double FontSize
    {
        get => Style.FontSize;
        set => Style.FontSize = value;
    }
    public double OverlayOpacity
    {
        get => Style.BackgroundOpacity;
        set => Style.BackgroundOpacity = value;
    }
    public int OffsetX
    {
        get => Style.OffsetX;
        set => Style.OffsetX = value;
    }
    public int OffsetY
    {
        get => Style.OffsetY;
        set => Style.OffsetY = value;
    }

    public string ApiKey
    {
        get => Dpapi.Unprotect(ApiKeyEnc);
        set => ApiKeyEnc = Dpapi.Protect(value ?? "");
    }

    public bool HasRegion => RoiW > 8 && RoiH > 8;
}

public static class AppStorage
{
    private static readonly string Dir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GalLens");

    private static readonly string SettingsPath = Path.Combine(Dir, "settings.json");
    private static readonly string CachePath = Path.Combine(Dir, "cache.json");

    private static readonly JsonSerializerOptions Opt = new() { WriteIndented = true };

    public static AppSettings LoadSettings()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            if (File.Exists(SettingsPath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath)) ?? new AppSettings();
        }
        catch { }
        return new AppSettings();
    }

    public static void SaveSettings(AppSettings s)
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(s, Opt));
        }
        catch { }
    }

    public static Dictionary<string, string> LoadCache()
    {
        try
        {
            if (File.Exists(CachePath))
            {
                var d = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(CachePath));
                if (d != null) return d;
            }
        }
        catch { }
        return new Dictionary<string, string>();
    }

    public static void SaveCache(Dictionary<string, string> cache)
    {
        try
        {
            Directory.CreateDirectory(Dir);
            var trimmed = cache.Count > 4000
                ? cache.Skip(cache.Count - 4000).ToDictionary(k => k.Key, v => v.Value)
                : cache;
            File.WriteAllText(CachePath, JsonSerializer.Serialize(trimmed, Opt));
        }
        catch { }
    }
}
