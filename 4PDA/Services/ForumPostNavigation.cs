using System;
using System.Net;
using System.Text.RegularExpressions;

namespace _4PDA.Services
{
    internal static class ForumPostNavigation
    {
        internal static int PostId(string url)
        {
            Uri uri;
            if (!Uri.TryCreate(new Uri("https://4pda.to/forum/index.php"), WebUtility.HtmlDecode(url ?? string.Empty), out uri)
                || !IsForumUri(uri)) return 0;
            Match match = Regex.Match(uri.Query, @"(?:[?&])(?:p|pid|post)=(\d+)(?:&|$)", RegexOptions.IgnoreCase);
            if (!match.Success) match = Regex.Match(uri.Fragment, @"^#(?:entry|post)(\d+)$", RegexOptions.IgnoreCase);
            if (!match.Success) match = Regex.Match(uri.Fragment, @"^#Spoil-(\d+)-\d+$", RegexOptions.IgnoreCase);
            int id;
            return match.Success && int.TryParse(match.Groups[1].Value, out id) ? id : 0;
        }

        internal static int SpoilerNumber(string fragment)
        {
            Match match = Regex.Match(fragment ?? string.Empty, @"^#Spoil-(?:\d+-)?(\d+)$", RegexOptions.IgnoreCase);
            int id; return match.Success && int.TryParse(match.Groups[1].Value, out id) ? id : 0;
        }

        internal static int PageOffset(int position, int pageSize)
        {
            return Math.Max(0, position) / Math.Max(1, pageSize) * Math.Max(1, pageSize);
        }

        internal static int StartOffset(string url, int pageSize)
        {
            Uri uri;
            if (!Uri.TryCreate(url, UriKind.Absolute, out uri) || !IsForumUri(uri)) return 0;
            Match match = Regex.Match(WebUtility.HtmlDecode(uri.Query), @"(?:[?&])st=(\d+)(?:&|$)", RegexOptions.IgnoreCase);
            int offset;
            return match.Success && int.TryParse(match.Groups[1].Value, out offset) ? PageOffset(offset, pageSize) : 0;
        }

        internal static bool IsForumUri(Uri uri)
        {
            return uri != null && uri.IsAbsoluteUri && IsForumHost(uri.Host)
                && uri.AbsolutePath.StartsWith("/forum/", StringComparison.OrdinalIgnoreCase);
        }

        internal static string PostUrl(int postId, string fragment = "")
        {
            return "https://4pda.to/forum/index.php?act=findpost&pid=" + postId + fragment;
        }

        private static bool IsForumHost(string host)
        {
            return host.Equals("4pda.to", StringComparison.OrdinalIgnoreCase) || host.EndsWith(".4pda.to", StringComparison.OrdinalIgnoreCase)
                || host.Equals("4pda.ru", StringComparison.OrdinalIgnoreCase) || host.EndsWith(".4pda.ru", StringComparison.OrdinalIgnoreCase);
        }
    }
}
