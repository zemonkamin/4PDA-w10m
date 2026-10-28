using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media.Imaging;
using Windows.UI.Xaml.Navigation;

namespace _4PDA
{
    public sealed partial class ChatPage : Page
    {
        private List<QmsMessage> _messages = new List<QmsMessage>();
        private readonly QmsService _qmsService = new QmsService();
        private QmsThreadNavigationArgs _args;
        private bool _loading;

        public ChatPage()
        {
            InitializeComponent();
            NavigationCacheMode = NavigationCacheMode.Enabled;
            MessagesListView.ItemsSource = _messages;
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            ForumAuthService.AccountChanged -= ForumAuthService_AccountChanged;
            ForumAuthService.AccountChanged += ForumAuthService_AccountChanged;

            if (!ForumAuthService.IsAuthorized)
            {
                ShowStatus("Войдите в аккаунт 4PDA, чтобы открыть чат.");
                SetComposerEnabled(false);
                return;
            }

            QmsThreadNavigationArgs newArgs = e.Parameter as QmsThreadNavigationArgs;
            if (newArgs == null || String.IsNullOrWhiteSpace(newArgs.ContactId) || String.IsNullOrWhiteSpace(newArgs.ThreadId))
            {
                ShowStatus("Чат не выбран.");
                SetComposerEnabled(false);
                return;
            }

            bool sameThread = _args != null &&
                String.Equals(_args.ContactId, newArgs.ContactId, StringComparison.Ordinal) &&
                String.Equals(_args.ThreadId, newArgs.ThreadId, StringComparison.Ordinal);
            _args = newArgs;

            if (sameThread && _messages.Count > 0)
            {
                ShowStatus("");
                EmptyTextBlock.Visibility = Visibility.Collapsed;
                SetComposerEnabled(true);
                ScrollToLastMessage();
                return;
            }

            List<QmsMessage> cachedMessages;
            if (_qmsService.TryGetCachedMessages(_args.ContactId, _args.ThreadId, out cachedMessages))
            {
                ReplaceMessages(cachedMessages);
                ShowStatus("");
                EmptyTextBlock.Visibility = Visibility.Collapsed;
                SetComposerEnabled(true);
                ScrollToLastMessage();
                return;
            }

            ReplaceMessages(new List<QmsMessage>());
            await LoadMessagesAsync();
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            LegacyProjects.UI.UniversalWindowsPlatform.Controls.PhotoViewer.TryCloseCurrent();
            ForumAuthService.AccountChanged -= ForumAuthService_AccountChanged;
            base.OnNavigatedFrom(e);
        }

        private async void ChatPhoto_Tapped(object sender, Windows.UI.Xaml.Input.TappedRoutedEventArgs e)
        {
            Image image = sender as Image;
            QmsMessageImage selected = image == null ? null : image.DataContext as QmsMessageImage;
            if (selected == null) return;
            e.Handled = true;
            var photos = new List<LegacyProjects.UI.UniversalWindowsPlatform.Models.PhotoViewerItem>();
            int index = 0;
            foreach (QmsMessage message in _messages)
                foreach (QmsMessageImage photo in message.Images)
                {
                    if (ReferenceEquals(photo, selected)) index = photos.Count;
                    photos.Add(new LegacyProjects.UI.UniversalWindowsPlatform.Models.PhotoViewerItem
                    { Url = photo.Url, Preview = ReferenceEquals(photo, selected) ? image.Source : null });
                }
            await Services.AppPhotoViewer.ShowAsync(photos, index, _args == null ? "Чат" : _args.ThreadTitle);
        }

        private async void ForumAuthService_AccountChanged(object sender, EventArgs e)
        {
            await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, delegate
            {
                if (!ForumAuthService.IsAuthorized)
                {
                    ReplaceMessages(new List<QmsMessage>());
                    ShowEmpty("Войдите в аккаунт 4PDA, чтобы открыть чат.");
                    SetComposerEnabled(false);
                }
            });
        }

        private async Task LoadMessagesAsync()
        {
            if (_loading || _args == null)
                return;

            _loading = true;
            SetBusy(true);
            ShowStatus("");
            EmptyTextBlock.Visibility = Visibility.Collapsed;

            try
            {
                List<QmsMessage> items = await _qmsService.GetMessagesAsync(
                    _args.ContactId,
                    _args.ThreadId,
                    _args.ContactNick,
                    _args.ThreadTitle,
                    _args.AvatarUrl);

                ReplaceMessages(items);

                if (_messages.Count == 0)
                    ShowEmpty("Сообщения в этом чате не найдены.");
                else
                    ScrollToLastMessage();
            }
            catch (UnauthorizedAccessException)
            {
                ReplaceMessages(new List<QmsMessage>());
                ShowEmpty("Войдите в аккаунт 4PDA, чтобы открыть чат.");
                SetComposerEnabled(false);
            }
            catch (Exception ex)
            {
                if (_messages.Count == 0)
                    ShowEmpty("Не удалось загрузить чат.");
                ShowStatus(ex.Message);
            }
            finally
            {
                SetBusy(false);
                _loading = false;
            }
        }

        private async void SendButton_Click(object sender, RoutedEventArgs e)
        {
            if (_args == null || _loading || !ForumAuthService.IsAuthorized)
                return;

            string text = MessageTextBox.Text == null ? "" : MessageTextBox.Text.Trim();
            if (String.IsNullOrWhiteSpace(text))
                return;

            _loading = true;
            SetBusy(true);
            ShowStatus("Отправка сообщения...");
            bool sent = false;

            try
            {
                await _qmsService.SendMessageAsync(_args.ContactId, _args.ThreadId, text);
                MessageTextBox.Text = "";
                ShowStatus("");
                sent = true;
            }
            catch (Exception ex)
            {
                ShowStatus("Не удалось отправить сообщение: " + ex.Message);
            }
            finally
            {
                SetBusy(false);
                _loading = false;
            }

            if (sent)
                await LoadMessagesAsync();
        }

        private void ReplaceMessages(List<QmsMessage> messages)
        {
            List<QmsMessage> prepared = new List<QmsMessage>();
            bool mobile = QmsService.SuppressDirectNetworkImages;
            if (messages != null)
            {
                foreach (QmsMessage message in messages)
                {
                    if (message == null)
                        continue;
                    message.ApplyViewState();
                    prepared.Add(message);
                }
            }

            if (mobile && prepared.Count > 80)
                prepared.RemoveRange(0, prepared.Count - 80);

            _messages = prepared;
            MessagesListView.ItemsSource = _messages;
        }

        private async void ScrollToLastMessage()
        {
            if (_messages.Count == 0)
                return;

            await Dispatcher.RunAsync(CoreDispatcherPriority.Low, delegate
            {
                try
                {
                    MessagesListView.ScrollIntoView(_messages[_messages.Count - 1]);
                }
                catch
                {
                }
            });
        }

        private void SetBusy(bool busy)
        {
            ChatProgressRing.IsActive = busy;
            MessagesListView.IsEnabled = true;
            SetComposerEnabled(!busy && ForumAuthService.IsAuthorized && _args != null);
        }

        private void SetComposerEnabled(bool enabled)
        {
            MessageTextBox.IsEnabled = enabled;
            SendButton.IsEnabled = enabled;
        }

        private void ShowEmpty(string text)
        {
            EmptyTextBlock.Text = text ?? "";
            EmptyTextBlock.Visibility = Visibility.Visible;
        }

        private void ShowStatus(string text)
        {
            ChatStatusTextBlock.Text = text ?? "";
            ChatStatusTextBlock.Visibility = String.IsNullOrWhiteSpace(ChatStatusTextBlock.Text)
                ? Visibility.Collapsed
                : Visibility.Visible;
        }
    }
}
