using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using _4PDA.Models;
using _4PDA.Services;
using Windows.Storage;
using Windows.ApplicationModel;
using Windows.Storage.Pickers;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media.Imaging;
using Windows.UI.Xaml.Navigation;

namespace _4PDA
{
    public sealed partial class MessagePage : Page
    {
        private readonly FourPdaForumService _forumService = new FourPdaForumService();
        private readonly List<StorageFile> _attachments = new List<StorageFile>();
        private MessagePageParameters _parameters;
        private bool _sending;
        private bool _sent;
        private bool _preview;
        private bool _smilesLoaded;

        private sealed class SmilePickerItem
        {
            public string Code { get; set; }
            public BitmapImage ImageSource { get; set; }
        }

        public MessagePage()
        {
            InitializeComponent();
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            _parameters = e.Parameter as MessagePageParameters;

            if (!ForumAuthService.IsAuthorized)
            {
                SetUnavailable("Для ответа необходимо войти в аккаунт.");
                return;
            }

            if (_parameters == null || _parameters.TopicId <= 0)
            {
                SetUnavailable("Не удалось определить тему для ответа.");
                return;
            }

            TopicTitleTextBlock.Text = string.IsNullOrWhiteSpace(_parameters.TopicTitle)
                ? "Тема #" + _parameters.TopicId
                : _parameters.TopicTitle;

            string draft = ReadDraft();
            MessageTextBox.Text = !string.IsNullOrWhiteSpace(_parameters.InitialText)
                ? _parameters.InitialText
                : draft;

            await LoadSmilesAsync();

            if (!_parameters.CanReply && _parameters.EditPostId == 0)
            {
                SetBusy(true, "Проверяем возможность ответа…");
                try
                {
                    TopicPageData data = await _forumService.GetTopicAsync(_parameters.TopicUrl, _parameters.Start);
                    if (data != null)
                    {
                        _parameters.TopicId = data.TopicId > 0 ? data.TopicId : _parameters.TopicId;
                        _parameters.ForumId = data.ForumId > 0 ? data.ForumId : _parameters.ForumId;
                        _parameters.AuthKey = data.AuthKey;
                        _parameters.CanReply = data.CanReply;
                    }
                }
                catch (Exception ex)
                {
                    StatusTextBlock.Text = "Не удалось проверить форму ответа: " + ex.Message;
                }
                finally
                {
                    string checkStatus = StatusTextBlock.Text;
                    if (string.Equals(checkStatus, "Проверяем возможность ответа…", StringComparison.Ordinal))
                        checkStatus = string.Empty;
                    SetBusy(false, checkStatus);
                }
            }

            if (!_parameters.CanReply)
            {
                SetUnavailable("В этой теме сейчас нельзя писать сообщения.");
                return;
            }

            SendButton.IsEnabled = !string.IsNullOrWhiteSpace(MessageTextBox.Text);
            MessageTextBox.Focus(FocusState.Programmatic);
            MessageTextBox.SelectionStart = MessageTextBox.Text == null ? 0 : MessageTextBox.Text.Length;
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            if (!_sent)
                SaveDraft();
            base.OnNavigatedFrom(e);
        }

        private void SetUnavailable(string message)
        {
            StatusTextBlock.Text = message;
            MessageTextBox.IsEnabled = false;
            SendButton.IsEnabled = false;
        }

        private void MessageTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_preview)
                UpdatePreview();

            SendButton.IsEnabled = !_sending
                && _parameters != null
                && _parameters.CanReply
                && !string.IsNullOrWhiteSpace(MessageTextBox.Text);
        }

        private void FormatButton_Click(object sender, RoutedEventArgs e)
        {
            Button button = sender as Button;
            string tag = button != null ? button.Tag as string : null;
            if (string.IsNullOrWhiteSpace(tag))
                return;

            if (tag == "list")
            {
                WrapSelection("[list]\r\n[*]", "\r\n[/list]");
                return;
            }

            WrapSelection("[" + tag + "]", "[/" + tag + "]");
        }

        private void WrapSelection(string startTag, string endTag)
        {
            string text = MessageTextBox.Text ?? string.Empty;
            int start = Math.Max(0, Math.Min(MessageTextBox.SelectionStart, text.Length));
            int length = Math.Max(0, Math.Min(MessageTextBox.SelectionLength, text.Length - start));
            string selected = length > 0 ? text.Substring(start, length) : string.Empty;
            string replacement = startTag + selected + endTag;

            MessageTextBox.Text = text.Substring(0, start) + replacement + text.Substring(start + length);
            MessageTextBox.Focus(FocusState.Programmatic);
            MessageTextBox.SelectionStart = start + startTag.Length;
            MessageTextBox.SelectionLength = selected.Length;
        }

        private async Task LoadSmilesAsync()
        {
            if (_smilesLoaded)
                return;

            try
            {
                StorageFolder assetsFolder = await Package.Current.InstalledLocation.GetFolderAsync("Assets");
                StorageFolder smilesFolder = await assetsFolder.GetFolderAsync("smiles");
                IReadOnlyList<StorageFile> packageFiles = await smilesFolder.GetFilesAsync();
                List<StorageFile> gifFiles = new List<StorageFile>();

                for (int i = 0; i < packageFiles.Count; i++)
                {
                    StorageFile file = packageFiles[i];
                    if (file != null && file.Name.EndsWith(".gif", StringComparison.OrdinalIgnoreCase))
                        gifFiles.Add(file);
                }

                gifFiles.Sort(delegate(StorageFile left, StorageFile right)
                {
                    return string.Compare(left.Name, right.Name, StringComparison.OrdinalIgnoreCase);
                });

                List<SmilePickerItem> items = new List<SmilePickerItem>();
                for (int i = 0; i < gifFiles.Count; i++)
                {
                    string fileName = gifFiles[i].Name;
                    string name = fileName.Substring(0, fileName.Length - 4);
                    AddSmilePickerItem(items, ":" + name + ":", fileName);

                    // На 4PDA код :lol: использует тот же GIF, что laugh.gif.
                    if (string.Equals(name, "laugh", StringComparison.OrdinalIgnoreCase))
                        AddSmilePickerItem(items, ":lol:", fileName);
                }

                SmileGridView.ItemsSource = items;
                SmileCountTextBlock.Text = "Смайлы (" + items.Count + ")";
                _smilesLoaded = true;
            }
            catch
            {
                SmileCountTextBlock.Text = "Не удалось загрузить список смайлов";
            }
        }

        private static void AddSmilePickerItem(List<SmilePickerItem> items, string code, string fileName)
        {
            if (items == null || string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(fileName))
                return;

            items.Add(new SmilePickerItem
            {
                Code = code,
                ImageSource = new BitmapImage(new Uri("ms-appx:///Assets/smiles/" + fileName, UriKind.Absolute))
            });
        }

        private void SmileGridView_ItemClick(object sender, ItemClickEventArgs e)
        {
            SmilePickerItem item = e.ClickedItem as SmilePickerItem;
            if (item == null)
                return;

            InsertAtSelection(item.Code);
            SmileFlyout.Hide();
        }

        private void InsertAtSelection(string value)
        {
            if (string.IsNullOrEmpty(value))
                return;

            string text = MessageTextBox.Text ?? string.Empty;
            int start = Math.Max(0, Math.Min(MessageTextBox.SelectionStart, text.Length));
            int length = Math.Max(0, Math.Min(MessageTextBox.SelectionLength, text.Length - start));
            MessageTextBox.Text = text.Substring(0, start) + value + text.Substring(start + length);
            MessageTextBox.Focus(FocusState.Programmatic);
            MessageTextBox.SelectionStart = start + value.Length;
            MessageTextBox.SelectionLength = 0;
        }

        private async void AttachmentButton_Click(object sender, RoutedEventArgs e)
        {
            FileOpenPicker picker = new FileOpenPicker();
            picker.ViewMode = PickerViewMode.List;
            picker.SuggestedStartLocation = PickerLocationId.Downloads;
            picker.FileTypeFilter.Add("*");

            IReadOnlyList<StorageFile> files = await picker.PickMultipleFilesAsync();
            if (files == null)
                return;

            for (int i = 0; i < files.Count; i++)
            {
                StorageFile file = files[i];
                bool exists = false;
                for (int j = 0; j < _attachments.Count; j++)
                {
                    if (string.Equals(_attachments[j].Path, file.Path, StringComparison.OrdinalIgnoreCase))
                    {
                        exists = true;
                        break;
                    }
                }

                if (!exists)
                    _attachments.Add(file);
            }

            RefreshAttachments();
        }

        private void RefreshAttachments()
        {
            AttachmentsPanel.Children.Clear();
            for (int i = 0; i < _attachments.Count; i++)
            {
                StorageFile file = _attachments[i];
                Grid row = new Grid();
                row.Margin = new Thickness(0, 0, 0, 4);
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                TextBlock name = new TextBlock();
                name.Text = file.Name;
                name.TextTrimming = TextTrimming.CharacterEllipsis;
                name.VerticalAlignment = VerticalAlignment.Center;
                Grid.SetColumn(name, 0);
                row.Children.Add(name);

                Button remove = new Button();
                remove.Content = "×";
                remove.Tag = file;
                remove.MinWidth = 34;
                remove.Padding = new Thickness(7, 2, 7, 3);
                remove.Click += RemoveAttachment_Click;
                Grid.SetColumn(remove, 1);
                row.Children.Add(remove);
                AttachmentsPanel.Children.Add(row);
            }

            bool hasFiles = _attachments.Count > 0;
            AttachmentsPanel.Visibility = hasFiles ? Visibility.Visible : Visibility.Collapsed;
            AttachmentCountTextBlock.Text = _attachments.Count.ToString();
            AttachmentCountTextBlock.Visibility = hasFiles ? Visibility.Visible : Visibility.Collapsed;
        }

        private void RemoveAttachment_Click(object sender, RoutedEventArgs e)
        {
            Button button = sender as Button;
            StorageFile file = button != null ? button.Tag as StorageFile : null;
            if (file != null)
                _attachments.Remove(file);
            RefreshAttachments();
        }

        private void PreviewButton_Click(object sender, RoutedEventArgs e)
        {
            _preview = !_preview;
            MessageTextBox.Visibility = _preview ? Visibility.Collapsed : Visibility.Visible;
            PreviewScrollViewer.Visibility = _preview ? Visibility.Visible : Visibility.Collapsed;
            if (_preview)
                UpdatePreview();
            else
                MessageTextBox.Focus(FocusState.Programmatic);
        }

        private void UpdatePreview()
        {
            string text = MessageTextBox.Text ?? string.Empty;
            text = Regex.Replace(text, "\\[br\\]", "\r\n", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, "\\[(?:/?(?:b|i|u|s|quote|code|spoiler|url|img|list)|\\*|url=[^\\]]+|color=[^\\]]+|size=[^\\]]+)\\]", string.Empty, RegexOptions.IgnoreCase);
            PreviewTextBlock.Text = text;
        }

        private async void SendButton_Click(object sender, RoutedEventArgs e)
        {
            if (_sending || _parameters == null)
                return;

            SetBusy(true, "Отправляем сообщение…");
            try
            {
                int postedPostId = await _forumService.SendTopicMessageAsync(_parameters, MessageTextBox.Text, _attachments);
                _sent = true;
                DeleteDraft();
                AppNavigationActions.RequestTopicRefresh(_parameters.TopicId, postedPostId);

                ContentDialog dialog = new ContentDialog();
                dialog.Content = "Сообщение отправлено.";
                dialog.PrimaryButtonText = "ОК";
                await dialog.ShowAsync();

                if (Frame != null && Frame.CanGoBack)
                    Frame.GoBack();
            }
            catch (Exception ex)
            {
                StatusTextBlock.Text = "Не удалось отправить: " + ex.Message;
            }
            finally
            {
                SetBusy(false, StatusTextBlock.Text);
            }
        }

        private void SetBusy(bool busy, string message)
        {
            _sending = busy;
            SendButton.Visibility = busy ? Visibility.Collapsed : Visibility.Visible;
            SendProgressRing.IsActive = busy;
            SendProgressRing.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
            MessageTextBox.IsEnabled = !busy;
            StatusTextBlock.Text = message ?? string.Empty;
            SendButton.IsEnabled = !busy && !string.IsNullOrWhiteSpace(MessageTextBox.Text);
        }

        private string DraftKey
        {
            get { return _parameters == null ? string.Empty : "TopicMessageDraft_" + _parameters.TopicId + "_" + _parameters.EditPostId; }
        }

        private string ReadDraft()
        {
            string key = DraftKey;
            if (string.IsNullOrWhiteSpace(key))
                return string.Empty;

            object value;
            return ApplicationData.Current.LocalSettings.Values.TryGetValue(key, out value)
                ? value as string ?? string.Empty
                : string.Empty;
        }

        private void SaveDraft()
        {
            string key = DraftKey;
            if (string.IsNullOrWhiteSpace(key))
                return;

            string text = MessageTextBox.Text ?? string.Empty;
            if (string.IsNullOrWhiteSpace(text))
                ApplicationData.Current.LocalSettings.Values.Remove(key);
            else
                ApplicationData.Current.LocalSettings.Values[key] = text;
        }

        private void DeleteDraft()
        {
            string key = DraftKey;
            if (!string.IsNullOrWhiteSpace(key))
                ApplicationData.Current.LocalSettings.Values.Remove(key);
        }
    }
}
