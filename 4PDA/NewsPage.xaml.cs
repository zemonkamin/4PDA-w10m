using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using _4PDA.Models;
using _4PDA.Services;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;

namespace _4PDA
{
    public sealed partial class NewsPage : Page
    {
        private readonly FourPdaNewsService _newsService = new FourPdaNewsService();
        private readonly HashSet<FrameworkElement> _cards = new HashSet<FrameworkElement>();
        private ItemsWrapGrid _itemsWrapGrid;
        private double _cellWidth, _cellHeight, _horizontalGap;
        private bool _loading, _active;
        private DateTime _loadedAt;
        private int _snapshotVersion;
        public List<NewsItem> NewsItems { get; private set; }

        public NewsPage()
        {
            InitializeComponent();
            NavigationCacheMode = NavigationCacheMode.Enabled;
            NewsItems = new List<NewsItem>();
            Loaded += delegate { _active = true; };
            Unloaded += delegate { _active = false; _snapshotVersion++; };
        }
        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            _active = true;
            await EnsureFreshAsync();
        }
        internal async Task EnsureFreshAsync()
        {
            if (!_loading && (NewsItems.Count == 0 || DateTime.UtcNow - _loadedAt > TimeSpan.FromMinutes(2)))
                await LoadNewsAsync();
        }
        private async Task LoadNewsAsync()
        {
            if (_loading) return;
            _loading = true; LoadingRing.IsActive = true;
            EmptyTextBlock.Visibility = StatusPanel.Visibility = Visibility.Collapsed;
            try
            {
                List<NewsItem> items = await _newsService.GetNewsAsync(1);
                // One reset instead of 30 collection/layout notifications and partial frames.
                NewsItems = items;
                NewsGridView.ItemsSource = NewsItems;
                _loadedAt = DateTime.UtcNow;
                EmptyTextBlock.Visibility = items.Count > 0 ? Visibility.Collapsed : Visibility.Visible;
                if (!string.IsNullOrWhiteSpace(_newsService.LastLoadWarning))
                { StatusTextBlock.Text = _newsService.LastLoadWarning; StatusPanel.Visibility = Visibility.Visible; }
                SaveNewsSnapshotQuietly();
            }
            catch (Exception ex)
            {
                StatusTextBlock.Text = "Не удалось обновить ленту. " + ex.Message;
                StatusPanel.Visibility = Visibility.Visible;
                EmptyTextBlock.Visibility = NewsItems.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            }
            finally { _loading = false; LoadingRing.IsActive = false; }
        }
        private async void RetryButton_Click(object sender, RoutedEventArgs e) { await LoadNewsAsync(); }
        private async void SaveNewsSnapshotQuietly()
        {
            int version = ++_snapshotVersion;
            var snapshot = new List<NewsItem>(NewsItems.Count);
            foreach (NewsItem item in NewsItems)
                snapshot.Add(new NewsItem { Id = item.Id, Url = item.Url, Title = item.Title, Description = item.Description,
                    Date = item.Date, Author = item.Author, ImageUrl = item.ImageUrl, CommentsCount = item.CommentsCount });
            try
            {
                await Task.Delay(2000);
                if (!_active || version != _snapshotVersion) return;
                await Task.Run(() => LiveTileService.SaveNewsSnapshotAsync(snapshot));
            }
            catch { }
        }
        private void NewsGridView_ItemClick(object sender, ItemClickEventArgs e)
        {
            var item = e.ClickedItem as NewsItem;
            if (item != null) AppNavigationActions.OpenNewsArticle(item);
        }
        private void NewsGridView_Loaded(object sender, RoutedEventArgs e)
        {
            _itemsWrapGrid = NewsGridView.ItemsPanelRoot as ItemsWrapGrid;
            UpdateCardWidth();
        }
        private void NewsGridView_SizeChanged(object sender, SizeChangedEventArgs e) { UpdateCardWidth(); }
        private void NewsGridView_ContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
        {
            FrameworkElement root = args.ItemContainer.ContentTemplateRoot as FrameworkElement;
            if (root == null) return;
            if (args.InRecycleQueue)
            {
                _cards.Remove(root);
                Image image = root.FindName("CardImage") as Image;
                if (image != null) { DeferredImage.SetUrl(image, string.Empty); image.Source = null; }
            }
            else if (args.Phase == 0)
            {
                _cards.Add(root);
                root.Margin = new Thickness(_horizontalGap / 2, 0, _horizontalGap / 2, 8);
            }
        }
        private void UpdateCardWidth()
        {
            if (_itemsWrapGrid == null) _itemsWrapGrid = NewsGridView.ItemsPanelRoot as ItemsWrapGrid;
            if (_itemsWrapGrid == null || NewsGridView.ActualWidth <= 16) return;
            double available = NewsGridView.ActualWidth - 16;
            double gap = NewsCardPolicy.Columns(available) > 1 ? ImageLoadPolicy.NewsGap(Window.Current.Bounds.Width, Window.Current.Bounds.Height) : 0;
            double cell = NewsCardPolicy.CellWidth(available);
            double height = Math.Floor((cell - gap) * 0.5625) + 146;
            if (_cellWidth == cell && _cellHeight == height && _horizontalGap == gap) return;
            _cellWidth = cell; _cellHeight = height; _horizontalGap = gap;
            _itemsWrapGrid.ItemWidth = cell; _itemsWrapGrid.ItemHeight = height;
            foreach (FrameworkElement root in _cards) root.Margin = new Thickness(gap / 2, 0, gap / 2, 8);
        }
    }
}
