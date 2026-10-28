using System;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.Storage.AccessCache;

namespace _4PDA.Services
{
    public static class AppSettingsService
    {
        private const string DownloadFolderToken = "4pda_download_folder";
        private const string DownloadFolderNameKey = "DownloadFolderName";
        private const string DownloadFolderPathKey = "DownloadFolderPath";
        private const string LiveTileEnabledKey = "LiveTileEnabled";
        private const string TopicPaginationKey = "TopicPaginationEnabled";
        private const string TopicScrollJumpKey = "TopicScrollJumpEnabled";

        private static ApplicationDataContainer LocalSettings
        {
            get { return ApplicationData.Current.LocalSettings; }
        }

        public static bool IsLiveTileEnabled
        {
            get
            {
                object value;
                if (LocalSettings.Values.TryGetValue(LiveTileEnabledKey, out value) && value is bool)
                {
                    return (bool)value;
                }

                // The feature is enabled by default, but the user can disable it in SettingsPage.
                return true;
            }
            set
            {
                LocalSettings.Values[LiveTileEnabledKey] = value;
            }
        }

        public static bool UseTopicPagination
        {
            get
            {
                object value;
                return !LocalSettings.Values.TryGetValue(TopicPaginationKey, out value) || !(value is bool) || (bool)value;
            }
            set { LocalSettings.Values[TopicPaginationKey] = value; }
        }

        public static bool ShowTopicScrollJump
        {
            get
            {
                object value;
                return LocalSettings.Values.TryGetValue(TopicScrollJumpKey, out value) && value is bool && (bool)value;
            }
            set { LocalSettings.Values[TopicScrollJumpKey] = value; }
        }

        public static Task SetDownloadFolderAsync(StorageFolder folder)
        {
            if (folder == null)
            {
                ClearDownloadFolder();
                return Task.FromResult(false);
            }

            StorageApplicationPermissions.FutureAccessList.AddOrReplace(DownloadFolderToken, folder);
            LocalSettings.Values[DownloadFolderNameKey] = folder.Name ?? string.Empty;
            LocalSettings.Values[DownloadFolderPathKey] = folder.Path ?? string.Empty;
            return Task.FromResult(true);
        }

        public static async Task<StorageFolder> GetDownloadFolderAsync()
        {
            if (!StorageApplicationPermissions.FutureAccessList.ContainsItem(DownloadFolderToken))
            {
                return null;
            }

            try
            {
                return await StorageApplicationPermissions.FutureAccessList.GetFolderAsync(DownloadFolderToken);
            }
            catch
            {
                ClearDownloadFolder();
                return null;
            }
        }

        public static async Task<StorageFile> CreateDownloadFileAsync(string desiredName)
        {
            if (string.IsNullOrWhiteSpace(desiredName))
            {
                desiredName = "download";
            }

            StorageFolder customFolder = await GetDownloadFolderAsync();
            if (customFolder != null)
            {
                return await customFolder.CreateFileAsync(
                    desiredName,
                    CreationCollisionOption.GenerateUniqueName);
            }

            return await DownloadsFolder.CreateFileAsync(
                desiredName,
                CreationCollisionOption.GenerateUniqueName);
        }

        public static async Task<string> GetDownloadFolderDisplayNameAsync()
        {
            StorageFolder folder = await GetDownloadFolderAsync();
            if (folder != null)
            {
                if (!string.IsNullOrWhiteSpace(folder.Path))
                {
                    return folder.Path;
                }

                return folder.Name ?? string.Empty;
            }

            return "Загрузки (системная папка)";
        }

        public static void ClearDownloadFolder()
        {
            try
            {
                if (StorageApplicationPermissions.FutureAccessList.ContainsItem(DownloadFolderToken))
                {
                    StorageApplicationPermissions.FutureAccessList.Remove(DownloadFolderToken);
                }
            }
            catch
            {
            }

            LocalSettings.Values.Remove(DownloadFolderNameKey);
            LocalSettings.Values.Remove(DownloadFolderPathKey);
        }
    }
}
