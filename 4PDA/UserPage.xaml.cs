using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using _4PDA.Services;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media.Imaging;
using Windows.UI.Xaml.Navigation;
using Windows.Data.Html;
using Windows.Web.Http;
using Windows.Storage.Streams;

namespace _4PDA
{
    public sealed partial class UserPage : Page
    {
        private const string ForumBaseUrl = "https://4pda.to/forum/index.php";

        private readonly ObservableCollection<ProfileRow> _personalRows = new ObservableCollection<ProfileRow>();
        private readonly ObservableCollection<ProfileRow> _statisticsRows = new ObservableCollection<ProfileRow>();
        private readonly ObservableCollection<DeviceRow> _deviceRows = new ObservableCollection<DeviceRow>();
        private readonly HttpClient _httpClient = new HttpClient();
        private bool _loading;
        private string _openedUserId = "";
        private bool _isOwnProfile = true;
        private bool _pageMessageDialogOpen;

        public UserPage()
        {
            this.InitializeComponent();
            PersonalRowsItemsControl.ItemsSource = _personalRows;
            StatisticsRowsItemsControl.ItemsSource = _statisticsRows;
            DeviceRowsItemsControl.ItemsSource = _deviceRows;
            ForumCookieHelper.ApplyDefaultHeaders(_httpClient);
            UpdateResponsiveLayout(Window.Current.Bounds.Width);
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            _openedUserId = ExtractUserId(e.Parameter as string);
            if (!ForumAuthService.IsAuthorized && String.IsNullOrWhiteSpace(_openedUserId))
            {
                Frame.Navigate(typeof(LoginPage));
                return;
            }

            string currentUserId = OnlyDigits(ForumAuthService.CurrentUserId);
            _isOwnProfile = ForumAuthService.IsAuthorized &&
                (String.IsNullOrWhiteSpace(_openedUserId) || (!String.IsNullOrWhiteSpace(currentUserId) && String.Equals(_openedUserId, currentUserId, StringComparison.OrdinalIgnoreCase)));

            await LoadProfileAsync();
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);
        }


        private async void RefreshAppBarButton_Click(object sender, RoutedEventArgs e)
        {
            await LoadProfileAsync();
        }

        private async void LogoutButton_Click(object sender, RoutedEventArgs e)
        {
            await LogoutAndBackAsync();
        }

        private void DeviceRowsItemsControl_ItemClick(object sender, ItemClickEventArgs e)
        {
            DeviceRow device = e.ClickedItem as DeviceRow;
            if (device == null)
                return;

            AppNavigationActions.OpenDevice(new DeviceNavigationArgs
            {
                DeviceId = ExtractDeviceId(device.Url),
                Name = device.Name,
                Accessory = device.Accessory,
                Url = device.Url
            });
        }

        private void UserPage_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdateResponsiveLayout(e.NewSize.Width);
        }



        private async Task LoadProfileAsync()
        {
            if (_loading)
                return;

            _loading = true;
            SetProfileLoading(true);
            SetBusy(true);
            SetPageStatus("Загружаем профиль...");

            try
            {
                ForumUserProfile profile;
                if (_isOwnProfile && !String.IsNullOrWhiteSpace(OnlyDigits(ForumAuthService.CurrentUserId)))
                    profile = await LoadUserProfileAsync(ForumAuthService.CurrentUserId);
                else if (_isOwnProfile)
                    profile = await ForumAuthService.GetCurrentUserProfileAsync();
                else
                    profile = await LoadUserProfileAsync(_openedUserId);

                await ApplyProfileAsync(profile);
                SetPageStatus(_isOwnProfile ? "" : "Открыт чужой профиль. Редактирование и выход из аккаунта недоступны.");
            }
            catch (Exception ex)
            {
                NickTextBlock.Text = _isOwnProfile ? ForumAuthService.CurrentUserLogin : "пользователь";
                IdTextBlock.Text = "ID: " + (_isOwnProfile ? ForumAuthService.CurrentUserId : _openedUserId);
                SetPageStatus("Не удалось загрузить профиль: " + ex.Message);
            }
            finally
            {
                SetBusy(false);
                SetProfileLoading(false);
                _loading = false;
            }
        }

        private async Task<ForumUserProfile> LoadUserProfileAsync(string userId)
        {
            return await OfficialApiService.GetProfileAsync(userId);
        }

        private async Task<ForumUserProfile> LoadUserProfileFromHtmlAsync(string userId)
        {
            string id = OnlyDigits(userId);
            if (String.IsNullOrWhiteSpace(id))
                throw new InvalidOperationException("не указан ID пользователя");

            ForumCookieHelper.SaveForumCookiesFromSystemCookieManager("UserPage.LoadUserProfile");

            string url = ForumBaseUrl + "?showuser=" + Uri.EscapeDataString(id);
            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, new Uri(url, UriKind.Absolute));
            PrepareForumRequest(request, ForumBaseUrl + "?showuser=" + Uri.EscapeDataString(id));

            HttpResponseMessage response = await _httpClient.SendRequestAsync(request);
            string html = await FourPdaResponseHelper.ReadHtmlAsync(response);

            return ParseUserProfile(html, id);
        }

        private void PrepareForumRequest(HttpRequestMessage request, string referer)
        {
            ForumCookieHelper.ApplyDefaultHeaders(request);
            if (!String.IsNullOrWhiteSpace(referer))
                ForumCookieHelper.TryAppendHeader(request, "Referer", referer);

            string cookieHeader = ForumCookieHelper.GetCookieHeaderForUrl(request.RequestUri);
            if (!String.IsNullOrWhiteSpace(cookieHeader))
                ForumCookieHelper.TryAppendHeader(request, "Cookie", cookieHeader);
        }

        private static ForumUserProfile ParseUserProfile(string html, string fallbackUserId)
        {
            string source = html ?? "";
            ForumUserProfile profile = new ForumUserProfile();
            profile.Id = fallbackUserId;

            bool parsedByForpdaPattern = ParseProfileByForpdaPattern(source, profile);

            string profileBlock = ExtractProfileBlock(source);
            string parseSource = String.IsNullOrWhiteSpace(profileBlock) ? source : profileBlock;
            string plain = HtmlToProfileText(parseSource);

            if (String.IsNullOrWhiteSpace(profile.Nick))
                profile.Nick = ExtractProfileNick(source, plain, fallbackUserId);
            if (String.IsNullOrWhiteSpace(profile.AvatarUrl))
                profile.AvatarUrl = ExtractAvatarUrl(source);
            if (String.IsNullOrWhiteSpace(profile.Group))
                profile.Group = FirstNonEmpty(
                    ExtractProfileField(plain, new string[] { "Группа", "Группа пользователя" }),
                    ExtractClassText(source, new string[] { "group", "member-group", "user-group" }));
            if (String.IsNullOrWhiteSpace(profile.Status))
                profile.Status = FirstNonEmpty(
                    ExtractProfileField(plain, new string[] { "Статус", "Личный статус" }),
                    ExtractClassText(source, new string[] { "member_title", "member-title", "status", "user-status" }));
            if (String.IsNullOrWhiteSpace(profile.RegistrationDate))
                profile.RegistrationDate = ExtractProfileField(plain, new string[] { "Регистрация", "Зарегистрирован", "Дата регистрации", "Рег." });
            if (String.IsNullOrWhiteSpace(profile.LastVisit))
                profile.LastVisit = ExtractProfileField(plain, new string[] { "Последний визит", "Последнее посещение", "Последнее", "Был", "Активность" });
            if (String.IsNullOrWhiteSpace(profile.Birthday))
                profile.Birthday = ExtractProfileField(plain, new string[] { "День рождения", "Дата рождения", "Дата" });
            if (String.IsNullOrWhiteSpace(profile.City))
                profile.City = ExtractProfileField(plain, new string[] { "Город", "Откуда" });
            if (String.IsNullOrWhiteSpace(profile.UserTime))
                profile.UserTime = ExtractProfileField(plain, new string[] { "Время пользователя", "Местное время", "Время" });
            if (String.IsNullOrWhiteSpace(profile.Gender))
                profile.Gender = ExtractProfileField(plain, new string[] { "Пол" });
            if (String.IsNullOrWhiteSpace(profile.Reputation))
                profile.Reputation = ExtractProfileField(plain, new string[] { "Репутация", "Репутации", "Репу" });
            if (String.IsNullOrWhiteSpace(profile.ForumPosts))
                profile.ForumPosts = ExtractProfileField(plain, new string[] { "Посты форума", "Постов форума", "Сообщений", "Сообщения", "Постов" });
            if (String.IsNullOrWhiteSpace(profile.ForumTopics))
                profile.ForumTopics = ExtractProfileField(plain, new string[] { "Темы форума", "Тем", "Темы" });
            if (String.IsNullOrWhiteSpace(profile.SiteKarma))
                profile.SiteKarma = ExtractProfileField(plain, new string[] { "Карма сайта", "Карма" });
            if (String.IsNullOrWhiteSpace(profile.SitePosts))
                profile.SitePosts = ExtractProfileField(plain, new string[] { "Посты сайта", "Постов сайта" });
            if (String.IsNullOrWhiteSpace(profile.SiteComments))
                profile.SiteComments = ExtractProfileField(plain, new string[] { "Комментарии сайта", "Комментов", "Комментарии" });
            if (String.IsNullOrWhiteSpace(profile.Signature))
                profile.Signature = ExtractProfileSection(source, new string[] { "u-note", "signature", "user-signature", "sign" }, new string[] { "Нет подписи" });
            if (String.IsNullOrWhiteSpace(profile.About))
                profile.About = FirstNonEmpty(
                    ExtractProfileSection(source, new string[] { "div-custom-about", "about", "user-about", "profile-about" }, null),
                    ExtractProfileField(plain, new string[] { "О себе", "Информация", "Обо мне" }));
            if (!parsedByForpdaPattern && HasNoVisibleProfileInfo(profile))
                profile.About = BuildProfileFallbackText(parseSource, profile.Nick, fallbackUserId);

            return profile;
        }

        private static bool ParseProfileByForpdaPattern(string source, ForumUserProfile profile)
        {
            if (profile == null || String.IsNullOrWhiteSpace(source))
                return false;

            string mainPattern = @"<div[^>]*?user-box[\s\S]*?<img[^>]*?src\s*=\s*['"" ](?<avatar>[^'"" >]*?)['"" ][\s\S]*?<h1[^>]*>(?<nick>[\s\S]*?)<\/h1>[\s\S]*?(?:<span[^>]*?class\s*=\s*['""][^'""<>]*?title[^'""<>]*?['""][^>]*>(?<status>[\s\S]*?)<\/span>|)[\s\S]*?<h2[^>]*>(?:<span[^>]*>|)(?<group>[\s\S]*?)(?:<\/span>|)<\/h2>[\s\S]*?(?<info><ul[\s\S]*?<\/ul>)[\s\S]*?<div[^>]*?class\s*=\s*['""][^'""<>]*?u-note[^'""<>]*?['""][^>]*>(?<sign>[\s\S]*?)<\/div>[\s\S]*?(?<personal><ul[\s\S]*?<\/ul>)[\s\S]*?(?<contacts><ul[\s\S]*?<\/ul>)[\s\S]*?(?<devices><ul[\s\S]*?<\/ul>)[\s\S]*?(?<site><ul[\s\S]*?<\/ul>)[\s\S]*?(?<forum><ul[\s\S]*?<\/ul>)";
            Match main = Regex.Match(source, mainPattern, RegexOptions.IgnoreCase | RegexOptions.Singleline);
            if (!main.Success)
                return false;

            profile.AvatarUrl = NormalizeForumUrl(main.Groups["avatar"].Value);
            profile.Nick = CleanProfileText(main.Groups["nick"].Value);
            profile.Status = CleanProfileText(main.Groups["status"].Value);
            profile.Group = CleanProfileText(main.Groups["group"].Value);

            ParseInfoList(main.Groups["info"].Value, profile);
            ParsePersonalList(main.Groups["personal"].Value, profile);
            ParseDevicesList(main.Groups["devices"].Value, profile);
            ParseSiteStatsList(main.Groups["site"].Value, profile);
            ParseForumStatsList(main.Groups["forum"].Value, profile);

            string sign = CleanProfileText(main.Groups["sign"].Value);
            if (!String.IsNullOrWhiteSpace(sign) && sign.IndexOf("Нет подписи", StringComparison.OrdinalIgnoreCase) < 0)
                profile.Signature = sign;

            profile.About = ExtractProfileSection(source, new string[] { "div-custom-about" }, null);

            return true;
        }

        private static void ParseInfoList(string html, ForumUserProfile profile)
        {
            foreach (string item in ExtractListItems(html))
            {
                string title = ExtractListItemTitle(item);
                string value = ExtractListItemArea(item);
                if (String.IsNullOrWhiteSpace(title) || String.IsNullOrWhiteSpace(value))
                    continue;

                if (title.IndexOf("Рег", StringComparison.OrdinalIgnoreCase) >= 0)
                    profile.RegistrationDate = value;
                else if (title.IndexOf("Послед", StringComparison.OrdinalIgnoreCase) >= 0)
                    profile.LastVisit = value;
            }
        }

        private static void ParsePersonalList(string html, ForumUserProfile profile)
        {
            foreach (string item in ExtractListItems(html))
            {
                string title = ExtractListItemTitle(item);
                string value = ExtractListItemArea(item);
                if (String.IsNullOrWhiteSpace(title))
                    continue;

                if (String.IsNullOrWhiteSpace(value))
                {
                    if (String.IsNullOrWhiteSpace(profile.Gender) && !LooksLikeProfileLabel(title, null))
                        profile.Gender = title;
                    continue;
                }

                if (title.IndexOf("Дата", StringComparison.OrdinalIgnoreCase) >= 0 || title.IndexOf("рожд", StringComparison.OrdinalIgnoreCase) >= 0)
                    profile.Birthday = value;
                else if (title.IndexOf("Время", StringComparison.OrdinalIgnoreCase) >= 0)
                    profile.UserTime = value;
                else if (title.IndexOf("Город", StringComparison.OrdinalIgnoreCase) >= 0 || title.IndexOf("Откуда", StringComparison.OrdinalIgnoreCase) >= 0)
                    profile.City = value;
                else if (title.IndexOf("Пол", StringComparison.OrdinalIgnoreCase) >= 0)
                    profile.Gender = value;
            }
        }

        private static void ParseDevicesList(string html, ForumUserProfile profile)
        {
            foreach (string item in ExtractListItems(html))
            {
                string anchor = FirstMatch(item, @"(?<v><a\b[^>]*>[\s\S]*?<\/a>)");
                string name = CleanProfileText(FirstMatch(anchor, @"<a\b[^>]*>(?<v>[\s\S]*?)<\/a>"));
                if (String.IsNullOrWhiteSpace(name))
                    continue;

                string href = FirstMatch(anchor, @"\bhref\s*=\s*['""](?<v>[^'""<>]+)['""]");
                string accessory = Regex.Replace(item, @"[\s\S]*?<\/a>", " ", RegexOptions.IgnoreCase | RegexOptions.Singleline);
                accessory = Regex.Replace(accessory, @"<\/li>[\s\S]*$", " ", RegexOptions.IgnoreCase | RegexOptions.Singleline);
                accessory = CleanProfileText(accessory);

                AddParsedDevice(profile, name, accessory, NormalizeDeviceProfileUrl(href));
            }
        }

        private static void ParseSiteStatsList(string html, ForumUserProfile profile)
        {
            foreach (string item in ExtractListItems(html))
            {
                string title = ExtractListItemTitle(item);
                string value = ExtractListItemArea(item);
                if (String.IsNullOrWhiteSpace(title) || String.IsNullOrWhiteSpace(value))
                    continue;

                if (title.IndexOf("Карма", StringComparison.OrdinalIgnoreCase) >= 0)
                    profile.SiteKarma = value;
                else if (title.IndexOf("Пост", StringComparison.OrdinalIgnoreCase) >= 0)
                    profile.SitePosts = value;
                else if (title.IndexOf("Коммент", StringComparison.OrdinalIgnoreCase) >= 0)
                    profile.SiteComments = value;
            }
        }

        private static void ParseForumStatsList(string html, ForumUserProfile profile)
        {
            foreach (string item in ExtractListItems(html))
            {
                string title = ExtractListItemTitle(item);
                string value = ExtractListItemArea(item);
                value = ExtractBestNumber(value);
                if (String.IsNullOrWhiteSpace(title) || String.IsNullOrWhiteSpace(value))
                    continue;

                if (title.IndexOf("Реп", StringComparison.OrdinalIgnoreCase) >= 0)
                    profile.Reputation = value;
                else if (title.IndexOf("Тем", StringComparison.OrdinalIgnoreCase) >= 0)
                    profile.ForumTopics = value;
                else if (title.IndexOf("Пост", StringComparison.OrdinalIgnoreCase) >= 0 || title.IndexOf("Сообщ", StringComparison.OrdinalIgnoreCase) >= 0)
                    profile.ForumPosts = value;
            }
        }

        private static string[] ExtractListItems(string html)
        {
            List<string> result = new List<string>();
            MatchCollection matches = Regex.Matches(html ?? "", @"<li\b[^>]*>[\s\S]*?<\/li>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            foreach (Match match in matches)
                result.Add(match.Value);
            return result.ToArray();
        }

        private static string ExtractListItemTitle(string itemHtml)
        {
            string title = FirstMatch(itemHtml, @"<span\b(?=[^>]*class\s*=\s*['""][^'""<>]*title[^'""<>]*['""])[^>]*>(?<v>[\s\S]*?)<\/span>");
            if (String.IsNullOrWhiteSpace(title))
                title = FirstMatch(itemHtml, @"\btitle\s*=\s*['""](?<v>[^'""<>]*?)['""]");
            if (String.IsNullOrWhiteSpace(title))
            {
                string plain = HtmlToProfileText(itemHtml);
                string[] lines = Regex.Split(plain, @"[\r\n]+");
                if (lines.Length > 0)
                    title = lines[0];
            }
            return CleanProfileText(title);
        }

        private static string ExtractListItemArea(string itemHtml)
        {
            string value = FirstMatch(itemHtml, @"<div\b(?=[^>]*class\s*=\s*['""][^'""<>]*area[^'""<>]*['""])[^>]*>(?<v>[\s\S]*?)<\/div>");
            if (String.IsNullOrWhiteSpace(value))
                value = FirstMatch(itemHtml, @"<div\b[^>]*>(?<v>[\s\S]*?)<\/div>");
            value = CleanProfileText(value);
            if (String.IsNullOrWhiteSpace(value))
            {
                string title = ExtractListItemTitle(itemHtml);
                string plain = HtmlToProfileText(itemHtml);
                if (!String.IsNullOrWhiteSpace(title) && plain.StartsWith(title, StringComparison.OrdinalIgnoreCase))
                    plain = plain.Substring(title.Length);
                value = CleanProfileText(plain);
            }
            return value;
        }

        private static string ExtractBestNumber(string value)
        {
            string text = CleanProfileText(value);
            MatchCollection matches = Regex.Matches(text, @"[-+]?\d+");
            if (matches.Count > 0)
                return matches[matches.Count - 1].Value;
            return text;
        }

        private static void AddParsedDevice(ForumUserProfile profile, string name, string accessory, string url)
        {
            if (profile == null || profile.Devices == null || String.IsNullOrWhiteSpace(name))
                return;

            ForumUserDevice device = new ForumUserDevice();
            device.Name = name;
            device.Accessory = accessory;
            device.Url = url;
            profile.Devices.Add(device);
        }

        private static string ExtractProfileBlock(string html)
        {
            string source = html ?? "";
            string block = FirstMatch(source, @"<form\b(?=[^>]*(?:showuser|act\s*=\s*profile))[^>]*>(?<v>[\s\S]*?</form>)");
            if (!String.IsNullOrWhiteSpace(block))
                return block;

            block = FirstMatch(source, @"<div\b(?=[^>]*class\s*=\s*['""][^'""<>]*(?:user-profile-list|profile|user-box)[^'""<>]*['""])[^>]*>(?<v>[\s\S]*?</div>)");
            if (!String.IsNullOrWhiteSpace(block))
                return block;

            return "";
        }

        private static string ExtractClassText(string html, string[] classNames)
        {
            if (classNames == null)
                return "";

            for (int i = 0; i < classNames.Length; i++)
            {
                string className = Regex.Escape(classNames[i] ?? "");
                if (String.IsNullOrWhiteSpace(className))
                    continue;

                string value = FirstMatch(html, @"<(?:div|span|p)\b(?=[^>]*class\s*=\s*['""][^'""<>]*(?:" + className + @")[^'""<>]*['""])[^>]*>(?<v>[\s\S]*?)</(?:div|span|p)>");
                value = CleanProfileText(value);
                if (!String.IsNullOrWhiteSpace(value) && !LooksLikeProfileLabel(value, null) && value.Length <= 160)
                    return value;
            }

            return "";
        }

        private static string ExtractProfileSection(string html, string[] classNames, string[] emptyValues)
        {
            if (classNames == null)
                return "";

            for (int i = 0; i < classNames.Length; i++)
            {
                string className = Regex.Escape(classNames[i] ?? "");
                if (String.IsNullOrWhiteSpace(className))
                    continue;

                string value = FirstMatch(html, @"<(?<tag>div|section|blockquote|p)\b(?=[^>]*class\s*=\s*['""][^'""<>]*(?:" + className + @")[^'""<>]*['""])[^>]*>(?<v>[\s\S]*?)</\k<tag>>");
                value = CleanProfileText(value);
                if (String.IsNullOrWhiteSpace(value))
                    continue;
                if (ContainsAny(value, emptyValues))
                    continue;
                if (value.Length > 1200)
                    value = value.Substring(0, 1200).Trim() + "...";
                return value;
            }

            return "";
        }

        private static bool ContainsAny(string value, string[] variants)
        {
            if (variants == null)
                return false;
            string text = value ?? "";
            for (int i = 0; i < variants.Length; i++)
            {
                if (!String.IsNullOrWhiteSpace(variants[i]) && text.IndexOf(variants[i], StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }
            return false;
        }

        private static bool HasNoVisibleProfileInfo(ForumUserProfile profile)
        {
            if (profile == null)
                return true;

            return String.IsNullOrWhiteSpace(profile.Group)
                && String.IsNullOrWhiteSpace(profile.Status)
                && String.IsNullOrWhiteSpace(profile.RegistrationDate)
                && String.IsNullOrWhiteSpace(profile.LastVisit)
                && String.IsNullOrWhiteSpace(profile.Birthday)
                && String.IsNullOrWhiteSpace(profile.City)
                && String.IsNullOrWhiteSpace(profile.UserTime)
                && String.IsNullOrWhiteSpace(profile.Gender)
                && String.IsNullOrWhiteSpace(profile.Reputation)
                && String.IsNullOrWhiteSpace(profile.ForumPosts)
                && String.IsNullOrWhiteSpace(profile.ForumTopics)
                && String.IsNullOrWhiteSpace(profile.SiteKarma)
                && String.IsNullOrWhiteSpace(profile.SitePosts)
                && String.IsNullOrWhiteSpace(profile.SiteComments)
                && String.IsNullOrWhiteSpace(profile.Signature)
                && String.IsNullOrWhiteSpace(profile.About);
        }

        private static string BuildProfileFallbackText(string html, string nick, string fallbackUserId)
        {
            string text = HtmlToProfileText(html);
            if (String.IsNullOrWhiteSpace(text))
                return "Профиль открыт, но страница не содержит распознаваемых полей. ID: " + fallbackUserId;

            string[] lines = Regex.Split(text, @"[\r\n]+");
            List<string> result = new List<string>();
            for (int i = 0; i < lines.Length && result.Count < 16; i++)
            {
                string line = CleanProfileText(lines[i]);
                if (String.IsNullOrWhiteSpace(line))
                    continue;
                if (!String.IsNullOrWhiteSpace(nick) && String.Equals(line, nick, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (IsNoisyProfileLine(line))
                    continue;
                if (line.Length > 180)
                    line = line.Substring(0, 180).Trim() + "...";
                if (!result.Contains(line))
                    result.Add(line);
            }

            if (result.Count == 0)
                return "Профиль открыт, но страница не содержит распознаваемых полей. ID: " + fallbackUserId;

            return String.Join("; ", result.ToArray());
        }

        private static bool IsNoisyProfileLine(string line)
        {
            string text = CleanProfileText(line);
            if (String.IsNullOrWhiteSpace(text))
                return true;
            if (text.Length < 2)
                return true;
            if (Regex.IsMatch(text, @"^(профиль|настройки|редактировать|личные данные|контакты|устройства|статистика|подпись|заметка|сохранить|отмена)$", RegexOptions.IgnoreCase))
                return true;
            if (text.IndexOf("javascript:", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            return false;
        }

        private static string FirstNonEmpty(params string[] values)
        {
            if (values == null)
                return "";

            for (int i = 0; i < values.Length; i++)
            {
                if (!String.IsNullOrWhiteSpace(values[i]))
                    return values[i];
            }

            return "";
        }

        private static string ExtractProfileNick(string html, string plain, string fallbackUserId)
        {
            string nick = CleanProfileText(FirstMatch(html, @"<h1\b[^>]*>(?<v>[\s\S]*?)</h1>"));
            if (!String.IsNullOrWhiteSpace(nick) && !IsBadProfileNick(nick))
                return nick;

            nick = CleanProfileText(FirstMatch(html, @"<title\b[^>]*>(?<v>[\s\S]*?)</title>"));
            nick = Regex.Replace(nick ?? "", @"(?i)^\s*(?:Просмотр\s+профиля|Профиль\s+пользователя|Профиль)\s*[:\-–]?\s*", "").Trim();
            nick = Regex.Replace(nick ?? "", @"\s*[-–—|].*$", "").Trim();
            if (!String.IsNullOrWhiteSpace(nick) && !IsBadProfileNick(nick))
                return nick;

            nick = ExtractProfileField(plain, new string[] { "Ник", "Имя", "Пользователь" });
            if (!String.IsNullOrWhiteSpace(nick) && !IsBadProfileNick(nick))
                return nick;

            return "ID " + fallbackUserId;
        }

        private static bool IsBadProfileNick(string value)
        {
            string text = (value ?? "").Trim();
            if (String.IsNullOrWhiteSpace(text))
                return true;
            if (text.IndexOf("4PDA", StringComparison.OrdinalIgnoreCase) >= 0 && text.IndexOf("проф", StringComparison.OrdinalIgnoreCase) < 0)
                return true;
            if (text.Length > 80)
                return true;
            return false;
        }

        private static string ExtractAvatarUrl(string html)
        {
            string url = FirstMatch(html, @"<img\b(?=[^>]*(?:avatar|photo|userpic|profile))[^>]*\bsrc\s*=\s*['"" ](?<v>[^'"" >]+)['"" ][^>]*>");
            if (String.IsNullOrWhiteSpace(url))
                url = FirstMatch(html, @"<img\b[^>]*\bsrc\s*=\s*['"" ](?<v>[^'"" >]*(?:avatar|photo|userpic)[^'"" >]*)['"" ][^>]*>");
            return NormalizeForumUrl(url);
        }

        private static string ExtractProfileField(string plainText, string[] labels)
        {
            if (labels == null || labels.Length == 0)
                return "";

            string[] lines = Regex.Split(plainText ?? "", @"[\r\n]+");
            for (int i = 0; i < lines.Length; i++)
            {
                string line = CleanProfileText(lines[i]);
                if (String.IsNullOrWhiteSpace(line))
                    continue;

                for (int j = 0; j < labels.Length; j++)
                {
                    string label = labels[j];
                    if (String.IsNullOrWhiteSpace(label))
                        continue;

                    if (String.Equals(line, label, StringComparison.OrdinalIgnoreCase) || String.Equals(line, label + ":", StringComparison.OrdinalIgnoreCase))
                    {
                        string next = FindNextProfileValue(lines, i + 1, labels);
                        if (!String.IsNullOrWhiteSpace(next))
                            return next;
                    }

                    string inline = ExtractInlineProfileValue(line, label);
                    if (!String.IsNullOrWhiteSpace(inline))
                        return inline;
                }
            }

            return "";
        }

        private static string ExtractInlineProfileValue(string line, string label)
        {
            if (String.IsNullOrWhiteSpace(line) || String.IsNullOrWhiteSpace(label))
                return "";

            string pattern = @"(?:^|\s)" + Regex.Escape(label) + @"\s*:\s*(?<v>.+)$";
            Match match = Regex.Match(line, pattern, RegexOptions.IgnoreCase);
            string value = "";
            if (match.Success)
            {
                value = CleanProfileText(match.Groups["v"].Value);
            }
            else if (line.StartsWith(label + "  ", StringComparison.OrdinalIgnoreCase))
            {
                value = CleanProfileText(line.Substring(label.Length));
            }
            else
            {
                return "";
            }

            if (String.IsNullOrWhiteSpace(value))
                return "";

            value = CutBeforeNextProfileLabel(value);
            if (value.Length > 180)
                value = value.Substring(0, 180).Trim() + "...";

            return value;
        }

        private static string CutBeforeNextProfileLabel(string value)
        {
            string text = value ?? "";
            string[] labels = new string[]
            {
                "Группа", "Группа пользователя", "Статус", "Личный статус", "Регистрация", "Зарегистрирован", "Дата регистрации",
                "Последний визит", "Последнее посещение", "День рождения", "Дата рождения", "Город", "Откуда",
                "Время пользователя", "Местное время", "Время", "Пол", "Репутация", "Репутации", "Репу",
                "Посты форума", "Постов форума", "Сообщений", "Сообщения", "Постов", "Темы форума", "Тем", "Темы",
                "Карма сайта", "Карма", "Посты сайта", "Постов сайта", "Комментарии сайта", "Комментов", "Комментарии",
                "Подпись", "О себе", "Информация", "Обо мне"
            };

            int cut = -1;
            for (int i = 0; i < labels.Length; i++)
            {
                string label = Regex.Escape(labels[i]);
                Match m = Regex.Match(text, @"\s" + label + @"\s*:", RegexOptions.IgnoreCase);
                if (m.Success && (cut < 0 || m.Index < cut))
                    cut = m.Index;
            }

            if (cut > 0)
                text = text.Substring(0, cut);

            return CleanProfileText(text);
        }

        private static string FindNextProfileValue(string[] lines, int startIndex, string[] currentLabels)
        {
            for (int i = startIndex; i < lines.Length && i < startIndex + 6; i++)
            {
                string value = CleanProfileText(lines[i]);
                if (String.IsNullOrWhiteSpace(value))
                    continue;
                if (LooksLikeProfileLabel(value, currentLabels))
                    return "";
                return value;
            }

            return "";
        }

        private static bool LooksLikeProfileLabel(string value, string[] currentLabels)
        {
            string text = CleanProfileText(value);
            if (String.IsNullOrWhiteSpace(text))
                return false;

            string[] labels = new string[]
            {
                "Группа", "Группа пользователя", "Статус", "Личный статус", "Регистрация", "Зарегистрирован", "Дата регистрации",
                "Последний визит", "День рождения", "Дата рождения", "Город", "Откуда", "Время пользователя", "Местное время",
                "Пол", "Репутация", "Посты форума", "Сообщений", "Темы форума", "Карма сайта", "Посты сайта", "Комментарии сайта",
                "Подпись", "О себе", "Информация", "Устройства", "Ник", "Имя", "Пользователь"
            };

            for (int i = 0; i < labels.Length; i++)
            {
                if (String.Equals(text, labels[i], StringComparison.OrdinalIgnoreCase) || String.Equals(text, labels[i] + ":", StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        private static string HtmlToProfileText(string html)
        {
            string source = html ?? "";
            source = Regex.Replace(source, @"<script\b[\s\S]*?</script>", " ", RegexOptions.IgnoreCase);
            source = Regex.Replace(source, @"<style\b[\s\S]*?</style>", " ", RegexOptions.IgnoreCase);
            source = Regex.Replace(source, @"<(?:br|p|div|tr|td|th|li|dt|dd|h1|h2|h3|section|article)\b[^>]*>", "\n", RegexOptions.IgnoreCase);
            source = Regex.Replace(source, @"</(?:p|div|tr|td|th|li|dt|dd|h1|h2|h3|section|article)>", "\n", RegexOptions.IgnoreCase);
            source = Regex.Replace(source, @"<[^>]+>", " ", RegexOptions.IgnoreCase);
            source = HtmlUtilities.ConvertToText(source);
            source = Regex.Replace(source, @"[ \t\u00A0]+", " ");
            source = Regex.Replace(source, @" *\n+ *", "\n");
            return source.Trim();
        }

        private static string FirstMatch(string source, string pattern)
        {
            Match match = Regex.Match(source ?? "", pattern, RegexOptions.IgnoreCase | RegexOptions.Singleline);
            return match.Success ? match.Groups["v"].Value : "";
        }

        private static string CleanProfileText(string value)
        {
            string text = HtmlUtilities.ConvertToText(value ?? "");
            text = Regex.Replace(text, @"[\r\n\t]+", " ");
            text = Regex.Replace(text, @"\s+", " ");
            return text.Trim();
        }

        private static string ExtractUserId(string parameter)
        {
            if (String.IsNullOrWhiteSpace(parameter))
                return "";

            Match match = Regex.Match(parameter, @"(?:showuser=|/user/|^)(?<id>\d+)", RegexOptions.IgnoreCase);
            return match.Success ? match.Groups["id"].Value : OnlyDigits(parameter);
        }

        private static string NormalizeSingleLine(string value)
        {
            if (String.IsNullOrWhiteSpace(value))
                return String.Empty;

            string result = Regex.Replace(value, "\\s+", " ").Trim();
            return result;
        }

        private static string OnlyDigits(string value)
        {
            if (String.IsNullOrWhiteSpace(value))
                return "";

            return Regex.Replace(value, @"\D+", "");
        }

        private static string NormalizeForumUrl(string url)
        {
            string value = CleanProfileText(url);
            if (String.IsNullOrWhiteSpace(value))
                return "";
            if (value.StartsWith("//", StringComparison.Ordinal))
                return "https:" + value;
            if (value.StartsWith("/", StringComparison.Ordinal))
                return "https://4pda.to" + value;
            if (!Regex.IsMatch(value, @"^https?://", RegexOptions.IgnoreCase))
                return "https://4pda.to/forum/" + value.TrimStart('/');
            return value;
        }

        private static string NormalizeDeviceProfileUrl(string url)
        {
            string value = CleanProfileText(url);
            if (String.IsNullOrWhiteSpace(value))
                return "";
            if (value.StartsWith("//", StringComparison.Ordinal))
                return "https:" + value;
            if (value.StartsWith("/", StringComparison.Ordinal))
                return "https://4pda.to" + value;
            if (Regex.IsMatch(value, @"^https?://", RegexOptions.IgnoreCase))
                return value;

            string relative = value.TrimStart('/');
            if (relative.IndexOf("devdb", StringComparison.OrdinalIgnoreCase) >= 0 || Regex.IsMatch(relative, @"^index\.php\?p=", RegexOptions.IgnoreCase))
                return "https://4pda.to/" + relative;
            return NormalizeForumUrl(value);
        }

        private static string ExtractDeviceId(string url)
        {
            string value = url ?? "";
            Match match = Regex.Match(value, @"(?:/devdb/|[?&]p=)(?<id>\d+)", RegexOptions.IgnoreCase);
            return match.Success ? match.Groups["id"].Value : "";
        }


        private Task LoadAvatarAsync(string avatarUrl)
        {
            DeferredImage.Attach(AvatarImage, avatarUrl, 144, 0);
            return Task.FromResult(0);
        }

        private async Task LogoutAndBackAsync()
        {
            if (_loading)
                return;

            _loading = true;
            SetBusy(true);
            SetPageStatus("Выходим...");

            try
            {
                await ForumAuthService.LogoutAsync();
                NavigateToLoginAfterLogout();
            }
            catch
            {
                try
                {
                    // Локальный выход должен работать даже при недоступном сервере.
                    ForumAuthService.ClearCurrentUser();
                    NavigateToLoginAfterLogout();
                }
                catch (Exception ex)
                {
                    SetPageStatus("Не удалось выйти: " + ex.Message);
                    SetBusy(false);
                    _loading = false;
                }
            }
        }

        private void NavigateToLoginAfterLogout()
        {
            if (Frame == null)
                return;

            Frame.Navigate(typeof(LoginPage));
            Frame.BackStack.Clear();
            AppNavigationActions.NotifyNavigationStateChanged();
        }

        private void ResetNavigationStack()
        {
            try
            {
                if (Frame != null)
                {
                    Frame.BackStack.Clear();
                }
            }
            catch
            {
            }
        }

        private void SetProfileLoading(bool loading)
        {
            if (LoadingCircle != null)
            {
                LoadingCircle.IsActive = loading;
                LoadingCircle.Visibility = loading ? Visibility.Visible : Visibility.Collapsed;
            }

            if (ProfileContentScrollViewer != null)
                ProfileContentScrollViewer.Visibility = loading ? Visibility.Collapsed : Visibility.Visible;
        }

        private void SetBusy(bool busy)
        {
            if (RefreshButton != null)
                RefreshButton.IsEnabled = !busy;
            ApplyProfileMode(busy);
        }

        private void ApplyProfileMode(bool busy)
        {
            if (ActionsTitleTextBlock != null)
                ActionsTitleTextBlock.Visibility = Visibility.Visible;
            if (ActionsGrid != null)
                ActionsGrid.Visibility = Visibility.Visible;
            if (ActionsBorder != null)
                ActionsBorder.Visibility = Visibility.Visible;

            if (LogoutButton != null)
            {
                LogoutButton.Visibility = _isOwnProfile ? Visibility.Visible : Visibility.Collapsed;
                LogoutButton.IsEnabled = _isOwnProfile && !busy;
            }

        }

        private async Task ApplyProfileAsync(ForumUserProfile profile)
        {
            if (profile == null)
                return;

            string fallbackNick = _isOwnProfile ? ForumAuthService.CurrentUserLogin : "ID " + (String.IsNullOrWhiteSpace(profile.Id) ? _openedUserId : profile.Id);
            string fallbackId = _isOwnProfile ? ForumAuthService.CurrentUserId : _openedUserId;
            NickTextBlock.Text = String.IsNullOrWhiteSpace(profile.Nick) ? fallbackNick : profile.Nick;
            IdTextBlock.Text = "ID: " + (String.IsNullOrWhiteSpace(profile.Id) ? fallbackId : profile.Id);
            GroupTextBlock.Text = NormalizeSingleLine(profile.Group);
            string profileStatus = NormalizeSingleLine(profile.Status);
            StatusTextBlock.Text = profileStatus;
            StatusTextBlock.Visibility = String.IsNullOrWhiteSpace(profileStatus) ? Visibility.Collapsed : Visibility.Visible;
            GroupTextBlock.Margin = new Thickness(0, String.IsNullOrWhiteSpace(profileStatus) ? 3 : 4, 0, 0);
            ApplyProfileMode(false);

            if (_isOwnProfile)
            {
                if (String.IsNullOrWhiteSpace(profile.AvatarUrl))
                    profile.AvatarUrl = ForumAuthService.CurrentUserAvatarUrl;
                await ForumAuthService.UpdateCachedProfileAsync(profile);
            }

            await LoadAvatarAsync(profile.AvatarUrl);

            _personalRows.Clear();
            _statisticsRows.Clear();
            _deviceRows.Clear();

            SetSummaryValue(RegistrationPanel, RegistrationTextBlock, profile.RegistrationDate);
            SetSummaryValue(LastVisitPanel, LastVisitTextBlock, profile.LastVisit);

            AddPersonalRow("пол", profile.Gender);
            AddPersonalRow("день рождения", profile.Birthday);
            AddPersonalRow("город", profile.City);
            AddPersonalRow("время пользователя", profile.UserTime);
            AddPersonalRow("подпись", profile.Signature);
            AddPersonalRow("о себе", profile.About);

            AddStatisticsRow("репутация", profile.Reputation);
            AddStatisticsRow("темы форума", profile.ForumTopics);
            AddStatisticsRow("посты форума", profile.ForumPosts);
            AddStatisticsRow("карма сайта", profile.SiteKarma);
            AddStatisticsRow("посты сайта", profile.SitePosts);
            AddStatisticsRow("комментарии сайта", profile.SiteComments);

            if (profile.Devices != null)
            {
                foreach (ForumUserDevice device in profile.Devices)
                    AddDeviceRow(device);
            }

            Visibility devicesVisibility = _deviceRows.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            DevicesBorder.Visibility = devicesVisibility;
            DevicesTitleTextBlock.Visibility = devicesVisibility;
            DeviceRowsItemsControl.Visibility = devicesVisibility;

            PersonalCard.Visibility = _personalRows.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            StatisticsCard.Visibility = _statisticsRows.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

            if (_personalRows.Count == 0 && _statisticsRows.Count == 0 && _deviceRows.Count == 0)
            {
                AddPersonalRow("профиль", "Информация не найдена, но авторизация сохранена.");
                PersonalCard.Visibility = Visibility.Visible;
            }
        }

        private void AddPersonalRow(string title, string value)
        {
            AddRow(_personalRows, title, value);
        }

        private void AddStatisticsRow(string title, string value)
        {
            AddRow(_statisticsRows, title, value);
        }

        private static void AddRow(ObservableCollection<ProfileRow> target, string title, string value)
        {
            if (target == null || String.IsNullOrWhiteSpace(value))
                return;

            target.Add(new ProfileRow
            {
                Title = title,
                Value = value
            });
        }

        private static void SetSummaryValue(FrameworkElement panel, TextBlock textBlock, string value)
        {
            bool visible = !String.IsNullOrWhiteSpace(value);
            if (panel != null)
                panel.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            if (textBlock != null)
                textBlock.Text = visible ? value : "";
        }

        private void UpdateResponsiveLayout(double width)
        {
            if (ProfileLayoutGrid == null || ProfileRootPanel == null)
                return;

            bool wide = width >= 1000;
            double availableWidth = Math.Max(0, width);

            // На телефоне карточки занимают всю ширину страницы. На широком
            // экране содержимое ограничено 1240 px и остаётся по центру.
            ProfileRootPanel.Width = wide
                ? Math.Max(0, Math.Min(1240, availableWidth - 48))
                : availableWidth;
            ProfileRootPanel.Margin = wide
                ? new Thickness(0, 28, 0, 44)
                : new Thickness(0, 18, 0, 32);

            LeftProfileColumn.Width = wide ? new GridLength(300) : new GridLength(1, GridUnitType.Star);
            LeftProfileSpacerColumn.Width = wide ? new GridLength(16) : new GridLength(0);
            CenterProfileColumn.Width = wide ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
            RightProfileSpacerColumn.Width = wide ? new GridLength(16) : new GridLength(0);
            RightProfileColumn.Width = wide ? new GridLength(300) : new GridLength(0);

            if (wide)
            {
                PlaceSection(LeftProfileStack, 0, 0, 1);
                PlaceSection(CenterProfileStack, 0, 2, 1);
                PlaceSection(RightProfileStack, 0, 4, 1);
            }
            else
            {
                PlaceSection(LeftProfileStack, 0, 0, 1);
                PlaceSection(CenterProfileStack, 1, 0, 1);
                PlaceSection(RightProfileStack, 2, 0, 1);
            }
        }

        private static void PlaceSection(FrameworkElement element, int row, int column, int columnSpan)
        {
            if (element == null)
                return;

            Grid.SetRow(element, row);
            Grid.SetColumn(element, column);
            Grid.SetColumnSpan(element, columnSpan);
        }

        private async void SetPageStatus(string message)
        {
            string text = message == null ? "" : message.Trim();
            if (String.IsNullOrWhiteSpace(text))
                return;

            if (text.StartsWith("Загружаем", StringComparison.OrdinalIgnoreCase) ||
                text.StartsWith("Открыт чужой профиль", StringComparison.OrdinalIgnoreCase) ||
                text.StartsWith("Сохраняем", StringComparison.OrdinalIgnoreCase) ||
                text.StartsWith("Выходим", StringComparison.OrdinalIgnoreCase))
                return;

            if (_pageMessageDialogOpen)
                return;

            _pageMessageDialogOpen = true;
            try
            {
                ContentDialog dialog = new ContentDialog
                {
                    Title = "Профиль",
                    Content = text,
                    PrimaryButtonText = "ОК"
                };
                await dialog.ShowAsync();
            }
            catch
            {
            }
            finally
            {
                _pageMessageDialogOpen = false;
            }
        }

        private void AddDeviceRow(ForumUserDevice device)
        {
            if (device == null || String.IsNullOrWhiteSpace(device.Name))
                return;

            _deviceRows.Add(new DeviceRow
            {
                Name = device.Name,
                Accessory = device.Accessory,
                Url = device.Url
            });
        }

        public sealed class ProfileRow
        {
            public string Title { get; set; }
            public string Value { get; set; }
        }

        public sealed class DeviceRow
        {
            public string Name { get; set; }
            public string Accessory { get; set; }
            public string Url { get; set; }
        }

    }
}
