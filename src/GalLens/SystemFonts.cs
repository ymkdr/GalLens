using System.Windows.Media;

namespace GalLens;

public static class SystemFonts
{
    /// <summary>常用中文字体，排在列表最前面。</summary>
    private static readonly string[] Preferred =
    {
        "Microsoft YaHei UI", "Microsoft YaHei", "SimHei", "SimSun", "KaiTi",
        "DengXian", "Microsoft JhengHei", "Yu Gothic UI", "Meiryo", "MS Gothic",
        "Noto Sans CJK SC", "Source Han Sans SC", "Source Han Sans CN"
    };

    private static List<string>? _cache;

    /// <summary>
    /// 枚举系统里能同时显示中日文的字体。
    /// 只是「使用」系统字体，不嵌入、不分发，不涉及字体授权问题。
    /// </summary>
    public static IReadOnlyList<string> GetCjkFonts()
    {
        if (_cache != null) return _cache;

        // 中文系统上字体的本地化名可能是「微软雅黑」，所以按所有语言名一起匹配
        var bySource = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var family in Fonts.SystemFontFamilies)
        {
            if (!SupportsCjk(family)) continue;
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { family.Source };
            foreach (var n in family.FamilyNames.Values) names.Add(n);
            bySource[family.Source] = names;
        }

        var ordered = new List<string>();
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var name in Preferred)
        {
            var hit = bySource.FirstOrDefault(kv =>
                kv.Key.Equals(name, StringComparison.OrdinalIgnoreCase) ||
                kv.Value.Contains(name));
            if (hit.Key != null && used.Add(hit.Key))
                ordered.Add(hit.Key);
        }

        var found = new HashSet<string>(
            bySource.Keys.Where(k => !used.Contains(k)),
            StringComparer.OrdinalIgnoreCase);
        ordered.AddRange(found.OrderBy(f => f, StringComparer.CurrentCultureIgnoreCase));

        if (ordered.Count == 0) ordered.Add("Microsoft YaHei UI");
        _cache = ordered;
        return ordered;
    }

    private static bool SupportsCjk(FontFamily family)
    {
        try
        {
            foreach (var typeface in family.GetTypefaces())
            {
                if (typeface.TryGetGlyphTypeface(out var glyph))
                {
                    var map = glyph.CharacterToGlyphMap;
                    if (map.ContainsKey('漢') && map.ContainsKey('あ'))
                        return true;
                }
            }
        }
        catch { }
        return false;
    }
}
