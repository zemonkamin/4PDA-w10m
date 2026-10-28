using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;

namespace _4PDA
{
    public sealed partial class MainPage : Page
    {
        public MainPage()
        {
            InitializeComponent();
            NavigationCacheMode = NavigationCacheMode.Enabled;
            NewsFrame.Navigate(typeof(NewsPage));
        }
        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            NewsPage page = NewsFrame.Content as NewsPage;
            if (page != null) await page.EnsureFreshAsync();
        }
    }
}
