using System;
using System.Collections.Generic;
using System.Threading;
using Windows.Foundation;
using Windows.System;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Imaging;

namespace _4PDA.Services
{
    public sealed class DeferredImage : DependencyObject
    {
        public static readonly DependencyProperty UrlProperty = DependencyProperty.RegisterAttached("Url", typeof(string),
            typeof(DeferredImage), new PropertyMetadata(string.Empty, UrlChanged));
        public static readonly DependencyProperty DecodeWidthProperty = DependencyProperty.RegisterAttached("DecodeWidth", typeof(int),
            typeof(DeferredImage), new PropertyMetadata(640));
        private static readonly DependencyProperty StateProperty = DependencyProperty.RegisterAttached("State", typeof(object),
            typeof(DeferredImage), new PropertyMetadata(null));
        private static readonly HashSet<State> Active = new HashSet<State>();
        private static readonly Dictionary<ScrollViewer, int> Scrolls = new Dictionary<ScrollViewer, int>();
        private static DispatcherTimer _timer;
        private static int _diagnosticScans;
        private static DateTime _resumeAfterScroll;

        public static string GetUrl(DependencyObject target) { return (string)target.GetValue(UrlProperty); }
        public static void SetUrl(DependencyObject target, string value) { target.SetValue(UrlProperty, value ?? string.Empty); }
        public static int GetDecodeWidth(DependencyObject target) { return (int)target.GetValue(DecodeWidthProperty); }
        public static void SetDecodeWidth(DependencyObject target, int value) { target.SetValue(DecodeWidthProperty, value); }

        internal static void Attach(Image image, string url, int width = 640, double placeholderHeight = 100)
        {
            Uri local;
            if (Uri.TryCreate(url, UriKind.Absolute, out local) && (local.Scheme == "ms-appx" || local.Scheme == "ms-appdata"))
            {
                image.Source = new BitmapImage { DecodePixelWidth = width, UriSource = local };
                return;
            }
            SetDecodeWidth(image, width);
            if (double.IsNaN(image.Height) && image.MinHeight == 0) image.MinHeight = placeholderHeight;
            SetUrl(image, url);
        }

        private static void UrlChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
        {
            Image image = sender as Image;
            if (image == null) return;
            State state = image.GetValue(StateProperty) as State;
            if (state == null)
            {
                state = new State { Image = image, MinimumHeight = image.MinHeight };
                image.SetValue(StateProperty, state);
                image.Loaded += ImageLoaded;
                image.Unloaded += ImageUnloaded;
            }
            Unregister(state);
            Release(state);
            image.MinHeight = state.MinimumHeight;
            state.Url = e.NewValue as string;
            state.Failed = false;
            Uri local;
            if (Uri.TryCreate(state.Url, UriKind.Absolute, out local) && (local.Scheme == "ms-appx" || local.Scheme == "ms-appdata"))
            {
                image.Source = new BitmapImage { DecodePixelWidth = GetDecodeWidth(image), UriSource = local };
                return;
            }
            if (IsConnected(image)) Register(state);
        }

        private static void ImageLoaded(object sender, RoutedEventArgs e) { Register((State)((Image)sender).GetValue(StateProperty)); }
        private static void ImageUnloaded(object sender, RoutedEventArgs e)
        {
            State state = (State)((Image)sender).GetValue(StateProperty);
            Unregister(state); Release(state);
            if (Active.Count == 0 && _timer != null) _timer.Stop();
        }

        private static void Register(State state)
        {
            if (state == null || string.IsNullOrWhiteSpace(state.Url)) return;
            if (_timer == null)
            {
                _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(40) };
                _timer.Tick += Tick;
                FrameworkElement root = Window.Current.Content as FrameworkElement;
                if (root != null) root.LayoutUpdated += delegate { Schedule(); };
                Window.Current.SizeChanged += delegate { Schedule(); };
                CoreDispatcher dispatcher = Window.Current.Dispatcher;
                MemoryManager.AppMemoryUsageIncreased += async delegate
                {
                    try { await dispatcher.RunAsync(CoreDispatcherPriority.Normal, Trim); } catch { }
                };
            }
            state.Failed = false;
            if (Active.Add(state))
            {
                DependencyObject parent = state.Image;
                while (parent != null)
                {
                    ScrollViewer scroll = parent as ScrollViewer;
                    if (scroll != null)
                    {
                        int count;
                        if (!Scrolls.TryGetValue(scroll, out count)) scroll.ViewChanged += ViewChanged;
                        Scrolls[scroll] = count + 1;
                        state.Scrolls.Add(scroll);
                    }
                    parent = VisualTreeHelper.GetParent(parent);
                }
            }
            Schedule();
        }

        private static void Schedule(int milliseconds = 40)
        {
            if (Active.Count == 0 || _timer == null || _timer.IsEnabled) return;
            _timer.Interval = TimeSpan.FromMilliseconds(milliseconds);
            _timer.Start();
        }
        private static void ViewChanged(object sender, ScrollViewerViewChangedEventArgs e)
        {
            _resumeAfterScroll = DateTime.UtcNow.AddMilliseconds(e.IsIntermediate ? 160 : 40);
            Schedule();
        }
        private static void Unregister(State state)
        {
            Active.Remove(state);
            foreach (ScrollViewer scroll in state.Scrolls)
            {
                int count;
                if (!Scrolls.TryGetValue(scroll, out count)) continue;
                if (count <= 1) { scroll.ViewChanged -= ViewChanged; Scrolls.Remove(scroll); }
                else Scrolls[scroll] = count - 1;
            }
            state.Scrolls.Clear();
        }

        internal static void Trim()
        {
            FourPdaImageService.TrimCache();
            foreach (State state in Snapshot()) if (!Visible(state.Image)) Release(state);
        }

        private static State[] Snapshot() { var states = new State[Active.Count]; Active.CopyTo(states); return states; }

        private static void Tick(object sender, object e)
        {
            _timer.Stop();
            if (DateTime.UtcNow < _resumeAfterScroll) { Schedule(80); return; }
            int visibleCount = 0;
            foreach (State state in Snapshot())
            {
                if (!IsConnected(state.Image)) { Unregister(state); Release(state); continue; }
                bool visible = Window.Current.Visible && Visible(state.Image);
                if (visible)
                {
                    visibleCount++;
                    state.LastVisible = DateTime.UtcNow;
                    if (state.Image.Source == null && state.Load == null && !state.Failed)
                    {
                        BitmapSource cached;
                        if (FourPdaImageService.TryGetCached(state.Url, GetDecodeWidth(state.Image), out cached))
                        {
                            state.Image.MinHeight = state.MinimumHeight;
                            state.Image.Source = cached;
                        }
                        else Load(state);
                    }
                }
                else
                {
                    if (state.Load != null) state.Load.Cancel();
                    if ((DateTime.UtcNow - state.LastVisible).TotalSeconds > 1) { Release(state); state.Failed = false; }
                    else if (state.Image.Source != null) Schedule();
                }
            }
            if (_diagnosticScans++ < 6)
                FourPdaImageService.Trace("viewport scan, tracked " + Active.Count + ", visible " + visibleCount + ", window " + Window.Current.Visible);
            if (Active.Count == 0) _timer.Stop();
        }

        private static async void Load(State state)
        {
            var cancellation = new CancellationTokenSource();
            state.Load = cancellation;
            try
            {
                BitmapSource source = await FourPdaImageService.LoadAsync(state.Url, GetDecodeWidth(state.Image), cancellation.Token);
                if (!cancellation.IsCancellationRequested && state.Load == cancellation && Visible(state.Image))
                {
                    state.Image.MinHeight = state.MinimumHeight;
                    state.Image.Source = source;
                    state.Failed = source == null;
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception error)
            {
                if (!cancellation.IsCancellationRequested)
                {
                    state.Failed = true;
                    FourPdaImageService.Trace("failed " + error.GetType().Name + " 0x" + error.HResult.ToString("X8"));
                }
            }
            finally
            {
                if (state.Load == cancellation) state.Load = null;
                cancellation.Dispose();
                Schedule();
            }
        }

        private static void Release(State state)
        {
            if (state.Load != null) { state.Load.Cancel(); state.Load = null; }
            Image image = state.Image;
            if (image.Source != null && double.IsNaN(image.Height))
                image.MinHeight = Math.Min(image.MaxHeight, Math.Max(state.MinimumHeight, image.ActualHeight));
            image.Source = null;
        }

        private static bool IsConnected(DependencyObject element)
        {
            var root = Window.Current == null ? null : Window.Current.Content;
            while (element != null) { if (element == root) return true; element = VisualTreeHelper.GetParent(element); }
            return false;
        }

        private static bool Visible(Image image)
        {
            UIElement root = Window.Current == null ? null : Window.Current.Content;
            if (root == null) return false;
            try
            {
                // An Image without Source can report zero width on old XAML,
                // even inside a measured placeholder. Observe that placeholder
                // so lazy loading can start before the first bitmap exists.
                double width = image.ActualWidth, height = image.ActualHeight;
                FrameworkElement host = VisualTreeHelper.GetParent(image) as FrameworkElement;
                if (width <= 0 && host != null) width = host.ActualWidth;
                if (height <= 0 && host != null) height = host.ActualHeight;
                if (width <= 0 || height <= 0) return false;
                Rect bounds = image.TransformToVisual(root).TransformBounds(new Rect(0, 0, width, height));
                Rect viewport = new Rect(0, 0, Window.Current.Bounds.Width, Window.Current.Bounds.Height);
                DependencyObject parent = image;
                while (parent != null && parent != root)
                {
                    FrameworkElement element = parent as FrameworkElement;
                    if (element != null && element.Visibility == Visibility.Collapsed) return false;
                    ScrollViewer scroll = parent as ScrollViewer;
                    if (scroll != null)
                    {
                        Rect clip = scroll.TransformToVisual(root).TransformBounds(new Rect(0, 0, scroll.ActualWidth, scroll.ActualHeight));
                        viewport.Intersect(clip);
                    }
                    parent = VisualTreeHelper.GetParent(parent);
                }
                bounds.Intersect(viewport);
                return !bounds.IsEmpty && bounds.Width > 0 && bounds.Height > 0;
            }
            catch { return false; }
        }

        private sealed class State
        {
            internal Image Image; internal string Url; internal bool Failed;
            internal CancellationTokenSource Load; internal DateTime LastVisible;
            internal double MinimumHeight;
            internal readonly List<ScrollViewer> Scrolls = new List<ScrollViewer>();
        }
    }
}
