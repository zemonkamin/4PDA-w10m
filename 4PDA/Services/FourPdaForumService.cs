using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using _4PDA.Models;
using Windows.Storage;
using Windows.Storage.Streams;
using Windows.Web.Http;
using Windows.Web.Http.Filters;

namespace _4PDA.Services
{
    public sealed class FourPdaForumService
    {
        private const string RootUrl = "https://4pda.to/";
        private const string ForumRootUrl = "https://4pda.to/forum";
        private const string ForumUrl = "https://4pda.to/forum/index.php";

        // Поиск 4PDA может установить промежуточную cookie и перенаправить запрос
        // на URL с searchid. Одноразовый HttpClient с CookieUsageBehavior.NoCookies
        // терял эту cookie между ответами, поэтому поиск завершался HTTP-ошибкой.
        private static readonly HttpBaseProtocolFilter ForumSearchFilter = new HttpBaseProtocolFilter();
        private static readonly HttpClient ForumSearchClient = CreateForumSearchClient(ForumSearchFilter);
        private static readonly HttpBaseProtocolFilter AnonymousForumSearchFilter = new HttpBaseProtocolFilter();
        private static readonly HttpClient AnonymousForumSearchClient = CreateForumSearchClient(AnonymousForumSearchFilter);

        public async Task<ForumItem> GetForumsAsync()
        {
            return await OfficialApiService.GetForumsAsync();
        }

        private async Task<ForumItem> GetForumsFromHtmlAsync()
        {
            try
            {
                string searchHtml = await LoadStringAsync(ForumUrl + "?act=search");
                ForumItem searchRoot = ParseSearchForums(searchHtml);
                if (searchRoot.HasChildren)
                {
                    return searchRoot;
                }
            }
            catch (FourPdaChallengeException)
            {
                throw;
            }
            catch
            {
            }

            string indexHtml = await LoadStringAsync(ForumUrl + "?act=idx");
            return ParseIndexForums(indexHtml);
        }

        public async Task<ForumTopicsData> SearchTopicsAsync(string query, int start)
        {
            return await OfficialApiService.SearchTopicsAsync(query, start);
        }

        private async Task<ForumTopicsData> SearchTopicsFromHtmlAsync(string query, int start)
        {
            string normalizedQuery = (query ?? string.Empty).Trim();
            ForumTopicsData empty = new ForumTopicsData();
            empty.PageSize = 30;
            if (normalizedQuery.Length < 3)
                return empty;

            int normalizedStart = Math.Max(0, start);
            List<string> urls = new List<string>();

            // Сначала повторяем URL из приложенного рабочего WP-клиента. Важно:
            // здесь нет noform/exclude_trash — текущая форма 4PDA сама выполняет
            // поиск и после этого может перенаправить на searchid.
            urls.Add(BuildClassicForumSearchUrl(normalizedQuery, normalizedStart));

            // Затем точный вариант ForPDA: query в windows-1251 и короткая форма.
            urls.Add(BuildForPdaForumSearchUrl(normalizedQuery, normalizedStart));

            // Минимальный современный вариант без лишних полей старой формы.
            urls.Add(BuildMinimalForumSearchUrl(normalizedQuery, normalizedStart));

            ForumTopicsData bestResult = null;
            Exception lastError = null;

            for (int i = 0; i < urls.Count; i++)
            {
                string html;
                try
                {
                    html = await LoadForumSearchHtmlAsync(urls[i]);
                }
                catch (FourPdaChallengeException) { throw; }
                catch (Exception ex)
                {
                    lastError = ex;
                    continue;
                }

                ForumTopicsData data = ParseSearchTopicsPage(html, normalizedStart);
                if (data.Items.Count > 0)
                    return data;

                if (bestResult == null)
                    bestResult = data;
            }

            if (bestResult != null)
                return bestResult;

            if (lastError != null)
                throw lastError;

            return empty;
        }

        private static HttpClient CreateForumSearchClient(HttpBaseProtocolFilter filter)
        {
            HttpClient client = new HttpClient(filter);
            ForumCookieHelper.ApplyDefaultHeaders(client);
            try { client.DefaultRequestHeaders.Accept.ParseAdd("text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8"); } catch { }
            return client;
        }

        private static string BuildClassicForumSearchUrl(string query, int start)
        {
            string encoded = Uri.EscapeDataString(query ?? string.Empty);
            string url = ForumUrl
                + "?act=search"
                + "&query=" + encoded
                + "&keywords=" + encoded
                + "&type=forums"
                + "&source=titles"
                + "&search_in=titles"
                + "&sort=date_desc"
                + "&result=topics"
                + "&result_type=topics"
                + "&forums%5B%5D=all"
                + "&subforums=1";

            if (start > 0)
                url += "&st=" + Math.Max(0, start);

            return url;
        }

        private static string BuildForPdaForumSearchUrl(string query, int start)
        {
            return ForumRootUrl
                + "?act=search"
                + "&result=topics"
                + "&sort=dd"
                + "&source=top"
                + "&query=" + UrlEncodeWindows1251(query)
                + "&subforums=1"
                + "&noform=1"
                + "&st=" + Math.Max(0, start)
                + "&exclude_trash=0";
        }

        private static string BuildMinimalForumSearchUrl(string query, int start)
        {
            return ForumUrl
                + "?act=search"
                + "&query=" + Uri.EscapeDataString(query ?? string.Empty)
                + "&result=topics"
                + "&source=top"
                + "&sort=dd"
                + "&subforums=1"
                + "&st=" + Math.Max(0, start);
        }

        private async Task<string> LoadForumSearchHtmlAsync(string url)
        {
            Exception sessionError = null;

            SeedForumSearchCookies();
            try
            {
                return await SendForumSearchRequestAsync(ForumSearchClient, url);
            }
            catch (FourPdaChallengeException) { throw; }
            catch (Exception ex)
            {
                sessionError = ex;
            }

            // Некоторые ответы 4PDA сначала требуют посещения индекса/формы поиска,
            // чтобы установить служебную cookie перед редиректом на searchid.
            await WarmUpForumSearchClientAsync(ForumSearchClient);
            try
            {
                return await SendForumSearchRequestAsync(ForumSearchClient, url);
            }
            catch (Exception ex)
            {
                sessionError = ex;
            }

            // Если сохранённая сессия устарела, выполняем тот же сценарий в чистом
            // cookie-контейнере. Cookie всё равно сохраняются между редиректами.
            try
            {
                return await SendForumSearchRequestAsync(AnonymousForumSearchClient, url);
            }
            catch
            {
            }

            await WarmUpForumSearchClientAsync(AnonymousForumSearchClient);
            try
            {
                return await SendForumSearchRequestAsync(AnonymousForumSearchClient, url);
            }
            catch
            {
                if (sessionError != null)
                    throw sessionError;
                throw;
            }
        }

        private static void SeedForumSearchCookies()
        {
            Dictionary<string, string> cookies = ForumCookieHelper.GetKnownCookies();
            foreach (KeyValuePair<string, string> pair in cookies)
            {
                if (string.IsNullOrWhiteSpace(pair.Key) || string.IsNullOrWhiteSpace(pair.Value))
                    continue;

                try
                {
                    HttpCookie cookie = new HttpCookie(pair.Key, "4pda.to", "/");
                    cookie.Value = pair.Value;
                    ForumSearchFilter.CookieManager.SetCookie(cookie);
                }
                catch
                {
                }
            }
        }

        private async Task WarmUpForumSearchClientAsync(HttpClient client)
        {
            string[] urls = new string[]
            {
                ForumUrl + "?act=idx",
                ForumUrl + "?act=search"
            };

            for (int i = 0; i < urls.Length; i++)
            {
                try
                {
                    await SendForumSearchRequestAsync(client, urls[i]);
                }
                catch
                {
                }
            }
        }

        private async Task<string> SendForumSearchRequestAsync(HttpClient client, string url)
        {
            Uri requestUri = new Uri(url, UriKind.Absolute);
            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, requestUri);
            ForumCookieHelper.ApplyDefaultHeaders(request);
            ForumCookieHelper.TryAppendHeader(request, "Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
            ForumCookieHelper.TryAppendHeader(request, "Referer", ForumUrl + "?act=search");

            HttpResponseMessage response = await client.SendRequestAsync(request);
            string html = await FourPdaResponseHelper.ReadHtmlAsync(response);
            if (string.IsNullOrWhiteSpace(html))
                throw new Exception("4PDA search returned an empty response.");

            return html;
        }

        public async Task<ForumTopicsData> GetTopicsAsync(int forumId, int start)
        {
            return await OfficialApiService.GetTopicsAsync(forumId, start);
        }

        private async Task<ForumTopicsData> GetTopicsFromHtmlAsync(int forumId, int start)
        {
            string url = ForumUrl + "?showforum=" + forumId + "&st=" + Math.Max(0, start);
            ForumTopicsData authenticatedData = null;
            Exception authenticatedError = null;

            try
            {
                string authenticatedHtml = await LoadStringAsync(url);
                authenticatedData = ParseTopicsPage(authenticatedHtml, forumId);

                // После входа 4PDA иногда отдаёт showforum в другой оболочке.
                // Если привычная разметка успешно разобрана, сохраняем авторизованный
                // результат — это важно для закрытых разделов.
                if (authenticatedData != null && authenticatedData.Items.Count > 0)
                {
                    return authenticatedData;
                }
            }
            catch (Exception ex)
            {
                authenticatedError = ex;
            }

            try
            {
                // Обычные списки тем публичны. Анонимный fallback возвращает ту же
                // стабильную HTML-разметку, что была до входа, и не меняет cookies
                // основной авторизованной сессии.
                string anonymousHtml = await LoadStringAsync(url, true);
                ForumTopicsData anonymousData = ParseTopicsPage(anonymousHtml, forumId);
                if (anonymousData != null && anonymousData.Items.Count > 0)
                {
                    return anonymousData;
                }

                if (authenticatedData != null)
                {
                    return authenticatedData;
                }

                return anonymousData;
            }
            catch
            {
                if (authenticatedError != null)
                {
                    throw authenticatedError;
                }

                throw;
            }
        }

        public async Task<ForumTopicsData> GetFavoriteForumsAsync()
        {
            return await OfficialApiService.GetFavoritesAsync();
        }

        private async Task<ForumTopicsData> GetFavoriteForumsFromHtmlAsync()
        {
            if (!ForumAuthService.IsAuthorized)
                throw new InvalidOperationException("Для просмотра избранных форумов необходимо войти в аккаунт.");

            ForumTopicsData result = new ForumTopicsData();
            result.ForumId = AppNavigationActions.FavoriteForumsId;
            result.Title = "Избранные форумы";
            result.CurrentPage = 1;
            result.TotalPages = 1;

            HashSet<int> loadedStarts = new HashSet<int>();
            HashSet<string> loadedFavoriteItems = new HashSet<string>();
            Queue<int> pendingStarts = new Queue<int>();
            pendingStarts.Enqueue(0);

            int guard = 0;
            while (pendingStarts.Count > 0 && guard++ < 50)
            {
                int start = Math.Max(0, pendingStarts.Dequeue());
                if (!loadedStarts.Add(start))
                    continue;

                // Страница списка избранного открывается через /forum, как в ForPDA.
                // index.php оставляем fallback-адресом: он нужен старым шаблонам и
                // некоторым сессиям, в которых канонический адрес отвечает редиректом.
                ForumCookieHelper.SaveForumCookiesFromSystemCookieManager("GetFavoriteForumsAsync");
                string html = await LoadFavoritePageWithFallbackAsync(start);
                if (IsForumLoginForm(html))
                    throw new InvalidOperationException("Для просмотра избранного необходимо войти в аккаунт.");

                ForumTopicsData page = ParseFavoriteForumsPage(html);
                if (page.PageSize > 0)
                    result.PageSize = page.PageSize;

                for (int i = 0; i < page.Items.Count; i++)
                {
                    ForumTopicItem item = page.Items[i];
                    if (item == null || item.Id <= 0)
                        continue;

                    string key = (item.IsForum ? "forum:" : "topic:") + item.Id.ToString();
                    if (loadedFavoriteItems.Add(key))
                        result.Items.Add(item);
                }

                MatchCollection pageStarts = Regex.Matches(html,
                    @"href\s*=\s*[""'][^""']*?act=fav[^""']*?(?:&amp;|&)st=(\d+)[^""']*[""']",
                    RegexOptions.IgnoreCase);
                for (int i = 0; i < pageStarts.Count; i++)
                {
                    int foundStart = ParseInt(pageStarts[i].Groups[1].Value);
                    if (foundStart >= 0 && !loadedStarts.Contains(foundStart))
                        pendingStarts.Enqueue(foundStart);
                }

                if (page.HasMore)
                {
                    int nextStart = start + Math.Max(1, page.PageSize);
                    if (!loadedStarts.Contains(nextStart))
                        pendingStarts.Enqueue(nextStart);
                }
            }

            result.Items.Sort(delegate(ForumTopicItem left, ForumTopicItem right)
            {
                return string.Compare(left == null ? string.Empty : left.Title,
                    right == null ? string.Empty : right.Title,
                    StringComparison.CurrentCultureIgnoreCase);
            });
            return result;
        }

        private async Task<string> LoadFavoritePageWithFallbackAsync(int start)
        {
            string query = "?act=fav&type=all&st=" + Math.Max(0, start)
                + "&sort_key=&sort_by=";
            string canonicalHtml = null;
            Exception canonicalError = null;

            try
            {
                canonicalHtml = await LoadFavoritePageAsync(AddCacheBuster(ForumRootUrl + query));
                if (ContainsFavoriteItems(canonicalHtml))
                    return canonicalHtml;
            }
            catch (Exception ex)
            {
                canonicalError = ex;
            }

            string legacyHtml = null;
            try
            {
                legacyHtml = await LoadFavoritePageAsync(AddCacheBuster(ForumUrl + query));
                if (ContainsFavoriteItems(legacyHtml))
                    return legacyHtml;
            }
            catch
            {
                if (canonicalError != null)
                    throw canonicalError;
            }

            // Пустой список тоже является корректным ответом. Предпочитаем страницу,
            // которая не является формой входа, даже если на ней нет элементов.
            if (!string.IsNullOrWhiteSpace(canonicalHtml) && !IsForumLoginForm(canonicalHtml))
                return canonicalHtml;
            if (!string.IsNullOrWhiteSpace(legacyHtml))
                return legacyHtml;
            if (canonicalError != null)
                throw canonicalError;

            return canonicalHtml ?? string.Empty;
        }

        public async Task<TopicPageData> GetTopicAsync(string url, int start)
        {
            return await OfficialApiService.GetTopicAsync(url, start);
        }

        public async Task<int> SendTopicMessageAsync(MessagePageParameters parameters, string message, IList<StorageFile> attachments)
        {
            return await OfficialApiService.SendTopicMessageAsync(parameters, message, attachments);
        }

        private async Task<int> SendTopicMessageWithHtmlAsync(MessagePageParameters parameters, string message, IList<StorageFile> attachments)
        {
            if (!ForumAuthService.IsAuthorized)
                throw new InvalidOperationException("Для отправки сообщения необходимо войти в аккаунт.");

            if (parameters == null || parameters.TopicId <= 0)
                throw new ArgumentException("Не указана тема для ответа.");

            string cleanMessage = (message ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(cleanMessage))
                throw new ArgumentException("Введите текст сообщения.");

            // Ключ формы может измениться между открытием TopicPage и нажатием
            // «Отправить». Перед отправкой пробуем получить свежий ключ, но при
            // временной ошибке чтения темы сохраняем уже полученный ранее.
            string authKey = parameters.AuthKey;
            try
            {
                ForumCookieHelper.SaveForumCookiesFromSystemCookieManager("MessagePage.BeforeRefreshReplyForm");
                TopicPageData refreshed = await GetTopicAsync(parameters.TopicUrl, Math.Max(0, parameters.Start));
                if (refreshed != null)
                {
                    if (!string.IsNullOrWhiteSpace(refreshed.AuthKey))
                    {
                        authKey = refreshed.AuthKey;
                        parameters.AuthKey = refreshed.AuthKey;
                    }

                    if (refreshed.ForumId > 0)
                        parameters.ForumId = refreshed.ForumId;
                    if (refreshed.IsReplyForbidden)
                        parameters.CanReply = false;
                    else if (refreshed.CanReply)
                        parameters.CanReply = true;
                }
            }
            catch
            {
                // Ошибка обновления страницы не означает выход из аккаунта: POST
                // ещё может успешно пройти с ключом, полученным при открытии темы.
            }

            if (!parameters.CanReply || string.IsNullOrWhiteSpace(authKey))
                throw new InvalidOperationException("Не удалось получить ключ формы ответа. Обновите тему и попробуйте снова.");

            HttpBaseProtocolFilter filter = new HttpBaseProtocolFilter();
            filter.CookieUsageBehavior = HttpCookieUsageBehavior.NoCookies;
            HttpClient client = new HttpClient(filter);
            var openedStreams = new List<IDisposable>();
            int postedPostId = 0;

            try
            {
                ForumCookieHelper.SaveForumCookiesFromSystemCookieManager("MessagePage.BeforeSend");

                Uri requestUri = new Uri(ForumUrl, UriKind.Absolute);
                HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, requestUri);
                ForumCookieHelper.ApplyDefaultHeaders(request);
                string topicReferer = string.IsNullOrWhiteSpace(parameters.TopicUrl)
                    ? ForumUrl + "?showtopic=" + parameters.TopicId + "&st=" + Math.Max(0, parameters.Start)
                    : BuildTopicPageUrl(parameters.TopicUrl, parameters.Start);
                ForumCookieHelper.TryAppendHeader(request, "Referer", topicReferer);
                AppendForumCookieHeader(request, requestUri);

                List<KeyValuePair<string, string>> values = BuildTopicMessageFields(parameters, authKey, cleanMessage);
                bool hasAttachments = attachments != null && attachments.Count > 0;
                if (!hasAttachments)
                {
                    request.Content = new HttpFormUrlEncodedContent(values);
                }
                else
                {
                    HttpMultipartFormDataContent multipart = new HttpMultipartFormDataContent();
                    for (int i = 0; i < values.Count; i++)
                    {
                        KeyValuePair<string, string> pair = values[i];
                        multipart.Add(new HttpStringContent(pair.Value ?? string.Empty), pair.Key);
                    }

                    for (int i = 0; i < attachments.Count; i++)
                    {
                        StorageFile file = attachments[i];
                        if (file == null)
                            continue;

                        IRandomAccessStreamWithContentType stream = await file.OpenReadAsync();
                        IInputStream inputStream = stream.GetInputStreamAt(0);
                        openedStreams.Add(inputStream);
                        openedStreams.Add(stream);
                        HttpStreamContent fileContent = new HttpStreamContent(inputStream);
                        multipart.Add(fileContent, "FILE_UPLOAD[]", file.Name);
                    }

                    request.Content = multipart;
                }

                HttpResponseMessage response = await client.SendRequestAsync(request);
                string responseBody = await response.Content.ReadAsStringAsync();
                if (!response.IsSuccessStatusCode)
                    throw new Exception("HTTP " + (int)response.StatusCode + " " + response.ReasonPhrase);

                ForumCookieHelper.SaveForumCookiesFromSystemCookieManager("MessagePage.AfterSend");

                string error = ExtractForumPostError(responseBody);
                if (!string.IsNullOrWhiteSpace(error))
                    throw new Exception(error);

                // act=auth встречается в обычных ссылках и меню даже при живой
                // сессии. Истёкшую сессию определяем только по настоящей форме
                // входа, где одновременно присутствуют поля логина и пароля.
                if (IsForumLoginForm(responseBody))
                    throw new InvalidOperationException("Сервер вернул форму входа. Войдите в аккаунт повторно.");

                postedPostId = ExtractPostedPostId(response, responseBody);
            }
            finally
            {
                for (int i = 0; i < openedStreams.Count; i++)
                    openedStreams[i].Dispose();
                client.Dispose();
                filter.Dispose();
            }

            return postedPostId;
        }

        private int ExtractPostedPostId(HttpResponseMessage response, string html)
        {
            string finalUrl = string.Empty;
            try
            {
                if (response != null && response.RequestMessage != null && response.RequestMessage.RequestUri != null)
                    finalUrl = response.RequestMessage.RequestUri.AbsoluteUri;
            }
            catch
            {
            }

            int postId = ExtractQueryInt(finalUrl, "p");
            if (postId <= 0)
                postId = ExtractQueryInt(finalUrl, "pid");
            if (postId > 0)
                return postId;

            Match fragment = Regex.Match(finalUrl ?? string.Empty, @"#(?:entry|post)(\d+)", RegexOptions.IgnoreCase);
            if (fragment.Success)
                return ParseInt(fragment.Groups[1].Value);

            int newest = 0;
            if (!string.IsNullOrWhiteSpace(html))
            {
                MatchCollection directLinks = Regex.Matches(html,
                    @"(?:act=findpost&amp;pid=|act=findpost&pid=|[?&](?:p|pid)=|#(?:entry|post))(\d+)",
                    RegexOptions.IgnoreCase);
                for (int i = 0; i < directLinks.Count; i++)
                    newest = Math.Max(newest, ParseInt(directLinks[i].Groups[1].Value));

                MatchCollection postContainers = Regex.Matches(html,
                    @"(?:id|name)\s*=\s*[""'](?:entry|post[-_]?)(\d+)[""']",
                    RegexOptions.IgnoreCase);
                for (int i = 0; i < postContainers.Count; i++)
                    newest = Math.Max(newest, ParseInt(postContainers[i].Groups[1].Value));
            }

            return newest;
        }

        private string AddCacheBuster(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return url;

            string separator = url.IndexOf('?') >= 0 ? "&" : "?";
            return url + separator + "_uwpFresh=" + DateTime.UtcNow.Ticks.ToString();
        }

        private List<KeyValuePair<string, string>> BuildTopicMessageFields(MessagePageParameters parameters, string authKey, string message)
        {
            var values = new List<KeyValuePair<string, string>>();
            values.Add(new KeyValuePair<string, string>("act", "Post"));
            values.Add(new KeyValuePair<string, string>("CODE", "03"));
            values.Add(new KeyValuePair<string, string>("f", Math.Max(0, parameters.ForumId).ToString()));
            values.Add(new KeyValuePair<string, string>("t", parameters.TopicId.ToString()));
            values.Add(new KeyValuePair<string, string>("auth_key", authKey ?? string.Empty));
            values.Add(new KeyValuePair<string, string>("Post", message ?? string.Empty));
            values.Add(new KeyValuePair<string, string>("enablesig", "yes"));
            values.Add(new KeyValuePair<string, string>("enableemo", "yes"));
            values.Add(new KeyValuePair<string, string>("st", Math.Max(0, parameters.Start).ToString()));
            values.Add(new KeyValuePair<string, string>("removeattachid", "0"));
            values.Add(new KeyValuePair<string, string>("MAX_FILE_SIZE", "0"));
            values.Add(new KeyValuePair<string, string>("parent_id", "0"));
            values.Add(new KeyValuePair<string, string>("ed-0_wysiwyg_used", "0"));
            values.Add(new KeyValuePair<string, string>("editor_ids[]", "ed-0"));
            values.Add(new KeyValuePair<string, string>("iconid", "0"));
            values.Add(new KeyValuePair<string, string>("_upload_single_file", "1"));
            values.Add(new KeyValuePair<string, string>("file-list", string.Empty));
            return values;
        }

        private string ExtractForumPostError(string html)
        {
            if (string.IsNullOrWhiteSpace(html))
                return string.Empty;

            string error = CleanText(FirstMatch(html,
                "<div[^>]*?class=[\\\"'][^\\\"']*(?:errorwrap|error|message)[^\\\"']*[\\\"'][^>]*?>([\\s\\S]*?)</div>", 1));
            if (!string.IsNullOrWhiteSpace(error)
                && (error.IndexOf("ошиб", StringComparison.OrdinalIgnoreCase) >= 0
                    || error.IndexOf("нельзя", StringComparison.OrdinalIgnoreCase) >= 0
                    || error.IndexOf("запрещ", StringComparison.OrdinalIgnoreCase) >= 0
                    || error.IndexOf("необходимо", StringComparison.OrdinalIgnoreCase) >= 0))
            {
                return error;
            }

            return string.Empty;
        }

        private bool IsForumLoginForm(string html)
        {
            if (string.IsNullOrWhiteSpace(html))
                return false;

            MatchCollection forms = Regex.Matches(html, @"<form\b[\s\S]*?</form>", RegexOptions.IgnoreCase);
            for (int i = 0; i < forms.Count; i++)
            {
                string form = forms[i].Value;
                bool hasLogin = Regex.IsMatch(form,
                    @"name\s*=\s*[""'](?:login|username|UserName)[""']",
                    RegexOptions.IgnoreCase);
                bool hasPassword = Regex.IsMatch(form,
                    @"(?:type\s*=\s*[""']password[""']|name\s*=\s*[""'](?:password|passwd|PassWord)[""'])",
                    RegexOptions.IgnoreCase);
                bool isAuthForm = Regex.IsMatch(form,
                    @"(?:action\s*=\s*[""'][^""']*act=auth|Вход\s+на\s+форум)",
                    RegexOptions.IgnoreCase);

                if (hasLogin && hasPassword && isAuthForm)
                    return true;
            }

            return false;
        }

        public async Task<string> VotePostAsync(int postId, bool positive)
        {
            return await OfficialApiService.VotePostAsync(postId, positive);
        }

        private async Task<string> VotePostWithHtmlAsync(int postId, bool positive)
        {
            if (postId <= 0)
                throw new ArgumentOutOfRangeException("postId");

            string body = await LoadStringAsync("https://4pda.to/forum/zka.php?i=" + postId + "&v=" + (positive ? "1" : "-1"));
            string trimmed = (body ?? string.Empty).Trim();
            Match result = Regex.Match(trimmed, "ok:\\s*((?:\\+|\\-)?\\d+)", RegexOptions.IgnoreCase);
            if (result.Success)
            {
                int code = ParseInt(result.Groups[1].Value);
                if (code == 1) return "Репутация сообщения повышена";
                if (code == -1) return "Репутация сообщения понижена";
                if (code == 0) return "Вы уже голосовали за это сообщение";
            }

            if (string.Equals(trimmed, "evote", StringComparison.OrdinalIgnoreCase))
                return "Вы уже голосовали за это сообщение";

            throw new Exception("Не удалось изменить репутацию сообщения");
        }

        public async Task ReportPostAsync(int topicId, int postId, string message)
        {
            await OfficialApiService.ReportPostAsync(postId, message);
        }

        private async Task ReportPostWithHtmlAsync(int topicId, int postId, string message)
        {
            if (topicId <= 0 || postId <= 0)
                throw new ArgumentException("Не указан пост для жалобы");

            HttpBaseProtocolFilter filter = new HttpBaseProtocolFilter();
            filter.CookieUsageBehavior = HttpCookieUsageBehavior.NoCookies;
            HttpClient client = new HttpClient(filter);
            try
            {
                Uri requestUri = new Uri(ForumUrl + "?act=report&send=1&t=" + topicId + "&p=" + postId, UriKind.Absolute);
                HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, requestUri);
                ForumCookieHelper.ApplyDefaultHeaders(request);
                ForumCookieHelper.TryAppendHeader(request, "Referer", ForumUrl + "?showtopic=" + topicId);
                AppendForumCookieHeader(request, requestUri);
                request.Content = new HttpStringContent(
                    "message=" + Uri.EscapeDataString(message ?? string.Empty),
                    Windows.Storage.Streams.UnicodeEncoding.Utf8,
                    "application/x-www-form-urlencoded");

                HttpResponseMessage response = await client.SendRequestAsync(request);
                string responseBody = await response.Content.ReadAsStringAsync();
                if (!response.IsSuccessStatusCode)
                    throw new Exception("HTTP " + (int)response.StatusCode);

                Match error = Regex.Match(responseBody ?? string.Empty, "<div[^>]*?class=[\\\"']errorwrap[\\\"'][^>]*?>[\\s\\S]*?<p>([\\s\\S]*?)</p>", RegexOptions.IgnoreCase);
                if (error.Success)
                    throw new Exception(CleanText(error.Groups[1].Value));
            }
            finally
            {
                client.Dispose();
                filter.Dispose();
            }
        }

        public async Task ExecutePostActionAsync(string url)
        {
            string normalized = NormalizeActionUrl(url);
            if (string.IsNullOrWhiteSpace(normalized))
                throw new ArgumentException("Ссылка действия отсутствует");
            await LoadStringAsync(normalized);
        }

        private async Task<string> LoadFavoritePageAsync(string url)
        {
            string manualCookieHtml = null;
            Exception manualCookieError = null;

            try
            {
                manualCookieHtml = await LoadStringAsync(url, false);
                if (ContainsFavoriteItems(manualCookieHtml))
                    return manualCookieHtml;
            }
            catch (Exception ex)
            {
                manualCookieError = ex;
            }

            // Fallback на системное cookie-хранилище того же UWP HTTP stack,
            // которым выполнялся вход. Это покрывает cookies, которые не попали
            // в список известных имён, но необходимы странице избранного.
            try
            {
                HttpBaseProtocolFilter filter = new HttpBaseProtocolFilter();
                HttpClient client = new HttpClient(filter);
                try
                {
                    Uri requestUri = new Uri(url, UriKind.Absolute);
                    HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, requestUri);
                    ForumCookieHelper.ApplyDefaultHeaders(request);
                    ForumCookieHelper.TryAppendHeader(request, "Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
                    ForumCookieHelper.TryAppendHeader(request, "Referer", ForumRootUrl + "?act=fav&type=all");
                    ForumCookieHelper.TryAppendHeader(request, "Cache-Control", "no-cache, no-store, max-age=0");
                    HttpResponseMessage response = await client.SendRequestAsync(request);
                    string systemCookieHtml = await FourPdaResponseHelper.ReadHtmlAsync(response);
                    if (!string.IsNullOrWhiteSpace(systemCookieHtml))
                        return systemCookieHtml;
                }
                finally
                {
                    client.Dispose();
                    filter.Dispose();
                }
            }
            catch
            {
                if (manualCookieError != null)
                    throw manualCookieError;
            }

            return manualCookieHtml ?? string.Empty;
        }

        private bool ContainsFavoriteItems(string html)
        {
            if (string.IsNullOrWhiteSpace(html))
                return false;

            // Старый шаблон 4PDA/ForPDA и варианты нового мобильного шаблона.
            if (Regex.IsMatch(html,
                @"\b(?:data-item-fid|data-fav-id|data-favid)\s*=",
                RegexOptions.IgnoreCase))
                return true;

            // На части шаблонов избранное отдаётся обычными строками списка тем
            // и форумов, без отдельного data-item-fid.
            return Regex.IsMatch(html,
                    @"<div\b[^>]*\bdata-topic\s*=",
                    RegexOptions.IgnoreCase)
                || Regex.IsMatch(html,
                    @"<div\b[^>]*\bclass\s*=\s*[""'][^""']*\bboard_forum_row\b",
                    RegexOptions.IgnoreCase);
        }

        private Task<string> LoadStringAsync(string url)
        {
            return LoadStringAsync(url, false);
        }

        private async Task<string> LoadStringAsync(string url, bool withoutCookies)
        {
            HttpBaseProtocolFilter filter = new HttpBaseProtocolFilter();
            filter.CookieUsageBehavior = HttpCookieUsageBehavior.NoCookies;
            HttpClient client = new HttpClient(filter);

            try
            {
                Uri requestUri = new Uri(url, UriKind.Absolute);
                HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, requestUri);

                // Авторизация выполняется клиентом ForumAuthService с IE/WP User-Agent.
                // cf_clearance и часть cookies 4PDA привязаны к User-Agent, поэтому
                // все форумные запросы должны повторять те же заголовки и явно
                // использовать сохранённые cookies.
                ForumCookieHelper.ApplyDefaultHeaders(request);
                ForumCookieHelper.TryAppendHeader(request, "Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
                ForumCookieHelper.TryAppendHeader(request, "Referer", RootUrl);
                ForumCookieHelper.TryAppendHeader(request, "Cache-Control", "no-cache, no-store, max-age=0");
                ForumCookieHelper.TryAppendHeader(request, "Pragma", "no-cache");

                ForumCookieHelper.SaveForumCookiesFromSystemCookieManager("FourPdaForumService.LoadStringAsync");
                string cookieHeader = ForumCookieHelper.GetCookieHeaderForUrl(requestUri, !withoutCookies);
                ForumCookieHelper.TryAppendHeader(request, "Cookie", cookieHeader);

                using (HttpResponseMessage response = await client.SendRequestAsync(request))
                {
                    return await FourPdaResponseHelper.ReadHtmlAsync(response);
                }
            }
            finally
            {
                client.Dispose();
                filter.Dispose();
            }
        }

        private static void AppendForumCookieHeader(HttpRequestMessage request, Uri requestUri)
        {
            if (request == null || requestUri == null)
                return;

            string cookieHeader = ForumCookieHelper.GetCookieHeaderForUrl(requestUri);
            if (!string.IsNullOrWhiteSpace(cookieHeader))
            {
                ForumCookieHelper.TryAppendHeader(request, "Cookie", cookieHeader);
            }
        }

        private ForumItem ParseSearchForums(string html)
        {
            ForumItem root = CreateRoot();
            string select = FirstMatch(html, "<select[^>]*?name=[\"']forums(?:\\[\\])?[\"'][^>]*?>([\\s\\S]*?)</select>", 1);
            if (string.IsNullOrWhiteSpace(select))
            {
                return root;
            }

            List<ForumItem> parents = new List<ForumItem>();
            ForumItem lastParent = root;
            parents.Add(lastParent);

            Regex itemRegex = new Regex("<option[^>]*?value=[\"'](\\d+)['\"][^>]*?>[^-\\s]*?(-*?)\\s*([\\s\\S]*?)</option>", RegexOptions.IgnoreCase);
            foreach (Match match in itemRegex.Matches(select))
            {
                ForumItem item = new ForumItem();
                item.Id = ParseInt(match.Groups[1].Value);
                item.Level = match.Groups[2].Value.Length / 2;
                item.Title = CleanText(match.Groups[3].Value);

                if (item.Id <= 0 || string.IsNullOrWhiteSpace(item.Title))
                {
                    continue;
                }

                if (item.Level <= lastParent.Level)
                {
                    int removeCount = lastParent.Level - item.Level + 1;
                    for (int i = 0; i < removeCount && parents.Count > 1; i++)
                    {
                        parents.RemoveAt(parents.Count - 1);
                    }

                    lastParent = parents[parents.Count - 1];
                }

                item.ParentId = lastParent.Id;
                lastParent.Children.Add(item);

                if (item.Level > lastParent.Level)
                {
                    lastParent = item;
                    parents.Add(lastParent);
                }
            }

            return root;
        }

        private ForumItem ParseIndexForums(string html)
        {
            ForumItem root = CreateRoot();
            Regex categoryRegex = new Regex("<div id=\"fo_(\\d+)\"[\\s\\S]*?<div class=\"cat_name\"[\\s\\S]*?<a[^>]*?showforum=\\d+[^>]*?>([\\s\\S]*?)</a></div>([\\s\\S]*?)(?=<div id=\"fc_\\d+\"|</div></div></[^>]+>|$)", RegexOptions.IgnoreCase);
            foreach (Match categoryMatch in categoryRegex.Matches(html))
            {
                ForumItem category = new ForumItem();
                category.Id = ParseInt(categoryMatch.Groups[1].Value);
                category.ParentId = root.Id;
                category.Level = 0;
                category.Title = CleanText(categoryMatch.Groups[2].Value);
                if (category.Id <= 0 || string.IsNullOrWhiteSpace(category.Title))
                {
                    continue;
                }

                ParseIndexForumRows(categoryMatch.Groups[3].Value, category);
                root.Children.Add(category);
            }

            if (!root.HasChildren)
            {
                ParseIndexForumRows(html, root);
            }

            return root;
        }

        private void ParseIndexForumRows(string html, ForumItem parent)
        {
            Regex rowRegex = new Regex("<div class=\"board_forum_row\"[\\s\\S]*?<div class=\"board_forum_name\"[\\s\\S]*?<a[^>]*?showforum=(\\d+)[^>]*?>([\\s\\S]*?)</a></div>([\\s\\S]*?)(?=<div class=\"board_forum_row\"|</div><div id=\"fc_|$)", RegexOptions.IgnoreCase);
            foreach (Match rowMatch in rowRegex.Matches(html))
            {
                ForumItem item = new ForumItem();
                item.Id = ParseInt(rowMatch.Groups[1].Value);
                item.ParentId = parent.Id;
                item.Level = parent.Level + 1;
                item.Title = CleanText(rowMatch.Groups[2].Value);

                string rest = rowMatch.Groups[3].Value;
                item.LastTopicTitle = CleanText(FirstMatch(rest, "board-forum-lastpost[\\s\\S]*?<a[^>]*?title=\"[^\"]*?\">[^<]*?</a>\\s*<a[^>]*?>([\\s\\S]*?)</a>", 1));
                item.LastPostInfo = CleanText(FirstMatch(rest, "<div class=\"board_forum_date\">([\\s\\S]*?)</div>", 1));

                if (item.Id > 0 && !string.IsNullOrWhiteSpace(item.Title))
                {
                    parent.Children.Add(item);
                }
            }
        }

        private ForumTopicsData ParseFavoriteForumsPage(string html)
        {
            ForumTopicsData data = new ForumTopicsData();
            data.ForumId = AppNavigationActions.FavoriteForumsId;
            data.Title = "Избранное";
            ParseForumPagination(html, data);

            if (string.IsNullOrWhiteSpace(html))
                return data;

            // Старый шаблон 4PDA (и ForPDA) помечает строку data-item-fid.
            // Встречаются также data-fav-id/data-favid, поэтому не полагаемся
            // на одно имя атрибута, его положение или наличие data-item-track.
            MatchCollection starts = Regex.Matches(html,
                @"<(?:div|tr|li)\b[^>]*\b(?:data-item-fid|data-fav-id|data-favid)\s*=\s*(?:[""'][^""']*[""']|[^\s>]+)[^>]*>",
                RegexOptions.IgnoreCase);

            // Новый шаблон иногда отдаёт избранное как обычный список тем/форумов.
            // Используем его только при отсутствии специальных favorite-маркеров,
            // чтобы вложенный data-topic не разрезал старую строку пополам.
            if (starts.Count == 0)
            {
                starts = Regex.Matches(html,
                    @"<div\b(?=[^>]*(?:\bdata-topic\s*=|\bclass\s*=\s*[""'][^""']*\bboard_forum_row\b))[^>]*>",
                    RegexOptions.IgnoreCase);
            }

            for (int i = 0; i < starts.Count; i++)
            {
                Match itemStart = starts[i];
                int itemEnd = i + 1 < starts.Count ? starts[i + 1].Index : html.Length;
                if (itemEnd <= itemStart.Index)
                    continue;

                string block = html.Substring(itemStart.Index, itemEnd - itemStart.Index);
                string openingTag = itemStart.Value;

                Match link = Regex.Match(block,
                    @"<a\b[^>]*\bhref\s*=\s*[""']([^""']*?(?:showtopic|showforum)=\d+[^""']*)[""'][^>]*>([\s\S]*?)</a>",
                    RegexOptions.IgnoreCase);

                // Некоторые мобильные шаблоны используют ссылку без showtopic/showforum
                // в первом заголовке. Тогда ищем ссылку с числовым id, исключая профиль,
                // пагинацию и управляющие ссылки самого избранного.
                if (!link.Success)
                {
                    MatchCollection links = Regex.Matches(block,
                        @"<a\b[^>]*\bhref\s*=\s*[""']([^""']+)[""'][^>]*>([\s\S]*?)</a>",
                        RegexOptions.IgnoreCase);
                    for (int linkIndex = 0; linkIndex < links.Count; linkIndex++)
                    {
                        string candidateUrl = WebUtility.HtmlDecode(links[linkIndex].Groups[1].Value);
                        if (candidateUrl.IndexOf("showuser=", StringComparison.OrdinalIgnoreCase) >= 0
                            || candidateUrl.IndexOf("act=fav", StringComparison.OrdinalIgnoreCase) >= 0
                            || candidateUrl.IndexOf("javascript:", StringComparison.OrdinalIgnoreCase) >= 0)
                            continue;

                        if (Regex.IsMatch(candidateUrl, @"(?:showtopic|showforum)=\d+", RegexOptions.IgnoreCase))
                        {
                            link = links[linkIndex];
                            break;
                        }
                    }
                }

                if (!link.Success)
                    continue;

                string itemUrl = WebUtility.HtmlDecode(link.Groups[1].Value).Replace("&amp;", "&");
                Match idMatch = Regex.Match(itemUrl,
                    @"(?:showtopic|showforum)=(\d+)", RegexOptions.IgnoreCase);
                int itemId = idMatch.Success ? ParseInt(idMatch.Groups[1].Value) : 0;
                string title = CleanText(link.Groups[2].Value);
                if (itemId <= 0 || string.IsNullOrWhiteSpace(title))
                    continue;

                bool isForum = itemUrl.IndexOf("showforum=", StringComparison.OrdinalIgnoreCase) >= 0;
                ForumTopicItem item = new ForumTopicItem();
                item.IsForum = isForum;
                item.Id = itemId;
                item.Title = title;
                item.Url = NormalizeForumUrl(itemUrl);
                item.IsPinned = ExtractHtmlAttribute(openingTag, "data-item-pin") == "1";
                item.IsNew = link.Groups[2].Value.IndexOf("<strong", StringComparison.OrdinalIgnoreCase) >= 0;

                string modifier = CleanText(FirstMatch(block,
                    @"class\s*=\s*[""'][^""']*(?:modifier|forum_img_with_link)[^""']*[""'][^>]*>([\s\S]*?)</(?:span|a)>", 1));
                item.IsPoll = modifier.Contains("^");
                item.IsClosed = modifier.IndexOf("X", StringComparison.OrdinalIgnoreCase) >= 0
                    || modifier.IndexOf("Х", StringComparison.OrdinalIgnoreCase) >= 0;
                item.IsNew = item.IsNew || modifier.Contains("+");

                if (isForum)
                {
                    item.ForumId = itemId;
                    item.Description = CleanText(FirstMatch(block,
                        @"<span[^>]*?class\s*=\s*[""'][^""']*forumdesc[^""']*[""'][^>]*>([\s\S]*?)</span>", 1));
                }
                else
                {
                    item.ForumId = ParseInt(FirstMatch(block,
                        @"showforum=(\d+)[^""']*[""'][^>]*>", 1));
                    item.Description = CleanText(FirstMatch(block,
                        @"<span[^>]*?class\s*=\s*[""'][^""']*topic_desc[^""']*[""'][^>]*>([\s\S]*?)</span>", 1));
                }

                MatchCollection users = Regex.Matches(block,
                    @"<a[^>]*?href\s*=\s*[""'][^""']*?showuser=(\d+)[^""']*[""'][^>]*>([\s\S]*?)</a>",
                    RegexOptions.IgnoreCase);
                if (!isForum && users.Count > 0)
                    item.Author = CleanText(users[0].Groups[2].Value);
                if (users.Count > 0)
                    item.LastUser = CleanText(users[users.Count - 1].Groups[2].Value);

                item.Date = CleanText(FirstMatch(block,
                    @"<div[^>]*?class\s*=\s*[""'][^""']*(?:board_forum_date|topic_body)[^""']*[""'][^>]*>\s*([^<]*?)\s*(?:<a|</div>)", 1));

                data.Items.Add(item);
            }

            return data;
        }

        private string ExtractHtmlAttribute(string tag, string attributeName)
        {
            if (string.IsNullOrWhiteSpace(tag) || string.IsNullOrWhiteSpace(attributeName))
                return string.Empty;

            Match match = Regex.Match(tag,
                @"\b" + Regex.Escape(attributeName) + @"\s*=\s*(?:[""']([^""']*)[""']|([^\s>]+))",
                RegexOptions.IgnoreCase);
            if (!match.Success)
                return string.Empty;

            return WebUtility.HtmlDecode(match.Groups[1].Success
                ? match.Groups[1].Value
                : match.Groups[2].Value);
        }

        private void ParseSearchTopicsTableLayout(string html, ForumTopicsData data)
        {
            if (string.IsNullOrWhiteSpace(html) || data == null)
                return;

            MatchCollection links = Regex.Matches(html,
                @"<a\b[^>]*?\bid\s*=\s*[""']tid-link-(\d+)[""'][^>]*>([\s\S]*?)</a>",
                RegexOptions.IgnoreCase);

            for (int i = 0; i < links.Count; i++)
            {
                Match link = links[i];
                int topicId = ParseInt(link.Groups[1].Value);
                string title = CleanText(link.Groups[2].Value);
                if (topicId <= 0 || !IsUsableSearchTopicTitle(title))
                    continue;

                int rowStart = html.LastIndexOf("<tr", link.Index, StringComparison.OrdinalIgnoreCase);
                int rowEnd = html.IndexOf("</tr>", link.Index, StringComparison.OrdinalIgnoreCase);
                string block;
                if (rowStart >= 0 && rowEnd > rowStart && rowEnd - rowStart < 40000)
                    block = html.Substring(rowStart, rowEnd + 5 - rowStart);
                else
                {
                    int contextStart = Math.Max(0, link.Index - 1400);
                    int contextEnd = Math.Min(html.Length, link.Index + link.Length + 5000);
                    block = html.Substring(contextStart, contextEnd - contextStart);
                }

                ForumTopicItem item = new ForumTopicItem();
                item.Id = topicId;
                item.Title = title;

                string href = ExtractHtmlAttribute(link.Value, "href");
                item.Url = string.IsNullOrWhiteSpace(href)
                    ? ForumUrl + "?showtopic=" + topicId
                    : NormalizeForumUrl(href);

                Match forumMatch = Regex.Match(block,
                    @"<a[^>]*?href\s*=\s*[""'][^""']*?(?:showforum=|[?&]f=)(\d+)[^""']*[""'][^>]*>([\s\S]*?)</a>",
                    RegexOptions.IgnoreCase);
                string forumTitle = string.Empty;
                if (forumMatch.Success)
                {
                    item.ForumId = ParseInt(forumMatch.Groups[1].Value);
                    forumTitle = CleanText(forumMatch.Groups[2].Value);
                }

                MatchCollection users = Regex.Matches(block,
                    @"<a[^>]*?(?:showuser=|[?&]u=)(\d+)[^>]*>([\s\S]*?)</a>",
                    RegexOptions.IgnoreCase);
                if (users.Count > 0)
                    item.Author = CleanText(users[0].Groups[2].Value);
                if (users.Count > 1)
                    item.LastUser = CleanText(users[users.Count - 1].Groups[2].Value);

                string description = CleanText(FirstMatch(block,
                    @"<span[^>]*?class\s*=\s*[""'][^""']*topic_desc[^""']*[""'][^>]*>([\s\S]*?)</span>", 1));
                int forumLabel = description.IndexOf("форум:", StringComparison.OrdinalIgnoreCase);
                if (forumLabel >= 0)
                    description = description.Substring(0, forumLabel).Trim();

                item.Description = string.IsNullOrWhiteSpace(forumTitle)
                    ? description
                    : (string.IsNullOrWhiteSpace(description)
                        ? "Форум: " + forumTitle
                        : description + " · Форум: " + forumTitle);

                item.Date = CleanText(FirstMatch(block,
                    @"Послед[\s\S]*?</a>\s*([^<]*?)(?:</div>|</td>|<br|$)", 1));
                item.IsPinned = block.IndexOf("(!)", StringComparison.OrdinalIgnoreCase) >= 0
                    || block.IndexOf("важн", StringComparison.OrdinalIgnoreCase) >= 0;
                item.IsClosed = block.IndexOf("закры", StringComparison.OrdinalIgnoreCase) >= 0;

                AddSearchTopicUnique(data, item);
            }
        }

        private ForumTopicsData ParseSearchTopicsPage(string html, int start)
        {
            ForumTopicsData data = new ForumTopicsData();
            data.ForumId = 0;
            data.Title = "Поиск";

            if (string.IsNullOrWhiteSpace(html))
                return data;

            // Старая и текущая табличная выдача использует id="tid-link-ID".
            // Это основной формат рабочего клиента из приложенного архива.
            ParseSearchTopicsTableLayout(html, data);

            // Разметка, на которую ориентируется ForPDA.
            Regex itemRegex = new Regex(
                @"<div[^>]*?data-topic\s*=\s*[""'](\d+)[""'][^>]*?>[\s\S]*?(?:<font\s+color\s*=\s*[""']([^""']*)[""'][^>]*?>([^<]*?)</font>[\s\S]*?)?<a[^>]*?>([\s\S]*?)</a>[\s\S]*?<span[^>]*?class\s*=\s*[""'][^""']*topic_desc[^""']*[""'][^>]*>(?:(?!форум)([\s\S]*?)<br[^>]*?>)?\s*форум\s*:[^<]*?<a[^>]*?showforum=(\d+)[^>]*?>([\s\S]*?)</a>[\s\S]*?автор\s*:[^<]*?<a[^>]*?showuser=(\d+)[^>]*?>([\s\S]*?)</a>[\s\S]*?Послед[\s\S]*?<a[^>]*?showuser=(\d+)[^>]*?>([\s\S]*?)</a>(?:\s*)?([^<]*?)</div>",
                RegexOptions.IgnoreCase);

            foreach (Match match in itemRegex.Matches(html))
            {
                ForumTopicItem item = new ForumTopicItem();
                item.Id = ParseInt(match.Groups[1].Value);
                item.Title = CleanText(match.Groups[4].Value);
                item.ForumId = ParseInt(match.Groups[6].Value);
                item.Author = CleanText(match.Groups[9].Value);
                item.LastUser = CleanText(match.Groups[11].Value);
                item.Date = CleanText(match.Groups[12].Value);
                item.Url = ForumUrl + "?showtopic=" + item.Id;

                string description = CleanText(match.Groups[5].Value);
                string forumTitle = CleanText(match.Groups[7].Value);
                if (!string.IsNullOrWhiteSpace(forumTitle))
                {
                    item.Description = string.IsNullOrWhiteSpace(description)
                        ? "Форум: " + forumTitle
                        : description + " · Форум: " + forumTitle;
                }
                else
                {
                    item.Description = description;
                }

                string marker = CleanText(match.Groups[3].Value);
                item.IsPinned = marker.IndexOf("важн", StringComparison.OrdinalIgnoreCase) >= 0;
                item.IsClosed = marker.IndexOf("закры", StringComparison.OrdinalIgnoreCase) >= 0;

                AddSearchTopicUnique(data, item);
            }

            if (data.Items.Count == 0)
                ParseSearchTopicsFallback(html, data);

            // Сервер иногда отдаёт результат как обычную страницу списка тем.
            if (data.Items.Count == 0)
            {
                ForumTopicsData regularTopics = ParseTopicsPage(html, 0);
                for (int i = 0; i < regularTopics.Items.Count; i++)
                {
                    ForumTopicItem item = regularTopics.Items[i];
                    if (item != null && !item.IsForum)
                        AddSearchTopicUnique(data, item);
                }
            }

            // Последний fallback не зависит от data-topic и находит темы по ссылкам.
            if (data.Items.Count == 0)
                ParseSearchTopicsByLinks(html, data);

            ParseSearchPagination(html, start, data);
            return data;
        }

        private void ParseSearchTopicsFallback(string html, ForumTopicsData data)
        {
            Regex rowRegex = new Regex(
                @"<(?:div|tr|li)\b[^>]*?data-topic\s*=\s*[""'](\d+)[""'][^>]*?>([\s\S]*?)(?=<(?:div|tr|li)\b[^>]*?data-topic\s*=\s*[""']\d+[""']|</form>|<!--\s*TABLE FOOTER|<div[^>]*?id\s*=\s*[""']gfooter[""']|$)",
                RegexOptions.IgnoreCase);

            foreach (Match row in rowRegex.Matches(html ?? string.Empty))
            {
                string block = row.Groups[2].Value;
                Match titleMatch = FindSearchTopicTitleLink(block, ParseInt(row.Groups[1].Value));
                if (!titleMatch.Success)
                    continue;

                ForumTopicItem item = CreateSearchTopicItem(block, titleMatch, ParseInt(row.Groups[1].Value));
                AddSearchTopicUnique(data, item);
            }
        }

        private void ParseSearchTopicsByLinks(string html, ForumTopicsData data)
        {
            if (string.IsNullOrWhiteSpace(html))
                return;

            MatchCollection links = Regex.Matches(html,
                @"<a\b[^>]*?href\s*=\s*[""']([^""']*?(?:showtopic=|[?&]t=)(\d+)[^""']*)[""'][^>]*>([\s\S]*?)</a>",
                RegexOptions.IgnoreCase);
            HashSet<int> used = new HashSet<int>();

            for (int i = 0; i < links.Count; i++)
            {
                Match link = links[i];
                int topicId = ParseInt(link.Groups[2].Value);
                string title = CleanText(link.Groups[3].Value);
                string href = WebUtility.HtmlDecode(link.Groups[1].Value).Replace("&amp;", "&");

                if (topicId <= 0 || used.Contains(topicId) || !IsUsableSearchTopicTitle(title))
                    continue;

                if (href.IndexOf("view=findpost", StringComparison.OrdinalIgnoreCase) >= 0
                    || href.IndexOf("view=getnewpost", StringComparison.OrdinalIgnoreCase) >= 0
                    || Regex.IsMatch(href, @"(?:[?&])st=\d+", RegexOptions.IgnoreCase))
                    continue;

                int contextStart = Math.Max(0, link.Index - 900);
                int contextEnd = Math.Min(html.Length, link.Index + link.Length + 2600);
                string block = html.Substring(contextStart, contextEnd - contextStart);

                // Не захватываем ссылки навигации и случайные упоминания тем.
                if (block.IndexOf("topic", StringComparison.OrdinalIgnoreCase) < 0
                    && block.IndexOf("форум", StringComparison.OrdinalIgnoreCase) < 0
                    && block.IndexOf("search", StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                ForumTopicItem item = CreateSearchTopicItem(block, link, topicId);
                if (item == null)
                    continue;

                used.Add(topicId);
                AddSearchTopicUnique(data, item);
            }
        }

        private Match FindSearchTopicTitleLink(string block, int topicId)
        {
            MatchCollection links = Regex.Matches(block ?? string.Empty,
                @"<a\b[^>]*?href\s*=\s*[""']([^""']*?(?:showtopic=|[?&]t=)(\d+)[^""']*)[""'][^>]*>([\s\S]*?)</a>",
                RegexOptions.IgnoreCase);

            for (int i = 0; i < links.Count; i++)
            {
                Match link = links[i];
                int candidateId = ParseInt(link.Groups[2].Value);
                string href = WebUtility.HtmlDecode(link.Groups[1].Value).Replace("&amp;", "&");
                string title = CleanText(link.Groups[3].Value);

                if (topicId > 0 && candidateId != topicId)
                    continue;
                if (!IsUsableSearchTopicTitle(title))
                    continue;
                if (href.IndexOf("view=findpost", StringComparison.OrdinalIgnoreCase) >= 0
                    || href.IndexOf("view=getnewpost", StringComparison.OrdinalIgnoreCase) >= 0
                    || Regex.IsMatch(href, @"(?:[?&])st=\d+", RegexOptions.IgnoreCase))
                    continue;

                return link;
            }

            return Match.Empty;
        }

        private ForumTopicItem CreateSearchTopicItem(string block, Match titleMatch, int topicId)
        {
            if (titleMatch == null || !titleMatch.Success)
                return null;

            int parsedId = topicId > 0 ? topicId : ParseInt(titleMatch.Groups[2].Value);
            string title = CleanText(titleMatch.Groups[3].Value);
            if (parsedId <= 0 || !IsUsableSearchTopicTitle(title))
                return null;

            ForumTopicItem item = new ForumTopicItem();
            item.Id = parsedId;
            item.Title = title;
            item.Url = NormalizeForumUrl(titleMatch.Groups[1].Value);

            Match forumMatch = Regex.Match(block ?? string.Empty,
                @"форум\s*:[^<]*?<a[^>]*?(?:showforum|[?&]f)=(\d+)[^>]*?>([\s\S]*?)</a>",
                RegexOptions.IgnoreCase);
            if (!forumMatch.Success)
            {
                forumMatch = Regex.Match(block ?? string.Empty,
                    @"<a[^>]*?href\s*=\s*[""'][^""']*?(?:showforum|[?&]f)=(\d+)[^""']*[""'][^>]*?>([\s\S]*?)</a>",
                    RegexOptions.IgnoreCase);
            }

            string forumTitle = string.Empty;
            if (forumMatch.Success)
            {
                item.ForumId = ParseInt(forumMatch.Groups[1].Value);
                forumTitle = CleanText(forumMatch.Groups[2].Value);
            }

            string descriptionHtml = FirstMatch(block,
                @"<span[^>]*?class\s*=\s*[""'][^""']*topic_desc[^""']*[""'][^>]*>([\s\S]*?)</span>", 1);
            string description = CleanText(descriptionHtml);
            int forumLabel = description.IndexOf("форум:", StringComparison.OrdinalIgnoreCase);
            if (forumLabel >= 0)
                description = description.Substring(0, forumLabel).Trim();

            if (!string.IsNullOrWhiteSpace(forumTitle))
            {
                item.Description = string.IsNullOrWhiteSpace(description)
                    ? "Форум: " + forumTitle
                    : description + " · Форум: " + forumTitle;
            }
            else
            {
                item.Description = description;
            }

            MatchCollection users = Regex.Matches(block ?? string.Empty,
                @"<a[^>]*?(?:showuser|[?&]u)=(\d+)[^>]*?>([\s\S]*?)</a>",
                RegexOptions.IgnoreCase);
            if (users.Count > 0)
                item.Author = CleanText(users[0].Groups[2].Value);
            if (users.Count > 1)
                item.LastUser = CleanText(users[users.Count - 1].Groups[2].Value);

            item.Date = CleanText(FirstMatch(block,
                @"Послед[\s\S]*?</a>\s*([^<]*?)(?:</div>|</td>|<br|$)", 1));

            string marker = CleanText(FirstMatch(block,
                @"class\s*=\s*[""'][^""']*(?:modifier|forum_img_with_link)[^""']*[""'][^>]*>([\s\S]*?)</(?:span|a)>", 1));
            item.IsPinned = marker.IndexOf("важн", StringComparison.OrdinalIgnoreCase) >= 0
                || (block ?? string.Empty).IndexOf("(!)", StringComparison.OrdinalIgnoreCase) >= 0;
            item.IsClosed = marker.IndexOf("закры", StringComparison.OrdinalIgnoreCase) >= 0
                || marker.IndexOf("X", StringComparison.OrdinalIgnoreCase) >= 0
                || marker.IndexOf("Х", StringComparison.OrdinalIgnoreCase) >= 0;
            item.IsPoll = marker.Contains("^");

            return item;
        }

        private void AddSearchTopicUnique(ForumTopicsData data, ForumTopicItem item)
        {
            if (data == null || item == null || item.Id <= 0 || string.IsNullOrWhiteSpace(item.Title))
                return;

            for (int i = 0; i < data.Items.Count; i++)
            {
                if (data.Items[i] != null && data.Items[i].Id == item.Id && !data.Items[i].IsForum)
                    return;
            }

            data.Items.Add(item);
        }

        private static bool IsUsableSearchTopicTitle(string title)
        {
            if (string.IsNullOrWhiteSpace(title) || title.Length < 2)
                return false;

            int numeric;
            if (Int32.TryParse(title, out numeric))
                return false;

            string normalized = title.Trim();
            return normalized != "#"
                && normalized.IndexOf("последнее сообщение", StringComparison.OrdinalIgnoreCase) < 0
                && normalized.IndexOf("перейти к", StringComparison.OrdinalIgnoreCase) < 0
                && normalized.IndexOf("следующая", StringComparison.OrdinalIgnoreCase) < 0
                && normalized.IndexOf("предыдущая", StringComparison.OrdinalIgnoreCase) < 0;
        }

        private void ParseSearchPagination(string html, int start, ForumTopicsData data)
        {
            ParseForumPagination(html, data);

            List<int> starts = new List<int>();
            foreach (Match match in Regex.Matches(html ?? string.Empty,
                @"(?:[?&]|&amp;)st=(\d+)", RegexOptions.IgnoreCase))
            {
                int value = ParseInt(match.Groups[1].Value);
                if (value >= 0 && !starts.Contains(value))
                    starts.Add(value);
            }

            starts.Sort();
            int pageSize = data.PageSize > 0 ? data.PageSize : 30;
            int smallestStep = Int32.MaxValue;
            int maximum = 0;
            for (int i = 0; i < starts.Count; i++)
            {
                if (i > 0)
                {
                    int step = starts[i] - starts[i - 1];
                    if (step > 0 && step < smallestStep)
                        smallestStep = step;
                }

                if (starts[i] > maximum)
                    maximum = starts[i];
            }

            if (smallestStep != Int32.MaxValue)
                pageSize = smallestStep;
            if (pageSize <= 0)
                pageSize = 30;

            data.PageSize = pageSize;
            data.CurrentPage = (Math.Max(0, start) / pageSize) + 1;

            if (maximum > 0)
                data.TotalPages = Math.Max(data.CurrentPage, (maximum / pageSize) + 1);
            else if (data.Items.Count >= pageSize)
                data.TotalPages = data.CurrentPage + 1;
            else
                data.TotalPages = data.CurrentPage;
        }

        private ForumTopicsData ParseTopicsPage(string html, int forumId)
        {
            ForumTopicsData data = new ForumTopicsData();
            data.ForumId = forumId;
            data.Title = CleanText(FirstMatch(html, "<div[^>]*?id=[\"']navstrip[\"'][\\s\\S]*?showforum=" + forumId + "[^>]*?>([\\s\\S]*?)</a>[\\s\\S]*?</div>", 1));
            if (string.IsNullOrWhiteSpace(data.Title))
            {
                data.Title = CleanText(FirstMatch(html, "<div[^>]*?class=[\"']cat_name[\"'][^>]*?>([\\s\\S]*?)</div>", 1));
            }

            ParseForumPagination(html, data);
            ParseTopicSubForums(html, data);
            ParseAnnounces(html, data);
            ParseTopics(html, data);
            return data;
        }

        private void ParseForumPagination(string html, ForumTopicsData data)
        {
            Match pagination = Regex.Match(html, "parseInt\\((\\d*)\\)[\\s\\S]*?parseInt\\(st\\*(\\d*)\\)[\\s\\S]*?pagination[\"'][^>]*?>[\\s\\S]*?<span[^>]*?>([^<]*?)</span>", RegexOptions.IgnoreCase);
            if (pagination.Success)
            {
                data.TotalPages = ParseInt(pagination.Groups[1].Value) + 1;
                data.PageSize = Math.Max(1, ParseInt(pagination.Groups[2].Value));
                data.CurrentPage = Math.Max(1, ParseInt(pagination.Groups[3].Value));
            }
            else
            {
                data.TotalPages = 1;
                data.PageSize = 30;
                data.CurrentPage = 1;
            }
        }

        private void ParseTopicSubForums(string html, ForumTopicsData data)
        {
            Regex rowRegex = new Regex("<div[^>]*?class=[\"']board_forum_row[\"'][^>]*?>([\\s\\S]*?)(?=<div[^>]*?class=[\"']board_forum_row[\"']|<div[^>]*?class=[\"']pagination[\"']|<div[^>]*?class=[\"']cat_name[\"']|<div[^>]*?data-topic=|<div id=[\"']gfooter[\"']|$)", RegexOptions.IgnoreCase);
            foreach (Match rowMatch in rowRegex.Matches(html))
            {
                string row = rowMatch.Groups[1].Value;
                Match titleMatch = Regex.Match(row, "<div[^>]*?class=[\"']board_forum_name[\"'][^>]*?>[\\s\\S]*?<a[^>]*?showforum=(\\d+)[^>]*?>([\\s\\S]*?)</a>", RegexOptions.IgnoreCase);
                if (!titleMatch.Success)
                {
                    continue;
                }

                ForumTopicItem item = new ForumTopicItem();
                item.IsForum = true;
                item.Id = ParseInt(titleMatch.Groups[1].Value);
                item.ForumId = item.Id;
                item.Title = CleanText(titleMatch.Groups[2].Value);
                item.Description = CleanText(FirstMatch(row, "<div[^>]*?class=[\"']board_forum_desc[\"'][^>]*?>([\\s\\S]*?)</div>", 1));
                item.LastUser = CleanText(FirstMatch(row, "showuser=\\d+[^>]*?>([\\s\\S]*?)</a>\\s*</div>", 1));
                item.Date = CleanText(FirstMatch(row, "<div[^>]*?class=[\"']board_forum_date[\"'][^>]*?>([\\s\\S]*?)</div>", 1));
                item.Url = ForumUrl + "?showforum=" + item.Id;

                string lastTopic = CleanText(FirstMatch(row, "board-forum-lastpost[\\s\\S]*?<a[^>]*?showtopic=\\d+[^>]*?>[\\s\\S]*?</a>\\s*<a[^>]*?>([\\s\\S]*?)</a>", 1));
                if (string.IsNullOrWhiteSpace(item.Description))
                {
                    item.Description = lastTopic;
                }

                if (item.Id > 0 && !string.IsNullOrWhiteSpace(item.Title))
                {
                    data.Items.Add(item);
                }
            }
        }

        private void ParseAnnounces(string html, ForumTopicsData data)
        {
            Regex announceRegex = new Regex("<div[^>]*?class=[\"']anonce_body[\"'][^>]*?>[\\s\\S]*?<a[^>]*?href=[\"']([^\"']*?)[\"'][^>]*?>([\\s\\S]*?)</a>[\\s\\S]*?</div>", RegexOptions.IgnoreCase);
            foreach (Match match in announceRegex.Matches(html))
            {
                ForumTopicItem item = new ForumTopicItem();
                item.IsPinned = true;
                item.Title = CleanText(match.Groups[2].Value);
                item.Description = "Объявление";
                item.Url = NormalizeForumUrl(match.Groups[1].Value);

                if (!string.IsNullOrWhiteSpace(item.Title))
                {
                    data.Items.Add(item);
                }
            }
        }

        private void ParseTopics(string html, ForumTopicsData data)
        {
            Regex topicRegex = new Regex("<div[^>]*?data-topic=[\"'](\\d+)[\"'][^>]*?>([\\s\\S]*?)(?=<div[^>]*?data-topic=[\"']\\d+[\"']|<div id=[\"']gfooter[\"']|$)", RegexOptions.IgnoreCase);
            foreach (Match topicMatch in topicRegex.Matches(html))
            {
                string block = topicMatch.Groups[2].Value;
                Match titleMatch = Regex.Match(block, "<div[^>]*?class=[\"']topic_title[\"'][^>]*?>([\\s\\S]*?)</div>", RegexOptions.IgnoreCase);
                Match bodyMatch = Regex.Match(block, "<div[^>]*?class=[\"']topic_body[\"'][^>]*?>([\\s\\S]*?)</div>", RegexOptions.IgnoreCase);
                if (!titleMatch.Success || !bodyMatch.Success)
                {
                    continue;
                }

                string titleHtml = titleMatch.Groups[1].Value;
                string bodyHtml = bodyMatch.Groups[1].Value;
                Match linkMatch = Regex.Match(titleHtml, "<a[^>]*?href=[\"']([^\"']*?showtopic=(\\d+)[^\"']*)[\"'][^>]*?>([\\s\\S]*?)</a>", RegexOptions.IgnoreCase);
                if (!linkMatch.Success)
                {
                    continue;
                }

                ForumTopicItem item = new ForumTopicItem();
                item.Id = ParseInt(topicMatch.Groups[1].Value);
                item.ForumId = data.ForumId;
                item.Url = NormalizeForumUrl(linkMatch.Groups[1].Value);
                item.Title = CleanText(linkMatch.Groups[3].Value);

                string modifier = CleanText(FirstMatch(titleHtml, "<span[^>]*?class=[\"']modifier[\"'][^>]*?>([\\s\\S]*?)</span>", 1));
                item.IsNew = modifier.Contains("+");
                item.IsClosed = modifier.IndexOf("X", StringComparison.OrdinalIgnoreCase) >= 0;
                item.IsPoll = modifier.Contains("^");
                item.IsPinned = titleHtml.Contains("(!)") || titleHtml.Contains("&#33;");

                List<string> descParts = MatchAll(bodyHtml, "<span[^>]*?class=[\"']topic_desc[\"'][^>]*?>([\\s\\S]*?)</span>");
                item.Description = ExtractDescription(descParts);
                item.Author = ExtractUserByLabel(bodyHtml, "автор");
                item.LastUser = ExtractLastUser(bodyHtml);
                item.Date = ExtractLastDate(bodyHtml);
                item.Curator = ExtractUserByLabel(bodyHtml, "Куратор темы");

                if (item.Id > 0 && !string.IsNullOrWhiteSpace(item.Title))
                {
                    data.Items.Add(item);
                }
            }
        }

        private TopicPageData ParseTopicPage(string html, string url)
        {
            TopicPageData data = new TopicPageData();
            data.Url = url;
            data.TopicId = ExtractQueryInt(url, "showtopic");
            data.ForumId = ExtractQueryInt(url, "f");

            Match topicIds = Regex.Match(html, "ipb_input_f:(\\d+),[\\s\\S]*?ipb_input_t:(\\d+),", RegexOptions.IgnoreCase);
            if (topicIds.Success)
            {
                data.ForumId = ParseInt(topicIds.Groups[1].Value);
                data.TopicId = ParseInt(topicIds.Groups[2].Value);
            }

            data.AuthKey = ParseTopicAuthKey(html);
            data.IsReplyForbidden = ParseReplyForbidden(html);
            data.CanReply = ForumAuthService.IsAuthorized
                && !data.IsReplyForbidden
                && (ParseCanReply(html) || data.TopicId > 0);

            string titleBlock = FirstMatch(html, "<div[^>]*?class=[\"']topic_title_post[\"'][^>]*?>([\\s\\S]*?)<br", 1);
            string fullTitle = CleanText(titleBlock);
            int divider = fullTitle.IndexOf("|", StringComparison.Ordinal);
            if (divider >= 0)
            {
                data.Title = fullTitle.Substring(0, divider).Trim();
                data.Description = fullTitle.Substring(divider + 1).Trim();
            }
            else
            {
                data.Title = fullTitle;
            }

            ParseTopicPagination(html, data);
            ParseTopicPosts(html, data);
            return data;
        }

        private void ParseTopicPagination(string html, TopicPageData data)
        {
            Match pagination = Regex.Match(html, "parseInt\\((\\d*)\\)[\\s\\S]*?parseInt\\(st\\*(\\d*)\\)[\\s\\S]*?pagination[\"'][^>]*?>[\\s\\S]*?<span[^>]*?>([^<]*?)</span>", RegexOptions.IgnoreCase);
            if (pagination.Success)
            {
                data.TotalPages = ParseInt(pagination.Groups[1].Value) + 1;
                data.PageSize = Math.Max(1, ParseInt(pagination.Groups[2].Value));
                data.CurrentPage = Math.Max(1, ParseInt(pagination.Groups[3].Value));
            }
            else
            {
                data.TotalPages = 1;
                data.PageSize = 20;
                data.CurrentPage = 1;
            }
        }

        private void ParseTopicPosts(string html, TopicPageData data)
        {
            MatchCollection starts = Regex.Matches(html, "<div[^>]*?data-post=[\"'](\\d+)[\"'][^>]*?>", RegexOptions.IgnoreCase);
            for (int i = 0; i < starts.Count; i++)
            {
                Match startMatch = starts[i];
                int blockStart = startMatch.Index;
                int blockEnd = i + 1 < starts.Count ? starts[i + 1].Index : html.Length;
                string block = html.Substring(blockStart, blockEnd - blockStart);

                TopicPostItem item = new TopicPostItem();
                item.Id = ParseInt(startMatch.Groups[1].Value);
                item.TopicId = data.TopicId;
                item.ForumId = data.ForumId;

                Match dateMatch = Regex.Match(block, "<span[^>]*?class=[\"']post_date[\"'][^>]*?>([^&|<]*?)(?:&nbsp;|\\s)*\\|[\\s\\S]*?>#?(\\d+)</a>", RegexOptions.IgnoreCase);
                if (dateMatch.Success)
                {
                    item.Date = CleanText(dateMatch.Groups[1].Value);
                    item.Number = ParseInt(dateMatch.Groups[2].Value);
                }

                string nickBlock = FirstMatch(block, "<span[^>]*?class=[\"']post_nick[\"'][^>]*?>([\\s\\S]*?)</span>", 1);
                Match nickMatch = Regex.Match(nickBlock, "<a[^>]*?showuser=(\\d+)[^>]*?>([\\s\\S]*?)</a>", RegexOptions.IgnoreCase);
                if (nickMatch.Success)
                {
                    item.UserId = ParseInt(nickMatch.Groups[1].Value);
                    item.AvatarUrl = BuildAvatarUrl(FirstMatch(nickMatch.Value, "data-av=[\"']([^\"']*)[\"']", 1));
                    item.Author = CleanPostAuthor(nickMatch.Groups[2].Value);
                }

                item.IsOnline = block.IndexOf("color=\"green\"", StringComparison.OrdinalIgnoreCase) >= 0;
                item.UserInfo = CleanText(FirstMatch(block, "<span[^>]*?class=[\"']post_user_info[\"'][^>]*?>([\\s\\S]*?)</span>", 1));
                item.Reputation = CleanText(FirstMatch(block, "data-member-rep=[\"']\\d+[\"'][^>]*?>([^<]*?)</span>", 1));

                ApplyPostCapabilities(block, item);
                item.BodyHtml = ExtractDivInnerByClass(block, "post_body");

                if (item.Id > 0 && (!string.IsNullOrWhiteSpace(item.BodyHtml) || !string.IsNullOrWhiteSpace(item.Author)))
                {
                    data.Posts.Add(item);
                }
            }
        }

        private void ApplyPostCapabilities(string block, TopicPostItem item)
        {
            if (item == null || string.IsNullOrWhiteSpace(block))
                return;

            // Разрешения находятся в шапке поста. Тело сообщения может содержать
            // процитированный HTML или текст с похожими именами классов, поэтому
            // оно не должно влиять на видимость действий текущего сообщения.
            Match bodyMatch = Regex.Match(block,
                @"<div[^>]*?class=[""'][^""']*\bpost_body\b",
                RegexOptions.IgnoreCase);
            string actionsBlock = bodyMatch.Success && bodyMatch.Index > 0
                ? block.Substring(0, bodyMatch.Index)
                : block;

            // ForPDA показывает каждую оценку только при наличии соответствующей
            // ссылки win_minus / win_add, которую сервер вернул именно для поста.
            item.CanMinus = item.CanMinus || HasPostVoteAction(actionsBlock, false);
            item.CanPlus = item.CanPlus || HasPostVoteAction(actionsBlock, true);
            item.CanReport = item.CanReport || Regex.IsMatch(actionsBlock,
                @"(?:act=report|report_post|reportPost|class=[""'][^""']*report)",
                RegexOptions.IgnoreCase);
            item.CanEdit = item.CanEdit || Regex.IsMatch(actionsBlock,
                @"(?:edit_post|editPost|CODE=08|do=edit)",
                RegexOptions.IgnoreCase);
            item.CanDelete = item.CanDelete || Regex.IsMatch(actionsBlock,
                @"(?:delete_post|deletePost|tact=delete|selectedpids=|class=[""'][^""']*delete)",
                RegexOptions.IgnoreCase);
            item.CanQuote = item.CanQuote || Regex.IsMatch(actionsBlock,
                @"(?:pasteQ|CODE=02|do=reply|reply_post|IThemePresenter\.reply)",
                RegexOptions.IgnoreCase);

            if (string.IsNullOrWhiteSpace(item.EditUrl))
            {
                item.EditUrl = NormalizeActionUrl(FirstMatch(actionsBlock,
                    @"<a[^>]*?href=[""']([^""']*)[""'][^>]*?(?:edit_post|CODE=08|do=edit)[^>]*?>", 1));
            }

            if (string.IsNullOrWhiteSpace(item.DeleteUrl))
            {
                item.DeleteUrl = NormalizeActionUrl(FirstMatch(actionsBlock,
                    @"<a[^>]*?href=[""']([^""']*)[""'][^>]*?(?:class=[""'][^""']*delete|tact=delete|selectedpids=|delete_post)[^>]*?>", 1));
            }
        }

        private bool HasPostVoteAction(string block, bool positive)
        {
            if (string.IsNullOrWhiteSpace(block))
                return false;

            string marker = positive ? "win_add" : "win_minus";
            if (Regex.IsMatch(block,
                @"<a\b(?=[^>]*\b" + marker + @"\b)[^>]*>",
                RegexOptions.IgnoreCase))
            {
                return true;
            }

            // Совместимость с вариантами, где действие записано URL или
            // JavaScript-вызовом, но только внутри ссылки действия поста.
            string valuePattern = positive
                ? @"(?:[?&](?:v|vote|type)=\+?1\b|,\s*(?:1|true)\s*\))"
                : @"(?:[?&](?:v|vote|type)=-1\b|,\s*(?:-1|false)\s*\))";

            return Regex.IsMatch(block,
                @"<a\b(?=[^>]*(?:zka\.php|votePost|rep(?:utation)?[_A-Za-z]*))(?=[^>]*"
                + valuePattern + @")[^>]*>",
                RegexOptions.IgnoreCase);
        }

        private void MergeTopicCapabilities(string authenticatedHtml, TopicPageData target)
        {
            if (target == null || string.IsNullOrWhiteSpace(authenticatedHtml))
                return;

            string authKey = ParseTopicAuthKey(authenticatedHtml);
            if (!string.IsNullOrWhiteSpace(authKey))
                target.AuthKey = authKey;

            target.IsReplyForbidden = ParseReplyForbidden(authenticatedHtml);
            target.CanReply = ForumAuthService.IsAuthorized
                && !target.IsReplyForbidden
                && (ParseCanReply(authenticatedHtml) || target.TopicId > 0);
        }

        private string ParseTopicAuthKey(string html)
        {
            string key = FirstMatch(html,
                "name\\s*=\\s*[\\\"']auth_key[\\\"'][^>]*value\\s*=\\s*[\\\"']([^\\\"' >]+)[\\\"']", 1);
            if (string.IsNullOrWhiteSpace(key))
            {
                key = FirstMatch(html,
                    "value\\s*=\\s*[\\\"']([^\\\"' >]+)[\\\"'][^>]*name\\s*=\\s*[\\\"']auth_key[\\\"']", 1);
            }
            if (string.IsNullOrWhiteSpace(key))
            {
                key = FirstMatch(html,
                    "(?:auth_key|authKey)\\s*[:=]\\s*[\\\"']([a-z0-9_-]{8,})[\\\"']", 1);
            }
            return WebUtility.HtmlDecode(key ?? string.Empty).Trim();
        }

        private bool ParseReplyForbidden(string html)
        {
            if (string.IsNullOrWhiteSpace(html))
                return false;

            // Нельзя искать просто слова closed/locked в любом class: они часто
            // встречаются у меню, иконок и служебных блоков даже в открытой теме.
            // Учитываем только явный текст запрета или специальные маркеры формы
            // ответа. Иначе TopicPage сначала показывает кнопку, а после разбора
            // обычного CSS-класса ошибочно скрывает её.
            return Regex.IsMatch(html,
                @"(?:>\s*Тема\s+закрыта(?:\s+для\s+ответов)?\s*<|" +
                @"Вы\s+не\s+можете\s+(?:отвечать|писать)|" +
                @"У\s+вас\s+нет\s+прав[^<]{0,80}(?:ответ|сообщен)|" +
                @"<(?:input|button)[^>]*(?:name=[""']Post[""']|CODE=03)[^>]*disabled)",
                RegexOptions.IgnoreCase);
        }

        private bool ParseCanReply(string html)
        {
            if (string.IsNullOrWhiteSpace(html) || ParseReplyForbidden(html))
                return false;

            return Regex.IsMatch(html,
                "(?:name\\s*=\\s*[\\\"']Post[\\\"']|act=Post[^\\\"' >]*(?:CODE=03|code=03)|Быстрый\\s+ответ|Добавить\\s+ответ|Ответить|reply_post|pasteQ|IThemePresenter\\.reply|\\breply\\b)",
                RegexOptions.IgnoreCase);
        }

        private void MergePostCapabilities(string authenticatedHtml, TopicPageData target)
        {
            if (string.IsNullOrWhiteSpace(authenticatedHtml) || target == null || target.Posts == null)
                return;

            bool pageCanQuote = Regex.IsMatch(authenticatedHtml,
                "(?:pasteQ|CODE=02|do=reply|IThemePresenter\\.reply)", RegexOptions.IgnoreCase);

            for (int i = 0; i < target.Posts.Count; i++)
            {
                TopicPostItem post = target.Posts[i];
                if (post == null || post.Id <= 0)
                    continue;

                string block = ExtractPostActionBlock(authenticatedHtml, post.Id);
                if (!string.IsNullOrWhiteSpace(block))
                {
                    ApplyPostCapabilities(block, post);
                }

                if (pageCanQuote)
                {
                    post.CanQuote = true;
                }
            }
        }

        private string ExtractPostActionBlock(string html, int postId)
        {
            if (string.IsNullOrWhiteSpace(html) || postId <= 0)
                return string.Empty;

            string id = postId.ToString();
            Match start = Regex.Match(html,
                "<div[^>]*?(?:data-post(?:-id)?=[\\\"']" + id + "[\\\"']|(?:id|name)=[\\\"'](?:entry|post)[_-]?" + id + "[\\\"'])[^>]*?>",
                RegexOptions.IgnoreCase);
            if (!start.Success)
                return string.Empty;

            Match next = Regex.Match(html.Substring(start.Index + start.Length),
                "<div[^>]*?(?:data-post(?:-id)?=[\\\"']\\d+[\\\"']|(?:id|name)=[\\\"'](?:entry|post)[_-]?\\d+[\\\"'])[^>]*?>",
                RegexOptions.IgnoreCase);

            int end = next.Success ? start.Index + start.Length + next.Index : html.Length;
            return html.Substring(start.Index, end - start.Index);
        }

        private string NormalizeActionUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return string.Empty;

            string value = WebUtility.HtmlDecode(url.Trim()).Replace("&amp;", "&");
            if (value.StartsWith("//", StringComparison.Ordinal))
                return "https:" + value;
            if (value.StartsWith("/", StringComparison.Ordinal))
                return "https://4pda.to" + value;
            if (value.StartsWith("index.php", StringComparison.OrdinalIgnoreCase))
                return "https://4pda.to/forum/" + value;
            if (value.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || value.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                return value;
            return string.Empty;
        }

        private string BuildTopicPageUrl(string url, int start)
        {
            string result = NormalizeForumUrl(url);
            if (string.IsNullOrWhiteSpace(result))
            {
                result = ForumUrl + "?showtopic=1";
            }

            int hashIndex = result.IndexOf("#", StringComparison.Ordinal);
            if (hashIndex >= 0)
            {
                result = result.Substring(0, hashIndex);
            }

            result = Regex.Replace(result, "([?&])st=\\d+(&?)", "$1", RegexOptions.IgnoreCase);
            result = result.TrimEnd('&', '?');
            result += result.IndexOf("?", StringComparison.Ordinal) >= 0 ? "&" : "?";
            result += "st=" + Math.Max(0, start).ToString();
            return result;
        }

        private int ExtractQueryInt(string url, string key)
        {
            if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(key))
            {
                return 0;
            }

            Match match = Regex.Match(url, "(?:\\?|&)" + Regex.Escape(key) + "=(\\d+)", RegexOptions.IgnoreCase);
            return match.Success ? ParseInt(match.Groups[1].Value) : 0;
        }

        private string BuildAvatarUrl(string avatar)
        {
            if (string.IsNullOrWhiteSpace(avatar))
            {
                return string.Empty;
            }

            string clean = WebUtility.HtmlDecode(avatar.Trim());
            if (clean.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || clean.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                return clean;
            }

            return "https://s.4pda.to/forum/uploads/" + clean.TrimStart('/');
        }

        private string ExtractDivInnerByClass(string source, string className)
        {
            if (string.IsNullOrWhiteSpace(source))
            {
                return string.Empty;
            }

            Match openMatch = Regex.Match(source, "<div[^>]*?class=[\"'][^\"']*" + Regex.Escape(className) + "[^\"']*[\"'][^>]*?>", RegexOptions.IgnoreCase);
            if (!openMatch.Success)
            {
                return string.Empty;
            }

            int innerStart = openMatch.Index + openMatch.Length;
            Regex tagRegex = new Regex("</?div\\b[^>]*>", RegexOptions.IgnoreCase);
            MatchCollection tags = tagRegex.Matches(source, innerStart);
            int depth = 1;
            foreach (Match tag in tags)
            {
                if (tag.Value.StartsWith("</", StringComparison.Ordinal))
                {
                    depth--;
                    if (depth == 0)
                    {
                        return source.Substring(innerStart, tag.Index - innerStart);
                    }
                }
                else
                {
                    depth++;
                }
            }

            return source.Substring(innerStart);
        }

        private List<string> MatchAll(string source, string pattern)
        {
            List<string> results = new List<string>();
            if (string.IsNullOrWhiteSpace(source))
            {
                return results;
            }

            foreach (Match match in Regex.Matches(source, pattern, RegexOptions.IgnoreCase))
            {
                if (match.Success && match.Groups.Count > 1)
                {
                    results.Add(match.Groups[1].Value);
                }
            }

            return results;
        }

        private string ExtractDescription(List<string> descParts)
        {
            for (int i = 0; i < descParts.Count; i++)
            {
                string clean = CleanText(descParts[i]);
                if (!string.IsNullOrWhiteSpace(clean)
                    && clean.IndexOf("автор:", StringComparison.OrdinalIgnoreCase) < 0
                    && clean.IndexOf("Куратор темы:", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    return clean;
                }
            }

            return string.Empty;
        }

        private string ExtractUserByLabel(string html, string label)
        {
            string pattern = Regex.Escape(label) + "[^<]*?<a[^>]*?showuser=\\d+[^>]*?>([\\s\\S]*?)</a>";
            return CleanText(FirstMatch(html, pattern, 1));
        }

        private string ExtractLastUser(string html)
        {
            Match lastMatch = Regex.Match(html, "Послед\\.[\\s\\S]*?<a[^>]*?showuser=\\d+[^>]*?>([\\s\\S]*?)</a>", RegexOptions.IgnoreCase);
            if (lastMatch.Success)
            {
                return CleanText(lastMatch.Groups[1].Value);
            }

            List<string> users = MatchAll(html, "<a[^>]*?showuser=\\d+[^>]*?>([\\s\\S]*?)</a>");
            if (users.Count > 1)
            {
                return CleanText(users[1]);
            }

            return string.Empty;
        }

        private string ExtractLastDate(string html)
        {
            string plain = CleanText(html);
            Match match = Regex.Match(plain, "Послед\\.:\\s*\\S+\\s+(.+?)(?:\\s+Куратор темы:|$)", RegexOptions.IgnoreCase);
            if (match.Success)
            {
                return match.Groups[1].Value.Trim();
            }

            return string.Empty;
        }

        private static bool LooksLikeLoginPage(string html)
        {
            if (string.IsNullOrWhiteSpace(html))
                return false;

            return html.IndexOf("name=\"login\"", StringComparison.OrdinalIgnoreCase) >= 0
                || html.IndexOf("name='login'", StringComparison.OrdinalIgnoreCase) >= 0
                || html.IndexOf("act=auth", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string UrlEncodeWindows1251(string value)
        {
            if (String.IsNullOrEmpty(value))
                return String.Empty;

            try
            {
                return UrlEncodeBytes(Encoding.GetEncoding("windows-1251").GetBytes(value));
            }
            catch
            {
                return UrlEncodeBytes(GetWindows1251BytesFallback(value));
            }
        }

        private static string UrlEncodeBytes(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0)
                return String.Empty;

            StringBuilder builder = new StringBuilder();
            for (int i = 0; i < bytes.Length; i++)
            {
                byte b = bytes[i];
                char ch = (char)b;
                if ((ch >= 'A' && ch <= 'Z')
                    || (ch >= 'a' && ch <= 'z')
                    || (ch >= '0' && ch <= '9')
                    || ch == '-' || ch == '_' || ch == '.' || ch == '*')
                {
                    builder.Append(ch);
                }
                else if (ch == ' ')
                {
                    builder.Append('+');
                }
                else
                {
                    builder.Append('%');
                    builder.Append(b.ToString("X2"));
                }
            }

            return builder.ToString();
        }

        private static byte[] GetWindows1251BytesFallback(string value)
        {
            List<byte> bytes = new List<byte>();
            for (int i = 0; i < value.Length; i++)
            {
                char ch = value[i];
                if (ch <= 0x7F)
                {
                    bytes.Add((byte)ch);
                    continue;
                }

                int mapped = MapWindows1251(ch);
                bytes.Add((byte)(mapped >= 0 ? mapped : 0x3F));
            }

            return bytes.ToArray();
        }

        private static int MapWindows1251(char ch)
        {
            if (ch >= '\u0410' && ch <= '\u042F')
                return 0xC0 + (ch - '\u0410');
            if (ch >= '\u0430' && ch <= '\u044F')
                return 0xE0 + (ch - '\u0430');
            if (ch >= '\u00A0' && ch <= '\u00BF')
                return ch;

            switch (ch)
            {
                case '\u0402': return 0x80;
                case '\u0403': return 0x81;
                case '\u201A': return 0x82;
                case '\u0453': return 0x83;
                case '\u201E': return 0x84;
                case '\u2026': return 0x85;
                case '\u2020': return 0x86;
                case '\u2021': return 0x87;
                case '\u20AC': return 0x88;
                case '\u2030': return 0x89;
                case '\u0409': return 0x8A;
                case '\u2039': return 0x8B;
                case '\u040A': return 0x8C;
                case '\u040C': return 0x8D;
                case '\u040B': return 0x8E;
                case '\u040F': return 0x8F;
                case '\u0452': return 0x90;
                case '\u2018': return 0x91;
                case '\u2019': return 0x92;
                case '\u201C': return 0x93;
                case '\u201D': return 0x94;
                case '\u2022': return 0x95;
                case '\u2013': return 0x96;
                case '\u2014': return 0x97;
                case '\u2122': return 0x99;
                case '\u0459': return 0x9A;
                case '\u203A': return 0x9B;
                case '\u045A': return 0x9C;
                case '\u045C': return 0x9D;
                case '\u045B': return 0x9E;
                case '\u045F': return 0x9F;
                case '\u040E': return 0xA1;
                case '\u045E': return 0xA2;
                case '\u0408': return 0xA3;
                case '\u0490': return 0xA5;
                case '\u0401': return 0xA8;
                case '\u0404': return 0xAA;
                case '\u0407': return 0xAF;
                case '\u0406': return 0xB2;
                case '\u0456': return 0xB3;
                case '\u0491': return 0xB4;
                case '\u0451': return 0xB8;
                case '\u0454': return 0xBA;
                case '\u0458': return 0xBC;
                case '\u0405': return 0xBD;
                case '\u0455': return 0xBE;
                case '\u0457': return 0xBF;
            }

            return -1;
        }

        private string NormalizeForumUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return string.Empty;
            }

            string normalized = WebUtility.HtmlDecode(url.Trim());
            if (normalized.StartsWith("//", StringComparison.Ordinal))
            {
                return "https:" + normalized;
            }

            if (normalized.StartsWith("/", StringComparison.Ordinal))
            {
                return RootUrl.TrimEnd('/') + normalized;
            }

            if (normalized.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || normalized.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                return normalized;
            }

            return RootUrl + normalized.TrimStart('/');
        }

        private ForumItem CreateRoot()
        {
            ForumItem root = new ForumItem();
            root.Id = -1;
            root.ParentId = -1;
            root.Level = -1;
            root.Title = "4PDA";
            return root;
        }

        private string FirstMatch(string source, string pattern, int group)
        {
            if (string.IsNullOrWhiteSpace(source))
            {
                return string.Empty;
            }

            Match match = Regex.Match(source, pattern, RegexOptions.IgnoreCase);
            if (!match.Success || group >= match.Groups.Count)
            {
                return string.Empty;
            }

            return match.Groups[group].Value;
        }

        private int ParseInt(string value)
        {
            int result;
            if (int.TryParse(CleanText(value), out result))
            {
                return result;
            }

            return 0;
        }

        private string CleanPostAuthor(string source)
        {
            string result = string.Empty;

            // Prefer the first real text node from the nickname link. The forum
            // often appends a second span/i element containing dropdown chrome.
            MatchCollection textParts = Regex.Matches(
                source ?? string.Empty,
                @"(?:^|>)([^<]+)(?=<|$)",
                RegexOptions.IgnoreCase);
            foreach (Match textPart in textParts)
            {
                string candidate = CleanText(textPart.Groups[1].Value);
                if (!string.IsNullOrWhiteSpace(candidate)
                    && !Regex.IsMatch(candidate, @"^[\p{S}\p{Co}\p{Cf}\s]+$"))
                {
                    result = candidate;
                    break;
                }
            }

            if (string.IsNullOrWhiteSpace(result))
            {
                result = CleanText(source);
            }
            if (string.IsNullOrWhiteSpace(result))
            {
                return string.Empty;
            }

            // The nickname link may append a dropdown/menu marker. On different
            // forum layouts it is represented by an arrow, a Unicode symbol, or
            // a private-use Segoe MDL2 glyph. Strip that UI chrome, not the name.
            result = result.Replace('\u00A0', ' ').Trim();
            result = Regex.Replace(result, @"[\u200B-\u200F\u202A-\u202E\u2060\uFEFF]", string.Empty);
            result = Regex.Replace(result, @"\s+[\p{S}\p{Co}\p{Cf}]+\s*$", string.Empty);
            result = Regex.Replace(
                result,
                @"\s*[\u2193\u2195\u21A1\u21A7\u2304\u2305\u25BC\u25BE\u02C5\uE70D\uE70E\uE74B]+[\uFE0F\s]*$",
                string.Empty);

            return result.Trim();
        }

        private string CleanText(string source)
        {
            if (string.IsNullOrWhiteSpace(source))
            {
                return string.Empty;
            }

            string result = source;
            result = Regex.Replace(result, "<script[\\s\\S]*?</script>", string.Empty, RegexOptions.IgnoreCase);
            result = Regex.Replace(result, "<style[\\s\\S]*?</style>", string.Empty, RegexOptions.IgnoreCase);
            result = Regex.Replace(result, "<[^>]+>", " ");
            result = WebUtility.HtmlDecode(result);
            result = Regex.Replace(result, "\\s+", " ");
            return result.Trim();
        }
    }
}
