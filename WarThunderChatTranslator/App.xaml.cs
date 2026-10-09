using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.IO;
using WarThunderChatTranslator.Configurations;
using System.Threading.Tasks;
using Application = Microsoft.UI.Xaml.Application;
using H.NotifyIcon;
using Microsoft.UI;
using System.Text;
using System.Diagnostics;
using System.Collections.Generic;
using System.Linq;
using System.Security.Principal;
using System.Runtime.InteropServices;
using NLog;
using WarThunderChatTranslator.Helpers;
using WarThunderChatTranslator.Services;
using Microsoft.UI.Xaml.Input;
using System.Threading;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Dispatching;

namespace WarThunderChatTranslator
{
    public partial class App : Microsoft.UI.Xaml.Application
    {
        public NLog.Logger logger = LogManager.GetCurrentClassLogger();
        private Window m_window;
        public TaskbarIcon TrayIcon { get; private set; }
        private readonly CancellationTokenSource _shutdownCts = new();
        private GameChatPollingService _gameChatPollingService;
        private LocalHttpServer _localHttpServer;
        private SuccessAudioService _successAudioService;
        private QuickTranslationService _quickTranslationService;
        private GlobalHotkeyService _globalHotkeyService;
        internal OverlayWindowManager OverlayManager { get; private set; }
        private Task _gamePollingTask;
        private DispatcherQueue _dispatcherQueue;
        private int _servicesStopped;
        private int _exitStarted;
        private bool _quickTranslationHotkeysSuspended;
        private readonly object _quickTranslationTasksLock = new();
        private readonly HashSet<Task> _quickTranslationTasks = new();

        private static Mutex mutex; // Defines the process-wide mutex used for single-instance enforcement.

        [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "MessageBoxW")]
        private static extern int ShowMessageBox(IntPtr owner, string message, string caption, uint flags);

        public App()
        {
            // Preserve the mutex name: changing it would break single-instance protection
            // for users who already have the older version running.
            try
            {
                mutex = new Mutex(true, "WarThunderChatTranslator_Mutex", out var isNewInstance);
                if (!isNewInstance)
                {
                    try
                    {
                        var language = Localization.SystemLanguage;
                        ShowMessageBox(IntPtr.Zero,
                            Localization.GetStringForLanguage("StartupAlreadyRunning", language),
                            Localization.GetStringForLanguage("AboutAppName", language), 0x40);
                    }
                    catch (Exception ex)
                    {
                        logger.Warn(ex, "Could not display the duplicate-instance notice.");
                    }
                    Environment.Exit(0);
                    return;
                }
            }
            catch (Exception ex)
            {
                logger.Warn(ex, "The single-instance mutex could not be created.");
            }

            InitializeLogging();
            RegisterGlobalExceptionHandlers();
            InitializeStartupLanguage();
            try
            {
                // Apply the supported culture BEFORE any localized XAML is loaded.
                InitializeComponent();
            }
            catch (Exception ex)
            {
                logger.Fatal(ex, "Fatal error loading application XAML.");
                try
                {
                    ShowMessageBox(IntPtr.Zero, ex.Message, "War Thunder Chat Translator", 0x10);
                }
                catch { /* Nothing else can recover from a broken App.xaml. */ }
                Environment.Exit(1);
            }
        }

        private void InitializeStartupLanguage()
        {
            string savedLanguage = null;
            try
            {
                savedLanguage = ApplicationConfig.GetSettings(ApplicationConfig.ApplicationLanguageKey);
            }
            catch (Exception ex)
            {
                logger.Warn(ex, "Cannot read the saved display language; using the system language.");
            }

            try
            {
                Localization.Initialize(savedLanguage);
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Cannot initialize localized resources. Falling back to English.");
                try { Localization.Apply(StartupLanguage.English, updateWindowsPreference: false, savePreference: false); }
                catch (Exception fallbackError) { logger.Error(fallbackError, "English culture fallback failed."); }
            }
        }

        protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
        {
            _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
            try
            {
                InitializeAppSettings();
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Some startup settings could not be initialized.");
            }

            try
            {
                OverlayManager = new OverlayWindowManager(() => _gameChatPollingService);
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Overlay manager initialization failed; the tray may still work.");
            }

            var trayCreated = false;
            try
            {
                InitializeTrayIcon();
                trayCreated = true;
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Could not create the system tray icon.");
                try { TrayIcon?.Dispose(); } catch { }
                TrayIcon = null;
            }

            try
            {
                ApplicationNotifications.Initialize(() => EnqueueOnUiThread(ToggleMainWindowVisibility));
                if (trayCreated)
                {
                    // Use the ORIGINAL Windows UI language, not a manually saved app override.
                    ApplicationNotifications.Show(Localization.GetSystemString("StartupTrayToast"));
                }
                else
                {
                    ApplicationNotifications.Show(Localization.GetSystemString("StartupTrayUnavailable"));
                }
            }
            catch (Exception ex)
            {
                logger.Warn(ex, "Windows notifications are unavailable; application startup continues.");
            }

            if (!trayCreated)
            {
                // Without the tray, a background-only application would become inaccessible.
                try { InitializeMainWindow(); }
                catch (Exception ex) { logger.Error(ex, "Cannot open the settings window as a tray fallback."); }
            }

            // This task catches its own startup failures so the UI thread remains alive.
            _ = StartBackgroundServicesAsync();

            if (Localization.UsedUnsupportedSystemLanguageFallback)
            {
                // A real (non-toast) notice works even when Windows notifications are off.
                // Queue it after startup so the modal message cannot interrupt initialization.
                EnqueueOnUiThread(ShowUnsupportedSystemLanguageNoticeOnce);
            }
        }

        private void ShowUnsupportedSystemLanguageNoticeOnce()
        {
            try
            {
                var systemLanguage = Localization.OriginalSystemLanguage;
                var noticeKey = ApplicationConfig.UnsupportedSystemLanguageNoticeKeyPrefix +
                    (string.IsNullOrWhiteSpace(systemLanguage) ? "unknown" : systemLanguage);
                // Even a damaged settings store must not suppress the message.
                try
                {
                    if (ApplicationConfig.GetSettings(noticeKey) == "true")
                    {
                        return;
                    }
                }
                catch (Exception ex)
                {
                    logger.Warn(ex, "Could not read the unsupported-language notice setting.");
                }

                var message = string.Format(System.Globalization.CultureInfo.InvariantCulture,
                    Localization.GetStringForLanguage("StartupUnsupportedSystemLanguage", StartupLanguage.English),
                    string.IsNullOrWhiteSpace(systemLanguage) ? "unknown" : systemLanguage);
                var result = ShowMessageBox(IntPtr.Zero, message,
                    Localization.GetStringForLanguage("StartupUnsupportedSystemLanguageTitle", StartupLanguage.English),
                    0x40);
                if (result != 0) // Store only after the OS successfully displayed the dialog.
                {
                    try { ApplicationConfig.SaveSettings(noticeKey, "true"); }
                    catch (Exception ex) { logger.Warn(ex, "Could not remember the unsupported-language notice."); }
                }
            }
            catch (Exception ex)
            {
                logger.Warn(ex, "Could not show the unsupported system-language notice.");
            }
        }

        private void InitializeLogging()
        {
            try
            {
                logger.Info("--------Application started--------");
                DeleteOldLogs();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.TraceWarning($"Logging initialization failed: {ex}");
            }
        }

        private void RegisterGlobalExceptionHandlers()
        {
            TaskScheduler.UnobservedTaskException += (sender, e) =>
            {
                if (!IsExpectedShutdownCancellation(e.Exception))
                {
                    HandleException(e.Exception);
                }

                e.SetObserved();
            };

            AppDomain.CurrentDomain.UnhandledException += (sender, e) =>
            {
                if (e.ExceptionObject is Exception ex && !IsExpectedShutdownCancellation(ex))
                {
                    HandleException(ex);
                }
            };

            App.Current.UnhandledException += (sender, e) =>
            {
                if (IsExpectedShutdownCancellation(e.Exception))
                {
                    e.Handled = true;
                    return;
                }

                HandleException(e.Exception);
                // Recover from managed UI callback failures; AppDomain-level fatal
                // exceptions can still terminate the process and cannot be "handled" here.
                e.Handled = true;
            };
        }

        private bool IsExpectedShutdownCancellation(Exception exception)
        {
            if (!_shutdownCts.IsCancellationRequested || exception == null)
            {
                return false;
            }

            if (exception is OperationCanceledException)
            {
                return true;
            }

            if (exception is AggregateException aggregate)
            {
                return aggregate.Flatten().InnerExceptions.All(inner =>
                    inner is OperationCanceledException);
            }

            return false;
        }

        private void InitializeAppSettings()
        {
            var defaultSettings = new Dictionary<string, string>
            {
                { ApplicationConfig.NetworkProxyModeKey, "Default" },
                { ApplicationConfig.ProxyAddressKey, "" },
                { ApplicationConfig.ProxyAccountKey, "" },
                { ApplicationConfig.ProxyPasswordKey, "" },
                { ApplicationConfig.LastUpdateCheckDateKey, "Never" },
                { ApplicationConfig.TranslateApiKey, "Microsoft" },
                { ApplicationConfig.AiSelectedProviderIdKey, "" },
                { ApplicationConfig.TargetLanguageKey, "zh-CN" },
                { ApplicationConfig.FontFamilyKey, "Segoe UI" },
                { ApplicationConfig.FontSizeKey, "14" },
                { ApplicationConfig.FontStyleKey, "normal" },
                { ApplicationConfig.AllyFontColorKey, "#FF5BC0DE" },
                { ApplicationConfig.EnemyFontColorKey, "#FFD9534F" },
                { ApplicationConfig.SystemFontColorKey, "#FF856404" },
                { ApplicationConfig.ThemeKey, "Default" },
                { ApplicationConfig.BackgroundCssKey, "background-color: #f4f4f4;" },
                { ApplicationConfig.GamePollingIntervalSecondsKey, ApplicationConfig.DefaultPollingIntervalSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture) },
                { ApplicationConfig.WebPollingIntervalSecondsKey, ApplicationConfig.DefaultPollingIntervalSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture) },
                { ApplicationConfig.ClearBattleChatCacheKey, "true" },
                { ApplicationConfig.BattleChatClearModeKey, ApplicationConfig.BattleChatClearModeLogical },
                { ApplicationConfig.PhysicalChatCacheLimitKey, ApplicationConfig.DefaultPhysicalChatCacheLimit.ToString(System.Globalization.CultureInfo.InvariantCulture) },
                { ApplicationConfig.OpenOverlayOnStartupKey, "false" },
                { ApplicationConfig.OpenDashboardOnStartupKey, "false" },
                { ApplicationConfig.OverlayDisplayModeKey, "translation" },
                { ApplicationConfig.OverlayShowSourceLanguageKey, "false" },
                { ApplicationConfig.OverlayOpacityPercentKey, "90" },
                { ApplicationConfig.OverlayWidthKey, OverlayWindow.DefaultWidth.ToString(System.Globalization.CultureInfo.InvariantCulture) },
                { ApplicationConfig.OverlayHeightKey, OverlayWindow.DefaultHeight.ToString(System.Globalization.CultureInfo.InvariantCulture) },
                { QuickTranslationConfig.EnabledKey, "false" },
                { QuickTranslationConfig.RecognitionLanguageKey, "" },
                { QuickTranslationConfig.MicrophoneDeviceIdKey, "" },
                { QuickTranslationConfig.RecordingStartTimingKey, QuickTranslationConfig.RecordingStartTimingOnPlaybackStart },
                { QuickTranslationConfig.RecordingStartSoundModeKey, QuickTranslationConfig.SoundModeSystem },
                { QuickTranslationConfig.RecordingStartPromptTextKey, QuickTranslationConfig.GetDefaultPromptText(QuickTranslationAudioCue.RecordingStart) },
                { QuickTranslationConfig.RecordingStartVoiceIdKey, "" },
                { QuickTranslationConfig.RecordingStartSpeakingRateKey, QuickTranslationConfig.DefaultSpeakingRate.ToString(System.Globalization.CultureInfo.InvariantCulture) },
                { QuickTranslationConfig.RecordingStartCustomAudioPathKey, "" },
                { QuickTranslationConfig.RecordingStartCustomAudioDisplayNameKey, "" },
                { QuickTranslationConfig.RecordingStartCustomAudioSourcePathKey, "" },
                { QuickTranslationConfig.RecordingEndSoundModeKey, QuickTranslationConfig.SoundModeSystem },
                { QuickTranslationConfig.RecordingEndPromptTextKey, QuickTranslationConfig.GetDefaultPromptText(QuickTranslationAudioCue.RecordingEnd) },
                { QuickTranslationConfig.RecordingEndVoiceIdKey, "" },
                { QuickTranslationConfig.RecordingEndSpeakingRateKey, QuickTranslationConfig.DefaultSpeakingRate.ToString(System.Globalization.CultureInfo.InvariantCulture) },
                { QuickTranslationConfig.RecordingEndCustomAudioPathKey, "" },
                { QuickTranslationConfig.RecordingEndCustomAudioDisplayNameKey, "" },
                { QuickTranslationConfig.RecordingEndCustomAudioSourcePathKey, "" },
                { QuickTranslationConfig.SuccessSoundModeKey, QuickTranslationConfig.SoundModeSystem },
                { QuickTranslationConfig.SuccessPromptTextKey, QuickTranslationConfig.GetDefaultPromptText(QuickTranslationAudioCue.Success) },
                { QuickTranslationConfig.SuccessVoiceIdKey, "" },
                { QuickTranslationConfig.SuccessSpeakingRateKey, QuickTranslationConfig.DefaultSpeakingRate.ToString(System.Globalization.CultureInfo.InvariantCulture) },
                { QuickTranslationConfig.SuccessCustomAudioPathKey, "" },
                { QuickTranslationConfig.SuccessCustomAudioDisplayNameKey, "" },
                { QuickTranslationConfig.SuccessCustomAudioSourcePathKey, "" },
                { QuickTranslationConfig.TranslationFailureSoundModeKey, QuickTranslationConfig.SoundModeSystem },
                { QuickTranslationConfig.TranslationFailurePromptTextKey, QuickTranslationConfig.GetDefaultPromptText(QuickTranslationAudioCue.TranslationFailure) },
                { QuickTranslationConfig.TranslationFailureVoiceIdKey, "" },
                { QuickTranslationConfig.TranslationFailureSpeakingRateKey, QuickTranslationConfig.DefaultSpeakingRate.ToString(System.Globalization.CultureInfo.InvariantCulture) },
                { QuickTranslationConfig.TranslationFailureCustomAudioPathKey, "" },
                { QuickTranslationConfig.TranslationFailureCustomAudioDisplayNameKey, "" },
                { QuickTranslationConfig.TranslationFailureCustomAudioSourcePathKey, "" },
                { QuickTranslationConfig.SuccessVolumeKey, "0.8" },
            };

            foreach (var setting in defaultSettings)
            {
                try
                {
                    if (ApplicationConfig.GetSettings(setting.Key) == null)
                    {
                        ApplicationConfig.SaveSettings(setting.Key, setting.Value);
                    }
                }
                catch (Exception ex)
                {
                    logger.Warn(ex, "Could not initialize default setting {0}.", setting.Key);
                }
            }

            try
            {
                if (ApplicationConfig.GetSettings(QuickTranslationConfig.HotkeysKey) == null)
                {
                    QuickTranslationConfig.SaveHotkeys(QuickTranslationConfig.DefaultHotkeys);
                }
            }
            catch (Exception ex)
            {
                logger.Warn(ex, "Quick translation hotkeys could not be initialized.");
            }

            try
            {
                logger.Info("Initializing translator.");
                TranslationHelper.init();
                logger.Info("Translator initialization completed.");
            }
            catch (Exception ex)
            {
                logger.Warn(ex, "Translation client initialization failed; other startup features will continue.");
            }
        }

        private void InitializeTrayIcon()
        {
            var openDashboardCommand = (XamlUICommand)Resources["OpenDashboardCommand"];
            openDashboardCommand.ExecuteRequested += (sender, args) =>
            {
                EnqueueOnUiThread(() => _ = OpenDashboardAsync());
            };

            var openOverlayCommand = (XamlUICommand)Resources["OpenOverlayCommand"];
            openOverlayCommand.ExecuteRequested += (sender, args) => EnqueueOnUiThread(() => OverlayManager?.Toggle());

            var showHideWindowCommand = (XamlUICommand)Resources["ShowHideWindowCommand"];
            showHideWindowCommand.ExecuteRequested += (sender, args) => EnqueueOnUiThread(ToggleMainWindowVisibility);

            var exitApplicationCommand = (XamlUICommand)Resources["ExitApplicationCommand"];
            exitApplicationCommand.ExecuteRequested += (sender, args) => EnqueueOnUiThread(() => _ = ExitApplicationAsync());

            TrayIcon = (TaskbarIcon)Resources["TrayIcon"];
            TrayIcon.ForceCreate();
            Localization.CultureChanged += UpdateTrayMenuWidth;
            try { UpdateTrayMenuWidth(); }
            catch (Exception ex) { logger.Warn(ex, "Could not measure the tray menu width."); }
        }

        private async Task OpenDashboardAsync()
        {
            // Never open the default port unless this instance is actually listening on it.
            var dashboardUri = _localHttpServer?.DashboardUri;
            if (dashboardUri is null)
            {
                logger.Warn("Dashboard is not listening; skipping browser launch.");
                ApplicationNotifications.Show(Localization.GetSystemString("StartupServiceUnavailable"));
                return;
            }

            try
            {
                if (!await Windows.System.Launcher.LaunchUriAsync(dashboardUri))
                {
                    logger.Warn("Windows refused to open Dashboard URL {0}.", dashboardUri);
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Failed to open the chat dashboard at {0}.", dashboardUri);
            }
        }

        private void EnqueueOnUiThread(Action action)
        {
            try
            {
                if (_dispatcherQueue == null || !_dispatcherQueue.TryEnqueue(() =>
                {
                    try { action(); }
                    catch (Exception ex) { logger.Error(ex, "Tray UI action failed."); }
                }))
                {
                    logger.Warn("Failed to dispatch the tray command to the main UI thread.");
                }
            }
            catch (Exception ex)
            {
                logger.Warn(ex, "Cannot dispatch a tray command.");
            }
        }

        private void UpdateTrayMenuWidth()
        {
            if (TrayIcon?.ContextFlyout is not MenuFlyout menuFlyout)
            {
                return;
            }

            var menuKeys = new[] { "TrayOverlay", "TrayDashboard", "TraySettings", "TrayExit" };
            var widestText = 0.0;
            foreach (var key in menuKeys)
            {
                var textBlock = new TextBlock
                {
                    FontSize = 14,
                    Text = Localization.GetString(key)
                };
                textBlock.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
                widestText = Math.Max(widestText, textBlock.DesiredSize.Width);
            }

            const double iconAndPaddingWidth = 88;
            var menuWidth = Math.Max(180, Math.Ceiling(widestText + iconAndPaddingWidth));
            foreach (var menuItem in menuFlyout.Items.OfType<MenuFlyoutItem>())
            {
                menuItem.Width = menuWidth;
                menuItem.MaxWidth = menuWidth;
            }
        }

        private void ToggleMainWindowVisibility()
        {
            if (m_window == null)
            {
                InitializeMainWindow();
                return;
            }

            if (!m_window.Visible)
            {
                m_window.Show();
            }

            m_window.Activate();
        }

        private void InitializeMainWindow()
        {
            // Especially important as a fallback when the tray cannot be created:
            // optional settings and visual effects must not prevent the UI from opening.
            var window = new MainWindow();
            m_window = window;
            var theme = "Default";
            try
            {
                theme = ApplicationConfig.GetSettings(ApplicationConfig.ThemeKey) ?? "Default";
            }
            catch (Exception ex)
            {
                logger.Warn(ex, "Cannot read the saved window theme.");
            }

            var settingsTheme = theme switch
            {
                "Light" => ElementTheme.Light,
                "Dark" => ElementTheme.Dark,
                _ => ElementTheme.Default,
            };

            try { ApplicationConfig.SaveSettings(ApplicationConfig.ThemeKey, theme); }
            catch (Exception ex) { logger.Warn(ex, "Cannot save the window theme."); }
            try { window.SystemBackdrop = new DesktopAcrylicBackdrop(); }
            catch (Exception ex) { logger.Warn(ex, "The acrylic backdrop is unavailable."); }
            try { ApplyTheme(settingsTheme); }
            catch (Exception ex) { logger.Warn(ex, "Cannot apply the saved window theme."); }
            try { CenterWindow(window); }
            catch (Exception ex) { logger.Warn(ex, "Cannot center the settings window."); }

            window.Closed += (sender, args) =>
            {
                if (HandleClosedEvents)
                {
                    args.Handled = true;
                    try { window.Hide(); }
                    catch (Exception ex) { logger.Warn(ex, "Cannot hide the settings window."); }
                }
            };
            window.Show();
            window.Activate();
        }

        public static void ApplyTheme(ElementTheme theme)
        {
            if (Current is App app && app.m_window?.Content is FrameworkElement root)
            {
                root.RequestedTheme = theme;
            }
        }

        private static void CenterWindow(Window window)
        {
            var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
            var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hWnd);
            var appWindow = Microsoft.UI.Windowing.AppWindow.GetFromWindowId(windowId);
            var displayArea = Microsoft.UI.Windowing.DisplayArea.GetFromWindowId(windowId, Microsoft.UI.Windowing.DisplayAreaFallback.Nearest);

            if (appWindow is not null && displayArea is not null)
            {
                var CenteredPosition = appWindow.Position;
                CenteredPosition.X = (displayArea.WorkArea.Width - appWindow.Size.Width) / 2;
                CenteredPosition.Y = (displayArea.WorkArea.Height - appWindow.Size.Height) / 2;
                appWindow.Move(CenteredPosition);
            }
        }

        private static bool IsAdmin()
        {
            var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }

        public bool HandleClosedEvents { get; set; } = true;

        private async Task ExitApplicationAsync()
        {
            if (Interlocked.Exchange(ref _exitStarted, 1) != 0)
            {
                return;
            }

            HandleClosedEvents = false;

            try
            {
                await StopBackgroundServicesAsync();
            }
            catch (Exception ex)
            {
                logger.Error(ex, "An error occurred while stopping background services.");
            }

            ApplicationNotifications.Shutdown();

            try
            {
                Localization.CultureChanged -= UpdateTrayMenuWidth;
                TrayIcon?.Dispose();
                TrayIcon = null;
            }
            catch (Exception ex)
            {
                logger.Error(ex, "An error occurred while disposing the tray icon.");
            }

            try
            {
                OverlayManager?.Close();
            }
            catch (Exception ex)
            {
                logger.Error(ex, "An error occurred while closing the overlay window.");
            }

            try
            {
                m_window?.Close();
                m_window = null;
            }
            catch (Exception ex)
            {
                logger.Error(ex, "An error occurred while closing the main window.");
            }

            Application.Current.Exit();
        }

        private void DeleteOldLogs()
        {
            try
            {
                string logDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "WTChatTranslator", "Log");
                string logFilePrefix = "Log-WTChatTranslator-";
                DateTime deletionDate = DateTime.Now.AddDays(-3);

                foreach (var logFile in Directory.EnumerateFiles(logDirectory, $"{logFilePrefix}*.log"))
                {
                    var dateString = Path.GetFileName(logFile)?.Substring(logFilePrefix.Length, 10);
                    if (DateTime.TryParseExact(dateString, "yyyy-MM-dd", null, System.Globalization.DateTimeStyles.None, out DateTime logDate) && logDate <= deletionDate)
                    {
                        File.Delete(logFile);
                        logger.Info("Deleted expired log file: " + Path.GetFileName(logFile));
                    }
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Failed to delete expired log files.");
            }
        }

        private void HandleException(Exception ex)
        {
            try
            {
                logger.Error(ex, "Unhandled application exception.");
                ApplicationNotifications.Show(Localization.GetSystemString("StartupUnhandledError"));
            }
            catch (Exception reportError)
            {
                System.Diagnostics.Trace.TraceError($"Exception reporting failed: {reportError}");
            }
        }

        private async Task StartBackgroundServicesAsync()
        {
            var dashboardStarted = false;
            try
            {
                var listenOnLan = false;
                try { listenOnLan = IsAdmin(); }
                catch (Exception ex) { logger.Warn(ex, "Cannot check elevation; listening on localhost only."); }

                _gameChatPollingService = new GameChatPollingService();
                try
                {
                    _localHttpServer = new LocalHttpServer(_gameChatPollingService, listenOnLan);
                    await _localHttpServer.StartAsync(_shutdownCts.Token);
                    dashboardStarted = true;
                    if (listenOnLan)
                    {
                        // Configure only the port Kestrel actually bound, without blocking startup.
                        _ = ConfigureFirewallAsync(_localHttpServer.Port);
                    }
                }
                catch (OperationCanceledException) when (_shutdownCts.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    logger.Error(ex, "Failed to start the local Dashboard server on the preferred or fallback port.");
                    ApplicationNotifications.Show(Localization.GetSystemString("StartupServiceUnavailable"));
                }

                try
                {
                    _gamePollingTask = _gameChatPollingService.RunAsync(_shutdownCts.Token);
                    _ = ObserveBackgroundTaskAsync(_gamePollingTask, "game chat polling");
                }
                catch (Exception ex)
                {
                    logger.Error(ex, "Game polling could not be started.");
                }
            }
            catch (OperationCanceledException) when (_shutdownCts.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Background service setup failed; keeping the tray application alive.");
                ApplicationNotifications.Show(Localization.GetSystemString("StartupServiceUnavailable"));
            }

            // Voice services and startup views are optional and do not depend on the Dashboard listener.
            try { InitializeQuickTranslationServices(); }
            catch (Exception ex) { logger.Error(ex, "Quick translation startup failed."); }
            try { OpenConfiguredStartupViews(dashboardStarted); }
            catch (Exception ex) { logger.Error(ex, "Could not open configured startup views."); }
        }

        private async Task ConfigureFirewallAsync(int port)
        {
            try
            {
                await Task.Run(() =>
                {
                    if (!IsPortAllowedInFirewall(port))
                    {
                        logger.Info("Port {0} is not in the firewall allow list. Adding rule...", port);
                        AddFirewallRule(port, GetFirewallRuleName(port));
                    }
                }, _shutdownCts.Token);
            }
            catch (OperationCanceledException) when (_shutdownCts.IsCancellationRequested) { }
            catch (Exception ex) { logger.Warn(ex, "Could not configure the optional LAN firewall rule."); }
        }

        private void OpenConfiguredStartupViews(bool dashboardStarted)
        {
            if (ApplicationConfig.GetBooleanSetting(ApplicationConfig.OpenOverlayOnStartupKey))
            {
                EnqueueOnUiThread(() => OverlayManager?.Show());
            }

            if (dashboardStarted && ApplicationConfig.GetBooleanSetting(ApplicationConfig.OpenDashboardOnStartupKey))
            {
                EnqueueOnUiThread(() => _ = OpenDashboardAsync());
            }
        }

        private void InitializeQuickTranslationServices()
        {
            try
            {
                _successAudioService = new SuccessAudioService();
                _quickTranslationService = new QuickTranslationService(_successAudioService);
                _globalHotkeyService = new GlobalHotkeyService();
                _globalHotkeyService.HotkeyPressed += OnQuickTranslationHotkeyPressed;
                _globalHotkeyService.RegistrationFailed += OnQuickTranslationHotkeyRegistrationFailed;
                _globalHotkeyService.Start();
                RefreshQuickTranslationHotkeys();

                if (QuickTranslationConfig.IsEnabled())
                {
                    _ = WarmUpQuickTranslationSpeechAsync();
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Failed to initialize quick voice translation services. The rest of the application will continue running.");
                try
                {
                    _globalHotkeyService?.Dispose();
                    _quickTranslationService?.Dispose();
                    _successAudioService?.Dispose();
                }
                catch
                {
                    // Best-effort cleanup after initialization failure.
                }

                _globalHotkeyService = null;
                _quickTranslationService = null;
                _successAudioService = null;
            }
        }

        private void OnQuickTranslationHotkeyPressed(WarThunderChatTranslator.Entities.QuickTranslationHotkey binding)
        {
            if (_quickTranslationHotkeysSuspended)
            {
                return;
            }

            EnqueueOnUiThread(() => TrackQuickTranslationTask(RunQuickTranslationAsync(binding)));
        }

        private void TrackQuickTranslationTask(Task task)
        {
            if (task == null)
            {
                return;
            }

            lock (_quickTranslationTasksLock)
            {
                _quickTranslationTasks.Add(task);
            }

            _ = RemoveQuickTranslationTaskWhenCompletedAsync(task);
        }

        private async Task RemoveQuickTranslationTaskWhenCompletedAsync(Task task)
        {
            try
            {
                await task;
            }
            catch
            {
                // RunQuickTranslationAsync already logs/handles pipeline failures.
            }
            finally
            {
                lock (_quickTranslationTasksLock)
                {
                    _quickTranslationTasks.Remove(task);
                }
            }
        }

        private async Task WaitForQuickTranslationTasksAsync(TimeSpan timeout)
        {
            Task[] tasks;
            lock (_quickTranslationTasksLock)
            {
                tasks = _quickTranslationTasks.Where(task => !task.IsCompleted).ToArray();
            }

            if (tasks.Length == 0)
            {
                return;
            }

            try
            {
                await Task.WhenAll(tasks).WaitAsync(timeout);
            }
            catch (TimeoutException)
            {
                logger.Warn("Timed out waiting for {0} quick translation task(s) during shutdown.", tasks.Length);
            }
            catch (OperationCanceledException)
            {
                // Expected when the application shutdown token cancels an active pipeline.
            }
            catch (Exception ex)
            {
                logger.Debug(ex, "A quick translation task ended with an error during shutdown.");
            }
        }

        private async Task RunQuickTranslationAsync(WarThunderChatTranslator.Entities.QuickTranslationHotkey binding)
        {
            var service = _quickTranslationService;
            if (service == null)
            {
                return;
            }

            try
            {
                await service.ExecuteAsync(binding, _shutdownCts.Token);
            }
            catch (OperationCanceledException) when (_shutdownCts.IsCancellationRequested)
            {
                // Normal shutdown while speech recognition or playback is in progress.
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Quick translation task terminated unexpectedly.");
            }
        }

        private void OnQuickTranslationHotkeyRegistrationFailed(WarThunderChatTranslator.Entities.QuickTranslationHotkey binding, string reason)
        {
            logger.Warn("Quick translation hotkey registration failed. Shortcut={0}, Target={1}, Reason={2}",
                binding.Shortcut, binding.TargetLanguage, reason);
        }

        internal void RefreshQuickTranslationHotkeys()
        {
            if (_globalHotkeyService == null)
            {
                return;
            }

            if (_quickTranslationHotkeysSuspended)
            {
                _globalHotkeyService.UpdateBindings(Array.Empty<WarThunderChatTranslator.Entities.QuickTranslationHotkey>());
                return;
            }

            var bindings = QuickTranslationConfig.IsEnabled()
                ? QuickTranslationConfig.GetHotkeys()
                : Array.Empty<WarThunderChatTranslator.Entities.QuickTranslationHotkey>();
            _globalHotkeyService.UpdateBindings(bindings);
        }

        internal void SuspendQuickTranslationHotkeys()
        {
            _quickTranslationHotkeysSuspended = true;
            _globalHotkeyService?.UpdateBindings(Array.Empty<WarThunderChatTranslator.Entities.QuickTranslationHotkey>());
        }

        internal void ResumeQuickTranslationHotkeys()
        {
            _quickTranslationHotkeysSuspended = false;
            RefreshQuickTranslationHotkeys();
        }

        internal SuccessAudioService QuickTranslationAudioService => _successAudioService;

        internal QuickTranslationService QuickTranslationService => _quickTranslationService;

        internal async Task WarmUpQuickTranslationSpeechAsync()
        {
            var service = _quickTranslationService;
            if (service == null || !QuickTranslationConfig.IsEnabled())
            {
                return;
            }

            try
            {
                logger.Debug("Preloading local quick speech recognition model. Model={0}", SpeechRecognitionService.ModelDisplayName);
                await service.WarmUpSpeechAsync(null, _shutdownCts.Token);
                logger.Info("Local quick speech model preload completed. State={0}, Model={1}", service.SpeechState, SpeechRecognitionService.ModelDisplayName);
            }
            catch (OperationCanceledException) when (_shutdownCts.IsCancellationRequested)
            {
                // Normal shutdown.
            }
            catch (Exception ex)
            {
                logger.Warn(ex, "Quick speech recognizer prewarm failed.");
            }
        }

        internal async Task ReinitializeQuickTranslationSpeechAsync()
        {
            var service = _quickTranslationService;
            if (service == null || !QuickTranslationConfig.IsEnabled())
            {
                return;
            }

            try
            {
                logger.Debug("Reloading local quick speech recognition model. Model={0}", SpeechRecognitionService.ModelDisplayName);
                await service.ReinitializeSpeechAsync(null, _shutdownCts.Token);
            }
            catch (OperationCanceledException) when (_shutdownCts.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
                logger.Warn(ex, "Quick speech recognizer reinitialization failed.");
            }
        }

        internal async Task ReleaseQuickTranslationSpeechAsync()
        {
            var service = _quickTranslationService;
            if (service == null)
            {
                return;
            }

            try
            {
                await service.ReleaseSpeechAsync(_shutdownCts.Token);
            }
            catch (OperationCanceledException) when (_shutdownCts.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
                logger.Debug(ex, "Could not release quick speech recognizer immediately.");
            }
        }

        private async Task ObserveBackgroundTaskAsync(Task task, string taskName)
        {
            try
            {
                await task;
            }
            catch (OperationCanceledException) when (_shutdownCts.IsCancellationRequested)
            {
                // Normal shutdown.
            }
            catch (Exception ex)
            {
                logger.Error(ex, $"Background task '{taskName}' terminated unexpectedly.");
            }
        }

        private static string GetFirewallRuleName(int port)
        {
            return $"WarThunderChatTranslator: Allow port {port}";
        }

        private static string GetLegacyFirewallRuleName(int port)
        {
            return $"WarThunderChatTranslator：允许端口 {port}";
        }

        private bool IsPortAllowedInFirewall(int port)
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "netsh",
                    Arguments = "advfirewall firewall show rule name=all",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8
                }
            };

            process.Start();
            string output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();

            return output.Contains(GetFirewallRuleName(port), StringComparison.Ordinal)
                || output.Contains(GetLegacyFirewallRuleName(port), StringComparison.Ordinal);
        }

        private void AddFirewallRule(int port, string ruleName)
        {
            var processStartInfo = new ProcessStartInfo
            {
                FileName = "netsh",
                Arguments = $"advfirewall firewall add rule name=\"{ruleName}\" protocol=TCP dir=in localport={port} action=allow description=\"Allows inbound TCP traffic on port {port}.\"",
                UseShellExecute = true,
                Verb = "runas",
                CreateNoWindow = true
            };

            try
            {
                using var process = Process.Start(processStartInfo);
                process.WaitForExit();
                logger.Info($"Firewall rule '{ruleName}' was added.");
            }
            catch (Exception ex)
            {
                logger.Warn(ex, $"Failed to add firewall rule '{ruleName}'.");
            }
        }

        private async Task StopBackgroundServicesAsync()
        {
            if (Interlocked.Exchange(ref _servicesStopped, 1) != 0)
            {
                return;
            }

            // Prevent any new quick-translation pipeline before cancellation begins.
            try
            {
                if (_globalHotkeyService != null)
                {
                    _globalHotkeyService.HotkeyPressed -= OnQuickTranslationHotkeyPressed;
                    _globalHotkeyService.RegistrationFailed -= OnQuickTranslationHotkeyRegistrationFailed;
                    _globalHotkeyService.Dispose();
                    _globalHotkeyService = null;
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Failed to stop the global hotkey service.");
            }

            _shutdownCts.Cancel();

            // Let any in-flight hotkey invocation observe cancellation and leave its normal
            // finally path before native audio/ASR resources are disposed. This prevents
            // a shutdown race between SenseVoice decode, WASAPI capture and service disposal.
            await WaitForQuickTranslationTasksAsync(TimeSpan.FromSeconds(5));

            var quickTranslationService = _quickTranslationService;
            _quickTranslationService = null;
            if (quickTranslationService != null)
            {
                try
                {
                    var disposeTask = quickTranslationService.DisposeAsync().AsTask();
                    var completed = await Task.WhenAny(disposeTask, Task.Delay(TimeSpan.FromSeconds(8), CancellationToken.None));
                    if (completed == disposeTask)
                    {
                        await disposeTask;
                    }
                    else
                    {
                        logger.Warn("Timed out disposing quick translation services during shutdown; continuing application exit.");
                    }
                }
                catch (OperationCanceledException)
                {
                    // Shutdown cleanup itself must never block process exit because of cancellation.
                }
                catch (Exception ex)
                {
                    logger.Debug(ex, "Quick translation services did not dispose cleanly during shutdown.");
                }
            }

            var successAudioService = _successAudioService;
            _successAudioService = null;
            try
            {
                successAudioService?.Dispose();
            }
            catch (Exception ex)
            {
                logger.Debug(ex, "Quick translation audio service did not dispose cleanly during shutdown.");
            }

            try
            {
                if (_localHttpServer != null)
                {
                    await _localHttpServer.DisposeAsync();
                    _localHttpServer = null;
                }
            }
            catch (OperationCanceledException)
            {
                // Expected if Kestrel observes the shutdown cancellation token.
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Failed to stop the local HTTP server.");
            }

            _gameChatPollingService?.Dispose();
            _gameChatPollingService = null;

            // Observe the polling task so cancellation is not left as an unobserved TaskCanceledException.
            if (_gamePollingTask != null)
            {
                try
                {
                    await _gamePollingTask;
                }
                catch (OperationCanceledException)
                {
                }
                catch (Exception ex)
                {
                    logger.Debug(ex, "Game chat polling task ended with an error during shutdown.");
                }
                finally
                {
                    _gamePollingTask = null;
                }
            }
        }

    }
}
