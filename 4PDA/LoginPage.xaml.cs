using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using _4PDA.Services;
using Windows.Data.Html;
using Windows.Storage;
using Windows.Storage.Streams;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media.Imaging;
using Windows.UI.Xaml.Navigation;
using Windows.Web.Http;
using Windows.Web.Http.Filters;

namespace _4PDA
{
    public sealed partial class LoginPage : Page
    {
        private readonly LoginFlow _flow = new LoginFlow();
        private ForumLoginForm _loginForm = new ForumLoginForm();
        private bool _loading;
        private bool _active;
        private bool _captchaReady;
        private int _requestVersion;

        public LoginPage()
        {
            InitializeComponent();
            NavigationCacheMode = NavigationCacheMode.Disabled;
            UpdateResponsiveLayout(Window.Current.Bounds.Width);
        }

        private void LoginPage_SizeChanged(object sender, SizeChangedEventArgs e) { UpdateResponsiveLayout(e.NewSize.Width); }
        private void UpdateResponsiveLayout(double width)
        {
            if (LoginContentPanel == null) return;
            bool wide = width >= 700;
            LoginContentPanel.Width = wide ? Math.Max(0, Math.Min(620, width - 48)) : Math.Max(0, width);
            LoginContentPanel.Margin = wide ? new Thickness(0, 36, 0, 48) : new Thickness(0, 18, 0, 32);
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            _active = true; _requestVersion++; _loading = false;
            _flow.Reset();
            if (ForumAuthService.IsAuthorized) { NavigateToUserPageAndRemoveLoginFromBackStack(); return; }
            RenderStep();
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            _active = false; _requestVersion++;
            _flow.Reset();
            PasswordTextBox.Password = string.Empty;
            CaptchaTextBox.Text = string.Empty;
            CaptchaImage.Source = null;
            base.OnNavigatedFrom(e);
        }

        private async void LoginInput_KeyDown(object sender, Windows.UI.Xaml.Input.KeyRoutedEventArgs e)
        {
            if (e.Key != Windows.System.VirtualKey.Enter) return;
            e.Handled = true;
            await SubmitAsync(false);
        }
        private async void LoginButton_Click(object sender, RoutedEventArgs e) { await SubmitAsync(false); }
        private async void ReloadCaptchaAppBarButton_Click(object sender, RoutedEventArgs e) { await SubmitAsync(true); }

        private void BackToCredentialsButton_Click(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            _flow.Reset(); _loginForm = new ForumLoginForm(); _captchaReady = false;
            CaptchaImage.Source = null; CaptchaTextBox.Text = string.Empty;
            ShowStatus(string.Empty); RenderStep();
            PasswordTextBox.Focus(FocusState.Programmatic);
        }

        private async Task SubmitAsync(bool refreshCaptcha)
        {
            if (_loading || !_active) return;
            string captcha = string.Empty;
            if (_flow.Step == LoginStep.Credentials)
            {
                try { _flow.Begin(LoginTextBox.Text, PasswordTextBox.Password, HiddenCheckBox.IsChecked == true); }
                catch (ArgumentException ex) { ShowStatus(ex.Message); return; }
            }
            else if (_flow.Step == LoginStep.Captcha && !refreshCaptcha)
            {
                captcha = (CaptchaTextBox.Text ?? string.Empty).Trim();
                int code;
                if (!_captchaReady || !int.TryParse(captcha, out code) || code <= 0)
                { ShowStatus("Введите цифры с картинки."); return; }
            }
            int version = _requestVersion;
            _loading = true; ShowStatus(refreshCaptcha ? "Обновляем капчу…" : "Проверяем…"); RenderStep();
            try
            {
                ForumLoginResult result = await ForumAuthService.LoginAsync(_loginForm, _flow.Login, _flow.Password, captcha, _flow.Hidden);
                if (!_active || version != _requestVersion) return;
                if (result.Success)
                {
                    _flow.Complete(); PasswordTextBox.Password = string.Empty;
                    CaptchaTextBox.Text = string.Empty;
                    NavigateToUserPageAndRemoveLoginFromBackStack();
                    return;
                }
                if (!string.IsNullOrWhiteSpace(result.CaptchaUrl))
                {
                    _flow.Challenge(result.CaptchaUrl);
                    _loginForm = new ForumLoginForm { CaptchaUrl = _flow.CaptchaUrl };
                    PasswordTextBox.Password = string.Empty;
                    CaptchaTextBox.Text = string.Empty; CaptchaImage.Source = null; _captchaReady = false;
                    RenderStep();
                    ShowStatus(string.IsNullOrEmpty(captcha) ? "Введите код с картинки, чтобы завершить вход." : "Код не подошёл. Введите новый код с картинки.");
                    BitmapImage image = await ForumAuthService.LoadCaptchaImageAsync(_loginForm);
                    if (!_active || version != _requestVersion) return;
                    CaptchaImage.Source = image; _captchaReady = image != null;
                    if (!_captchaReady) ShowStatus("Картинка не загрузилась. Нажмите «Обновить изображение».");
                }
                else
                {
                    _flow.Reset(); PasswordTextBox.Password = string.Empty;
                    ShowStatus(string.IsNullOrWhiteSpace(result.ErrorMessage) ? "Не удалось войти. Проверьте данные аккаунта." : result.ErrorMessage);
                }
            }
            catch (Exception ex)
            {
                if (_active && version == _requestVersion) ShowStatus("Не удалось выполнить вход: " + ex.Message);
            }
            finally
            {
                if (_active && version == _requestVersion)
                {
                    _loading = false; RenderStep();
                    if (_flow.Step == LoginStep.Captcha && _captchaReady) CaptchaTextBox.Focus(FocusState.Programmatic);
                }
            }
        }

        private void RenderStep()
        {
            bool captcha = _flow.Step == LoginStep.Captcha;
            CredentialsPanel.Visibility = captcha ? Visibility.Collapsed : Visibility.Visible;
            CaptchaPanel.Visibility = BackToCredentialsButton.Visibility = captcha ? Visibility.Visible : Visibility.Collapsed;
            LoginStepTextBlock.Text = captcha ? "Шаг 2 · Подтверждение входа" : "Шаг 1 · Данные аккаунта";
            CaptchaAccountTextBlock.Text = captcha ? "Вход в аккаунт «" + _flow.Login + "»" : string.Empty;
            LoginButton.Content = captcha ? "Войти" : "Продолжить";
            LoginButton.IsEnabled = !_loading && (!captcha || _captchaReady);
            LoginTextBox.IsEnabled = PasswordTextBox.IsEnabled = HiddenCheckBox.IsEnabled = !_loading;
            CaptchaTextBox.IsEnabled = ReloadCaptchaButton.IsEnabled = BackToCredentialsButton.IsEnabled = !_loading;
            LoadingCircle.IsActive = _loading;
            LoadingCircle.Visibility = _loading ? Visibility.Visible : Visibility.Collapsed;
        }
        private void ShowStatus(string text)
        {
            LoginStatusTextBlock.Text = text ?? string.Empty;
            LoginStatusTextBlock.Visibility = string.IsNullOrWhiteSpace(text) ? Visibility.Collapsed : Visibility.Visible;
        }
        private void NavigateToUserPageAndRemoveLoginFromBackStack()
        {
            Frame.Navigate(typeof(UserPage));
            for (int i = Frame.BackStack.Count - 1; i >= 0; i--)
                if (Frame.BackStack[i].SourcePageType == typeof(LoginPage)) Frame.BackStack.RemoveAt(i);
            AppNavigationActions.NotifyNavigationStateChanged();
        }
    }

    internal static class ForumAuthService
    {
        private const string Host = "4pda.to";
        private const string ForumRootUrl = "https://4pda.to/forum/index.php";
        private const string AuthUrl = "https://4pda.to/forum/index.php?act=auth";
        private const string MinimalPageUrl = "https://4pda.to/forum/index.php?showforum=200#afterauth";
        private const string KeyUserId = "ForumUserId";
        private const string KeyUserLogin = "ForumUserLogin";
        private const string KeyUserAvatar = "ForumUserAvatar";
        private const string KeyUserAvatarCache = "ForumUserAvatarCache";
        private const string KeyLogoutK = "ForumLogoutK";
        private const string AvatarFilePrefix = "forum_account_avatar";

        public static event EventHandler AccountChanged;

        private static readonly HttpBaseProtocolFilter Filter = new HttpBaseProtocolFilter();
        private static readonly HttpClient Client = new HttpClient(Filter);

        static ForumAuthService()
        {
            try
            {
                Client.DefaultRequestHeaders.UserAgent.ParseAdd(
                    "Mozilla/5.0 (Windows NT 6.3; Trident/7.0; rv:11.0) like Gecko");
            }
            catch
            {
            }

            try
            {
                Client.DefaultRequestHeaders.Accept.ParseAdd("text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
            }
            catch
            {
            }

            try
            {
                Client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("ru-RU,ru;q=0.9,en-US;q=0.8,en;q=0.7");
            }
            catch
            {
            }
        }

        public static bool IsAuthorized
        {
            get { return OfficialApiService.IsAuthorized; }
        }

        public static string CurrentUserId
        {
            get { return ReadSetting(KeyUserId); }
        }

        public static string CurrentUserLogin
        {
            get
            {
                string login = ReadSetting(KeyUserLogin);
                return String.IsNullOrWhiteSpace(login) ? CurrentUserId : login;
            }
        }

        public static string CurrentUserAvatarUrl
        {
            get { return ReadSetting(KeyUserAvatar); }
        }

        public static string CurrentUserAvatarCacheUri
        {
            get { return ReadSetting(KeyUserAvatarCache); }
        }

        public static Task<ForumLoginForm> GetLoginFormAsync()
        {
            return Task.FromResult(new ForumLoginForm());
        }

        private static async Task<ForumLoginForm> GetHtmlLoginFormAsync()
        {
            // ForPDA берёт форму сразу с act=auth. Это основной путь.
            string firstPage = await ReadPageAsync(new Uri(AuthUrl, UriKind.Absolute));

            var alreadyLoginResult = new ForumLoginResult();
            CheckLogin(firstPage, alreadyLoginResult);
            if (alreadyLoginResult.Success)
            {
                SaveCurrentUser(alreadyLoginResult);
                throw new InvalidOperationException("Вы уже авторизованы.");
            }

            ForumLoginForm form = TryParseLoginForm(firstPage);
            if (IsUsableLoginForm(form))
                return form;

            // 4pdaClient-plus дополнительно открывает act=auth&k=... . Оставляем это как fallback.
            string k = ExtractValue(firstPage, "act=auth[^\\\"']*[;&]k=(?<value>[^&\\\"']*)");
            var fallbackUrls = new List<string>();

            if (!String.IsNullOrWhiteSpace(k))
                fallbackUrls.Add(AuthUrl + "&k=" + Uri.EscapeDataString(k));

            fallbackUrls.Add(AuthUrl + "&k=" + Uri.EscapeDataString(Guid.NewGuid().ToString()));

            string lastPage = firstPage;
            for (int i = 0; i < fallbackUrls.Count; i++)
            {
                try
                {
                    lastPage = await ReadPageAsync(new Uri(fallbackUrls[i], UriKind.Absolute));
                    form = TryParseLoginForm(lastPage);
                    if (IsUsableLoginForm(form))
                        return form;
                }
                catch
                {
                    // Следующий fallback ниже.
                }
            }

            string diagnostic = BuildLoginFormDiagnostic(lastPage);
            throw new InvalidOperationException(diagnostic);
        }

        private static async Task<string> ReadPageAsync(Uri uri)
        {
            using (HttpResponseMessage response = await Client.GetAsync(uri))
                return await FourPdaResponseHelper.ReadHtmlAsync(response);
        }

        public static async Task<BitmapImage> LoadCaptchaImageAsync(ForumLoginForm form)
        {
            if (form == null || String.IsNullOrWhiteSpace(form.CaptchaUrl))
                return null;

            using (HttpResponseMessage response = await Client.GetAsync(new Uri(form.CaptchaUrl, UriKind.Absolute)))
            {
                response.EnsureSuccessStatusCode();
                IBuffer buffer = await response.Content.ReadAsBufferAsync();
                if (buffer == null || buffer.Length == 0) throw new InvalidOperationException("Сервер вернул пустую капчу.");
                using (var stream = new InMemoryRandomAccessStream())
                {
                    await stream.WriteAsync(buffer); stream.Seek(0);
                    var image = new BitmapImage();
                    await image.SetSourceAsync(stream);
                    return image;
                }
            }
        }

        public static async Task<ForumLoginResult> LoginAsync(ForumLoginForm form, string login, string password, string captcha, bool hidden)
        {
            try
            {
                ApiArray response = await OfficialApiService.LoginAsync(login, password, captcha, hidden);
                var result = new ForumLoginResult { Success = true, UserId = response.Int(0).ToString(), UserLogin = login };
                SaveCurrentUser(result);
                // UserPage loads and caches the profile after navigation. Do not
                // delay completion of login with a second profile/avatar request.
                return result;
            }
            catch (OfficialApiException ex)
            {
                if (ex.Command != "ml") throw;
                return new ForumLoginResult { ErrorMessage = ex.Status == 4 ? "Введите код с картинки." : "Неверный логин или пароль.",
                    CaptchaUrl = ex.Status == 4 ? ex.Payload.Text(0) : string.Empty };
            }
        }

        private static async Task<ForumLoginResult> LoginWithHtmlAsync(ForumLoginForm form, string login, string password, string captcha, bool hidden)
        {
            var result = new ForumLoginResult();

            if (form == null)
            {
                result.ErrorMessage = "Форма авторизации не загружена.";
                return result;
            }

            // ForPDA отправляет не всю HTML-форму, а фиксированный набор полей на act=auth.
            // Важно: login/password должны быть закодированы как windows-1251, как URLEncoder.encode(..., "windows-1251") в ForPDA.
            var values = new List<FormPostField>();

            AddFormValue(values, "captcha-time", form.CaptchaTime, false);
            AddFormValue(values, "captcha-sig", form.CaptchaSig, false);
            AddFormValue(values, "captcha", form.CaptchaRequired ? captcha : "", false);
            AddFormValue(values, "return", MinimalPageUrl, false);
            AddFormValue(values, "login", UrlEncodeWindows1251(login), true);
            AddFormValue(values, "password", UrlEncodeWindows1251(password), true);
            AddFormValue(values, "remember", "1", false);
            AddFormValue(values, "hidden", hidden ? "1" : "0", false);

            string bodyToSend = BuildFormBody(values);
            HttpStringContent content = new HttpStringContent(
                bodyToSend,
                Windows.Storage.Streams.UnicodeEncoding.Utf8,
                "application/x-www-form-urlencoded");

            HttpResponseMessage response = await Client.PostAsync(new Uri(AuthUrl, UriKind.Absolute), content);
            string body = await response.Content.ReadAsStringAsync();

            if (String.IsNullOrWhiteSpace(body))
            {
                result.ErrorMessage = "Сервер вернул пустую страницу после отправки формы.";
                return result;
            }

            string explicitError = TryExtractLoginError(body);
            if (!String.IsNullOrWhiteSpace(explicitError))
            {
                result.ErrorMessage = explicitError;
                return result;
            }

            // Сначала проверяем cookie, потому что на успешном входе 4PDA может редиректить не туда,
            // где старый HTML-шаблон содержит showuser + logout.
            ApplyCookieLoginResult(result, login);
            CheckLogin(body, result);

            // Повторяем проверку на минимальной странице, как в ForPDA: там ожидается showuser + action=logout&k=.
            if (!result.Success)
            {
                try
                {
                    string minimalPage = await Client.GetStringAsync(new Uri(MinimalPageUrl, UriKind.Absolute));
                    string minimalError = TryExtractLoginError(minimalPage);

                    if (String.IsNullOrWhiteSpace(minimalError))
                    {
                        CheckLogin(minimalPage, result);
                        ApplyCookieLoginResult(result, login);
                    }
                    else
                    {
                        result.ErrorMessage = minimalError;
                    }
                }
                catch
                {
                    // Ниже вернём нормальную диагностическую ошибку.
                }
            }

            if (result.Success)
            {
                if (String.IsNullOrWhiteSpace(result.UserLogin))
                    result.UserLogin = login;

                SaveCurrentUser(result);
                ForumCookieHelper.SaveForumCookiesFromSystemCookieManager("ForumAuthService.LoginAsync");
                await CacheCurrentUserAvatarAsync(result.UserAvatarUrl);
                return result;
            }

            if (String.IsNullOrWhiteSpace(result.ErrorMessage))
                result.ErrorMessage = BuildAuthFailureDiagnostic(body);

            return result;
        }

        public static async Task LogoutAsync()
        {
            try { if (OfficialApiService.IsAuthorized) await OfficialApiService.CallAsync("ma", 0, "", 0); }
            finally { ClearCurrentUser(); }
        }

        private static async Task LogoutFromHtmlAsync()
        {
            string storedKey = ReadSetting(KeyLogoutK);
            string logoutUrl = "";

            try
            {
                string forumPage = await Client.GetStringAsync(new Uri(ForumRootUrl, UriKind.Absolute));
                logoutUrl = ExtractLogoutUrl(forumPage);

                if (String.IsNullOrWhiteSpace(logoutUrl))
                {
                    string liveKey = ExtractValue(forumPage, "(?:action=logout|CODE=03|act=(?:logout|Login))[^\"']*?[;&]k=(?<value>[a-z0-9]{16,64})");
                    if (!String.IsNullOrWhiteSpace(liveKey))
                        storedKey = liveKey;
                }
            }
            catch
            {
                // Если форум недоступен, всё равно завершаем локальную сессию ниже.
            }

            var logoutUrls = new List<string>();
            if (!String.IsNullOrWhiteSpace(logoutUrl))
                logoutUrls.Add(logoutUrl);

            if (!String.IsNullOrWhiteSpace(storedKey))
            {
                logoutUrls.Add("https://" + Host + "/forum/index.php?act=Login&CODE=03&k=" + Uri.EscapeDataString(storedKey));
                logoutUrls.Add("https://" + Host + "/forum/index.php?act=logout&k=" + Uri.EscapeDataString(storedKey));
            }

            for (int i = 0; i < logoutUrls.Count; i++)
            {
                string candidate = logoutUrls[i];
                if (String.IsNullOrWhiteSpace(candidate))
                    continue;

                bool duplicate = false;
                for (int j = 0; j < i; j++)
                {
                    if (String.Equals(logoutUrls[j], candidate, StringComparison.OrdinalIgnoreCase))
                    {
                        duplicate = true;
                        break;
                    }
                }
                if (duplicate)
                    continue;

                try
                {
                    HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, new Uri(candidate, UriKind.Absolute));
                    ApplyLogoutRequestHeaders(request);
                    await Client.SendRequestAsync(request);
                }
                catch
                {
                    // Следующий вариант logout ниже.
                }
            }

            ClearCurrentUser();
        }

        private static void ApplyLogoutRequestHeaders(HttpRequestMessage request)
        {
            ForumCookieHelper.ApplyDefaultHeaders(request);
            ForumCookieHelper.TryAppendHeader(request, "Referer", ForumRootUrl);

            string cookieHeader = ForumCookieHelper.GetCookieHeaderForUrl(request.RequestUri);
            if (!String.IsNullOrWhiteSpace(cookieHeader))
                ForumCookieHelper.TryAppendHeader(request, "Cookie", cookieHeader);
        }

        private static string ExtractLogoutUrl(string html)
        {
            if (String.IsNullOrWhiteSpace(html))
                return "";

            Match match = Regex.Match(
                html,
                "\\bhref\\s*=\\s*(?:\"(?<url>[^\"]*(?:action=logout|CODE=03|act=logout)[^\"]*)\"|'(?<url>[^']*(?:action=logout|CODE=03|act=logout)[^']*)')",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);

            if (!match.Success)
                return "";

            string value = HtmlDecode(match.Groups["url"].Value).Trim();
            if (String.IsNullOrWhiteSpace(value))
                return "";

            if (value.StartsWith("//", StringComparison.Ordinal))
                return "https:" + value;

            Uri absolute;
            if (Uri.TryCreate(value, UriKind.Absolute, out absolute))
                return absolute.ToString();

            return new Uri(new Uri(ForumRootUrl, UriKind.Absolute), value).ToString();
        }

        public static async Task<ForumUserProfile> GetCurrentUserProfileAsync()
        {
            OfficialApiService.RequireAuthorization();
            ForumUserProfile profile = await OfficialApiService.GetProfileAsync(CurrentUserId);
            await UpdateCachedProfileAsync(profile);
            return profile;
        }

        private static async Task<ForumUserProfile> GetCurrentUserProfileFromHtmlAsync()
        {
            if (!IsAuthorized)
                throw new InvalidOperationException("Пользователь не авторизован.");

            string userId = CurrentUserId;
            string page = await ReadPageAsync(new Uri("https://" + Host + "/forum/index.php?showuser=" + Uri.EscapeDataString(userId), UriKind.Absolute));
            ForumUserProfile profile = ParseProfile(page);
            profile.Id = userId;

            if (String.IsNullOrWhiteSpace(profile.Nick))
                profile.Nick = CurrentUserLogin;

            if (String.IsNullOrWhiteSpace(profile.AvatarUrl))
                profile.AvatarUrl = CurrentUserAvatarUrl;

            await UpdateCachedProfileAsync(profile);
            return profile;
        }

        public static void ClearCurrentUser()
        {
            OfficialApiService.ClearSession();
            ForumCookieHelper.ClearForumAuthenticationCookies(Filter.CookieManager);

            ApplicationData.Current.LocalSettings.Values.Remove(KeyUserId);
            ApplicationData.Current.LocalSettings.Values.Remove(KeyUserLogin);
            ApplicationData.Current.LocalSettings.Values.Remove(KeyUserAvatar);
            ApplicationData.Current.LocalSettings.Values.Remove(KeyUserAvatarCache);
            ApplicationData.Current.LocalSettings.Values.Remove(KeyLogoutK);

            Task ignored = DeleteCachedAvatarFilesAsync();
            NotifyAccountChanged();
        }

        public static async Task UpdateCachedProfileAsync(ForumUserProfile profile)
        {
            if (profile == null)
                return;

            if (!String.IsNullOrWhiteSpace(profile.Id))
                WriteSetting(KeyUserId, OnlyDigits(profile.Id));
            if (!String.IsNullOrWhiteSpace(profile.Nick))
                WriteSetting(KeyUserLogin, profile.Nick);
            if (!String.IsNullOrWhiteSpace(profile.AvatarUrl))
                WriteSetting(KeyUserAvatar, profile.AvatarUrl);

            await CacheCurrentUserAvatarAsync(profile.AvatarUrl);
            NotifyAccountChanged();
        }

        public static async Task CacheCurrentUserAvatarAsync(string avatarUrl)
        {
            if (String.IsNullOrWhiteSpace(avatarUrl))
            {
                NotifyAccountChanged();
                return;
            }

            try
            {
                Uri uri = new Uri(NormalizeUrl(avatarUrl), UriKind.Absolute);
                HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, uri);
                ForumCookieHelper.ApplyDefaultHeaders(request);
                ForumCookieHelper.TryAppendHeader(request, "Referer", ForumRootUrl);
                string cookieHeader = ForumCookieHelper.GetCookieHeaderForUrl(uri);
                if (!String.IsNullOrWhiteSpace(cookieHeader))
                    ForumCookieHelper.TryAppendHeader(request, "Cookie", cookieHeader);

                HttpResponseMessage response = await Client.SendRequestAsync(request);
                if (!response.IsSuccessStatusCode)
                    return;

                IBuffer buffer = await response.Content.ReadAsBufferAsync();
                if (buffer == null || buffer.Length == 0)
                    return;

                string extension = GetAvatarFileExtension(uri.AbsolutePath);
                await DeleteCachedAvatarFilesAsync();
                StorageFile file = await ApplicationData.Current.LocalFolder.CreateFileAsync(AvatarFilePrefix + extension, CreationCollisionOption.ReplaceExisting);
                await FileIO.WriteBufferAsync(file, buffer);
                WriteSetting(KeyUserAvatarCache, "ms-appdata:///local/" + file.Name);
            }
            catch
            {
            }
            finally
            {
                NotifyAccountChanged();
            }
        }

        private static string GetAvatarFileExtension(string path)
        {
            string value = path == null ? "" : path.ToLowerInvariant();
            if (value.EndsWith(".png")) return ".png";
            if (value.EndsWith(".gif")) return ".gif";
            if (value.EndsWith(".bmp")) return ".bmp";
            if (value.EndsWith(".jpeg")) return ".jpeg";
            return ".jpg";
        }

        private static async Task DeleteCachedAvatarFilesAsync()
        {
            try
            {
                IReadOnlyList<StorageFile> files = await ApplicationData.Current.LocalFolder.GetFilesAsync();
                foreach (StorageFile file in files)
                {
                    if (file != null && file.Name.StartsWith(AvatarFilePrefix, StringComparison.OrdinalIgnoreCase))
                        await file.DeleteAsync(StorageDeleteOption.PermanentDelete);
                }
            }
            catch
            {
            }
        }

        private static void NotifyAccountChanged()
        {
            EventHandler handler = AccountChanged;
            if (handler != null)
                handler(null, EventArgs.Empty);
        }

        private static ForumLoginForm TryParseLoginForm(string page)
        {
            if (String.IsNullOrWhiteSpace(page))
                return null;

            string formHtml = ExtractAuthForm(page);
            if (String.IsNullOrWhiteSpace(formHtml))
            {
                if (page.IndexOf("captcha-time", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    page.IndexOf("captcha-sig", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    page.IndexOf("name=\"login\"", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    page.IndexOf("name='login'", StringComparison.OrdinalIgnoreCase) >= 0)
                    formHtml = page;
                else
                    return null;
            }

            ForumLoginForm form = new ForumLoginForm();
            form.PostUrl = NormalizeUrl(HtmlDecode(ExtractAttribute(ExtractFirst(formHtml, "(?<value><form\\b[^>]*>)"), "action")));
            form.Fields = ExtractInputFields(formHtml);
            form.Session = GetFieldValue(form.Fields, "s");
            form.CaptchaTime = GetFieldValue(form.Fields, "captcha-time", "captcha_time");
            form.CaptchaSig = GetFieldValue(form.Fields, "captcha-sig", "captcha_sig");
            form.CaptchaUrl = FindCaptchaImageUrl(formHtml);

            // Regex из ForPDA: captcha-time -> captcha-sig -> src. Используем как дополнительный путь.
            if (String.IsNullOrWhiteSpace(form.CaptchaTime) || String.IsNullOrWhiteSpace(form.CaptchaSig) || String.IsNullOrWhiteSpace(form.CaptchaUrl))
            {
                Match m = Regex.Match(page,
                    "captcha-time[\\\"']?\\s+value=[\\\"'](?<time>[^\\\"']*?)[\\\"'][\\s\\S]*?captcha-sig[\\\"']?\\s+value=[\\\"'](?<sig>[^\\\"']*?)[\\\"'][\\s\\S]*?src=[\\\"'](?<src>[^\\\"']*?)[\\\"']",
                    RegexOptions.IgnoreCase | RegexOptions.Singleline);

                if (m.Success)
                {
                    if (String.IsNullOrWhiteSpace(form.CaptchaTime))
                        form.CaptchaTime = HtmlDecode(m.Groups["time"].Value);

                    if (String.IsNullOrWhiteSpace(form.CaptchaSig))
                        form.CaptchaSig = HtmlDecode(m.Groups["sig"].Value);

                    if (String.IsNullOrWhiteSpace(form.CaptchaUrl))
                        form.CaptchaUrl = NormalizeUrl(HtmlDecode(m.Groups["src"].Value));
                }
            }

            if (!String.IsNullOrWhiteSpace(form.CaptchaTime) && !form.Fields.ContainsKey("captcha-time"))
                form.Fields["captcha-time"] = form.CaptchaTime;

            if (!String.IsNullOrWhiteSpace(form.CaptchaSig) && !form.Fields.ContainsKey("captcha-sig"))
                form.Fields["captcha-sig"] = form.CaptchaSig;

            return form;
        }

        private static bool IsUsableLoginForm(ForumLoginForm form)
        {
            if (form == null)
                return false;

            bool hasLoginField = form.Fields != null &&
                (form.Fields.ContainsKey("login") || form.Fields.ContainsKey("UserName") || form.Fields.ContainsKey("username"));

            bool hasPasswordField = form.Fields != null &&
                (form.Fields.ContainsKey("password") || form.Fields.ContainsKey("PassWord") || form.Fields.ContainsKey("passwd"));

            bool hasFullCaptcha = !String.IsNullOrWhiteSpace(form.CaptchaTime) &&
                !String.IsNullOrWhiteSpace(form.CaptchaSig) &&
                !String.IsNullOrWhiteSpace(form.CaptchaUrl);

            bool hasPartialCaptcha = !String.IsNullOrWhiteSpace(form.CaptchaTime) ||
                !String.IsNullOrWhiteSpace(form.CaptchaSig) ||
                !String.IsNullOrWhiteSpace(form.CaptchaUrl);

            if (hasFullCaptcha)
                return true;

            if (hasPartialCaptcha)
                return false;

            return hasLoginField && hasPasswordField;
        }

        private static string BuildLoginFormDiagnostic(string page)
        {
            string text = CleanText(page);

            if (text.IndexOf("hcaptcha", StringComparison.OrdinalIgnoreCase) >= 0 ||
                text.IndexOf("h-captcha", StringComparison.OrdinalIgnoreCase) >= 0)
                return "4PDA отдала hCaptcha/антибот-страницу, а не старую форму с captcha-time/captcha-sig.";

            if (text.IndexOf("captcha", StringComparison.OrdinalIgnoreCase) >= 0 ||
                page.IndexOf("captcha", StringComparison.OrdinalIgnoreCase) >= 0)
                return "4PDA отдала форму капчи в другом формате: старые поля captcha-time/captcha-sig не найдены.";

            if (text.IndexOf("ошибка", StringComparison.OrdinalIgnoreCase) >= 0 ||
                text.IndexOf("error", StringComparison.OrdinalIgnoreCase) >= 0)
                return ExtractLoginError(page);

            return "Форма входа не найдена. Возможно, сайт вернул мобильную/антибот-страницу вместо act=auth.";
        }

        private static string ExtractAuthForm(string page)
        {
            MatchCollection forms = Regex.Matches(page, "(?<value><form\\b[\\s\\S]*?</form>)", RegexOptions.IgnoreCase | RegexOptions.Singleline);

            foreach (Match match in forms)
            {
                string form = match.Groups["value"].Value;
                string start = ExtractFirst(form, "(?<value><form\\b[^>]*>)");
                string id = ExtractAttribute(start, "id");
                string name = ExtractAttribute(start, "name");
                string action = ExtractAttribute(start, "action");

                if (String.Equals(id, "auth", StringComparison.OrdinalIgnoreCase) ||
                    String.Equals(name, "auth", StringComparison.OrdinalIgnoreCase) ||
                    action.IndexOf("act=auth", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    form.IndexOf("captcha-time", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    form.IndexOf("captcha-sig", StringComparison.OrdinalIgnoreCase) >= 0)
                    return form;
            }

            return "";
        }

        private static Dictionary<string, string> ExtractInputFields(string html)
        {
            var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            if (String.IsNullOrWhiteSpace(html))
                return fields;

            MatchCollection inputs = Regex.Matches(html, "<input\\b(?<attrs>[^>]*)>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            foreach (Match input in inputs)
            {
                string attrs = input.Groups["attrs"].Value;
                string name = HtmlDecode(ExtractAttribute(attrs, "name"));
                string value = HtmlDecode(ExtractAttribute(attrs, "value"));

                if (String.IsNullOrWhiteSpace(name))
                    continue;

                if (!fields.ContainsKey(name))
                    fields.Add(name, value);
                else
                    fields[name] = value;
            }

            return fields;
        }

        private static string FindCaptchaImageUrl(string html)
        {
            if (String.IsNullOrWhiteSpace(html))
                return "";

            string fallback = "";
            MatchCollection images = Regex.Matches(html, "<img\\b(?<attrs>[^>]*)>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            foreach (Match image in images)
            {
                string attrs = image.Groups["attrs"].Value;
                string src = HtmlDecode(ExtractAttribute(attrs, "src"));
                if (String.IsNullOrWhiteSpace(src))
                    continue;

                string all = attrs + " " + src;
                if (all.IndexOf("captcha", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    all.IndexOf("antibot", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    all.IndexOf("cap", StringComparison.OrdinalIgnoreCase) >= 0)
                    return NormalizeUrl(src);

                if (String.IsNullOrWhiteSpace(fallback))
                    fallback = src;
            }

            if (!String.IsNullOrWhiteSpace(fallback) &&
                (html.IndexOf("captcha-time", StringComparison.OrdinalIgnoreCase) >= 0 || html.IndexOf("captcha-sig", StringComparison.OrdinalIgnoreCase) >= 0))
                return NormalizeUrl(fallback);

            return "";
        }

        private static string GetFieldValue(Dictionary<string, string> fields, params string[] names)
        {
            if (fields == null || names == null)
                return "";

            for (int i = 0; i < names.Length; i++)
            {
                string value;
                if (fields.TryGetValue(names[i], out value))
                    return value == null ? "" : value;
            }

            foreach (KeyValuePair<string, string> pair in fields)
            {
                for (int i = 0; i < names.Length; i++)
                {
                    if (String.Equals(pair.Key, names[i], StringComparison.OrdinalIgnoreCase))
                        return pair.Value == null ? "" : pair.Value;
                }
            }

            return "";
        }

        private static void SetFormValue(List<KeyValuePair<string, string>> values, string name, string value)
        {
            for (int i = values.Count - 1; i >= 0; i--)
            {
                if (String.Equals(values[i].Key, name, StringComparison.OrdinalIgnoreCase))
                    values.RemoveAt(i);
            }

            values.Add(new KeyValuePair<string, string>(name, value == null ? "" : value));
        }

        private static void AddFormValue(List<FormPostField> values, string name, string value, bool alreadyEncoded)
        {
            values.Add(new FormPostField
            {
                Name = name == null ? "" : name,
                Value = value == null ? "" : value,
                AlreadyEncoded = alreadyEncoded
            });
        }

        private static string BuildFormBody(List<FormPostField> values)
        {
            if (values == null || values.Count == 0)
                return "";

            StringBuilder builder = new StringBuilder();

            for (int i = 0; i < values.Count; i++)
            {
                FormPostField field = values[i];

                if (i > 0)
                    builder.Append('&');

                builder.Append(UrlEncodeUtf8(field.Name));
                builder.Append('=');
                builder.Append(field.AlreadyEncoded ? field.Value : UrlEncodeUtf8(field.Value));
            }

            return builder.ToString();
        }

        private static string UrlEncodeUtf8(string value)
        {
            if (String.IsNullOrEmpty(value))
                return "";

            return UrlEncodeBytes(Encoding.UTF8.GetBytes(value));
        }

        private static string UrlEncodeWindows1251(string value)
        {
            if (String.IsNullOrEmpty(value))
                return "";

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
                return "";

            StringBuilder builder = new StringBuilder();

            for (int i = 0; i < bytes.Length; i++)
            {
                byte b = bytes[i];
                char ch = (char)b;

                if ((ch >= 'A' && ch <= 'Z') ||
                    (ch >= 'a' && ch <= 'z') ||
                    (ch >= '0' && ch <= '9') ||
                    ch == '-' || ch == '_' || ch == '.' || ch == '*')
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

        private static ForumUserProfile ParseProfile(string page)
        {
            ForumUserProfile profile = new ForumUserProfile();

            if (String.IsNullOrWhiteSpace(page))
                return profile;

            profile.RawText = CleanText(page);
            profile.Id = ExtractFirst(page, "showuser=(?<value>\\d+)");

            // Основной парсер перенесён по смыслу из ForPDA ProfileParser + patterns.json.
            // Он сначала пытается разобрать старую структуру user-box целиком, затем добирает поля мягкими fallback-ами.
            TryParseProfileMainBlock(page, profile);
            TryParseProfileDevices(page, profile);
            TryParseProfileAbout(page, profile);
            TryParseProfileWarnings(page, profile);
            FillProfileFallbacks(page, profile);

            return profile;
        }

        private static void TryParseProfileMainBlock(string page, ForumUserProfile profile)
        {
            string mainPattern = "<div[^>]*?user-box[\\s\\S]*?<img src=\"([^\"]*?)\"[\\s\\S]*?<h1>([^<]*?)</h1>[\\s\\S]*?(?=<span class=\"title\">([^<]*?)</span>| )[\\s\\S]*?<h2>(?:<span style[^>]*?>|)([^\"<]*?)(?:</span>|)</h2>[\\s\\S]*?(<ul[\\s\\S]*?</ul>)[\\s\\S]*?<div class=\"u-note\">([\\s\\S]*?)</div>[^<]*?(?:</li>|<div)[\\s\\S]*?(<ul[\\s\\S]*?</ul>)[\\s\\S]*?(<ul[\\s\\S]*?</ul>)[\\s\\S]*?(<ul[\\s\\S]*?</ul>)[\\s\\S]*?(<ul[\\s\\S]*?</ul>)[\\s\\S]*?(<ul[\\s\\S]*?</ul>)";
            Match main = Regex.Match(page, mainPattern, RegexOptions.IgnoreCase | RegexOptions.Singleline);

            if (!main.Success)
                return;

            profile.AvatarUrl = NormalizeUrl(HtmlDecode(main.Groups[1].Value.Trim()));
            profile.Nick = CleanText(main.Groups[2].Value);
            profile.Status = CleanText(main.Groups[3].Value);
            profile.Group = CleanText(main.Groups[4].Value);

            ParseInfoList(main.Groups[5].Value, profile);

            string signature = main.Groups[6].Value == null ? "" : main.Groups[6].Value.Trim();
            if (!String.Equals(CleanText(signature), "Нет подписи", StringComparison.OrdinalIgnoreCase))
                profile.Signature = CleanText(signature);

            ParsePersonalList(main.Groups[7].Value, profile);
            ParseDevicesList(main.Groups[9].Value, profile);
            ParseSiteStats(main.Groups[10].Value, profile);
            ParseForumStats(main.Groups[11].Value, profile);
        }

        private static void ParseInfoList(string html, ForumUserProfile profile)
        {
            if (String.IsNullOrWhiteSpace(html))
                return;

            foreach (Match matcher in Regex.Matches(html, "<li[\\s\\S]*?title[^>]*?>([^>]*?)<[\\s\\S]*?div[^>]*>([\\s\\S]*?)</div>", RegexOptions.IgnoreCase | RegexOptions.Singleline))
            {
                string field = CleanText(matcher.Groups[1].Value);
                string value = CleanText(matcher.Groups[2].Value);

                if (field.IndexOf("Рег", StringComparison.OrdinalIgnoreCase) >= 0)
                    profile.RegistrationDate = value;
                else if (field.IndexOf("Последнее", StringComparison.OrdinalIgnoreCase) >= 0)
                    profile.LastVisit = value;
            }
        }

        private static void ParsePersonalList(string html, ForumUserProfile profile)
        {
            if (String.IsNullOrWhiteSpace(html))
                return;

            foreach (Match matcher in Regex.Matches(html, "<li[\\s\\S]*?title[^>]*?>([^>]*?)<[\\s\\S]*?(?=<div[^>]*>([^<]*)[\\s\\S]*?</div>|)<", RegexOptions.IgnoreCase | RegexOptions.Singleline))
            {
                string field = CleanText(matcher.Groups[1].Value);
                string value = CleanText(matcher.Groups[2].Value);

                if (String.IsNullOrWhiteSpace(value))
                {
                    if (String.IsNullOrWhiteSpace(profile.Gender))
                        profile.Gender = field;
                }
                else if (field.IndexOf("Дата", StringComparison.OrdinalIgnoreCase) >= 0)
                    profile.Birthday = value;
                else if (field.IndexOf("Время", StringComparison.OrdinalIgnoreCase) >= 0)
                    profile.UserTime = value;
                else if (field.IndexOf("Город", StringComparison.OrdinalIgnoreCase) >= 0)
                    profile.City = value;
            }
        }

        private static void ParseSiteStats(string html, ForumUserProfile profile)
        {
            if (String.IsNullOrWhiteSpace(html))
                return;

            foreach (Match matcher in Regex.Matches(html, "<span class=\"title\">([^<]*?)</span>[\\s\\S]*?<div class=\"area\">[\\s\\S]*?(?:<a[^>]*?href=\"([^\"]*?)\"[^>]*?>)?([\\s\\S]*?)(?:</a>)?</div>", RegexOptions.IgnoreCase | RegexOptions.Singleline))
            {
                string field = CleanText(matcher.Groups[1].Value);
                string value = CleanText(matcher.Groups[3].Value);

                if (field.IndexOf("Карма", StringComparison.OrdinalIgnoreCase) >= 0)
                    profile.SiteKarma = value;
                else if (field.IndexOf("Постов", StringComparison.OrdinalIgnoreCase) >= 0)
                    profile.SitePosts = value;
                else if (field.IndexOf("Комментов", StringComparison.OrdinalIgnoreCase) >= 0)
                    profile.SiteComments = value;
            }
        }

        private static void ParseForumStats(string html, ForumUserProfile profile)
        {
            if (String.IsNullOrWhiteSpace(html))
                return;

            foreach (Match matcher in Regex.Matches(html, "<span class=\"title\">([^<]*?)</span>[\\s\\S]*?<div class=\"area\">(?:(\\d+)|[\\s\\S]*?<a[^>]*?href=\"([^\"]*?act=(?:search|rep[^\"]*?view=history)[^\"]*?)\"[^>]*?>(?:<span[^>]*?>)?(-?\\d+)(?:</span>)?</a>)", RegexOptions.IgnoreCase | RegexOptions.Singleline))
            {
                string field = CleanText(matcher.Groups[1].Value);
                string value = matcher.Groups[4].Success && !String.IsNullOrWhiteSpace(matcher.Groups[4].Value)
                    ? matcher.Groups[4].Value
                    : matcher.Groups[2].Value;

                if (field.IndexOf("Репу", StringComparison.OrdinalIgnoreCase) >= 0)
                    profile.Reputation = value;
                else if (field.IndexOf("Тем", StringComparison.OrdinalIgnoreCase) >= 0)
                    profile.ForumTopics = value;
                else if (field.IndexOf("Постов", StringComparison.OrdinalIgnoreCase) >= 0)
                    profile.ForumPosts = value;
            }
        }

        private static void ParseDevicesList(string html, ForumUserProfile profile)
        {
            if (String.IsNullOrWhiteSpace(html) || profile == null)
                return;

            foreach (Match matcher in Regex.Matches(html, "<a[^>]*?href=\"([^\"]*)\"[^>]*?>([\\s\\S]*?)</a>([\\s\\S]*?)</li>", RegexOptions.IgnoreCase | RegexOptions.Singleline))
            {
                string url = NormalizeUrl(HtmlDecode(matcher.Groups[1].Value.Trim()));
                string name = CleanText(matcher.Groups[2].Value);
                string accessory = CleanText(matcher.Groups[3].Value);
                AddProfileDevice(profile, name, accessory, url);
            }
        }

        private static void TryParseProfileDevices(string page, ForumUserProfile profile)
        {
            if (String.IsNullOrWhiteSpace(page) || profile == null || profile.Devices.Count > 0)
                return;

            string devicesBlock = ExtractFirst(page, "(?<value><ul[^>]*>[\\s\\S]*?(?:devdb|devices|Устройства|Мои устройства)[\\s\\S]*?</ul>)");
            ParseDevicesList(devicesBlock, profile);

            if (profile.Devices.Count > 0)
                return;

            foreach (Match matcher in Regex.Matches(page, "<a[^>]*?href=\"(?<url>[^\"]*(?:devdb|devices)[^\"]*)\"[^>]*?>(?<name>[\\s\\S]*?)</a>(?<accessory>[\\s\\S]*?)</li>", RegexOptions.IgnoreCase | RegexOptions.Singleline))
            {
                string url = NormalizeUrl(HtmlDecode(matcher.Groups["url"].Value.Trim()));
                string name = CleanText(matcher.Groups["name"].Value);
                string accessory = CleanText(matcher.Groups["accessory"].Value);
                AddProfileDevice(profile, name, accessory, url);
            }
        }

        private static void AddProfileDevice(ForumUserProfile profile, string name, string accessory, string url)
        {
            if (profile == null || String.IsNullOrWhiteSpace(name))
                return;

            string cleanName = name.Trim();
            string cleanAccessory = String.IsNullOrWhiteSpace(accessory) ? "" : accessory.Trim();
            string cleanUrl = String.IsNullOrWhiteSpace(url) ? "" : url.Trim();

            for (int i = 0; i < profile.Devices.Count; i++)
            {
                ForumUserDevice existing = profile.Devices[i];
                if (String.Equals(existing.Name, cleanName, StringComparison.OrdinalIgnoreCase) &&
                    String.Equals(existing.Url, cleanUrl, StringComparison.OrdinalIgnoreCase))
                    return;
            }

            profile.Devices.Add(new ForumUserDevice
            {
                Name = cleanName,
                Accessory = cleanAccessory,
                Url = cleanUrl
            });
        }

        private static void TryParseProfileAbout(string page, ForumUserProfile profile)
        {
            string about = ExtractFirst(page, "<div[^>]*?div-custom-about[^>]*?>(?<value>[\\s\\S]*?)</div>");
            profile.About = CleanText(about);
        }

        private static void TryParseProfileWarnings(string page, ForumUserProfile profile)
        {
            int count = Regex.Matches(page, "<li class=\"wlog-", RegexOptions.IgnoreCase).Count;
            if (count > 0)
                profile.Warnings = count.ToString();
        }

        private static void FillProfileFallbacks(string page, ForumUserProfile profile)
        {
            string text = profile.RawText;

            if (String.IsNullOrWhiteSpace(profile.Nick))
                profile.Nick = CleanText(ExtractFirst(page, @"<h1[^>]*>(?<value>[\\s\\S]*?)</h1>"));

            if (String.IsNullOrWhiteSpace(profile.AvatarUrl))
            {
                string imageTag = ExtractFirst(page, @"<img\b(?=[^>]*(avatar|photo|user-box))(?<value>[^>]*)>");
                if (String.IsNullOrWhiteSpace(imageTag))
                    imageTag = ExtractFirst(page, @"<img\b(?<value>[^>]*)>");
                profile.AvatarUrl = NormalizeUrl(HtmlDecode(ExtractAttribute(imageTag, "src")));
            }

            if (String.IsNullOrWhiteSpace(profile.Group))
                profile.Group = ExtractTextField(text, "Группа");

            if (String.IsNullOrWhiteSpace(profile.ForumPosts))
                profile.ForumPosts = ExtractTextField(text, "Сообщений");

            if (String.IsNullOrWhiteSpace(profile.Reputation))
                profile.Reputation = ExtractTextField(text, "Репутация");

            if (String.IsNullOrWhiteSpace(profile.RegistrationDate))
                profile.RegistrationDate = ExtractTextField(text, "Регистрация");

            if (String.IsNullOrWhiteSpace(profile.LastVisit))
            {
                profile.LastVisit = ExtractTextField(text, "Последнее посещение");
                if (String.IsNullOrWhiteSpace(profile.LastVisit))
                    profile.LastVisit = ExtractTextField(text, "Последнее");
            }

            if (String.IsNullOrWhiteSpace(profile.Status))
            {
                profile.Status = ExtractTextField(text, "Статус");
                if (String.IsNullOrWhiteSpace(profile.Status))
                    profile.Status = ExtractTextField(text, "Звание");
            }
        }

        private static void CheckLogin(string pageBody, ForumLoginResult loginResult)
        {
            if (String.IsNullOrWhiteSpace(pageBody) || loginResult == null)
                return;

            // Точная идея из ForPDA: на странице после входа должны быть showuser=ID и logout с k=32.
            Match matcher = Regex.Match(pageBody,
                "showuser=(?<id>\\d+)\"[\\s\\S]*?action=logout[^\"']*?[;&]k=(?<k>[a-z0-9]{32})",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);

            if (!matcher.Success)
            {
                // Более мягкий вариант для других HTML-шаблонов 4PDA.
                matcher = Regex.Match(pageBody,
                    "showuser=(?<id>\\d+)[\\s\\S]*?act=(?:logout|Login)[^\"']*?[;&]k=(?<k>[a-z0-9]{32})",
                    RegexOptions.IgnoreCase | RegexOptions.Singleline);
            }

            if (matcher.Success)
            {
                loginResult.UserId = matcher.Groups["id"].Value;
                loginResult.K = matcher.Groups["k"].Value;
                loginResult.Success = true;
            }
            else
            {
                string k = ExtractValue(pageBody, @"act=(?:logout|Login)[^""']*?[;&]k=(?<value>[a-z0-9]{32})");
                if (!String.IsNullOrWhiteSpace(k))
                    loginResult.K = k;

                string userId = ExtractValue(pageBody, "showuser=(?<value>\\d+)");
                if (!String.IsNullOrWhiteSpace(userId) && !String.IsNullOrWhiteSpace(k))
                {
                    loginResult.UserId = userId;
                    loginResult.Success = true;
                }
            }

            string avatarTag = ExtractFirst(pageBody, @"<img\b(?=[^>]*(avatar|photo))(?<value>[^>]*)>");
            string avatar = NormalizeUrl(HtmlDecode(ExtractAttribute(avatarTag, "src")));
            if (!String.IsNullOrWhiteSpace(avatar))
                loginResult.UserAvatarUrl = avatar;
        }

        private static string ExtractLoginError(string html)
        {
            string error = TryExtractLoginError(html);
            return String.IsNullOrWhiteSpace(error) ? "Не удалось подтвердить авторизацию." : error;
        }

        private static string TryExtractLoginError(string html)
        {
            if (String.IsNullOrWhiteSpace(html))
                return "";

            string errorList = ExtractFirst(html, "errors-list[^>]*>(?<value>[\\s\\S]*?)(?:</ul>|</div>|<form|$)");
            if (!String.IsNullOrWhiteSpace(errorList))
                return CleanText(errorList);

            string errorText = ExtractFirst(html, "wr va-m text[^>]*>(?<value>[\\s\\S]*?)(?:</div>|<br|$)");
            if (!String.IsNullOrWhiteSpace(errorText))
                return CleanText(errorText);

            string text = CleanText(html);
            string reason = ExtractValue(text, @"Причина:\s*(?<value>[^\r\n]+)");

            if (!String.IsNullOrWhiteSpace(reason))
                return reason;

            if (text.IndexOf("Неверно введено слово", StringComparison.OrdinalIgnoreCase) >= 0 ||
                text.IndexOf("код подтверждения", StringComparison.OrdinalIgnoreCase) >= 0)
                return "Неверно введена капча.";

            if (text.IndexOf("Неверное имя пользователя", StringComparison.OrdinalIgnoreCase) >= 0 ||
                text.IndexOf("Неверный пароль", StringComparison.OrdinalIgnoreCase) >= 0)
                return "Неправильный логин или пароль.";

            return "";
        }

        private static string BuildAuthFailureDiagnostic(string html)
        {
            string text = CleanText(html);

            if (text.IndexOf("hcaptcha", StringComparison.OrdinalIgnoreCase) >= 0 ||
                text.IndexOf("h-captcha", StringComparison.OrdinalIgnoreCase) >= 0)
                return "4PDA вернула антибот/hCaptcha-страницу. Старый вход через act=auth не может пройти эту проверку.";

            if (text.IndexOf("captcha", StringComparison.OrdinalIgnoreCase) >= 0 ||
                text.IndexOf("капч", StringComparison.OrdinalIgnoreCase) >= 0)
                return "Авторизация не подтверждена. Скорее всего, неверно введена капча или 4PDA изменила формат проверки.";

            if (text.IndexOf("password", StringComparison.OrdinalIgnoreCase) >= 0 ||
                text.IndexOf("парол", StringComparison.OrdinalIgnoreCase) >= 0)
                return "Авторизация не подтверждена. Проверьте логин и пароль.";

            return "Авторизация не подтверждена: cookie member_id не пришла и ссылка logout не найдена.";
        }

        private static void ApplyCookieLoginResult(ForumLoginResult result, string login)
        {
            if (result == null)
                return;

            string cookieUserId = GetCookieValue("member_id");
            string passHash = GetCookieValue("pass_hash");

            if (!String.IsNullOrWhiteSpace(cookieUserId) &&
                !String.Equals(cookieUserId, "deleted", StringComparison.OrdinalIgnoreCase) &&
                !String.Equals(cookieUserId, "0", StringComparison.OrdinalIgnoreCase))
            {
                result.UserId = cookieUserId;
                result.UserLogin = login;
                result.Success = true;
            }

            if (String.Equals(cookieUserId, "deleted", StringComparison.OrdinalIgnoreCase) ||
                String.Equals(passHash, "deleted", StringComparison.OrdinalIgnoreCase))
            {
                result.Success = false;
                result.ErrorMessage = "Неправильный логин, пароль или капча.";
            }
        }

        private static string ExtractInputValue(string html, string name)
        {
            if (String.IsNullOrWhiteSpace(html))
                return "";

            string input = ExtractFirst(html, "<input\\b(?=[^>]*\\bname\\s*=\\s*['\"]?" + Regex.Escape(name) + "['\"]?)[^>]*>");
            return ExtractAttribute(input, "value");
        }

        private static string ExtractAttribute(string tag, string name)
        {
            if (String.IsNullOrWhiteSpace(tag))
                return "";

            string pattern = "\\b" + Regex.Escape(name) + "\\s*=\\s*(?:\"(?<value>[^\"]*)\"|'(?<value>[^']*)'|(?<value>[^\\s>]+))";
            Match match = Regex.Match(tag, pattern, RegexOptions.IgnoreCase | RegexOptions.Singleline);
            return match.Success ? match.Groups["value"].Value : "";
        }

        private static string ExtractFirst(string text, string pattern)
        {
            if (String.IsNullOrEmpty(text))
                return "";

            Match match = Regex.Match(text, pattern, RegexOptions.IgnoreCase | RegexOptions.Singleline);
            return match.Success ? match.Groups["value"].Value : "";
        }

        private static string ExtractValue(string text, string pattern)
        {
            return ExtractFirst(text, pattern);
        }

        private static string ExtractTextField(string text, string title)
        {
            if (String.IsNullOrWhiteSpace(text))
                return "";

            string pattern = Regex.Escape(title) + @"\s*:?\s*(?<value>[^\r\n]+)";
            string value = ExtractValue(text, pattern);
            return value.Trim();
        }

        private static string CleanText(string html)
        {
            if (String.IsNullOrWhiteSpace(html))
                return "";

            string text = Regex.Replace(html, @"<\s*br\s*/?\s*>", "\n", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"</\s*(p|div|li|tr|h1|h2|h3|h4|h5|h6)\s*>", "\n", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"<[^>]+>", " ");
            text = HtmlDecode(text);
            text = Regex.Replace(text, @"[ \t]+", " ");
            text = Regex.Replace(text, @"\s*\n\s*", "\n");
            text = Regex.Replace(text, @"\n{2,}", "\n");
            return text.Trim();
        }

        private static string HtmlDecode(string value)
        {
            if (String.IsNullOrEmpty(value))
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

        private static string NormalizeUrl(string url)
        {
            if (String.IsNullOrWhiteSpace(url))
                return "";

            url = url.Trim();

            if (url.StartsWith("//", StringComparison.Ordinal))
                return "https:" + url;

            Uri absolute;
            if (Uri.TryCreate(url, UriKind.Absolute, out absolute))
                return absolute.ToString();

            return new Uri(new Uri("https://" + Host + "/"), url).ToString();
        }

        private static string GetCookieValue(string name)
        {
            string[] urls =
            {
                "https://" + Host + "/",
                "https://." + Host + "/",
                "https://" + Host + "/forum/",
                "https://" + Host + "/forum/index.php"
            };

            foreach (string url in urls)
            {
                try
                {
                    foreach (HttpCookie cookie in Filter.CookieManager.GetCookies(new Uri(url, UriKind.Absolute)))
                    {
                        if (String.Equals(cookie.Name, name, StringComparison.OrdinalIgnoreCase))
                            return cookie.Value;
                    }
                }
                catch
                {
                }
            }

            return "";
        }

        private static void SaveCurrentUser(ForumLoginResult result)
        {
            WriteSetting(KeyUserId, result.UserId);
            WriteSetting(KeyUserLogin, result.UserLogin);
            WriteSetting(KeyUserAvatar, result.UserAvatarUrl);
            WriteSetting(KeyLogoutK, result.K);
            NotifyAccountChanged();
        }

        private static string OnlyDigits(string value)
        {
            if (String.IsNullOrWhiteSpace(value))
                return "";
            return Regex.Replace(value, @"\D", "");
        }

        private static string ReadSetting(string key)
        {
            object value;
            if (ApplicationData.Current.LocalSettings.Values.TryGetValue(key, out value) && value != null)
                return value.ToString();

            return "";
        }

        private static void WriteSetting(string key, string value)
        {
            if (String.IsNullOrWhiteSpace(value))
                ApplicationData.Current.LocalSettings.Values.Remove(key);
            else
                ApplicationData.Current.LocalSettings.Values[key] = value;
        }


    }

    internal sealed class FormPostField
    {
        public string Name { get; set; }
        public string Value { get; set; }
        public bool AlreadyEncoded { get; set; }
    }

    internal sealed class ForumLoginForm
    {
        public ForumLoginForm()
        {
            Fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        public string K { get; set; }
        public string PostUrl { get; set; }
        public string CaptchaUrl { get; set; }
        public string CaptchaTime { get; set; }
        public string CaptchaSig { get; set; }
        public string Session { get; set; }
        public Dictionary<string, string> Fields { get; set; }

        public bool CaptchaRequired
        {
            get
            {
                return !String.IsNullOrWhiteSpace(CaptchaUrl) ||
                    !String.IsNullOrWhiteSpace(CaptchaTime) ||
                    !String.IsNullOrWhiteSpace(CaptchaSig);
            }
        }
    }

    internal sealed class ForumLoginResult
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; }
        public string UserId { get; set; }
        public string UserLogin { get; set; }
        public string UserAvatarUrl { get; set; }
        public string K { get; set; }
        public string CaptchaUrl { get; set; }
    }

    internal sealed class ForumUserProfile
    {
        public ForumUserProfile()
        {
            Devices = new List<ForumUserDevice>();
        }

        public string Id { get; set; }
        public string Nick { get; set; }
        public string AvatarUrl { get; set; }
        public string Group { get; set; }
        public string Status { get; set; }
        public string Reputation { get; set; }
        public string ForumPosts { get; set; }
        public string ForumTopics { get; set; }
        public string SiteKarma { get; set; }
        public string SitePosts { get; set; }
        public string SiteComments { get; set; }
        public string RegistrationDate { get; set; }
        public string LastVisit { get; set; }
        public string Birthday { get; set; }
        public string UserTime { get; set; }
        public string City { get; set; }
        public string Gender { get; set; }
        public string Signature { get; set; }
        public string About { get; set; }
        public string Warnings { get; set; }
        public List<ForumUserDevice> Devices { get; set; }
        public string RawText { get; set; }
    }

    internal sealed class ForumUserDevice
    {
        public string Name { get; set; }
        public string Accessory { get; set; }
        public string Url { get; set; }
    }

    public static class ForumCookieHelper
    {
        public const string ForumRoot = "https://4pda.to/";
        public const string ForumUrl = "https://4pda.to/forum/";

        // Важно: этот UA совпадает с WebView/IE Mobile на WP 8.1.
        // cf_clearance обычно привязан к User-Agent, поэтому HTTP-запросы скачивания должны идти с тем же UA,
        // с которым пользователь прошёл проверку в WebView.
        public const string UserAgent = "Mozilla/5.0 (Windows NT 6.3; Trident/7.0; rv:11.0) like Gecko";
        public const string AcceptLanguage = "ru-RU,ru;q=0.8,en-US;q=0.6,en;q=0.4";

        private static readonly string[] KnownCookieNames = new string[]
        {
            "ngx_mb",
            "member_id",
            "pass_hash",
            "session_id",
            "anonymous",
            "cf_clearance"
        };

        private static readonly string[] ForumCookieProbeUrls = new string[]
        {
            "https://4pda.to/",
            "https://4pda.to/forum/",
            "https://4pda.to/forum/index.php",
            "http://4pda.to/",
            "http://4pda.to/forum/"
        };

        public static void ClearForumAuthenticationCookies(HttpCookieManager primaryCookieManager)
        {
            ClearSavedAuthenticationCookies();
            DeleteAuthenticationCookies(primaryCookieManager);

            try
            {
                using (HttpBaseProtocolFilter filter = new HttpBaseProtocolFilter())
                    DeleteAuthenticationCookies(filter.CookieManager);
            }
            catch
            {
            }
        }

        private static void ClearSavedAuthenticationCookies()
        {
            try
            {
                ApplicationDataContainer settings = ApplicationData.Current.LocalSettings;
                var keys = new List<string>();
                foreach (KeyValuePair<string, object> item in settings.Values)
                    keys.Add(item.Key);

                foreach (string key in keys)
                {
                    string cookieName = key ?? "";
                    if (cookieName.StartsWith("cookie_", StringComparison.OrdinalIgnoreCase))
                        cookieName = cookieName.Substring("cookie_".Length);

                    if (IsAuthenticationCookieName(cookieName))
                        settings.Values.Remove(key);
                }
            }
            catch
            {
            }
        }

        private static void DeleteAuthenticationCookies(HttpCookieManager cookieManager)
        {
            if (cookieManager == null)
                return;

            for (int i = 0; i < ForumCookieProbeUrls.Length; i++)
            {
                try
                {
                    Uri uri = new Uri(ForumCookieProbeUrls[i], UriKind.Absolute);
                    HttpCookieCollection source = cookieManager.GetCookies(uri);
                    var cookies = new List<HttpCookie>();
                    foreach (HttpCookie cookie in source)
                    {
                        if (cookie != null && IsAuthenticationCookieName(cookie.Name))
                            cookies.Add(cookie);
                    }

                    foreach (HttpCookie cookie in cookies)
                    {
                        try { cookieManager.DeleteCookie(cookie); } catch { }
                    }
                }
                catch
                {
                }
            }
        }

        private static bool IsAuthenticationCookieName(string name)
        {
            if (String.IsNullOrWhiteSpace(name))
                return false;

            return name.Equals("member_id", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("pass_hash", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("session_id", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("anonymous", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("ipb_member_id", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("ipb_pass_hash", StringComparison.OrdinalIgnoreCase) ||
                name.IndexOf("member_id", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("pass_hash", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("session_id", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.StartsWith("ipsconnect_", StringComparison.OrdinalIgnoreCase);
        }

        public static void ApplyDefaultHeaders(HttpClient client)
        {
            if (client == null)
                return;

            try { client.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent); } catch { }
            try { client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("ru-RU"); } catch { }
            try { client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("ru;q=0.8"); } catch { }
            try { client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en-US;q=0.6"); } catch { }
            try { client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en;q=0.4"); } catch { }
        }

        public static void ApplyDefaultHeaders(HttpRequestMessage request)
        {
            if (request == null)
                return;

            try { request.Headers.UserAgent.ParseAdd(UserAgent); } catch { }
            try { request.Headers.AcceptLanguage.ParseAdd("ru-RU"); } catch { }
            try { request.Headers.AcceptLanguage.ParseAdd("ru;q=0.8"); } catch { }
            try { request.Headers.AcceptLanguage.ParseAdd("en-US;q=0.6"); } catch { }
            try { request.Headers.AcceptLanguage.ParseAdd("en;q=0.4"); } catch { }
            TryAppendHeader(request, "Accept-Language", AcceptLanguage);
        }

        public static void TryAppendHeader(HttpRequestMessage request, string name, string value)
        {
            if (request == null || String.IsNullOrWhiteSpace(name) || String.IsNullOrWhiteSpace(value))
                return;

            try
            {
                request.Headers.TryAppendWithoutValidation(name, value);
            }
            catch
            {
                try { request.Headers.Append(name, value); } catch { }
            }
        }

        public static string GetCookieHeaderForUrl(Uri uri)
        {
            return GetCookieHeaderForUrl(uri, true);
        }

        public static string GetCookieHeaderForUrl(Uri uri, bool includeAuthentication)
        {
            // Проверяем границу домена, прежде чем читать или отправлять cookies.
            if (uri == null || !(uri.Host.Equals("4pda.to", StringComparison.OrdinalIgnoreCase)
                || uri.Host.EndsWith(".4pda.to", StringComparison.OrdinalIgnoreCase)))
                return string.Empty;

            Dictionary<string, string> cookies = GetKnownCookies();
            if (!cookies.ContainsKey("ngx_mb"))
                cookies["ngx_mb"] = "1";

            // Публичный fallback всё ещё нуждается в cookie проверки доступа и
            // выборе мобильной разметки, но не должен отправлять сессию аккаунта.
            if (!includeAuthentication)
            {
                var publicCookies = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (string name in new string[] { "ngx_mb", "cf_clearance" })
                {
                    string value;
                    if (cookies.TryGetValue(name, out value))
                        publicCookies[name] = value;
                }
                cookies = publicCookies;
            }

            List<string> pairs = new List<string>();
            foreach (string name in KnownCookieNames)
            {
                string value;
                if (cookies.TryGetValue(name, out value) && !String.IsNullOrWhiteSpace(value))
                    pairs.Add(name + "=" + value);
            }

            foreach (KeyValuePair<string, string> pair in cookies)
            {
                if (Array.IndexOf(KnownCookieNames, pair.Key) >= 0)
                    continue;
                if (!String.IsNullOrWhiteSpace(pair.Value))
                    pairs.Add(pair.Key + "=" + pair.Value);
            }

            return String.Join("; ", pairs.ToArray());
        }

        public static Dictionary<string, string> GetKnownCookies()
        {
            Dictionary<string, string> result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            result["ngx_mb"] = "1";

            ApplicationDataContainer settings = ApplicationData.Current.LocalSettings;
            foreach (KeyValuePair<string, object> item in settings.Values)
            {
                string key = item.Key ?? "";
                string value = item.Value == null ? "" : item.Value.ToString();
                if (String.IsNullOrWhiteSpace(value))
                    continue;

                if (key.StartsWith("cookie_", StringComparison.OrdinalIgnoreCase))
                {
                    string name = key.Substring("cookie_".Length);
                    string cookieValue = ExtractCookieValue(name, value);
                    if (!String.IsNullOrWhiteSpace(name) && !String.IsNullOrWhiteSpace(cookieValue))
                        result[name] = cookieValue;
                }
                else if (Array.IndexOf(KnownCookieNames, key) >= 0)
                {
                    result[key] = value;
                }
            }

            return result;
        }

        public static bool HasCookie(string name)
        {
            if (String.IsNullOrWhiteSpace(name))
                return false;
            Dictionary<string, string> cookies = GetKnownCookies();
            string value;
            return cookies.TryGetValue(name, out value) && !String.IsNullOrWhiteSpace(value);
        }

        public static string DescribeKnownCookies()
        {
            return DescribeCookieHeader(GetCookieHeaderForUrl(new Uri(ForumRoot)));
        }

        public static string DescribeCookieHeader(string cookieHeader)
        {
            if (String.IsNullOrWhiteSpace(cookieHeader))
                return "<none>";

            List<string> parts = new List<string>();
            string[] pairs = cookieHeader.Split(new char[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string raw in pairs)
            {
                string pair = raw.Trim();
                int eq = pair.IndexOf('=');
                if (eq <= 0)
                    continue;
                string name = pair.Substring(0, eq).Trim();
                string value = pair.Substring(eq + 1).Trim();
                parts.Add(name + "(" + value.Length.ToString() + ")");
            }

            return parts.Count == 0 ? "<none>" : String.Join(", ", parts.ToArray());
        }

        public static void SaveForumCookiesFromSystemCookieManager(string reason)
        {
            SaveForumCookiesFromSystemCookieManager(new Uri(ForumRoot), reason);
            SaveForumCookiesFromSystemCookieManager(new Uri(ForumUrl), reason);
        }

        private static void SaveForumCookiesFromSystemCookieManager(Uri uri, string reason)
        {
            try
            {
                HttpBaseProtocolFilter filter = new HttpBaseProtocolFilter();
                HttpCookieCollection cookies = filter.CookieManager.GetCookies(uri);
                foreach (HttpCookie cookie in cookies)
                    SaveKnownCookie(cookie, uri, reason);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[4PDA.Auth] " + DateTime.Now.ToString("HH:mm:ss.fff") + " SaveForumCookiesFromSystemCookieManager failed uri=" + uri + " reason=" + reason + " error=" + ex.Message);
            }
        }

        private static void SaveKnownCookie(HttpCookie cookie, Uri sourceUri, string reason)
        {
            if (cookie == null || String.IsNullOrWhiteSpace(cookie.Name) || String.IsNullOrWhiteSpace(cookie.Value))
                return;

            if (!IsKnownForumCookie(cookie.Name))
                return;

            SaveCookieValue(cookie.Name, cookie.Value, sourceUri == null ? ForumRoot : sourceUri.AbsoluteUri, reason);
        }

        public static void SaveCookieValue(string name, string value, string url, string reason)
        {
            if (String.IsNullOrWhiteSpace(name) || String.IsNullOrWhiteSpace(value))
                return;

            try
            {
                string normalizedUrl = String.IsNullOrWhiteSpace(url) ? ForumRoot : url;
                string stored = normalizedUrl + "|:|" + name + "=" + value;
                ApplicationData.Current.LocalSettings.Values["cookie_" + name] = stored;
                if (name.Equals("member_id", StringComparison.OrdinalIgnoreCase))
                    ApplicationData.Current.LocalSettings.Values["member_id"] = value;

                Debug.WriteLine("[4PDA.Auth] " + DateTime.Now.ToString("HH:mm:ss.fff") + " saved cookie_" + name + "(" + value.Length.ToString() + ") reason=" + reason);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[4PDA.Auth] " + DateTime.Now.ToString("HH:mm:ss.fff") + " SaveCookieValue failed name=" + name + " error=" + ex.Message);
            }
        }

        private static bool IsKnownForumCookie(string name)
        {
            if (String.IsNullOrWhiteSpace(name))
                return false;
            return Array.IndexOf(KnownCookieNames, name) >= 0;
        }

        private static string ExtractCookieValue(string expectedName, string raw)
        {
            if (String.IsNullOrWhiteSpace(raw))
                return "";

            string value = raw.Trim();
            int split = value.IndexOf("|:|", StringComparison.Ordinal);
            if (split >= 0)
                value = value.Substring(split + 3);

            int semicolon = value.IndexOf(';');
            string firstPair = semicolon >= 0 ? value.Substring(0, semicolon) : value;
            int eq = firstPair.IndexOf('=');
            if (eq >= 0)
            {
                string name = firstPair.Substring(0, eq).Trim();
                string cookieValue = firstPair.Substring(eq + 1).Trim();
                if (String.IsNullOrWhiteSpace(expectedName) || name.Equals(expectedName, StringComparison.OrdinalIgnoreCase))
                    return cookieValue;
            }

            return value;
        }
    }

}
