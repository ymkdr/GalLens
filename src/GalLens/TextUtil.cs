using System.Text;

namespace GalLens;

public static class TextUtil
{
    /// <summary>全角标点、假名、汉字、全角符号：这些字符之间的空格是 OCR 硬加的，要去掉。</summary>
    private static bool IsCjk(char c) =>
        (c >= 0x2000 && c <= 0x206F) ||
        (c >= 0x3000 && c <= 0x30FF) ||
        (c >= 0x3400 && c <= 0x4DBF) ||
        (c >= 0x4E00 && c <= 0x9FFF) ||
        (c >= 0xFF00 && c <= 0xFFEF);

    public static string Normalize(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;

        var s = raw.Replace("\r", "").Replace((char)0x00A0, ' ');
        var sb = new StringBuilder(s.Length);

        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];

            if (c == ' ' && sb.Length > 0 && i + 1 < s.Length &&
                IsCjk(sb[sb.Length - 1]) && IsCjk(s[i + 1]))
                continue;

            if (c == ' ' && sb.Length > 1 && sb[sb.Length - 1] == ' ' && sb[sb.Length - 2] == ' ')
                continue;

            if (c == '\n' && sb.Length > 1 && sb[sb.Length - 1] == '\n' && sb[sb.Length - 2] == '\n')
                continue;

            sb.Append(c);
        }

        var lines = sb.ToString().Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.Length > 0);

        return string.Join("\n", lines).Trim();
    }

    public static string ForDisplay(string s) => s.Replace("\n", " ");

    /// <summary>OCR 结果是不是「认得出来但明显不可靠」——用来决定要不要走图像翻译。</summary>
    public static bool IsLowQuality(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return false;   // 空的不算，避免在无文字画面上狂发请求
        int cjk = 0;
        foreach (var c in s)
            if ((c >= 0x3040 && c <= 0x30FF) || (c >= 0x4E00 && c <= 0x9FFF)) cjk++;
        if (cjk == 0) return true;                        // 一个假名/汉字都没有，基本是乱码
        return cjk * 100 / s.Length < 30;                 // 文字占比太低，多半是符号噪声
    }

    public static bool LooksLikeUiNoise(string s)
    {
        if (s.Length < 2) return true;
        int cjk = s.Count(c => (c >= 0x3040 && c <= 0x30FF) || (c >= 0x4E00 && c <= 0x9FFF));
        return cjk == 0 && s.Length < 6;
    }
}
