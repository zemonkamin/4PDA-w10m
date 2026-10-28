using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace _4PDA.Services
{
    internal static class OfficialApiContent
    {
        public static string Html(string source, ApiArray attachments = null, bool news = false, int postId = 0)
        {
            source = source ?? string.Empty;
            source = Regex.Replace(source, @"(?<code>\[code\][\s\S]*?(?:\[/code\]|$))"
                + @"|\[snapback\]\s*(?<post>\d+)\s*\[/snapback\]|\[url\](?<url>[^\[]+)\[/url\]|\[img\](?<img>[^\[]+)\[/img\]",
                m =>
                {
                    if (m.Groups["code"].Success) return m.Value;
                    if (m.Groups["img"].Success) return "[apiimage=" + Uri.EscapeDataString(m.Groups["img"].Value) + "]";
                    if (m.Groups["url"].Success) return "[url=" + m.Groups["url"].Value + "]" + m.Groups["url"].Value + "[/url]";
                    int id;
                    return int.TryParse(m.Groups["post"].Value, out id) && id > 0
                        ? "[url=\"" + ForumPostNavigation.PostUrl(id) + "\"]↗[/url]" : string.Empty;
                }, RegexOptions.IgnoreCase);
            var output = new StringBuilder();
            var byId = new Dictionary<int, ApiArray>();
            var imageIds = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var used = new HashSet<int>();
            if (attachments != null)
                for (int i = 0; i < attachments.Count; i++)
                {
                    ApiArray attachment = attachments.Array(i);
                    int id = attachment.Int(0);
                    if (id <= 0) continue;
                    byId[id] = attachment;
                    if (!news && attachment.Int(1) > 0)
                    {
                        string image = SafeUrl(attachment.Text(4));
                        if (!string.IsNullOrEmpty(image)) imageIds[image] = id;
                    }
                }
            int position = 0;
            int spoilerIndex = 0;
            foreach (Match token in Regex.Matches(source, @"\[(?<end>/)?(?<name>[a-z*][a-z0-9]*)(?:=(?<value>[^\]]*)|(?<attributes>\s+[^\]]*))?\]", RegexOptions.IgnoreCase))
            {
                if (token.Index < position) continue;
                AppendText(output, source.Substring(position, token.Index - position));
                position = token.Index + token.Length;
                string name = token.Groups["name"].Value.ToLowerInvariant();
                string value = WebUtility.HtmlDecode(token.Groups["value"].Value.Trim().Trim('"', '\''));
                bool end = token.Groups["end"].Success;
                switch (name)
                {
                    case "b": case "i": case "u": case "s": case "sub": case "sup":
                        output.Append("<" + (end ? "/" : "") + name + ">"); break;
                    case "quote":
                        if (end) output.Append("</div></div>");
                        else
                        {
                            string attributes = token.Groups["attributes"].Value;
                            string author = QuotedAttribute(attributes, "name");
                            string date = QuotedAttribute(attributes, "date");
                            int quotedPost; int.TryParse(QuotedAttribute(attributes, "post"), out quotedPost);
                            string title = Attribute(string.IsNullOrWhiteSpace(author) ? (string.IsNullOrWhiteSpace(value) ? "Цитата" : value) : author + " · " + date);
                            if (quotedPost > 0) title = "<a href=\"" + Attribute(ForumPostNavigation.PostUrl(quotedPost)) + "\">" + title + " ↗</a>";
                            output.Append("<div class=\"post-block quote\"><div class=\"block-title\">" + title + "</div><div class=\"block-body\">");
                        }
                        break;
                    case "code":
                        if (!end)
                        {
                            Match close = Regex.Match(source.Substring(position), @"\[/code\]", RegexOptions.IgnoreCase);
                            int length = close.Success ? close.Index : source.Length - position;
                            output.Append("<div class=\"post-block code\"><div class=\"block-title\">Код</div><div class=\"block-body\">");
                            AppendText(output, source.Substring(position, length));
                            output.Append("</div></div>");
                            position += length + (close.Success ? close.Length : 0);
                        }
                        break;
                    case "url":
                        if (!end)
                        {
                            Uri uri;
                            if (Uri.TryCreate(LinkUrl(value, postId), UriKind.Absolute, out uri))
                            {
                                int linkedId = OfficialApiAttachments.AttachmentId(uri);
                                if (linkedId > 0) used.Add(linkedId);
                            }
                        }
                        output.Append(end ? "</a>" : "<a href=\"" + Attribute(LinkUrl(value, postId)) + "\">"); break;
                    case "apiimage":
                        string imageUrl = SafeUrl(Uri.UnescapeDataString(value));
                        int imageId;
                        if (imageIds.TryGetValue(imageUrl, out imageId)) used.Add(imageId);
                        output.Append(Image(imageUrl, !news)); break;
                    case "attachment":
                        int id;
                        if (!end && int.TryParse(value.Split(':')[0], out id) && id > 0)
                        {
                            ApiArray attachment;
                            if (byId.TryGetValue(id, out attachment))
                            {
                                output.Append(Attachment(attachment, news));
                                used.Add(id);
                            }
                            else if (!news)
                            {
                                int colon = value.IndexOf(':');
                                string label = colon >= 0 ? value.Substring(colon + 1) : "Файл #" + id;
                                output.Append(FileLink(id, label));
                            }
                        }
                        break;
                    case "youtube":
                        if (!end) output.Append("<a href=\"https://www.youtube.com/watch?v="
                            + Attribute(Uri.EscapeDataString(value.Split(':')[0])) + "\">Видео на YouTube</a>");
                        break;
                    case "spoiler": case "hide":
                        output.Append(end ? "</div></div>" : "<div class=\"post-block spoil\" data-spoiler=\"" + (++spoilerIndex)
                            + "\"><div class=\"block-title\">" + Attribute(string.IsNullOrWhiteSpace(value) ? "Спойлер" : value)
                            + "</div><div class=\"block-body\">"); break;
                    case "center": case "left": case "right":
                        output.Append(end ? "</div>" : "<div>"); break;
                    case "color": case "size": case "font":
                        output.Append(end ? "</span>" : "<span>"); break;
                    case "list": output.Append(end ? "</ul>" : "<ul>"); break;
                    case "*": output.Append(end ? "</li>" : "<li>"); break;
                    case "br": case "break": output.Append("<br/>"); break;
                    case "hr": case "seporator": output.Append("<hr/>"); break;
                }
            }
            AppendText(output, source.Substring(position));
            if (!news && attachments != null)
            {
                // BBString appends attachments not inserted into the BBCode by the author.
                // Keep images in the normal lazy-loading pipeline; files remain visible links.
                for (int group = 0; group < 2; group++)
                {
                    bool heading = false;
                    for (int i = 0; i < attachments.Count; i++)
                    {
                        ApiArray attachment = attachments.Array(i);
                        int id = attachment.Int(0);
                        bool image = attachment.Int(1) > 0;
                        if (id <= 0 || used.Contains(id) || image != (group == 0)) continue;
                        string rendered = Attachment(attachment, false);
                        if (string.IsNullOrEmpty(rendered)) continue;
                        if (!heading)
                        {
                            output.Append("<br/><br/><b>" + (image ? "Прикреплённые изображения" : "Прикреплённые файлы") + "</b>");
                            heading = true;
                        }
                        output.Append("<br/>").Append(rendered);
                        used.Add(id);
                    }
                }
            }
            return output.ToString();
        }

        private static string Attachment(ApiArray attachment, bool news)
        {
            if (news) return Image(attachment.Text(1));
            if (attachment.Int(1) > 0)
            {
                string actionLabel = ForumPostImagePolicy.AttachmentLabel(attachment);
                if (actionLabel != null) return Attribute(actionLabel);
                string url = SafeUrl(attachment.Text(4));
                if (!string.IsNullOrEmpty(url)) return Image(url, true);
            }
            string link = FileLink(attachment.Int(0), attachment.Text(2));
            int bytes = attachment.Int(3);
            return link + (bytes > 0 ? " (" + FileSize(bytes) + ")" : string.Empty);
        }

        private static string FileLink(int id, string label)
        {
            if (string.IsNullOrWhiteSpace(label)) label = "Файл #" + id;
            return "<a href=\"" + Attribute(OfficialApiAttachments.FileUrl(id, label)) + "\">" + Attribute(label) + "</a>";
        }

        private static string FileSize(int bytes)
        {
            if (bytes < 1024) return bytes + " Б";
            if (bytes < 1024 * 1024) return (bytes / 1024.0).ToString("0.#") + " КБ";
            return (bytes / (1024.0 * 1024)).ToString("0.#") + " МБ";
        }

        public static string PlainText(string source)
        {
            return WebUtility.HtmlDecode(Regex.Replace(Html(source), "<[^>]*>", " "))
                .Replace("\u0002", string.Empty).Replace("\u0003", string.Empty).Trim();
        }

        private static void AppendText(StringBuilder output, string text)
        {
            output.Append(Attribute(text).Replace("\r\n", "\n").Replace("\n", "<br/>"));
        }
        private static string Attribute(string text) { return WebUtility.HtmlEncode(WebUtility.HtmlDecode(text ?? string.Empty)); }
        private static string QuotedAttribute(string text, string name)
        {
            Match match = Regex.Match(text, @"\b" + name + @"\s*=\s*(?:""(?<v>[^""]*)""|'(?<v>[^']*)'|(?<v>[^\s]+))", RegexOptions.IgnoreCase);
            return match.Success ? match.Groups["v"].Value : string.Empty;
        }
        private static string LinkUrl(string url, int postId)
        {
            if (url.StartsWith("#", StringComparison.Ordinal)) return postId > 0 ? ForumPostNavigation.PostUrl(postId, url) : url;
            if (url.StartsWith("index.php?", StringComparison.OrdinalIgnoreCase) || url.StartsWith("?", StringComparison.Ordinal))
                return new Uri(new Uri("https://4pda.to/forum/index.php"), url).AbsoluteUri;
            return SafeUrl(url);
        }
        private static string Image(string url, bool forum = false)
        {
            string safe = SafeUrl(url);
            if (forum && !string.IsNullOrWhiteSpace(safe))
            {
                string label = ForumPostImagePolicy.ActionLabel(safe);
                if (label != null) return Attribute(label);
                if (ForumPostImagePolicy.IsSkinAsset(new Uri(safe))) return string.Empty;
            }
            return string.IsNullOrWhiteSpace(safe) ? string.Empty : "<img src=\"" + Attribute(safe) + "\"/>";
        }
        private static string SafeUrl(string url)
        {
            Uri uri;
            if (url.StartsWith("//", StringComparison.Ordinal)) url = "https:" + url;
            if (url.StartsWith("/", StringComparison.Ordinal)) url = "https://4pda.to" + url;
            return Uri.TryCreate(url, UriKind.Absolute, out uri) && (uri.Scheme == "http" || uri.Scheme == "https")
                ? uri.AbsoluteUri : string.Empty;
        }
    }
}
