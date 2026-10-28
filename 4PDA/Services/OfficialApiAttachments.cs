using System;
using System.Net;
using System.Text.RegularExpressions;

namespace _4PDA.Services
{
    internal static class OfficialApiAttachments
    {
        internal static int AttachmentId(Uri uri)
        {
            if (!ForumPostNavigation.IsForumUri(uri)) return 0;
            Match match = Regex.Match(uri.AbsolutePath, @"^/forum/dl/post/(\d+)(?:/|$)", RegexOptions.IgnoreCase);
            if (!match.Success)
            {
                string query = WebUtility.HtmlDecode(uri.Query);
                match = Regex.Match(query, @"(?:[?&])(?:attach_id|attachid|getattach)=(\d+)(?:&|$)", RegexOptions.IgnoreCase);
                if (!match.Success && Regex.IsMatch(query, @"(?:[?&])(?:act|view)=(?:attach|getattach)(?:&|$)", RegexOptions.IgnoreCase))
                    match = Regex.Match(query, @"(?:[?&])id=(\d+)(?:&|$)", RegexOptions.IgnoreCase);
            }
            int id;
            return match.Success && int.TryParse(match.Groups[1].Value, out id) && id > 0 ? id : 0;
        }

        internal static string FileUrl(int id, string name)
        {
            return "https://4pda.to/forum/dl/post/" + id + "/" + Uri.EscapeDataString(name ?? string.Empty);
        }

        internal static int ImageId(Uri uri)
        {
            int id = AttachmentId(uri);
            if (id > 0) return id;
            if (uri == null || !uri.IsAbsoluteUri) return 0;
            string host = uri.Host;
            if (host != "4pda.to" && !host.EndsWith(".4pda.to", StringComparison.OrdinalIgnoreCase)
                && host != "4pda.ru" && !host.EndsWith(".4pda.ru", StringComparison.OrdinalIgnoreCase)) return 0;
            Match match = Regex.Match(uri.AbsolutePath, @"^/static/c/img/att-\d+/(\d+)\.[a-z0-9]+$", RegexOptions.IgnoreCase);
            return match.Success && int.TryParse(match.Groups[1].Value, out id) && id > 0 ? id : 0;
        }

        internal static Uri DownloadUri(ApiArray response)
        {
            Uri uri;
            if (!Uri.TryCreate(response.Text(0), UriKind.Absolute, out uri)
                || (uri.Scheme != "https" && uri.Scheme != "http") || !string.IsNullOrEmpty(uri.UserInfo))
                throw new InvalidOperationException("4PDA не вернул адрес файла.");
            // The API URL already authorizes the transfer. Preserve its signature/query.
            return uri;
        }

        internal static string ErrorMessage(int status)
        {
            if (status == 4) return "Превышен лимит скачиваний 4PDA. Попробуйте ещё раз завтра.";
            if (status == 5) return "4PDA запретил скачивание этого файла для данного клиента.";
            return "Не удалось получить адрес файла от 4PDA (код " + status + ").";
        }
    }
}
