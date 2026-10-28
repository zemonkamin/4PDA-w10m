using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using _4PDA.Models;
using Windows.Data.Json;
using Windows.Data.Xml.Dom;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;
using Windows.UI.Notifications;
using Windows.Web.Http;

namespace _4PDA.Services
{
    public static class LiveTileService
    {
        private const string DataFileName = "live_tile_news.json";
        private const string ImageFolderName = "LiveTile";
        private const int ImmediateQueueSize = 5;

        private static readonly HttpClient ImageClient = new HttpClient();
        private static readonly SemaphoreSlim UpdateGate = new SemaphoreSlim(1, 1);
        private static int _tileImageWorkGeneration;

        static LiveTileService()
        {
            _4PDA.ForumCookieHelper.ApplyDefaultHeaders(ImageClient);
        }

        public static async Task SaveNewsSnapshotAsync(IEnumerable<NewsItem> newsItems)
        {
            List<NewsItem> items = CreateNewsReferenceList(newsItems);
            await SaveNewsCoreAsync(items, false);
            QueueTileImageWork(items);
        }

        public static async Task SaveNewsAndRefreshAsync(IEnumerable<NewsItem> newsItems)
        {
            Interlocked.Increment(ref _tileImageWorkGeneration);
            await SaveNewsCoreAsync(CreateNewsReferenceList(newsItems), true);
        }

        private static async Task SaveNewsCoreAsync(IEnumerable<NewsItem> newsItems, bool cacheImages)
        {
            await UpdateGate.WaitAsync();
            try
            {
                List<LiveTileNewsItem> items = CreateStoredItems(newsItems);
                List<LiveTileNewsItem> previousItems = await LoadItemsAsync();
                PreserveCachedImageState(items, previousItems);

                if (cacheImages && AppSettingsService.IsLiveTileEnabled)
                {
                    await CacheImagesAsync(items, false);
                }

                await SaveItemsAsync(items);

                if (AppSettingsService.IsLiveTileEnabled)
                {
                    ApplyTileNotifications(items);
                }
                else
                {
                    ClearTile();
                }
            }
            finally
            {
                UpdateGate.Release();
            }
        }

        public static async Task RefreshFromSavedNewsAsync()
        {
            List<NewsItem> backgroundItems = null;
            await UpdateGate.WaitAsync();
            try
            {
                if (!AppSettingsService.IsLiveTileEnabled)
                {
                    ClearTile();
                    return;
                }

                List<LiveTileNewsItem> items = await LoadItemsAsync();
                if (items.Count == 0)
                {
                    ClearTile();
                    return;
                }

                bool changed = await CacheImagesAsync(items, false);
                if (changed)
                {
                    await SaveItemsAsync(items);
                }

                ApplyTileNotifications(items);
                backgroundItems = ConvertStoredItems(items);
            }
            finally
            {
                UpdateGate.Release();
            }

            if (backgroundItems != null && backgroundItems.Count > 0)
                QueueTileImageWork(backgroundItems);
        }

        private static List<NewsItem> CreateNewsReferenceList(IEnumerable<NewsItem> source)
        {
            List<NewsItem> result = new List<NewsItem>();
            if (source == null)
                return result;

            foreach (NewsItem item in source)
            {
                if (item != null && !String.IsNullOrWhiteSpace(item.Title))
                    result.Add(item);
            }

            return result;
        }

        private static List<NewsItem> ConvertStoredItems(List<LiveTileNewsItem> source)
        {
            List<NewsItem> result = new List<NewsItem>();
            if (source == null)
                return result;

            for (int i = 0; i < source.Count; i++)
            {
                LiveTileNewsItem stored = source[i];
                if (stored == null || String.IsNullOrWhiteSpace(stored.Title))
                    continue;

                NewsItem item = new NewsItem();
                item.Id = stored.Id;
                item.Title = stored.Title;
                item.Url = stored.Url;
                item.ImageUrl = stored.ImageUrl;
                result.Add(item);
            }

            return result;
        }

        private static void PreserveCachedImageState(List<LiveTileNewsItem> items, List<LiveTileNewsItem> previousItems)
        {
            if (items == null || previousItems == null)
                return;

            for (int i = 0; i < items.Count; i++)
            {
                LiveTileNewsItem current = items[i];
                if (current == null)
                    continue;

                LiveTileNewsItem previous = null;
                for (int j = 0; j < previousItems.Count; j++)
                {
                    if (IsSameNewsItem(current, previousItems[j]))
                    {
                        previous = previousItems[j];
                        break;
                    }
                }

                if (previous == null)
                    continue;

                if (String.IsNullOrWhiteSpace(current.ImageUrl))
                    current.ImageUrl = previous.ImageUrl ?? String.Empty;

                if (!String.IsNullOrWhiteSpace(previous.LocalImageUri)
                    && String.Equals(current.ImageUrl ?? String.Empty, previous.ImageUrl ?? String.Empty, StringComparison.OrdinalIgnoreCase))
                {
                    current.LocalImageUri = previous.LocalImageUri;
                }
            }
        }

        private static bool IsSameNewsItem(LiveTileNewsItem first, LiveTileNewsItem second)
        {
            if (first == null || second == null)
                return false;

            if (first.Id > 0 && second.Id > 0)
                return first.Id == second.Id;

            return !String.IsNullOrWhiteSpace(first.Url)
                && String.Equals(first.Url, second.Url, StringComparison.OrdinalIgnoreCase);
        }

        private static async void QueueTileImageWork(List<NewsItem> items)
        {
            if (items == null || items.Count == 0 || !AppSettingsService.IsLiveTileEnabled)
                return;

            int generation = Interlocked.Increment(ref _tileImageWorkGeneration);
            try { await Task.Run(() => RunTileImageWorkAsync(items, generation)); }
            catch { }
        }

        private static async Task RunTileImageWorkAsync(List<NewsItem> items, int generation)
        {
            try
            {
                if (generation != _tileImageWorkGeneration)
                    return;

                // Сначала локально кэшируем те изображения, которые уже пришли
                // в ленте. Плитка обновляется независимо от страницы новостей.
                if (AppSettingsService.IsLiveTileEnabled)
                    await SaveNewsCoreAsync(items, true);
                if (generation != _tileImageWorkGeneration)
                    return;

                FourPdaNewsService newsService = new FourPdaNewsService();
                List<Task> batch = new List<Task>();
                bool batchHasMissingImages = false;

                for (int i = 0; i < items.Count; i++)
                {
                    NewsItem item = items[i];
                    if (item == null || String.IsNullOrWhiteSpace(item.Url) || !String.IsNullOrWhiteSpace(item.ImageUrl))
                        continue;

                    batch.Add(EnrichNewsItemQuietlyAsync(newsService, item));
                    batchHasMissingImages = true;
                    if (batch.Count < 3)
                        continue;

                    await Task.WhenAll(batch);
                    batch.Clear();
                    if (generation != _tileImageWorkGeneration)
                        return;

                    if (AppSettingsService.IsLiveTileEnabled)
                        await SaveNewsCoreAsync(items, true);
                }

                if (batch.Count > 0)
                {
                    await Task.WhenAll(batch);
                    if (generation != _tileImageWorkGeneration)
                        return;
                    if (AppSettingsService.IsLiveTileEnabled)
                        await SaveNewsCoreAsync(items, true);
                }
                else if (batchHasMissingImages && generation == _tileImageWorkGeneration)
                {
                    // Последняя полная тройка уже была сохранена внутри цикла.
                }
            }
            catch
            {
                // Плитка остаётся рабочей с уже загруженными изображениями и текстом.
            }
        }

        private static async Task EnrichNewsItemQuietlyAsync(FourPdaNewsService service, NewsItem item)
        {
            if (service == null || item == null)
                return;

            try
            {
                NewsItem enriched = new NewsItem();
                enriched.Id = item.Id;
                enriched.Url = item.Url;
                enriched.Title = item.Title;
                enriched.Description = item.Description;
                enriched.Date = item.Date;
                enriched.Author = item.Author;
                enriched.ImageUrl = item.ImageUrl;
                enriched.Html = item.Html;
                enriched.CommentsCount = item.CommentsCount;

                await Task.Run(async delegate
                {
                    await service.EnrichNewsItemAsync(enriched);
                });

                if (String.IsNullOrWhiteSpace(item.ImageUrl))
                    item.ImageUrl = enriched.ImageUrl;
                if (String.IsNullOrWhiteSpace(item.Html))
                    item.Html = enriched.Html;
                if (String.IsNullOrWhiteSpace(item.Author))
                    item.Author = enriched.Author;
                if (item.CommentsCount == 0)
                    item.CommentsCount = enriched.CommentsCount;
            }
            catch
            {
            }
        }

        public static void ClearTile()
        {
            try
            {
                TileUpdater updater = TileUpdateManager.CreateTileUpdaterForApplication();
                RemoveScheduledNotifications(updater);
                updater.EnableNotificationQueue(false);
                updater.Clear();
            }
            catch
            {
            }
        }

        private static List<LiveTileNewsItem> CreateStoredItems(IEnumerable<NewsItem> source)
        {
            List<LiveTileNewsItem> result = new List<LiveTileNewsItem>();
            if (source == null)
            {
                return result;
            }

            foreach (NewsItem item in source)
            {
                if (item == null || string.IsNullOrWhiteSpace(item.Title))
                {
                    continue;
                }

                LiveTileNewsItem stored = new LiveTileNewsItem();
                stored.Id = item.Id;
                stored.Title = item.Title.Trim();
                stored.Url = item.Url ?? string.Empty;
                stored.ImageUrl = item.ImageUrl ?? string.Empty;
                stored.LocalImageUri = string.Empty;
                result.Add(stored);
            }

            return result;
        }

        private static async Task<bool> CacheImagesAsync(List<LiveTileNewsItem> items, bool clearExisting)
        {
            if (items == null || items.Count == 0)
                return false;

            StorageFolder folder = await ApplicationData.Current.LocalFolder.CreateFolderAsync(
                ImageFolderName,
                CreationCollisionOption.OpenIfExists);

            if (clearExisting)
            {
                IReadOnlyList<StorageFile> files = await folder.GetFilesAsync();
                for (int i = 0; i < files.Count; i++)
                {
                    try
                    {
                        await files[i].DeleteAsync(StorageDeleteOption.PermanentDelete);
                    }
                    catch
                    {
                    }
                }
            }

            bool changed = false;
            List<Task<bool>> batch = new List<Task<bool>>();
            for (int i = 0; i < items.Count; i++)
            {
                LiveTileNewsItem item = items[i];
                if (item == null || String.IsNullOrWhiteSpace(item.ImageUrl))
                    continue;

                if (!clearExisting && !String.IsNullOrWhiteSpace(item.LocalImageUri))
                    continue;

                batch.Add(CacheImageAsync(folder, item, i));
                if (batch.Count < 3)
                    continue;

                bool[] results = await Task.WhenAll(batch);
                for (int j = 0; j < results.Length; j++)
                    changed = changed || results[j];
                batch.Clear();
            }

            if (batch.Count > 0)
            {
                bool[] results = await Task.WhenAll(batch);
                for (int i = 0; i < results.Length; i++)
                    changed = changed || results[i];
            }

            return changed;
        }

        private static async Task<bool> CacheImageAsync(StorageFolder folder, LiveTileNewsItem item, int index)
        {
            try
            {
                Uri imageUri;
                if (folder == null || item == null || !Uri.TryCreate(item.ImageUrl, UriKind.Absolute, out imageUri))
                    return false;

                HttpResponseMessage response = await ImageClient.GetAsync(imageUri);
                try
                {
                    if (!response.IsSuccessStatusCode)
                        return false;

                    IBuffer sourceBuffer = await response.Content.ReadAsBufferAsync();
                    if (sourceBuffer == null || sourceBuffer.Length == 0)
                        return false;

                    string baseFileName = "news_" + index.ToString() + "_" + GetStableId(item, index);
                    StorageFile file = await folder.CreateFileAsync(baseFileName + ".jpg", CreationCollisionOption.ReplaceExisting);
                    if (!await WriteTileImageAsync(file, sourceBuffer))
                    {
                        try
                        {
                            await file.DeleteAsync(StorageDeleteOption.PermanentDelete);
                        }
                        catch
                        {
                        }

                        string extension = GetImageExtension(response, imageUri);
                        file = await folder.CreateFileAsync(baseFileName + extension, CreationCollisionOption.ReplaceExisting);
                        await FileIO.WriteBufferAsync(file, sourceBuffer);
                    }

                    item.LocalImageUri = "ms-appdata:///local/" + ImageFolderName + "/" + file.Name;
                    return true;
                }
                finally
                {
                    response.Dispose();
                }
            }
            catch
            {
                return false;
            }
        }

        private static async Task<bool> WriteTileImageAsync(StorageFile file, IBuffer sourceBuffer)
        {
            if (file == null || sourceBuffer == null || sourceBuffer.Length == 0)
                return false;

            try
            {
                using (InMemoryRandomAccessStream sourceStream = new InMemoryRandomAccessStream())
                {
                    await sourceStream.WriteAsync(sourceBuffer);
                    sourceStream.Seek(0);
                    BitmapDecoder decoder = await BitmapDecoder.CreateAsync(sourceStream);

                    uint sourceWidth = decoder.PixelWidth;
                    uint sourceHeight = decoder.PixelHeight;
                    if (sourceWidth == 0 || sourceHeight == 0)
                        return false;

                    const uint maximumDimension = 512;
                    double scale = Math.Min(1.0, Math.Min(
                        maximumDimension / (double)sourceWidth,
                        maximumDimension / (double)sourceHeight));
                    uint targetWidth = Math.Max(1, (uint)Math.Round(sourceWidth * scale));
                    uint targetHeight = Math.Max(1, (uint)Math.Round(sourceHeight * scale));

                    BitmapTransform transform = new BitmapTransform();
                    transform.ScaledWidth = targetWidth;
                    transform.ScaledHeight = targetHeight;
                    transform.InterpolationMode = BitmapInterpolationMode.Fant;

                    PixelDataProvider pixels = await decoder.GetPixelDataAsync(
                        BitmapPixelFormat.Bgra8,
                        BitmapAlphaMode.Ignore,
                        transform,
                        ExifOrientationMode.RespectExifOrientation,
                        ColorManagementMode.DoNotColorManage);

                    using (IRandomAccessStream output = await file.OpenAsync(FileAccessMode.ReadWrite))
                    {
                        output.Size = 0;
                        BitmapEncoder encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.JpegEncoderId, output);
                        encoder.SetPixelData(
                            BitmapPixelFormat.Bgra8,
                            BitmapAlphaMode.Ignore,
                            targetWidth,
                            targetHeight,
                            96,
                            96,
                            pixels.DetachPixelData());
                        await encoder.FlushAsync();
                    }
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        private static string GetStableId(LiveTileNewsItem item, int index)
        {
            if (item != null && item.Id > 0)
            {
                return item.Id.ToString();
            }

            unchecked
            {
                int hash = item == null || item.Url == null ? index : item.Url.GetHashCode();
                if (hash == int.MinValue)
                {
                    hash = 0;
                }

                return Math.Abs(hash).ToString();
            }
        }

        private static string GetImageExtension(HttpResponseMessage response, Uri uri)
        {
            string mediaType = string.Empty;
            if (response != null && response.Content != null && response.Content.Headers.ContentType != null)
            {
                mediaType = response.Content.Headers.ContentType.MediaType ?? string.Empty;
            }

            mediaType = mediaType.ToLowerInvariant();
            if (mediaType.IndexOf("png", StringComparison.Ordinal) >= 0)
            {
                return ".png";
            }

            if (mediaType.IndexOf("gif", StringComparison.Ordinal) >= 0)
            {
                return ".gif";
            }

            if (mediaType.IndexOf("jpeg", StringComparison.Ordinal) >= 0 || mediaType.IndexOf("jpg", StringComparison.Ordinal) >= 0)
            {
                return ".jpg";
            }

            string extension = uri == null ? string.Empty : Path.GetExtension(uri.AbsolutePath);
            if (string.Equals(extension, ".png", StringComparison.OrdinalIgnoreCase)
                || string.Equals(extension, ".gif", StringComparison.OrdinalIgnoreCase)
                || string.Equals(extension, ".jpg", StringComparison.OrdinalIgnoreCase)
                || string.Equals(extension, ".jpeg", StringComparison.OrdinalIgnoreCase))
            {
                return extension.ToLowerInvariant();
            }

            return ".jpg";
        }

        private static async Task SaveItemsAsync(List<LiveTileNewsItem> items)
        {
            JsonArray array = new JsonArray();
            if (items != null)
            {
                for (int i = 0; i < items.Count; i++)
                {
                    LiveTileNewsItem item = items[i];
                    if (item == null)
                    {
                        continue;
                    }

                    JsonObject json = new JsonObject();
                    json["id"] = JsonValue.CreateNumberValue(item.Id);
                    json["title"] = JsonValue.CreateStringValue(item.Title ?? string.Empty);
                    json["url"] = JsonValue.CreateStringValue(item.Url ?? string.Empty);
                    json["imageUrl"] = JsonValue.CreateStringValue(item.ImageUrl ?? string.Empty);
                    json["localImageUri"] = JsonValue.CreateStringValue(item.LocalImageUri ?? string.Empty);
                    array.Add(json);
                }
            }

            StorageFile file = await ApplicationData.Current.LocalFolder.CreateFileAsync(
                DataFileName,
                CreationCollisionOption.ReplaceExisting);
            await FileIO.WriteTextAsync(file, array.Stringify());
        }

        private static async Task<List<LiveTileNewsItem>> LoadItemsAsync()
        {
            List<LiveTileNewsItem> result = new List<LiveTileNewsItem>();
            try
            {
                StorageFile file = await ApplicationData.Current.LocalFolder.GetFileAsync(DataFileName);
                string text = await FileIO.ReadTextAsync(file);
                JsonArray array = JsonArray.Parse(text);
                for (int i = 0; i < array.Count; i++)
                {
                    JsonObject json = array[i].GetObject();
                    LiveTileNewsItem item = new LiveTileNewsItem();
                    item.Id = GetJsonInt(json, "id");
                    item.Title = GetJsonString(json, "title");
                    item.Url = GetJsonString(json, "url");
                    item.ImageUrl = GetJsonString(json, "imageUrl");
                    item.LocalImageUri = GetJsonString(json, "localImageUri");
                    if (!string.IsNullOrWhiteSpace(item.Title))
                    {
                        result.Add(item);
                    }
                }
            }
            catch
            {
            }

            return result;
        }

        private static int GetJsonInt(JsonObject json, string key)
        {
            if (json == null || !json.ContainsKey(key))
            {
                return 0;
            }

            try
            {
                return (int)json[key].GetNumber();
            }
            catch
            {
                return 0;
            }
        }

        private static string GetJsonString(JsonObject json, string key)
        {
            if (json == null || !json.ContainsKey(key))
            {
                return string.Empty;
            }

            try
            {
                return json[key].GetString() ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static void ApplyTileNotifications(List<LiveTileNewsItem> items)
        {
            try
            {
                TileUpdater updater = TileUpdateManager.CreateTileUpdaterForApplication();
                RemoveScheduledNotifications(updater);
                updater.Clear();
                updater.EnableNotificationQueue(true);

                int immediateCount = Math.Min(ImmediateQueueSize, items.Count);
                for (int i = 0; i < immediateCount; i++)
                {
                    TileNotification notification = CreateNotification(items[i], i);
                    notification.ExpirationTime = DateTimeOffset.Now.AddDays(7);
                    updater.Update(notification);
                }

                // Windows keeps at most five notifications in the live-tile queue.
                // The remaining saved news are scheduled in sequence so they also
                // enter the rotating queue instead of being discarded permanently.
                DateTimeOffset deliveryTime = DateTimeOffset.Now.AddMinutes(2);
                for (int i = ImmediateQueueSize; i < items.Count; i++)
                {
                    XmlDocument xml = CreateTileXml(items[i]);
                    ScheduledTileNotification scheduled = new ScheduledTileNotification(xml, deliveryTime);
                    scheduled.Id = "news" + i.ToString();
                    scheduled.Tag = "n" + i.ToString();
                    scheduled.ExpirationTime = deliveryTime.AddDays(7);
                    updater.AddToSchedule(scheduled);
                    deliveryTime = deliveryTime.AddMinutes(2);
                }
            }
            catch
            {
            }
        }

        private static TileNotification CreateNotification(LiveTileNewsItem item, int index)
        {
            TileNotification notification = new TileNotification(CreateTileXml(item));
            notification.Tag = "n" + index.ToString();
            return notification;
        }

        private static XmlDocument CreateTileXml(LiveTileNewsItem item)
        {
            string title = TrimTitle(item == null ? string.Empty : item.Title);
            string imageSource = string.Empty;
            if (item != null)
            {
                imageSource = !string.IsNullOrWhiteSpace(item.LocalImageUri)
                    ? item.LocalImageUri
                    : item.ImageUrl;
            }

            string image = string.IsNullOrWhiteSpace(imageSource)
                ? string.Empty
                : "<image placement=\"background\" hint-overlay=\"65\" src=\"" + EscapeXml(imageSource) + "\"/>";

            string text = "<text hint-style=\"base\" hint-wrap=\"true\" hint-maxLines=\"2\" hint-align=\"center\" lang=\"ru-RU\">"
                + EscapeXml(title)
                + "</text>";

            string xmlText =
                "<tile>"
                + "<visual>"
                + "<binding template=\"TileSmall\" branding=\"none\">" + image + "</binding>"
                + "<binding template=\"TileMedium\" branding=\"none\" hint-textStacking=\"center\">" + image + text + "</binding>"
                + "<binding template=\"TileWide\" branding=\"none\" hint-textStacking=\"center\">" + image + text + "</binding>"
                + "<binding template=\"TileLarge\" branding=\"none\" hint-textStacking=\"center\">" + image + text + "</binding>"
                + "</visual>"
                + "</tile>";

            XmlDocument xml = new XmlDocument();
            xml.LoadXml(xmlText);
            return xml;
        }

        private static void RemoveScheduledNotifications(TileUpdater updater)
        {
            if (updater == null)
            {
                return;
            }

            IReadOnlyList<ScheduledTileNotification> scheduled = updater.GetScheduledTileNotifications();
            for (int i = 0; i < scheduled.Count; i++)
            {
                try
                {
                    updater.RemoveFromSchedule(scheduled[i]);
                }
                catch
                {
                }
            }
        }

        private static string TrimTitle(string value)
        {
            string title = string.IsNullOrWhiteSpace(value) ? "4PDA" : value.Trim();
            if (title.Length <= 96)
            {
                return title;
            }

            return title.Substring(0, 93).TrimEnd() + "...";
        }

        private static string EscapeXml(string value)
        {
            return (value ?? string.Empty)
                .Replace("&", "&amp;")
                .Replace("<", "&lt;")
                .Replace(">", "&gt;")
                .Replace("\"", "&quot;")
                .Replace("'", "&apos;");
        }
    }
}
