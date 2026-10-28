using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading;
using System.Threading.Tasks;
using System.Text;
using LegacyProjects.UI.UniversalWindowsPlatform.Helpers;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;
using Windows.System.Profile;
using Windows.UI.Xaml.Media.Imaging;

namespace _4PDA.Services
{
    internal static class FourPdaImageService
    {
        internal static readonly bool IsMobile = string.Equals(AnalyticsInfo.VersionInfo.DeviceFamily,
            "Windows.Mobile", StringComparison.OrdinalIgnoreCase);
        private static readonly SemaphoreSlim Pipelines = new SemaphoreSlim(IsMobile ? 2 : 4);
        private static readonly SharedRequestPool<BitmapSource> Requests = new SharedRequestPool<BitmapSource>();
        private static readonly Dictionary<string, CacheEntry> Cache = new Dictionary<string, CacheEntry>();
        private static long _cacheBytes;
        private static long _use;
        private static readonly object TraceGate = new object();
        private static readonly StringBuilder TraceBuffer = new StringBuilder();
        private static bool _traceRunning;

        internal static void TrimCache() { Cache.Clear(); _cacheBytes = 0; }

        internal static bool TryGetCached(string url, int decodeWidth, out BitmapSource image)
        {
            image = null;
            CacheEntry best = null;
            int width = Math.Max(32, Math.Min(IsMobile ? 640 : 1280, decodeWidth));
            foreach (CacheEntry entry in Cache.Values)
                if (entry.Url == url && entry.Width >= width && (best == null || entry.Width < best.Width)) best = entry;
            if (best == null) return false;
            best.Used = ++_use; image = best.Image; return true;
        }

        public static async Task<BitmapSource> LoadAsync(string url, int decodeWidth, CancellationToken cancellation)
        {
            Uri uri;
            if (!Uri.TryCreate(url, UriKind.Absolute, out uri) || (uri.Scheme != "https" && uri.Scheme != "http")) return null;
            url = uri.AbsoluteUri;
            cancellation.ThrowIfCancellationRequested();
            decodeWidth = Math.Max(32, Math.Min(IsMobile ? 640 : 1280, decodeWidth));
            BitmapSource cached;
            if (TryGetCached(url, decodeWidth, out cached)) return cached;
            return await Requests.RunAsync(url + "|" + decodeWidth,
                token => LoadCoreAsync(uri, decodeWidth, token), cancellation);
        }

        private static async Task<BitmapSource> LoadCoreAsync(Uri uri, int decodeWidth, CancellationToken cancellation)
        {
            await Pipelines.WaitAsync(cancellation);
            try
            {
                BitmapSource cached;
                if (TryGetCached(uri.AbsoluteUri, decodeWidth, out cached)) return cached;
                var clock = System.Diagnostics.Stopwatch.StartNew();
                Trace("image start, target width " + decodeWidth);
                DecodedImage decoded = await DecodeAsync(uri, decodeWidth, cancellation);
                cancellation.ThrowIfCancellationRequested();
                if (decoded == null) return null;
                var image = new WriteableBitmap(decoded.Width, decoded.Height);
                using (Stream destination = image.PixelBuffer.AsStream()) destination.Write(decoded.Pixels, 0, decoded.Pixels.Length);
                image.Invalidate();
                Store(uri.AbsoluteUri, decodeWidth, image, (long)decoded.Width * decoded.Height * 4);
                Trace("decoded " + decoded.Width + "x" + decoded.Height + ", elapsed " + clock.ElapsedMilliseconds + " ms");
                return image;
            }
            finally { Pipelines.Release(); }
        }

        private static async Task<DecodedImage> DecodeAsync(Uri uri, int decodeWidth, CancellationToken cancellation)
        {
            uint maximumBytes = IsMobile ? 4u * 1024 * 1024 : 8u * 1024 * 1024;
            using (IRandomAccessStream encoded = await ImageDownloadCache.OpenAsync(uri, maximumBytes, cancellation, request =>
            {
                ForumCookieHelper.ApplyDefaultHeaders(request);
                ForumCookieHelper.TryAppendHeader(request, "Accept", "image/jpeg,image/png,image/gif,image/bmp,image/*;q=0.8,*/*;q=0.4");
                ForumCookieHelper.TryAppendHeader(request, "Referer", "https://4pda.to/");
                ForumCookieHelper.TryAppendHeader(request, "Cookie", ForumCookieHelper.GetCookieHeaderForUrl(uri, false));
            }).ConfigureAwait(false))
            {
                return await ImageDecodeScheduler.RunAsync(async () =>
                {
                    using (var reader = new DataReader(encoded.GetInputStreamAt(0)))
                    {
                        await reader.LoadAsync(8).AsTask(cancellation).ConfigureAwait(false);
                        byte[] signature = new byte[8]; reader.ReadBytes(signature);
                        if (IsMobile && !IsLegacyBitmap(signature)) return null;
                    }
                    encoded.Seek(0);
                    BitmapDecoder decoder = await BitmapDecoder.CreateAsync(encoded).AsTask(cancellation).ConfigureAwait(false);
                    if (IsMobile && decoder.DecoderInformation.CodecId != BitmapDecoder.JpegDecoderId
                        && (long)decoder.PixelWidth * decoder.PixelHeight > 8388608) return null;
                    int width, height;
                    if (!ImageLoadPolicy.DecodeSize(decoder.PixelWidth, decoder.PixelHeight, decodeWidth, IsMobile, out width, out height)) return null;
                    PixelDataProvider pixels = await decoder.GetPixelDataAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied,
                        new BitmapTransform { ScaledWidth = (uint)width, ScaledHeight = (uint)height, InterpolationMode = BitmapInterpolationMode.Linear },
                        ExifOrientationMode.IgnoreExifOrientation, ColorManagementMode.DoNotColorManage).AsTask(cancellation).ConfigureAwait(false);
                    cancellation.ThrowIfCancellationRequested();
                    return new DecodedImage { Width = width, Height = height, Pixels = pixels.DetachPixelData() };
                }, cancellation).ConfigureAwait(false);
            }
        }

        private static void Store(string url, int width, BitmapSource image, long bytes)
        {
            if (Windows.System.MemoryManager.AppMemoryUsageLevel >= Windows.System.AppMemoryUsageLevel.High) return;
            string key = url + "|" + width;
            CacheEntry replaced;
            if (Cache.TryGetValue(key, out replaced)) { _cacheBytes -= replaced.Bytes; Cache.Remove(key); }
            long budget = IsMobile ? 8 * 1024 * 1024 : 32 * 1024 * 1024;
            while (Cache.Count > 0 && (_cacheBytes + bytes > budget || Cache.Count >= 64))
            {
                string oldest = null; long oldestUse = long.MaxValue;
                foreach (var entry in Cache) if (entry.Value.Used < oldestUse) { oldest = entry.Key; oldestUse = entry.Value.Used; }
                _cacheBytes -= Cache[oldest].Bytes; Cache.Remove(oldest);
            }
            Cache[key] = new CacheEntry { Url = url, Width = width, Image = image, Bytes = bytes, Used = ++_use }; _cacheBytes += bytes;
        }

        private static bool IsLegacyBitmap(byte[] data)
        {
            return (data[0] == 0xff && data[1] == 0xd8 && data[2] == 0xff)
                || (data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4e && data[3] == 0x47
                    && data[4] == 13 && data[5] == 10 && data[6] == 26 && data[7] == 10)
                || (data[0] == 0x47 && data[1] == 0x49 && data[2] == 0x46 && data[3] == 0x38
                    && (data[4] == 0x37 || data[4] == 0x39) && data[5] == 0x61)
                || (data[0] == 0x42 && data[1] == 0x4d);
        }

        private sealed class CacheEntry { internal string Url; internal int Width; internal BitmapSource Image; internal long Bytes; internal long Used; }
        private sealed class DecodedImage { internal int Width, Height; internal byte[] Pixels; }

        [System.Diagnostics.Conditional("DEBUG")]
        internal static void Trace(string message)
        {
            lock (TraceGate)
            {
                if (TraceBuffer.Length < 32768) TraceBuffer.Append(DateTimeOffset.Now.ToString("O") + " " + message
                    + ", cache " + _cacheBytes + ", app memory " + Windows.System.MemoryManager.AppMemoryUsage + Environment.NewLine);
                if (_traceRunning) return;
                _traceRunning = true;
                Task.Run(FlushTraceAsync);
            }
        }

        private static async Task FlushTraceAsync()
        {
            while (true)
            {
                await Task.Delay(400);
                string batch;
                lock (TraceGate)
                {
                    if (TraceBuffer.Length == 0) { _traceRunning = false; return; }
                    batch = TraceBuffer.ToString(); TraceBuffer.Clear();
                }
                try
                {
                    string path = Path.Combine(Windows.Storage.ApplicationData.Current.LocalFolder.Path, "image-diagnostics.log");
                    if (File.Exists(path) && new FileInfo(path).Length > 262144) File.WriteAllText(path, string.Empty);
                    File.AppendAllText(path, batch);
                }
                catch { }
            }
        }
    }
}
