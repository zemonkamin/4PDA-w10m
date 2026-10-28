using System;
using System.Text.RegularExpressions;

namespace _4PDA.Services
{
    internal static class ForumPostImagePolicy
    {
        // Actual instructional attachments from topic 658368, post 39497045.
        // The lower IDs are the original files whose names survive in copies.
        // Do not infer a UI image from its dimensions or a word in its filename.
        internal static string ActionLabel(string url, int attachmentId = 0)
        {
            string label = Label(attachmentId);
            if (label != null) return label;
            Uri uri;
            if (!Uri.TryCreate(url, UriKind.Absolute, out uri) || !IsForumHost(uri.Host)) return null;
            Match match = Regex.Match(uri.AbsolutePath,
                @"^/(?:static/c/img/att-\d+/|forum/dl/post/)(\d+)(?:\.(?:gif|png)|/[^/]*)/?$", RegexOptions.IgnoreCase);
            int id;
            return match.Success && int.TryParse(match.Groups[1].Value, out id) ? Label(id) : null;
        }

        internal static string AttachmentLabel(ApiArray attachment)
        {
            string label = ActionLabel(attachment.Text(4), attachment.Int(0));
            if (label != null) return label;
            // Copies receive a new attachment ID but keep the old filename.
            // Require both that exact filename and its known dimensions.
            int width = attachment.Int(5), height = attachment.Int(6);
            switch (attachment.Text(2).ToLowerInvariant())
            {
                case "4027733.gif": return width == 10 && height == 10 ? Label(4027733) : null;
                case "4027734.gif": return width == 72 && height == 21 ? Label(4027734) : null;
                case "4027735.png": return width == 79 && height == 19 ? Label(4027735) : null;
                case "4027737.png": return width == 79 && height == 19 ? Label(4027737) : null;
                default: return null;
            }
        }

        internal static bool IsSkinAsset(Uri uri)
        {
            if (uri == null || !IsForumHost(uri.Host)) return false;
            string path = uri.AbsolutePath;
            return path.StartsWith("/forum/style_images/", StringComparison.OrdinalIgnoreCase)
                || path.StartsWith("/style_images/", StringComparison.OrdinalIgnoreCase);
        }

        private static string Label(int id)
        {
            switch (id)
            {
                case 4027733: case 6131283: return "«+»";
                case 4027734: case 6131284: return "«Жалоба»";
                case 4027735: case 6131285: return "«Хорошо ↑»";
                case 4027737: case 6131287: return "«Плохо ↓»";
                default: return null;
            }
        }

        private static bool IsForumHost(string host)
        {
            return host.Equals("4pda.to", StringComparison.OrdinalIgnoreCase)
                || host.EndsWith(".4pda.to", StringComparison.OrdinalIgnoreCase)
                || host.Equals("4pda.ru", StringComparison.OrdinalIgnoreCase)
                || host.EndsWith(".4pda.ru", StringComparison.OrdinalIgnoreCase);
        }
    }
}
