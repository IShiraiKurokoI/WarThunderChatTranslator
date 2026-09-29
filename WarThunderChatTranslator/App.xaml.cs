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
        private Task _gamePollingTask;
        private DispatcherQueue _dispatcherQueue;
        private int _servicesStopped;
        private int _exitStarted;

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
            InitializeTrayIcon();
            _ = StartBackgroundServicesAsync();
        }

        private void InitializeLogging()
        {
            logger = NLog.LogManager.GetCurrentClassLogger();
            logger.Info("--------程序启动--------");
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
            var defaultSettings = new Dictionary<string, string>
            {
                { "NetworkProxyMode", "Default" },
                { "ProxyAddress", "" },
                { "ProxyAccount", "" },
                { "ProxyPassword", "" },
                { "LastUpdateCheckDate", "Never" },
                { "TranslateAPI", "Microsoft" },
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
            };

            foreach (var setting in defaultSettings)
            {
                if (ApplicationConfig.GetSettings(setting.Key) == null)
                {
                    ApplicationConfig.SaveSettings(setting.Key, setting.Value);
                }
            }

            Localization.Initialize(ApplicationConfig.GetSettings("ApplicationLanguage"));

            logger.Info("初始化翻译器对象");
            TranslationHelper.init();
            logger.Info("翻译器对象初始化完成");
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
                        logger.Error(ex, "打开聊天面板失败");
                    }
                });
            };

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
                logger.Warn("无法将托盘命令调度到主 UI 线程");
            }
        }

        private void UpdateTrayMenuWidth()
        {
            if (TrayIcon?.ContextFlyout is not MenuFlyout menuFlyout)
            {
                return;
            }

            var menuKeys = new[] { "TrayDashboard", "TraySettings", "TrayExit" };
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
                logger.Error(ex, "停止后台服务时发生错误");
            }

            try
            {
                Localization.CultureChanged -= UpdateTrayMenuWidth;
                TrayIcon?.Dispose();
                TrayIcon = null;
            }
            catch (Exception ex)
            {
                logger.Error(ex, "释放托盘图标时发生错误");
            }

            try
            {
                m_window?.Close();
                m_window = null;
            }
            catch (Exception ex)
            {
                logger.Error(ex, "关闭主窗口时发生错误");
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
                        logger.Info("删除过期日志: " + Path.GetFileName(logFile));
                    }
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex.ToString());
            }
        }

        private void HandleException(Exception ex)
        {
            logger.Error(ex.ToString());

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
                    logger.Info("端口 8100 在防火墙中未被允许。正在添加规则...");
                    AddFirewallRule(8100, "WarThunderChatTranslator：允许端口 8100");
                }

                _gameChatPollingService = new GameChatPollingService();
                _localHttpServer = new LocalHttpServer(_gameChatPollingService, listenOnLan);

                await _localHttpServer.StartAsync(_shutdownCts.Token);

                _gamePollingTask = _gameChatPollingService.RunAsync(_shutdownCts.Token);
                _ = ObserveBackgroundTaskAsync(_gamePollingTask, "游戏聊天轮询");
            }
            catch (OperationCanceledException) when (_shutdownCts.IsCancellationRequested)
            {
                // The application is shutting down.
            }
            catch (Exception ex)
            {
                logger.Error(ex, "启动本地 HTTP 服务失败");

                var toastXml = ToastNotificationManager.GetTemplateContent(ToastTemplateType.ToastText01);
                toastXml.GetElementsByTagName("text")[0].AppendChild(
                    toastXml.CreateTextNode("8100端口启动失败，请检查端口占用后再打开应用！"));
                var toast = new ToastNotification(toastXml);
                ToastNotificationManager.CreateToastNotifier("WarThunderChatTranslator").Show(toast);

                await ExitApplicationAsync();
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
                logger.Error(ex, $"后台任务 {taskName} 异常退出");
            }
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

            return output.Contains($"WarThunderChatTranslator：允许端口 {port}");
        }

        private void AddFirewallRule(int port, string ruleName)
        {
            var processStartInfo = new ProcessStartInfo
            {
                FileName = "netsh",
                Arguments = $"advfirewall firewall add rule name=\"{ruleName}\" protocol=TCP dir=in localport={port} action=allow description=\"此规则允许端口 {port} 的入站访问\"",
                UseShellExecute = true,
                Verb = "runas",
                CreateNoWindow = true
            };

            try
            {
                using var process = Process.Start(processStartInfo);
                process.WaitForExit();
                logger.Info($"防火墙规则 '{ruleName}' 已添加。");
            }
            catch (Exception ex)
            {
                logger.Info($"无法添加防火墙规则: {ex.Message}");
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
                if (_localHttpServer != null)
                {
                    await _localHttpServer.DisposeAsync();
                    _localHttpServer = null;
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, "停止本地 HTTP 服务失败");
            }

            _gameChatPollingService?.Dispose();
            _gameChatPollingService = null;
        }
    }
}