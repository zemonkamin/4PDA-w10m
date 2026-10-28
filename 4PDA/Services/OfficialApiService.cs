using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using _4PDA.Models;
using Windows.Storage;
using Windows.Security.Cryptography;
using Windows.Security.Cryptography.Core;

namespace _4PDA.Services
{
    internal static class OfficialApiService
    {
        internal static readonly OfficialApiClient Client = new OfficialApiClient();
        internal static bool IsAuthorized
        {
            get { return SettingInt("OfficialApiUserId") > 0 && !string.IsNullOrWhiteSpace(Setting("OfficialApiLoginKey")); }
        }

        internal static async Task<ApiArray> CallAsync(string command, params object[] arguments)
        {
            Client.UserId = SettingInt("OfficialApiUserId");
            Client.LoginKey = Setting("OfficialApiLoginKey");
            Client.Hidden = Setting("OfficialApiHidden") == "1";
            try { return await Client.CallAsync(command, arguments); }
            catch (OfficialApiException ex)
            {
                if (ex.Command != "ma") throw;
                ForumAuthService.ClearCurrentUser();
                throw new UnauthorizedAccessException("Сессия 4PDA истекла. Войдите в аккаунт повторно.");
            }
        }

        public static async Task<List<NewsItem>> GetNewsAsync(int page)
        {
            ApiArray response = await CallAsync("sl", Math.Max(0, page - 1) * 30, 30, -1, 0);
            return await Task.Run(() =>
            {
                ApiArray rows = response.Array(1);
                var items = new List<NewsItem>(rows.Count);
                for (int i = 0; i < rows.Count; i++)
                {
                    ApiArray row = rows.Array(i);
                    NewsItem item = News(row);
                    item.Description = NewsCardPolicy.Summary(row.Text(10));
                    items.Add(item);
                }
                return items;
            });
        }

        public static async Task<NewsArticle> GetArticleAsync(int id)
        {
            ApiArray response = await CallAsync("sa", id);
            return await Task.Run(() =>
            {
                NewsItem item = News(response);
                return new NewsArticle {
                Id = item.Id, Url = item.Url, Title = item.Title, Date = item.Date,
                Author = item.Author, ImageUrl = item.ImageUrl, CommentsCount = item.CommentsCount,
                Html = OfficialApiContent.Html(response.Text(10), response.Array(11), true)
                };
            });
        }

        private static NewsItem News(ApiArray row)
        {
            return new NewsItem
            {
                Id = row.Int(0), Url = "https://4pda.to/index.php?p=" + row.Int(0),
                Date = Date(row.Int(1)), AuthorId = row.Int(5), Author = Clean(row.Text(6)),
                CommentsCount = row.Int(7), ImageUrl = row.Text(8), Title = Clean(row.Text(9))
            };
        }

        public static async Task<ForumItem> GetForumsAsync()
        {
            ApiArray response = await CallAsync("fi");
            ApiArray rows = response.Array(0);
            var root = new ForumItem { Title = "Форумы" };
            ForumItem parent = root;
            for (int i = 0; i < rows.Count; i++)
            {
                ApiArray row = rows.Array(i);
                var forum = new ForumItem { Id = row.Int(0), Title = Clean(row.Text(1)) };
                if ((row.Int(2) & 16) != 0)
                {
                    root.Children.Add(forum); parent = forum;
                }
                else
                {
                    forum.ParentId = parent.Id; forum.Level = 1; parent.Children.Add(forum);
                }
            }
            return root;
        }

        public static async Task<ForumTopicsData> GetTopicsAsync(int forumId, int start)
        {
            ApiArray response = await CallAsync("fs", forumId, Math.Max(0, start), 30);
            var data = new ForumTopicsData
            {
                ForumId = forumId, Title = Clean(response.Text(2)), PageSize = 30,
                CurrentPage = start / 30 + 1, TotalPages = Math.Max(1, (response.Int(7) + 29) / 30)
            };
            if (start == 0)
            {
                ApiArray forums = response.Array(6);
                for (int i = 0; i < forums.Count; i++)
                {
                    ApiArray row = forums.Array(i);
                    data.Items.Add(new ForumTopicItem { Id = row.Int(0), ForumId = row.Int(0), Title = Clean(row.Text(1)),
                        IsForum = true, IsNew = (row.Int(2) & 32) != 0, Url = "https://4pda.to/forum/index.php?showforum=" + row.Int(0) });
                }
            }
            ApiArray topics = response.Array(8);
            for (int i = 0; i < topics.Count; i++) data.Items.Add(Topic(topics.Array(i), forumId));
            return data;
        }

        internal static ForumTopicItem Topic(ApiArray row, int forumId)
        {
            int flags = row.Int(3);
            int id = (flags & 16) != 0 ? row.Int(4) : row.Int(0);
            return new ForumTopicItem
            {
                Id = id, ForumId = forumId, Title = Clean(row.Text(1)), Description = Clean(row.Text(2)),
                IsPinned = (flags & 1) != 0, IsClosed = (flags & 4) != 0, IsNew = (flags & 32) != 0,
                LastUser = Clean(row.Text(7)), Date = Date(row.Int(5)), Url = "https://4pda.to/forum/index.php?showtopic=" + id
            };
        }

        public static async Task<TopicPageData> GetTopicAsync(string url, int start)
        {
            int id = UrlId(url, "showtopic");
            int target = 0, mode = 0;
            int post = ForumPostNavigation.PostId(url);
            bool lastPost = (url ?? string.Empty).IndexOf("view=getlastpost", StringComparison.OrdinalIgnoreCase) >= 0;
            if (start == 0 && post == 0 && !lastPost) start = ForumPostNavigation.StartOffset(url, 20);
            if (start == 0 && (post > 0 || lastPost))
            {
                // Same as Android API.k: topic, position, actual post, mode.
                ApiArray location = await CallAsync("fj", post > 0 ? 3 : 2, post > 0 ? post : id, -1, 0);
                id = location.Int(0); start = ForumPostNavigation.PageOffset(location.Int(1), 20);
                target = location.Int(2); mode = location.Int(3);
            }
            if (id <= 0) throw new ArgumentException("Не указан ID темы.");
            ApiArray response = await CallAsync("fr", id, Math.Max(0, start), 20, mode);
            bool authorized = IsAuthorized;
            return await Task.Run(() => ParseTopic(response, id, start, target, post, authorized));
        }

        private static TopicPageData ParseTopic(ApiArray response, int id, int start, int target, int post, bool authorized)
        {
            ApiArray parents = response.Array(0);
            int forumId = parents.Count > 0 ? parents.Array(parents.Count - 1).Int(1) : 0;
            int flags = response.Int(4);
            var data = new TopicPageData
            {
                TopicId = id, ForumId = forumId, Title = Clean(response.Text(2)), Description = Clean(response.Text(3)),
                StartOffset = start, TargetPostId = target, RequestedPostId = post,
                Url = "https://4pda.to/forum/index.php?showtopic=" + id, PageSize = 20,
                CurrentPage = Math.Max(0, start) / 20 + 1, TotalPages = Math.Max(1, (response.Int(11) + 19) / 20),
                CanReply = authorized && (flags & 64) != 0 && (flags & 16) == 0,
                IsReplyForbidden = (flags & 16) != 0
            };
            ApiArray posts = response.Array(12);
            int number = Math.Max(0, start);
            for (int i = 0; i < posts.Count; i++)
            {
                ApiArray row = posts.Array(i);
                int permissions = row.Int(1);
                int reputation = row.Int(12);
                ApiArray attachments = row.Array(11);
                int[] attachmentIds = new int[attachments.Count];
                for (int j = 0; j < attachments.Count; j++) attachmentIds[j] = attachments.Array(j).Int(0);
                data.Posts.Add(new TopicPostItem
                {
                    Id = row.Int(0), TopicId = id, ForumId = forumId, Number = (permissions & 1) != 0 ? 0 : ++number,
                    UserId = row.Int(2), Author = Clean(row.Text(3)), UserInfo = Group(row.Int(4)),
                    Reputation = row.Int(6).ToString(), Date = Date(row.Int(7)), AvatarUrl = row.Text(9),
                    IsOnline = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - row.Int(5) < 900,
                    BodyHtml = OfficialApiContent.Html(row.Text(8), row.Array(11), false, row.Int(0)),
                    BodyBbCode = row.Text(8),
                    AttachmentIds = attachmentIds,
                    CanQuote = authorized && data.CanReply, CanReport = authorized,
                    CanEdit = authorized && (permissions & 128) != 0,
                    CanDelete = authorized && (permissions & 256) != 0,
                    CanPlus = authorized && (reputation & 1) != 0 && (reputation & 2) == 0,
                    CanMinus = authorized && (reputation & 1) != 0 && (reputation & 2) == 0
                });
            }
            return data;
        }

        public static async Task<ForumTopicsData> SearchTopicsAsync(string query, int start)
        {
            var result = new ForumTopicsData { Title = "Поиск: " + query, CurrentPage = start / 30 + 1, PageSize = 30 };
            if ((query ?? string.Empty).Trim().Length < 3) return result;
            // titles, topics result, include subforums, newest first (Page_Search).
            ApiArray response = await CallAsync("rc", 2 | 65536 | 131072 | 524288,
                new object[] { 0 }, new object[] { 0 }, new object[] { 0 }, query.Trim(), Math.Max(0, start), 30);
            result.TotalPages = Math.Max(1, (response.Int(0) + 29) / 30);
            ApiArray rows = response.Array(1);
            for (int i = 0; i < rows.Count; i++)
            {
                ApiArray row = rows.Array(i);
                if ((row.Int(0) & 2) == 0) continue;
                var values = new Windows.Data.Json.JsonArray();
                for (int j = 1; j < row.Count; j++) values.Add(row.Values[j]);
                ForumTopicItem item = Topic(new ApiArray(values), 0);
                item.Title = OfficialApiContent.PlainText(item.Title);
                item.Description = OfficialApiContent.PlainText(item.Description);
                result.Items.Add(item);
            }
            return result;
        }

        public static async Task<ForumTopicsData> GetFavoritesAsync()
        {
            RequireAuthorization();
            var result = new ForumTopicsData { ForumId = AppNavigationActions.FavoriteForumsId, Title = "Избранное", PageSize = 30 };
            for (int start = 0; start < 3000; start += 30)
            {
                ApiArray response = await CallAsync("mf", start, 30, 0);
                ApiArray rows = response.Array(1);
                for (int i = 0; i < rows.Count; i++)
                {
                    ApiArray row = rows.Array(i);
                    ForumTopicItem item = OfficialApiFavorites.Item(row);
                    if (item == null) continue;
                    item.Date = item.IsForum ? string.Empty : Date(row.Int(9));
                    result.Items.Add(item);
                }
                if (rows.Count == 0 || start + rows.Count >= response.Int(0)) break;
            }
            return result;
        }

        public static async Task<Uri> GetAttachmentDownloadUriAsync(int attachmentId, bool requireAuthorization = true)
        {
            if (requireAuthorization) RequireAuthorization();
            if (attachmentId <= 0) throw new InvalidOperationException("Не удалось определить номер вложения.");
            try
            {
                // Android API.h/i: fa [attachmentId] returns the final download URL.
                return OfficialApiAttachments.DownloadUri(await CallAsync("fa", attachmentId));
            }
            catch (OfficialApiException ex)
            {
                if (ex.Command != "fa") throw;
                throw new InvalidOperationException(OfficialApiAttachments.ErrorMessage(ex.Status));
            }
        }

        public static async Task<ApiArray> LoginAsync(string login, string password, string captcha, bool hidden)
        {
            int code; int.TryParse(captcha, out code);
            ApiArray response = await CallAsync("ml", login, password, hidden ? 1 : 0, code);
            if (response.Int(0) <= 0 || string.IsNullOrWhiteSpace(response.Text(1)))
                throw new InvalidOperationException("Сервер не вернул сессию аккаунта.");
            var values = ApplicationData.Current.LocalSettings.Values;
            values["OfficialApiUserId"] = response.Int(0).ToString();
            values["OfficialApiLoginKey"] = response.Text(1);
            values["OfficialApiHidden"] = hidden ? "1" : "0";
            return response;
        }

        public static void ClearSession()
        {
            var values = ApplicationData.Current.LocalSettings.Values;
            values.Remove("OfficialApiUserId"); values.Remove("OfficialApiLoginKey"); values.Remove("OfficialApiHidden");
            Client.UserId = 0; Client.LoginKey = string.Empty;
        }

        public static async Task<ForumUserProfile> GetProfileAsync(string userId)
        {
            int id;
            if (!int.TryParse(userId, out id) || id <= 0) throw new ArgumentException("Не указан ID пользователя.");
            ApiArray row = await CallAsync("mi", id);
            var profile = new ForumUserProfile
            {
                Id = id.ToString(), Nick = Clean(row.Text(1)), AvatarUrl = row.Text(2), Group = Group(row.Int(3)),
                Status = Clean(row.Text(4)), Signature = OfficialApiContent.PlainText(row.Text(5)),
                About = OfficialApiContent.PlainText(row.Text(6)), RegistrationDate = Date(row.Int(7)),
                LastVisit = Date(row.Int(8)), City = Clean(row.Text(12)), Reputation = row.Int(17).ToString(),
                SiteKarma = (row.Int(14) / 100.0).ToString("0.##"), SitePosts = row.Int(15).ToString(),
                SiteComments = row.Int(16).ToString(), ForumTopics = row.Int(18).ToString(), ForumPosts = row.Int(19).ToString()
            };
            ApiArray devices = row.Array(13);
            for (int i = 0; i < devices.Count; i++)
            {
                ApiArray device = devices.Array(i);
                string url = device.Text(0);
                if (!url.StartsWith("http", StringComparison.OrdinalIgnoreCase)) url = "https://4pda.to/devdb/" + url;
                profile.Devices.Add(new ForumUserDevice { Name = Clean(device.Text(1)), Url = url,
                    Accessory = device.Int(2) == 1 ? "Основное устройство" : string.Empty });
            }
            return profile;
        }

        public static async Task<DevicePageData> GetDeviceAsync(string url)
        {
            Uri uri;
            if (!Uri.TryCreate(url, UriKind.Absolute, out uri) || !uri.AbsolutePath.StartsWith("/devdb/", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Не указан адрес устройства.");
            string key = Uri.UnescapeDataString(uri.AbsolutePath.Substring("/devdb/".Length)).Trim('/').Replace('/', ':');
            ApiArray row = await CallAsync("de", key);
            var data = new DevicePageData
            {
                DeviceId = key, CategoryTitle = Clean(row.Text(1)), BrandTitle = Clean(row.Text(3)),
                Title = Clean(row.Text(3) + " " + row.Text(4)), Subtitle = Clean(row.Text(5)),
                Description = string.Empty, ImageUrls = new List<string>(),
                SpecificationGroups = new List<DeviceSpecificationGroup>(), RelatedItems = new List<DeviceRelatedItem>()
            };
            ApiArray images = row.Array(7);
            for (int i = 0; i < images.Count; i++)
            {
                ApiArray image = images.Array(i);
                string imageUrl = string.IsNullOrWhiteSpace(image.Text(1)) ? image.Text(2) : image.Text(1);
                if (string.IsNullOrWhiteSpace(imageUrl)) continue;
                data.ImageUrls.Add(imageUrl);
                if (string.IsNullOrWhiteSpace(data.ImageUrl) || image.Int(0) == 1) data.ImageUrl = imageUrl;
            }
            ApiArray specs = row.Array(8);
            DeviceSpecificationGroup group = null;
            for (int i = 0; i < specs.Count; i++)
            {
                ApiArray field = specs.Array(i);
                if (field.Int(0) == 0 || group == null)
                {
                    group = new DeviceSpecificationGroup { Title = field.Int(0) == 0 ? Clean(field.Text(2)) : "Характеристики" };
                    data.SpecificationGroups.Add(group);
                }
                if (field.Int(0) != 0)
                    group.Items.Add(new DeviceInfoRow { Title = Clean(field.Text(2)).TrimEnd(':'), Value = Clean(field.Text(4)) });
            }
            return data;
        }

        internal static void RequireAuthorization()
        {
            if (!IsAuthorized) throw new UnauthorizedAccessException("Нужно войти в аккаунт 4PDA.");
        }

        public static async Task<List<QmsContact>> GetQmsContactsAsync()
        {
            RequireAuthorization();
            ApiArray response = await CallAsync("ql");
            ApiArray rows = response.Array(0);
            var contacts = new List<QmsContact>();
            for (int i = 0; i < rows.Count; i++)
            {
                ApiArray row = rows.Array(i);
                contacts.Add(new QmsContact { Id = Math.Abs(row.Int(0)).ToString(), Title = Clean(row.Text(1)),
                    AvatarUrl = row.Text(3), UnreadCount = row.Int(6), Chats = QmsThreads(row.Array(7)) });
            }
            return contacts;
        }

        public static async Task<List<QmsThread>> GetQmsThreadsAsync(string contactId)
        {
            RequireAuthorization();
            int id; if (!int.TryParse(contactId, out id)) throw new ArgumentException("Не указан контакт.");
            ApiArray response = await CallAsync("ql", id);
            return QmsThreads(response.Array(0).Array(0).Array(7));
        }

        private static List<QmsThread> QmsThreads(ApiArray rows)
        {
            var threads = new List<QmsThread>();
            for (int i = 0; i < rows.Count; i++)
            {
                ApiArray row = rows.Array(i);
                threads.Add(new QmsThread { Id = row.Int(0).ToString(), Title = Clean(row.Text(3)),
                    MessagesCount = row.Int(4), UnreadCount = row.Int(5), LastMessageText = Date(row.Int(2)) });
            }
            return threads;
        }

        public static async Task<List<QmsMessage>> GetQmsMessagesAsync(string threadId, string contactNick)
        {
            RequireAuthorization();
            int id; if (!int.TryParse(threadId, out id)) throw new ArgumentException("Не указан диалог.");
            ApiArray response = await CallAsync("qt", id, 0, -60);
            ApiArray rows = response.Array(13);
            var messages = new List<QmsMessage>();
            string previousDay = string.Empty;
            for (int i = 0; i < rows.Count; i++)
            {
                ApiArray row = rows.Array(i);
                bool own = row.Int(1) == Client.UserId;
                DateTimeOffset date = DateTimeOffset.FromUnixTimeSeconds(Math.Max(0, row.Int(2))).ToLocalTime();
                string day = date.ToString("dd.MM.yyyy");
                var message = new QmsMessage
                {
                    Id = row.Int(0).ToString(), Author = own ? ForumAuthService.CurrentUserLogin : contactNick,
                    Text = OfficialApiContent.PlainText(row.Text(3)), IsOwn = own, IsRead = true,
                    DateText = day, TimeText = date.ToString("HH:mm"), DaySeparatorText = day,
                    DateSeparatorVisibility = day != previousDay ? Windows.UI.Xaml.Visibility.Visible : Windows.UI.Xaml.Visibility.Collapsed,
                    TimeVisibility = Windows.UI.Xaml.Visibility.Visible
                };
                previousDay = day;
                ApiArray attachments = row.Array(4);
                for (int j = 0; j < attachments.Count; j++)
                {
                    ApiArray attachment = attachments.Array(j);
                    if (attachment.Int(1) > 0 && !string.IsNullOrWhiteSpace(attachment.Text(4)))
                        message.Images.Add(new QmsMessageImage { Url = attachment.Text(4) });
                }
                message.ApplyViewState();
                messages.Add(message);
            }
            return messages;
        }

        public static async Task SendQmsMessageAsync(string threadId, string message)
        {
            RequireAuthorization();
            int id; if (!int.TryParse(threadId, out id) || id <= 0) throw new ArgumentException("Не указан диалог.");
            if (string.IsNullOrWhiteSpace(message)) throw new ArgumentException("Сообщение пустое.");
            await CallAsync("qp", id, message, new object[0]);
        }

        public static async Task<int> SendTopicMessageAsync(MessagePageParameters parameters, string message, IList<StorageFile> files)
        {
            RequireAuthorization();
            if (parameters == null || parameters.TopicId <= 0) throw new ArgumentException("Не указана тема.");
            if (string.IsNullOrWhiteSpace(message)) throw new ArgumentException("Введите текст сообщения.");
            var attachments = new List<object>();
            if (parameters.ExistingAttachmentIds != null)
                foreach (int id in parameters.ExistingAttachmentIds) if (id > 0) attachments.Add(id);
            if (files != null)
                foreach (StorageFile file in files) attachments.Add(await UploadAttachmentAsync(file));
            ApiArray response = await CallAsync("fp", parameters.TopicId, parameters.EditPostId, 0, message, attachments.ToArray());
            return response.Int(2);
        }

        private static async Task<int> UploadAttachmentAsync(StorageFile file)
        {
            if (file == null) throw new ArgumentException("Не указан файл вложения.");
            using (Stream stream = await file.OpenStreamForReadAsync())
            {
                if (stream.Length <= 0 || stream.Length > int.MaxValue) throw new ArgumentException("Недопустимый размер вложения.");
                var hash = HashAlgorithmProvider.OpenAlgorithm(HashAlgorithmNames.Md5).CreateHash();
                byte[] buffer = new byte[131072];
                int read;
                while ((read = await stream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                {
                    byte[] part = new byte[read]; System.Buffer.BlockCopy(buffer, 0, part, 0, read);
                    hash.Append(CryptographicBuffer.CreateFromByteArray(part));
                }
                string checksum = CryptographicBuffer.EncodeToHexString(hash.GetValueAndReset()).ToUpperInvariant();
                try
                {
                    ApiArray known = await CallAsync("fu", file.Name, (int)stream.Length, checksum);
                    return known.Int(0);
                }
                catch (OfficialApiException ex) { if (ex.Status != 4) throw; }
                string key = "att-" + Client.UserId + "-" + Guid.NewGuid().ToString("N");
                ApiArray initial = await CallAsync("su", key);
                int offset = initial.Int(0);
                if (offset < 0 || offset > stream.Length) throw new IOException("Некорректное смещение загрузки вложения.");
                stream.Position = offset;
                if (stream.Position < stream.Length)
                {
                    ApiArray uploaded = await Client.UploadAsync("su", new object[] { key }, stream);
                    if (uploaded.Int(0) != stream.Length) throw new IOException("Вложение загружено не полностью.");
                }
                ApiArray finished = await CallAsync("su", key, "fu", file.Name, (int)stream.Length, checksum);
                if (finished.Int(0) <= 0) throw new IOException("Сервер не вернул ID вложения.");
                return finished.Int(0);
            }
        }

        public static async Task<string> VotePostAsync(int postId, bool positive)
        {
            RequireAuthorization();
            await CallAsync("fk", postId, positive ? 1 : -1);
            return positive ? "Репутация сообщения повышена" : "Репутация сообщения понижена";
        }

        public static async Task ReportPostAsync(int postId, string message)
        {
            RequireAuthorization();
            await CallAsync("ta", 0, postId, message ?? string.Empty);
        }

        public static async Task DeletePostAsync(int postId)
        {
            RequireAuthorization();
            await CallAsync("fm", 4, new object[] { postId }, 0, 0, "");
        }
        internal static string Group(int id)
        {
            switch (id)
            {
                case 2: return "Гости"; case 3: return "Пользователи"; case 4: return "Администраторы";
                case 5: return "Забанен"; case 7: return "Активные пользователи"; case 8: return "Друзья 4PDA";
                case 9: return "Модераторы"; case 10: return "Супермодераторы"; default: return string.Empty;
            }
        }

        internal static string Date(int timestamp)
        {
            return timestamp > 0 ? DateTimeOffset.FromUnixTimeSeconds(timestamp).ToLocalTime().ToString("dd.MM.yyyy HH:mm") : string.Empty;
        }
        internal static string Clean(string text) { return WebUtility.HtmlDecode(text ?? string.Empty); }
        internal static int UrlId(string url, string name)
        {
            Match match = Regex.Match(url ?? string.Empty, @"(?:[?&])" + Regex.Escape(name) + @"=(\d+)", RegexOptions.IgnoreCase);
            int result; return match.Success && int.TryParse(match.Groups[1].Value, out result) ? result : 0;
        }
        internal static string Setting(string name)
        {
            object value;
            return ApplicationData.Current.LocalSettings.Values.TryGetValue(name, out value) ? Convert.ToString(value) : string.Empty;
        }
        private static int SettingInt(string name) { int value; return int.TryParse(Setting(name), out value) ? value : 0; }
    }
}
