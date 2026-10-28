using System;
using System.Collections.Generic;
using System.Net;
using System.Text.RegularExpressions;
using _4PDA.Models;
using _4PDA.Services;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Documents;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Imaging;
using Windows.UI.Xaml.Navigation;

namespace _4PDA
{
    public sealed partial class ShowNewsPage : Page
    {
        private readonly FourPdaNewsService _newsService = new FourPdaNewsService();
        private readonly List<Image> _bodyImages = new List<Image>();
        private NewsItem _item;
        private int _headerImageGeneration;
        private int _bodyRenderGeneration;

        public ShowNewsPage()
        {
            InitializeComponent();
            ArticleImage.Tapped += ArticlePhoto_Tapped;
            BodyPanel.SizeChanged += BodyPanel_SizeChanged;
        }

        protected async override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            _item = e.Parameter as NewsItem;
            ApplyHeader(_item);

            LoadingRing.IsActive = true;
            try
            {
                NewsArticle article = await _newsService.GetArticleAsync(_item);
                ApplyHeader(article);
                RenderHtml(article.Html);
                ArticleStatusTextBlock.Text = article.LoadWarning ?? string.Empty;
                ArticleStatusTextBlock.Visibility = string.IsNullOrWhiteSpace(article.LoadWarning)
                    ? Visibility.Collapsed : Visibility.Visible;
            }
            catch (Exception ex)
            {
                RenderText(ex.Message);
            }
            finally
            {
                LoadingRing.IsActive = false;
            }
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            LegacyProjects.UI.UniversalWindowsPlatform.Controls.PhotoViewer.TryCloseCurrent();
            _headerImageGeneration++;
            _bodyRenderGeneration++;
            base.OnNavigatedFrom(e);
        }

        private void ApplyHeader(NewsItem item)
        {
            if (item == null)
            {
                return;
            }

            TitleTextBlock.Text = item.Title;
            DateTextBlock.Text = item.Date;
            AuthorTextBlock.Text = item.Author;
            CommentsTextBlock.Text = item.CommentsText;
            if (!string.IsNullOrWhiteSpace(item.Html)) RenderHtml(item.Html);
            else RenderText(item.Description ?? string.Empty);
            SetImage(item.ImageUrl);
        }

        private void ApplyHeader(NewsArticle article)
        {
            if (article == null)
            {
                return;
            }

            TitleTextBlock.Text = article.Title;
            DateTextBlock.Text = article.Date;
            AuthorTextBlock.Text = article.Author;
            CommentsTextBlock.Text = article.CommentsText;
            SetImage(article.ImageUrl);
        }

        private void SetImage(string imageUrl)
        {
            ArticleImage.Visibility = string.IsNullOrWhiteSpace(imageUrl) ? Visibility.Collapsed : Visibility.Visible;
            DeferredImage.Attach(ArticleImage, imageUrl, 1000, 160);
        }

        private void RenderHtml(string html)
        {
            _bodyRenderGeneration++;
            BodyPanel.Children.Clear();
            _bodyImages.Clear();

            if (string.IsNullOrWhiteSpace(html))
            {
                RenderText("\u041D\u043E\u0432\u043E\u0441\u0442\u044C \u043D\u0435 \u0437\u0430\u0433\u0440\u0443\u0436\u0435\u043D\u0430.");
                return;
            }

            string result = html;
            result = Regex.Replace(result, "<script[\\s\\S]*?</script>", string.Empty, RegexOptions.IgnoreCase);
            result = Regex.Replace(result, "<style[\\s\\S]*?</style>", string.Empty, RegexOptions.IgnoreCase);
            result = Regex.Replace(result, "<br\\s*/?>", "\n", RegexOptions.IgnoreCase);

            Regex blockRegex = new Regex("<figure[\\s\\S]*?</figure>|<blockquote[\\s\\S]*?</blockquote>|<p[^>]*>[\\s\\S]*?</p>|<h[1-6][^>]*>[\\s\\S]*?</h[1-6]>|<li[^>]*>[\\s\\S]*?</li>|<div[^>]*class=[\"'][^\"']*(?:article-footer-item|article-footer-tags)[^\"']*[\"'][^>]*>[\\s\\S]*?</div>|<img[^>]*>", RegexOptions.IgnoreCase);
            MatchCollection blocks = blockRegex.Matches(result);
            if (blocks.Count == 0)
            {
                AddTextBlock(result, false, false, false);
                return;
            }

            int position = 0;
            foreach (Match block in blocks)
            {
                if (block.Index > position)
                {
                    AddTextBlock(result.Substring(position, block.Index - position), false, false, false);
                }

                AddHtmlBlock(block.Value);
                position = block.Index + block.Length;
            }

            if (position < result.Length)
            {
                AddTextBlock(result.Substring(position), false, false, false);
            }
        }

        private void RenderText(string text)
        {
            _bodyRenderGeneration++;
            BodyPanel.Children.Clear();
            _bodyImages.Clear();
            AddTextBlock(text, false, false, false);
        }

        private void AddHtmlBlock(string html)
        {
            if (string.IsNullOrWhiteSpace(html))
            {
                return;
            }

            string lower = html.ToLowerInvariant();
            if (lower.StartsWith("<figure"))
            {
                AddImagesFromHtml(html);
                AddTextBlock(FirstMatch(html, "<figcaption[^>]*>([\\s\\S]*?)</figcaption>", 1), false, false, true);
                return;
            }

            if (lower.StartsWith("<img"))
            {
                AddImageFromTag(html);
                return;
            }

            if (lower.StartsWith("<blockquote"))
            {
                AddTextBlock(InnerHtml(html), false, true, false);
                return;
            }

            if (lower.StartsWith("<li"))
            {
                AddTextBlock(InnerHtml(html), true, false, false);
                return;
            }

            if (lower.StartsWith("<h"))
            {
                AddTextBlock(InnerHtml(html), false, false, false);
                return;
            }

            if (html.IndexOf("<img", StringComparison.OrdinalIgnoreCase) >= 0 || html.IndexOf("<figure", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                string textOnly = Regex.Replace(html, "<figure[\\s\\S]*?</figure>|<img[^>]*>", string.Empty, RegexOptions.IgnoreCase);
                AddTextBlock(InnerHtml(textOnly), false, false, false);
                AddImagesFromHtml(html);
                AddTextBlock(FirstMatch(html, "<figcaption[^>]*>([\\s\\S]*?)</figcaption>", 1), false, false, true);
                return;
            }

            AddTextBlock(InnerHtml(html), false, false, lower.IndexOf("article-footer", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private void AddTextBlock(string html, bool bullet, bool quote, bool note)
        {
            string textProbe = CleanInlineText(html);
            if (string.IsNullOrWhiteSpace(textProbe))
            {
                return;
            }

            RichTextBlock richTextBlock = new RichTextBlock();
            richTextBlock.TextWrapping = TextWrapping.Wrap;
            richTextBlock.FontSize = note ? 14 : 17;
            richTextBlock.LineHeight = note ? 22 : 27;
            richTextBlock.Margin = new Thickness(0, note ? 4 : 8, 0, note ? 4 : 8);
            richTextBlock.Foreground = ResolveBrush(note ? "PdaSecondaryTextBrush" : "PdaTextBrush");

            Paragraph paragraph = new Paragraph();
            if (bullet)
            {
                paragraph.Inlines.Add(new Run() { Text = "- " });
            }
            else if (quote)
            {
                paragraph.Inlines.Add(new Run() { Text = "\u201C" });
            }

            AddInlines(paragraph, html);
            if (quote)
            {
                paragraph.Inlines.Add(new Run() { Text = "\u201D" });
            }

            richTextBlock.Blocks.Add(paragraph);
            BodyPanel.Children.Add(richTextBlock);
        }

        private void AddInlines(Paragraph paragraph, string html)
        {
            Regex linkRegex = new Regex("<a[^>]*?href=[\"']([^\"']+)[\"'][^>]*>([\\s\\S]*?)</a>", RegexOptions.IgnoreCase);
            int position = 0;
            foreach (Match match in linkRegex.Matches(html))
            {
                if (match.Index > position)
                {
                    AddRunWithMarkdownLinks(paragraph, html.Substring(position, match.Index - position));
                }

                string href = WebUtility.HtmlDecode(match.Groups[1].Value);
                string text = CleanInlineText(match.Groups[2].Value);
                Uri uri = NormalizeHref(href);
                if (uri != null && !string.IsNullOrWhiteSpace(text))
                {
                    Hyperlink hyperlink = new Hyperlink();
                    hyperlink.NavigateUri = uri;
                    hyperlink.Inlines.Add(new Run() { Text = text });
                    paragraph.Inlines.Add(hyperlink);
                }
                else
                {
                    AddRunWithMarkdownLinks(paragraph, text);
                }

                position = match.Index + match.Length;
            }

            if (position < html.Length)
            {
                AddRunWithMarkdownLinks(paragraph, html.Substring(position));
            }
        }

        private void AddRunWithMarkdownLinks(Paragraph paragraph, string html)
        {
            string text = CleanInlineText(html);
            if (string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            Regex markdownLinkRegex = new Regex("\\[([^\\]]+)\\]\\((https?://[^\\)]+)\\)", RegexOptions.IgnoreCase);
            int position = 0;
            foreach (Match match in markdownLinkRegex.Matches(text))
            {
                if (match.Index > position)
                {
                    paragraph.Inlines.Add(new Run() { Text = text.Substring(position, match.Index - position) });
                }

                Uri uri = NormalizeHref(match.Groups[2].Value);
                if (uri != null)
                {
                    Hyperlink hyperlink = new Hyperlink();
                    hyperlink.NavigateUri = uri;
                    hyperlink.Inlines.Add(new Run() { Text = match.Groups[1].Value });
                    paragraph.Inlines.Add(hyperlink);
                }

                position = match.Index + match.Length;
            }

            if (position < text.Length)
            {
                paragraph.Inlines.Add(new Run() { Text = text.Substring(position) });
            }
        }

        private void AddImagesFromHtml(string html)
        {
            foreach (Match match in Regex.Matches(html, "<img[^>]*>", RegexOptions.IgnoreCase))
            {
                AddImageFromTag(match.Value);
            }
        }

        private void AddImageFromTag(string imgTag)
        {
            string source = FourPdaNewsService.ExtractImageSource(imgTag);

            Uri uri = NormalizeHref(source);
            if (uri == null)
            {
                return;
            }

            Image image = new Image();
            image.Stretch = Stretch.Uniform;
            image.HorizontalAlignment = HorizontalAlignment.Stretch;
            image.Margin = new Thickness(0, 10, 0, 10);
            image.MaxHeight = 520;
            _bodyImages.Add(image);
            image.Tapped += ArticlePhoto_Tapped;
            UpdateImageWidth(image);
            BodyPanel.Children.Add(image);
            DeferredImage.Attach(image, uri.AbsoluteUri, 1000, 140);
        }

        private async void ArticlePhoto_Tapped(object sender, Windows.UI.Xaml.Input.TappedRoutedEventArgs e)
        {
            var images = new List<Image> { ArticleImage };
            images.AddRange(_bodyImages);
            e.Handled = true;
            await AppPhotoViewer.ShowImagesAsync((Image)sender, images, TitleTextBlock.Text);
        }

        private void BodyPanel_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            for (int i = 0; i < _bodyImages.Count; i++)
            {
                UpdateImageWidth(_bodyImages[i]);
            }
        }

        private void UpdateImageWidth(Image image)
        {
            if (image == null)
            {
                return;
            }

            double width = BodyPanel.ActualWidth;
            if (width <= 0 && Window.Current != null)
            {
                width = Math.Max(0, Window.Current.Bounds.Width - 32);
            }

            if (width > 0)
            {
                image.Width = width;
            }
        }

        private string InnerHtml(string html)
        {
            return Regex.Replace(html, "^<[^>]+>|</[^>]+>$", string.Empty, RegexOptions.IgnoreCase).Trim();
        }

        private string CleanInlineText(string html)
        {
            if (string.IsNullOrWhiteSpace(html))
            {
                return string.Empty;
            }

            string result = html;
            result = Regex.Replace(result, "<sup[^>]*>([\\s\\S]*?)</sup>", "[$1]", RegexOptions.IgnoreCase);
            result = Regex.Replace(result, "<br\\s*/?>", "\n", RegexOptions.IgnoreCase);
            result = Regex.Replace(result, "<[^>]+>", " ", RegexOptions.IgnoreCase);
            result = WebUtility.HtmlDecode(result);
            result = Regex.Replace(result, "[ \\t\\x0B\\f\\r]+", " ");
            result = Regex.Replace(result, "\\s+\\n", "\n");
            result = Regex.Replace(result, "\\n{3,}", "\n\n");
            return result.Trim();
        }

        private Uri NormalizeHref(string href)
        {
            if (string.IsNullOrWhiteSpace(href))
            {
                return null;
            }

            string result = WebUtility.HtmlDecode(href.Trim());
            if (result.StartsWith("#"))
            {
                return null;
            }

            if (result.StartsWith("//"))
            {
                result = "https:" + result;
            }
            else if (result.StartsWith("/"))
            {
                result = "https://4pda.to" + result;
            }

            Uri uri;
            if (Uri.TryCreate(result, UriKind.Absolute, out uri))
            {
                return uri;
            }

            return null;
        }

        private Brush ResolveBrush(string key)
        {
            object value = Application.Current.Resources[key];
            Brush brush = value as Brush;
            if (brush != null)
            {
                return brush;
            }

            return null;
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
    }
}
