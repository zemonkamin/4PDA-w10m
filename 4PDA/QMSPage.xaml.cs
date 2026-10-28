using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Threading.Tasks;
using Windows.UI.Core;
using _4PDA.Services;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media.Imaging;
using Windows.UI.Xaml.Navigation;

namespace _4PDA
{
    public sealed partial class QMSPage : Page
    {
        private readonly ObservableCollection<QmsNode> _visibleItems = new ObservableCollection<QmsNode>();
        private readonly List<QmsNode> _rootItems = new List<QmsNode>();
        private readonly QmsService _qmsService = new QmsService();
        private bool _loading;
        private bool _isPageActive;
        private readonly HashSet<QmsNode> _toggling = new HashSet<QmsNode>();

        public QMSPage()
        {
            InitializeComponent();
            QmsListView.ItemsSource = _visibleItems;
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            _isPageActive = true;
            ForumAuthService.AccountChanged -= ForumAuthService_AccountChanged;
            ForumAuthService.AccountChanged += ForumAuthService_AccountChanged;

            if (!ForumAuthService.IsAuthorized)
            {
                ShowAuthorizationRequired();
                return;
            }

            await LoadContactsAsync(false);
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            _isPageActive = false;
            ForumAuthService.AccountChanged -= ForumAuthService_AccountChanged;
            base.OnNavigatedFrom(e);
        }

        private async void ForumAuthService_AccountChanged(object sender, EventArgs e)
        {
            await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, async delegate
            {
                if (!ForumAuthService.IsAuthorized)
                {
                    ShowAuthorizationRequired();
                    return;
                }

                await LoadContactsAsync(true);
            });
        }

        private async Task LoadContactsAsync(bool forceReload)
        {
            if (_loading)
                return;

            if (!ForumAuthService.IsAuthorized)
            {
                ShowAuthorizationRequired();
                return;
            }

            if (!forceReload && _rootItems.Count > 0)
                return;

            _loading = true;
            SetBusy(true);
            SetStatus("");
            QmsEmptyTextBlock.Visibility = Visibility.Collapsed;

            try
            {
                List<QmsContact> contacts = await _qmsService.GetContactsAsync();
                if (!_isPageActive) return;
                _rootItems.Clear();
                _visibleItems.Clear();

                if (contacts != null)
                {
                    foreach (QmsContact contact in contacts)
                    {
                        if (contact == null || String.IsNullOrWhiteSpace(contact.Id))
                            continue;

                        QmsNode node = new QmsNode();
                        node.Id = contact.Id;
                        node.ContactId = contact.Id;
                        node.ContactNick = contact.Title;
                        node.Title = String.IsNullOrWhiteSpace(contact.Title)
                            ? "Пользователь " + contact.Id
                            : contact.Title;
                        node.Subtitle = contact.UnreadCount > 0
                            ? "Новых сообщений: " + contact.UnreadCount
                            : "Нажмите, чтобы показать чаты";
                        node.AvatarUrl = contact.AvatarUrl;
                        node.UnreadCount = contact.UnreadCount;
                        node.IsThread = false;


                        _rootItems.Add(node);
                        _visibleItems.Add(node);
                    }
                }

                if (_visibleItems.Count == 0)
                    ShowEmpty("Диалоги QMS не найдены.");
                else
                    QmsEmptyTextBlock.Visibility = Visibility.Collapsed;
            }
            catch (UnauthorizedAccessException)
            {
                ShowAuthorizationRequired();
            }
            catch (Exception ex)
            {
                ShowEmpty("Не удалось загрузить сообщения.");
                SetStatus(ex.Message);
            }
            finally
            {
                SetBusy(false);
                _loading = false;
            }
        }

        private async Task LoadThreadsAsync(QmsNode contactNode)
        {
            if (contactNode == null || contactNode.IsThread || contactNode.ThreadsLoaded || contactNode.IsLoadingThreads)
                return;

            contactNode.IsLoadingThreads = true;
            string previousSubtitle = contactNode.Subtitle;
            contactNode.Subtitle = "Загрузка чатов...";
            SetStatus("");

            try
            {
                List<QmsThread> threads = await _qmsService.GetThreadsAsync(contactNode.ContactId);
                contactNode.Children.Clear();

                if (threads != null)
                {
                    foreach (QmsThread thread in threads)
                    {
                        if (thread == null || String.IsNullOrWhiteSpace(thread.Id))
                            continue;

                        QmsNode child = new QmsNode();
                        child.Id = thread.Id;
                        child.ThreadId = thread.Id;
                        child.ContactId = contactNode.ContactId;
                        child.ContactNick = contactNode.ContactNick;
                        child.Title = String.IsNullOrWhiteSpace(thread.Title) ? "Без названия" : thread.Title;
                        child.Subtitle = BuildThreadSubtitle(thread);
                        child.UnreadCount = thread.UnreadCount;
                        child.MessagesCount = thread.MessagesCount;
                        child.IsThread = true;
                        child.Parent = contactNode;
                        child.AvatarUrl = contactNode.AvatarUrl;
                        child.AvatarImage = null;
                        contactNode.Children.Add(child);
                    }
                }

                contactNode.ThreadsLoaded = true;
                contactNode.Subtitle = contactNode.Children.Count == 0
                    ? "Чатов нет"
                    : "Чатов: " + contactNode.Children.Count;
            }
            catch (Exception ex)
            {
                contactNode.Subtitle = String.IsNullOrWhiteSpace(previousSubtitle)
                    ? "Не удалось загрузить чаты"
                    : previousSubtitle;
                SetStatus("Чаты не загружены: " + ex.Message);
                throw;
            }
            finally
            {
                contactNode.IsLoadingThreads = false;
            }
        }

        private static string BuildThreadSubtitle(QmsThread thread)
        {
            if (thread == null)
                return "";

            List<string> parts = new List<string>();
            if (thread.MessagesCount > 0)
                parts.Add("сообщений: " + thread.MessagesCount);
            if (thread.UnreadCount > 0)
                parts.Add("новых: " + thread.UnreadCount);
            if (!String.IsNullOrWhiteSpace(thread.LastMessageText))
                parts.Add(thread.LastMessageText);

            return parts.Count == 0 ? "Открыть чат" : String.Join(" · ", parts.ToArray());
        }

        private async void QmsListView_ItemClick(object sender, ItemClickEventArgs e)
        {
            QmsNode node = e.ClickedItem as QmsNode;
            if (node == null)
                return;

            if (!node.IsThread)
            {
                await ToggleNodeAsync(node);
                return;
            }

            OpenChat(node);
        }

        private async void QmsArrow_Tapped(object sender, TappedRoutedEventArgs e)
        {
            FrameworkElement element = sender as FrameworkElement;
            QmsNode node = element == null ? null : element.Tag as QmsNode;
            if (node == null || node.IsThread)
                return;

            e.Handled = true;
            await ToggleNodeAsync(node);
        }

        private async Task ToggleNodeAsync(QmsNode node)
        {
            if (!_isPageActive || node == null || node.IsThread || !_toggling.Add(node)) return;
            try
            {
                if (!node.ThreadsLoaded) await LoadThreadsAsync(node);
                // Leave the native pointer/ItemClick event before changing its containers.
                await Task.Delay(1);
                if (!_isPageActive) return;
                int index = _visibleItems.IndexOf(node);
                if (index < 0) return;
                node.IsExpanded = !node.IsExpanded;
                if (node.IsExpanded)
                {
                    int inserted = 0;
                    foreach (QmsNode child in node.Children)
                    {
                        if (!_isPageActive) return;
                        _visibleItems.Insert(index + 1 + inserted++, child);
                        if (inserted % 12 == 0) await Task.Delay(1);
                    }
                }
                else
                {
                    while (index + 1 < _visibleItems.Count && _visibleItems[index + 1].Parent == node)
                        _visibleItems.RemoveAt(index + 1);
                }
            }
            catch (Exception ex)
            {
                SetStatus("Чаты не загружены: " + ex.Message);
            }
            finally { _toggling.Remove(node); }
        }

        private void OpenChat(QmsNode node)
        {
            if (node == null || String.IsNullOrWhiteSpace(node.ContactId) || String.IsNullOrWhiteSpace(node.ThreadId))
                return;

            QmsThreadNavigationArgs args = new QmsThreadNavigationArgs();
            args.ContactId = node.ContactId;
            args.ContactNick = node.ContactNick;
            args.ThreadId = node.ThreadId;
            args.ThreadTitle = node.Title;
            args.AvatarUrl = node.AvatarUrl;
            args.AvatarImage = node.AvatarImage;
            Frame.Navigate(typeof(ChatPage), args);
        }

        private void AvatarImage_ImageFailed(object sender, ExceptionRoutedEventArgs e)
        {
            Image image = sender as Image;
            QmsNode node = image == null ? null : image.DataContext as QmsNode;
            if (node != null)
            {
                node.AvatarImage = null;
                return;
            }

            if (image != null)
                image.Source = null;
        }

        private void SetBusy(bool busy)
        {
            QmsProgressRing.IsActive = busy;
            QmsListView.IsEnabled = !busy && ForumAuthService.IsAuthorized;
        }

        private void ShowAuthorizationRequired()
        {
            _loading = false;
            _rootItems.Clear();
            _visibleItems.Clear();
            ShowEmpty("Войдите в аккаунт 4PDA, чтобы открыть сообщения.");
            SetStatus("");
            SetBusy(false);
        }

        private void ShowEmpty(string text)
        {
            QmsEmptyTextBlock.Text = text ?? "";
            QmsEmptyTextBlock.Visibility = Visibility.Visible;
        }

        private void SetStatus(string text)
        {
            StatusTextBlock.Text = text ?? "";
            StatusTextBlock.Visibility = String.IsNullOrWhiteSpace(StatusTextBlock.Text)
                ? Visibility.Collapsed
                : Visibility.Visible;
        }
    }

    public sealed class QmsNode : INotifyPropertyChanged
    {
        private bool _isExpanded;
        private bool _isLoadingThreads;
        private string _subtitle;
        private BitmapImage _avatarImage;

        public QmsNode()
        {
            Children = new List<QmsNode>();
            Title = "";
            Subtitle = "";
        }

        public event PropertyChangedEventHandler PropertyChanged;

        public string Id { get; set; }
        public string ContactId { get; set; }
        public string ContactNick { get; set; }
        public string ThreadId { get; set; }
        public string Title { get; set; }
        public string AvatarUrl { get; set; }
        public bool ThreadsLoaded { get; set; }
        public bool IsThread { get; set; }
        public int MessagesCount { get; set; }
        public int UnreadCount { get; set; }
        public QmsNode Parent { get; set; }
        public List<QmsNode> Children { get; private set; }

        public string Subtitle
        {
            get { return _subtitle; }
            set
            {
                if (_subtitle == value)
                    return;
                _subtitle = value;
                OnPropertyChanged("Subtitle");
            }
        }

        public bool IsExpanded
        {
            get { return _isExpanded; }
            set
            {
                if (_isExpanded == value)
                    return;
                _isExpanded = value;
                OnPropertyChanged("IsExpanded");
                OnPropertyChanged("ExpandButtonText");
            }
        }

        public bool IsLoadingThreads
        {
            get { return _isLoadingThreads; }
            set
            {
                if (_isLoadingThreads == value)
                    return;
                _isLoadingThreads = value;
                OnPropertyChanged("IsLoadingThreads");
                OnPropertyChanged("ExpandButtonText");
            }
        }

        public BitmapImage AvatarImage
        {
            get { return _avatarImage; }
            set
            {
                if (Object.ReferenceEquals(_avatarImage, value))
                    return;
                _avatarImage = value;
                OnPropertyChanged("AvatarImage");
                OnPropertyChanged("AvatarVisibility");
                OnPropertyChanged("IconText");
            }
        }

        public string ExpandButtonText
        {
            get
            {
                if (IsThread)
                    return "";
                if (IsLoadingThreads)
                    return "…";
                return IsExpanded ? "▾" : "▸";
            }
        }

        public double ExpandButtonOpacity
        {
            get { return IsThread ? 0.0 : 0.88; }
        }

        public string IconText
        {
            get
            {
                if (AvatarImage != null)
                    return "";
                return "\uE77B";
            }
        }

        public Visibility AvatarVisibility
        {
            get { return !IsThread && !string.IsNullOrWhiteSpace(AvatarUrl) ? Visibility.Visible : Visibility.Collapsed; }
        }

        public double TitleFontSize
        {
            get { return IsThread ? 16.0 : 18.0; }
        }

        public Thickness RowMargin
        {
            get { return IsThread ? new Thickness(28, 0, 0, 0) : new Thickness(0); }
        }

        private void OnPropertyChanged(string propertyName)
        {
            PropertyChangedEventHandler handler = PropertyChanged;
            if (handler != null)
                handler(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
