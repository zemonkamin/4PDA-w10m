using System;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace _4PDA.Services
{
    internal static class NewsCardPolicy
    {
        internal const int SummaryLimit = 240;
        internal static string Summary(string source)
        {
            if (string.IsNullOrEmpty(source)) return string.Empty;
            int end = Math.Min(source.Length, 4096);
            var text = new StringBuilder(SummaryLimit + 32);
            for (int i = 0; i < end && text.Length < SummaryLimit + 16; i++)
            {
                char ch = source[i];
                if (ch == '[')
                {
                    int close = source.IndexOf(']', i + 1, Math.Min(320, end - i - 1));
                    if (close >= 0 && i + 1 < end && (char.IsLetter(source[i + 1]) || source[i + 1] == '/' || source[i + 1] == '*'))
                    {
                        string tag = source.Substring(i + 1, close - i - 1);
                        if (tag.Equals("img", StringComparison.OrdinalIgnoreCase))
                        {
                            int imageEnd = source.IndexOf("[/img]", close + 1, end - close - 1, StringComparison.OrdinalIgnoreCase);
                            i = imageEnd >= 0 ? imageEnd + 5 : end;
                        }
                        else i = close;
                        continue;
                    }
                }
                if (ch == '\u0002' || ch == '\u0003') continue;
                text.Append(ch);
            }
            string result = Regex.Replace(WebUtility.HtmlDecode(text.ToString()), @"\s+", " ").Trim();
            if (result.Length <= SummaryLimit) return result;
            int length = SummaryLimit - 1;
            if (char.IsHighSurrogate(result[length - 1])) length--;
            return result.Substring(0, length).TrimEnd() + "…";
        }

        internal static int Columns(double availableWidth) { return availableWidth >= 1050 ? 3 : availableWidth >= 700 ? 2 : 1; }
        internal static double CellWidth(double availableWidth) { return Math.Max(1, Math.Floor(availableWidth / Columns(availableWidth)) - 6); }
    }
}
