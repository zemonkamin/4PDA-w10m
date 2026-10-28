using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using _4PDA.Services;
using Windows.ApplicationModel;
using Windows.ApplicationModel.Activation;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;

namespace _4PDA
{
    /// <summary>
    /// Provides application-specific behavior to supplement the default Application class.
    /// </summary>
    sealed partial class App : Application
    {
        private Frame _rootFrame;
        private SystemNavigationManager _systemNavigationManager;
        private bool _backNavigationInitialized;
        /// <summary>
        /// Initializes the singleton application object.  This is the first line of authored code
        /// executed, and as such is the logical equivalent of main() or WinMain().
        /// </summary>
        public App()
        {
            this.InitializeComponent();
            this.Suspending += OnSuspending;
            this.UnhandledException += delegate(object sender, UnhandledExceptionEventArgs error)
            {
                try
                {
                    string path = Path.Combine(Windows.Storage.ApplicationData.Current.LocalFolder.Path, "crash-diagnostics.log");
                    File.AppendAllText(path, DateTimeOffset.Now.ToString("O") + ", memory " + Windows.System.MemoryManager.AppMemoryUsage
                        + "/" + Windows.System.MemoryManager.AppMemoryUsageLimit + Environment.NewLine
                        + error.Exception + Environment.NewLine);
                }
                catch { }
            };
        }

        /// <summary>
        /// Invoked when the application is launched normally by the end user.  Other entry points
        /// will be used such as when the application is launched to open a specific file.
        /// </summary>
        /// <param name="e">Details about the launch request and process.</param>
        protected override void OnLaunched(LaunchActivatedEventArgs e)
        {
#if DEBUG
            if (System.Diagnostics.Debugger.IsAttached)
            {
                this.DebugSettings.EnableFrameRateCounter = true;
            }
#endif
            Frame rootFrame = Window.Current.Content as Frame;

            // Do not repeat app initialization when the Window already has content,
            // just ensure that the window is active
            if (rootFrame == null)
            {
                // Create a Frame to act as the navigation context and navigate to the first page
                rootFrame = new Frame();

                rootFrame.NavigationFailed += OnNavigationFailed;

                if (e.PreviousExecutionState == ApplicationExecutionState.Terminated)
                {
                    //TODO: Load state from previously suspended application
                }

                // Place the frame in the current Window
                Window.Current.Content = rootFrame;
            }

            _rootFrame = rootFrame;
            EnsureSystemBackNavigation();

            if (e.PrelaunchActivated == false)
            {
                if (rootFrame.Content == null)
                {
                    // When the navigation stack isn't restored navigate to the first page,
                    // configuring the new page by passing required information as a navigation
                    // parameter
                    rootFrame.Navigate(typeof(SidebarPage), e.Arguments);
                }
                // Ensure the current window is active
                Window.Current.Activate();
                RefreshLiveTileOnLaunch();
            }
        }

        private async void RefreshLiveTileOnLaunch()
        {
            try
            {
                await System.Threading.Tasks.Task.Delay(2000);
                await System.Threading.Tasks.Task.Run(() => LiveTileService.RefreshFromSavedNewsAsync());
            }
            catch
            {
            }
        }

        private void EnsureSystemBackNavigation()
        {
            if (_backNavigationInitialized)
            {
                UpdateSystemBackButtonVisibility();
                return;
            }

            _systemNavigationManager = SystemNavigationManager.GetForCurrentView();
            _systemNavigationManager.BackRequested += SystemNavigationManager_BackRequested;

            if (_rootFrame != null)
                _rootFrame.Navigated += RootFrame_Navigated;

            _backNavigationInitialized = true;
            UpdateSystemBackButtonVisibility();
        }

        private void RootFrame_Navigated(object sender, NavigationEventArgs e)
        {
            UpdateSystemBackButtonVisibility();
        }

        private void SystemNavigationManager_BackRequested(object sender, BackRequestedEventArgs e)
        {
            if (AppNavigationActions.TryGoBack())
            {
                e.Handled = true;
                UpdateSystemBackButtonVisibility();
                return;
            }

            if (_rootFrame != null && _rootFrame.CanGoBack)
            {
                _rootFrame.GoBack();
                e.Handled = true;
                UpdateSystemBackButtonVisibility();
            }
        }

        internal void UpdateSystemBackButtonVisibility()
        {
            if (_systemNavigationManager == null)
                return;

            bool canGoBack = AppNavigationActions.CanGoBack || (_rootFrame != null && _rootFrame.CanGoBack);
            _systemNavigationManager.AppViewBackButtonVisibility = canGoBack
                ? AppViewBackButtonVisibility.Visible
                : AppViewBackButtonVisibility.Collapsed;
        }

        /// <summary>
        /// Invoked when Navigation to a certain page fails
        /// </summary>
        /// <param name="sender">The Frame which failed navigation</param>
        /// <param name="e">Details about the navigation failure</param>
        void OnNavigationFailed(object sender, NavigationFailedEventArgs e)
        {
            throw new Exception("Failed to load Page " + e.SourcePageType.FullName);
        }

        /// <summary>
        /// Invoked when application execution is being suspended.  Application state is saved
        /// without knowing whether the application will be terminated or resumed with the contents
        /// of memory still intact.
        /// </summary>
        /// <param name="sender">The source of the suspend request.</param>
        /// <param name="e">Details about the suspend request.</param>
        private void OnSuspending(object sender, SuspendingEventArgs e)
        {
            var deferral = e.SuspendingOperation.GetDeferral();
            //TODO: Save application state and stop any background activity
            deferral.Complete();
        }
    }
}
