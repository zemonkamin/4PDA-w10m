using System;
using _4PDA.Services;
using Windows.ApplicationModel;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.UI.Popups;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;

namespace _4PDA
{
    public sealed partial class SettingsPage : Page
    {
        private bool _updatingControls;

        public SettingsPage()
        {
            InitializeComponent();

            PackageVersion version = Package.Current.Id.Version;
            ApplicationVersionTextBlock.Text = $"Версия приложения: {version.Major}.{version.Minor}.{version.Build}.{version.Revision}";
        }

        protected async override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            _updatingControls = true;
            LiveTileToggleSwitch.IsOn = AppSettingsService.IsLiveTileEnabled;
            TopicReadingModeComboBox.SelectedIndex = AppSettingsService.UseTopicPagination ? 0 : 1;
            TopicScrollJumpToggleSwitch.IsOn = AppSettingsService.ShowTopicScrollJump;
            _updatingControls = false;
            await RefreshDownloadFolderTextAsync();
        }

        private async void SelectDownloadFolderButton_Click(object sender, RoutedEventArgs e)
        {
            FolderPicker picker = new FolderPicker();
            picker.SuggestedStartLocation = PickerLocationId.Downloads;
            picker.FileTypeFilter.Add("*");

            StorageFolder folder = await picker.PickSingleFolderAsync();
            if (folder == null)
            {
                return;
            }

            try
            {
                await AppSettingsService.SetDownloadFolderAsync(folder);
                await RefreshDownloadFolderTextAsync();
                ShowStatus("Папка для загрузок сохранена.");
            }
            catch (Exception ex)
            {
                await new MessageDialog(
                    string.IsNullOrWhiteSpace(ex.Message) ? "Не удалось сохранить выбранную папку." : ex.Message,
                    "Ошибка").ShowAsync();
            }
        }

        private async void LiveTileToggleSwitch_Toggled(object sender, RoutedEventArgs e)
        {
            if (_updatingControls)
            {
                return;
            }

            bool enabled = LiveTileToggleSwitch.IsOn;
            AppSettingsService.IsLiveTileEnabled = enabled;

            if (enabled)
            {
                ShowStatus("Обновляем живую плитку...");
                await LiveTileService.RefreshFromSavedNewsAsync();
                ShowStatus("Живая плитка включена.");
            }
            else
            {
                LiveTileService.ClearTile();
                ShowStatus("Живая плитка выключена.");
            }
        }

        private void TopicReadingModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_updatingControls || TopicReadingModeComboBox == null || TopicReadingModeComboBox.SelectedIndex < 0) return;
            AppSettingsService.UseTopicPagination = TopicReadingModeComboBox.SelectedIndex == 0;
            ShowStatus("Способ чтения сохранён.");
        }

        private void TopicScrollJumpToggleSwitch_Toggled(object sender, RoutedEventArgs e)
        {
            if (_updatingControls || TopicScrollJumpToggleSwitch == null) return;
            AppSettingsService.ShowTopicScrollJump = TopicScrollJumpToggleSwitch.IsOn;
            ShowStatus("Настройка кнопки прокрутки сохранена.");
        }

        private async System.Threading.Tasks.Task RefreshDownloadFolderTextAsync()
        {
            string folder = await AppSettingsService.GetDownloadFolderDisplayNameAsync();
            DownloadFolderTextBlock.Text = string.IsNullOrWhiteSpace(folder)
                ? "Загрузки (системная папка)"
                : folder;
        }

        private void ShowStatus(string text)
        {
            SettingsStatusTextBlock.Text = text ?? string.Empty;
            SettingsStatusTextBlock.Visibility = string.IsNullOrWhiteSpace(text)
                ? Visibility.Collapsed
                : Visibility.Visible;
        }
    }
}
