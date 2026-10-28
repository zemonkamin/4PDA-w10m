using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using LegacyProjects.UI.UniversalWindowsPlatform.Controls;
using LegacyProjects.UI.UniversalWindowsPlatform.Models;
using Windows.UI.Xaml.Controls;

namespace _4PDA.Services
{
    // Data adapter only. The viewer, networking, zoom, saving and sharing live in the UI library.
    internal static class AppPhotoViewer
    {
        internal static Task ShowAsync(IEnumerable<PhotoViewerItem> items, int selected, string title)
        {
            return PhotoViewer.ShowAsync(items, selected, new PhotoViewerOptions
            {
                Title = title, Referer = new Uri("https://4pda.to/"),
                ResolveUriAsync = async (item, cancellation) =>
                {
                    cancellation.ThrowIfCancellationRequested();
                    var uri = new Uri(item.Url);
                    int id = OfficialApiAttachments.ImageId(uri);
                    return id > 0 ? await OfficialApiService.GetAttachmentDownloadUriAsync(id, false) : uri;
                }
            });
        }

        internal static Task ShowImagesAsync(Image selected, IEnumerable<Image> images, string title, Func<Image, string> source = null)
        {
            var items = new List<PhotoViewerItem>();
            var indices = new Dictionary<string, int>(StringComparer.Ordinal);
            int index = -1;
            foreach (Image image in images)
            {
                string url = source == null ? DeferredImage.GetUrl(image) : source(image);
                Uri uri;
                if (!Uri.TryCreate(url, UriKind.Absolute, out uri) || (uri.Scheme != "http" && uri.Scheme != "https")) continue;
                int existing;
                if (!indices.TryGetValue(url, out existing))
                {
                    existing = items.Count; indices[url] = existing;
                    items.Add(new PhotoViewerItem { Url = url, Title = title, FileName = System.IO.Path.GetFileName(uri.AbsolutePath) });
                }
                if (image == selected) { index = existing; items[existing].Preview = image.Source; }
            }
            return index >= 0 ? ShowAsync(items, index, title) : Task.FromResult(false);
        }
    }
}
