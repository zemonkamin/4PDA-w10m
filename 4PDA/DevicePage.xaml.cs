using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Threading;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using _4PDA.Services;
using Windows.Data.Html;
using Windows.Foundation.Metadata;
using Windows.Security.Cryptography;
using Windows.System.Profile;
using Windows.Storage.Streams;
using Windows.System;
using Windows.UI.Core;
using Windows.UI.ViewManagement;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media.Imaging;
using Windows.UI.Xaml.Navigation;
using Windows.Web.Http;

namespace _4PDA
{
    public sealed partial class DevicePage : Page
    {
        private const string FourPdaRoot = "https://4pda.to/";
        private static readonly object DeviceCacheSync = new object();
        private static readonly Dictionary<string, DevicePageData> DeviceDataCache = new Dictionary<string, DevicePageData>(StringComparer.OrdinalIgnoreCase);
        private static readonly SemaphoreSlim StatusBarOperationLock = new SemaphoreSlim(1, 1);
        private static int StatusBarOwnerSeed;
        private static int ActiveStatusBarOwner;
        private static bool StatusBarUnavailable;
        private readonly int _statusBarOwnerId = Interlocked.Increment(ref StatusBarOwnerSeed);
        private readonly ObservableCollection<DeviceInfoRow> _summaryRows = new ObservableCollection<DeviceInfoRow>();
        private readonly ObservableCollection<DeviceSpecificationGroup> _specificationGroups = new ObservableCollection<DeviceSpecificationGroup>();
        private readonly ObservableCollection<DeviceRelatedItem> _relatedItems = new ObservableCollection<DeviceRelatedItem>();
        private DeviceNavigationArgs _navigationArgs;
        private string _deviceUrl = "";
        private bool _loading;
        private int _loadGeneration;
        private bool _compactLayout;
        private int _loadedImagePixelWidth;
        private int _loadedImagePixelHeight;
        private bool _isPageActive;

        public DevicePage()
        {
            InitializeComponent();
            SummaryItemsControl.ItemsSource = _summaryRows;
            SpecificationGroupsItemsControl.ItemsSource = _specificationGroups;
            RelatedItemsListView.ItemsSource = _relatedItems;
            DeviceImage.RegisterPropertyChangedCallback(Image.SourceProperty, delegate
            {
                var bitmap = DeviceImage.Source as BitmapSource;
                if (bitmap == null) return;
                _loadedImagePixelWidth = bitmap.PixelWidth;
                _loadedImagePixelHeight = bitmap.PixelHeight;
                DeviceImagePlaceholder.Visibility = Visibility.Collapsed;
                UpdateDeviceImageSize();
            });
            UpdateResponsiveLayout(Window.Current.Bounds.Width);
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            _isPageActive = true;
            _navigationArgs = e.Parameter as DeviceNavigationArgs ?? new DeviceNavigationArgs();
            _deviceUrl = ResolveDeviceUrl(_navigationArgs);

            string initialTitle = NormalizeText(_navigationArgs.Name);
            if (!String.IsNullOrWhiteSpace(initialTitle))
                DeviceTitleTextBlock.Text = initialTitle;

            string accessory = NormalizeText(_navigationArgs.Accessory);
            DeviceAccessoryTextBlock.Text = accessory;
            DeviceAccessoryTextBlock.Visibility = String.IsNullOrWhiteSpace(accessory) ? Visibility.Collapsed : Visibility.Visible;

            await LoadDeviceAsync(false);
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            _isPageActive = false;
            _loadGeneration++;
            _loading = false;
            SetLoading(false);
            base.OnNavigatedFrom(e);
        }

        private async void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            await LoadDeviceAsync(true);
        }

        private async void RelatedItemsListView_ItemClick(object sender, ItemClickEventArgs e)
        {
            DeviceRelatedItem item = e.ClickedItem as DeviceRelatedItem;
            if (item == null)
                return;

            Uri uri;
            if (!Uri.TryCreate(item.Url, UriKind.Absolute, out uri))
                return;

            await Launcher.LaunchUriAsync(uri);
        }

        private void DevicePage_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdateResponsiveLayout(e.NewSize.Width);
        }

        private async Task LoadDeviceAsync(bool forceRefresh)
        {
            if (_loading)
                return;

            int generation = ++_loadGeneration;
            _loading = true;
            SetLoading(true);
            SetStatus("");

            try
            {
                if (String.IsNullOrWhiteSpace(_deviceUrl))
                    throw new InvalidOperationException("в профиле не найдена ссылка на устройство");

                DevicePageData cachedData;
                if (!forceRefresh && TryGetCachedDeviceData(_deviceUrl, out cachedData))
                {
                    ApplyData(cachedData);
                    BindDeviceImage(cachedData);
                    return;
                }

                DevicePageData data = await OfficialApiService.GetDeviceAsync(_deviceUrl);
                if (generation != _loadGeneration)
                    return;

                if (String.IsNullOrWhiteSpace(data.ImageUrl) && data.ImageUrls != null && data.ImageUrls.Count > 0)
                    data.ImageUrl = data.ImageUrls[0];

                StoreCachedDeviceData(_deviceUrl, data);
                ApplyData(data);
                BindDeviceImage(data);
            }
            catch (Exception ex)
            {
                if (generation == _loadGeneration)
                    SetStatus("Не удалось загрузить данные устройства: " + ex.Message);
            }
            finally
            {
                if (generation == _loadGeneration)
                {
                    SetLoading(false);
                    _loading = false;
                }
            }
        }

        private static bool TryGetCachedDeviceData(string url, out DevicePageData data)
        {
            lock (DeviceCacheSync)
                return DeviceDataCache.TryGetValue(url ?? "", out data);
        }

        private static void StoreCachedDeviceData(string url, DevicePageData data)
        {
            if (String.IsNullOrWhiteSpace(url) || data == null)
                return;

            lock (DeviceCacheSync)
            {
                if (DeviceDataCache.Count >= (FourPdaImageService.IsMobile ? 6 : 24) && !DeviceDataCache.ContainsKey(url))
                    DeviceDataCache.Clear();
                DeviceDataCache[url] = data;
            }
        }

        private void PrepareRequest(HttpRequestMessage request, string referer)
        {
            ForumCookieHelper.ApplyDefaultHeaders(request);
            ForumCookieHelper.TryAppendHeader(request, "Referer", String.IsNullOrWhiteSpace(referer) ? FourPdaRoot : referer);
            string cookie = ForumCookieHelper.GetCookieHeaderForUrl(request.RequestUri);
            if (!String.IsNullOrWhiteSpace(cookie))
                ForumCookieHelper.TryAppendHeader(request, "Cookie", cookie);
        }

        private void ApplyData(DevicePageData data)
        {
            if (data == null)
                return;

            string title = FirstNonEmpty(data.Title, _navigationArgs == null ? "" : _navigationArgs.Name, "Устройство");
            DeviceTitleTextBlock.Text = title;

            string breadcrumb = JoinNonEmpty("  ›  ", data.CategoryTitle, data.BrandTitle);
            DeviceBreadcrumbTextBlock.Text = breadcrumb;
            DeviceBreadcrumbTextBlock.Visibility = String.IsNullOrWhiteSpace(breadcrumb) ? Visibility.Collapsed : Visibility.Visible;

            string accessory = FirstNonEmpty(_navigationArgs == null ? "" : _navigationArgs.Accessory, data.Subtitle);
            DeviceAccessoryTextBlock.Text = accessory;
            DeviceAccessoryTextBlock.Visibility = String.IsNullOrWhiteSpace(accessory) ? Visibility.Collapsed : Visibility.Visible;

            DeviceDescriptionTextBlock.Text = data.Description;
            DeviceDescriptionTextBlock.Visibility = String.IsNullOrWhiteSpace(data.Description) ? Visibility.Collapsed : Visibility.Visible;

            DeviceRatingTextBlock.Text = data.Rating > 0 ? "Рейтинг: " + data.Rating.ToString() : "";
            DeviceRatingTextBlock.Visibility = data.Rating > 0 ? Visibility.Visible : Visibility.Collapsed;

            _summaryRows.Clear();
            AddSummaryRow("производитель", data.BrandTitle);
            AddSummaryRow("категория", data.CategoryTitle);
            AddSummaryRow("модель", data.Title);
            AddSummaryRow("ID устройства", data.DeviceId);
            AddSummaryRow("вариант пользователя", _navigationArgs == null ? "" : _navigationArgs.Accessory);
            SummaryBorder.Visibility = _summaryRows.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

            _specificationGroups.Clear();
            if (data.SpecificationGroups != null)
            {
                foreach (DeviceSpecificationGroup group in data.SpecificationGroups)
                {
                    if (group != null && group.Items != null && group.Items.Count > 0)
                        _specificationGroups.Add(group);
                }
            }
            SpecificationsBorder.Visibility = _specificationGroups.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            var rows = new List<DeviceSpecificationViewRow>();
            foreach (DeviceSpecificationGroup group in _specificationGroups)
            {
                rows.Add(new DeviceSpecificationViewRow { Title = group.Title, IsHeading = true });
                foreach (DeviceInfoRow item in group.Items)
                    rows.Add(new DeviceSpecificationViewRow { Title = item.Title, Value = item.Value });
            }
            MobileDeviceList.ItemsSource = rows;

            _relatedItems.Clear();
            if (data.RelatedItems != null)
            {
                foreach (DeviceRelatedItem item in data.RelatedItems)
                {
                    if (item != null && !String.IsNullOrWhiteSpace(item.Title) && !String.IsNullOrWhiteSpace(item.Url))
                        _relatedItems.Add(item);
                }
            }
            RelatedBorder.Visibility = _relatedItems.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void AddSummaryRow(string title, string value)
        {
            string clean = NormalizeText(value);
            if (String.IsNullOrWhiteSpace(clean))
                return;

            _summaryRows.Add(new DeviceInfoRow { Title = title, Value = clean });
        }

        private void BindDeviceImage(DevicePageData data)
        {
            DeviceImagePlaceholder.Visibility = Visibility.Visible;
            _loadedImagePixelWidth = _loadedImagePixelHeight = 0;
            UpdateDeviceImageSize();
            DeferredImage.Attach(DeviceImage, data.ImageUrl, 440, 0);
        }

        private void SetLoading(bool loading)
        {
            if (DeviceContentScrollViewer != null)
                DeviceContentScrollViewer.Visibility = _compactLayout ? Visibility.Collapsed : Visibility.Visible;
            if (RefreshButton != null)
                RefreshButton.IsEnabled = !loading;
            if (DeviceProgressRing != null)
            {
                DeviceProgressRing.IsActive = loading;
                DeviceProgressRing.Visibility = loading ? Visibility.Visible : Visibility.Collapsed;
            }

            // На старых Windows 10 Mobile системный StatusBar иногда падает в нативном
            // RemoteAsync при быстрых переходах. Для DevicePage оставляем только
            // локальный индикатор, чтобы вход на страницу не трогал этот API.
            if (UseLocalDeviceProgressOnly())
                return;

            if (loading)
            {
                if (!_isPageActive)
                    return;
                Interlocked.Exchange(ref ActiveStatusBarOwner, _statusBarOwnerId);
            }
            else
            {
                // Старый экземпляр страницы не должен скрывать индикатор новой DevicePage.
                if (Interlocked.CompareExchange(ref ActiveStatusBarOwner, 0, _statusBarOwnerId) != _statusBarOwnerId)
                    return;
            }

            UpdateSystemStatusBarAsync(loading, _statusBarOwnerId);
        }

        private static bool UseLocalDeviceProgressOnly()
        {
            return true;
        }

        private async void UpdateSystemStatusBarAsync(bool loading, int ownerId)
        {
            if (StatusBarUnavailable || !IsSystemStatusBarAvailable())
                return;

            await StatusBarOperationLock.WaitAsync();
            try
            {
                // Проверяем владельца ещё раз после ожидания блокировки: состояние могло измениться.
                if (loading)
                {
                    if (Interlocked.CompareExchange(ref ActiveStatusBarOwner, 0, 0) != ownerId || !_isPageActive)
                        return;
                }
                else if (Interlocked.CompareExchange(ref ActiveStatusBarOwner, 0, 0) != 0)
                {
                    return;
                }

                await RunOnUiThreadAsync(async delegate
                {
                    if (loading)
                    {
                        if (Interlocked.CompareExchange(ref ActiveStatusBarOwner, 0, 0) != ownerId || !_isPageActive)
                            return;
                    }
                    else if (Interlocked.CompareExchange(ref ActiveStatusBarOwner, 0, 0) != 0)
                    {
                        return;
                    }

                    StatusBarProgressIndicator progress = StatusBar.GetForCurrentView().ProgressIndicator;
                    if (loading)
                    {
                        progress.Text = "Загрузка устройства";
                        progress.ProgressValue = null;
                        await progress.ShowAsync();
                    }
                    else
                    {
                        await progress.HideAsync();
                    }
                });
            }
            catch (Exception ex)
            {
                // На старых сборках Windows 10 Mobile повторный/параллельный RemoteAsync-вызов
                // может дать UnauthorizedAccessException, а затем уронить нативный процесс.
                // После первой ошибки больше не обращаемся к системному индикатору в этом запуске.
                StatusBarUnavailable = true;
                Debug.WriteLine("[4PDA.DevicePage] system status bar disabled: " + ex.GetType().Name + " " + ex.Message);
            }
            finally
            {
                StatusBarOperationLock.Release();
            }
        }

        private Task RunOnUiThreadAsync(Func<Task> action)
        {
            CoreDispatcher dispatcher = Dispatcher;
            if (dispatcher == null)
                return Task.FromResult(false);

            if (dispatcher.HasThreadAccess)
                return action();

            TaskCompletionSource<bool> completion = new TaskCompletionSource<bool>();
            try
            {
                var ignored = dispatcher.RunAsync(CoreDispatcherPriority.Normal, async delegate
                {
                    try
                    {
                        await action();
                        completion.TrySetResult(true);
                    }
                    catch (Exception ex)
                    {
                        completion.TrySetException(ex);
                    }
                });
            }
            catch (Exception ex)
            {
                completion.TrySetException(ex);
            }
            return completion.Task;
        }

        private static bool IsSystemStatusBarAvailable()
        {
            if (!ApiInformation.IsTypePresent("Windows.UI.ViewManagement.StatusBar") ||
                !ApiInformation.IsApiContractPresent("Windows.Phone.PhoneContract", 1))
                return false;

            try
            {
                return String.Equals(AnalyticsInfo.VersionInfo.DeviceFamily, "Windows.Mobile", StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private static bool IsSupportedBitmap(byte[] data)
        {
            if (data == null || data.Length < 2)
                return false;

            // JPEG
            if (data.Length >= 3 && data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF)
                return true;

            // PNG
            if (data.Length >= 8 && data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47 &&
                data[4] == 0x0D && data[5] == 0x0A && data[6] == 0x1A && data[7] == 0x0A)
                return true;

            // GIF87a / GIF89a
            if (data.Length >= 6 && data[0] == 0x47 && data[1] == 0x49 && data[2] == 0x46 && data[3] == 0x38 &&
                (data[4] == 0x37 || data[4] == 0x39) && data[5] == 0x61)
                return true;

            // BMP
            return data[0] == 0x42 && data[1] == 0x4D;
        }

        private void SetStatus(string message)
        {
            string text = NormalizeText(message);
            StatusTextBlock.Text = text;
            StatusTextBlock.Visibility = String.IsNullOrWhiteSpace(text) ? Visibility.Collapsed : Visibility.Visible;
        }

        private void UpdateResponsiveLayout(double width)
        {
            if (DeviceRootGrid == null || LeftContentColumn == null || RightContentColumn == null)
                return;

            _compactLayout = FourPdaImageService.IsMobile || width < 820;
            if (_compactLayout)
            {
                if (DeviceContentScrollViewer.Content != null)
                {
                    DeviceContentScrollViewer.Content = null;
                    MobileDeviceList.Header = DeviceRootGrid;
                }
                SpecificationGroupsItemsControl.ItemsSource = null;
                RightColumnPanel.Visibility = Visibility.Collapsed;
                DeviceContentScrollViewer.Visibility = Visibility.Collapsed;
                MobileDeviceList.Visibility = Visibility.Visible;
            }
            else
            {
                if (MobileDeviceList.Header != null)
                {
                    MobileDeviceList.Header = null;
                    DeviceContentScrollViewer.Content = DeviceRootGrid;
                }
                SpecificationGroupsItemsControl.ItemsSource = _specificationGroups;
                RightColumnPanel.Visibility = Visibility.Visible;
                DeviceContentScrollViewer.Visibility = Visibility.Visible;
                MobileDeviceList.Visibility = Visibility.Collapsed;
            }
            DeviceRootGrid.Width = _compactLayout
                ? Math.Max(0, width)
                : Math.Max(0, Math.Min(1180, width - 48));
            DeviceRootGrid.Margin = _compactLayout
                ? new Thickness(0, 18, 0, 32)
                : new Thickness(0, 28, 0, 44);

            if (_compactLayout)
            {
                LeftContentColumn.Width = new GridLength(1, GridUnitType.Star);
                RightContentColumn.Width = new GridLength(0);

                Grid.SetRow(LeftColumnPanel, 0);
                Grid.SetColumn(LeftColumnPanel, 0);
                Grid.SetColumnSpan(LeftColumnPanel, 2);

                Grid.SetRow(RightColumnPanel, 1);
                Grid.SetColumn(RightColumnPanel, 0);
                Grid.SetColumnSpan(RightColumnPanel, 2);
                RightColumnPanel.Margin = new Thickness(0);

                Grid.SetRow(StatusTextBlock, 2);
                Grid.SetColumn(StatusTextBlock, 0);
                Grid.SetColumnSpan(StatusTextBlock, 2);
            }
            else
            {
                double leftWidth = Math.Min(410, Math.Max(340, DeviceRootGrid.Width * 0.36));
                LeftContentColumn.Width = new GridLength(leftWidth);
                RightContentColumn.Width = new GridLength(1, GridUnitType.Star);

                Grid.SetRow(LeftColumnPanel, 0);
                Grid.SetColumn(LeftColumnPanel, 0);
                Grid.SetColumnSpan(LeftColumnPanel, 1);

                Grid.SetRow(RightColumnPanel, 0);
                Grid.SetColumn(RightColumnPanel, 1);
                Grid.SetColumnSpan(RightColumnPanel, 1);
                RightColumnPanel.Margin = new Thickness(16, 0, 0, 0);

                Grid.SetRow(StatusTextBlock, 1);
                Grid.SetColumn(StatusTextBlock, 0);
                Grid.SetColumnSpan(StatusTextBlock, 2);
            }

            UpdateDeviceImageSize();
        }

        private void UpdateDeviceImageSize()
        {
            if (DeviceImageHost == null || DeviceImage == null)
                return;

            double availableWidth = _compactLayout
                ? Math.Max(160, Math.Min(310, DeviceRootGrid == null ? 310 : DeviceRootGrid.Width - 40))
                : 330;
            double availableHeight = _compactLayout ? 250 : 250;

            if (_loadedImagePixelWidth <= 0 || _loadedImagePixelHeight <= 0)
            {
                double placeholderSize = _compactLayout ? 190 : 220;
                DeviceImageHost.Width = placeholderSize;
                DeviceImageHost.Height = placeholderSize;
                DeviceImage.Width = Double.NaN;
                DeviceImage.Height = Double.NaN;
                return;
            }

            double widthScale = availableWidth / _loadedImagePixelWidth;
            double heightScale = availableHeight / _loadedImagePixelHeight;
            double scale = Math.Min(widthScale, heightScale);
            if (Double.IsNaN(scale) || Double.IsInfinity(scale) || scale <= 0)
                scale = 1;

            double imageWidth = Math.Max(1, Math.Round(_loadedImagePixelWidth * scale));
            double imageHeight = Math.Max(1, Math.Round(_loadedImagePixelHeight * scale));

            DeviceImageHost.Width = imageWidth;
            DeviceImageHost.Height = imageHeight;
            DeviceImage.Width = imageWidth;
            DeviceImage.Height = imageHeight;
        }

        private static DevicePageData ParseDevicePage(string html, string pageUrl, DeviceNavigationArgs args)
        {
            string source = html ?? "";
            DevicePageData data = new DevicePageData();
            data.DeviceId = FirstNonEmpty(ExtractDeviceId(pageUrl), args == null ? "" : args.DeviceId);
            data.Title = CleanDeviceTitle(FirstNonEmpty(
                ExtractMetaContent(source, "og:title"),
                ExtractFirstText(source, new string[] { "h1", "h2" }),
                args == null ? "" : args.Name));
            data.Description = NormalizeText(FirstNonEmpty(
                ExtractMetaContent(source, "description"),
                ExtractClassText(source, new string[] { "device-description", "devdb-description", "description", "desc" })));
            data.ImageUrls = ExtractDeviceImageUrls(source, pageUrl);
            data.ImageUrl = data.ImageUrls.Count > 0 ? data.ImageUrls[0] : "";
            data.Rating = ExtractRating(source);

            List<string> breadcrumbs = ExtractBreadcrumbs(source);
            ApplyBreadcrumbs(data, breadcrumbs);
            if (String.IsNullOrWhiteSpace(data.BrandTitle))
                data.BrandTitle = FindNamedValue(source, new string[] { "Производитель", "Бренд", "Brand" });
            if (String.IsNullOrWhiteSpace(data.CategoryTitle))
                data.CategoryTitle = FindNamedValue(source, new string[] { "Категория", "Тип устройства" });

            data.SpecificationGroups = ExtractSpecificationGroups(source);
            data.RelatedItems = ExtractRelatedItems(source, pageUrl);
            return data;
        }

        private static void ApplyBreadcrumbs(DevicePageData data, List<string> breadcrumbs)
        {
            if (data == null || breadcrumbs == null || breadcrumbs.Count == 0)
                return;

            List<string> useful = new List<string>();
            foreach (string item in breadcrumbs)
            {
                string value = NormalizeText(item);
                if (String.IsNullOrWhiteSpace(value) ||
                    value.Equals("4PDA", StringComparison.OrdinalIgnoreCase) ||
                    value.IndexOf("База устройств", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    value.Equals(data.Title, StringComparison.OrdinalIgnoreCase))
                    continue;
                useful.Add(value);
            }

            if (useful.Count >= 2)
            {
                data.CategoryTitle = useful[useful.Count - 2];
                data.BrandTitle = useful[useful.Count - 1];
            }
            else if (useful.Count == 1)
            {
                data.BrandTitle = useful[0];
            }
        }

        private static List<DeviceSpecificationGroup> ExtractSpecificationGroups(string html)
        {
            List<DeviceSpecificationGroup> groups = new List<DeviceSpecificationGroup>();
            Dictionary<string, DeviceSpecificationGroup> byTitle = new Dictionary<string, DeviceSpecificationGroup>(StringComparer.OrdinalIgnoreCase);
            HashSet<string> unique = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string currentTitle = "Характеристики";

            MatchCollection blocks = Regex.Matches(html ?? "", @"(?<heading><h[2-4]\b[^>]*>[\s\S]*?<\/h[2-4]>)|(?<table><table\b[^>]*>[\s\S]*?<\/table>)|(?<dl><dl\b[^>]*>[\s\S]*?<\/dl>)", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            foreach (Match block in blocks)
            {
                if (block.Groups["heading"].Success)
                {
                    string heading = HtmlToText(block.Groups["heading"].Value);
                    if (IsSpecificationHeading(heading))
                        currentTitle = NormalizeSpecificationGroupTitle(heading);
                    continue;
                }

                if (block.Groups["table"].Success)
                    ParseSpecificationTable(block.Groups["table"].Value, currentTitle, groups, byTitle, unique);
                else if (block.Groups["dl"].Success)
                    ParseSpecificationDefinitionList(block.Groups["dl"].Value, currentTitle, groups, byTitle, unique);
            }

            ParseSpecificationLists(html, groups, byTitle, unique);

            if (CountSpecificationRows(groups) == 0)
                ParseColonFallback(html, groups, byTitle, unique);

            return groups;
        }

        private static void ParseSpecificationTable(string table, string groupTitle, List<DeviceSpecificationGroup> groups, Dictionary<string, DeviceSpecificationGroup> byTitle, HashSet<string> unique)
        {
            MatchCollection rows = Regex.Matches(table ?? "", @"<tr\b[^>]*>(?<v>[\s\S]*?)<\/tr>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            foreach (Match row in rows)
            {
                List<string> cells = new List<string>();
                MatchCollection cellMatches = Regex.Matches(row.Groups["v"].Value, @"<(?:th|td)\b[^>]*>(?<v>[\s\S]*?)<\/(?:th|td)>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
                foreach (Match cell in cellMatches)
                {
                    string value = HtmlToText(cell.Groups["v"].Value);
                    if (!String.IsNullOrWhiteSpace(value))
                        cells.Add(value);
                }

                if (cells.Count < 2)
                    continue;

                string title = cells[0];
                string valueText = "";
                for (int i = 1; i < cells.Count; i++)
                    valueText = String.IsNullOrWhiteSpace(valueText) ? cells[i] : valueText + "; " + cells[i];
                AddSpecification(groups, byTitle, unique, groupTitle, title, valueText);
            }
        }

        private static void ParseSpecificationDefinitionList(string dl, string groupTitle, List<DeviceSpecificationGroup> groups, Dictionary<string, DeviceSpecificationGroup> byTitle, HashSet<string> unique)
        {
            MatchCollection pairs = Regex.Matches(dl ?? "", @"<dt\b[^>]*>(?<title>[\s\S]*?)<\/dt>\s*<dd\b[^>]*>(?<value>[\s\S]*?)<\/dd>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            foreach (Match pair in pairs)
                AddSpecification(groups, byTitle, unique, groupTitle, HtmlToText(pair.Groups["title"].Value), HtmlToText(pair.Groups["value"].Value));
        }

        private static void ParseSpecificationLists(string html, List<DeviceSpecificationGroup> groups, Dictionary<string, DeviceSpecificationGroup> byTitle, HashSet<string> unique)
        {
            MatchCollection items = Regex.Matches(html ?? "", @"<li\b[^>]*>(?<v>[\s\S]*?)<\/li>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            foreach (Match item in items)
            {
                string inner = item.Groups["v"].Value;
                string title = FirstNonEmpty(
                    ExtractClassText(inner, new string[] { "title", "name", "label", "key" }),
                    HtmlToText(FirstMatch(inner, @"<(?:b|strong)\b[^>]*>(?<v>[\s\S]*?)<\/(?:b|strong)>")));
                string value = ExtractClassText(inner, new string[] { "area", "value", "desc", "description" });
                if (String.IsNullOrWhiteSpace(value))
                {
                    string plain = HtmlToText(inner);
                    int colon = plain.IndexOf(':');
                    if (colon > 0 && colon < plain.Length - 1)
                    {
                        title = FirstNonEmpty(title, plain.Substring(0, colon));
                        value = plain.Substring(colon + 1);
                    }
                }

                AddSpecification(groups, byTitle, unique, "Характеристики", title, value);
            }
        }

        private static void ParseColonFallback(string html, List<DeviceSpecificationGroup> groups, Dictionary<string, DeviceSpecificationGroup> byTitle, HashSet<string> unique)
        {
            string text = HtmlToText(Regex.Replace(html ?? "", @"<(?:script|style|nav|header|footer)\b[^>]*>[\s\S]*?<\/(?:script|style|nav|header|footer)>", " ", RegexOptions.IgnoreCase | RegexOptions.Singleline));
            string[] lines = Regex.Split(text, @"[\r\n]+|\s{3,}");
            int added = 0;
            foreach (string line in lines)
            {
                string value = NormalizeText(line);
                int colon = value.IndexOf(':');
                if (colon < 2 || colon > 80 || colon >= value.Length - 2)
                    continue;

                string title = value.Substring(0, colon);
                string content = value.Substring(colon + 1);
                int before = CountSpecificationRows(groups);
                AddSpecification(groups, byTitle, unique, "Характеристики", title, content);
                if (CountSpecificationRows(groups) > before)
                    added++;
                if (added >= 60)
                    break;
            }
        }

        private static void AddSpecification(List<DeviceSpecificationGroup> groups, Dictionary<string, DeviceSpecificationGroup> byTitle, HashSet<string> unique, string groupTitle, string title, string value)
        {
            string cleanTitle = NormalizeText(title).Trim(' ', ':', '-', '–');
            string cleanValue = NormalizeText(value).Trim(' ', ':', '-', '–');
            if (!LooksLikeSpecification(cleanTitle, cleanValue))
                return;

            string key = cleanTitle + "\n" + cleanValue;
            if (!unique.Add(key))
                return;

            string cleanGroup = NormalizeSpecificationGroupTitle(groupTitle);
            DeviceSpecificationGroup group;
            if (!byTitle.TryGetValue(cleanGroup, out group))
            {
                group = new DeviceSpecificationGroup { Title = cleanGroup };
                byTitle[cleanGroup] = group;
                groups.Add(group);
            }

            group.Items.Add(new DeviceInfoRow { Title = cleanTitle, Value = cleanValue });
        }

        private static bool LooksLikeSpecification(string title, string value)
        {
            if (String.IsNullOrWhiteSpace(title) || String.IsNullOrWhiteSpace(value))
                return false;
            if (title.Length > 100 || value.Length > 1000)
                return false;
            if (title.Equals(value, StringComparison.OrdinalIgnoreCase))
                return false;
            if (title.IndexOf("cookie", StringComparison.OrdinalIgnoreCase) >= 0 ||
                title.IndexOf("войти", StringComparison.OrdinalIgnoreCase) >= 0 ||
                title.IndexOf("регистрац", StringComparison.OrdinalIgnoreCase) >= 0 ||
                title.IndexOf("комментар", StringComparison.OrdinalIgnoreCase) >= 0)
                return false;
            return true;
        }

        private static bool IsSpecificationHeading(string heading)
        {
            string value = NormalizeText(heading);
            return value.IndexOf("характер", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   value.IndexOf("экран", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   value.IndexOf("процессор", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   value.IndexOf("память", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   value.IndexOf("камера", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   value.IndexOf("питание", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   value.IndexOf("связь", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   value.IndexOf("общие", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string NormalizeSpecificationGroupTitle(string title)
        {
            string value = NormalizeText(title);
            if (String.IsNullOrWhiteSpace(value) || value.Length > 80)
                return "Характеристики";
            return value;
        }

        private static int CountSpecificationRows(List<DeviceSpecificationGroup> groups)
        {
            int count = 0;
            if (groups == null)
                return count;
            foreach (DeviceSpecificationGroup group in groups)
                count += group == null || group.Items == null ? 0 : group.Items.Count;
            return count;
        }

        private static List<DeviceRelatedItem> ExtractRelatedItems(string html, string pageUrl)
        {
            List<DeviceRelatedItem> result = new List<DeviceRelatedItem>();
            HashSet<string> unique = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            MatchCollection sections = Regex.Matches(html ?? "", @"<h[2-4]\b[^>]*>(?<heading>[\s\S]*?)<\/h[2-4]>(?<body>[\s\S]*?)(?=<h[2-4]\b|$)", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            foreach (Match section in sections)
            {
                string heading = HtmlToText(section.Groups["heading"].Value);
                string sectionName = GetRelatedSectionName(heading);
                if (String.IsNullOrWhiteSpace(sectionName))
                    continue;

                MatchCollection links = Regex.Matches(section.Groups["body"].Value, @"<a\b[^>]*href\s*=\s*['""](?<url>[^'""<>]+)['""][^>]*>(?<title>[\s\S]*?)<\/a>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
                foreach (Match link in links)
                {
                    string title = HtmlToText(link.Groups["title"].Value);
                    string url = NormalizeUrl(link.Groups["url"].Value, pageUrl);
                    if (String.IsNullOrWhiteSpace(title) || String.IsNullOrWhiteSpace(url) || !unique.Add(url))
                        continue;

                    result.Add(new DeviceRelatedItem { Title = title, Section = sectionName, Url = url });
                    if (result.Count >= 50)
                        return result;
                }
            }
            return result;
        }

        private static string GetRelatedSectionName(string heading)
        {
            string value = NormalizeText(heading);
            if (value.IndexOf("обсужден", StringComparison.OrdinalIgnoreCase) >= 0 || value.IndexOf("форум", StringComparison.OrdinalIgnoreCase) >= 0)
                return "Обсуждения";
            if (value.IndexOf("прошив", StringComparison.OrdinalIgnoreCase) >= 0)
                return "Прошивки";
            if (value.IndexOf("новост", StringComparison.OrdinalIgnoreCase) >= 0)
                return "Новости";
            if (value.IndexOf("обзор", StringComparison.OrdinalIgnoreCase) >= 0 || value.IndexOf("стать", StringComparison.OrdinalIgnoreCase) >= 0)
                return "Статьи и обзоры";
            return "";
        }

        private static List<string> ExtractBreadcrumbs(string html)
        {
            string block = FirstMatch(html, @"<(?:div|nav|ul)\b(?=[^>]*class\s*=\s*['""][^'""<>]*(?:breadcrumb|breadcrumbs)[^'""<>]*['""])[^>]*>(?<v>[\s\S]*?)<\/(?:div|nav|ul)>");
            List<string> result = new List<string>();
            MatchCollection links = Regex.Matches(block ?? "", @"<a\b[^>]*>(?<v>[\s\S]*?)<\/a>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            foreach (Match link in links)
            {
                string value = HtmlToText(link.Groups["v"].Value);
                if (!String.IsNullOrWhiteSpace(value))
                    result.Add(value);
            }
            return result;
        }

        private static string FindNamedValue(string html, string[] names)
        {
            if (names == null)
                return "";
            for (int i = 0; i < names.Length; i++)
            {
                string name = Regex.Escape(names[i]);
                string value = FirstMatch(html, @"(?:<[^>]+>\s*)?" + name + @"\s*:?(?:\s*<\/[^>]+>)?\s*(?:<[^>]+>\s*)*(?<v>[^<\r\n]{1,120})");
                value = NormalizeText(value);
                if (!String.IsNullOrWhiteSpace(value))
                    return value;
            }
            return "";
        }

        private static string ExtractBestDeviceImageUrl(string html, string pageUrl)
        {
            List<string> urls = ExtractDeviceImageUrls(html, pageUrl);
            return urls.Count > 0 ? urls[0] : "";
        }

        private static List<string> ExtractDeviceImageUrls(string html, string pageUrl)
        {
            string source = html ?? "";
            List<DeviceImageCandidate> candidates = new List<DeviceImageCandidate>();

            // В DevDB полноразмерное фото лежит в href ссылки внутри item-visual,
            // а img/src обычно содержит только миниатюру. Сначала добавляем оба URL.
            string visualBlock = FirstMatch(source,
                @"<div\b(?=[^>]*class\s*=\s*['""][^'""<>]*\bitem-visual\b[^'""<>]*['""])[^>]*>(?<v>[\s\S]*?)(?=<div\b(?=[^>]*class\s*=\s*['""][^'""<>]*\bitem-info\b)|</section>|</article>)");
            if (String.IsNullOrWhiteSpace(visualBlock))
                visualBlock = FirstMatch(source,
                    @"<[^>]+\bclass\s*=\s*['""][^'""<>]*(?:device-images|device-image|product-image|gallery)[^'""<>]*['""][^>]*>(?<v>[\s\S]*?)</[^>]+>");

            if (!String.IsNullOrWhiteSpace(visualBlock))
            {
                MatchCollection visualLinks = Regex.Matches(visualBlock,
                    @"<a\b[^>]*href\s*=\s*['""](?<url>[^'""<>]+)['""][^>]*>(?<body>[\s\S]*?)</a>",
                    RegexOptions.IgnoreCase | RegexOptions.Singleline);
                foreach (Match link in visualLinks)
                {
                    string body = link.Groups["body"].Value;
                    if (body.IndexOf("<img", StringComparison.OrdinalIgnoreCase) < 0)
                        continue;

                    AddImageCandidate(candidates, link.Groups["url"].Value, 360);
                    Match imageMatch = Regex.Match(body, @"<img\b[^>]*>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
                    if (imageMatch.Success)
                    {
                        string imageTag = imageMatch.Value;
                        AddImageCandidate(candidates, ExtractTagAttribute(imageTag, "data-original"), 350);
                        AddImageCandidate(candidates, ExtractTagAttribute(imageTag, "data-src"), 345);
                        AddImageCandidate(candidates, ExtractTagAttribute(imageTag, "src"), 340);
                    }
                }

                MatchCollection visualImages = Regex.Matches(visualBlock, @"<img\b[^>]*>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
                foreach (Match image in visualImages)
                {
                    string tag = image.Value;
                    AddImageCandidate(candidates, ExtractTagAttribute(tag, "data-original"), 330);
                    AddImageCandidate(candidates, ExtractTagAttribute(tag, "data-image"), 328);
                    AddImageCandidate(candidates, ExtractTagAttribute(tag, "data-lazy-src"), 326);
                    AddImageCandidate(candidates, ExtractTagAttribute(tag, "data-src"), 324);
                    AddImageCandidate(candidates, ExtractTagAttribute(tag, "src"), 320);
                }
            }

            AddImageCandidate(candidates, ExtractMetaRawContent(source, "og:image"), 220);
            AddImageCandidate(candidates, ExtractMetaRawContent(source, "twitter:image"), 210);
            AddImageCandidate(candidates, FirstMatch(source, @"<meta\b(?=[^>]*itemprop\s*=\s*['""]image['""])[^>]*content\s*=\s*['""](?<v>[^'""<>]+)['""][^>]*>"), 208);
            AddImageCandidate(candidates, FirstMatch(source, @"<link\b(?=[^>]*rel\s*=\s*['""]image_src['""])[^>]*href\s*=\s*['""](?<v>[^'""<>]+)['""][^>]*>"), 205);
            AddImageCandidate(candidates, FirstMatch(source, @"[""'](?:image|imageUrl|image_url|photo|picture|thumbnail)[""']\s*:\s*[""'](?<v>(?:\\.|[^""'])+)[""']"), 190);

            MatchCollection images = Regex.Matches(source, @"<img\b[^>]*>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            foreach (Match image in images)
            {
                string tag = image.Value;
                int baseScore = 30;
                if (tag.IndexOf("device", StringComparison.OrdinalIgnoreCase) >= 0)
                    baseScore += 70;
                if (tag.IndexOf("devdb", StringComparison.OrdinalIgnoreCase) >= 0)
                    baseScore += 60;
                if (tag.IndexOf("product", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    tag.IndexOf("model", StringComparison.OrdinalIgnoreCase) >= 0)
                    baseScore += 45;
                if (tag.IndexOf("itemprop=\"image\"", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    tag.IndexOf("itemprop='image'", StringComparison.OrdinalIgnoreCase) >= 0)
                    baseScore += 55;

                AddImageCandidate(candidates, ExtractTagAttribute(tag, "data-original"), baseScore + 25);
                AddImageCandidate(candidates, ExtractTagAttribute(tag, "data-image"), baseScore + 24);
                AddImageCandidate(candidates, ExtractTagAttribute(tag, "data-lazy-src"), baseScore + 22);
                AddImageCandidate(candidates, ExtractTagAttribute(tag, "data-src"), baseScore + 20);
                AddImageCandidate(candidates, ExtractTagAttribute(tag, "poster"), baseScore + 16);
                AddImageCandidate(candidates, ExtractTagAttribute(tag, "src"), baseScore + 10);

                string srcset = FirstNonEmpty(ExtractTagAttribute(tag, "data-srcset"), ExtractTagAttribute(tag, "srcset"));
                if (!String.IsNullOrWhiteSpace(srcset))
                {
                    string[] parts = srcset.Split(',');
                    for (int i = 0; i < parts.Length; i++)
                    {
                        string part = parts[i].Trim();
                        int separator = part.IndexOf(' ');
                        AddImageCandidate(candidates, separator > 0 ? part.Substring(0, separator) : part, baseScore + 18 + i);
                    }
                }
            }

            MatchCollection cssImages = Regex.Matches(source, @"background(?:-image)?\s*:\s*url\(\s*['""]?(?<v>[^)'""]+)['""]?\s*\)", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            foreach (Match cssImage in cssImages)
                AddImageCandidate(candidates, cssImage.Groups["v"].Value, 70);

            List<string> result = new List<string>();
            HashSet<string> unique = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < candidates.Count; i++)
            {
                DeviceImageCandidate candidate = candidates[i];
                candidate.Url = NormalizeUrl(candidate.Url, pageUrl);
                if (!IsUsableDeviceImageUrl(candidate.Url))
                {
                    candidate.Score = Int32.MinValue;
                    continue;
                }

                string lower = candidate.Url.ToLowerInvariant();
                if (lower.IndexOf("devdb") >= 0 || lower.IndexOf("device") >= 0)
                    candidate.Score += 20;
                if (lower.IndexOf("logo") >= 0 || lower.IndexOf("avatar") >= 0 ||
                    lower.IndexOf("icon") >= 0 || lower.IndexOf("sprite") >= 0 ||
                    lower.IndexOf("smile") >= 0)
                    candidate.Score -= 100;
                if (lower.EndsWith(".svg", StringComparison.OrdinalIgnoreCase))
                    candidate.Score -= 150;
            }

            candidates.Sort(delegate(DeviceImageCandidate left, DeviceImageCandidate right)
            {
                return right.Score.CompareTo(left.Score);
            });

            for (int i = 0; i < candidates.Count; i++)
            {
                DeviceImageCandidate candidate = candidates[i];
                if (candidate.Score > -50 && IsUsableDeviceImageUrl(candidate.Url) && unique.Add(candidate.Url))
                {
                    result.Add(candidate.Url);
                    if (result.Count >= 12)
                        break;
                }
            }
            return result;
        }

        private static void AddImageCandidate(List<DeviceImageCandidate> candidates, string value, int score)
        {
            string url = DecodeUrlValue(value);
            if (String.IsNullOrWhiteSpace(url))
                return;
            candidates.Add(new DeviceImageCandidate { Url = url, Score = score });
        }

        private static string ExtractTagAttribute(string tag, string attribute)
        {
            string name = Regex.Escape(attribute ?? "");
            Match match = Regex.Match(tag ?? "", @"(?:^|\s)" + name + @"\s*=\s*(?:['""](?<v>[^'""]*)['""]|(?<v>[^\s>]+))", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            return match.Success ? match.Groups["v"].Value : "";
        }

        private static string ExtractMetaRawContent(string html, string name)
        {
            string escaped = Regex.Escape(name ?? "");
            string value = FirstMatch(html, @"<meta\b(?=[^>]*(?:property|name)\s*=\s*['""]" + escaped + @"['""])[^>]*content\s*=\s*['""](?<v>[^'""<>]*)['""][^>]*>");
            if (String.IsNullOrWhiteSpace(value))
                value = FirstMatch(html, @"<meta\b(?=[^>]*content\s*=\s*['""](?<v>[^'""<>]*)['""])[^>]*(?:property|name)\s*=\s*['""]" + escaped + @"['""][^>]*>");
            return DecodeUrlValue(value);
        }

        private static string DecodeUrlValue(string value)
        {
            if (String.IsNullOrWhiteSpace(value))
                return "";

            string result = value.Trim();
            try
            {
                result = HtmlUtilities.ConvertToText(result);
            }
            catch
            {
            }
            result = result.Replace("\\/", "/")
                           .Replace("\\u002F", "/")
                           .Replace("\\u002f", "/")
                           .Replace("\\u003A", ":")
                           .Replace("\\u003a", ":")
                           .Replace("\\u0026", "&");
            return result.Trim();
        }

        private static bool IsUsableDeviceImageUrl(string url)
        {
            if (String.IsNullOrWhiteSpace(url) ||
                url.StartsWith("data:", StringComparison.OrdinalIgnoreCase) ||
                url.StartsWith("blob:", StringComparison.OrdinalIgnoreCase))
                return false;

            return Uri.IsWellFormedUriString(url, UriKind.Absolute);
        }

        private static int ExtractRating(string html)
        {
            string value = FirstMatch(html, @"(?:rating|рейтинг)[^0-9]{0,80}(?<v>\d{1,3})", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            int rating;
            return Int32.TryParse(value, out rating) ? rating : 0;
        }

        private static string ExtractMetaContent(string html, string name)
        {
            string escaped = Regex.Escape(name ?? "");
            string value = FirstMatch(html, @"<meta\b(?=[^>]*(?:property|name)\s*=\s*['""]" + escaped + @"['""])[^>]*content\s*=\s*['""](?<v>[^'""<>]*)['""][^>]*>");
            if (String.IsNullOrWhiteSpace(value))
                value = FirstMatch(html, @"<meta\b(?=[^>]*content\s*=\s*['""](?<v>[^'""<>]*)['""])[^>]*(?:property|name)\s*=\s*['""]" + escaped + @"['""][^>]*>");
            return HtmlToText(value);
        }

        private static string ExtractFirstText(string html, string[] tags)
        {
            if (tags == null)
                return "";
            for (int i = 0; i < tags.Length; i++)
            {
                string tag = Regex.Escape(tags[i]);
                string value = FirstMatch(html, @"<" + tag + @"\b[^>]*>(?<v>[\s\S]*?)<\/" + tag + @">");
                value = HtmlToText(value);
                if (!String.IsNullOrWhiteSpace(value))
                    return value;
            }
            return "";
        }

        private static string ExtractClassText(string html, string[] classNames)
        {
            if (classNames == null)
                return "";
            for (int i = 0; i < classNames.Length; i++)
            {
                string className = Regex.Escape(classNames[i]);
                string value = FirstMatch(html, @"<(?:div|span|p)\b(?=[^>]*class\s*=\s*['""][^'""<>]*" + className + @"[^'""<>]*['""])[^>]*>(?<v>[\s\S]*?)<\/(?:div|span|p)>");
                value = HtmlToText(value);
                if (!String.IsNullOrWhiteSpace(value))
                    return value;
            }
            return "";
        }

        private static string FirstMatch(string input, string pattern)
        {
            return FirstMatch(input, pattern, RegexOptions.IgnoreCase | RegexOptions.Singleline);
        }

        private static string FirstMatch(string input, string pattern, RegexOptions options)
        {
            Match match = Regex.Match(input ?? "", pattern, options);
            return match.Success ? match.Groups["v"].Value : "";
        }

        private static string HtmlToText(string html)
        {
            if (String.IsNullOrWhiteSpace(html))
                return "";
            string value = Regex.Replace(html, @"<(?:script|style)\b[^>]*>[\s\S]*?<\/(?:script|style)>", " ", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            value = Regex.Replace(value, @"<(?:br|\/p|\/div|\/li|\/tr|\/h[1-6])\b[^>]*>", "\n", RegexOptions.IgnoreCase);
            try
            {
                value = HtmlUtilities.ConvertToText(value);
            }
            catch
            {
                value = Regex.Replace(value, "<[^>]+>", " ");
            }
            return NormalizeText(value);
        }

        private static string NormalizeText(string value)
        {
            if (String.IsNullOrWhiteSpace(value))
                return "";
            string result = value.Replace('\u00A0', ' ').Replace("&nbsp;", " ");
            result = Regex.Replace(result, @"[ \t\f\v]+", " ");
            result = Regex.Replace(result, @"\s*\r?\n\s*", "\n");
            result = Regex.Replace(result, @"\n{3,}", "\n\n");
            return result.Trim();
        }

        private static string CleanDeviceTitle(string title)
        {
            string value = NormalizeText(title);
            value = Regex.Replace(value, @"\s*[-–—|]\s*4PDA.*$", "", RegexOptions.IgnoreCase);
            return value.Trim();
        }

        private static string ResolveDeviceUrl(DeviceNavigationArgs args)
        {
            if (args == null)
                return "";
            string id = NormalizeText(args.DeviceId);
            string url = NormalizeUrl(args.Url, FourPdaRoot);
            if (!String.IsNullOrWhiteSpace(url))
                return url;
            if (Regex.IsMatch(id, @"^\d+$"))
                return FourPdaRoot + "devdb/" + id;
            return "";
        }

        private static string NormalizeUrl(string value, string baseUrl)
        {
            string url = DecodeUrlValue(value).Replace("&amp;", "&");
            if (String.IsNullOrWhiteSpace(url) || url.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase) || url.StartsWith("#", StringComparison.Ordinal))
                return "";
            if (url.StartsWith("//", StringComparison.Ordinal))
                return "https:" + url;
            if (Regex.IsMatch(url, @"^https?://", RegexOptions.IgnoreCase))
            {
                Uri absoluteUri;
                if (Uri.TryCreate(url, UriKind.Absolute, out absoluteUri) &&
                    absoluteUri.Scheme.Equals("http", StringComparison.OrdinalIgnoreCase) &&
                    (absoluteUri.Host.EndsWith("4pda.to", StringComparison.OrdinalIgnoreCase) ||
                     absoluteUri.Host.EndsWith("4pda.ru", StringComparison.OrdinalIgnoreCase)))
                    return "https://" + absoluteUri.Host + absoluteUri.PathAndQuery;
                return url;
            }
            if (url.StartsWith("/", StringComparison.Ordinal))
                return "https://4pda.to" + url;

            Uri baseUri;
            Uri result;
            if (Uri.TryCreate(baseUrl, UriKind.Absolute, out baseUri) && Uri.TryCreate(baseUri, url, out result))
                return result.AbsoluteUri;
            return FourPdaRoot + url.TrimStart('/');
        }

        private static string ExtractDeviceId(string value)
        {
            Match match = Regex.Match(value ?? "", @"(?:/devdb/|[?&]p=)(?<v>\d+)", RegexOptions.IgnoreCase);
            return match.Success ? match.Groups["v"].Value : "";
        }

        private static string FirstNonEmpty(params string[] values)
        {
            if (values == null)
                return "";
            for (int i = 0; i < values.Length; i++)
            {
                string value = NormalizeText(values[i]);
                if (!String.IsNullOrWhiteSpace(value))
                    return value;
            }
            return "";
        }

        private static string JoinNonEmpty(string separator, params string[] values)
        {
            string result = "";
            if (values == null)
                return result;
            for (int i = 0; i < values.Length; i++)
            {
                string value = NormalizeText(values[i]);
                if (String.IsNullOrWhiteSpace(value))
                    continue;
                result = String.IsNullOrWhiteSpace(result) ? value : result + separator + value;
            }
            return result;
        }
    }

    public sealed class DeviceSpecificationViewRow
    {
        public string Title { get; set; }
        public string Value { get; set; }
        public bool IsHeading { get; set; }
        public Visibility HeadingVisibility { get { return IsHeading ? Visibility.Visible : Visibility.Collapsed; } }
        public Visibility RowVisibility { get { return IsHeading ? Visibility.Collapsed : Visibility.Visible; } }
    }

    public sealed class DeviceInfoRow
    {
        public string Title { get; set; }
        public string Value { get; set; }
    }

    public sealed class DeviceSpecificationGroup
    {
        public DeviceSpecificationGroup()
        {
            Items = new ObservableCollection<DeviceInfoRow>();
        }

        public string Title { get; set; }
        public ObservableCollection<DeviceInfoRow> Items { get; private set; }
    }

    public sealed class DeviceRelatedItem
    {
        public string Title { get; set; }
        public string Section { get; set; }
        public string Url { get; set; }
    }

    internal sealed class DeviceImageCandidate
    {
        public string Url { get; set; }
        public int Score { get; set; }
    }

    internal sealed class DevicePageData
    {
        public string DeviceId { get; set; }
        public string Title { get; set; }
        public string Subtitle { get; set; }
        public string BrandTitle { get; set; }
        public string CategoryTitle { get; set; }
        public string Description { get; set; }
        public string ImageUrl { get; set; }
        public List<string> ImageUrls { get; set; }
        public int Rating { get; set; }
        public List<DeviceSpecificationGroup> SpecificationGroups { get; set; }
        public List<DeviceRelatedItem> RelatedItems { get; set; }
    }
}
