using System;
using System.Collections.Generic;
using _4PDA.Models;
using _4PDA.Services;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;

namespace _4PDA
{
    public sealed partial class ForumsListPage : Page
    {
        private readonly FourPdaForumService _forumService = new FourPdaForumService();
        private List<ForumListItem> _visibleItems = new List<ForumListItem>();
        private bool _loaded;

        public ForumsListPage()
        {
            InitializeComponent();
            ForumsListView.ItemsSource = _visibleItems;
        }

        protected async override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            if (!_loaded || _visibleItems.Count == 0)
            {
                _loaded = true;
                await LoadForumsAsync();
            }
        }

        private async System.Threading.Tasks.Task LoadForumsAsync()
        {
            LoadingRing.IsActive = true;
            EmptyTextBlock.Visibility = Visibility.Collapsed;

            try
            {
                ForumItem root = await _forumService.GetForumsAsync();
                _visibleItems = ForumListItem.AllForums(root);
                ForumsListView.ItemsSource = _visibleItems;

                EmptyTextBlock.Visibility = _visibleItems.Count > 0 ? Visibility.Collapsed : Visibility.Visible;
            }
            catch (Exception ex)
            {
                _loaded = false;
                _visibleItems.Clear();
                ForumsListView.ItemsSource = null;
                EmptyTextBlock.Text = "Форумы не загружены. " + ex.Message;
                EmptyTextBlock.Visibility = Visibility.Visible;
            }
            finally
            {
                LoadingRing.IsActive = false;
            }
        }

        private void ForumsListView_ItemClick(object sender, ItemClickEventArgs e)
        {
            ForumListItem item = e.ClickedItem as ForumListItem;
            if (item == null || item.Forum == null)
            {
                return;
            }

            if (!item.IsSection) AppNavigationActions.OpenForumTopics(item.Forum);
        }
    }
}
