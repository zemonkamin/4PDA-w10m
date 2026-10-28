using System;
using _4PDA.Models;
using LegacyProjects.UI.UniversalWindowsPlatform.Enums;
using Windows.System;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Imaging;
using Windows.UI.Xaml.Navigation;

namespace _4PDA
{
    public sealed partial class SidebarPage : Page
    {
        public SidebarPage()
        {
            InitializeComponent();
            SidebarHost.RegisterPropertyChangedCallback(SplitView.IsPaneOpenProperty, SidebarHost_PaneStateChanged);
            UpdateSidebarLayout();
            HideComposeItem();
            ContentFrame.Navigated += ContentFrame_Navigated;
            TopNavigationBar.SizeChanged += TopNavigationBar_SizeChanged;
            ContentFrame.Navigate(typeof(MainPage));
        }

        private void Page_Loaded(object sender, RoutedEventArgs e)
        {
            AppNavigationActions.RegisterSidebar(SidebarHost);
            AppNavigationActions.RegisterContentFrame(ContentFrame);
            ForumAuthService.AccountChanged -= ForumAuthService_AccountChanged;
            ForumAuthService.AccountChanged += ForumAuthService_AccountChanged;
            AppNavigationActions.MessageContextChanged -= AppNavigationActions_MessageContextChanged;
            AppNavigationActions.MessageContextChanged += AppNavigationActions_MessageContextChanged;
            RefreshAccountItem();
            UpdateComposeItem();
            UpdateSidebarDismissOverlay();
        }

        private void SidebarHost_PaneStateChanged(DependencyObject sender, DependencyProperty property)
        {
            UpdateSidebarDismissOverlay();
            AppNavigationActions.NotifyNavigationStateChanged();
        }

        private void UpdateSidebarDismissOverlay()
        {
            if (SidebarDismissOverlay == null || SidebarHost == null)
                return;

            SidebarDismissOverlay.Visibility = SidebarHost.IsPaneOpen
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        private void SidebarDismissOverlay_Tapped(object sender, TappedRoutedEventArgs e)
        {
            if (SidebarHost != null) SidebarHost.IsPaneOpen = false;

            e.Handled = true;
        }

        private void Page_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdateSidebarLayout();
        }

        private void UpdateSidebarLayout()
        {
            if (SidebarHost == null || Sidebar == null) return;
            bool wide = Window.Current.Bounds.Width > Sidebar.HorizontalBreakpoint;
            SidebarHost.DisplayMode = wide ? SplitViewDisplayMode.CompactOverlay : SplitViewDisplayMode.Overlay;
            SidebarHost.CompactPaneLength = wide ? Sidebar.CompactPaneWidth : 0;
        }

        private void Page_Unloaded(object sender, RoutedEventArgs e)
        {
            ForumAuthService.AccountChanged -= ForumAuthService_AccountChanged;
            AppNavigationActions.MessageContextChanged -= AppNavigationActions_MessageContextChanged;
            AppNavigationActions.UnregisterSidebar(SidebarHost);
            AppNavigationActions.UnregisterContentFrame(ContentFrame);
        }

        private void ContentFrame_Navigated(object sender, NavigationEventArgs e)
        {
            Type pageType = e.SourcePageType;
            ApplyNavigationHeader(pageType, e.Parameter);
            UpdateTitleWidth();
            Sidebar.SelectedKey = GetSelectedKey(pageType, e.Parameter);
            RefreshAccountItem();
            UpdateComposeItem();
            AppNavigationActions.NotifyNavigationStateChanged();
        }


        private void ApplyNavigationHeader(Type pageType, object parameter)
        {
            HideChatHeader();
            HideForumSearchHeader();
            ShowPageTitleHeader(GetPageTitle(pageType, parameter));

            // Заголовок рисуется отдельным слоем. Так кнопка меню всегда имеет
            // свои 50 пикселей, а длинный текст обрезается до правых кнопок.
            TitleNavigationItem.Glyph = string.Empty;
            TitleNavigationItem.Text = string.Empty;
            TitleNavigationItem.Width = 0;
            TitleNavigationItem.MinWidth = 0;

            ForumSearchNavigationArgs search = parameter as ForumSearchNavigationArgs;
            if (pageType == typeof(ForumPage) && search != null)
            {
                HidePageTitleHeader();
                ShowForumSearchHeader(search);
                return;
            }

            if (pageType != typeof(ChatPage))
                return;

            HidePageTitleHeader();
            QmsThreadNavigationArgs chat = parameter as QmsThreadNavigationArgs;
            ShowChatHeader(chat);
        }

        private void ShowPageTitleHeader(string title)
        {
            if (PageTitleHeaderOverlay == null || PageTitleTextBlock == null)
                return;

            PageTitleTextBlock.Text = title ?? string.Empty;
            PageTitleHeaderOverlay.Visibility = Visibility.Visible;
        }

        private void HidePageTitleHeader()
        {
            if (PageTitleHeaderOverlay == null)
                return;

            PageTitleHeaderOverlay.Visibility = Visibility.Collapsed;
        }

        private void ShowForumSearchHeader(ForumSearchNavigationArgs search)
        {
            string query = search == null ? string.Empty : search.Query;
            ForumSearchTextBox.Text = query ?? string.Empty;

            // В режиме поиска обычный navbar полностью убирается: не остаются
            // ни кнопка sidebar, ни кнопка поиска. Поле занимает всю ширину.
            TopNavigationBar.Visibility = Visibility.Collapsed;
            ForumSearchHeaderOverlay.Visibility = Visibility.Visible;

            // Фокус ставится после завершения навигации, когда TextBox уже попал
            // в визуальное дерево. Это также сразу открывает экранную клавиатуру.
            Dispatcher.RunAsync(CoreDispatcherPriority.Normal, delegate
            {
                AlignForumSearchText();
                ForumSearchTextBox.Focus(FocusState.Programmatic);
                ForumSearchTextBox.Select(ForumSearchTextBox.Text.Length, 0);
            });
        }

        private void ForumSearchTextBox_Loaded(object sender, RoutedEventArgs e)
        {
            AlignForumSearchText();
        }

        private void AlignForumSearchText()
        {
            // The Windows 10 TextBox template does not bind these parts to the
            // control's VerticalContentAlignment. Keep the native editing template.
            ForumSearchTextBox.ApplyTemplate();
            if (VisualTreeHelper.GetChildrenCount(ForumSearchTextBox) == 0) return;
            var root = VisualTreeHelper.GetChild(ForumSearchTextBox, 0) as FrameworkElement;
            if (root == null) return;
            var placeholder = root.FindName("PlaceholderTextContentPresenter") as Control;
            if (placeholder != null) placeholder.VerticalContentAlignment = VerticalAlignment.Center;
            var editor = root.FindName("ContentElement") as ScrollViewer;
            // The native TextBoxView fills its ScrollViewer viewport. Center the
            // editor itself, otherwise the text and caret remain at the top.
            if (editor != null) editor.VerticalAlignment = VerticalAlignment.Center;
        }

        private void HideForumSearchHeader()
        {
            if (ForumSearchHeaderOverlay == null)
                return;

            ForumSearchHeaderOverlay.Visibility = Visibility.Collapsed;
            if (TopNavigationBar != null)
                TopNavigationBar.Visibility = Visibility.Visible;
        }

        private async void ForumSearchTextBox_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key != VirtualKey.Enter)
                return;

            e.Handled = true;
            await ExecuteForumSearchAsync();
        }

        private async System.Threading.Tasks.Task ExecuteForumSearchAsync()
        {
            string query = (ForumSearchTextBox.Text ?? string.Empty).Trim();
            ForumPage page = ContentFrame.Content as ForumPage;
            if (page == null || !page.IsSearchMode)
            {
                ForumSearchNavigationArgs search = new ForumSearchNavigationArgs();
                search.Query = query;
                ContentFrame.Navigate(typeof(ForumPage), search);
                return;
            }

            await page.SearchAsync(query);
        }

        private void ShowChatHeader(QmsThreadNavigationArgs chat)
        {
            string title = chat == null || String.IsNullOrWhiteSpace(chat.ThreadTitle)
                ? "Чат"
                : chat.ThreadTitle.Trim();
            string contact = chat == null || String.IsNullOrWhiteSpace(chat.ContactNick)
                ? "Собеседник"
                : chat.ContactNick.Trim();

            ChatHeaderTitleTextBlock.Text = title;
            ChatHeaderContactTextBlock.Text = contact;
            ChatHeaderOverlay.Visibility = Visibility.Visible;
            ChatAvatarFallback.Visibility = Visibility.Visible;
            ChatAvatarImage.Source = null;

            if (chat == null)
                return;

            if (chat.AvatarImage != null)
            {
                ChatAvatarImage.Source = chat.AvatarImage;
                return;
            }

            if (String.IsNullOrWhiteSpace(chat.AvatarUrl))
                return;

            try
            {
                _4PDA.Services.DeferredImage.Attach(ChatAvatarImage, chat.AvatarUrl, 88, 0);
            }
            catch
            {
                ChatAvatarImage.Source = null;
                ChatAvatarFallback.Visibility = Visibility.Visible;
            }
        }

        private void HideChatHeader()
        {
            if (ChatHeaderOverlay == null)
                return;

            ChatHeaderOverlay.Visibility = Visibility.Collapsed;
            ChatHeaderTitleTextBlock.Text = string.Empty;
            ChatHeaderContactTextBlock.Text = string.Empty;
            _4PDA.Services.DeferredImage.SetUrl(ChatAvatarImage, string.Empty);
            ChatAvatarImage.Source = null;
            ChatAvatarFallback.Visibility = Visibility.Visible;
        }

        private void ChatAvatarImage_ImageOpened(object sender, RoutedEventArgs e)
        {
            ChatAvatarFallback.Visibility = Visibility.Collapsed;
        }

        private void ChatAvatarImage_ImageFailed(object sender, ExceptionRoutedEventArgs e)
        {
            ChatAvatarImage.Source = null;
            ChatAvatarFallback.Visibility = Visibility.Visible;
        }

        private void TopNavigationBar_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdateTitleWidth();
        }

        private void UpdateTitleWidth()
        {
            if (TopNavigationBar == null || PageTitleHeaderOverlay == null)
                return;

            double rightWidth = 0;
            for (int i = 0; i < TopNavigationBar.RightItems.Count; i++)
            {
                double itemWidth = TopNavigationBar.RightItems[i].Width;
                rightWidth += double.IsNaN(itemWidth) ? 50 : Math.Max(0, itemWidth);
            }

            // Слева всегда резервируется кнопка меню, справа — только реально
            // видимые кнопки. TextBlock внутри растягиваемой области сам
            // обрезает длинный заголовок многоточием.
            double maximumRightMargin = Math.Max(0, TopNavigationBar.ActualWidth - 50);
            rightWidth = Math.Min(rightWidth, maximumRightMargin);
            PageTitleHeaderOverlay.Margin = new Thickness(50, 0, rightWidth, 0);
            ChatHeaderOverlay.Margin = new Thickness(50, 0, rightWidth, 0);

            TitleNavigationItem.Width = 0;
            TitleNavigationItem.MinWidth = 0;
        }

        private string GetPageTitle(Type pageType, object parameter)
        {
            if (pageType == typeof(MainPage))
            {
                return "4PDA";
            }

            if (pageType == typeof(NewsPage))
            {
                return "\u0421\u0430\u0439\u0442";
            }

            if (pageType == typeof(ShowNewsPage))
            {
                return "\u041D\u043E\u0432\u043E\u0441\u0442\u044C";
            }

            if (pageType == typeof(ForumPage))
            {
                if (parameter is ForumSearchNavigationArgs)
                    return "Поиск";

                ForumItem forum = parameter as ForumItem;
                if (forum != null && !string.IsNullOrWhiteSpace(forum.Title))
                {
                    return forum.Title;
                }

                return "\u0424\u043E\u0440\u0443\u043C";
            }

            if (pageType == typeof(ForumsListPage))
            {
                return "\u0424\u043E\u0440\u0443\u043C";
            }

            if (pageType == typeof(QMSPage))
            {
                return "Сообщения";
            }

            if (pageType == typeof(FavouritesPage))
            {
                return "\u0418\u0437\u0431\u0440\u0430\u043D\u043D\u043E\u0435";
            }

            if (pageType == typeof(UserPage))
            {
                return "\u041F\u0440\u043E\u0444\u0438\u043B\u044C";
            }

            if (pageType == typeof(DevicePage))
            {
                DeviceNavigationArgs device = parameter as DeviceNavigationArgs;
                return device != null && !string.IsNullOrWhiteSpace(device.Name) ? device.Name : "Устройство";
            }

            if (pageType == typeof(LoginPage))
            {
                return "\u0412\u0445\u043E\u0434";
            }

            if (pageType == typeof(SettingsPage))
            {
                return "\u041D\u0430\u0441\u0442\u0440\u043E\u0439\u043A\u0438";
            }

            if (pageType == typeof(MessagePage))
            {
                return "Ответ";
            }

            if (pageType == typeof(TopicPage))
            {
                TopicNavigationState state = parameter as TopicNavigationState;
                ForumTopicItem topic = state == null ? parameter as ForumTopicItem : state.Topic;
                if (topic != null && !string.IsNullOrWhiteSpace(topic.Title))
                {
                    return topic.Title;
                }

                return "\u0422\u0435\u043C\u0430";
            }

            if (pageType == typeof(ChatPage))
            {
                QmsThreadNavigationArgs chat = parameter as QmsThreadNavigationArgs;
                if (chat != null && !string.IsNullOrWhiteSpace(chat.ThreadTitle))
                    return chat.ThreadTitle;

                return "\u0427\u0430\u0442";
            }

            return "4PDA";
        }

        private async void ForumAuthService_AccountChanged(object sender, EventArgs e)
        {
            await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, delegate
            {
                RefreshAccountItem();
                UpdateComposeItem();
            });
        }

        private async void AppNavigationActions_MessageContextChanged(object sender, EventArgs e)
        {
            await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, delegate
            {
                UpdateComposeItem();
                TopicPage topic = ContentFrame.Content as TopicPage;
                if (topic != null && !string.IsNullOrWhiteSpace(topic.TopicTitle)) ShowPageTitleHeader(topic.TopicTitle);
            });
        }

        private void UpdateComposeItem()
        {
            if (ComposeNavigationItem == null || TopNavigationBar == null || ContentFrame == null)
                return;

            bool shouldShow = ContentFrame.CurrentSourcePageType == typeof(TopicPage)
                && AppNavigationActions.CanOpenMessage;

            if (shouldShow)
            {
                ComposeNavigationItem.Glyph = "\uEDC6";
                ComposeNavigationItem.Width = 50;
                ComposeNavigationItem.MinWidth = 50;
                ComposeNavigationItem.IsEnabled = true;
            }
            else
            {
                HideComposeItem();
            }

            UpdateTitleWidth();
        }

        private void HideComposeItem()
        {
            if (ComposeNavigationItem == null)
                return;

            // Не удаляем модель из коллекции навбара: у старого UWP-контрола
            // повторная вставка DependencyObject иногда не перестраивала визуальное
            // дерево. Нулевая ширина надёжно скрывает кнопку и сохраняет её позицию
            // слева от поиска.
            ComposeNavigationItem.Glyph = string.Empty;
            ComposeNavigationItem.Width = 0;
            ComposeNavigationItem.MinWidth = 0;
            ComposeNavigationItem.IsEnabled = false;
        }

        private void RefreshAccountItem()
        {
            bool isAuthorized = ForumAuthService.IsAuthorized;

            if (MessagesSidebarItem != null)
                MessagesSidebarItem.IsEnabled = isAuthorized;

            if (FavouritesSidebarItem != null)
                FavouritesSidebarItem.IsEnabled = isAuthorized;

            if (AccountSidebarItem == null)
                return;

            string login = isAuthorized ? ForumAuthService.CurrentUserLogin : string.Empty;
            string title = !isAuthorized ? "Аккаунт" : string.IsNullOrWhiteSpace(login) ? "Профиль" : login;
            string avatar = isAuthorized ? ForumAuthService.CurrentUserAvatarCacheUri : string.Empty;
            UniversalSidebarIconType icon = string.IsNullOrWhiteSpace(avatar) ? UniversalSidebarIconType.Glyph : UniversalSidebarIconType.Image;
            // Each changed item rebuilds the sidebar. Avoid resetting a cached avatar
            // to a glyph and back on every page navigation.
            if (AccountSidebarItem.Glyph != "\uE77B") AccountSidebarItem.Glyph = "\uE77B";
            if (AccountSidebarItem.Text != title) AccountSidebarItem.Text = title;
            if (AccountSidebarItem.ImagePath != avatar) AccountSidebarItem.ImagePath = avatar;
            if (AccountSidebarItem.IconType != icon) AccountSidebarItem.IconType = icon;
        }

        private string GetSelectedKey(Type pageType, object parameter)
        {
            if (pageType == typeof(MainPage))
            {
                return "home";
            }

            if (pageType == typeof(NewsPage) || pageType == typeof(ShowNewsPage))
            {
                return "home";
            }

            if (pageType == typeof(ForumPage))
            {
                if (parameter is ForumSearchNavigationArgs)
                    return "forum";

                ForumItem forum = parameter as ForumItem;
                if (forum != null && forum.Id == AppNavigationActions.FavoriteForumsId)
                {
                    return "favourites";
                }

                return "forum";
            }

            if (pageType == typeof(ForumsListPage))
            {
                return "forum";
            }

            if (pageType == typeof(QMSPage) || pageType == typeof(ChatPage))
            {
                return "qms";
            }

            if (pageType == typeof(FavouritesPage))
            {
                return "favourites";
            }

            if (pageType == typeof(UserPage) || pageType == typeof(LoginPage) || pageType == typeof(DevicePage))
            {
                return "profile";
            }

            if (pageType == typeof(SettingsPage))
            {
                return "settings";
            }

            if (pageType == typeof(MessagePage))
            {
                return "forum";
            }

            return string.Empty;
        }
    }
}
