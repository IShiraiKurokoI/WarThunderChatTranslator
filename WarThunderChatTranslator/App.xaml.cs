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
using Windows.UI.Notifications;
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
        public NLog.Logger logger;
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

        private static Mutex mutex; // Defines the process-wide mutex used for single-instance enforcement.

        public App()
        {
            // Create the mutex and detect whether another instance already owns the same name.
            bool isNewInstance;
            mutex = new Mutex(true, "WarThunderChatTranslator_Mutex", out isNewInstance);

            if (!isNewInstance)
            {
                var toastXml = ToastNotificationManager.GetTemplateContent(ToastTemplateType.ToastText01);
                toastXml.GetElementsByTagName("text")[0].AppendChild(toastXml.CreateTextNode("WarThunderChatTranslator 已在运行，请勿开启新实例。"));
                var toast = new ToastNotification(toastXml);
                ToastNotificationManager.CreateToastNotifier("WarThunderChatTranslator").Show(toast);
                Environment.Exit(0);
                return;
            }
            this.InitializeComponent();
            InitializeLogging();
            RegisterGlobalExceptionHandlers();
        }

        protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
        {
            _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
            InitializeAppSettings();
            OverlayManager = new OverlayWindowManager(() => _gameChatPollingService);
            InitializeTrayIcon();
            _ = StartBackgroundServicesAsync();
        }

        private void InitializeLogging()
        {
            logger = NLog.LogManager.GetCurrentClassLogger();
            logger.Info("--------Application started--------");
            DeleteOldLogs();
        }

        private void RegisterGlobalExceptionHandlers()
        {
            TaskScheduler.UnobservedTaskException += (sender, e) => HandleException(e.Exception);
            AppDomain.CurrentDomain.UnhandledException += (sender, e) => HandleException(e.ExceptionObject as Exception);
            App.Current.UnhandledException += (sender, e) => HandleException(e.Exception);
        }

        private void InitializeAppSettings()
        {
            // Initialize UI culture before creating localized default values such as
            // the quick-translation TTS prompt text.
            Localization.Initialize(ApplicationConfig.GetSettings("ApplicationLanguage"));

            var defaultSettings = new Dictionary<string, string>
            {
                { "NetworkProxyMode", "Default" },
                { "ProxyAddress", "" },
                { "ProxyAccount", "" },
                { "ProxyPassword", "" },
                { "LastUpdateCheckDate", "Never" },
                { "TranslateAPI", "Microsoft" },
                { "AiSelectedProviderId", "" },
                { "TargetLanguage", "zh-CN" },
                { "FontFamily", "Segoe UI" },
                { "FontSize", "14" },
                { "FontStyle", "normal" },
                { "AllyFontColor", "#FF5BC0DE" },
                { "EnemyFontColor", "#FFD9534F" },
                { "SystemFontColor", "#FF856404" },
                { "Theme", "Default" },
                { "BackgroundCSS", "background-color: #f4f4f4;" },
                { ApplicationConfig.GamePollingIntervalSecondsKey, ApplicationConfig.DefaultPollingIntervalSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture) },
                { ApplicationConfig.WebPollingIntervalSecondsKey, ApplicationConfig.DefaultPollingIntervalSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture) },
                { ApplicationConfig.OverlayDisplayModeKey, "translation" },
                { ApplicationConfig.OverlayShowSourceLanguageKey, "false" },
                { ApplicationConfig.OverlayOpacityPercentKey, "90" },
                { ApplicationConfig.OverlayWidthKey, OverlayWindow.DefaultWidth.ToString(System.Globalization.CultureInfo.InvariantCulture) },
                { ApplicationConfig.OverlayHeightKey, OverlayWindow.DefaultHeight.ToString(System.Globalization.CultureInfo.InvariantCulture) },
                { QuickTranslationConfig.EnabledKey, "false" },
                { QuickTranslationConfig.RecognitionLanguageKey, "" },
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
                if (ApplicationConfig.GetSettings(setting.Key) == null)
                {
                    ApplicationConfig.SaveSettings(setting.Key, setting.Value);
                }
            }

            if (ApplicationConfig.GetSettings(QuickTranslationConfig.HotkeysKey) == null)
            {
                QuickTranslationConfig.SaveHotkeys(QuickTranslationConfig.DefaultHotkeys);
            }

            logger.Info("Initializing translator.");
            TranslationHelper.init();
            logger.Info("Translator initialization completed.");
        }

        private void InitializeTrayIcon()
        {
            var openDashboardCommand = (XamlUICommand)Resources["OpenDashboardCommand"];
            openDashboardCommand.ExecuteRequested += (sender, args) =>
            {
                EnqueueOnUiThread(async () =>
                {
                    try
                    {
                        await Windows.System.Launcher.LaunchUriAsync(new Uri("http://localhost:8100"));
                    }
                    catch (Exception ex)
                    {
                        logger.Error(ex, "Failed to open the chat dashboard.");
                    }
                });
            };

            var openOverlayCommand = (XamlUICommand)Resources["OpenOverlayCommand"];
            openOverlayCommand.ExecuteRequested += (sender, args) => EnqueueOnUiThread(() => OverlayManager?.Toggle());

            var showHideWindowCommand = (XamlUICommand)Resources["ShowHideWindowCommand"];
            showHideWindowCommand.ExecuteRequested += (sender, args) => EnqueueOnUiThread(ToggleMainWindowVisibility);

            var exitApplicationCommand = (XamlUICommand)Resources["ExitApplicationCommand"];
            exitApplicationCommand.ExecuteRequested += (sender, args) => EnqueueOnUiThread(() => _ = ExitApplicationAsync());

            TrayIcon = (TaskbarIcon)Resources["TrayIcon"];
            UpdateTrayMenuWidth();
            TrayIcon.ForceCreate();
            Localization.CultureChanged += UpdateTrayMenuWidth;
        }

        private void EnqueueOnUiThread(Action action)
        {
            if (_dispatcherQueue == null || !_dispatcherQueue.TryEnqueue(() => action()))
            {
                logger.Warn("Failed to dispatch the tray command to the main UI thread.");
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
            m_window = new MainWindow();

            var theme = ApplicationConfig.GetSettings("Theme") ?? "Default";
            ElementTheme SettingsTheme = theme switch
            {
                "Light" => ElementTheme.Light,
                "Dark" => ElementTheme.Dark,
                _ => ElementTheme.Default,
            };

            ApplicationConfig.SaveSettings("Theme", theme);

            m_window.SystemBackdrop = new DesktopAcrylicBackdrop();
            ApplyTheme(SettingsTheme);

            CenterWindow(m_window);

            m_window.Closed += (sender, args) =>
            {
                if (HandleClosedEvents)
                {
                    args.Handled = true;
                    m_window.Hide();
                }
            };
            m_window.Show();
            m_window.Activate();
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
            logger.Error(ex, "Unhandled application exception.");

            var toastXml = ToastNotificationManager.GetTemplateContent(ToastTemplateType.ToastText01);
            toastXml.GetElementsByTagName("text")[0].AppendChild(toastXml.CreateTextNode(ex.Message + ex.StackTrace));
            var toast = new ToastNotification(toastXml);
            ToastNotificationManager.CreateToastNotifier("WarThunderChatTranslator").Show(toast);
        }

        private async Task StartBackgroundServicesAsync()
        {
            try
            {
                var listenOnLan = IsAdmin();
                if (listenOnLan && !IsPortAllowedInFirewall(8100))
                {
                    logger.Info("Port 8100 is not allowed by the firewall. Adding a rule...");
                    AddFirewallRule(8100, GetFirewallRuleName(8100));
                }

                _gameChatPollingService = new GameChatPollingService();
                _localHttpServer = new LocalHttpServer(_gameChatPollingService, listenOnLan);

                await _localHttpServer.StartAsync(_shutdownCts.Token);

                _gamePollingTask = _gameChatPollingService.RunAsync(_shutdownCts.Token);
                _ = ObserveBackgroundTaskAsync(_gamePollingTask, "game chat polling");

                InitializeQuickTranslationServices();
            }
            catch (OperationCanceledException) when (_shutdownCts.IsCancellationRequested)
            {
                // The application is shutting down.
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Failed to start the local HTTP server.");

                var toastXml = ToastNotificationManager.GetTemplateContent(ToastTemplateType.ToastText01);
                toastXml.GetElementsByTagName("text")[0].AppendChild(
                    toastXml.CreateTextNode("8100端口启动失败，请检查端口占用后再打开应用！"));
                var toast = new ToastNotification(toastXml);
                ToastNotificationManager.CreateToastNotifier("WarThunderChatTranslator").Show(toast);

                await ExitApplicationAsync();
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

            EnqueueOnUiThread(() => _ = RunQuickTranslationAsync(binding));
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
                var languageTag = ApplicationConfig.GetSettings(QuickTranslationConfig.RecognitionLanguageKey);
                logger.Debug("Prewarming quick speech recognizer. Language={0}",
                    string.IsNullOrWhiteSpace(languageTag) ? "system" : languageTag);
                await service.WarmUpSpeechAsync(languageTag, _shutdownCts.Token);
                logger.Info("Quick speech recognizer prewarm completed. State={0}", service.SpeechState);
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
                var languageTag = ApplicationConfig.GetSettings(QuickTranslationConfig.RecognitionLanguageKey);
                logger.Debug("Reinitializing quick speech recognizer. Language={0}",
                    string.IsNullOrWhiteSpace(languageTag) ? "system" : languageTag);
                await service.ReinitializeSpeechAsync(languageTag, _shutdownCts.Token);
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

            _shutdownCts.Cancel();

            // Stop Kestrel asynchronously so shutdown never blocks the UI thread.
            // The polling cancellation token interrupts delays and in-flight game HTTP requests.

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

            // Quick translation uses a continuous recognition session so a second press of the
            // same shortcut can stop recording and flush pending speech results. During shutdown,
            // cancellation stops the active session; avoid disposing shared audio objects while an
            // in-flight operation may still be unwinding.
            _quickTranslationService = null;
            _successAudioService = null;

            try
            {
                if (_localHttpServer != null)
                {
                    await _localHttpServer.DisposeAsync();
                    _localHttpServer = null;
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Failed to stop the local HTTP server.");
            }

            _gameChatPollingService?.Dispose();
            _gameChatPollingService = null;
        }
    }
}