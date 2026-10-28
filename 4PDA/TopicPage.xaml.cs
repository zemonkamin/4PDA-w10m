using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using _4PDA.Models;
using _4PDA.Services;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using Windows.Storage;
using Windows.Storage.Streams;
using Windows.UI;
using Windows.UI.Popups;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Documents;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Animation;
using Windows.UI.Xaml.Media.Imaging;
using Windows.UI.Xaml.Navigation;
using Windows.Web.Http;
using Windows.Web.Http.Filters;

namespace _4PDA
{
    public sealed partial class TopicPage : Page
    {
        private static readonly Dictionary<string, string> SmileFiles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "acute", "acute.gif" },
            { "aggressive", "aggressive.gif" },
            { "air_kiss", "air_kiss.gif" },
            { "angry", "angry.gif" },
            { "antifeminism", "antifeminism.gif" },
            { "banned", "banned.gif" },
            { "beee", "beee.gif" },
            { "beta", "beta.gif" },
            { "biggrin", "biggrin.gif" },
            { "blink", "blink.gif" },
            { "blush", "blush.gif" },
            { "boy_girl", "boy_girl.gif" },
            { "bubble", "bubble.gif" },
            { "butcher", "butcher.gif" },
            { "censored", "censored.gif" },
            { "clap", "clap.gif" },
            { "clapping", "clapping.gif" },
            { "close_tema", "close_tema.gif" },
            { "coldly", "coldly.gif" },
            { "comando", "comando.gif" },
            { "confusion", "confusion.gif" },
            { "confusion2", "confusion2.gif" },
            { "congratulate", "congratulate.gif" },
            { "cool", "cool.gif" },
            { "cry", "cry.gif" },
            { "cry2", "cry2.gif" },
            { "daisy", "daisy.gif" },
            { "dance", "dance.gif" },
            { "dancer", "dancer.gif" },
            { "derisive", "derisive.gif" },
            { "devil", "devil.gif" },
            { "dinamo", "dinamo.gif" },
            { "dirol", "dirol.gif" },
            { "diver", "diver.gif" },
            { "download", "download.gif" },
            { "drag", "drag.gif" },
            { "drinks", "drinks.gif" },
            { "dry", "dry.gif" },
            { "feminist", "feminist.gif" },
            { "first_move", "first_move.gif" },
            { "flood", "flood.gif" },
            { "fool", "fool.gif" },
            { "foto", "foto.gif" },
            { "friends", "friends.gif" },
            { "girl_angry", "girl_angry.gif" },
            { "girl_blum", "girl_blum.gif" },
            { "girl_chmok", "girl_chmok.gif" },
            { "girl_cray", "girl_cray.gif" },
            { "girl_cray2", "girl_cray2.gif" },
            { "girl_crazy", "girl_crazy.gif" },
            { "girl_curtsey", "girl_curtsey.gif" },
            { "girl_dance", "girl_dance.gif" },
            { "girl_devil", "girl_devil.gif" },
            { "girl_flirt", "girl_flirt.gif" },
            { "girl_haha", "girl_haha.gif" },
            { "girl_hide", "girl_hide.gif" },
            { "girl_hospital", "girl_hospital.gif" },
            { "girl_hysterics", "girl_hysterics.gif" },
            { "girl_in_love", "girl_in_love.gif" },
            { "girl_kiss", "girl_kiss.gif" },
            { "girl_mad", "girl_mad.gif" },
            { "girl_parting", "girl_parting.gif" },
            { "girl_pinkglassesf", "girl_pinkglassesf.gif" },
            { "girl_prepare_fish", "girl_prepare_fish.gif" },
            { "girl_sad", "girl_sad.gif" },
            { "girl_smile", "girl_smile.gif" },
            { "girl_spruce_up", "girl_spruce_up.gif" },
            { "girl_tear", "girl_tear.gif" },
            { "girl_teddy", "girl_teddy.gif" },
            { "girl_tender", "girl_tender.gif" },
            { "girl_to_babruysk", "girl_to_babruysk.gif" },
            { "girl_to_take_umbrage", "girl_to_take_umbrage.gif" },
            { "girl_tongue", "girl_tongue.gif" },
            { "girl_triniti", "girl_triniti.gif" },
            { "girl_wacko", "girl_wacko.gif" },
            { "girl_werewolf", "girl_werewolf.gif" },
            { "girl_wink", "girl_wink.gif" },
            { "girl_wink2", "girl_wink2.gif" },
            { "girl_witch", "girl_witch.gif" },
            { "give_rose", "give_rose.gif" },
            { "good", "good.gif" },
            { "good_luck", "good_luck.gif" },
            { "grabli", "grabli.gif" },
            { "guess", "guess.gif" },
            { "hang", "hang.gif" },
            { "happy", "happy.gif" },
            { "heart", "heart.gif" },
            { "help", "help.gif" },
            { "helpsmilie", "helpsmilie.gif" },
            { "hemp", "hemp.gif" },
            { "heppy_dancing", "heppy_dancing.gif" },
            { "huh", "huh.gif" },
            { "hysterics", "hysterics.gif" },
            { "i-m_so_happy", "i-m_so_happy.gif" },
            { "indeec", "indeec.gif" },
            { "kindness", "kindness.gif" },
            { "king", "king.gif" },
            { "kiss", "kiss.gif" },
            { "kiss2", "kiss2.gif" },
            { "kitten", "kitten.gif" },
            { "laugh", "laugh.gif" },
            { "laugh2", "laugh2.gif" },
            { "laugh_wild", "laugh_wild.gif" },
            { "love_4PDA", "love_4PDA.gif" },
            { "lol", "laugh.gif" },
            { "mad", "mad.gif" },
            { "mellow", "mellow.gif" },
            { "moil", "moil.gif" },
            { "nea", "nea.gif" },
            { "no", "no.gif" },
            { "nono", "nono.gif" },
            { "offtopic", "offtopic.gif" },
            { "ohmy", "ohmy.gif" },
            { "ok", "ok.gif" },
            { "papuas", "papuas.gif" },
            { "party", "party.gif" },
            { "ph34r", "ph34r.gif" },
            { "pioneer_smoke", "pioneer_smoke.gif" },
            { "pipiska", "pipiska.gif" },
            { "popcorm", "popcorm.gif" },
            { "protest", "protest.gif" },
            { "rabbi", "rabbi.gif" },
            { "resent", "resent.gif" },
            { "rofl", "rofl.gif" },
            { "roll", "roll.gif" },
            { "rolleyes", "rolleyes.gif" },
            { "rolleyes2", "rolleyes2.gif" },
            { "rtfm", "rtfm.gif" },
            { "russian", "russian.gif" },
            { "russian_garmoshka", "russian_garmoshka.gif" },
            { "russian_ru", "russian_ru.gif" },
            { "sad", "sad.gif" },
            { "scare", "scare.gif" },
            { "scratch_one-s_head", "scratch_one-s_head.gif" },
            { "search", "search.gif" },
            { "secret", "secret.gif" },
            { "shok", "shok.gif" },
            { "skull", "skull.gif" },
            { "sleep", "sleep.gif" },
            { "smile", "smile.gif" },
            { "smile_good", "smile_good.gif" },
            { "smile_good2", "smile_good2.gif" },
            { "smoke", "smoke.gif" },
            { "sorry", "sorry.gif" },
            { "spiteful", "spiteful.gif" },
            { "stop_flood", "stop_flood.gif" },
            { "stop_holywar", "stop_holywar.gif" },
            { "suicide", "suicide.gif" },
            { "superman", "superman.gif" },
            { "superstition", "superstition.gif" },
            { "sveta", "sveta.gif" },
            { "tablet_protiv", "tablet_protiv.gif" },
            { "tablet_za", "tablet_za.gif" },
            { "thank_you", "thank_you.gif" },
            { "this", "this.gif" },
            { "to_clue", "to_clue.gif" },
            { "tomato", "tomato.gif" },
            { "tommy", "tommy.gif" },
            { "tongue", "tongue.gif" },
            { "tongue3", "tongue3.gif" },
            { "umnik", "umnik.gif" },
            { "unsure", "unsure.gif" },
            { "victory", "victory.gif" },
            { "vinsent", "vinsent.gif" },
            { "wacko", "wacko.gif" },
            { "wallbash", "wallbash.gif" },
            { "whistle", "whistle.gif" },
            { "wink", "wink.gif" },
            { "wink_kind", "wink_kind.gif" },
            { "wub", "wub.gif" },
            { "yahoo", "yahoo.gif" },
            { "yes", "yes.gif" },
        };

        private const string FileIconTag = "4pda-file-icon";
        private const string FourPdaDownloadIconFileName = "mQ600b0kBHtbbyz1UYtbg5GfeT1YikOfEOteLNm9MACz2oFQOdeHvBJLWbb5SV.gif";

        private readonly FourPdaForumService _forumService = new FourPdaForumService();
        private readonly CoreDispatcher _uiDispatcher;
        private readonly HttpBaseProtocolFilter _downloadFilter = new HttpBaseProtocolFilter();
        private readonly HttpClient _downloadClient;
        private readonly ObservableCollection<TopicPostItem> _postItems = new ObservableCollection<TopicPostItem>();
        private readonly Dictionary<int, TopicPostItem> _postsById = new Dictionary<int, TopicPostItem>();
        private ScrollViewer TopicScrollViewer;
        private int _loadVersion;
        private readonly HashSet<int> _loadedPosts = new HashSet<int>();
        private readonly List<Image> _images = new List<Image>();
        private readonly Dictionary<Image, Uri> _photoTargets = new Dictionary<Image, Uri>();
        private readonly Dictionary<Image, PostImageLayoutInfo> _imageLayouts = new Dictionary<Image, PostImageLayoutInfo>();
        private readonly Dictionary<int, FrameworkElement> _postElements = new Dictionary<int, FrameworkElement>();
        private string _topicUrl;
        private string _topicTitle;
        private int _topicId;
        private int _forumId;
        private bool _topicKnownClosed;
        private int _pendingRefreshPostId;
        private bool _refreshAfterSendRequested;
        private MessagePageParameters _messageContext;
        private bool _canReply;
        private int _start;
        private readonly TopicPagingState _paging = new TopicPagingState();
        private TopicNavigationState _navigationState;
        private bool _usePagination;
        private readonly ScrollJumpState _scrollHint = new ScrollJumpState();
        private readonly DispatcherTimer _scrollHideTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        private readonly Storyboard _scrollFade = new Storyboard();
        private bool _showScrollJump, _scrollJumping, _jumpToBottom;
        private int _pageSize = 20;
        private bool _loading;
        private bool _isPageActive;
        private bool _postJumping;
        private int _initialLinkedPostId;
        private int _initialRequestedPostId;
        private string _initialLinkUrl;
        private readonly Dictionary<int, List<SpoilerBlock>> _spoilers = new Dictionary<int, List<SpoilerBlock>>();
        private readonly Dictionary<string, bool> _spoilerOpen = new Dictionary<string, bool>();
        internal string TopicTitle { get { return _topicTitle; } }
        private bool _hasMore = true;
        private bool _downloadingFile;
        private DateTime _lastDownloadProgressUpdate = DateTime.MinValue;
        private ulong _lastDownloadProgressBytes;
        private readonly object _downloadProgressSync = new object();
        private DateTime _downloadSpeedStartedAt = DateTime.MinValue;

        public TopicPage()
        {
            InitializeComponent();
            _uiDispatcher = Dispatcher;
            _downloadFilter.CookieUsageBehavior = HttpCookieUsageBehavior.NoCookies;
            _downloadClient = new HttpClient(_downloadFilter);
            ForumCookieHelper.ApplyDefaultHeaders(_downloadClient);
            // Match the forum page: post media is constrained by the content area,
            // so the page itself never needs horizontal scrolling.
            PostsListView.ItemsSource = _postItems;
            TopicRootGrid.SizeChanged += TopicRootGrid_SizeChanged;
            _scrollHideTimer.Tick += ScrollJumpHideTimer_Tick;
            var fade = new DoubleAnimation { To = 0, Duration = TimeSpan.FromMilliseconds(280) };
            Storyboard.SetTarget(fade, ScrollJumpButton);
            Storyboard.SetTargetProperty(fade, "Opacity");
            _scrollFade.Children.Add(fade);
            _scrollFade.Completed += delegate { if (!_scrollHideTimer.IsEnabled) HideScrollJump(); };
        }

        protected async override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            _isPageActive = true;
            _loadVersion++;
            _loading = false;
            _postJumping = false;
            int version = _loadVersion;
            _usePagination = AppSettingsService.UseTopicPagination;
            _showScrollJump = AppSettingsService.ShowTopicScrollJump;
            _scrollJumping = false;
            _scrollHint.Reset();
            HideScrollJump();
            _paging.Reset();
            _navigationState = e.Parameter as TopicNavigationState;
            ForumTopicItem topic = _navigationState == null ? e.Parameter as ForumTopicItem : _navigationState.Topic;
            if (topic != null)
            {
                _topicUrl = _navigationState == null ? topic.Url : _navigationState.Url;
                _topicTitle = topic.Title;
                _topicId = topic.Id;
                _forumId = topic.ForumId;
                _topicKnownClosed = topic.IsClosed;
            }
            else
            {
                _topicUrl = _navigationState == null ? e.Parameter as string : _navigationState.Url;
                _topicTitle = string.Empty;
                _topicId = ExtractTopicId(_topicUrl);
                _forumId = ExtractForumId(_topicUrl);
                _topicKnownClosed = false;
            }

            _initialLinkUrl = _topicUrl;
            _pendingRefreshPostId = 0;
            _refreshAfterSendRequested = AppNavigationActions.TryConsumeTopicRefresh(_topicId, out _pendingRefreshPostId);

            _postItems.Clear();
            _postsById.Clear();
            _images.Clear();
            _photoTargets.Clear();
            _loadedPosts.Clear();
            _postElements.Clear();
            _imageLayouts.Clear();
            _spoilers.Clear();
            _spoilerOpen.Clear();
            _initialLinkedPostId = _initialRequestedPostId = 0;
            _start = 0;
            _hasMore = true;
            _canReply = false;
            _messageContext = null;
            AppNavigationActions.SetMessageContext(null);
            PublishProvisionalMessageContext();
            EmptyTextBlock.Visibility = Visibility.Collapsed;
            UpdatePagination();

            await LoadNextPageAsync(true);
            if (!_isPageActive || version != _loadVersion) return;

            if (_pendingRefreshPostId > 0)
            {
                int postId = _pendingRefreshPostId;
                _pendingRefreshPostId = 0;
                await ScrollToPostAsync(postId, BuildFindPostUri(postId));
            }
            else if (_initialLinkedPostId > 0)
            {
                Uri linkedUri;
                Uri.TryCreate(_initialLinkUrl, UriKind.Absolute, out linkedUri);
                await ScrollToPostAsync(_initialLinkedPostId, linkedUri);
                if (_initialRequestedPostId > 0 && _initialRequestedPostId != _initialLinkedPostId)
                    await ShowPostMessageAsync("Пост удалён. Показано ближайшее доступное сообщение.");
            }
            else if (_refreshAfterSendRequested && _topicId > 0)
            {
                // Некоторые ответы 4PDA не содержат ID созданного поста. В таком
                // случае после возврата из редактора отдельно запрашиваем последнюю
                // страницу темы и прокручиваем ленту вниз.
                await LoadPostUrlAsync("https://4pda.to/forum/index.php?showtopic="
                    + _topicId.ToString() + "&view=getlastpost");
                await ScrollToTopicBottomAsync();
            }

            _refreshAfterSendRequested = false;
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            LegacyProjects.UI.UniversalWindowsPlatform.Controls.PhotoViewer.TryCloseCurrent();
            PageNumberFlyout.Hide();
            HideScrollJump();
            _scrollHint.Reset();
            if (_navigationState != null && !_usePagination)
            {
                var panel = PostsListView.ItemsPanelRoot as ItemsStackPanel;
                int first = panel == null ? 0 : Math.Max(0, panel.FirstVisibleIndex);
                for (int i = first; i < _postItems.Count; i++)
                    if (_postItems[i].Number > 0)
                    {
                        _navigationState.Url = _topicUrl + "&st=" + ForumPostNavigation.PageOffset(_postItems[i].Number - 1, _pageSize);
                        break;
                    }
            }
            _isPageActive = false;
            _loadVersion++;
            if (TopicScrollViewer != null) TopicScrollViewer.ViewChanged -= TopicScrollViewer_ViewChanged;
            TopicScrollViewer = null;
            foreach (FrameworkElement root in new List<FrameworkElement>(_postElements.Values)) ReleasePostVisual(root);
            _postItems.Clear();
            _postsById.Clear();
            _loadedPosts.Clear();
            HideDownloadOverlay();
            AppNavigationActions.SetMessageContext(null);
            base.OnNavigatedFrom(e);
        }

        private async void TopicScrollViewer_ViewChanged(object sender, ScrollViewerViewChangedEventArgs e)
        {
            if (TopicScrollViewer != null)
            {
                ScrollJumpAction action = _scrollHint.Observe(TopicScrollViewer.VerticalOffset, TopicScrollViewer.ScrollableHeight,
                    _showScrollJump && _isPageActive, _scrollJumping || _postJumping || _loading);
                if (action == ScrollJumpAction.Hide) HideScrollJump();
                else if (action == ScrollJumpAction.Up || action == ScrollJumpAction.Down) ShowScrollJump(action == ScrollJumpAction.Down);
            }
            if (!e.IsIntermediate && ShouldLoadMore())
            {
                await LoadNextPageAsync(false);
            }
        }

        private void ShowScrollJump(bool down)
        {
            _jumpToBottom = down;
            ScrollJumpIcon.Glyph = down ? "\uE74B" : "\uE74A";
            Windows.UI.Xaml.Automation.AutomationProperties.SetName(ScrollJumpButton, down ? "В конец списка" : "В начало списка");
            _scrollFade.Stop();
            ScrollJumpButton.Opacity = 1;
            ScrollJumpButton.Visibility = Visibility.Visible;
            _scrollHideTimer.Stop(); _scrollHideTimer.Start();
        }

        private void HideScrollJump()
        {
            _scrollHideTimer.Stop(); _scrollFade.Stop();
            ScrollJumpButton.Visibility = Visibility.Collapsed;
            ScrollJumpButton.Opacity = 0;
        }

        private void ScrollJumpHideTimer_Tick(object sender, object e)
        {
            _scrollHideTimer.Stop();
            if (_isPageActive && ScrollJumpButton.Visibility == Visibility.Visible) _scrollFade.Begin();
        }

        private async void ScrollJumpButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_isPageActive || _loading || _postJumping || _scrollJumping || TopicScrollViewer == null || _postItems.Count == 0) return;
            bool down = _jumpToBottom;
            int version = _loadVersion;
            _scrollJumping = true;
            HideScrollJump();
            UpdatePagination();
            try
            {
                PostsListView.ScrollIntoView(_postItems[down ? _postItems.Count - 1 : 0], ScrollIntoViewAlignment.Leading);
                PostsListView.UpdateLayout();
                await Task.Delay(80);
                if (!_isPageActive || version != _loadVersion || TopicScrollViewer == null) return;
                PostsListView.UpdateLayout();
                TopicScrollViewer.ChangeView(null, down ? TopicScrollViewer.ScrollableHeight : 0, null, true);
            }
            catch (Exception error) { System.Diagnostics.Debug.WriteLine("[4PDA.Topic] Scroll jump: " + error.GetType().Name); }
            finally
            {
                if (version == _loadVersion)
                {
                    _scrollJumping = false;
                    if (TopicScrollViewer != null) _scrollHint.Reset(TopicScrollViewer.VerticalOffset, TopicScrollViewer.ScrollableHeight);
                    UpdatePagination();
                }
            }
        }

        private bool ShouldLoadMore()
        {
            if (_usePagination || !_isPageActive || _postJumping || _scrollJumping || _loading || !_hasMore)
            {
                return false;
            }

            return TopicScrollViewer != null && (TopicScrollViewer.ScrollableHeight <= 0
                || TopicScrollViewer.VerticalOffset >= TopicScrollViewer.ScrollableHeight - 520);
        }

        private async System.Threading.Tasks.Task LoadNextPageAsync(bool firstLoad)
        {
            if ((!firstLoad && _usePagination) || !_isPageActive || _postJumping || _loading || !_hasMore)
            {
                return;
            }

            int version = _loadVersion;
            _loading = true;
            SetBusy(firstLoad, true);

            try
            {
                TopicPageData data = await GetTopicPageAsync(_topicUrl, _start);
                if (!_isPageActive || version != _loadVersion) return;
                if (data != null)
                {
                    if (firstLoad)
                    {
                        _start = data.StartOffset;
                        _initialLinkedPostId = data.TargetPostId;
                        _initialRequestedPostId = data.RequestedPostId;
                    }
                    ApplyTopicPage(data, false);
                }
                else
                {
                    _hasMore = false;
                }

                EmptyTextBlock.Visibility = _postItems.Count > 0 ? Visibility.Collapsed : Visibility.Visible;
            }
            catch (Exception error)
            {
                if (!_isPageActive || version != _loadVersion) return;
                if (_postItems.Count == 0)
                {
                    EmptyTextBlock.Text = "Тема не загружена. " + error.Message;
                    EmptyTextBlock.Visibility = Visibility.Visible;
                }

                _hasMore = false;
            }
            finally
            {
                if (version == _loadVersion)
                {
                    _loading = false;
                    SetBusy(firstLoad, false);
                    await Task.Delay(16);
                    if (version == _loadVersion && !firstLoad && ShouldLoadMore()) await LoadNextPageAsync(false);
                }
            }
        }

        private void PublishProvisionalMessageContext()
        {
            if (!ForumAuthService.IsAuthorized || _topicKnownClosed || _topicId <= 0)
                return;

            _canReply = true;
            _messageContext = new MessagePageParameters
            {
                TopicId = _topicId,
                ForumId = _forumId,
                Start = Math.Max(0, ForumPostNavigation.StartOffset(_topicUrl, _pageSize)),
                TopicTitle = _topicTitle,
                TopicUrl = _topicUrl,
                AuthKey = string.Empty,
                CanReply = true
            };

            AppNavigationActions.SetMessageContext(_messageContext);
        }

        private void UpdateMessageContext(TopicPageData data)
        {
            if (data == null)
                return;

            if (!string.IsNullOrWhiteSpace(data.Title))
                _topicTitle = data.Title;
            if (data.TopicId > 0)
                _topicId = data.TopicId;
            if (data.ForumId > 0)
                _forumId = data.ForumId;
            if (!string.IsNullOrWhiteSpace(data.Url))
                _topicUrl = data.Url;

            // Отсутствие auth_key или старого HTML-маркера не означает, что тема
            // закрыта. Кнопку скрываем только при явном запрете, распознанном
            // сервисом, либо если тема уже была помечена закрытой в списке.
            _canReply = ForumAuthService.IsAuthorized
                && !_topicKnownClosed
                && !data.IsReplyForbidden
                && _topicId > 0;

            _messageContext = new MessagePageParameters
            {
                TopicId = _topicId,
                ForumId = _forumId,
                Start = Math.Max(0, data.StartOffset),
                TopicTitle = _topicTitle,
                TopicUrl = string.IsNullOrWhiteSpace(data.Url) ? _topicUrl : data.Url,
                AuthKey = data.AuthKey,
                CanReply = _canReply
            };

            AppNavigationActions.SetMessageContext(_canReply ? _messageContext : null);
        }

        private static int ExtractTopicId(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return 0;

            Match match = Regex.Match(url, "(?:[?&](?:showtopic|t)=|/topic/)(\\d+)", RegexOptions.IgnoreCase);
            int value;
            return match.Success && int.TryParse(match.Groups[1].Value, out value) ? value : 0;
        }

        private static int ExtractForumId(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return 0;

            Match match = Regex.Match(url, "(?:[?&](?:showforum|f)=|/forum/)(\\d+)", RegexOptions.IgnoreCase);
            int value;
            return match.Success && int.TryParse(match.Groups[1].Value, out value) ? value : 0;
        }

        private void SetBusy(bool firstLoad, bool busy)
        {
            if (busy) HideScrollJump();
            if (firstLoad)
            {
                LoadingRing.IsActive = busy;
                LoadingRing.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
            }
            else
            {
                LoadMoreRing.IsActive = busy;
                LoadMoreRing.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
            }
            PostsListView.IsHitTestVisible = !(firstLoad && busy);
            UpdatePagination();
        }

        private async Task<TopicPageData> GetTopicPageAsync(string url, int start)
        {
            TopicPageData data = await _forumService.GetTopicAsync(url, start);
            if (data != null && _isPageActive && data.Posts.Count == 0 && data.StartOffset > 0)
            {
                // Posts may have been deleted since the last visit. Load the new
                // last page rather than leave the reader on an empty stale page.
                var bounds = new TopicPagingState();
                bounds.Apply(data.StartOffset, data.TotalPages, data.PageSize);
                int last = bounds.OffsetForPage(bounds.TotalPages);
                if (data.StartOffset > last) data = await _forumService.GetTopicAsync(data.Url, last);
            }
            return data;
        }

        private void ClearPagePosts()
        {
            foreach (FrameworkElement root in new List<FrameworkElement>(_postElements.Values)) ReleasePostVisual(root);
            _postItems.Clear(); _postsById.Clear(); _loadedPosts.Clear();
            _postElements.Clear(); _images.Clear(); _photoTargets.Clear(); _imageLayouts.Clear();
            _spoilers.Clear(); _spoilerOpen.Clear();
        }

        private void ApplyTopicPage(TopicPageData data, bool replace)
        {
            if (replace) ClearPagePosts();
            if (data.PageSize > 0) _pageSize = data.PageSize;
            _paging.Apply(data.StartOffset, data.TotalPages, _pageSize);
            _start = data.StartOffset + _pageSize;
            _hasMore = data.HasMore && data.Posts.Count > 0;
            UpdateMessageContext(data);
            if (_navigationState != null)
                _navigationState.Url = _topicUrl + "&st=" + _paging.OffsetForPage(_paging.CurrentPage);
            foreach (TopicPostItem post in data.Posts)
                if (post.Id > 0 && _loadedPosts.Add(post.Id)) AddPost(post);
            EmptyTextBlock.Visibility = _postItems.Count > 0 ? Visibility.Collapsed : Visibility.Visible;
            UpdatePagination();
        }

        private void UpdatePagination()
        {
            bool show = _paging.ShowPagination(_usePagination, _loading, _postItems.Count);
            PaginationBar.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            InfiniteScrollFooter.Visibility = _usePagination || show ? Visibility.Collapsed : Visibility.Visible;
            PageNumberButton.Content = _paging.CurrentPage + "/" + _paging.TotalPages;
            Windows.UI.Xaml.Automation.AutomationProperties.SetName(PageNumberButton,
                "Страница " + _paging.CurrentPage + " из " + _paging.TotalPages + ". Выбрать страницу");
            bool ready = _isPageActive && !_loading && !_postJumping && !_scrollJumping;
            FirstPageButton.IsEnabled = PreviousPageButton.IsEnabled = ready && _paging.CanPrevious;
            NextPageButton.IsEnabled = LastPageButton.IsEnabled = ready && _paging.CanNext;
            PageNumberButton.IsEnabled = ready && _paging.TotalPages > 1;
        }

        private async Task NavigatePageAsync(int page)
        {
            if (!_paging.ShowPagination(_usePagination, _loading, _postItems.Count)
                || !_isPageActive || _postJumping || _scrollJumping) return;
            PageNumberFlyout.Hide();
            await LoadPostUrlAsync("https://4pda.to/forum/index.php?showtopic=" + _topicId,
                _paging.OffsetForPage(page), true);
        }

        private async void FirstPageButton_Click(object sender, RoutedEventArgs e) { await NavigatePageAsync(1); }
        private async void PreviousPageButton_Click(object sender, RoutedEventArgs e) { await NavigatePageAsync(_paging.CurrentPage - 1); }
        private async void NextPageButton_Click(object sender, RoutedEventArgs e) { await NavigatePageAsync(_paging.CurrentPage + 1); }
        private async void LastPageButton_Click(object sender, RoutedEventArgs e) { await NavigatePageAsync(_paging.TotalPages); }

        private void PageNumberFlyout_Opened(object sender, object e)
        {
            PageRangeText.Text = "Номер страницы (1–" + _paging.TotalPages + ")";
            PageNumberTextBox.Text = _paging.CurrentPage.ToString();
            PageNumberTextBox.SelectAll();
            PageNumberTextBox.Focus(FocusState.Programmatic);
        }

        private bool TryPageNumber(out int page)
        {
            return int.TryParse(PageNumberTextBox.Text.Trim(), out page) && page >= 1 && page <= _paging.TotalPages;
        }

        private void PageNumberTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (GoToPageButton == null) return;
            int page; GoToPageButton.IsEnabled = TryPageNumber(out page);
        }

        private async void GoToPageButton_Click(object sender, RoutedEventArgs e)
        {
            int page; if (TryPageNumber(out page)) await NavigatePageAsync(page);
        }

        private async void PageNumberTextBox_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key != Windows.System.VirtualKey.Enter) return;
            e.Handled = true;
            int page; if (TryPageNumber(out page)) await NavigatePageAsync(page);
        }

        private void PostsListView_Loaded(object sender, RoutedEventArgs e)
        {
            if (TopicScrollViewer != null) TopicScrollViewer.ViewChanged -= TopicScrollViewer_ViewChanged;
            TopicScrollViewer = FindPostScrollViewer(PostsListView);
            if (TopicScrollViewer != null) TopicScrollViewer.ViewChanged += TopicScrollViewer_ViewChanged;
        }

        private static ScrollViewer FindPostScrollViewer(DependencyObject parent)
        {
            var scroll = parent as ScrollViewer;
            if (scroll != null) return scroll;
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                scroll = FindPostScrollViewer(VisualTreeHelper.GetChild(parent, i));
                if (scroll != null) return scroll;
            }
            return null;
        }

        private void PostsListView_ContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
        {
            Border host = args.ItemContainer.ContentTemplateRoot as Border;
            if (args.InRecycleQueue)
            {
                if (host != null) { ReleasePostVisual(host.Child as FrameworkElement); host.Child = null; }
                return;
            }
            args.Handled = true;
            if (args.Phase == 0)
            {
                FrameworkElement old = host == null ? null : host.Child as FrameworkElement;
                if (old != null && !ReferenceEquals(old.Tag, args.Item))
                { ReleasePostVisual(old); host.Child = null; }
                args.RegisterUpdateCallback(1, PostsListView_ContainerContentChanging);
            }
            else if (_isPageActive) RealizePost(args.ItemContainer, args.Item as TopicPostItem);
        }

        private void RealizePost(SelectorItem container, TopicPostItem post)
        {
            Border host = container == null ? null : container.ContentTemplateRoot as Border;
            if (host == null || post == null) return;
            FrameworkElement old = host.Child as FrameworkElement;
            if (old != null && ReferenceEquals(old.Tag, post)) return;
            ReleasePostVisual(old);
            try { host.Child = CreatePost(post); }
            catch (Exception error)
            {
                FrameworkElement failed;
                if (_postElements.TryGetValue(post.Id, out failed)) ReleasePostVisual(failed);
                // Container callbacks run outside the network-load try/catch.
                // Preserve a readable post if malformed content cannot build its view.
                var fallback = new Border { Tag = post, Padding = new Thickness(12), Margin = new Thickness(0, 0, 0, 8) };
                fallback.Child = new TextBlock { Text = post.Author + "\n" + CleanInlineText(post.BodyHtml),
                    TextWrapping = TextWrapping.Wrap, FontSize = 16, Foreground = ResolveBrush("PdaTextBrush") };
                _postElements[post.Id] = fallback;
                host.Child = fallback;
                System.Diagnostics.Debug.WriteLine("[4PDA.Topic] Post view fallback: " + error.GetType().Name);
            }
        }

        private void ReleasePostVisual(FrameworkElement root)
        {
            if (root == null) return;
            TopicPostItem post = root.Tag as TopicPostItem;
            FrameworkElement current;
            if (post != null && _postElements.TryGetValue(post.Id, out current) && ReferenceEquals(current, root))
            {
                _postElements.Remove(post.Id);
                _spoilers.Remove(post.Id);
            }
            ReleasePostImages(root);
        }

        private void ReleasePostImages(DependencyObject element)
        {
            Image image = element as Image;
            if (image != null)
            {
                DeferredImage.SetUrl(image, string.Empty);
                image.Source = null;
                _images.Remove(image);
                _photoTargets.Remove(image);
                _imageLayouts.Remove(image);
            }
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(element); i++) ReleasePostImages(VisualTreeHelper.GetChild(element, i));
        }

        private void AddPost(TopicPostItem post)
        {
            _postsById[post.Id] = post;
            _postItems.Insert(GetPostInsertIndex(post), post);
        }

        private FrameworkElement CreatePost(TopicPostItem post)
        {
            Border root = new Border();
            root.Background = ResolveBrush("PdaSurfaceBrush");
            root.BorderBrush = ResolveBrush("PdaSelectedBrush");
            root.BorderThickness = new Thickness(0, 0, 0, 1);
            root.Margin = new Thickness(0, 0, 0, 8);

            StackPanel layout = new StackPanel();
            root.Child = layout;
            root.Tag = post;
            if (post.Id > 0)
            {
                _postElements[post.Id] = root;
            }

            Grid header = new Grid();
            header.Background = ResolveBrush("PdaSelectedBrush");
            header.Padding = new Thickness(12, 9, 12, 9);
            header.ColumnDefinitions.Add(new ColumnDefinition() { Width = GridLength.Auto });
            header.ColumnDefinitions.Add(new ColumnDefinition() { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition() { Width = GridLength.Auto });

            FrameworkElement avatar = CreateAvatar(post);
            if (post.UserId > 0)
            {
                avatar.Tag = post.UserId;
                avatar.Tapped += (sender, args) => OpenUserProfile(post.UserId);
            }
            Grid.SetColumn(avatar, 0);
            header.Children.Add(avatar);

            StackPanel authorPanel = new StackPanel();
            authorPanel.Margin = new Thickness(10, 0, 8, 0);
            if (post.UserId > 0)
            {
                authorPanel.Tag = post.UserId;
                authorPanel.Tapped += (sender, args) => OpenUserProfile(post.UserId);
            }
            Grid.SetColumn(authorPanel, 1);

            TextBlock author = new TextBlock();
            author.Text = string.IsNullOrWhiteSpace(post.Author) ? "4PDA" : post.Author;
            author.FontSize = 16;
            author.FontWeight = Windows.UI.Text.FontWeights.SemiBold;
            author.Foreground = ResolveBrush("PdaTextBrush");
            author.TextTrimming = TextTrimming.CharacterEllipsis;
            authorPanel.Children.Add(author);

            TextBlock info = new TextBlock();
            info.Text = BuildUserMeta(post);
            info.FontSize = 12;
            info.Margin = new Thickness(0, 2, 0, 0);
            info.Foreground = ResolveBrush("PdaSecondaryTextBrush");
            info.TextTrimming = TextTrimming.CharacterEllipsis;
            authorPanel.Children.Add(info);

            header.Children.Add(authorPanel);

            Grid rightHeader = new Grid();
            rightHeader.HorizontalAlignment = HorizontalAlignment.Right;
            rightHeader.ColumnDefinitions.Add(new ColumnDefinition() { Width = GridLength.Auto });
            rightHeader.ColumnDefinitions.Add(new ColumnDefinition() { Width = GridLength.Auto });
            Grid.SetColumn(rightHeader, 2);

            StackPanel numberPanel = new StackPanel();
            numberPanel.HorizontalAlignment = HorizontalAlignment.Right;
            Grid.SetColumn(numberPanel, 0);

            TextBlock number = new TextBlock();
            number.Text = post.NumberText;
            number.FontSize = 14;
            number.FontWeight = Windows.UI.Text.FontWeights.SemiBold;
            number.Foreground = ResolveBrush("SystemControlHighlightAccentBrush");
            number.HorizontalAlignment = HorizontalAlignment.Right;
            numberPanel.Children.Add(number);

            TextBlock date = new TextBlock();
            date.Text = post.Date;
            date.FontSize = 12;
            date.Margin = new Thickness(0, 2, 0, 0);
            date.Foreground = ResolveBrush("PdaSecondaryTextBrush");
            date.HorizontalAlignment = HorizontalAlignment.Right;
            numberPanel.Children.Add(date);
            rightHeader.Children.Add(numberPanel);

            if (ForumAuthService.IsAuthorized && HasPostMenu(post))
            {
                Button menuButton = new Button();
                menuButton.Style = Resources["PostMenuIconButtonStyle"] as Style;
                menuButton.Width = 40;
                menuButton.Height = 40;
                menuButton.MinWidth = 40;
                menuButton.MinHeight = 40;
                menuButton.Margin = new Thickness(6, -3, -6, 0);
                menuButton.VerticalAlignment = VerticalAlignment.Top;
                menuButton.HorizontalAlignment = HorizontalAlignment.Right;
                menuButton.Content = new TextBlock
                {
                    Text = "\uE712",
                    FontFamily = new FontFamily("Segoe MDL2 Assets"),
                    FontSize = 20,
                    Foreground = ResolveBrush("PdaTextBrush"),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
                menuButton.Tag = post;
                menuButton.Click += PostMenuButton_Click;
                Grid.SetColumn(menuButton, 1);
                rightHeader.Children.Add(menuButton);
            }

            header.Children.Add(rightHeader);
            layout.Children.Add(header);

            StackPanel body = new StackPanel();
            body.Padding = new Thickness(12, 10, 12, 14);
            body.SizeChanged += PostBody_SizeChanged;

            try
            {
                _spoilers.Remove(post.Id);
                RenderPostBody(body, post.BodyHtml, post.Id);
            }
            catch
            {
                // A malformed forum fragment must not abort loading the whole topic.
                // Fall back to plain text for this post only.
                body.Children.Clear();
                TextBlock fallback = new TextBlock();
                fallback.Text = CleanInlineText(post.BodyHtml);
                fallback.TextWrapping = TextWrapping.Wrap;
                fallback.FontSize = 16;
                fallback.LineHeight = 24;
                fallback.Foreground = ResolveBrush("PdaTextBrush");
                body.Children.Add(fallback);
            }

            layout.Children.Add(body);

            if (ForumAuthService.IsAuthorized)
            {
                FrameworkElement actions = CreatePostActions(post);
                if (actions != null)
                    layout.Children.Add(actions);
            }

            return root;
        }

        private bool HasPostMenu(TopicPostItem post)
        {
            // Как в шаблоне ForPDA: меню поста присутствует всегда. Даже когда
            // сервер не разрешает модераторские действия, в нём остаются ссылка
            // и ответ. Гостям кнопка отсекается вызывающим условием.
            return post != null && post.Id > 0;
        }

        private bool IsOwnPost(TopicPostItem post)
        {
            if (post == null || post.UserId <= 0)
                return false;

            int currentUserId;
            if (!int.TryParse(ForumAuthService.CurrentUserId, out currentUserId))
                return false;

            return currentUserId == post.UserId;
        }

        private FrameworkElement CreatePostActions(TopicPostItem post)
        {
            if (post == null)
                return null;

            StackPanel panel = new StackPanel();
            panel.Orientation = Orientation.Horizontal;
            panel.HorizontalAlignment = HorizontalAlignment.Right;
            panel.Padding = new Thickness(12, 0, 12, 10);

            if (_canReply && post.Id > 0)
            {
                Button reply = CreatePostActionButton("Ответить");
                reply.Tag = post;
                reply.Click += ReplyPostButton_Click;
                panel.Children.Add(reply);
            }

            if (post.CanMinus)
            {
                Button bad = CreatePostActionButton("Плохо");
                bad.Tag = post;
                bad.Click += BadPostButton_Click;
                panel.Children.Add(bad);
            }

            if (post.CanPlus)
            {
                Button good = CreatePostActionButton("Хорошо");
                good.Tag = post;
                good.Click += GoodPostButton_Click;
                panel.Children.Add(good);
            }

            return panel.Children.Count > 0 ? panel : null;
        }

        private Button CreatePostActionButton(string text)
        {
            Button button = new Button();
            button.Content = text;
            button.MinHeight = 34;
            button.Padding = new Thickness(12, 4, 12, 4);
            button.Margin = new Thickness(6, 0, 0, 0);
            return button;
        }

        private async void GoodPostButton_Click(object sender, RoutedEventArgs e)
        {
            await VotePostAsync(sender as Button, true);
        }

        private async void BadPostButton_Click(object sender, RoutedEventArgs e)
        {
            await VotePostAsync(sender as Button, false);
        }

        private async System.Threading.Tasks.Task VotePostAsync(Button button, bool positive)
        {
            TopicPostItem post = button != null ? button.Tag as TopicPostItem : null;
            if (!ForumAuthService.IsAuthorized || post == null || post.Id <= 0)
                return;

            button.IsEnabled = false;
            try
            {
                string message = await _forumService.VotePostAsync(post.Id, positive);
                await ShowPostMessageAsync(message);
            }
            catch (Exception ex)
            {
                button.IsEnabled = true;
                await ShowPostMessageAsync("Не удалось проголосовать: " + ex.Message);
            }
        }

        private void ReplyPostButton_Click(object sender, RoutedEventArgs e)
        {
            Button button = sender as Button;
            TopicPostItem post = button != null ? button.Tag as TopicPostItem : null;
            if (!ForumAuthService.IsAuthorized || !_canReply || post == null)
                return;

            AppNavigationActions.OpenMessageForPost(post);
        }

        private void PostMenuButton_Click(object sender, RoutedEventArgs e)
        {
            Button button = sender as Button;
            TopicPostItem post = button != null ? button.Tag as TopicPostItem : null;
            if (!ForumAuthService.IsAuthorized || button == null || post == null)
                return;

            MenuFlyout flyout = new MenuFlyout();

            MenuFlyoutItem copyLink = new MenuFlyoutItem();
            copyLink.Text = "Скопировать ссылку";
            copyLink.Tag = post;
            copyLink.Click += CopyPostLink_Click;
            flyout.Items.Add(copyLink);

            if (_canReply && post.Id > 0)
            {
                MenuFlyoutItem reply = new MenuFlyoutItem();
                reply.Text = "Ответить";
                reply.Tag = post;
                reply.Click += ReplyPostMenu_Click;
                flyout.Items.Add(reply);
            }

            if (post.CanReport || !IsOwnPost(post))
            {
                MenuFlyoutItem report = new MenuFlyoutItem();
                report.Text = "Пожаловаться";
                report.Tag = post;
                report.Click += ReportPostMenu_Click;
                flyout.Items.Add(report);
            }

            if (post.CanEdit && post.Id > 0)
            {
                MenuFlyoutItem edit = new MenuFlyoutItem();
                edit.Text = "Редактировать";
                edit.Tag = post;
                edit.Click += EditPostMenu_Click;
                flyout.Items.Add(edit);
            }

            if (post.CanDelete && post.Id > 0)
            {
                MenuFlyoutItem delete = new MenuFlyoutItem();
                delete.Text = "Удалить";
                delete.Tag = post;
                delete.Click += DeletePostMenu_Click;
                flyout.Items.Add(delete);
            }

            flyout.ShowAt(button);
        }

        private async void CopyPostLink_Click(object sender, RoutedEventArgs e)
        {
            MenuFlyoutItem item = sender as MenuFlyoutItem;
            TopicPostItem post = item != null ? item.Tag as TopicPostItem : null;
            if (post == null)
                return;

            DataPackage package = new DataPackage();
            package.SetText("https://4pda.to/forum/index.php?act=findpost&pid=" + post.Id);
            Clipboard.SetContent(package);
            await ShowPostMessageAsync("Ссылка на сообщение скопирована");
        }

        private void ReplyPostMenu_Click(object sender, RoutedEventArgs e)
        {
            MenuFlyoutItem item = sender as MenuFlyoutItem;
            TopicPostItem post = item != null ? item.Tag as TopicPostItem : null;
            if (post == null || !_canReply)
                return;

            AppNavigationActions.OpenMessageForPost(post);
        }

        private async void ReportPostMenu_Click(object sender, RoutedEventArgs e)
        {
            MenuFlyoutItem item = sender as MenuFlyoutItem;
            TopicPostItem post = item != null ? item.Tag as TopicPostItem : null;
            if (post == null)
                return;

            TextBox input = new TextBox();
            input.AcceptsReturn = true;
            input.TextWrapping = TextWrapping.Wrap;
            input.MinHeight = 100;
            input.PlaceholderText = "Причина жалобы";

            ContentDialog dialog = new ContentDialog();
            dialog.Title = "Жалоба на сообщение";
            dialog.Content = input;
            dialog.PrimaryButtonText = "Отправить";
            dialog.SecondaryButtonText = "Отмена";
            ContentDialogResult result = await dialog.ShowAsync();
            if (result != ContentDialogResult.Primary || string.IsNullOrWhiteSpace(input.Text))
                return;

            try
            {
                await _forumService.ReportPostAsync(post.TopicId, post.Id, input.Text.Trim());
                await ShowPostMessageAsync("Жалоба отправлена");
            }
            catch (Exception ex)
            {
                await ShowPostMessageAsync("Не удалось отправить жалобу: " + ex.Message);
            }
        }

        private void EditPostMenu_Click(object sender, RoutedEventArgs e)
        {
            MenuFlyoutItem item = sender as MenuFlyoutItem;
            TopicPostItem post = item != null ? item.Tag as TopicPostItem : null;
            AppNavigationActions.OpenEditMessage(post);
        }

        private async void DeletePostMenu_Click(object sender, RoutedEventArgs e)
        {
            MenuFlyoutItem item = sender as MenuFlyoutItem;
            TopicPostItem post = item != null ? item.Tag as TopicPostItem : null;
            if (post == null || post.Id <= 0)
                return;

            ContentDialog confirm = new ContentDialog();
            confirm.Title = "Удалить сообщение?";
            confirm.Content = "Это действие нельзя отменить.";
            confirm.PrimaryButtonText = "Удалить";
            confirm.SecondaryButtonText = "Отмена";
            if (await confirm.ShowAsync() != ContentDialogResult.Primary)
                return;

            try
            {
                await OfficialApiService.DeletePostAsync(post.Id);
                FrameworkElement element;
                if (_postElements.TryGetValue(post.Id, out element)) ReleasePostVisual(element);
                _postItems.Remove(post);
                _postsById.Remove(post.Id);
                _loadedPosts.Remove(post.Id);
                await ShowPostMessageAsync("Сообщение удалено");
            }
            catch (Exception ex)
            {
                await ShowPostMessageAsync("Не удалось удалить сообщение: " + ex.Message);
            }
        }

        private async System.Threading.Tasks.Task ShowPostMessageAsync(string message)
        {
            ContentDialog dialog = new ContentDialog();
            dialog.Content = message ?? string.Empty;
            dialog.PrimaryButtonText = "ОК";
            await dialog.ShowAsync();
        }

        private int GetPostInsertIndex(TopicPostItem post)
        {
            if (post == null || post.Number <= 0)
            {
                return _postItems.Count;
            }

            for (int i = 0; i < _postItems.Count; i++)
            {
                TopicPostItem existingPost = _postItems[i];
                if (existingPost == null || existingPost.Number <= 0)
                {
                    continue;
                }

                if (post.Number < existingPost.Number
                    || (post.Number == existingPost.Number && post.Id > 0 && post.Id < existingPost.Id))
                {
                    return i;
                }
            }

            return _postItems.Count;
        }

        private void OpenUserProfile(int userId)
        {
            if (userId <= 0 || Frame == null)
                return;

            Frame.Navigate(typeof(UserPage), "https://4pda.to/forum/index.php?showuser=" + userId.ToString());
        }

        private FrameworkElement CreateAvatar(TopicPostItem post)
        {
            Border holder = new Border();
            holder.Width = 44;
            holder.Height = 44;
            holder.Background = ResolveBrush("PdaPageBackgroundBrush");

            Uri uri = NormalizeHref(post.AvatarUrl);
            if (uri != null)
            {
                Image image = new Image();
                DeferredImage.Attach(image, uri.AbsoluteUri, 88, 0);
                image.Stretch = Stretch.UniformToFill;
                holder.Child = image;
            }
            else
            {
                TextBlock glyph = new TextBlock();
                glyph.Text = "\uE77B";
                glyph.FontFamily = new FontFamily("Segoe MDL2 Assets");
                glyph.FontSize = 20;
                glyph.HorizontalAlignment = HorizontalAlignment.Center;
                glyph.VerticalAlignment = VerticalAlignment.Center;
                glyph.Foreground = ResolveBrush("PdaSecondaryTextBrush");
                holder.Child = glyph;
            }

            return holder;
        }

        private string BuildUserMeta(TopicPostItem post)
        {
            string result = string.Empty;
            if (!string.IsNullOrWhiteSpace(post.UserInfo))
            {
                result = post.UserInfo;
            }

            if (!string.IsNullOrWhiteSpace(post.ReputationText))
            {
                if (!string.IsNullOrWhiteSpace(result))
                {
                    result += "   ";
                }

                result += post.ReputationText;
            }

            return result;
        }

        private void RenderPostBody(StackPanel panel, string html, int postId = 0, int depth = 0)
        {
            if (panel == null)
            {
                return;
            }

            if (depth > 48) { AddRichText(panel, WebUtility.HtmlEncode(CleanInlineText(html)), false); return; }
            string source = StripUnsafeHtml(html);
            if (string.IsNullOrWhiteSpace(source))
            {
                AddRichText(panel, "\u041F\u0443\u0441\u0442\u043E\u0439 \u043F\u043E\u0441\u0442.", false);
                return;
            }

            int position = 0;
            foreach (Match blockMatch in Regex.Matches(source, "<div[^>]*?class=[\"'][^\"']*post-block[^\"']*[\"'][^>]*?>", RegexOptions.IgnoreCase))
            {
                if (blockMatch.Index < position)
                {
                    continue;
                }

                if (blockMatch.Index > position)
                {
                    AddContentSegment(panel, source.Substring(position, blockMatch.Index - position));
                }

                string blockHtml = ExtractBalancedDiv(source, blockMatch.Index);
                if (string.IsNullOrWhiteSpace(blockHtml))
                {
                    position = blockMatch.Index + blockMatch.Length;
                    continue;
                }

                AddPostBlock(panel, blockHtml, postId, depth);
                position = blockMatch.Index + blockHtml.Length;
            }

            if (position < source.Length)
            {
                AddContentSegment(panel, source.Substring(position));
            }
        }

        private void AddContentSegment(StackPanel panel, string source)
        {
            source = PrepareInlineHtml(source);
            if (string.IsNullOrWhiteSpace(source))
            {
                return;
            }

            // Normal photos are rendered in a wrapping gallery, because large
            // InlineUIContainer elements do not wrap reliably in RichTextBlock on
            // the VS2015/UWP 14393 target. Smiles and file icons stay in RichTextBlock
            // so the file name can remain immediately to the right of the icon.
            Regex mediaRegex = new Regex(
                @"<a\b[^>]*>(?:(?!</a>)[\s\S])*?<img\b[^>]*>(?:(?!</a>)[\s\S])*?</a>|<img\b[^>]*>",
                RegexOptions.IgnoreCase);

            int textStart = 0;
            PhotoWrapPanel gallery = null;

            foreach (Match match in mediaRegex.Matches(source))
            {
                string imageTag = FirstMatch(match.Value, @"(<img\b[^>]*>)", 1);
                Uri thumbnailUri = ExtractImageUri(imageTag);
                string mediaAnchorTag = FirstMatch(match.Value, @"(<a\b[^>]*>)", 1);
                Uri mediaNavigateUri = NormalizeHref(ExtractHref(mediaAnchorTag));
                bool isFileIcon = IsForumFileIcon(imageTag, thumbnailUri, mediaNavigateUri);
                Uri imageUri = isFileIcon ? thumbnailUri : SelectBestPostImageUri(thumbnailUri, mediaNavigateUri);
                string smileFile;
                bool isSmile = TryGetSmileFile(imageTag, out smileFile);
                bool isPhoto = !isFileIcon
                    && !isSmile
                    && imageUri != null
                    && ShouldRenderPostImage(imageTag, imageUri);

                if (!isPhoto)
                {
                    continue;
                }

                string between = source.Substring(textStart, match.Index - textStart);
                if (gallery == null)
                {
                    AddRichText(panel, between, false);
                    gallery = CreatePhotoGallery();
                }
                else if (!IsOnlyLayoutWhitespace(between))
                {
                    panel.Children.Add(gallery);
                    gallery = null;
                    AddRichText(panel, between, false);
                    gallery = CreatePhotoGallery();
                }

                Image image = CreateGalleryPostImage(imageTag, imageUri);
                if (image != null)
                {
                    ApplyMediaNavigation(image, match.Value);
                    gallery.Children.Add(image);
                }

                string caption = GetMediaCaption(match.Value);
                if (!string.IsNullOrWhiteSpace(caption))
                {
                    panel.Children.Add(gallery);
                    gallery = null;
                    AddRichText(panel, WebUtility.HtmlEncode(caption), false);
                }

                textStart = match.Index + match.Length;
            }

            if (gallery != null)
            {
                panel.Children.Add(gallery);
            }

            if (textStart < source.Length)
            {
                AddRichText(panel, source.Substring(textStart), false);
            }
        }

        private PhotoWrapPanel CreatePhotoGallery()
        {
            PhotoWrapPanel gallery = new PhotoWrapPanel();
            gallery.HorizontalAlignment = HorizontalAlignment.Stretch;
            gallery.HorizontalSpacing = 6;
            gallery.VerticalSpacing = 6;
            gallery.Margin = new Thickness(0, 4, 0, 8);
            return gallery;
        }

        private string GetMediaCaption(string mediaHtml)
        {
            if (string.IsNullOrWhiteSpace(mediaHtml))
            {
                return string.Empty;
            }

            string withoutImages = Regex.Replace(mediaHtml, @"<img\b[^>]*>", string.Empty, RegexOptions.IgnoreCase);
            return CleanInlineText(withoutImages);
        }

        private bool IsOnlyLayoutWhitespace(string html)
        {
            if (string.IsNullOrWhiteSpace(html))
            {
                return true;
            }

            string text = Regex.Replace(html, "<[^>]+>", string.Empty, RegexOptions.IgnoreCase);
            text = WebUtility.HtmlDecode(text).Replace("\u00A0", " ");
            return string.IsNullOrWhiteSpace(text);
        }

        private Image CreateGalleryPostImage(string html, Uri uri)
        {
            if (uri == null)
            {
                return null;
            }

            // Keep the page-provided preview source and let layout cap it by the
            // post width. The linked full attachment is opened only after a tap.
            Image image = new Image();
            DeferredImage.Attach(image, uri.AbsoluteUri, 640, 0);
            image.Stretch = Stretch.Uniform;
            image.HorizontalAlignment = HorizontalAlignment.Left;
            image.VerticalAlignment = VerticalAlignment.Top;
            RegisterPostImage(image, html, null);
            image.ImageOpened += GalleryImage_ImageOpened;
            _images.Add(image);
            _photoTargets[image] = uri;
            image.Tapped += PostPhoto_Tapped;
            return image;
        }

        private async void PostPhoto_Tapped(object sender, Windows.UI.Xaml.Input.TappedRoutedEventArgs e)
        {
            Image selected = sender as Image;
            if (selected == null) return;
            e.Handled = true;
            int postId = ParentPostId(selected);
            var photos = new List<Image>();
            foreach (Image image in _images)
                if (_photoTargets.ContainsKey(image) && ParentPostId(image) == postId) photos.Add(image);
            await AppPhotoViewer.ShowImagesAsync(selected, photos, _topicTitle, image => _photoTargets[image].AbsoluteUri);
        }

        private static int ParentPostId(DependencyObject element)
        {
            while (element != null)
            {
                var view = element as FrameworkElement;
                TopicPostItem post = view == null ? null : view.Tag as TopicPostItem;
                if (post != null) return post.Id;
                element = VisualTreeHelper.GetParent(element);
            }
            return 0;
        }

        private void GalleryImage_ImageOpened(object sender, RoutedEventArgs e)
        {
            Image image = sender as Image;
            UpdateImageWidth(image, GetPostImageOwner(image));
        }

        private Uri SelectBestPostImageUri(Uri thumbnailUri, Uri navigateUri)
        {
            // Render exactly the image version embedded in the topic HTML. The link
            // around it can point to a full-size attachment and is used only when the
            // user taps the picture; loading it here downloads every original file.
            return thumbnailUri;
        }

        private bool IsDirectImageUri(Uri uri)
        {
            if (uri == null)
                return false;

            string value = WebUtility.HtmlDecode(uri.AbsoluteUri ?? string.Empty).ToLowerInvariant();
            return Regex.IsMatch(value, @"\.(?:jpe?g|png|gif|webp|bmp)(?:[?#]|$)", RegexOptions.IgnoreCase);
        }

        private bool IsViewerPhotoLink(Uri uri)
        {
            return uri != null && (IsDirectImageUri(uri)
                || (IsForumDownloadLink(uri) && string.IsNullOrEmpty(Path.GetExtension(uri.AbsolutePath))));
        }

        private void ApplyMediaNavigation(Image image, string mediaHtml)
        {
            if (image == null || string.IsNullOrWhiteSpace(mediaHtml))
            {
                return;
            }

            string anchorTag = FirstMatch(mediaHtml, @"(<a\b[^>]*>)", 1);
            if (string.IsNullOrWhiteSpace(anchorTag))
            {
                return;
            }

            string href = ExtractHref(anchorTag);
            Uri uri = NormalizeHref(href);
            int targetPostId = ExtractPostIdFromAnchor(anchorTag, href, uri);
            if (targetPostId > 0)
            {
                image.Tapped -= PostPhoto_Tapped;
                int postId = targetPostId;
                Uri postUri = uri ?? BuildFindPostUri(postId);
                image.Tapped += async (s, e) => await ScrollToPostAsync(postId, postUri);
            }
            else if (IsViewerPhotoLink(uri))
            {
                _photoTargets[image] = uri;
            }
            else if (uri != null)
            {
                image.Tapped -= PostPhoto_Tapped;
                Uri destination = uri;
                if (IsForumDownloadLink(uri))
                    image.Tapped += async (s, e) => await DownloadForumFileAsync(destination, GetSuggestedDownloadFileName(destination, string.Empty));
                else image.Tapped += async (s, e) => await Windows.System.Launcher.LaunchUriAsync(destination);
            }
        }

        private void AddPostBlock(StackPanel panel, string html, int postId, int depth)
        {
            string title = FirstMatch(html, "<div[^>]*?class=[\"'][^\"']*block-title[^\"']*[\"'][^>]*?>([\\s\\S]*?)</div>", 1);
            string body = ExtractDivInnerByClass(html, "block-body");
            string openingTag = FirstMatch(html, @"^(<div\b[^>]*>)", 1);
            bool isSpoiler = Regex.IsMatch(openingTag, @"\b(?:spoil|hide)\b", RegexOptions.IgnoreCase);
            bool isCode = Regex.IsMatch(openingTag, @"\bcode\b", RegexOptions.IgnoreCase);

            Border border = new Border();
            border.Background = ResolveBrush("PdaPageBackgroundBrush");
            border.BorderBrush = ResolveBrush("PdaSelectedBrush");
            border.BorderThickness = new Thickness(1);
            border.Margin = new Thickness(0, 6, 0, 9);

            Grid grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition() { Width = new GridLength(4) });
            grid.ColumnDefinitions.Add(new ColumnDefinition() { Width = new GridLength(1, GridUnitType.Star) });
            border.Child = grid;

            Border accent = new Border();
            accent.Background = ResolveBrush("SystemControlHighlightAccentBrush");
            Grid.SetColumn(accent, 0);
            grid.Children.Add(accent);

            StackPanel content = new StackPanel();
            content.Padding = new Thickness(9, 8, 9, 9);
            Grid.SetColumn(content, 1);
            grid.Children.Add(content);

            StackPanel bodyPanel = new StackPanel();
            bool bodyRendered = false;
            Action renderBody = () =>
            {
                if (bodyRendered) return;
                bodyRendered = true;
                RenderPostBody(bodyPanel, body, postId, depth + 1);
            };
            if (isSpoiler)
            {
                // A plain Border is used instead of Button so the spoiler has no
                // pressed/scale animation while keeping the whole header clickable.
                Border header = new Border();
                header.HorizontalAlignment = HorizontalAlignment.Stretch;
                header.MinHeight = 32;
                header.Padding = new Thickness(0);
                header.Background = new SolidColorBrush(Colors.Transparent);
                FontIcon arrow = SetSpoilerHeader(header, title);
                int number = ParseAttributeInt(openingTag, "data-spoiler");
                List<SpoilerBlock> blocks;
                if (!_spoilers.TryGetValue(postId, out blocks)) { blocks = new List<SpoilerBlock>(); _spoilers[postId] = blocks; }
                if (number <= 0) number = blocks.Count + 1;
                string key = postId + ":" + number;
                int lastNumber = number;
                foreach (Match nested in Regex.Matches(html, @"\bdata-spoiler=""(\d+)"""))
                    lastNumber = Math.Max(lastNumber, ParseInt(nested.Groups[1].Value));
                var block = new SpoilerBlock { Number = number, LastNumber = lastNumber, Header = header, Body = bodyPanel };
                block.SetOpen = open =>
                {
                    if (open) renderBody();
                    bodyPanel.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
                    arrow.Glyph = open ? "\uE70E" : "\uE70D";
                    _spoilerOpen[key] = open;
                };
                blocks.Add(block);
                header.Tapped += (s, e) =>
                {
                    e.Handled = true;
                    block.SetOpen(bodyPanel.Visibility != Visibility.Visible);
                };
                content.Children.Add(header);
                bool wasOpen;
                block.SetOpen(_spoilerOpen.TryGetValue(key, out wasOpen) && wasOpen);
            }
            else if (!string.IsNullOrWhiteSpace(CleanInlineText(title)))
            {
                AddRichText(content, title, true);
            }

            if (isCode)
            {
                TextBlock code = new TextBlock();
                code.Text = CleanInlineText(body);
                code.FontFamily = new FontFamily("Consolas");
                code.FontSize = 14;
                code.TextWrapping = TextWrapping.Wrap;
                code.Foreground = ResolveBrush("PdaTextBrush");
                code.Margin = new Thickness(0, 4, 0, 0);
                bodyPanel.Children.Add(code);
            }
            else if (!isSpoiler)
            {
                renderBody();
            }

            content.Children.Add(bodyPanel);
            panel.Children.Add(border);
        }

        private FontIcon SetSpoilerHeader(Border header, string title)
        {
            Grid headerGrid = new Grid();
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition() { Width = new GridLength(1, GridUnitType.Star) });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition() { Width = GridLength.Auto });

            TextBlock headerText = new TextBlock();
            headerText.Text = string.IsNullOrWhiteSpace(CleanInlineText(title)) ? "Спойлер" : CleanInlineText(title);
            headerText.TextWrapping = TextWrapping.Wrap;
            headerText.VerticalAlignment = VerticalAlignment.Center;
            headerText.Foreground = ResolveBrush("PdaTextBrush");
            Grid.SetColumn(headerText, 0);
            headerGrid.Children.Add(headerText);

            FontIcon arrow = new FontIcon();
            arrow.Glyph = "\uE70D";
            arrow.FontSize = 12;
            arrow.Margin = new Thickness(12, 0, 0, 0);
            arrow.VerticalAlignment = VerticalAlignment.Center;
            arrow.Foreground = ResolveBrush("PdaTextBrush");
            Grid.SetColumn(arrow, 1);
            headerGrid.Children.Add(arrow);

            header.Child = headerGrid;
            return arrow;
        }

        private string StripUnsafeHtml(string html)
        {
            if (string.IsNullOrWhiteSpace(html))
            {
                return string.Empty;
            }

            string result = html;
            result = Regex.Replace(result, "<script[\\s\\S]*?</script>", string.Empty, RegexOptions.IgnoreCase);
            result = Regex.Replace(result, "<style[\\s\\S]*?</style>", string.Empty, RegexOptions.IgnoreCase);
            result = Regex.Replace(result, "<!--[\\s\\S]*?-->", string.Empty, RegexOptions.IgnoreCase);
            return result;
        }

        private string PrepareInlineHtml(string html)
        {
            if (string.IsNullOrWhiteSpace(html))
            {
                return string.Empty;
            }

            string result = StripUnsafeHtml(html);

            // New lines between HTML tags belong to the page source. Real post line
            // breaks are inserted below from br, block elements and list items.
            result = Regex.Replace(result, "[\\r\\n\\t]+", " ");
            result = Regex.Replace(result, "<div[^>]*?class=[\"'][^\"']*(?:hidetop|quotetop|post-block-title)[^\"']*[\"'][^>]*?>([\\s\\S]*?)</div>", "\n[$1]\n", RegexOptions.IgnoreCase);
            result = Regex.Replace(result, "<li[^>]*>", "\n- ", RegexOptions.IgnoreCase);
            result = Regex.Replace(result, "</li>", "\n", RegexOptions.IgnoreCase);
            result = Regex.Replace(result, "<br\\s*/?>|<hr\\s*/?>", "\n", RegexOptions.IgnoreCase);
            result = Regex.Replace(result, "</p>|</div>|</ul>|</ol>|</table>|</tr>|</blockquote>", "\n", RegexOptions.IgnoreCase);
            result = Regex.Replace(result, "</td>|</th>", "  ", RegexOptions.IgnoreCase);
            return result;
        }

        private void AddRichText(StackPanel panel, string html, bool note)
        {
            string probe = CleanInlineText(html);
            string smileFile;
            bool hasImage = Regex.IsMatch(html ?? string.Empty, @"<img\b", RegexOptions.IgnoreCase);
            if (string.IsNullOrWhiteSpace(probe) && !hasImage && !TryGetSmileFile(html, out smileFile))
            {
                return;
            }

            RichTextBlock rich = new RichTextBlock();
            rich.TextWrapping = TextWrapping.Wrap;
            rich.FontSize = note ? 13 : 16;
            rich.LineHeight = note ? 20 : 24;
            rich.Margin = new Thickness(0, 3, 0, 7);
            rich.Foreground = ResolveBrush(note ? "PdaSecondaryTextBrush" : "PdaTextBrush");

            Paragraph paragraph = new Paragraph();
            InlineStyle inlineStyle = new InlineStyle();
            inlineStyle.InlineImageSize = note ? 13 : 16;
            AddInlines(paragraph, html, inlineStyle);
            rich.Blocks.Add(paragraph);
            panel.Children.Add(rich);
        }

        private void AddInlines(Paragraph paragraph, string html, InlineStyle style)
        {
            if (paragraph == null || string.IsNullOrEmpty(html))
            {
                return;
            }

            AddInlineContent(paragraph, null, html, style ?? new InlineStyle(), false);
        }

        private void AddInlinesToSpan(Span span, string html, InlineStyle style, bool insideHyperlink = false)
        {
            if (span == null || string.IsNullOrEmpty(html))
            {
                return;
            }

            AddInlineContent(null, span, html, style ?? new InlineStyle(), insideHyperlink);
        }

        private void AddInlineContent(Paragraph paragraph, Span span, string html, InlineStyle style, bool insideHyperlink)
        {
            if (string.IsNullOrEmpty(html))
            {
                return;
            }

            int position = 0;
            while (position < html.Length)
            {
                int tagStart = html.IndexOf('<', position);
                if (tagStart < 0)
                {
                    AddTextInlines(paragraph, span, html.Substring(position), style);
                    break;
                }

                if (tagStart > position)
                {
                    AddTextInlines(paragraph, span, html.Substring(position, tagStart - position), style);
                }

                int tagEnd = FindHtmlTagEnd(html, tagStart);
                if (tagEnd < 0)
                {
                    AddTextInlines(paragraph, span, html.Substring(tagStart), style);
                    break;
                }

                string tagHtml = html.Substring(tagStart, tagEnd - tagStart + 1);
                string tag = GetHtmlTagName(tagHtml);
                bool closing = Regex.IsMatch(tagHtml, @"^<\s*/", RegexOptions.IgnoreCase);

                if (string.IsNullOrWhiteSpace(tag) || closing)
                {
                    position = tagEnd + 1;
                    continue;
                }

                if (tag == "br" || tag == "hr")
                {
                    AddInlineElement(paragraph, span, new LineBreak());
                    position = tagEnd + 1;
                    continue;
                }

                if (tag == "img")
                {
                    Uri inlineImageUri = ExtractImageUri(tagHtml);
                    bool isFileIcon = IsForumFileIcon(tagHtml, inlineImageUri);
                    string smileFile;

                    // The 4PDA attachment icon commonly has alt=":download:".
                    // It must be handled as the original remote file icon before
                    // smile detection, otherwise it is silently replaced with the
                    // local Assets/smiles/download.gif resource.
                    if (!insideHyperlink && isFileIcon && AddImageInline(paragraph, span, tagHtml, style))
                    {
                        // Rendered as the original 4PDA attachment icon.
                    }
                    else if (!insideHyperlink && TryGetSmileFile(tagHtml, out smileFile))
                    {
                        AddSmileInline(paragraph, span, smileFile, style);
                    }
                    else if (!insideHyperlink && AddImageInline(paragraph, span, tagHtml, style))
                    {
                        // Rendered as an inline image.
                    }
                    else
                    {
                        // InlineUIContainer cannot safely be nested in Hyperlink on
                        // the target UWP version. Image anchors are handled separately.
                        string alt = CleanInlineText(FirstMatch(tagHtml, "alt=[\"']([^\"']*)[\"']", 1));
                        if (!string.IsNullOrWhiteSpace(alt))
                        {
                            AddTextInlines(paragraph, span, alt, style);
                        }
                    }

                    position = tagEnd + 1;
                    continue;
                }

                if (!IsInlineContainerTag(tag))
                {
                    // Skip the tag itself. Its text and nested supported tags are parsed next.
                    position = tagEnd + 1;
                    continue;
                }

                int closeStart;
                int closeEnd;
                if (!FindMatchingClosingTag(html, tag, tagEnd + 1, out closeStart, out closeEnd))
                {
                    // Old forum posts can contain malformed HTML. Do not lose the rest.
                    position = tagEnd + 1;
                    continue;
                }

                string inner = html.Substring(tagEnd + 1, closeStart - tagEnd - 1);
                InlineStyle childStyle = style.Clone();
                ApplyTagStyle(childStyle, tag, tagHtml);

                if (insideHyperlink)
                {
                    childStyle.Foreground = ResolveBrush("SystemControlHighlightAccentBrush");
                }

                if (tag == "a")
                {
                    AddAnchorInline(paragraph, span, tagHtml, inner, childStyle, insideHyperlink);
                }
                else
                {
                    AddInlineContent(paragraph, span, inner, childStyle, insideHyperlink);
                }

                position = closeEnd + 1;
            }
        }

        private void AddAnchorInline(Paragraph paragraph, Span span, string tagHtml, string inner, InlineStyle style, bool insideHyperlink)
        {
            if (insideHyperlink)
            {
                AddInlineContent(paragraph, span, inner, style, true);
                return;
            }

            string href = ExtractHref(tagHtml);
            Uri uri = NormalizeHref(href);
            int targetPostId = ExtractPostIdFromAnchor(tagHtml, href, uri);
            string text = CleanInlineText(inner);

            if (targetPostId > 0 && uri == null)
            {
                uri = BuildFindPostUri(targetPostId);
            }

            if (targetPostId <= 0 && uri == null)
            {
                AddInlineContent(paragraph, span, inner, style, false);
                return;
            }

            if (string.IsNullOrWhiteSpace(text))
            {
                text = targetPostId > 0 ? "пост" : href;
            }

            if (Regex.IsMatch(inner ?? string.Empty, @"<img\b", RegexOptions.IgnoreCase))
            {
                AddLinkedImageAnchorContent(paragraph, span, inner, style, uri, targetPostId);
                return;
            }

            if (string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            Brush linkBrush = ResolveBrush("SystemControlHighlightAccentBrush");
            Hyperlink hyperlink = new Hyperlink();
            hyperlink.Foreground = linkBrush;

            InlineStyle linkStyle = style.Clone();
            linkStyle.Foreground = linkBrush;
            AddInlinesToSpan(hyperlink, inner, linkStyle, true);

            if (hyperlink.Inlines.Count == 0)
            {
                Run linkRun = new Run();
                linkRun.Text = text;
                linkRun.Foreground = linkBrush;
                hyperlink.Inlines.Add(linkRun);
            }

            if (targetPostId > 0)
            {
                int postId = targetPostId;
                Uri postUri = uri;
                hyperlink.Click += async (s, e) => await ScrollToPostAsync(postId, postUri);
            }
            else if (IsForumDownloadLink(uri))
            {
                Uri downloadUri = uri;
                string suggestedName = GetSuggestedDownloadFileName(downloadUri, text);
                hyperlink.Click += async (s, e) => await DownloadForumFileAsync(downloadUri, suggestedName);
            }
            else
            {
                hyperlink.NavigateUri = uri;
            }

            AddInlineElement(paragraph, span, hyperlink);
        }

        private void AddLinkedImageAnchorContent(Paragraph paragraph, Span span, string inner, InlineStyle style, Uri navigateUri, int targetPostId)
        {
            if (string.IsNullOrEmpty(inner))
            {
                return;
            }

            int position = 0;
            MatchCollection images = Regex.Matches(inner, @"<img\b[^>]*>", RegexOptions.IgnoreCase);
            for (int i = 0; i < images.Count; i++)
            {
                Match imageMatch = images[i];
                if (imageMatch.Index > position)
                {
                    AddLinkedTextFragment(paragraph, span, inner.Substring(position, imageMatch.Index - position), style, navigateUri, targetPostId);
                }

                AddLinkedImageInline(paragraph, span, imageMatch.Value, style, navigateUri, targetPostId);
                position = imageMatch.Index + imageMatch.Length;
            }

            if (position < inner.Length)
            {
                AddLinkedTextFragment(paragraph, span, inner.Substring(position), style, navigateUri, targetPostId);
            }
        }

        private void AddLinkedTextFragment(Paragraph paragraph, Span span, string html, InlineStyle style, Uri navigateUri, int targetPostId)
        {
            string text = CleanInlineText(html);
            if (string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            Brush linkBrush = ResolveBrush("SystemControlHighlightAccentBrush");
            Hyperlink hyperlink = new Hyperlink();
            hyperlink.Foreground = linkBrush;

            Run run = new Run();
            run.Text = text;
            InlineStyle linkStyle = style != null ? style.Clone() : new InlineStyle();
            linkStyle.Foreground = linkBrush;
            ApplyRunStyle(run, linkStyle);
            hyperlink.Inlines.Add(run);

            if (targetPostId > 0)
            {
                int postId = targetPostId;
                Uri postUri = navigateUri;
                hyperlink.Click += async (s, e) => await ScrollToPostAsync(postId, postUri);
            }
            else if (IsForumDownloadLink(navigateUri))
            {
                Uri downloadUri = navigateUri;
                string suggestedName = GetSuggestedDownloadFileName(downloadUri, text);
                hyperlink.Click += async (s, e) => await DownloadForumFileAsync(downloadUri, suggestedName);
            }
            else if (navigateUri != null)
            {
                hyperlink.NavigateUri = navigateUri;
            }

            AddInlineElement(paragraph, span, hyperlink);
        }

        private void AddLinkedImageInline(Paragraph paragraph, Span span, string html, InlineStyle style, Uri navigateUri, int targetPostId)
        {
            Uri thumbnailUri = ExtractImageUri(html);
            bool isFileIcon = IsForumFileIcon(html, thumbnailUri, navigateUri);
            Uri imageUri = isFileIcon ? thumbnailUri : SelectBestPostImageUri(thumbnailUri, navigateUri);
            if (imageUri == null || (!isFileIcon && !ShouldRenderPostImage(html, imageUri)))
            {
                string alt = CleanInlineText(FirstMatch(html, "alt=[\"']([^\"']*)[\"']", 1));
                if (!string.IsNullOrWhiteSpace(alt))
                {
                    AddLinkedTextFragment(paragraph, span, alt, style, navigateUri, targetPostId);
                }
                return;
            }

            Image image = CreateInlinePostImage(html, imageUri, isFileIcon, style);
            if (image == null)
            {
                return;
            }

            FrameworkElement presenter = isFileIcon ? CreateFileIconPresenter(image) : (FrameworkElement)image;
            if (targetPostId > 0)
            {
                image.Tapped -= PostPhoto_Tapped;
                int postId = targetPostId;
                Uri postUri = navigateUri;
                presenter.Tapped += async (s, e) => await ScrollToPostAsync(postId, postUri);
            }
            else if (!isFileIcon && IsViewerPhotoLink(navigateUri))
            {
                _photoTargets[image] = navigateUri;
            }
            else if (navigateUri != null)
            {
                image.Tapped -= PostPhoto_Tapped;
                Uri destination = navigateUri;
                if (isFileIcon || IsForumDownloadLink(destination))
                {
                    string alt = CleanInlineText(FirstMatch(html, "(?:alt|title)=[\"']([^\"']*)[\"']", 1));
                    string suggestedName = GetSuggestedDownloadFileName(destination, alt);
                    presenter.Tapped += async (s, e) => await DownloadForumFileAsync(destination, suggestedName);
                }
                else
                {
                    presenter.Tapped += async (s, e) => await Windows.System.Launcher.LaunchUriAsync(destination);
                }
            }

            InlineUIContainer container = new InlineUIContainer();
            container.Child = presenter;
            AddInlineElement(paragraph, span, container);
        }

        private void AddTextInlines(Paragraph paragraph, Span span, string html, InlineStyle style)
        {
            if (string.IsNullOrEmpty(html))
            {
                return;
            }

            string text = Regex.Replace(html, "<[^>]+>", " ", RegexOptions.IgnoreCase);
            text = WebUtility.HtmlDecode(text);
            text = text.Replace("\r\n", "\n").Replace('\r', '\n');
            text = Regex.Replace(text, "[ \\t\\x0B\\f]+", " ");
            text = Regex.Replace(text, " *\\n *", "\n");
            text = Regex.Replace(text, "\\n{3,}", "\n\n");

            string[] lines = text.Split('\n');
            bool joinAfterFileIcon = EndsWithFileIcon(paragraph, span);
            int leadingEmptyLines = 0;
            while (leadingEmptyLines < lines.Length && string.IsNullOrWhiteSpace(lines[leadingEmptyLines]))
            {
                leadingEmptyLines++;
            }

            for (int i = 0; i < lines.Length; i++)
            {
                AddTextLineWithSmiles(paragraph, span, lines[i], style);

                if (i < lines.Length - 1)
                {
                    // 4PDA attachment markup often puts the 16 px icon and the file
                    // name into neighbouring block tags. Ignore only those artificial
                    // leading breaks so the name stays on the icon's right.
                    if (joinAfterFileIcon && i < leadingEmptyLines)
                    {
                        continue;
                    }

                    AddInlineElement(paragraph, span, new LineBreak());
                }
            }
        }

        private bool EndsWithFileIcon(Paragraph paragraph, Span span)
        {
            InlineCollection inlines = span != null ? span.Inlines : (paragraph != null ? paragraph.Inlines : null);
            if (inlines == null || inlines.Count == 0)
            {
                return false;
            }

            InlineUIContainer container = inlines[inlines.Count - 1] as InlineUIContainer;
            FrameworkElement child = container != null ? container.Child as FrameworkElement : null;
            return child != null && IsFileIconElement(child);
        }

        private void AddTextLineWithSmiles(Paragraph paragraph, Span span, string text, InlineStyle style)
        {
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            int position = 0;
            foreach (Match match in Regex.Matches(text, @":([A-Za-z0-9][A-Za-z0-9_-]*):"))
            {
                string smileFile;
                if (!SmileFiles.TryGetValue(match.Groups[1].Value, out smileFile))
                {
                    continue;
                }

                if (match.Index > position)
                {
                    AddStyledRun(paragraph, span, text.Substring(position, match.Index - position), style);
                }

                AddSmileInline(paragraph, span, smileFile, style);
                position = match.Index + match.Length;
            }

            if (position < text.Length)
            {
                AddStyledRun(paragraph, span, text.Substring(position), style);
            }
        }

        private void AddStyledRun(Paragraph paragraph, Span span, string text, InlineStyle style)
        {
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            Run run = new Run();
            run.Text = text;
            ApplyRunStyle(run, style);
            AddInlineElement(paragraph, span, run);
        }

        private void AddSmileInline(Paragraph paragraph, Span span, string smileFile, InlineStyle style)
        {
            if (string.IsNullOrWhiteSpace(smileFile))
            {
                return;
            }

            Uri uri;
            if (!Uri.TryCreate("ms-appx:///Assets/smiles/" + smileFile, UriKind.Absolute, out uri))
            {
                return;
            }

            double imageSize = style != null && style.InlineImageSize > 0
                ? style.InlineImageSize
                : 16;

            Image image = new Image();
            DeferredImage.Attach(image, uri.AbsoluteUri, 640, 0);
            image.Stretch = Stretch.Uniform;
            image.VerticalAlignment = VerticalAlignment.Center;

            if (string.Equals(smileFile, "download.gif", StringComparison.OrdinalIgnoreCase))
            {
                SetFileIconSize(image);
            }
            else
            {
                image.Margin = new Thickness(1, 0, 1, 0);
                image.Height = imageSize;
                image.MaxHeight = imageSize;
                image.MaxWidth = imageSize * 4;
            }

            InlineUIContainer container = new InlineUIContainer();
            container.Child = image;
            AddInlineElement(paragraph, span, container);
        }

        private bool AddImageInline(Paragraph paragraph, Span span, string html, InlineStyle style)
        {
            Uri uri = ExtractImageUri(html);
            bool isFileIcon = IsForumFileIcon(html, uri);
            if (uri == null || (!isFileIcon && !ShouldRenderPostImage(html, uri)))
            {
                return false;
            }

            Image image = CreateInlinePostImage(html, uri, isFileIcon, style);
            if (image == null)
            {
                return false;
            }

            InlineUIContainer container = new InlineUIContainer();
            container.Child = isFileIcon ? CreateFileIconPresenter(image) : (FrameworkElement)image;
            AddInlineElement(paragraph, span, container);
            return true;
        }

        private Image CreateInlinePostImage(string html, Uri uri, bool isFileIcon, InlineStyle style)
        {
            if (uri == null)
            {
                return null;
            }

            Image image = new Image();
            image.Stretch = Stretch.Uniform;
            image.VerticalAlignment = VerticalAlignment.Center;

            if (isFileIcon)
            {
                // Keep the original 4PDA network image. Do not replace attachment
                // icons with a local smile resource: the server icon is the source
                // of truth and is explicitly constrained to text-height below.
                DeferredImage.Attach(image, uri.AbsoluteUri, 640, 0);
                SetFileIconSize(image);
                return image;
            }

            DeferredImage.Attach(image, uri.AbsoluteUri, 640, 0);
            image.Stretch = Stretch.Uniform;
            image.HorizontalAlignment = HorizontalAlignment.Left;
            image.VerticalAlignment = VerticalAlignment.Top;
            image.Margin = new Thickness(2, 3, 2, 3);
            RegisterPostImage(image, html, null);
            image.ImageOpened += GalleryImage_ImageOpened;
            _images.Add(image);
            _photoTargets[image] = uri;
            image.Tapped += PostPhoto_Tapped;
            return image;
        }

        private bool TryGetSmileFile(string html, out string smileFile)
        {
            smileFile = null;
            if (string.IsNullOrWhiteSpace(html))
            {
                return false;
            }

            string alt = WebUtility.HtmlDecode(FirstMatch(html, "(?:alt|title)=[\"']([^\"']*)[\"']", 1));
            Match code = Regex.Match(alt ?? string.Empty, @"^\s*:([A-Za-z0-9][A-Za-z0-9_-]*):\s*$");
            if (code.Success && SmileFiles.TryGetValue(code.Groups[1].Value, out smileFile))
            {
                return true;
            }

            string source = WebUtility.HtmlDecode(FirstMatch(html, "(?:data-src|src)=[\"']([^\"']+)[\"']", 1));
            if (string.IsNullOrWhiteSpace(source))
            {
                return false;
            }

            source = source.Replace('\\', '/');
            int suffix = source.IndexOfAny(new char[] { '?', '#' });
            if (suffix >= 0)
            {
                source = source.Substring(0, suffix);
            }

            int slash = source.LastIndexOf('/');
            string file = slash >= 0 ? source.Substring(slash + 1) : source;
            file = Uri.UnescapeDataString(file);
            if (!file.EndsWith(".gif", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            string alias = file.Substring(0, file.Length - 4);
            return SmileFiles.TryGetValue(alias, out smileFile);
        }

        private void AddInlineElement(Paragraph paragraph, Span span, Inline inline)
        {
            if (inline == null)
            {
                return;
            }

            if (span != null)
            {
                span.Inlines.Add(inline);
            }
            else if (paragraph != null)
            {
                paragraph.Inlines.Add(inline);
            }
        }

        private bool IsInlineContainerTag(string tag)
        {
            return tag == "a"
                || tag == "b"
                || tag == "strong"
                || tag == "i"
                || tag == "em"
                || tag == "u"
                || tag == "s"
                || tag == "strike"
                || tag == "del"
                || tag == "sup"
                || tag == "sub"
                || tag == "font"
                || tag == "span"
                || tag == "small"
                || tag == "big"
                || tag == "tt"
                || tag == "code";
        }

        private int FindHtmlTagEnd(string html, int tagStart)
        {
            char quote = '\0';
            for (int i = tagStart + 1; i < html.Length; i++)
            {
                char current = html[i];
                if (quote != '\0')
                {
                    if (current == quote)
                    {
                        quote = '\0';
                    }
                }
                else if (current == '\'' || current == '"')
                {
                    quote = current;
                }
                else if (current == '>')
                {
                    return i;
                }
            }

            return -1;
        }

        private string GetHtmlTagName(string tagHtml)
        {
            Match match = Regex.Match(tagHtml ?? string.Empty, @"^<\s*/?\s*([a-zA-Z0-9]+)", RegexOptions.IgnoreCase);
            return match.Success ? match.Groups[1].Value.ToLowerInvariant() : string.Empty;
        }

        private bool FindMatchingClosingTag(string html, string tagName, int contentStart, out int closeStart, out int closeEnd)
        {
            closeStart = -1;
            closeEnd = -1;
            int depth = 1;
            int position = contentStart;

            while (position < html.Length)
            {
                int tagStart = html.IndexOf('<', position);
                if (tagStart < 0)
                {
                    return false;
                }

                int tagEnd = FindHtmlTagEnd(html, tagStart);
                if (tagEnd < 0)
                {
                    return false;
                }

                string tagHtml = html.Substring(tagStart, tagEnd - tagStart + 1);
                string foundName = GetHtmlTagName(tagHtml);
                if (string.Equals(foundName, tagName, StringComparison.OrdinalIgnoreCase))
                {
                    bool closing = Regex.IsMatch(tagHtml, @"^<\s*/", RegexOptions.IgnoreCase);
                    bool selfClosing = Regex.IsMatch(tagHtml, @"/\s*>$");
                    if (closing)
                    {
                        depth--;
                        if (depth == 0)
                        {
                            closeStart = tagStart;
                            closeEnd = tagEnd;
                            return true;
                        }
                    }
                    else if (!selfClosing)
                    {
                        depth++;
                    }
                }

                position = tagEnd + 1;
            }

            return false;
        }

        private string ExtractHref(string attributes)
        {
            if (string.IsNullOrWhiteSpace(attributes))
            {
                return string.Empty;
            }

            Match quoted = Regex.Match(attributes, @"\bhref\s*=\s*[""']([^""']+)[""']", RegexOptions.IgnoreCase);
            if (quoted.Success)
            {
                return quoted.Groups[1].Value;
            }

            Match unquoted = Regex.Match(attributes, @"\bhref\s*=\s*([^\s>]+)", RegexOptions.IgnoreCase);
            return unquoted.Success ? unquoted.Groups[1].Value.Trim('"', '\'') : string.Empty;
        }

        private int ExtractPostIdFromAnchor(string tagHtml, string href, Uri uri)
        {
            int id = ExtractInternalPostId(uri);
            if (id > 0)
            {
                return id;
            }

            if (uri != null && !ForumPostNavigation.IsForumUri(uri)) return 0;
            string source = WebUtility.HtmlDecode((tagHtml ?? string.Empty) + " " + (href ?? string.Empty));
            id = ExtractQueryInt(source, "p");
            if (id <= 0)
            {
                id = ExtractQueryInt(source, "pid");
            }

            if (id <= 0)
            {
                Match scriptPost = Regex.Match(source, @"(?:link_to_post|findpost)\s*\(\s*['""']?(\d+)", RegexOptions.IgnoreCase);
                if (scriptPost.Success)
                {
                    id = ParseInt(scriptPost.Groups[1].Value);
                }
            }

            if (id <= 0)
            {
                Match dataPost = Regex.Match(source, @"\bdata-(?:post|post-id|pid)\s*=\s*[""']?(\d+)", RegexOptions.IgnoreCase);
                if (dataPost.Success)
                {
                    id = ParseInt(dataPost.Groups[1].Value);
                }
            }

            if (id <= 0)
            {
                Match fragmentPost = Regex.Match(source, @"#(?:entry|post)(\d+)", RegexOptions.IgnoreCase);
                if (fragmentPost.Success)
                {
                    id = ParseInt(fragmentPost.Groups[1].Value);
                }
            }

            return id;
        }

        private Uri BuildFindPostUri(int postId)
        {
            if (postId <= 0)
            {
                return null;
            }

            Uri uri;
            string url = "https://4pda.to/forum/index.php?act=findpost&pid=" + postId.ToString();
            return Uri.TryCreate(url, UriKind.Absolute, out uri) ? uri : null;
        }

        private void AddImage(StackPanel panel, string html)
        {
            Uri uri = ExtractImageUri(html);
            bool isFileIcon = IsForumFileIcon(html, uri);
            if (uri == null || (!isFileIcon && !ShouldRenderPostImage(html, uri)))
            {
                return;
            }

            Image image = new Image();
            DeferredImage.Attach(image, uri.AbsoluteUri, 640, 0);
            image.Stretch = Stretch.Uniform;
            image.HorizontalAlignment = HorizontalAlignment.Left;

            if (isFileIcon)
            {
                panel.Children.Add(CreateFileIconPresenter(image));
                return;
            }

            image.Margin = new Thickness(0, 8, 0, 10);
            image.Stretch = Stretch.Uniform;
            RegisterPostImage(image, html, panel);
            image.ImageOpened += PostImage_ImageOpened;
            _images.Add(image);
            _photoTargets[image] = uri;
            image.Tapped += PostPhoto_Tapped;
            panel.Children.Add(image);
        }

        private bool IsForumFileIcon(string html, Uri uri, Uri navigateUri = null)
        {
            if (uri == null)
            {
                return false;
            }

            string absolute = WebUtility.HtmlDecode(uri.AbsoluteUri ?? string.Empty);
            if (absolute.IndexOf(FourPdaDownloadIconFileName, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            string fileName = uri.Segments.Length > 0
                ? Uri.UnescapeDataString(uri.Segments[uri.Segments.Length - 1].Trim('/'))
                : string.Empty;

            if (string.Equals(fileName, FourPdaDownloadIconFileName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // A /forum/dl/post/ link can point either to a file or to the original
            // photo. Never classify it as a 16 px icon by the link alone; inspect the
            // IMG metadata below so linked photo thumbnails remain normal images.
            string source = WebUtility.HtmlDecode(html ?? string.Empty);
            string lowerSource = source.ToLowerInvariant();
            string alt = WebUtility.HtmlDecode(FirstMatch(html, "(?:alt|title)=[\"']([^\"']*)[\"']", 1));
            string lowerAlt = (alt ?? string.Empty).ToLowerInvariant();

            if (Regex.IsMatch(alt ?? string.Empty, @"^\s*:download:\s*$", RegexOptions.IgnoreCase))
            {
                return true;
            }

            bool imageHint = lowerAlt.IndexOf("изображ", StringComparison.Ordinal) >= 0
                || lowerAlt.IndexOf("image", StringComparison.Ordinal) >= 0
                || lowerAlt.IndexOf("photo", StringComparison.Ordinal) >= 0
                || lowerSource.IndexOf("linked-image", StringComparison.Ordinal) >= 0;

            bool fileHint = lowerAlt.IndexOf("прикрепленный файл", StringComparison.Ordinal) >= 0
                || lowerAlt.IndexOf("прикреплённый файл", StringComparison.Ordinal) >= 0
                || lowerAlt.IndexOf("attached file", StringComparison.Ordinal) >= 0
                || lowerAlt.IndexOf("download", StringComparison.Ordinal) >= 0
                || lowerAlt.IndexOf("скач", StringComparison.Ordinal) >= 0
                || lowerSource.IndexOf("download", StringComparison.Ordinal) >= 0
                || lowerSource.IndexOf("attachment-icon", StringComparison.Ordinal) >= 0
                || lowerSource.IndexOf("file-icon", StringComparison.Ordinal) >= 0
                || lowerSource.IndexOf("mime", StringComparison.Ordinal) >= 0;

            int width = ParseAttributeInt(html, "width");
            int height = ParseAttributeInt(html, "height");
            bool smallIcon = (width > 0 && width <= 24) || (height > 0 && height <= 24);
            string lowerUri = absolute.ToLowerInvariant();
            bool iconSource = lowerUri.IndexOf("/style_images/", StringComparison.Ordinal) >= 0
                || lowerUri.IndexOf("/mime/", StringComparison.Ordinal) >= 0
                || (lowerUri.IndexOf("/s/", StringComparison.Ordinal) >= 0
                    && lowerUri.IndexOf(".gif", StringComparison.Ordinal) >= 0);

            return !imageHint && fileHint && (smallIcon || iconSource);
        }

        private bool IsForumDownloadUri(Uri uri)
        {
            if (uri == null)
            {
                return false;
            }

            string value = WebUtility.HtmlDecode(uri.AbsoluteUri ?? string.Empty).ToLowerInvariant();
            return value.IndexOf("/forum/dl/post/", StringComparison.Ordinal) >= 0
                || value.IndexOf("act=attach", StringComparison.Ordinal) >= 0
                || value.IndexOf("view=attach", StringComparison.Ordinal) >= 0
                || value.IndexOf("getattach", StringComparison.Ordinal) >= 0
                || value.IndexOf("attachment", StringComparison.Ordinal) >= 0;
        }

        private void SetFileIconSize(Image image)
        {
            if (image == null)
            {
                return;
            }

            const double iconSize = 16;
            image.Tag = FileIconTag;
            image.Width = iconSize;
            image.Height = iconSize;
            image.MinWidth = iconSize;
            image.MaxWidth = iconSize;
            image.MinHeight = iconSize;
            image.MaxHeight = iconSize;
            image.Margin = new Thickness(0);
            image.Stretch = Stretch.Uniform;
            image.HorizontalAlignment = HorizontalAlignment.Center;
            image.VerticalAlignment = VerticalAlignment.Center;
            image.UseLayoutRounding = true;
        }

        private FrameworkElement CreateFileIconPresenter(Image image)
        {
            if (image == null)
            {
                return null;
            }

            const double iconSize = 16;
            SetFileIconSize(image);

            Border presenter = new Border();
            presenter.Tag = FileIconTag;
            presenter.Width = iconSize;
            presenter.Height = iconSize;
            presenter.MinWidth = iconSize;
            presenter.MaxWidth = iconSize;
            presenter.MinHeight = iconSize;
            presenter.MaxHeight = iconSize;
            presenter.Margin = new Thickness(1, 0, 4, 0);
            presenter.HorizontalAlignment = HorizontalAlignment.Left;
            presenter.VerticalAlignment = VerticalAlignment.Center;
            presenter.UseLayoutRounding = true;
            presenter.Child = image;
            return presenter;
        }

        private bool IsFileIconElement(FrameworkElement element)
        {
            if (element == null)
            {
                return false;
            }

            if (string.Equals(element.Tag as string, FileIconTag, StringComparison.Ordinal))
            {
                return true;
            }

            Border border = element as Border;
            Image image = border != null ? border.Child as Image : element as Image;
            return image != null && string.Equals(image.Tag as string, FileIconTag, StringComparison.Ordinal);
        }

        private Uri ExtractImageUri(string html)
        {
            if (string.IsNullOrWhiteSpace(html))
            {
                return null;
            }

            // Use the same preview source that the forum page renders. Do not prefer
            // data-original, the largest srcset entry or an enclosing href: those
            // values normally point to the full attachment and cause unnecessary
            // high-quality downloads for every visible post image.
            string source = FirstMatch(html, "(?<![A-Za-z0-9_-])src=[\"']([^\"']+)[\"']", 1);
            if (IsEmptyImageSource(source))
            {
                source = FirstMatch(html, "data-src=[\"']([^\"']+)[\"']", 1);
            }

            if (IsEmptyImageSource(source))
            {
                source = FirstMatch(html, "data-lazy-src=[\"']([^\"']+)[\"']", 1);
            }

            if (IsEmptyImageSource(source))
            {
                string sourceSet = FirstMatch(html, "(?:data-srcset|srcset)=[\"']([^\"']+)[\"']", 1);
                source = ExtractSmallestSourceSetUri(sourceSet);
            }

            return IsEmptyImageSource(source) ? null : NormalizeHref(source);
        }

        private bool IsEmptyImageSource(string source)
        {
            if (string.IsNullOrWhiteSpace(source))
            {
                return true;
            }

            string value = WebUtility.HtmlDecode(source).Trim().ToLowerInvariant();
            return value.Length == 0
                || value == "#"
                || value.StartsWith("data:image/", StringComparison.Ordinal)
                || value.IndexOf("spacer.gif", StringComparison.Ordinal) >= 0
                || value.IndexOf("blank.gif", StringComparison.Ordinal) >= 0
                || value.IndexOf("transparent.gif", StringComparison.Ordinal) >= 0;
        }

        private string ExtractSmallestSourceSetUri(string sourceSet)
        {
            if (string.IsNullOrWhiteSpace(sourceSet))
            {
                return string.Empty;
            }

            string best = string.Empty;
            int bestWidth = int.MaxValue;
            string[] candidates = WebUtility.HtmlDecode(sourceSet).Split(',');
            for (int i = 0; i < candidates.Length; i++)
            {
                string candidate = candidates[i].Trim();
                if (candidate.Length == 0)
                {
                    continue;
                }

                string[] parts = Regex.Split(candidate, @"\s+");
                string value = parts.Length > 0 ? parts[0].Trim() : string.Empty;
                int width = 0;
                if (parts.Length > 1)
                {
                    Match widthMatch = Regex.Match(parts[parts.Length - 1], @"^(\d+)w$", RegexOptions.IgnoreCase);
                    if (widthMatch.Success)
                    {
                        int.TryParse(widthMatch.Groups[1].Value, out width);
                    }
                }

                if (string.IsNullOrWhiteSpace(value) || IsEmptyImageSource(value))
                {
                    continue;
                }

                // Source sets are normally ordered from the lightest preview to the
                // largest original. Prefer the smallest declared width; when widths
                // are absent, keep the first page-provided candidate.
                if (best.Length == 0 || (width > 0 && width < bestWidth))
                {
                    best = value;
                    bestWidth = width > 0 ? width : bestWidth;
                }
            }

            return best;
        }

        private bool ShouldRenderPostImage(string html, Uri uri)
        {
            if (uri == null)
            {
                return false;
            }

            string lowerHtml = html == null ? string.Empty : html.ToLowerInvariant();
            string source = uri.AbsoluteUri.ToLowerInvariant();
            string alt = CleanInlineText(FirstMatch(html, "alt=[\"']([^\"']*)[\"']", 1)).ToLowerInvariant();

            if (alt == "*" || lowerHtml.IndexOf("act=findpost", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return false;
            }

            if (ForumPostImagePolicy.IsSkinAsset(uri))
            {
                return false;
            }

            if (lowerHtml.IndexOf("linked-image", StringComparison.OrdinalIgnoreCase) >= 0
                || alt.IndexOf("изображ", StringComparison.OrdinalIgnoreCase) >= 0
                || source.IndexOf("/forum/dl/post/", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            // Small dimensions are not enough to reject an image: they can belong
            // to a legitimate page preview or a genuinely small user picture.
            return lowerHtml.IndexOf("<img", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private int ParseAttributeInt(string html, string attribute)
        {
            string value = FirstMatch(html, attribute + "=[\"']?(\\d+)", 1);
            int result;
            return int.TryParse(value, out result) ? result : 0;
        }

        private void PostImage_ImageOpened(object sender, RoutedEventArgs e)
        {
            Image image = sender as Image;
            UpdateImageWidth(image, GetPostImageOwner(image));
        }

        private void PostBody_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            StackPanel panel = sender as StackPanel;
            if (panel == null)
            {
                return;
            }

            for (int i = 0; i < panel.Children.Count; i++)
            {
                Image image = panel.Children[i] as Image;
                if (image != null)
                {
                    UpdateImageWidth(image, panel);
                }
            }
        }

        private void PostsListView_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            for (int i = 0; i < _images.Count; i++)
            {
                UpdateImageWidth(_images[i], null);
            }
        }

        private void RegisterPostImage(Image image, string html, FrameworkElement owner)
        {
            if (image == null)
            {
                return;
            }

            image.MinHeight = 100;
            image.Width = Math.Max(1, Math.Min(320, Window.Current.Bounds.Width - 48));
            image.RegisterPropertyChangedCallback(Image.SourceProperty, delegate
            {
                if (image.Source == null) return;
                image.MinHeight = 0;
                UpdateImageWidth(image, owner);
            });
            PostImageLayoutInfo info = new PostImageLayoutInfo();
            info.Owner = owner;
            info.DeclaredWidth = ParseImageCssPixels(html, "width");
            info.DeclaredHeight = ParseImageCssPixels(html, "height");
            info.WidthFraction = ParseImageCssFraction(html, "width");
            info.MaxWidthFraction = ParseImageCssFraction(html, "max-width");
            info.MaxWidthPixels = ParseImageCssPixels(html, "max-width");
            _imageLayouts[image] = info;
        }

        private FrameworkElement GetPostImageOwner(Image image)
        {
            PostImageLayoutInfo info;
            return image != null && _imageLayouts.TryGetValue(image, out info)
                ? info.Owner
                : null;
        }

        private double ParseImageCssPixels(string html, string property)
        {
            if (string.IsNullOrWhiteSpace(html))
            {
                return 0;
            }

            if (string.Equals(property, "width", StringComparison.OrdinalIgnoreCase)
                || string.Equals(property, "height", StringComparison.OrdinalIgnoreCase))
            {
                string attribute = FirstMatch(html, "(?<![A-Za-z0-9_-])" + property + "\\s*=\\s*[\"']?([0-9]+(?:\\.[0-9]+)?)(?:px)?[\"']?", 1);
                double attributeValue;
                if (double.TryParse(attribute, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out attributeValue)
                    && attributeValue > 0)
                {
                    return attributeValue;
                }
            }

            string styleValue = FirstMatch(html, "style\\s*=\\s*[\"'][^\"']*(?<![A-Za-z0-9_-])" + Regex.Escape(property) + "\\s*:\\s*([0-9]+(?:\\.[0-9]+)?)px", 1);
            double pixels;
            return double.TryParse(styleValue, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out pixels)
                && pixels > 0
                ? pixels
                : 0;
        }

        private double ParseImageCssFraction(string html, string property)
        {
            if (string.IsNullOrWhiteSpace(html))
            {
                return 0;
            }

            string value = FirstMatch(html, "style\\s*=\\s*[\"'][^\"']*(?<![A-Za-z0-9_-])" + Regex.Escape(property) + "\\s*:\\s*([0-9]+(?:\\.[0-9]+)?)%", 1);
            double percent;
            if (!double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out percent)
                || percent <= 0)
            {
                return 0;
            }

            return Math.Min(1, percent / 100.0);
        }

        private void UpdateImageWidth(Image image, FrameworkElement owner)
        {
            if (image == null)
            {
                return;
            }

            if (string.Equals(image.Tag as string, FileIconTag, StringComparison.Ordinal))
            {
                SetFileIconSize(image);
                return;
            }

            BitmapSource bitmap = image.Source as BitmapSource;
            if (bitmap == null || bitmap.PixelWidth <= 0 || bitmap.PixelHeight <= 0)
            {
                return;
            }

            PostImageLayoutInfo info;
            _imageLayouts.TryGetValue(image, out info);
            if (owner == null && info != null)
            {
                owner = info.Owner;
            }

            double availableWidth = GetAvailablePostImageWidth(image, owner);
            if (availableWidth <= 0)
            {
                return;
            }

            double intrinsicWidth = bitmap.PixelWidth;
            double intrinsicHeight = bitmap.PixelHeight;
            double preferredWidth = intrinsicWidth;

            // The forum often supplies the intended preview size in IMG/style.
            // Respect it before using the decoded bitmap dimensions, because the
            // preview file itself can have a denser pixel size than its CSS size.
            if (info != null && info.DeclaredWidth > 0)
            {
                preferredWidth = Math.Min(preferredWidth, info.DeclaredWidth);
            }
            else if (info != null && info.DeclaredHeight > 0 && intrinsicHeight > 0)
            {
                preferredWidth = Math.Min(preferredWidth, info.DeclaredHeight * intrinsicWidth / intrinsicHeight);
            }

            if (info != null && info.WidthFraction > 0)
            {
                preferredWidth = Math.Min(preferredWidth, availableWidth * info.WidthFraction);
            }
            if (info != null && info.MaxWidthFraction > 0)
            {
                preferredWidth = Math.Min(preferredWidth, availableWidth * info.MaxWidthFraction);
            }
            if (info != null && info.MaxWidthPixels > 0)
            {
                preferredWidth = Math.Min(preferredWidth, info.MaxWidthPixels);
            }

            // Do not let an image become a full-width banner merely because the
            // preview bitmap is wider than the post. The cap is proportional to the
            // current content width (not a fixed pixel value) and varies by aspect
            // ratio, matching the compact presentation used by the forum page.
            double aspect = intrinsicHeight > 0 ? intrinsicWidth / intrinsicHeight : 1;
            double responsiveFraction = aspect < 0.8
                ? 0.76
                : (aspect < 1.25 ? 0.86 : 0.94);
            double responsiveLimit = availableWidth * responsiveFraction;
            if (preferredWidth >= availableWidth * 0.96)
            {
                preferredWidth = responsiveLimit;
            }

            double targetWidth = Math.Max(1, Math.Min(preferredWidth, availableWidth));
            double targetHeight = Math.Max(1, targetWidth * intrinsicHeight / intrinsicWidth);

            image.MinWidth = 0;
            image.MinHeight = 0;
            image.MaxWidth = availableWidth;
            image.MaxHeight = double.PositiveInfinity;
            image.Width = targetWidth;
            image.Height = targetHeight;
            image.Stretch = Stretch.Uniform;
            image.HorizontalAlignment = HorizontalAlignment.Left;
        }

        private double GetAvailablePostImageWidth(Image image, FrameworkElement owner)
        {
            double width = 0;
            if (owner != null && owner.ActualWidth > 0)
            {
                // Post bodies use 12 px padding on both sides.
                width = Math.Max(1, owner.ActualWidth - 24);
            }

            DependencyObject current = image != null ? VisualTreeHelper.GetParent(image) : null;
            int depth = 0;
            while (current != null && depth < 10)
            {
                FrameworkElement element = current as FrameworkElement;
                if (element != null && element.ActualWidth > 0)
                {
                    width = width > 0
                        ? Math.Min(width, element.ActualWidth)
                        : element.ActualWidth;
                }

                current = VisualTreeHelper.GetParent(current);
                depth++;
            }

            double viewportWidth = 0;
            if (TopicScrollViewer != null && TopicScrollViewer.ViewportWidth > 0)
            {
                viewportWidth = TopicScrollViewer.ViewportWidth - 24;
            }
            else if (Window.Current != null && Window.Current.Bounds.Width > 0)
            {
                viewportWidth = Window.Current.Bounds.Width - 24;
            }

            if (viewportWidth > 0)
            {
                width = width > 0 ? Math.Min(width, viewportWidth) : viewportWidth;
            }

            return Math.Max(1, width);
        }

        private int ExtractInternalPostId(Uri uri)
        {
            return uri == null ? 0 : ForumPostNavigation.PostId(uri.AbsoluteUri);
        }

        private async System.Threading.Tasks.Task ScrollToPostAsync(int postId, Uri postUri)
        {
            if (postId <= 0 || !_isPageActive || _postJumping) return;
            int version = _loadVersion;
            _postJumping = true;
            HideScrollJump();
            try
            {
                // Already visible posts need no network request. Otherwise open
                // the exact page returned by fj, as Android API.k does.
                TopicPostItem post;
                if (!_postsById.TryGetValue(postId, out post))
                {
                    while (_loading && _isPageActive) await Task.Delay(25);
                    if (!_isPageActive || version != _loadVersion) return;
                    if (!_postsById.TryGetValue(postId, out post))
                    {
                        string fragment = postUri == null ? string.Empty : postUri.Fragment;
                        Frame.Navigate(typeof(TopicPage), new TopicNavigationState(ForumPostNavigation.PostUrl(postId, fragment)));
                        return;
                    }
                }
                PostsListView.ScrollIntoView(post, ScrollIntoViewAlignment.Leading);
                PostsListView.UpdateLayout();
                await Task.Delay(16);
                if (!_isPageActive || version != _loadVersion) return;
                RealizePost(PostsListView.ContainerFromItem(post) as SelectorItem, post);
                PostsListView.UpdateLayout();
                FrameworkElement element;
                if (!_postElements.TryGetValue(postId, out element) || TopicScrollViewer == null) return;
                int spoilerNumber = ForumPostNavigation.SpoilerNumber(postUri == null ? string.Empty : postUri.Fragment);
                List<SpoilerBlock> blocks;
                if (spoilerNumber > 0 && _spoilers.TryGetValue(postId, out blocks))
                {
                    SpoilerBlock target = blocks.Find(block => block.Number == spoilerNumber);
                    for (int depth = 0; target == null && depth < 48; depth++)
                    {
                        // Bodies are built only on demand. Open containing
                        // ranges until the target nested spoiler is registered.
                        SpoilerBlock containing = blocks.Find(block => block.Number < spoilerNumber
                            && block.LastNumber >= spoilerNumber && block.Body.Visibility != Visibility.Visible);
                        if (containing == null) break;
                        containing.SetOpen(true);
                        target = blocks.Find(block => block.Number == spoilerNumber);
                    }
                    if (target != null)
                    {
                        // Open every ancestor, so an inner spoiler can be reached
                        // even when an enclosing spoiler is still collapsed.
                        foreach (SpoilerBlock block in new List<SpoilerBlock>(blocks))
                        {
                            DependencyObject parent = target.Body;
                            while (parent != null)
                            {
                                if (parent == block.Body) { block.SetOpen(true); break; }
                                parent = VisualTreeHelper.GetParent(parent);
                            }
                        }
                        element = target.Header;
                    }
                }
                PostsListView.UpdateLayout();
                await Task.Delay(16);
                if (!_isPageActive || version != _loadVersion) return;
                UIElement content = TopicScrollViewer.Content as UIElement;
                double targetOffset = element.TransformToVisual(content).TransformPoint(new Point(0, 0)).Y;
                TopicScrollViewer.ChangeView(null, Math.Max(0, targetOffset - 8), null, true);
            }
            catch (Exception error)
            {
                if (_isPageActive) await ShowPostMessageAsync("Не удалось открыть пост: " + error.Message);
            }
            finally
            {
                if (version == _loadVersion) { _postJumping = false; UpdatePagination(); }
            }
        }

        private sealed class SpoilerBlock
        {
            internal int Number;
            internal int LastNumber;
            internal FrameworkElement Header;
            internal StackPanel Body;
            internal Action<bool> SetOpen;
        }

        private async System.Threading.Tasks.Task ScrollToTopicBottomAsync()
        {
            PostsListView.UpdateLayout();
            await System.Threading.Tasks.Task.Delay(80);
            if (_postItems.Count > 0) PostsListView.ScrollIntoView(_postItems[_postItems.Count - 1]);
            if (TopicScrollViewer != null) TopicScrollViewer.ChangeView(null, TopicScrollViewer.ScrollableHeight, null);
        }

        private async System.Threading.Tasks.Task LoadPostUrlAsync(string url, int start = 0, bool goToTop = false)
        {
            if (string.IsNullOrWhiteSpace(url) || _loading)
            {
                return;
            }

            int version = _loadVersion;
            _loading = true;
            SetBusy(true, true);

            try
            {
                // fj selects the last page after sending without a returned post ID.
                TopicPageData data = await GetTopicPageAsync(url, start);
                if (data == null || !_isPageActive || version != _loadVersion)
                {
                    return;
                }

                if (_topicId > 0 && data.TopicId != _topicId)
                {
                    Frame.Navigate(typeof(TopicPage), new TopicNavigationState(url));
                    return;
                }
                ApplyTopicPage(data, _usePagination || goToTop);
                if (goToTop)
                {
                    PostsListView.UpdateLayout();
                    if (_postItems.Count > 0) PostsListView.ScrollIntoView(_postItems[0], ScrollIntoViewAlignment.Leading);
                    if (TopicScrollViewer != null) TopicScrollViewer.ChangeView(null, 0, null, true);
                }
            }
            catch (Exception error)
            {
                if (_isPageActive && version == _loadVersion)
                    await ShowPostMessageAsync("Не удалось загрузить страницу: " + error.Message);
            }
            finally
            {
                if (version == _loadVersion)
                {
                    _loading = false;
                    SetBusy(true, false);
                }
            }
        }

        private string ExtractBalancedDiv(string source, int startIndex)
        {
            if (string.IsNullOrWhiteSpace(source) || startIndex < 0 || startIndex >= source.Length)
            {
                return string.Empty;
            }

            Regex tagRegex = new Regex("</?div\\b[^>]*>", RegexOptions.IgnoreCase);
            MatchCollection tags = tagRegex.Matches(source, startIndex);
            int depth = 0;
            for (int i = 0; i < tags.Count; i++)
            {
                Match tag = tags[i];
                if (tag.Value.StartsWith("</", StringComparison.Ordinal))
                {
                    depth--;
                    if (depth == 0)
                    {
                        return source.Substring(startIndex, tag.Index + tag.Length - startIndex);
                    }
                }
                else
                {
                    depth++;
                }
            }

            return string.Empty;
        }

        private string ExtractDivInnerByClass(string source, string className)
        {
            if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(className))
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

        private void ApplyTagStyle(InlineStyle style, string tag, string tagHtml)
        {
            if (style == null || string.IsNullOrWhiteSpace(tag))
            {
                return;
            }

            if (tag == "b" || tag == "strong")
            {
                style.Bold = true;
            }
            else if (tag == "i" || tag == "em")
            {
                style.Italic = true;
            }
            else if (tag == "u")
            {
                style.Underline = true;
                style.Prefix = "_";
                style.Suffix = "_";
            }
            else if (tag == "s" || tag == "strike" || tag == "del")
            {
                style.Strikethrough = true;
                style.Prefix = "~~";
                style.Suffix = "~~";
            }
            else if (tag == "sup")
            {
                style.Prefix = "[";
                style.Suffix = "]";
            }
            else if (tag == "sub")
            {
                style.Prefix = "_";
            }

            string color = FirstMatch(tagHtml, "color=[\"']?([^\"' >;]+)", 1);
            if (string.IsNullOrWhiteSpace(color))
            {
                color = FirstMatch(tagHtml, "style=[\"'][^\"']*color\\s*:\\s*([^;\"']+)", 1);
            }

            Brush brush = BrushFromColor(color);
            if (brush != null)
            {
                style.Foreground = brush;
            }
        }

        private void ApplyRunStyle(Run run, InlineStyle style)
        {
            if (run == null || style == null)
            {
                return;
            }

            if (!string.IsNullOrEmpty(style.Prefix) || !string.IsNullOrEmpty(style.Suffix))
            {
                run.Text = (style.Prefix ?? string.Empty) + run.Text + (style.Suffix ?? string.Empty);
            }

            if (style.Bold)
            {
                run.FontWeight = Windows.UI.Text.FontWeights.Bold;
            }

            if (style.Italic)
            {
                run.FontStyle = Windows.UI.Text.FontStyle.Italic;
            }

            if (style.Foreground != null)
            {
                run.Foreground = style.Foreground;
            }

        }

        private Brush BrushFromColor(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            string clean = WebUtility.HtmlDecode(value.Trim()).Trim('\'', '"').ToLowerInvariant();
            if (clean.StartsWith("#", StringComparison.Ordinal))
            {
                Color parsed;
                if (TryParseHexColor(clean, out parsed))
                {
                    return new SolidColorBrush(parsed);
                }
            }

            if (clean.StartsWith("rgb", StringComparison.Ordinal))
            {
                Match rgb = Regex.Match(clean, "rgba?\\((\\d+)\\s*,\\s*(\\d+)\\s*,\\s*(\\d+)");
                if (rgb.Success)
                {
                    return new SolidColorBrush(Color.FromArgb(255, ClampByte(rgb.Groups[1].Value), ClampByte(rgb.Groups[2].Value), ClampByte(rgb.Groups[3].Value)));
                }
            }

            Color named;
            if (TryParseNamedColor(clean, out named))
            {
                return new SolidColorBrush(named);
            }

            return null;
        }

        private bool TryParseHexColor(string value, out Color color)
        {
            color = Colors.Transparent;
            string hex = value.TrimStart('#');
            if (hex.Length == 3)
            {
                hex = new string(new[] { hex[0], hex[0], hex[1], hex[1], hex[2], hex[2] });
            }

            if (hex.Length != 6)
            {
                return false;
            }

            byte r;
            byte g;
            byte b;
            if (byte.TryParse(hex.Substring(0, 2), System.Globalization.NumberStyles.HexNumber, null, out r)
                && byte.TryParse(hex.Substring(2, 2), System.Globalization.NumberStyles.HexNumber, null, out g)
                && byte.TryParse(hex.Substring(4, 2), System.Globalization.NumberStyles.HexNumber, null, out b))
            {
                color = Color.FromArgb(255, r, g, b);
                return true;
            }

            return false;
        }

        private byte ClampByte(string value)
        {
            int result;
            if (!int.TryParse(value, out result))
            {
                return 0;
            }

            return (byte)Math.Max(0, Math.Min(255, result));
        }

        private bool TryParseNamedColor(string value, out Color color)
        {
            color = Colors.Transparent;
            switch (value)
            {
                case "black": color = Colors.Black; return true;
                case "white": color = Colors.White; return true;
                case "red": color = Colors.Red; return true;
                case "darkred": color = Color.FromArgb(255, 140, 0, 0); return true;
                case "green": color = Colors.Green; return true;
                case "limegreen": color = Color.FromArgb(255, 65, 163, 23); return true;
                case "blue": color = Colors.Blue; return true;
                case "darkblue": color = Colors.DarkBlue; return true;
                case "royalblue": color = Color.FromArgb(255, 65, 105, 225); return true;
                case "skyblue": color = Colors.SkyBlue; return true;
                case "teal": color = Color.FromArgb(255, 0, 128, 128); return true;
                case "orange": color = Colors.Orange; return true;
                case "orangered": color = Colors.OrangeRed; return true;
                case "crimson": color = Colors.Crimson; return true;
                case "purple": color = Colors.Purple; return true;
                case "indigo": color = Colors.Indigo; return true;
                case "gray": color = Colors.Gray; return true;
                case "grey": color = Colors.Gray; return true;
                case "silver": color = Colors.Silver; return true;
                case "deeppink": color = Colors.DeepPink; return true;
                case "tomato": color = Colors.Tomato; return true;
                case "coral": color = Colors.Coral; return true;
                case "sienna": color = Colors.Sienna; return true;
                case "chocolate": color = Colors.Chocolate; return true;
                case "burlywood": color = Colors.BurlyWood; return true;
            }

            return false;
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

        private int ParseInt(string value)
        {
            int result;
            return int.TryParse(value, out result) ? result : 0;
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

        private bool IsForumDownloadLink(Uri uri)
        {
            return OfficialApiAttachments.AttachmentId(uri) > 0;
        }

        private async Task DownloadForumFileAsync(Uri uri, string suggestedName)
        {
            if (uri == null || _downloadingFile)
            {
                return;
            }

            if (!ForumAuthService.IsAuthorized)
            {
                await new MessageDialog(
                    "Для скачивания файлов требуется вход в аккаунт.",
                    "Требуется вход").ShowAsync();
                return;
            }

            _downloadingFile = true;
            StorageFile savedFile = null;

            try
            {
                string displayName = GetSuggestedDownloadFileName(uri, suggestedName);
                ShowDownloadOverlay("Загрузка файла", displayName, "Подключаемся...");

                Uri downloadUri = await OfficialApiService.GetAttachmentDownloadUriAsync(OfficialApiAttachments.AttachmentId(uri));
                savedFile = await DownloadForumFileToFolderAsync(downloadUri, displayName);

                HideDownloadOverlay();
                string downloadFolderName = await AppSettingsService.GetDownloadFolderDisplayNameAsync();
                await new MessageDialog(
                    "Файл сохранён в «" + downloadFolderName + "»: " + savedFile.Name,
                    "Скачивание завершено").ShowAsync();
            }
            catch (Exception ex)
            {
                HideDownloadOverlay();
                string message = ex.Message;
                if (string.IsNullOrWhiteSpace(message))
                {
                    message = "Не удалось скачать файл.";
                }

                await new MessageDialog(message, "Ошибка скачивания").ShowAsync();
            }
            finally
            {
                _downloadingFile = false;
            }
        }

        private async Task<StorageFile> DownloadForumFileToFolderAsync(
            Uri downloadUri,
            string suggestedName)
        {
            using (var request = new HttpRequestMessage(HttpMethod.Get, downloadUri))
            {
                PrepareDownloadRequest(request);
                using (HttpResponseMessage response = await _downloadClient.SendRequestAsync(request, HttpCompletionOption.ResponseHeadersRead))
                {
                    int statusCode = (int)response.StatusCode;
                    if (!response.IsSuccessStatusCode)
                    {
                        throw new InvalidOperationException(
                            "Сервер файлов вернул ошибку " + statusCode + ". Попробуйте скачать файл ещё раз.");
                    }

                    Uri responseUri = response.RequestMessage != null && response.RequestMessage.RequestUri != null
                        ? response.RequestMessage.RequestUri
                        : downloadUri;
                    string contentType = GetDownloadContentType(response);

                    if (IsDownloadHtml(contentType) && !HasDownloadDisposition(response))
                    {
                        throw new InvalidOperationException("Сервер файлов вернул веб-страницу вместо вложения. Попробуйте скачать файл ещё раз.");
                    }

                    string fileName = GetDownloadResponseFileName(response, suggestedName, responseUri);
                    StorageFile targetFile = await AppSettingsService.CreateDownloadFileAsync(fileName);

                    try
                    {
                        ulong? totalBytes = GetDownloadContentLength(response);
                        await CopyDownloadResponseAsync(response, targetFile, targetFile.Name, totalBytes);
                        return targetFile;
                    }
                    catch
                    {
                        try
                        {
                            await targetFile.DeleteAsync(StorageDeleteOption.PermanentDelete);
                        }
                        catch
                        {
                        }

                        throw;
                    }
                }
            }
        }

        private void PrepareDownloadRequest(HttpRequestMessage request)
        {
            ForumCookieHelper.ApplyDefaultHeaders(request);
            ForumCookieHelper.TryAppendHeader(request, "Accept", "*/*");
            ForumCookieHelper.TryAppendHeader(
                request,
                "Referer",
                String.IsNullOrWhiteSpace(_topicUrl) ? "https://4pda.to/forum/" : _topicUrl);
            // As in Android, the URL from fa authorizes this request; no web cookies.
        }

        private async Task CopyDownloadResponseAsync(
            HttpResponseMessage response,
            StorageFile targetFile,
            string fileName,
            ulong? totalBytes)
        {
            ulong downloaded = 0;
            QueueDownloadOverlayProgress(fileName, downloaded, totalBytes, true);

            // Keep the whole transfer inside the native WinRT HTTP/storage pipeline.
            // The old ReadAsync/WriteAsync loop crossed the managed/WinRT boundary for
            // every block and could fall to tens of KB/s on x86 CoreCLR.
            using (IRandomAccessStream targetStream = await targetFile.OpenAsync(FileAccessMode.ReadWrite))
            using (IOutputStream outputStream = targetStream.GetOutputStreamAt(0))
            {
                targetStream.Size = 0;

                DirectDownloadProgress progress = new DirectDownloadProgress(bytesWritten =>
                {
                    downloaded = bytesWritten;
                    QueueDownloadOverlayProgress(fileName, bytesWritten, totalBytes, false);
                });

                await response.Content.WriteToStreamAsync(outputStream).AsTask(progress);
                await outputStream.FlushAsync();
                downloaded = targetStream.Size;
            }

            QueueDownloadOverlayProgress(fileName, downloaded, totalBytes, true);
        }

        private sealed class DirectDownloadProgress : IProgress<ulong>
        {
            private readonly Action<ulong> _report;

            public DirectDownloadProgress(Action<ulong> report)
            {
                _report = report;
            }

            public void Report(ulong value)
            {
                if (_report != null)
                {
                    _report(value);
                }
            }
        }

        private void ShowDownloadOverlay(string title, string fileName, string progressText)
        {
            LoadingRing.IsActive = false;
            DownloadTitleTextBlock.Text = title;
            DownloadFileTextBlock.Text = String.IsNullOrWhiteSpace(fileName) ? "Файл" : fileName;
            DownloadProgressBar.IsIndeterminate = true;
            DownloadProgressBar.Value = 0;
            DownloadProgressTextBlock.Text = progressText;
            UpdateDownloadOverlayBounds();
            DownloadOverlayPopup.IsOpen = true;
            _lastDownloadProgressUpdate = DateTime.MinValue;
            _lastDownloadProgressBytes = 0;
            _downloadSpeedStartedAt = DateTime.UtcNow;
        }

        private void TopicRootGrid_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (DownloadOverlayPopup != null && DownloadOverlayPopup.IsOpen)
            {
                UpdateDownloadOverlayBounds();
            }
        }

        private void UpdateDownloadOverlayBounds()
        {
            if (DownloadOverlayPopup == null || DownloadOverlay == null)
            {
                return;
            }

            FrameworkElement windowRoot = Window.Current.Content as FrameworkElement;
            double width = windowRoot != null && windowRoot.ActualWidth > 0
                ? windowRoot.ActualWidth
                : Window.Current.Bounds.Width;
            double height = windowRoot != null && windowRoot.ActualHeight > 0
                ? windowRoot.ActualHeight
                : Window.Current.Bounds.Height;

            Point pageOrigin = new Point(0, 0);
            if (windowRoot != null && TopicRootGrid != null)
            {
                try
                {
                    GeneralTransform transform = TopicRootGrid.TransformToVisual(windowRoot);
                    pageOrigin = transform.TransformPoint(new Point(0, 0));
                }
                catch
                {
                    pageOrigin = new Point(0, 0);
                }
            }

            // The Popup is declared in TopicPage, so zero is the content area's origin.
            // Shift it back by the real sidebar/navbar offset to cover and center on the window.
            DownloadOverlayPopup.HorizontalOffset = -pageOrigin.X;
            DownloadOverlayPopup.VerticalOffset = -pageOrigin.Y;
            DownloadOverlay.Width = Math.Max(1, width);
            DownloadOverlay.Height = Math.Max(1, height);
            DownloadOverlayContent.Width = Math.Max(240, Math.Min(440, width - 64));
        }

        private void QueueDownloadOverlayProgress(
            string fileName,
            ulong downloaded,
            ulong? totalBytes,
            bool force)
        {
            bool shouldUpdate;
            lock (_downloadProgressSync)
            {
                DateTime now = DateTime.UtcNow;
                ulong byteDelta = downloaded >= _lastDownloadProgressBytes
                    ? downloaded - _lastDownloadProgressBytes
                    : downloaded;

                // Progress callbacks come from the native transfer thread. Keep the UI
                // work rare so it cannot throttle the network pipeline.
                shouldUpdate = force
                    || (now - _lastDownloadProgressUpdate).TotalMilliseconds >= 300
                    || byteDelta >= 8UL * 1024UL * 1024UL;

                if (shouldUpdate)
                {
                    _lastDownloadProgressUpdate = now;
                    _lastDownloadProgressBytes = downloaded;
                }
            }

            if (!shouldUpdate)
            {
                return;
            }

            _uiDispatcher.RunAsync(CoreDispatcherPriority.Low, () =>
            {
                ApplyDownloadOverlayProgress(fileName, downloaded, totalBytes);
            });
        }

        private void ApplyDownloadOverlayProgress(string fileName, ulong downloaded, ulong? totalBytes)
        {
            DownloadFileTextBlock.Text = String.IsNullOrWhiteSpace(fileName) ? "Файл" : fileName;

            if (totalBytes.HasValue && totalBytes.Value > 0)
            {
                double percent = downloaded * 100.0 / totalBytes.Value;
                if (percent < 0) percent = 0;
                if (percent > 100) percent = 100;

                DownloadProgressBar.IsIndeterminate = false;
                DownloadProgressBar.Value = percent;
                DownloadProgressTextBlock.Text =
                    FormatDownloadBytes(downloaded) + " из " + FormatDownloadBytes(totalBytes.Value)
                    + " (" + ((int)Math.Round(percent)).ToString() + "%)"
                    + FormatDownloadSpeedSuffix(downloaded);
            }
            else
            {
                DownloadProgressBar.IsIndeterminate = true;
                DownloadProgressTextBlock.Text = "Загружено " + FormatDownloadBytes(downloaded)
                    + FormatDownloadSpeedSuffix(downloaded);
            }
        }

        private void HideDownloadOverlay()
        {
            if (DownloadOverlayPopup == null)
            {
                return;
            }

            DownloadOverlayPopup.IsOpen = false;
            DownloadProgressBar.IsIndeterminate = false;
            DownloadProgressBar.Value = 0;
            DownloadProgressTextBlock.Text = String.Empty;
            DownloadFileTextBlock.Text = String.Empty;
            _lastDownloadProgressUpdate = DateTime.MinValue;
            _lastDownloadProgressBytes = 0;
            _downloadSpeedStartedAt = DateTime.MinValue;
        }

        private static bool IsDownloadHtml(string contentType)
        {
            return !String.IsNullOrWhiteSpace(contentType)
                && (contentType.IndexOf("text/html", StringComparison.OrdinalIgnoreCase) >= 0
                    || contentType.IndexOf("application/xhtml", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static bool HasDownloadDisposition(HttpResponseMessage response)
        {
            try
            {
                string headers = response != null && response.Content != null
                    ? response.Content.Headers.ToString()
                    : String.Empty;
                return !String.IsNullOrWhiteSpace(headers)
                    && (headers.IndexOf("content-disposition", StringComparison.OrdinalIgnoreCase) >= 0
                        && (headers.IndexOf("attachment", StringComparison.OrdinalIgnoreCase) >= 0
                            || headers.IndexOf("filename=", StringComparison.OrdinalIgnoreCase) >= 0
                            || headers.IndexOf("filename*=", StringComparison.OrdinalIgnoreCase) >= 0));
            }
            catch
            {
                return false;
            }
        }

        private static string GetDownloadContentType(HttpResponseMessage response)
        {
            try
            {
                if (response != null && response.Content != null && response.Content.Headers.ContentType != null)
                {
                    return response.Content.Headers.ContentType.ToString();
                }
            }
            catch
            {
            }

            return String.Empty;
        }

        private static ulong? GetDownloadContentLength(HttpResponseMessage response)
        {
            try
            {
                if (response != null && response.Content != null)
                {
                    return response.Content.Headers.ContentLength;
                }
            }
            catch
            {
            }

            return null;
        }

        private string GetDownloadResponseFileName(HttpResponseMessage response, string suggestedName, Uri responseUri)
        {
            string headers = String.Empty;
            try
            {
                headers = response != null && response.Content != null
                    ? response.Content.Headers.ToString()
                    : String.Empty;
            }
            catch
            {
            }

            Match utfName = Regex.Match(
                headers ?? String.Empty,
                @"filename\*\s*=\s*UTF-8''(?<name>[^;\r\n]+)",
                RegexOptions.IgnoreCase);
            if (utfName.Success)
            {
                try
                {
                    return SanitizeDownloadFileName(Uri.UnescapeDataString(utfName.Groups["name"].Value.Trim()));
                }
                catch
                {
                }
            }

            Match plainName = Regex.Match(
                headers ?? String.Empty,
                "filename\\s*=\\s*[\\\"']?(?<name>[^\\\"';\\r\\n]+)",
                RegexOptions.IgnoreCase);
            if (plainName.Success)
            {
                string decoded = WebUtility.HtmlDecode(plainName.Groups["name"].Value.Trim());
                string clean = SanitizeDownloadFileName(decoded);
                if (!String.IsNullOrWhiteSpace(clean))
                {
                    return clean;
                }
            }

            return GetSuggestedDownloadFileName(responseUri, suggestedName);
        }

        private string GetSuggestedDownloadFileName(Uri uri, string hint)
        {
            string cleanHint = CleanInlineText(hint ?? String.Empty);
            Match fileInHint = Regex.Match(
                cleanHint,
                @"(?<name>[^\\/:*?""<>|\r\n]+\.[A-Za-z0-9]{1,10})",
                RegexOptions.IgnoreCase);
            if (fileInHint.Success)
            {
                string hintedName = SanitizeDownloadFileName(fileInHint.Groups["name"].Value.Trim());
                if (!String.IsNullOrWhiteSpace(hintedName))
                {
                    return hintedName;
                }
            }

            if (uri != null)
            {
                string query = WebUtility.HtmlDecode(uri.Query ?? String.Empty);
                Match queryName = Regex.Match(
                    query,
                    @"(?:^|[?&])(?:filename|file|name)=(?<name>[^&]+)",
                    RegexOptions.IgnoreCase);
                if (queryName.Success)
                {
                    string value = queryName.Groups["name"].Value.Replace("+", " ");
                    try { value = Uri.UnescapeDataString(value); } catch { }
                    string queryFileName = SanitizeDownloadFileName(value);
                    if (!String.IsNullOrWhiteSpace(queryFileName))
                    {
                        return queryFileName;
                    }
                }

                if (uri.Segments != null && uri.Segments.Length > 0)
                {
                    string segment = uri.Segments[uri.Segments.Length - 1].Trim('/');
                    try { segment = Uri.UnescapeDataString(segment); } catch { }
                    string pathFileName = SanitizeDownloadFileName(segment);
                    if (!String.IsNullOrWhiteSpace(pathFileName) && pathFileName.IndexOf('.') > 0)
                    {
                        return pathFileName;
                    }
                }
            }

            return "4pda_file.bin";
        }

        private static string SanitizeDownloadFileName(string value)
        {
            if (String.IsNullOrWhiteSpace(value))
            {
                return String.Empty;
            }

            string result = WebUtility.HtmlDecode(value).Trim().Trim('.', ' ');
            char[] invalid = Path.GetInvalidFileNameChars();
            for (int i = 0; i < invalid.Length; i++)
            {
                result = result.Replace(invalid[i], '_');
            }

            if (result.Length > 120)
            {
                string extension = Path.GetExtension(result);
                int keep = Math.Max(1, 120 - extension.Length);
                result = result.Substring(0, keep) + extension;
            }

            return result;
        }

        private static string GetSafeDownloadExtension(string fileName)
        {
            string extension = String.Empty;
            try
            {
                extension = Path.GetExtension(fileName ?? String.Empty);
            }
            catch
            {
            }

            if (String.IsNullOrWhiteSpace(extension)
                || extension.Length > 12
                || !Regex.IsMatch(extension, @"^\.[A-Za-z0-9]+$"))
            {
                return ".bin";
            }

            return extension.ToLowerInvariant();
        }

        private string FormatDownloadSpeedSuffix(ulong downloaded)
        {
            if (_downloadSpeedStartedAt == DateTime.MinValue || downloaded == 0)
            {
                return String.Empty;
            }

            double seconds = (DateTime.UtcNow - _downloadSpeedStartedAt).TotalSeconds;
            if (seconds < 0.75)
            {
                return String.Empty;
            }

            ulong bytesPerSecond = (ulong)(downloaded / seconds);
            return " · " + FormatDownloadBytes(bytesPerSecond) + "/с";
        }

        private static string FormatDownloadBytes(ulong value)
        {
            double size = value;
            string[] units = new string[] { "Б", "КБ", "МБ", "ГБ", "ТБ" };
            int unit = 0;
            while (size >= 1024 && unit < units.Length - 1)
            {
                size /= 1024;
                unit++;
            }

            string format = unit == 0 ? "0" : (size >= 100 ? "0" : size >= 10 ? "0.0" : "0.00");
            return size.ToString(format) + " " + units[unit];
        }




        private Uri NormalizeHref(string href)
        {
            if (string.IsNullOrWhiteSpace(href))
            {
                return null;
            }

            string result = WebUtility.HtmlDecode(href.Trim()).Trim('"', '\'');
            if (string.IsNullOrWhiteSpace(result) || result == "#")
            {
                return null;
            }

            if (result.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase)
                || result.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            if (result.StartsWith("//", StringComparison.Ordinal))
            {
                result = "https:" + result;
            }

            Uri uri;
            if (Uri.TryCreate(result, UriKind.Absolute, out uri))
            {
                return IsSupportedLinkScheme(uri) ? uri : null;
            }

            Uri forumRoot = new Uri("https://4pda.to/", UriKind.Absolute);
            Uri forumPage = new Uri("https://4pda.to/forum/index.php", UriKind.Absolute);
            Uri currentPage = null;

            if (!string.IsNullOrWhiteSpace(_topicUrl))
            {
                if (!Uri.TryCreate(_topicUrl, UriKind.Absolute, out currentPage))
                {
                    Uri.TryCreate(forumPage, _topicUrl, out currentPage);
                }
            }

            if (result.StartsWith("/", StringComparison.Ordinal))
            {
                if (Uri.TryCreate(forumRoot, result, out uri))
                {
                    return IsSupportedLinkScheme(uri) ? uri : null;
                }
            }
            else
            {
                Uri baseUri = currentPage ?? forumPage;
                if (Uri.TryCreate(baseUri, result, out uri))
                {
                    return IsSupportedLinkScheme(uri) ? uri : null;
                }
            }

            return null;
        }

        private bool IsSupportedLinkScheme(Uri uri)
        {
            return uri != null
                && (string.Equals(uri.Scheme, "http", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(uri.Scheme, "https", StringComparison.OrdinalIgnoreCase));
        }

        private Brush ResolveBrush(string key)
        {
            object value = Application.Current.Resources[key];
            Brush brush = value as Brush;
            if (brush != null)
            {
                return brush;
            }

            if (key == "SystemControlHighlightAccentBrush")
            {
                return new SolidColorBrush(Color.FromArgb(255, 0, 120, 215));
            }

            return new SolidColorBrush(Colors.Transparent);
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

        private sealed class PhotoWrapPanel : Panel
        {
            public double HorizontalSpacing { get; set; }
            public double VerticalSpacing { get; set; }

            protected override Size MeasureOverride(Size availableSize)
            {
                double availableWidth = availableSize.Width;
                if (double.IsInfinity(availableWidth) || availableWidth <= 0)
                {
                    availableWidth = Window.Current != null && Window.Current.Bounds.Width > 0
                        ? Math.Max(1, Window.Current.Bounds.Width - 48)
                        : 1;
                }

                double rowWidth = 0;
                double rowHeight = 0;
                double totalHeight = 0;
                double measuredWidth = 0;

                for (int i = 0; i < Children.Count; i++)
                {
                    UIElement child = Children[i];
                    // Equivalent to CSS max-width: 100%: small photos keep their
                    // intrinsic size, large ones are measured inside the post width.
                    child.Measure(new Size(availableWidth, double.PositiveInfinity));
                    Size desired = child.DesiredSize;

                    double nextWidth = rowWidth <= 0
                        ? desired.Width
                        : rowWidth + HorizontalSpacing + desired.Width;

                    if (rowWidth > 0 && nextWidth > availableWidth)
                    {
                        measuredWidth = Math.Max(measuredWidth, rowWidth);
                        totalHeight += rowHeight + VerticalSpacing;
                        rowWidth = desired.Width;
                        rowHeight = desired.Height;
                    }
                    else
                    {
                        rowWidth = nextWidth;
                        rowHeight = Math.Max(rowHeight, desired.Height);
                    }
                }

                measuredWidth = Math.Max(measuredWidth, rowWidth);
                totalHeight += rowHeight;
                return new Size(availableWidth, totalHeight);
            }

            protected override Size ArrangeOverride(Size finalSize)
            {
                double availableWidth = finalSize.Width > 0
                    ? finalSize.Width
                    : (Window.Current != null && Window.Current.Bounds.Width > 0
                        ? Math.Max(1, Window.Current.Bounds.Width - 48)
                        : 1);
                double x = 0;
                double y = 0;
                double rowHeight = 0;

                for (int i = 0; i < Children.Count; i++)
                {
                    UIElement child = Children[i];
                    Size desired = child.DesiredSize;
                    double nextRight = x <= 0 ? desired.Width : x + HorizontalSpacing + desired.Width;

                    if (x > 0 && nextRight > availableWidth)
                    {
                        x = 0;
                        y += rowHeight + VerticalSpacing;
                        rowHeight = 0;
                    }

                    if (x > 0)
                    {
                        x += HorizontalSpacing;
                    }

                    child.Arrange(new Rect(x, y, desired.Width, desired.Height));
                    x += desired.Width;
                    rowHeight = Math.Max(rowHeight, desired.Height);
                }

                return new Size(availableWidth, y + rowHeight);
            }
        }

        private sealed class PostImageLayoutInfo
        {
            public FrameworkElement Owner { get; set; }
            public double DeclaredWidth { get; set; }
            public double DeclaredHeight { get; set; }
            public double WidthFraction { get; set; }
            public double MaxWidthFraction { get; set; }
            public double MaxWidthPixels { get; set; }
        }

        private sealed class InlineStyle
        {
            public bool Bold { get; set; }
            public bool Italic { get; set; }
            public bool Underline { get; set; }
            public bool Strikethrough { get; set; }
            public string Prefix { get; set; }
            public string Suffix { get; set; }
            public Brush Foreground { get; set; }
            public double InlineImageSize { get; set; }

            public InlineStyle Clone()
            {
                InlineStyle clone = new InlineStyle();
                clone.Bold = Bold;
                clone.Italic = Italic;
                clone.Underline = Underline;
                clone.Strikethrough = Strikethrough;
                clone.Prefix = Prefix;
                clone.Suffix = Suffix;
                clone.Foreground = Foreground;
                clone.InlineImageSize = InlineImageSize;
                return clone;
            }
        }
    }
}
