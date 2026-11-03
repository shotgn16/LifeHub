using System.Net;
using System.Text.RegularExpressions;

namespace LifeHub.Services;

/// <summary>Very small HTML → readable text for reference pages.</summary>
public static class HtmlText
{
    public static string Extract(string html, string? anchor = null, int maxChars = 8000)
    {
        var s = Regex.Replace(html, @"<(script|style|noscript|svg|nav|header|footer|form)[^>]*>[\s\S]*?</\1>", " ", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"<(br|/p|/div|/h[1-6]|/li|/tr)[^>]*>", "\n", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"<[^>]+>", " ");
        s = WebUtility.HtmlDecode(s);
        s = Regex.Replace(s, @"[ \t\r\f\v]+", " ");
        s = Regex.Replace(s, @"\n\s*\n+", "\n\n").Trim();
        if (!string.IsNullOrEmpty(anchor))
        {
            // Skip menus: start at the first real mention of the passage after the page header.
            var idx = s.IndexOf(anchor, Math.Min(300, s.Length), StringComparison.OrdinalIgnoreCase);
            if (idx > 0) s = s[idx..];
        }
        return s.Length <= maxChars ? s : s[..maxChars];
    }
}
