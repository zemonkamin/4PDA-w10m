using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using _4PDA.Services;
using Windows.Data.Html;
using Windows.Data.Json;
using Windows.System.Profile;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Imaging;
using Windows.Web.Http;

namespace _4PDA
{
    public sealed class QmsService
    {
        private const string ForumEndpoint = "https://4pda.to/forum/index.php";
        private const int ContactsCacheSeconds = 600;
        private const int ThreadsCacheSeconds = 300;
        private const int MessagesCacheSeconds = 60;
        private const int MobileThreadsLimit = 40;
        private static readonly RegexOptions Rx = RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant;
        private static readonly object CacheSync = new object();
        private static readonly Dictionary<string, QmsCacheEntry<List<QmsThread>>> ThreadCache = new Dictionary<string, QmsCacheEntry<List<QmsThread>>>();
        private static readonly Dictionary<string, QmsCacheEntry<List<QmsMessage>>> MessageCache = new Dictionary<string, QmsCacheEntry<List<QmsMessage>>>();
        private static readonly Dictionary<string, Task<List<QmsThread>>> ThreadLoads = new Dictionary<string, Task<List<QmsThread>>>();
        private static readonly Dictionary<string, Task<List<QmsMessage>>> MessageLoads = new Dictionary<string, Task<List<QmsMessage>>>();
        private static QmsCacheEntry<List<QmsContact>> _contactsCache;
        private static Task<List<QmsContact>> _contactsLoad;
        private static string _cacheUserId = "";
        private static bool? _suppressDirectNetworkImages;
        private readonly HttpClient _httpClient;

        public QmsService()
        {
            _httpClient = CreateHttpClient();
        }

        public static bool SuppressDirectNetworkImages
        {
            get
            {
                if (!_suppressDirectNetworkImages.HasValue)
                    _suppressDirectNetworkImages = IsWindowsMobile();
                return _suppressDirectNetworkImages.Value;
            }
        }


        public bool TryGetCachedContacts(out List<QmsContact> contacts)
        {
            EnsureCacheUser();
            lock (CacheSync)
            {
                if (IsFresh(_contactsCache, ContactsCacheSeconds))
                {
                    contacts = _contactsCache.Value;
                    return true;
                }
            }

            contacts = null;
            return false;
        }

        public bool TryGetCachedThreads(string contactId, out List<QmsThread> threads)
        {
            EnsureCacheUser();
            string key = contactId == null ? "" : contactId.Trim();
            lock (CacheSync)
            {
                QmsCacheEntry<List<QmsThread>> cached;
                if (ThreadCache.TryGetValue(key, out cached) && IsFresh(cached, ThreadsCacheSeconds))
                {
                    threads = cached.Value;
                    return true;
                }
            }

            threads = null;
            return false;
        }

        private bool TryGetContactThreads(string contactId, out List<QmsThread> threads)
        {
            EnsureCacheUser();
            lock (CacheSync)
            {
                if (IsFresh(_contactsCache, ContactsCacheSeconds) && _contactsCache.Value != null)
                {
                    foreach (QmsContact contact in _contactsCache.Value)
                    {
                        if (contact == null ||
                            !String.Equals(contact.Id, contactId, StringComparison.Ordinal) ||
                            contact.Chats == null ||
                            contact.Chats.Count == 0)
                            continue;

                        threads = new List<QmsThread>(contact.Chats);
                        return true;
                    }
                }
            }

            threads = null;
            return false;
        }

        private static void UpdateCachedContact(QmsContact updated)
        {
            if (updated == null || String.IsNullOrWhiteSpace(updated.Id))
                return;

            lock (CacheSync)
            {
                if (_contactsCache == null || _contactsCache.Value == null)
                    return;

                for (int i = 0; i < _contactsCache.Value.Count; i++)
                {
                    QmsContact contact = _contactsCache.Value[i];
                    if (contact == null || !String.Equals(contact.Id, updated.Id, StringComparison.Ordinal))
                        continue;

                    contact.Title = updated.Title;
                    contact.AvatarUrl = updated.AvatarUrl;
                    contact.UnreadCount = updated.UnreadCount;
                    contact.Chats = updated.Chats;
                    return;
                }
            }
        }

        public bool TryGetCachedMessages(string contactId, string threadId, out List<QmsMessage> messages)
        {
            EnsureCacheUser();
            string key = BuildMessageCacheKey(contactId, threadId);
            lock (CacheSync)
            {
                QmsCacheEntry<List<QmsMessage>> cached;
                if (MessageCache.TryGetValue(key, out cached) && IsFresh(cached, MessagesCacheSeconds))
                {
                    messages = cached.Value;
                    return true;
                }
            }

            messages = null;
            return false;
        }

        public Task<List<QmsContact>> GetContactsAsync()
        {
            EnsureCacheUser();

            lock (CacheSync)
            {
                if (IsFresh(_contactsCache, ContactsCacheSeconds))
                    return Task.FromResult(_contactsCache.Value);

                if (_contactsLoad == null)
                    _contactsLoad = LoadContactsCoreAsync();

                return _contactsLoad;
            }
        }

        private async Task<List<QmsContact>> LoadContactsCoreAsync()
        {
            try
            {
                List<QmsContact> contacts = await OfficialApiService.GetQmsContactsAsync();

                lock (CacheSync)
                    _contactsCache = new QmsCacheEntry<List<QmsContact>>(contacts);

                return contacts;
            }
            finally
            {
                lock (CacheSync)
                    _contactsLoad = null;
            }
        }

        public Task<List<QmsThread>> GetThreadsAsync(string contactId)
        {
            if (String.IsNullOrWhiteSpace(contactId))
                return Task.FromResult(new List<QmsThread>());

            List<QmsThread> cachedThreads;
            if (TryGetContactThreads(contactId.Trim(), out cachedThreads))
                return Task.FromResult(cachedThreads);

            return LoadThreadsCoreAsync(contactId.Trim());
        }

        private async Task<List<QmsThread>> LoadThreadsCoreAsync(string contactId)
        {
            return await OfficialApiService.GetQmsThreadsAsync(contactId);
        }

        private async Task<List<QmsThread>> LoadThreadsFromHtmlAsync(string contactId)
        {
            List<QmsThread> xhrThreads = await TryLoadThreadsFromQmsJsonAsync(contactId);
            if (xhrThreads != null)
                return xhrThreads;

            string url = ForumEndpoint + "?act=qms&mid=" + Uri.EscapeDataString(contactId);
            string html = await GetStringAsync(url);
            EnsureAuthorized(html);
            return ParseThreads(html);
        }

        private async Task<List<QmsThread>> TryLoadThreadsFromQmsJsonAsync(string contactId)
        {
            string url = ForumEndpoint + "?act=qms-xhr&action=userinfo&mid=" + Uri.EscapeDataString(contactId);
            try
            {
                string json = await GetStringAsync(url);
                EnsureAuthorized(json);

                QmsContact contact;
                if (TryParseQmsUserJson(json, out contact))
                {
                    UpdateCachedContact(contact);
                    return contact.Chats ?? new List<QmsThread>();
                }
            }
            catch
            {
            }

            return null;
        }

        public Task<List<QmsMessage>> GetMessagesAsync(string contactId, string threadId, string contactNick, string threadTitle)
        {
            return GetMessagesAsync(contactId, threadId, contactNick, threadTitle, "");
        }

        public Task<List<QmsMessage>> GetMessagesAsync(string contactId, string threadId, string contactNick, string threadTitle, string contactAvatarUrl)
        {
            if (String.IsNullOrWhiteSpace(contactId) || String.IsNullOrWhiteSpace(threadId))
                return Task.FromResult(new List<QmsMessage>());

            EnsureCacheUser();
            string key = BuildMessageCacheKey(contactId, threadId);

            lock (CacheSync)
            {
                QmsCacheEntry<List<QmsMessage>> cached;
                if (MessageCache.TryGetValue(key, out cached) && IsFresh(cached, MessagesCacheSeconds))
                    return Task.FromResult(cached.Value);

                Task<List<QmsMessage>> loading;
                if (MessageLoads.TryGetValue(key, out loading))
                    return loading;

                loading = LoadMessagesCoreAsync(contactId, threadId, contactNick, threadTitle, contactAvatarUrl, key);
                MessageLoads[key] = loading;
                return loading;
            }
        }

        private async Task<List<QmsMessage>> LoadMessagesCoreAsync(string contactId, string threadId, string contactNick, string threadTitle, string contactAvatarUrl, string key)
        {
            try
            {
                List<QmsMessage> messages = await OfficialApiService.GetQmsMessagesAsync(threadId, contactNick);

                lock (CacheSync)
                    MessageCache[key] = new QmsCacheEntry<List<QmsMessage>>(messages);

                return messages;
            }
            finally
            {
                lock (CacheSync)
                    MessageLoads.Remove(key);
            }
        }

        public async Task SendMessageAsync(string contactId, string threadId, string message)
        {
            await OfficialApiService.SendQmsMessageAsync(threadId, message);
            lock (CacheSync) MessageCache.Remove(BuildMessageCacheKey(contactId, threadId));
        }

        private async Task SendMessageWithHtmlAsync(string contactId, string threadId, string message)
        {
            if (String.IsNullOrWhiteSpace(contactId) || String.IsNullOrWhiteSpace(threadId))
                throw new InvalidOperationException("Не указан контакт или чат.");

            if (String.IsNullOrWhiteSpace(message))
                throw new InvalidOperationException("Сообщение пустое.");

            string url = ForumEndpoint + "?act=qms&mid=" + Uri.EscapeDataString(contactId) +
                "&t=" + Uri.EscapeDataString(threadId) + "&xhr=body&do=1";

            List<KeyValuePair<string, string>> data = new List<KeyValuePair<string, string>>();
            data.Add(new KeyValuePair<string, string>("action", "send-message"));
            data.Add(new KeyValuePair<string, string>("mid", contactId));
            data.Add(new KeyValuePair<string, string>("t", threadId));
            data.Add(new KeyValuePair<string, string>("message", message));

            string html = await PostFormAsync(url, data);
            EnsureAuthorized(html);

            if (LooksLikeQmsError(html))
            {
                data[0] = new KeyValuePair<string, string>("action", "add-message");
                html = await PostFormAsync(url, data);
                EnsureAuthorized(html);
                if (LooksLikeQmsError(html))
                    throw new InvalidOperationException(CleanText(html));
            }

            lock (CacheSync)
                MessageCache.Remove(BuildMessageCacheKey(contactId, threadId));
        }

        private async Task<string> GetStringAsync(string url)
        {
            return await GetStringAsync(url, false);
        }

        private async Task<string> GetStringAsync(string url, bool cookiesRefreshed)
        {
            if (!ForumAuthService.IsAuthorized)
                throw new UnauthorizedAccessException("Нужно войти в аккаунт 4PDA.");

            Uri uri = new Uri(url, UriKind.Absolute);
            HttpRequestMessage request = CreateRequest(HttpMethod.Get, uri);
            HttpResponseMessage response = await _httpClient.SendRequestAsync(request);
            string html = await FourPdaResponseHelper.ReadHtmlAsync(response);

            if (!cookiesRefreshed && LooksLikeLoginPage(html))
            {
                ForumCookieHelper.SaveForumCookiesFromSystemCookieManager("QmsService.GetStringAsync.Retry");
                return await GetStringAsync(url, true);
            }

            return html;
        }

        private async Task<string> PostFormAsync(string url, IEnumerable<KeyValuePair<string, string>> data)
        {
            return await PostFormAsync(url, data, false);
        }

        private async Task<string> PostFormAsync(string url, IEnumerable<KeyValuePair<string, string>> data, bool cookiesRefreshed)
        {
            if (!ForumAuthService.IsAuthorized)
                throw new UnauthorizedAccessException("Нужно войти в аккаунт 4PDA.");

            Uri uri = new Uri(url, UriKind.Absolute);
            HttpRequestMessage request = CreateRequest(HttpMethod.Post, uri);
            request.Content = new HttpFormUrlEncodedContent(data);
            HttpResponseMessage response = await _httpClient.SendRequestAsync(request);
            string html = await FourPdaResponseHelper.ReadHtmlAsync(response);

            if (!cookiesRefreshed && LooksLikeLoginPage(html))
            {
                ForumCookieHelper.SaveForumCookiesFromSystemCookieManager("QmsService.PostFormAsync.Retry");
                return await PostFormAsync(url, data, true);
            }

            return html;
        }

        private static HttpClient CreateHttpClient()
        {
            HttpClient client = new HttpClient();
            ForumCookieHelper.ApplyDefaultHeaders(client);
            return client;
        }

        private static HttpRequestMessage CreateRequest(HttpMethod method, Uri uri)
        {
            HttpRequestMessage request = new HttpRequestMessage(method, uri);
            ForumCookieHelper.ApplyDefaultHeaders(request);
            ForumCookieHelper.TryAppendHeader(request, "Accept", "text/html,application/xhtml+xml,application/json;q=0.9,*/*;q=0.8");
            ForumCookieHelper.TryAppendHeader(request, "Referer", ForumEndpoint + "?act=qms");
            ForumCookieHelper.TryAppendHeader(request, "Cache-Control", "no-cache, no-store, max-age=0");

            string cookieHeader = ForumCookieHelper.GetCookieHeaderForUrl(uri);
            if (!String.IsNullOrWhiteSpace(cookieHeader))
                ForumCookieHelper.TryAppendHeader(request, "Cookie", cookieHeader);

            return request;
        }

        private static bool IsWindowsMobile()
        {
            try
            {
                return String.Equals(AnalyticsInfo.VersionInfo.DeviceFamily, "Windows.Mobile", StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private static void EnsureCacheUser()
        {
            string userId = ForumAuthService.CurrentUserId ?? "";
            lock (CacheSync)
            {
                if (String.Equals(_cacheUserId, userId, StringComparison.Ordinal))
                    return;

                _cacheUserId = userId;
                _contactsCache = null;
                _contactsLoad = null;
                ThreadCache.Clear();
                MessageCache.Clear();
                ThreadLoads.Clear();
                MessageLoads.Clear();
            }
        }

        private static bool IsFresh<T>(QmsCacheEntry<T> entry, int seconds)
        {
            return entry != null && (DateTimeOffset.UtcNow - entry.CreatedAt).TotalSeconds <= seconds;
        }

        private static string BuildMessageCacheKey(string contactId, string threadId)
        {
            return (contactId ?? "").Trim() + "|" + (threadId ?? "").Trim();
        }

        private sealed class QmsCacheEntry<T>
        {
            public QmsCacheEntry(T value)
            {
                Value = value;
                CreatedAt = DateTimeOffset.UtcNow;
            }

            public T Value { get; private set; }
            public DateTimeOffset CreatedAt { get; private set; }
        }

        private static List<QmsContact> ParseContacts(string html)
        {
            List<QmsContact> result = new List<QmsContact>();
            if (String.IsNullOrWhiteSpace(html))
                return result;

            if (TryParseQmsContactsJson(html, out result))
                return result;

            HashSet<string> seen = new HashSet<string>();
            int position = 0;
            while (position < html.Length)
            {
                int anchorStart = IndexOfIgnoreCase(html, "<a", position);
                if (anchorStart < 0)
                    break;

                int openEnd = html.IndexOf('>', anchorStart + 2);
                if (openEnd < 0)
                    break;

                string openTag = html.Substring(anchorStart, openEnd - anchorStart + 1);
                position = openEnd + 1;
                string classes = GetRawAttribute(openTag, "class");
                if (!HasClassToken(classes, "list-group-item"))
                    continue;

                int closeStart = IndexOfIgnoreCase(html, "</a", openEnd + 1);
                if (closeStart < 0)
                    break;

                string block = html.Substring(openEnd + 1, closeStart - openEnd - 1);
                position = closeStart + 3;

                string href = GetAttribute(openTag, "href");
                string contactId = GetAttribute(openTag, "data-mid");
                if (String.IsNullOrWhiteSpace(contactId))
                    contactId = ExtractNumberAfterToken(href, "mid=");
                if (String.IsNullOrWhiteSpace(contactId))
                    contactId = ExtractLastPathNumber(href);
                if (String.IsNullOrWhiteSpace(contactId) || !seen.Add(contactId))
                    continue;

                string imageTag = ExtractFirstOpenTag(block, "img");
                string avatar = GetAttribute(imageTag, "data-src");
                if (String.IsNullOrWhiteSpace(avatar))
                    avatar = GetAttribute(imageTag, "data-lazy-src");
                if (String.IsNullOrWhiteSpace(avatar))
                    avatar = GetAttribute(imageTag, "data-original");
                if (String.IsNullOrWhiteSpace(avatar))
                    avatar = GetAttribute(imageTag, "src");

                string title = GetAttribute(imageTag, "title");
                if (String.IsNullOrWhiteSpace(title))
                    title = GetAttribute(imageTag, "alt");
                if (String.IsNullOrWhiteSpace(title))
                    title = GetAttribute(openTag, "title");
                if (String.IsNullOrWhiteSpace(title))
                    title = CleanText(block);

                string unread = ExtractClassElementText(block, "bage");
                if (String.IsNullOrWhiteSpace(unread))
                    unread = ExtractClassElementText(block, "badge");
                if (String.IsNullOrWhiteSpace(unread))
                    unread = ExtractClassElementText(block, "unread");

                QmsContact contact = new QmsContact();
                contact.Id = contactId;
                contact.Title = String.IsNullOrWhiteSpace(title) ? "Пользователь " + contactId : title;
                contact.AvatarUrl = NormalizeUrl(avatar);
                contact.UnreadCount = ParseInt(unread);
                result.Add(contact);
            }

            if (result.Count > 0)
                return result;

            string itemPattern = "<a[^>]+href=\"[^\"]*(?:mid=|/)(\\d+)[^\"]*\"[^>]*>([\\s\\S]*?)</a>";
            foreach (Match match in Regex.Matches(html, itemPattern, Rx))
            {
                string contactId = match.Groups[1].Value;
                if (String.IsNullOrWhiteSpace(contactId) || !seen.Add(contactId))
                    continue;

                string block = match.Groups[2].Value;
                string imageTag = ExtractFirstOpenTag(block, "img");
                string title = GetAttribute(imageTag, "title");
                if (String.IsNullOrWhiteSpace(title))
                    title = GetAttribute(imageTag, "alt");
                if (String.IsNullOrWhiteSpace(title))
                    title = CleanText(block);

                string avatar = GetAttribute(imageTag, "data-src");
                if (String.IsNullOrWhiteSpace(avatar))
                    avatar = GetAttribute(imageTag, "data-lazy-src");
                if (String.IsNullOrWhiteSpace(avatar))
                    avatar = GetAttribute(imageTag, "data-original");
                if (String.IsNullOrWhiteSpace(avatar))
                    avatar = GetAttribute(imageTag, "src");

                QmsContact contact = new QmsContact();
                contact.Id = contactId;
                contact.Title = String.IsNullOrWhiteSpace(title) ? "Пользователь " + contactId : title;
                contact.AvatarUrl = NormalizeUrl(avatar);
                contact.UnreadCount = ParseInt(FirstMatch(block, "<(?:div|span)[^>]+class=\"[^\"]*(?:bage|badge|unread)[^\"]*\"[^>]*>([\\s\\S]*?)</(?:div|span)>"));
                result.Add(contact);
            }

            return result;
        }

        private static bool TryParseQmsContactsJson(string text, out List<QmsContact> contacts)
        {
            contacts = new List<QmsContact>();
            if (String.IsNullOrWhiteSpace(text) || !text.TrimStart().StartsWith("[", StringComparison.Ordinal))
                return false;

            try
            {
                JsonArray root = JsonArray.Parse(text);
                JsonArray users = GetJsonArray(root, 2);
                if (users == null)
                    return false;

                for (uint i = 0; i < users.Count; i++)
                {
                    JsonArray user = GetJsonArray(users, i);
                    if (user == null)
                        continue;

                    QmsContact contact = ParseQmsUserArray(user);
                    AddContactIfNew(contacts, contact);
                }

                return true;
            }
            catch
            {
                contacts = new List<QmsContact>();
                return false;
            }
        }

        private static bool TryParseQmsUserJson(string text, out QmsContact contact)
        {
            contact = null;
            if (String.IsNullOrWhiteSpace(text) || !text.TrimStart().StartsWith("[", StringComparison.Ordinal))
                return false;

            try
            {
                JsonArray root = JsonArray.Parse(text);
                JsonArray users = GetJsonArray(root, 2);
                if (users != null && users.Count > 0)
                {
                    JsonArray user = GetJsonArray(users, 0);
                    if (user != null)
                    {
                        contact = ParseQmsUserArray(user);
                        return contact != null && !String.IsNullOrWhiteSpace(contact.Id);
                    }
                }

                contact = ParseQmsUserArray(root);
                return contact != null && !String.IsNullOrWhiteSpace(contact.Id);
            }
            catch
            {
                contact = null;
                return false;
            }
        }

        private static QmsContact ParseQmsUserArray(JsonArray user)
        {
            if (user == null || user.Count < 2)
                return null;

            string id = GetJsonString(user, 0);
            if (String.IsNullOrWhiteSpace(id))
                return null;

            QmsContact contact = new QmsContact();
            contact.Id = id;
            contact.Title = Decode(GetJsonString(user, 1));
            contact.AvatarUrl = NormalizeUrl(GetJsonString(user, 3));
            contact.UnreadCount = GetJsonInt(user, 6);
            contact.Chats = ParseQmsChatsArray(GetJsonArray(user, 7));
            return contact;
        }

        private static List<QmsThread> ParseQmsChatsArray(JsonArray chats)
        {
            List<QmsThread> result = new List<QmsThread>();
            if (chats == null)
                return result;

            for (uint i = 0; i < chats.Count; i++)
            {
                JsonArray chat = GetJsonArray(chats, i);
                if (chat == null)
                    continue;

                string id = GetJsonString(chat, 0);
                if (String.IsNullOrWhiteSpace(id))
                    continue;

                QmsThread thread = new QmsThread();
                thread.Id = id;
                thread.Title = Decode(GetJsonString(chat, 3));
                thread.MessagesCount = GetJsonInt(chat, 4);
                thread.UnreadCount = GetJsonInt(chat, 5);
                result.Add(thread);
            }

            return result;
        }

        private static JsonArray GetJsonArray(JsonArray array, uint index)
        {
            if (array == null || index >= array.Count)
                return null;

            IJsonValue value = array[(int)index];
            if (value == null || value.ValueType != JsonValueType.Array)
                return null;

            return value.GetArray();
        }

        private static string GetJsonString(JsonArray array, uint index)
        {
            if (array == null || index >= array.Count)
                return "";

            IJsonValue value = array[(int)index];
            if (value == null)
                return "";

            try
            {
                if (value.ValueType == JsonValueType.String)
                    return value.GetString();
                if (value.ValueType == JsonValueType.Number)
                    return ((long)value.GetNumber()).ToString();
                if (value.ValueType == JsonValueType.Boolean)
                    return value.GetBoolean() ? "1" : "0";
            }
            catch
            {
            }

            return "";
        }

        private static int GetJsonInt(JsonArray array, uint index)
        {
            string value = GetJsonString(array, index);
            return ParseInt(value);
        }

        private static string ExtractFirstOpenTag(string html, string tagName)
        {
            if (String.IsNullOrWhiteSpace(html) || String.IsNullOrWhiteSpace(tagName))
                return "";

            int start = IndexOfIgnoreCase(html, "<" + tagName, 0);
            if (start < 0)
                return "";

            int end = html.IndexOf('>', start + tagName.Length + 1);
            return end < 0 ? "" : html.Substring(start, end - start + 1);
        }

        private static string ExtractNumberAfterToken(string value, string token)
        {
            if (String.IsNullOrWhiteSpace(value) || String.IsNullOrWhiteSpace(token))
                return "";

            int index = value.IndexOf(token, StringComparison.OrdinalIgnoreCase);
            if (index < 0)
                return "";

            int start = index + token.Length;
            int end = start;
            while (end < value.Length && Char.IsDigit(value[end]))
                end++;

            return end > start ? value.Substring(start, end - start) : "";
        }

        private static string ExtractLastPathNumber(string value)
        {
            if (String.IsNullOrWhiteSpace(value))
                return "";

            int end = value.Length - 1;
            while (end >= 0 && !Char.IsDigit(value[end]))
                end--;
            if (end < 0)
                return "";

            int start = end;
            while (start >= 0 && Char.IsDigit(value[start]))
                start--;

            return value.Substring(start + 1, end - start);
        }

        private static string ExtractClassElementText(string html, string classToken)
        {
            if (String.IsNullOrWhiteSpace(html) || String.IsNullOrWhiteSpace(classToken))
                return "";

            int position = 0;
            while (position < html.Length)
            {
                int start = html.IndexOf('<', position);
                if (start < 0)
                    return "";

                int openEnd = html.IndexOf('>', start + 1);
                if (openEnd < 0)
                    return "";

                string openTag = html.Substring(start, openEnd - start + 1);
                position = openEnd + 1;
                if (!HasClassToken(GetRawAttribute(openTag, "class"), classToken))
                    continue;

                string tagName = GetTagName(openTag);
                if (String.IsNullOrWhiteSpace(tagName))
                    continue;

                int closeStart = IndexOfIgnoreCase(html, "</" + tagName, openEnd + 1);
                if (closeStart < 0)
                    return "";

                return CleanText(html.Substring(openEnd + 1, closeStart - openEnd - 1));
            }

            return "";
        }

        private static void AddContactIfNew(List<QmsContact> result, QmsContact contact)
        {
            if (contact == null || String.IsNullOrWhiteSpace(contact.Id))
                return;

            foreach (QmsContact item in result)
            {
                if (item != null && item.Id == contact.Id)
                    return;
            }

            if (String.IsNullOrWhiteSpace(contact.Title))
                contact.Title = "Пользователь " + contact.Id;

            result.Add(contact);
        }

        private static List<QmsThread> ParseThreads(string html)
        {
            List<QmsThread> result = new List<QmsThread>();
            if (String.IsNullOrWhiteSpace(html))
                return result;

            HashSet<string> seen = new HashSet<string>();
            int position = 0;
            while (position < html.Length)
            {
                int anchorStart = IndexOfIgnoreCase(html, "<a", position);
                if (anchorStart < 0)
                    break;

                int openEnd = html.IndexOf('>', anchorStart + 2);
                if (openEnd < 0)
                    break;

                string openTag = html.Substring(anchorStart, openEnd - anchorStart + 1);
                position = openEnd + 1;
                string classes = GetRawAttribute(openTag, "class");
                if (!HasClassToken(classes, "list-group-item"))
                    continue;

                int closeStart = IndexOfIgnoreCase(html, "</a", openEnd + 1);
                if (closeStart < 0)
                    break;

                string block = html.Substring(openEnd + 1, closeStart - openEnd - 1);
                position = closeStart + 3;

                string href = GetAttribute(openTag, "href");
                string threadId = GetAttribute(openTag, "data-t");
                if (String.IsNullOrWhiteSpace(threadId))
                    threadId = GetAttribute(openTag, "data-thread-id");
                if (String.IsNullOrWhiteSpace(threadId))
                    threadId = ExtractNumberAfterToken(href, "t=");
                if (String.IsNullOrWhiteSpace(threadId))
                    threadId = ExtractNumberAfterToken(classes, "list-group-item-");
                if (String.IsNullOrWhiteSpace(threadId) || !seen.Add(threadId))
                    continue;

                string title = FirstMatch(block, "<strong[^>]*>([\\s\\S]*?)</strong>");
                if (String.IsNullOrWhiteSpace(title))
                    title = FirstMatch(block, "<span[^>]*class=\"[^\"]*title[^\"]*\"[^>]*>([\\s\\S]*?)</span>");
                if (String.IsNullOrWhiteSpace(title))
                    title = GetAttribute(openTag, "title");
                if (String.IsNullOrWhiteSpace(title))
                    title = CleanText(block);
                else
                    title = Decode(title);

                string unread = ExtractClassElementText(block, "unread");
                if (String.IsNullOrWhiteSpace(unread))
                    unread = ExtractClassElementText(block, "new");
                if (String.IsNullOrWhiteSpace(unread))
                    unread = ExtractClassElementText(block, "badge");
                if (String.IsNullOrWhiteSpace(unread))
                    unread = ExtractClassElementText(block, "bage");

                string count = ExtractClassElementText(block, "count");
                if (String.IsNullOrWhiteSpace(count))
                    count = ExtractClassElementText(block, "messages");

                string lastMessage = FirstMatch(block, "<(?:div|span)[^>]*class=\"[^\"]*(?:desc|last|small)[^\"]*\"[^>]*>([\\s\\S]*?)</(?:div|span)>");

                QmsThread thread = new QmsThread();
                thread.Id = threadId;
                thread.Title = title;
                thread.UnreadCount = ParseInt(unread);
                thread.MessagesCount = ParseInt(count);
                thread.LastMessageText = String.IsNullOrWhiteSpace(lastMessage) ? "" : CleanText(lastMessage);
                result.Add(thread);

                if (SuppressDirectNetworkImages && result.Count >= MobileThreadsLimit)
                    return result;
            }

            if (result.Count > 0)
                return result;

            string linkPattern = "<a[^>]+href=\"[^\"]*[?&]t=(\\d+)[^\"]*\"[^>]*>([\\s\\S]*?)</a>";
            foreach (Match match in Regex.Matches(html, linkPattern, Rx))
            {
                string threadId = match.Groups[1].Value;
                if (String.IsNullOrWhiteSpace(threadId) || !seen.Add(threadId))
                    continue;

                QmsThread thread = new QmsThread();
                thread.Id = threadId;
                thread.Title = CleanText(match.Groups[2].Value);
                result.Add(thread);

                if (SuppressDirectNetworkImages && result.Count >= MobileThreadsLimit)
                    return result;
            }

            return result;
        }

        private sealed class QmsHtmlMarker
        {
            public int Index { get; set; }
            public bool IsDate { get; set; }
            public string Html { get; set; }
        }

        private static List<QmsMessage> ParseMessages(string html, string contactNick, string threadTitle, string contactAvatarUrl)
        {
            List<QmsMessage> result = new List<QmsMessage>();
            if (String.IsNullOrWhiteSpace(html))
                return result;

            List<QmsHtmlMarker> markers = FindQmsMarkers(html);
            int messageStart = 0;
            foreach (QmsHtmlMarker item in markers)
            {
                if (item != null && !item.IsDate)
                {
                    messageStart = item.Index;
                    break;
                }
            }
            int terminalEnd = FindMessagesTerminal(html, messageStart);
            string currentDay = "";
            string lastPrintedDay = "";

            for (int i = 0; i < markers.Count; i++)
            {
                QmsHtmlMarker marker = markers[i];
                if (marker == null)
                    continue;

                if (marker.IsDate)
                {
                    string day = ExtractDateSeparatorText(marker.Html);
                    if (!String.IsNullOrWhiteSpace(day))
                        currentDay = day;
                    continue;
                }

                int blockEnd = GetQmsBlockEnd(markers, i, terminalEnd);
                if (blockEnd <= marker.Index)
                    continue;

                string block = html.Substring(marker.Index, blockEnd - marker.Index);
                QmsMessage message = ParseMessageBlock(block, contactNick, threadTitle, contactAvatarUrl, currentDay);
                if (message == null)
                    continue;

                if (!String.IsNullOrWhiteSpace(currentDay) && currentDay != lastPrintedDay)
                {
                    message.DaySeparatorText = currentDay;
                    message.DateSeparatorVisibility = Visibility.Visible;
                    lastPrintedDay = currentDay;
                }

                result.Add(message);
            }

            if (result.Count > 0)
                return result;

            List<string> blocks = ExtractMessageBlocks(html);
            foreach (string block in blocks)
            {
                QmsMessage message = ParseMessageBlock(block, contactNick, threadTitle, contactAvatarUrl, "");
                if (message != null)
                    result.Add(message);
            }

            return result;
        }

        private static List<QmsHtmlMarker> FindQmsMarkers(string html)
        {
            List<QmsHtmlMarker> markers = new List<QmsHtmlMarker>();
            if (String.IsNullOrWhiteSpace(html))
                return markers;

            int position = 0;
            while (position < html.Length)
            {
                int tagStart = html.IndexOf('<', position);
                if (tagStart < 0)
                    break;

                int tagEnd = html.IndexOf('>', tagStart + 1);
                if (tagEnd < 0)
                    break;

                string openTag = html.Substring(tagStart, tagEnd - tagStart + 1);
                position = tagEnd + 1;

                if (openTag.Length < 3 || openTag[1] == '/' || openTag[1] == '!' || openTag[1] == '?')
                    continue;

                string tagName = GetTagName(openTag);
                if (String.IsNullOrWhiteSpace(tagName))
                    continue;

                if (String.Equals(tagName, "div", StringComparison.OrdinalIgnoreCase))
                {
                    string classes = GetRawAttribute(openTag, "class");
                    if (HasExactClassToken(classes, "date"))
                    {
                        int closeStart = IndexOfIgnoreCase(html, "</div", tagEnd + 1);
                        int closeEnd = closeStart < 0 ? -1 : html.IndexOf('>', closeStart);
                        QmsHtmlMarker dateMarker = new QmsHtmlMarker();
                        dateMarker.Index = tagStart;
                        dateMarker.IsDate = true;
                        dateMarker.Html = closeEnd > closeStart
                            ? html.Substring(tagStart, closeEnd - tagStart + 1)
                            : openTag;
                        markers.Add(dateMarker);
                        continue;
                    }
                }

                if (!String.Equals(tagName, "div", StringComparison.OrdinalIgnoreCase) &&
                    !String.Equals(tagName, "li", StringComparison.OrdinalIgnoreCase) &&
                    !String.Equals(tagName, "a", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (!HasAttribute(openTag, "data-message-id") &&
                    !HasAttribute(openTag, "data-msg-id"))
                    continue;

                if (!IsQmsMessageOpenTag(openTag))
                    continue;

                QmsHtmlMarker messageMarker = new QmsHtmlMarker();
                messageMarker.Index = tagStart;
                messageMarker.IsDate = false;
                messageMarker.Html = openTag;
                markers.Add(messageMarker);
            }

            return markers;
        }

        private static string GetTagName(string openTag)
        {
            if (String.IsNullOrWhiteSpace(openTag) || openTag.Length < 3)
                return "";

            int start = 1;
            while (start < openTag.Length && Char.IsWhiteSpace(openTag[start]))
                start++;

            int end = start;
            while (end < openTag.Length &&
                   !Char.IsWhiteSpace(openTag[end]) &&
                   openTag[end] != '>' &&
                   openTag[end] != '/')
                end++;

            return end > start ? openTag.Substring(start, end - start) : "";
        }

        private static bool IsQmsMessageOpenTag(string openTag)
        {
            if (String.IsNullOrWhiteSpace(openTag))
                return false;

            string classes = GetRawAttribute(openTag, "class");
            if (HasClassToken(classes, "list-group-item") ||
                HasClassToken(classes, "msgbox") ||
                HasClassToken(classes, "qms-message") ||
                HasClassToken(classes, "message-row") ||
                HasClassToken(classes, "msg-row") ||
                HasClassToken(classes, "msg-item"))
                return true;

            return false;
        }

        private static int GetQmsBlockEnd(List<QmsHtmlMarker> markers, int markerIndex, int terminalEnd)
        {
            int end = terminalEnd;
            if (markers != null && markerIndex + 1 < markers.Count && markers[markerIndex + 1] != null)
                end = Math.Min(end, markers[markerIndex + 1].Index);
            return end;
        }

        private static int FindMessagesTerminal(string html, int startIndex)
        {
            if (String.IsNullOrWhiteSpace(html))
                return 0;

            int end = html.Length;
            if (startIndex < 0 || startIndex >= html.Length)
                startIndex = 0;

            int formIndex = IndexOfIgnoreCase(html, "<form", startIndex);
            if (formIndex >= 0)
                end = Math.Min(end, formIndex);

            int textAreaIndex = IndexOfIgnoreCase(html, "<textarea", startIndex);
            if (textAreaIndex >= 0)
                end = Math.Min(end, textAreaIndex);

            int bottomIndex = IndexOfIgnoreCase(html, "id=\"thread-inside-bottom\"", startIndex);
            if (bottomIndex >= 0)
                end = Math.Min(end, bottomIndex);

            return end;
        }

        private static int IndexOfIgnoreCase(string text, string value, int startIndex)
        {
            if (String.IsNullOrEmpty(text) || String.IsNullOrEmpty(value) || startIndex < 0 || startIndex >= text.Length)
                return -1;

            return text.IndexOf(value, startIndex, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsDateSeparatorBlock(string block)
        {
            if (String.IsNullOrWhiteSpace(block))
                return false;

            string open = FirstMatch(block.TrimStart(), "^(<[^>]+>)");
            string classes = GetRawAttribute(open, "class");
            return HasExactClassToken(classes, "date");
        }

        private static string ExtractDateSeparatorText(string block)
        {
            string day = CleanText(FirstNonEmpty(
                FirstMatch(block, "<span[^>]*>([\\s\\S]*?)</span>"),
                block));

            return day;
        }

        private static List<string> ExtractMessageBlocks(string html)
        {
            List<string> blocks = new List<string>();
            if (String.IsNullOrWhiteSpace(html))
                return blocks;

            string[] patterns = new string[]
            {
                "<(?:div|li|a)[^>]+(?=class=\"[^\"]*(?:list-group-item|msgbox|qms-message|message-row|msg-row|msg-item)[^\"]*\")(?=[^>]*(?:data-message-id|data-msg-id))[\\s\\S]*?(?=<(?:div|li|a)[^>]+(?=class=\"[^\"]*(?:date|list-group-item|msgbox|qms-message|message-row|msg-row|msg-item)[^\"]*\")|<form|<textarea|$)",
                "<(?:div|li)[^>]+class=\"[^\"]*(?:qms-message|message-row|msg-row|msg-item|msg\\b)[^\"]*\"[^>]*>[\\s\\S]*?(?=<(?:div|li)[^>]+class=\"[^\"]*(?:qms-message|message-row|msg-row|msg-item|msg\\b)[^\"]*\"|<form|<textarea|$)",
                "<(?:div|li)[^>]+(?:data-msg-id|data-message-id)[^>]*>[\\s\\S]*?(?=<(?:div|li)[^>]+(?:data-msg-id|data-message-id)|<form|<textarea|$)"
            };

            foreach (string pattern in patterns)
            {
                foreach (Match match in Regex.Matches(html, pattern, Rx))
                {
                    string block = match.Value;
                    if (!IsPotentialMessageBlock(block))
                        continue;

                    if (!ContainsString(blocks, block))
                        blocks.Add(block);
                }

                if (blocks.Count > 0)
                    return blocks;
            }

            return blocks;
        }

        private static bool ContainsString(List<string> items, string value)
        {
            if (items == null)
                return false;

            foreach (string item in items)
            {
                if (item == value)
                    return true;
            }

            return false;
        }

        private static bool IsPotentialMessageBlock(string block)
        {
            if (String.IsNullOrWhiteSpace(block))
                return false;

            string lower = block.ToLowerInvariant();
            if (lower.IndexOf("act=qms", StringComparison.Ordinal) >= 0 &&
                lower.IndexOf("data-message-id", StringComparison.Ordinal) < 0 &&
                lower.IndexOf("data-msg-id", StringComparison.Ordinal) < 0)
                return false;

            bool hasTextContainer = Regex.IsMatch(block, "class=\"[^\"]*(?:msg-content|msg-text|message-body|message-text|postcolor|content|body|text)[^\"]*\"", Rx);
            bool hasId = Regex.IsMatch(block, "(?:data-msg-id|data-message-id)\\s*=", Rx);
            return hasTextContainer || hasId;
        }

        private static QmsMessage ParseMessageBlock(string block, string contactNick, string threadTitle, string contactAvatarUrl, string currentDay)
        {
            if (String.IsNullOrWhiteSpace(block))
                return null;

            string content = ExtractMessageContent(block);
            if (String.IsNullOrWhiteSpace(content))
                return null;

            bool isOwn = DetectOwnMessage(block, contactNick);
            string timeText = ExtractMessageTime(block);
            QmsMessage message = new QmsMessage();
            message.Id = ExtractMessageId(block);
            message.Author = "";
            message.Text = CleanMessageText(content);
            message.TimeText = timeText;
            message.DateText = BuildDateText(currentDay, timeText);
            message.IsOwn = isOwn;
            message.IsRead = !HasUnreadMarker(block);

            List<QmsMessageImage> images = ExtractImages(content, contactAvatarUrl, "");
            foreach (QmsMessageImage image in images)
                message.Images.Add(image);

            if (String.IsNullOrWhiteSpace(message.Text) && message.Images.Count == 0)
                return null;

            return message;
        }

        private static string ExtractMessageId(string block)
        {
            string open = FirstMatch(block.TrimStart(), "^(<[^>]+>)");
            string value = GetAttribute(open, "data-message-id");
            if (String.IsNullOrWhiteSpace(value))
                value = GetAttribute(open, "data-msg-id");
            if (String.IsNullOrWhiteSpace(value))
                value = FirstMatch(block, "data-message-id\\s*=\\s*\"([^\"]+)\"");
            if (String.IsNullOrWhiteSpace(value))
                value = FirstMatch(block, "data-message-id\\s*=\\s*'([^']+)'");
            if (String.IsNullOrWhiteSpace(value))
                value = FirstMatch(block, "data-msg-id\\s*=\\s*\"([^\"]+)\"");
            if (String.IsNullOrWhiteSpace(value))
                value = FirstMatch(block, "data-msg-id\\s*=\\s*'([^']+)'");
            return value;
        }

        private static bool HasUnreadMarker(string block)
        {
            if (String.IsNullOrWhiteSpace(block))
                return false;

            string lower = block.ToLowerInvariant();
            return lower.IndexOf("data-unread-status=\"1\"", StringComparison.Ordinal) >= 0 ||
                lower.IndexOf("data-unread-status='1'", StringComparison.Ordinal) >= 0 ||
                lower.IndexOf(" unread", StringComparison.Ordinal) >= 0;
        }

        private static string BuildDateText(string day, string time)
        {
            if (!String.IsNullOrWhiteSpace(day) && !String.IsNullOrWhiteSpace(time))
                return day + " " + time;
            if (!String.IsNullOrWhiteSpace(day))
                return day;
            return time;
        }

        private static string ExtractMessageTime(string block)
        {
            if (String.IsNullOrWhiteSpace(block))
                return "";

            string raw = FirstMatch(block, "</b>\\s*([^<]{1,80})");
            if (String.IsNullOrWhiteSpace(raw))
                raw = FirstMatch(block, "<(?:span|div)[^>]+class=\"[^\"]*(?:msg-date|message-date|time)[^\"]*\"[^>]*>([\\s\\S]*?)</(?:span|div)>");
            if (String.IsNullOrWhiteSpace(raw))
                raw = FirstMatch(block, "<time[^>]*>([\\s\\S]*?)</time>");

            string time = ExtractTimeValue(String.IsNullOrWhiteSpace(raw) ? "" : CleanText(raw));
            if (!String.IsNullOrWhiteSpace(time))
                return time;

            return ExtractTimeValue(CleanText(block));
        }

        private static string ExtractTimeValue(string text)
        {
            if (String.IsNullOrWhiteSpace(text))
                return "";

            Match match = Regex.Match(text, "\\b([0-2]?\\d:[0-5]\\d(?::[0-5]\\d)?)\\b", RegexOptions.CultureInvariant);
            return match.Success ? match.Groups[1].Value : "";
        }

        private static string GetMessageAuthor(bool isOwn, string contactNick, string block)
        {
            if (isOwn)
            {
                string current = GetCurrentUserLoginSafe();
                if (!String.IsNullOrWhiteSpace(current))
                    return current;

                return "Вы";
            }

            if (!String.IsNullOrWhiteSpace(contactNick))
                return contactNick;

            string extracted = ExtractAuthorNick(block);
            return String.IsNullOrWhiteSpace(extracted) ? "Собеседник" : extracted;
        }

        private static string GetCurrentUserLoginSafe()
        {
            try
            {
                return ForumAuthService.CurrentUserLogin;
            }
            catch
            {
                return "";
            }
        }

        private static string ExtractMessageContent(string block)
        {
            string content = ExtractDivContentByClass(block, "msg-content");
            if (String.IsNullOrWhiteSpace(content))
                content = ExtractDivContentByClass(block, "msg-text");
            if (String.IsNullOrWhiteSpace(content))
                content = ExtractDivContentByClass(block, "message-text");
            if (String.IsNullOrWhiteSpace(content))
                content = ExtractDivContentByClass(block, "message-body");
            if (String.IsNullOrWhiteSpace(content))
                content = ExtractDivContentByClass(block, "postcolor");
            if (String.IsNullOrWhiteSpace(content))
                content = FirstMatch(block, "<td[^>]+class=\"[^\"]*(?:msg-text|message-text|message-body|postcolor|content|body|text)[^\"]*\"[^>]*>([\\s\\S]*?)</td>");

            if (String.IsNullOrWhiteSpace(content))
            {
                string cleanedBlock = RemoveNonMessageParts(block);
                if (HasMessageContent(cleanedBlock))
                    return cleanedBlock;
                return "";
            }

            return RemoveNonMessageParts(content);
        }

        private static string ExtractDivContentByClass(string html, string classToken)
        {
            if (String.IsNullOrWhiteSpace(html) || String.IsNullOrWhiteSpace(classToken))
                return "";

            foreach (Match match in Regex.Matches(html, "<div[^>]+class=\"([^\"]*)\"[^>]*>", Rx))
            {
                string classes = match.Groups[1].Value;
                if (!HasClassToken(classes, classToken))
                    continue;

                int contentStart = match.Index + match.Length;
                int contentEnd = FindClosingDiv(html, contentStart);
                if (contentEnd > contentStart)
                    return html.Substring(contentStart, contentEnd - contentStart);
            }

            foreach (Match match in Regex.Matches(html, "<div[^>]+class='([^']*)'[^>]*>", Rx))
            {
                string classes = match.Groups[1].Value;
                if (!HasClassToken(classes, classToken))
                    continue;

                int contentStart = match.Index + match.Length;
                int contentEnd = FindClosingDiv(html, contentStart);
                if (contentEnd > contentStart)
                    return html.Substring(contentStart, contentEnd - contentStart);
            }

            return "";
        }

        private static int FindClosingDiv(string html, int contentStart)
        {
            if (String.IsNullOrWhiteSpace(html) || contentStart < 0 || contentStart >= html.Length)
                return -1;

            int depth = 1;
            int position = contentStart;
            while (position < html.Length)
            {
                int nextOpen = html.IndexOf("<div", position, StringComparison.OrdinalIgnoreCase);
                int nextClose = html.IndexOf("</div", position, StringComparison.OrdinalIgnoreCase);

                if (nextClose < 0)
                    return -1;

                if (nextOpen >= 0 && nextOpen < nextClose)
                {
                    int openEnd = html.IndexOf('>', nextOpen + 4);
                    if (openEnd < 0)
                        return -1;
                    depth++;
                    position = openEnd + 1;
                    continue;
                }

                depth--;
                if (depth == 0)
                    return nextClose;

                int closeEnd = html.IndexOf('>', nextClose + 5);
                if (closeEnd < 0)
                    return -1;
                position = closeEnd + 1;
            }

            return -1;
        }

        private static bool HasMessageContent(string html)
        {
            if (String.IsNullOrWhiteSpace(html))
                return false;

            if (CleanMessageText(html).Length > 0)
                return true;

            return ExtractImages(html).Count > 0;
        }

        private static string RemoveNonMessageParts(string html)
        {
            if (String.IsNullOrWhiteSpace(html))
                return "";

            string result = html;
            result = Regex.Replace(result, "<(?:script|style)[\\s\\S]*?</(?:script|style)>", " ", Rx);
            result = Regex.Replace(result, "<(?:div|span|a)[^>]+class=\"[^\"]*(?:avatar|ava|userpic|nick|name|author|date|time|status|read-status|controls|actions)[^\"]*\"[^>]*>[\\s\\S]*?</(?:div|span|a)>", " ", Rx);
            result = Regex.Replace(result, "<(?:div|span|a)[^>]+class='[^']*(?:avatar|ava|userpic|nick|name|author|date|time|status|read-status|controls|actions)[^']*'[^>]*>[\\s\\S]*?</(?:div|span|a)>", " ", Rx);
            result = Regex.Replace(result, "<img[^>]+(?:class|alt|title)=\"[^\"]*(?:avatar|аватар|userpic)[^\"]*\"[^>]*>", " ", Rx);
            result = Regex.Replace(result, "<img[^>]+(?:class|alt|title)='[^']*(?:avatar|аватар|userpic)[^']*'[^>]*>", " ", Rx);
            result = Regex.Replace(result, "<a[^>]+href=\"[^\"]*act=members[^\"]*\"[^>]*>[\\s\\S]*?</a>", " ", Rx);
            result = Regex.Replace(result, "<a[^>]+href='[^']*act=members[^']*'[^>]*>[\\s\\S]*?</a>", " ", Rx);
            return result;
        }

        private static bool DetectOwnMessage(string block, string contactNick)
        {
            if (String.IsNullOrWhiteSpace(block))
                return false;

            string open = FirstMatch(block.TrimStart(), "^(<[^>]+>)");
            string classes = GetRawAttribute(open, "class");

            if (HasClassToken(classes, "not-our-message"))
                return false;

            if (HasClassToken(classes, "our-message"))
                return true;

            if (HasClassToken(classes, "my-message") ||
                HasClassToken(classes, "own-message") ||
                HasClassToken(classes, "from-me") ||
                HasClassToken(classes, "outgoing") ||
                HasClassToken(classes, "msg-my") ||
                HasClassToken(classes, "my-msg") ||
                HasClassToken(classes, "message-my") ||
                HasClassToken(classes, "msg-right"))
                return true;

            if (HasClassToken(classes, "list-group-item") &&
                Regex.IsMatch(open, "data-(?:message|msg)-id\\s*=", Rx))
                return false;

            if (HasClassToken(classes, "incoming") ||
                HasClassToken(classes, "from-them") ||
                HasClassToken(classes, "other") ||
                HasClassToken(classes, "msg-left"))
                return false;

            string authorNick = NormalizeComparable(ExtractAuthorNick(block));
            string contact = NormalizeComparable(contactNick);
            if (contact.Length > 0 && authorNick == contact)
                return false;

            string current = NormalizeComparable(GetCurrentUserLoginSafe());
            if (current.Length > 0 && authorNick == current)
                return true;

            return false;
        }

        private static string ExtractAuthorNick(string block)
        {
            string avatarTag = FirstMatch(block, "(<img[^>]+class=\"[^\"]*(?:avatar|userpic|photo)[^\"]*\"[^>]*>)");
            if (String.IsNullOrWhiteSpace(avatarTag))
                avatarTag = FirstMatch(block, "(<img[^>]+(?:title|alt)=\"[^\"]+\"[^>]*>)");

            string authorHtml = FirstNonEmpty(
                FirstMatch(block, "<(?:a|span|div)[^>]+class=\"[^\"]*(?:nick|name|author|user)[^\"]*\"[^>]*>([\\s\\S]*?)</(?:a|span|div)>"),
                GetAttribute(avatarTag, "title"),
                GetAttribute(avatarTag, "alt"),
                FirstMatch(block, "<b[^>]*>([\\s\\S]*?)</b>"));

            return CleanText(authorHtml);
        }

        private static bool HasExactClassToken(string classes, string token)
        {
            if (String.IsNullOrWhiteSpace(classes) || String.IsNullOrWhiteSpace(token))
                return false;

            string[] parts = classes.Split(new char[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string part in parts)
            {
                if (String.Equals(part, token, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        private static bool HasClassToken(string classes, string token)
        {
            if (String.IsNullOrWhiteSpace(classes) || String.IsNullOrWhiteSpace(token))
                return false;

            string text = " " + classes.Replace('_', ' ').Replace('-', ' ') + " ";
            string normalized = " " + token.Replace('_', ' ').Replace('-', ' ') + " ";
            return text.IndexOf(normalized, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string NormalizeComparable(string value)
        {
            if (String.IsNullOrWhiteSpace(value))
                return "";

            string result = CleanText(value).ToLowerInvariant();
            result = Regex.Replace(result, "\\s+", " ");
            return result.Trim();
        }

        private static bool SameUrl(string left, string right)
        {
            string a = NormalizeUrl(left).TrimEnd('/').ToLowerInvariant();
            string b = NormalizeUrl(right).TrimEnd('/').ToLowerInvariant();
            return a.Length > 0 && b.Length > 0 && a == b;
        }

        private static string CleanMessageText(string html)
        {
            if (String.IsNullOrWhiteSpace(html))
                return "";

            string text = html;
            text = Regex.Replace(text, "<img[^>]*>", " ", Rx);
            text = Regex.Replace(text, "<script[\\s\\S]*?</script>", " ", Rx);
            text = Regex.Replace(text, "<style[\\s\\S]*?</style>", " ", Rx);
            text = Regex.Replace(text, "<blockquote[\\s\\S]*?</blockquote>", " ", Rx);
            text = Regex.Replace(text, "<br\\s*/?>", "\n", Rx);
            text = Regex.Replace(text, "</p>|</div>|</li>|</tr>", "\n", Rx);
            text = Regex.Replace(text, "<(?:span|div)[^>]+class=\"[^\"]*(?:date|time|avatar|controls|actions)[^\"]*\"[^>]*>[\\s\\S]*?</(?:span|div)>", " ", Rx);
            return CleanText(text);
        }

        private static List<QmsMessageImage> ExtractImages(string html)
        {
            return ExtractImages(RemoveNonMessageParts(html), "", "");
        }

        private static List<QmsMessageImage> ExtractImages(string html, string skipAvatarUrl, string skipMessageAvatarUrl)
        {
            List<QmsMessageImage> result = new List<QmsMessageImage>();
            if (String.IsNullOrWhiteSpace(html))
                return result;

            string content = html;
            foreach (Match match in Regex.Matches(content, "<img[^>]*>", Rx))
            {
                string tag = match.Value;
                string url = GetAttribute(tag, "data-fullsrc");
                if (String.IsNullOrWhiteSpace(url))
                    url = GetAttribute(tag, "data-src");
                if (String.IsNullOrWhiteSpace(url))
                    url = GetAttribute(tag, "data-lazy-src");
                if (String.IsNullOrWhiteSpace(url))
                    url = GetAttribute(tag, "data-original");
                if (String.IsNullOrWhiteSpace(url))
                    url = GetAttribute(tag, "data-url");
                if (String.IsNullOrWhiteSpace(url))
                    url = GetAttribute(tag, "src");
                url = NormalizeUrl(url);

                AddMessageImageIfAllowed(result, tag, url, skipAvatarUrl, skipMessageAvatarUrl);
            }

            foreach (Match match in Regex.Matches(content, "<a[^>]+href=\"([^\"]+)\"[^>]*>[\\s\\S]*?</a>", Rx))
            {
                string url = NormalizeUrl(Decode(match.Groups[1].Value));
                if (!LooksLikeImageUrl(url))
                    continue;

                AddMessageImageIfAllowed(result, match.Value, url, skipAvatarUrl, skipMessageAvatarUrl);
            }

            return result;
        }

        private static void AddMessageImageIfAllowed(List<QmsMessageImage> result, string tag, string url, string skipAvatarUrl, string skipMessageAvatarUrl)
        {
            if (String.IsNullOrWhiteSpace(url) ||
                !LooksLikeImageUrl(url) ||
                SameUrl(url, skipAvatarUrl) ||
                SameUrl(url, skipMessageAvatarUrl) ||
                IsIgnoredMessageImage(tag, url) ||
                ContainsImage(result, url))
                return;

            QmsMessageImage image = new QmsMessageImage();
            image.Url = url;
            result.Add(image);
        }

        private static bool ContainsImage(List<QmsMessageImage> result, string url)
        {
            foreach (QmsMessageImage image in result)
            {
                if (image != null && SameUrl(image.Url, url))
                    return true;
            }

            return false;
        }

        private static bool LooksLikeImageUrl(string url)
        {
            if (String.IsNullOrWhiteSpace(url))
                return false;

            string lower = url.ToLowerInvariant();
            int sharp = lower.IndexOf('#');
            if (sharp >= 0)
                lower = lower.Substring(0, sharp);

            int question = lower.IndexOf('?');
            if (question >= 0)
                lower = lower.Substring(0, question);

            return lower.EndsWith(".jpg", StringComparison.Ordinal) ||
                lower.EndsWith(".jpeg", StringComparison.Ordinal) ||
                lower.EndsWith(".png", StringComparison.Ordinal) ||
                lower.EndsWith(".gif", StringComparison.Ordinal) ||
                lower.EndsWith(".bmp", StringComparison.Ordinal) ||
                lower.EndsWith(".webp", StringComparison.Ordinal) ||
                lower.IndexOf("/forum/dl/post/", StringComparison.Ordinal) >= 0 ||
                lower.IndexOf("/uploads/", StringComparison.Ordinal) >= 0 ||
                lower.IndexOf("attach", StringComparison.Ordinal) >= 0;
        }

        private static bool IsIgnoredMessageImage(string tag, string url)
        {
            string lowerUrl = (url == null ? "" : url).ToLowerInvariant();
            string lowerTag = (tag == null ? "" : tag).ToLowerInvariant();

            if (lowerTag.IndexOf("avatar", StringComparison.Ordinal) >= 0 ||
                lowerTag.IndexOf("аватар", StringComparison.Ordinal) >= 0 ||
                lowerTag.IndexOf("userpic", StringComparison.Ordinal) >= 0 ||
                lowerTag.IndexOf("emoticon", StringComparison.Ordinal) >= 0 ||
                lowerTag.IndexOf("smil", StringComparison.Ordinal) >= 0)
                return true;

            int width = ParseAttributeInt(tag, "width");
            int height = ParseAttributeInt(tag, "height");
            if (width > 0 && height > 0 && width <= 32 && height <= 32)
                return true;

            return lowerUrl.IndexOf("style_emoticons", StringComparison.Ordinal) >= 0 ||
                lowerUrl.IndexOf("style_images", StringComparison.Ordinal) >= 0 ||
                lowerUrl.IndexOf("/icons/", StringComparison.Ordinal) >= 0 ||
                lowerUrl.IndexOf("icon_", StringComparison.Ordinal) >= 0 ||
                lowerUrl.IndexOf("emoticon", StringComparison.Ordinal) >= 0 ||
                lowerUrl.IndexOf("smil", StringComparison.Ordinal) >= 0 ||
                lowerUrl.IndexOf("avatar", StringComparison.Ordinal) >= 0 ||
                lowerUrl.IndexOf("userpic", StringComparison.Ordinal) >= 0 ||
                lowerUrl.IndexOf("spacer", StringComparison.Ordinal) >= 0 ||
                lowerUrl.IndexOf("captcha", StringComparison.Ordinal) >= 0;
        }


        private static int ParseAttributeInt(string html, string attributeName)
        {
            string value = GetAttribute(html, attributeName);
            return ParseInt(value);
        }

        private static void EnsureAuthorized(string html)
        {
            if (LooksLikeLoginPage(html))
                throw new UnauthorizedAccessException("Нужно войти в аккаунт 4PDA.");
        }

        private static bool LooksLikeLoginPage(string html)
        {
            if (String.IsNullOrWhiteSpace(html))
                return false;

            string lower = html.ToLowerInvariant();
            return (lower.IndexOf("act=login", StringComparison.Ordinal) >= 0 ||
                    lower.IndexOf("login-form", StringComparison.Ordinal) >= 0 ||
                    lower.IndexOf("name=\"username\"", StringComparison.Ordinal) >= 0) &&
                   lower.IndexOf("act=qms", StringComparison.Ordinal) < 0;
        }

        private static bool LooksLikeQmsError(string html)
        {
            if (String.IsNullOrWhiteSpace(html))
                return false;

            string cleaned = CleanText(html).ToLowerInvariant();
            if (cleaned.Length == 0)
                return false;

            return cleaned.IndexOf("ошибка", StringComparison.Ordinal) >= 0 ||
                cleaned.IndexOf("error", StringComparison.Ordinal) >= 0 ||
                cleaned.IndexOf("не удалось", StringComparison.Ordinal) >= 0 ||
                cleaned.IndexOf("заполните", StringComparison.Ordinal) >= 0;
        }

        private static string FirstMatch(string html, string pattern)
        {
            if (String.IsNullOrWhiteSpace(html) || String.IsNullOrWhiteSpace(pattern))
                return "";

            Match match = Regex.Match(html, pattern, Rx);
            return match.Success ? match.Groups[1].Value : "";
        }

        private static string FirstNonEmpty(params string[] values)
        {
            if (values == null)
                return "";

            foreach (string value in values)
            {
                if (!String.IsNullOrWhiteSpace(value))
                    return value;
            }

            return "";
        }

        private static bool HasAttribute(string html, string attributeName)
        {
            return !String.IsNullOrWhiteSpace(GetRawAttribute(html, attributeName));
        }

        private static string GetRawAttribute(string html, string attributeName)
        {
            if (String.IsNullOrWhiteSpace(html) || String.IsNullOrWhiteSpace(attributeName))
                return "";

            int searchStart = 0;
            while (searchStart < html.Length)
            {
                int index = html.IndexOf(attributeName, searchStart, StringComparison.OrdinalIgnoreCase);
                if (index < 0)
                    return "";

                int afterName = index + attributeName.Length;
                bool validBefore = index == 0 || !IsAttributeNameCharacter(html[index - 1]);
                bool validAfter = afterName >= html.Length || !IsAttributeNameCharacter(html[afterName]);
                if (!validBefore || !validAfter)
                {
                    searchStart = afterName;
                    continue;
                }

                int position = afterName;
                while (position < html.Length && Char.IsWhiteSpace(html[position]))
                    position++;
                if (position >= html.Length || html[position] != '=')
                {
                    searchStart = afterName;
                    continue;
                }

                position++;
                while (position < html.Length && Char.IsWhiteSpace(html[position]))
                    position++;
                if (position >= html.Length || (html[position] != '"' && html[position] != '\''))
                    return "";

                char quote = html[position];
                int valueStart = position + 1;
                int valueEnd = html.IndexOf(quote, valueStart);
                if (valueEnd < 0)
                    return "";

                return html.Substring(valueStart, valueEnd - valueStart);
            }

            return "";
        }

        private static string GetAttribute(string html, string attributeName)
        {
            string raw = GetRawAttribute(html, attributeName);
            return String.IsNullOrWhiteSpace(raw) ? "" : Decode(raw);
        }

        private static bool IsAttributeNameCharacter(char value)
        {
            return Char.IsLetterOrDigit(value) || value == '-' || value == '_' || value == ':';
        }

        private static string CleanText(string html)
        {
            if (String.IsNullOrWhiteSpace(html))
                return "";

            string text = Regex.Replace(html, "<script[\\s\\S]*?</script>", " ", Rx);
            text = Regex.Replace(text, "<style[\\s\\S]*?</style>", " ", Rx);
            text = Regex.Replace(text, "<[^>]+>", " ", Rx);
            text = Decode(text);
            text = Regex.Replace(text, "[ \\t\\x0B\\f\\r]+", " ");
            text = Regex.Replace(text, " *\\n *", "\n");
            text = Regex.Replace(text, "\\n{3,}", "\n\n");
            return text.Trim();
        }

        private static string Decode(string value)
        {
            if (String.IsNullOrWhiteSpace(value))
                return "";

            try
            {
                return HtmlUtilities.ConvertToText(value).Trim();
            }
            catch
            {
                return value.Trim();
            }
        }

        private static int ParseInt(string value)
        {
            if (String.IsNullOrWhiteSpace(value))
                return 0;

            Match match = Regex.Match(value, "\\d+");
            if (!match.Success)
                return 0;

            int result;
            return Int32.TryParse(match.Value, out result) ? result : 0;
        }

        private static string NormalizeUrl(string url)
        {
            if (String.IsNullOrWhiteSpace(url))
                return "";

            string result = Decode(url).Trim();
            if (result.StartsWith("data:", StringComparison.OrdinalIgnoreCase) ||
                result.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase))
                return "";

            if (result.StartsWith("//", StringComparison.Ordinal))
                result = "https:" + result;
            else if (result.StartsWith("/", StringComparison.Ordinal))
                result = "https://4pda.to" + result;
            else if (result.StartsWith("http://4pda.ru", StringComparison.OrdinalIgnoreCase))
                result = "https://4pda.to" + result.Substring("http://4pda.ru".Length);
            else if (result.StartsWith("https://4pda.ru", StringComparison.OrdinalIgnoreCase))
                result = "https://4pda.to" + result.Substring("https://4pda.ru".Length);
            else if (!result.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                     !result.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                result = "https://4pda.to/forum/" + result.TrimStart('/');

            return result;
        }
    }

    public sealed class QmsContact
    {
        public string Id { get; set; }
        public string Title { get; set; }
        public string AvatarUrl { get; set; }
        public int UnreadCount { get; set; }
        public List<QmsThread> Chats { get; set; }
    }

    public sealed class QmsThread
    {
        public string Id { get; set; }
        public string Title { get; set; }
        public string LastMessageText { get; set; }
        public int MessagesCount { get; set; }
        public int UnreadCount { get; set; }
    }

    public sealed class QmsMessage
    {
        public QmsMessage()
        {
            Images = new ObservableCollection<QmsMessageImage>();
            Id = "";
            Author = "";
            Text = "";
            DateText = "";
            TimeText = "";
            DaySeparatorText = "";
            DateSeparatorVisibility = Visibility.Collapsed;
            TimeVisibility = Visibility.Collapsed;
            BubbleAlignment = HorizontalAlignment.Left;
            BubbleBrush = null;
            AuthorBrush = null;
        }

        public string Id { get; set; }
        public string Author { get; set; }
        public string Text { get; set; }
        public string DateText { get; set; }
        public string TimeText { get; set; }
        public string DaySeparatorText { get; set; }
        public bool IsOwn { get; set; }
        public bool IsRead { get; set; }
        public ObservableCollection<QmsMessageImage> Images { get; private set; }
        public HorizontalAlignment BubbleAlignment { get; set; }
        public Brush BubbleBrush { get; set; }
        public Brush AuthorBrush { get; set; }
        public Visibility DateSeparatorVisibility { get; set; }
        public Visibility TimeVisibility { get; set; }

        private static Brush _ownMessageBrush;
        private static Brush _otherMessageBrush;
        private static Brush _transparentBrush;

        public void ApplyViewState()
        {
            if (_ownMessageBrush == null)
                _ownMessageBrush = CreateDarkAccentBrush();
            if (_otherMessageBrush == null)
                _otherMessageBrush = new SolidColorBrush(Color.FromArgb(38, 128, 128, 128));
            if (_transparentBrush == null)
                _transparentBrush = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0));

            BubbleAlignment = IsOwn ? HorizontalAlignment.Right : HorizontalAlignment.Left;
            BubbleBrush = IsOwn ? _ownMessageBrush : _otherMessageBrush;
            AuthorBrush = _transparentBrush;
            TimeVisibility = String.IsNullOrWhiteSpace(TimeText) ? Visibility.Collapsed : Visibility.Visible;
        }

        private static Brush CreateDarkAccentBrush()
        {
            Color accent = Color.FromArgb(255, 0, 120, 215);
            try
            {
                SolidColorBrush resourceBrush = Application.Current.Resources["SystemControlHighlightAccentBrush"] as SolidColorBrush;
                if (resourceBrush != null)
                    accent = resourceBrush.Color;
            }
            catch
            {
            }

            const double factor = 0.84;
            byte red = (byte)Math.Max(0, Math.Min(255, accent.R * factor));
            byte green = (byte)Math.Max(0, Math.Min(255, accent.G * factor));
            byte blue = (byte)Math.Max(0, Math.Min(255, accent.B * factor));
            return new SolidColorBrush(Color.FromArgb(230, red, green, blue));
        }
    }

    public sealed class QmsMessageImage
    {
        public string Url { get; set; }
    }

    public sealed class QmsThreadNavigationArgs
    {
        public string ContactId { get; set; }
        public string ContactNick { get; set; }
        public string ThreadId { get; set; }
        public string ThreadTitle { get; set; }
        public string AvatarUrl { get; set; }
        public BitmapImage AvatarImage { get; set; }
    }
}
