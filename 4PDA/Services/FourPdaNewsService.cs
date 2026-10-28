using System;
using System.Collections.Generic;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using _4PDA.Models;
using Windows.Web.Http;

namespace _4PDA.Services
{
    public sealed class FourPdaNewsService
    {
        private const string RootUrl = "https://4pda.to/";

        public string LastLoadWarning { get; private set; }

        public async Task<List<NewsItem>> GetNewsAsync(int pageNumber)
        {
            LastLoadWarning = string.Empty;
            try { return await OfficialApiService.GetNewsAsync(pageNumber); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine("[4PDA.API.News] " + ex.Message); }
            string url = pageNumber <= 1 ? RootUrl : RootUrl + "page/" + pageNumber + "/";
            string html;
            try
            {
                html = await LoadStringAsync(url);
            }
            catch (Exception ex)
            {
                LastLoadWarning = "Загружена только RSS-лента без полных статей и изображений. " + ex.Message;
                return ParseFeed(await LoadStringAsync(RootUrl + "feed/"));
            }

            List<NewsItem> result = ParseNewsList(html);

            if (result.Count == 0)
            {
                LastLoadWarning = "Не удалось разобрать страницу новостей. Загружена резервная RSS-лента.";
                result = ParseFeed(await LoadStringAsync(RootUrl + "feed/"));
            }

            return result;
        }

        public async Task EnrichNewsItemAsync(NewsItem item)
        {
            if (item == null || string.IsNullOrWhiteSpace(item.Url))
            {
                return;
            }

            NewsArticle article = await GetArticleAsync(item);
            if (string.IsNullOrWhiteSpace(item.ImageUrl))
            {
                item.ImageUrl = article.ImageUrl;
            }

            if (string.IsNullOrWhiteSpace(item.Html))
            {
                item.Html = article.Html;
            }

            if (string.IsNullOrWhiteSpace(item.Author))
            {
                item.Author = article.Author;
            }

            if (item.CommentsCount == 0)
            {
                item.CommentsCount = article.CommentsCount;
            }
        }

        public async Task<NewsArticle> GetArticleAsync(NewsItem item)
        {
            int id = item != null && item.Id > 0 ? item.Id : ExtractArticleId(item == null ? string.Empty : item.Url);
            if (id > 0)
            {
                try { return await OfficialApiService.GetArticleAsync(id); }
                catch (Exception ex) { System.Diagnostics.Debug.WriteLine("[4PDA.API.Article] " + ex.Message); }
            }
            string url = item != null && !string.IsNullOrWhiteSpace(item.Url)
                ? item.Url
                : RootUrl + "index.php?p=" + (item != null ? item.Id : 0);

            NewsArticle article;
            try
            {
                string html = await LoadStringAsync(url);
                article = ParseArticle(html);
            }
            catch (Exception ex)
            {
                article = CreateArticleFromItem(item);
                article.LoadWarning = "Полная статья не загружена. " + ex.Message;
            }

            if (item != null)
            {
                article.Id = article.Id == 0 ? item.Id : article.Id;
                article.Url = string.IsNullOrWhiteSpace(article.Url) ? item.Url : article.Url;
                article.Title = string.IsNullOrWhiteSpace(article.Title) ? item.Title : article.Title;
                article.Date = string.IsNullOrWhiteSpace(article.Date) ? item.Date : article.Date;
                article.Author = string.IsNullOrWhiteSpace(article.Author) ? item.Author : article.Author;
                article.ImageUrl = string.IsNullOrWhiteSpace(article.ImageUrl) ? item.ImageUrl : article.ImageUrl;
                article.CommentsCount = article.CommentsCount == 0 ? item.CommentsCount : article.CommentsCount;
            }

            return article;
        }

        private NewsArticle CreateArticleFromItem(NewsItem item)
        {
            NewsArticle article = new NewsArticle();
            if (item == null)
            {
                return article;
            }

            article.Id = item.Id;
            article.Url = item.Url;
            article.Title = item.Title;
            article.Date = item.Date;
            article.Author = item.Author;
            article.ImageUrl = item.ImageUrl;
            article.CommentsCount = item.CommentsCount;
            article.Html = !string.IsNullOrWhiteSpace(item.Html)
                ? NormalizeArticleHtml(item.Html)
                : WebUtility.HtmlEncode(item.Description);
            return article;
        }

        private async Task<string> LoadStringAsync(string url)
        {
            using (HttpClient client = new HttpClient())
            {
                Uri requestUri = new Uri(url, UriKind.Absolute);
                using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, requestUri))
                {
                    ForumCookieHelper.ApplyDefaultHeaders(request);
                    ForumCookieHelper.TryAppendHeader(request, "Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
                    ForumCookieHelper.TryAppendHeader(request, "Referer", RootUrl);
                    ForumCookieHelper.SaveForumCookiesFromSystemCookieManager("FourPdaNewsService.LoadStringAsync");
                    ForumCookieHelper.TryAppendHeader(request, "Cookie", ForumCookieHelper.GetCookieHeaderForUrl(requestUri));
                    using (HttpResponseMessage response = await client.SendRequestAsync(request))
                        return await FourPdaResponseHelper.ReadHtmlAsync(response);
                }
            }
        }

        private List<NewsItem> ParseNewsList(string html)
        {
            List<NewsItem> items = new List<NewsItem>();
            if (string.IsNullOrWhiteSpace(html))
            {
                return items;
            }

            Regex articleRegex = new Regex("<article[^>]*?class=\"post[^\"]*?\"[^>]*?>([\\s\\S]*?)</article>", RegexOptions.IgnoreCase);
            MatchCollection matches = articleRegex.Matches(html);
            foreach (Match match in matches)
            {
                string block = match.Groups[1].Value;
                string url = FirstMatch(block, "<a[^>]*?href=\"([^\"]*?/(\\d+)/?[^\"]*)\"[^>]*?(?:title=\"([^\"]*)\")?", 1);
                string idText = FirstMatch(url, "/(\\d+)/?$", 1);
                string title = FirstMatch(block, "<a[^>]*?title=\"([^\"]+)\"", 1);
                if (string.IsNullOrWhiteSpace(title))
                {
                    title = FirstMatch(block, "<h\\d[^>]*?>[\\s\\S]*?<a[^>]*?>([\\s\\S]*?)</a>", 1);
                }

                string image = ExtractImageSource(block);
                string comments = FirstMatch(block, "(?:v-count|comments?)[^>]*>\\s*(\\d+)\\s*<", 1);
                string date = FirstMatch(block, "<em[^>]*?class=\"date\"[^>]*>([\\s\\S]*?)</em>", 1);
                if (string.IsNullOrWhiteSpace(date))
                {
                    date = FirstMatch(block, "<time[^>]*?(?:datetime=\"([^\"]*)\")?[^>]*>([\\s\\S]*?)</time>", 2);
                }

                string author = FirstMatch(block, "showuser=(\\d+)[^\"]*\"[^>]*>([\\s\\S]*?)</a>", 2);
                string authorIdText = FirstMatch(block, "showuser=(\\d+)", 1);
                string description = FirstMatch(block, "itemprop=\"description\"[^>]*>([\\s\\S]*?)</div>", 1);

                if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(url))
                {
                    continue;
                }

                NewsItem item = new NewsItem();
                item.Url = NormalizeUrl(url);
                item.Id = ExtractArticleId(item.Url);
                if (item.Id == 0)
                {
                    item.Id = ParseInt(idText);
                }

                item.Title = CleanText(title);
                item.ImageUrl = NormalizeUrl(image);
                item.CommentsCount = ParseInt(comments);
                item.Date = CleanText(date);
                item.AuthorId = ParseInt(authorIdText);
                item.Author = CleanText(author);
                item.Description = CleanText(description);
                items.Add(item);
            }

            return items;
        }

        private List<NewsItem> ParseFeed(string xml)
        {
            List<NewsItem> items = new List<NewsItem>();
            if (string.IsNullOrWhiteSpace(xml))
            {
                return items;
            }

            Regex itemRegex = new Regex("<item[\\s\\S]*?</item>", RegexOptions.IgnoreCase);
            foreach (Match match in itemRegex.Matches(xml))
            {
                string block = match.Value;
                string encodedContent = UnwrapCdata(FirstMatch(block, "<content:encoded[^>]*>([\\s\\S]*?)</content:encoded>", 1));
                string description = UnwrapCdata(FirstMatch(block, "<description[^>]*>([\\s\\S]*?)</description>", 1));
                string comments = CleanText(FirstMatch(block, "<slash:comments[^>]*>([\\s\\S]*?)</slash:comments>", 1));

                NewsItem item = new NewsItem();
                item.Title = CleanText(UnwrapCdata(FirstMatch(block, "<title>([\\s\\S]*?)</title>", 1)));
                item.Url = NormalizeUrl(CleanText(FirstMatch(block, "<link>([\\s\\S]*?)</link>", 1)));
                item.Date = CleanText(FirstMatch(block, "<pubDate>([\\s\\S]*?)</pubDate>", 1));
                item.Author = CleanText(UnwrapCdata(FirstMatch(block, "<dc:creator[^>]*>([\\s\\S]*?)</dc:creator>", 1)));
                item.Description = CleanText(description);
                item.Html = encodedContent;
                item.ImageUrl = NormalizeUrl(ExtractImageSource(encodedContent + description));
                if (string.IsNullOrWhiteSpace(item.ImageUrl))
                {
                    foreach (Match media in Regex.Matches(block, @"<(?:media:content|media:thumbnail|enclosure)\b[^>]*>", RegexOptions.IgnoreCase))
                    {
                        string type = GetAttribute(media.Value, "type");
                        if (media.Value.StartsWith("<enclosure", StringComparison.OrdinalIgnoreCase)
                            && !type.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
                            continue;
                        item.ImageUrl = NormalizeUrl(GetAttribute(media.Value, "url"));
                        if (!string.IsNullOrWhiteSpace(item.ImageUrl))
                            break;
                    }
                }
                item.Id = ExtractArticleId(item.Url);
                item.CommentsCount = ParseInt(comments);
                if (!string.IsNullOrWhiteSpace(item.Title))
                {
                    items.Add(item);
                }
            }

            return items;
        }

        private NewsArticle ParseArticle(string html)
        {
            NewsArticle article = new NewsArticle();
            if (string.IsNullOrWhiteSpace(html))
            {
                return article;
            }

            article.Id = ParseInt(FirstMatch(html, "data-ztm=\"\\d+:(\\d+)[^\"]*\"", 1));
            if (article.Id == 0)
            {
                article.Id = ParseInt(FirstMatch(html, "itemid=\"(\\d+)\"", 1));
            }

            article.ImageUrl = NormalizeUrl(GetMetaContent(html, "og:image"));
            if (string.IsNullOrWhiteSpace(article.ImageUrl))
            {
                article.ImageUrl = NormalizeUrl(GetMetaContent(html, "twitter:image"));
            }

            if (string.IsNullOrWhiteSpace(article.ImageUrl))
            {
                article.ImageUrl = NormalizeUrl(FirstMatch(html, "<link[^>]*?rel=\"image_src\"[^>]*?href=\"([^\"]*)\"", 1));
            }

            article.Title = CleanText(FirstMatch(html, "<meta[^>]*?property=\"og:title\"[^>]*?content=\"([^\"]*)\"", 1));
            if (string.IsNullOrWhiteSpace(article.Title))
            {
                article.Title = CleanText(FirstMatch(html, "<h1[^>]*>([\\s\\S]*?)</h1>", 1));
            }

            article.Date = CleanText(FirstMatch(html, "<time[^>]*>([\\s\\S]*?)</time>", 1));
            if (string.IsNullOrWhiteSpace(article.Date))
            {
                article.Date = CleanText(FirstMatch(html, "<em[^>]*?class=\"date\"[^>]*>([\\s\\S]*?)</em>", 1));
            }

            article.Author = CleanText(FirstMatch(html, "class=\"name\"[\\s\\S]*?<a[^>]*>([\\s\\S]*?)</a>", 1));
            if (string.IsNullOrWhiteSpace(article.Author))
            {
                article.Author = "News";
            }

            article.CommentsCount = ParseInt(FirstMatch(html, "article-meta-comment\"[\\s\\S]*?<a[^>]*>(\\d+)</a>", 1));
            if (article.CommentsCount == 0)
            {
                article.CommentsCount = ParseInt(FirstMatch(html, "href=\"#comments\"[^>]*>(\\d+)</a>", 1));
            }

            string body = FirstMatch(html, "<div[^>]*?class=\"content-box\"[^>]*?itemprop=\"articleBody\"[^>]*?>([\\s\\S]*?)</div>\\s*</div>\\s*</div>", 1);
            if (string.IsNullOrWhiteSpace(body))
            {
                body = FirstMatch(html, "<div class=\"article\">([\\s\\S]*?)<div class=\"article-footer\"", 1);
                body = PrepareModernArticleBody(body);
                string footer = ExtractArticleFooter(html);
                if (!string.IsNullOrWhiteSpace(footer))
                {
                    body = body + footer;
                }
            }

            if (string.IsNullOrWhiteSpace(body))
            {
                body = FirstMatch(html, "<meta property=\"og:description\"[\\s\\S]*?>([\\s\\S]*?)<div class=\"article-footer", 1);
            }

            if (string.IsNullOrWhiteSpace(body))
            {
                body = CleanText(FirstMatch(html, "<meta[^>]*?property=\"og:description\"[^>]*?content=\"([^\"]*)\"", 1));
            }

            article.Html = NormalizeArticleHtml(body);
            return article;
        }

        private string NormalizeArticleHtml(string html)
        {
            if (string.IsNullOrWhiteSpace(html))
            {
                return string.Empty;
            }

            string result = html;
            result = Regex.Replace(result, "src=\"//", "src=\"https://", RegexOptions.IgnoreCase);
            result = Regex.Replace(result, "href=\"//", "href=\"https://", RegexOptions.IgnoreCase);
            result = Regex.Replace(result, "src=\"/", "src=\"" + RootUrl, RegexOptions.IgnoreCase);
            result = Regex.Replace(result, "href=\"/", "href=\"" + RootUrl, RegexOptions.IgnoreCase);
            result = Regex.Replace(result, "<script[\\s\\S]*?</script>", string.Empty, RegexOptions.IgnoreCase);
            result = Regex.Replace(result, "<style[\\s\\S]*?</style>", string.Empty, RegexOptions.IgnoreCase);
            return result;
        }

        private string ExtractArticleFooter(string html)
        {
            if (string.IsNullOrWhiteSpace(html))
            {
                return string.Empty;
            }

            string result = string.Empty;
            MatchCollection footerItems = Regex.Matches(html, "<div[^>]*class=\"[^\"]*(?:article-footer-item|article-footer-tags)[^\"]*\"[^>]*>([\\s\\S]*?)</div>", RegexOptions.IgnoreCase);
            foreach (Match match in footerItems)
            {
                result += "<div class=\"article-footer-item\">" + match.Groups[1].Value + "</div>";
            }

            return result;
        }

        private string PrepareModernArticleBody(string html)
        {
            if (string.IsNullOrWhiteSpace(html))
            {
                return string.Empty;
            }

            string intro = FirstMatch(html, "<div class=\"article-anons\">[\\s\\S]*?<p>([\\s\\S]*?)</p>", 1);
            int moreIndex = html.IndexOf("<!--more-->", StringComparison.OrdinalIgnoreCase);
            string result = moreIndex >= 0 ? html.Substring(moreIndex + "<!--more-->".Length) : html;

            if (!string.IsNullOrWhiteSpace(intro))
            {
                result = "<p>" + intro + "</p>" + result;
            }

            result = Regex.Replace(result, "<h1[\\s\\S]*?</h1>", string.Empty, RegexOptions.IgnoreCase);
            result = Regex.Replace(result, "<div class=\"article-header\"[\\s\\S]*?</div>\\s*</div>", string.Empty, RegexOptions.IgnoreCase);
            result = Regex.Replace(result, "<div class=\"article-meta\"[\\s\\S]*?</div>", string.Empty, RegexOptions.IgnoreCase);
            result = Regex.Replace(result, "<meta[^>]*?property=\"og:description\"[^>]*>", string.Empty, RegexOptions.IgnoreCase);
            result = Regex.Replace(result, "<!--more-->", string.Empty, RegexOptions.IgnoreCase);
            result = Regex.Replace(result, "^\\s*</p>", string.Empty, RegexOptions.IgnoreCase);
            return result;
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

        internal static string GetAttribute(string tag, string name)
        {
            Match match = Regex.Match(tag ?? string.Empty,
                @"(?:^|\s)" + Regex.Escape(name) + @"\s*=\s*([""'])(.*?)\1", RegexOptions.IgnoreCase);
            return match.Success ? WebUtility.HtmlDecode(match.Groups[2].Value) : string.Empty;
        }

        internal static string ExtractImageSource(string html)
        {
            foreach (Match image in Regex.Matches(html ?? string.Empty, @"<img\b[^>]*>", RegexOptions.IgnoreCase))
            {
                // src часто содержит прозрачную заглушку перед data-src.
                foreach (string name in new string[] { "data-src", "data-original", "src", "data-srcset", "srcset" })
                {
                    string value = GetAttribute(image.Value, name);
                    if (name.EndsWith("srcset", StringComparison.Ordinal))
                        value = Regex.Match(value, @"^\s*([^\s,]+)").Groups[1].Value;
                    if (!string.IsNullOrWhiteSpace(value) && !value.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                        return value;
                }
            }
            return string.Empty;
        }

        private static string GetMetaContent(string html, string name)
        {
            foreach (Match meta in Regex.Matches(html ?? string.Empty, @"<meta\b[^>]*>", RegexOptions.IgnoreCase))
            {
                if (string.Equals(GetAttribute(meta.Value, "property"), name, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(GetAttribute(meta.Value, "name"), name, StringComparison.OrdinalIgnoreCase))
                    return GetAttribute(meta.Value, "content");
            }
            return string.Empty;
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

        private int ExtractArticleId(string url)
        {
            string id = FirstMatch(url, "/(\\d+)/[^/]+/?(?:\\?.*)?$", 1);
            if (string.IsNullOrWhiteSpace(id))
            {
                id = FirstMatch(url, "[?&]p=(\\d+)", 1);
            }

            return ParseInt(id);
        }

        private string NormalizeUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return string.Empty;
            }

            string result = WebUtility.HtmlDecode(url.Trim());
            if (result.StartsWith("//"))
            {
                return "https:" + result;
            }

            if (result.StartsWith("/"))
            {
                return RootUrl.TrimEnd('/') + result;
            }

            return result;
        }

        private string UnwrapCdata(string source)
        {
            if (string.IsNullOrWhiteSpace(source))
            {
                return string.Empty;
            }

            string result = source.Trim();
            if (result.StartsWith("<![CDATA[", StringComparison.OrdinalIgnoreCase) && result.EndsWith("]]>", StringComparison.OrdinalIgnoreCase))
            {
                result = result.Substring(9, result.Length - 12);
            }

            return WebUtility.HtmlDecode(result);
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
