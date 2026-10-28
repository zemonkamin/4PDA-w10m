using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using _4PDA.Models;
using _4PDA.Services;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;

namespace _4PDA
{
    public sealed partial class ForumPage : Page
    {
        private readonly FourPdaForumService _forumService = new FourPdaForumService();
        private readonly ObservableCollection<ForumTopicItem> _items = new ObservableCollection<ForumTopicItem>();
        private readonly HashSet<int> _loadedStarts = new HashSet<int>();
        private readonly HashSet<string> _loadedItems = new HashSet<string>();
        private ForumItem _forum;
        private ScrollViewer _scrollViewer;
        private string _searchQuery = string.Empty;
        private ForumSearchNavigationArgs _searchArgs;
        private int _loadVersion;
        private int _start;
        private int _pageSize = 30;
        private bool _loading;
        private bool _hasMore = true;

        private bool IsFavoriteForums
        {
            get { return _forum != null && _forum.Id == AppNavigationActions.FavoriteForumsId; }
        }

        public bool IsSearchMode { get; private set; }

        public string CurrentSearchQuery
        {
            get { return _searchQuery; }
        }

        public ForumPage()
        {
            InitializeComponent();
            TopicsListView.ItemsSource = _items;
        }

        protected async override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            ForumSearchNavigationArgs search = e.Parameter as ForumSearchNavigationArgs;
            _searchArgs = search;
            IsSearchMode = search != null;
            _searchQuery = search == null ? string.Empty : (search.Query ?? string.Empty).Trim();

            if (IsSearchMode)
            {
                _forum = null;
                ResetItems();

                if (string.IsNullOrWhiteSpace(_searchQuery))
                {
                    EmptyTextBlock.Text = "Введите запрос";
                    EmptyTextBlock.Visibility = Visibility.Visible;
                    return;
                }

                if (_searchQuery.Length < 3)
                {
                    EmptyTextBlock.Text = "Введите не менее трёх символов";
                    EmptyTextBlock.Visibility = Visibility.Visible;
                    return;
                }

                await LoadNextPageAsync(true);
                return;
            }

            ForumItem forum = e.Parameter as ForumItem;
            if (forum != null)
            {
                _forum = forum;
            }

            if (_forum == null)
            {
                _forum = new ForumItem();
                _forum.Id = 1;
                _forum.Title = "Форум";
            }

            ResetItems();
            await LoadNextPageAsync(true);
        }

        public async System.Threading.Tasks.Task SearchAsync(string query)
        {
            if (!IsSearchMode)
                return;

            _searchQuery = (query ?? string.Empty).Trim();
            if (_searchArgs != null)
                _searchArgs.Query = _searchQuery;

            ResetItems();

            if (_searchQuery.Length < 3)
            {
                EmptyTextBlock.Text = "Введите не менее трёх символов";
                EmptyTextBlock.Visibility = Visibility.Visible;
                return;
            }

            await LoadNextPageAsync(true);
        }

        private void ResetItems()
        {
            _loadVersion++;
            _loading = false;
            _items.Clear();
            _loadedStarts.Clear();
            _loadedItems.Clear();
            _start = 0;
            _pageSize = 30;
            _hasMore = true;
            EmptyTextBlock.Visibility = Visibility.Collapsed;
            SetBusy(true, false);
            SetBusy(false, false);
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            if (_scrollViewer != null)
            {
                _scrollViewer.ViewChanged -= ScrollViewer_ViewChanged;
                _scrollViewer = null;
            }

            base.OnNavigatedFrom(e);
        }

        private void TopicsListView_Loaded(object sender, RoutedEventArgs e)
        {
            if (_scrollViewer != null)
            {
                return;
            }

            _scrollViewer = FindVisualChild<ScrollViewer>(TopicsListView);
            if (_scrollViewer != null)
            {
                _scrollViewer.ViewChanged += ScrollViewer_ViewChanged;
            }
        }

        private async void ScrollViewer_ViewChanged(object sender, ScrollViewerViewChangedEventArgs e)
        {
            if (ShouldLoadMore())
            {
                await LoadNextPageAsync(false);
            }
        }

        private void TopicsListView_ItemClick(object sender, ItemClickEventArgs e)
        {
            ForumTopicItem item = e.ClickedItem as ForumTopicItem;
            if (item == null)
            {
                return;
            }

            if (item.IsForum)
            {
                ForumItem forum = new ForumItem();
                forum.Id = item.Id;
                forum.Title = item.Title;
                AppNavigationActions.OpenForumTopics(forum);
                return;
            }

            if (!string.IsNullOrWhiteSpace(item.Url))
            {
                AppNavigationActions.OpenTopic(item);
            }
        }

        private bool ShouldLoadMore()
        {
            if (_scrollViewer == null || _loading || !_hasMore)
            {
                return false;
            }

            return _scrollViewer.ScrollableHeight <= 0
                || _scrollViewer.VerticalOffset >= _scrollViewer.ScrollableHeight - 360;
        }

        private async System.Threading.Tasks.Task LoadNextPageAsync(bool firstLoad)
        {
            if (_loading || !_hasMore || (!IsSearchMode && _forum == null))
            {
                return;
            }

            if (IsSearchMode && string.IsNullOrWhiteSpace(_searchQuery))
                return;

            if (_loadedStarts.Contains(_start))
            {
                return;
            }

            _loading = true;
            _loadedStarts.Add(_start);
            SetBusy(firstLoad, true);
            int loadVersion = _loadVersion;
            int requestedStart = _start;

            try
            {
                ForumTopicsData data;
                if (IsSearchMode)
                    data = await _forumService.SearchTopicsAsync(_searchQuery, requestedStart);
                else if (IsFavoriteForums)
                    data = await _forumService.GetFavoriteForumsAsync();
                else
                    data = await _forumService.GetTopicsAsync(_forum.Id, requestedStart);

                if (loadVersion != _loadVersion)
                    return;

                if (data != null)
                {
                    if (data.PageSize > 0)
                    {
                        _pageSize = data.PageSize;
                    }

                    for (int i = 0; i < data.Items.Count; i++)
                    {
                        if (TryRememberItem(data.Items[i]))
                        {
                            _items.Add(data.Items[i]);
                        }
                    }

                    _hasMore = !IsFavoriteForums && data.HasMore && data.Items.Count > 0;
                    _start = requestedStart + Math.Max(_pageSize, data.Items.Count);
                }
                else
                {
                    _hasMore = false;
                }

                if (_items.Count == 0)
                {
                    EmptyTextBlock.Text = IsSearchMode
                        ? "Ничего не найдено"
                        : (IsFavoriteForums ? "Избранных форумов нет" : "Темы не загружены");
                    EmptyTextBlock.Visibility = Visibility.Visible;
                }
                else
                {
                    EmptyTextBlock.Visibility = Visibility.Collapsed;
                }
            }
            catch (Exception ex)
            {
                if (loadVersion != _loadVersion)
                    return;

                // Не считаем неудачный start загруженным навсегда: после возврата на
                // страницу или повторного открытия запрос можно выполнить снова.
                _loadedStarts.Remove(requestedStart);

                if (_items.Count == 0)
                {
                    EmptyTextBlock.Text = IsSearchMode
                        ? "Поиск не выполнен: " + GetShortError(ex)
                        : (IsFavoriteForums ? "Избранные форумы не загружены: " : "Темы не загружены: ") + GetShortError(ex);
                    EmptyTextBlock.Visibility = Visibility.Visible;
                }

                _hasMore = _items.Count == 0;
            }
            finally
            {
                if (loadVersion == _loadVersion)
                {
                    _loading = false;
                    SetBusy(firstLoad, false);

                    if (!firstLoad && ShouldLoadMore())
                    {
                        await LoadNextPageAsync(false);
                    }
                }
            }
        }

        private void SetBusy(bool firstLoad, bool busy)
        {
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
        }

        private bool TryRememberItem(ForumTopicItem item)
        {
            if (item == null)
            {
                return false;
            }

            string prefix = item.IsForum ? "forum:" : "topic:";
            string key = prefix + item.Id.ToString();
            if (item.Id <= 0 && !string.IsNullOrWhiteSpace(item.Url))
            {
                key = prefix + item.Url;
            }

            if (_loadedItems.Contains(key))
            {
                return false;
            }

            _loadedItems.Add(key);
            return true;
        }

        private static string GetShortError(Exception error)
        {
            string message = error == null ? string.Empty : (error.Message ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(message))
                return "неизвестная ошибка";
            if (message.Length > 140)
                message = message.Substring(0, 140).Trim();
            return message;
        }

        private T FindVisualChild<T>(DependencyObject root) where T : DependencyObject
        {
            if (root == null)
            {
                return null;
            }

            int count = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(root, i);
                T typedChild = child as T;
                if (typedChild != null)
                {
                    return typedChild;
                }

                T nestedChild = FindVisualChild<T>(child);
                if (nestedChild != null)
                {
                    return nestedChild;
                }
            }

            return null;
        }
    }
}
