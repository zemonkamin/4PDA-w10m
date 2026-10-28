using System;
using _4PDA.Models;
using LegacyProjects.UI.UniversalWindowsPlatform.Controls;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace _4PDA
{
    public sealed class AppNavigationActions
    {
        internal const int FavoriteForumsId = -1000;
        private static SplitView _activeSidebar;
        private static Frame _contentFrame;
        private static MessagePageParameters _messageContext;
        private static int _pendingRefreshTopicId;
        private static int _pendingRefreshPostId;

        internal static event EventHandler MessageContextChanged;

        public static void RegisterSidebar(SplitView sidebar)
        {
            _activeSidebar = sidebar;
        }

        public static void UnregisterSidebar(SplitView sidebar)
        {
            if (_activeSidebar == sidebar)
                _activeSidebar = null;
        }

        public static void RegisterContentFrame(Frame frame)
        {
            _contentFrame = frame;
            NotifyNavigationStateChanged();
        }

        public static void UnregisterContentFrame(Frame frame)
        {
            if (_contentFrame == frame)
            {
                _contentFrame = null;
                NotifyNavigationStateChanged();
            }
        }

        internal static bool CanGoBack
        {
            get { return _contentFrame != null && _contentFrame.CanGoBack; }
        }

        internal static bool CanOpenMessage
        {
            get
            {
                return ForumAuthService.IsAuthorized
                    && _messageContext != null
                    && _messageContext.TopicId > 0
                    && _messageContext.CanReply;
            }
        }

        internal static void SetMessageContext(MessagePageParameters context)
        {
            _messageContext = context;
            EventHandler handler = MessageContextChanged;
            if (handler != null)
                handler(null, EventArgs.Empty);
        }

        internal static void RequestTopicRefresh(int topicId, int postId)
        {
            if (topicId <= 0)
                return;

            _pendingRefreshTopicId = topicId;
            _pendingRefreshPostId = Math.Max(0, postId);
        }

        internal static bool TryConsumeTopicRefresh(int topicId, out int postId)
        {
            postId = 0;
            if (topicId <= 0 || _pendingRefreshTopicId != topicId)
                return false;

            postId = _pendingRefreshPostId;
            _pendingRefreshTopicId = 0;
            _pendingRefreshPostId = 0;
            return true;
        }

        internal static bool TryGoBack()
        {
            if (PhotoViewer.TryCloseCurrent()) return true;
            if (_activeSidebar != null && _activeSidebar.IsPaneOpen)
            {
                _activeSidebar.IsPaneOpen = false;
                return true;
            }

            if (_contentFrame != null && _contentFrame.CanGoBack)
            {
                _contentFrame.GoBack();
                return true;
            }

            return false;
        }

        internal static void NotifyNavigationStateChanged()
        {
            App app = Application.Current as App;
            if (app != null)
                app.UpdateSystemBackButtonVisibility();
        }

        public static void ToggleSidebar()
        {
            if (_activeSidebar != null)
                _activeSidebar.IsPaneOpen = !_activeSidebar.IsPaneOpen;
        }

        public static void CloseSidebar()
        {
            if (_activeSidebar != null)
                _activeSidebar.IsPaneOpen = false;
        }

        public static void OpenHome()
        {
            Navigate(typeof(MainPage), null, true);
        }

        public static void OpenSite()
        {
            Navigate(typeof(NewsPage));
        }

        public static void OpenForum()
        {
            Navigate(typeof(ForumsListPage));
        }

        public static void OpenForumSearch()
        {
            Navigate(typeof(ForumPage), new ForumSearchNavigationArgs(), true);
        }

        public static void OpenForumTopics(ForumItem item)
        {
            Navigate(typeof(ForumPage), item, true);
        }

        public static void OpenTopic(ForumTopicItem item)
        {
            if (item == null) return;
            Navigate(typeof(TopicPage), new TopicNavigationState(item), true);
        }

        public static void OpenQms()
        {
            if (!ForumAuthService.IsAuthorized)
                return;

            Navigate(typeof(QMSPage));
        }

        public static void OpenFavourites()
        {
            if (!ForumAuthService.IsAuthorized)
                return;

            ForumItem favorites = new ForumItem();
            favorites.Id = FavoriteForumsId;
            favorites.Title = "Избранные форумы";
            Navigate(typeof(ForumPage), favorites, true);
        }

        public static void OpenProfile()
        {
            Navigate(ForumAuthService.IsAuthorized ? typeof(UserPage) : typeof(LoginPage));
        }

        public static void OpenSettings()
        {
            Navigate(typeof(SettingsPage));
        }

        public static void OpenNewsArticle(NewsItem item)
        {
            Navigate(typeof(ShowNewsPage), item, true);
        }

        public static void OpenDevice(DeviceNavigationArgs device)
        {
            if (device == null)
                return;

            Navigate(typeof(DevicePage), device, true);
        }

        public static void OpenMessage()
        {
            if (!CanOpenMessage)
                return;

            MessagePageParameters parameters = _messageContext.Clone();
            parameters.InitialText = string.Empty;
            Navigate(typeof(MessagePage), parameters, true);
        }

        internal static void OpenMessageForPost(TopicPostItem post)
        {
            if (!CanOpenMessage || post == null || post.Id <= 0)
                return;

            MessagePageParameters parameters = _messageContext.Clone();
            string author = (post.Author ?? string.Empty).Trim();
            while (author.StartsWith("*", StringComparison.Ordinal))
            {
                author = author.Substring(1).TrimStart();
            }

            parameters.InitialText = "[b]" + author + ",[/b] ";
            Navigate(typeof(MessagePage), parameters, true);
        }

        internal static void OpenEditMessage(TopicPostItem post)
        {
            if (!ForumAuthService.IsAuthorized || _messageContext == null || post == null || !post.CanEdit)
                return;
            MessagePageParameters parameters = _messageContext.Clone();
            parameters.EditPostId = post.Id;
            parameters.ExistingAttachmentIds = post.AttachmentIds == null ? null : (int[])post.AttachmentIds.Clone();
            parameters.InitialText = post.BodyBbCode;
            parameters.CanReply = true;
            Navigate(typeof(MessagePage), parameters, true);
        }

        private static void Navigate(Type pageType)
        {
            Navigate(pageType, null, false);
        }

        private static void Navigate(Type pageType, object parameter, bool force)
        {
            if (_contentFrame == null || pageType == null)
                return;

            if (force || _contentFrame.CurrentSourcePageType != pageType)
                _contentFrame.Navigate(pageType, parameter);

            CloseSidebar();
        }
    }

    public sealed class ForumSearchNavigationArgs
    {
        public string Query { get; set; }
    }

    public sealed class DeviceNavigationArgs
    {
        public string DeviceId { get; set; }
        public string Name { get; set; }
        public string Accessory { get; set; }
        public string Url { get; set; }
    }
}
