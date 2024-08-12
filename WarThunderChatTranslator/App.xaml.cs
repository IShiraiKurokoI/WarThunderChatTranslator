// Copyright (c) Microsoft Corporation and Contributors.
// Licensed under the MIT License.

using Microsoft.UI.Xaml;
using System;
using System.IO;
using WarThunderChatTranslator.Configurations;
using WinUICommunity;
using Microsoft.Windows.AppNotifications.Builder;
using Microsoft.Windows.AppNotifications;
using System.Threading.Tasks;
using Path = System.IO.Path;
using Application = Microsoft.UI.Xaml.Application;
using Microsoft.UI.Xaml.Input;
using H.NotifyIcon;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace WarThunderChatTranslator
{
    /// <summary>
    /// Provides application-specific behavior to supplement the default Application class.
    /// </summary>
    public partial class App : Microsoft.UI.Xaml.Application
    {
        public NLog.Logger logger;
        public static ThemeManager themeManager { get; set; }

        public App()
        {
            this.InitializeComponent();
        }

        private Window m_window;
        public TaskbarIcon TrayIcon { get; private set; }

        protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
        {
            //初始化日志记录
            logger = NLog.LogManager.GetCurrentClassLogger();
            logger.Info("--------程序启动--------");
            logger.Info("日志记录初始化成功");
            DeleteLog();
            //注册全局异常捕获
            System.Threading.Tasks.TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
            App.Current.UnhandledException += App_UnhandledException;
            Application.Current.UnhandledException += App_UnhandledException;
            //初始化应用设置
            if (ApplicationConfig.GetSettings("NetworkProxyMode") == null)
            {
                ApplicationConfig.SaveSettings("NetworkProxyMode", "Default");
            }
            if (ApplicationConfig.GetSettings("ProxyAddress") == null)
            {
                ApplicationConfig.SaveSettings("ProxyAddress", "");
            }
            if (ApplicationConfig.GetSettings("ProxyPort") == null)
            {
                ApplicationConfig.SaveSettings("ProxyPort", "");
            }
            if (ApplicationConfig.GetSettings("LastUpdateCheckDate") == null)
            {
                ApplicationConfig.SaveSettings("LastUpdateCheckDate", "从未");
            }
            if (ApplicationConfig.GetSettings("TranslateAPI") == null)
            {
                ApplicationConfig.SaveSettings("TranslateAPI", "Yandex");
            }
            if (ApplicationConfig.GetSettings("FontSize") == null)
            {
                ApplicationConfig.SaveSettings("FontSize", "14");
            }
            if (ApplicationConfig.GetSettings("FontStyle") == null)
            {
                ApplicationConfig.SaveSettings("FontStyle", "Normal");
            }
            if (ApplicationConfig.GetSettings("FontColor") == null)
            {
                ApplicationConfig.SaveSettings("FontColor", "#FF000000");
            }
            //
            var showHideWindowCommand = (XamlUICommand)Resources["ShowHideWindowCommand"];
            showHideWindowCommand.ExecuteRequested += ShowHideWindowCommand_ExecuteRequested;

            var exitApplicationCommand = (XamlUICommand)Resources["ExitApplicationCommand"];
            exitApplicationCommand.ExecuteRequested += ExitApplicationCommand_ExecuteRequested;

            TrayIcon = (TaskbarIcon)Resources["TrayIcon"];
            TrayIcon.ForceCreate();
        }
        public bool HandleClosedEvents { get; set; } = true;

        private void ShowHideWindowCommand_ExecuteRequested(XamlUICommand sender, ExecuteRequestedEventArgs args)
        {
            if (m_window == null)
            {
                //初始化设置窗口
                m_window = new MainWindow();
                //初始化主题设置
                ElementTheme SettingsTheme = ElementTheme.Default;
                if (ApplicationConfig.GetSettings("Theme") != null)
                {
                    if (ApplicationConfig.GetSettings("Theme") == "Light")
                    {
                        SettingsTheme = ElementTheme.Light;
                    }
                    if (ApplicationConfig.GetSettings("Theme") == "Dark")
                    {
                        SettingsTheme = ElementTheme.Dark;
                    }
                }
                else
                {
                    ApplicationConfig.SaveSettings("Theme", "Default");
                }
                themeManager = ThemeManager.Initialize(m_window, new ThemeOptions
                {
                    BackdropType = BackdropType.DesktopAcrylic,
                    ElementTheme = SettingsTheme,
                    TitleBarCustomization = new TitleBarCustomization
                    {
                        TitleBarType = TitleBarType.AppWindow
                    }
                });
                var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(m_window);
                Microsoft.UI.WindowId windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hWnd);
                Microsoft.UI.Windowing.AppWindow appWindow = Microsoft.UI.Windowing.AppWindow.GetFromWindowId(windowId);
                if (appWindow is not null)
                {
                    Microsoft.UI.Windowing.DisplayArea displayArea = Microsoft.UI.Windowing.DisplayArea.GetFromWindowId(windowId, Microsoft.UI.Windowing.DisplayAreaFallback.Nearest);
                    if (displayArea is not null)
                    {
                        var CenteredPosition = appWindow.Position;
                        CenteredPosition.X = (int.Parse(ApplicationConfig.GetSettings("ChatStartUpLoactionX")));
                        CenteredPosition.Y = (int.Parse(ApplicationConfig.GetSettings("ChatStartUpLoactionY")));
                        appWindow.Move(CenteredPosition);
                    }
                }
                m_window.Closed += (sender, args) =>
                {
                    if (HandleClosedEvents)
                    {
                        args.Handled = true;
                        m_window.Hide();
                    }
                };
                m_window.Show();
                return;
            }

            if (m_window.Visible)
            {
                m_window.Hide();
            }
            else
            {
                m_window.Show();
            }
        }

        private void ExitApplicationCommand_ExecuteRequested(XamlUICommand sender, ExecuteRequestedEventArgs args)
        {
            HandleClosedEvents = false;
            TrayIcon?.Dispose();
            m_window?.Close();

            // https://github.com/HavenDV/H.NotifyIcon/issues/66
            if (m_window == null)
            {
                Environment.Exit(0);
            }
        }

        public void DeleteLog()
        {
            try
            {
                string logDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments) + "\\WTChatTranslator\\Log";
                string logFilePrefix = "Log-WTChatTranslator-";
                int daysThreshold = 3;
                DateTime deletionDate = DateTime.Now.AddDays(-daysThreshold);
                string[] logFiles = Directory.GetFiles(logDirectory, logFilePrefix + "*.log");

                foreach (string logFile in logFiles)
                {
                    string fileName = Path.GetFileName(logFile);
                    string dateString = fileName.Substring(logFilePrefix.Length, 10);
                    DateTime logDate;

                    if (DateTime.TryParseExact(dateString, "yyyy-MM-dd", null, System.Globalization.DateTimeStyles.None, out logDate))
                    {
                        if (logDate <= deletionDate)
                        {
                            File.Delete(logFile);
                            logger.Info("删除过期日志: " + fileName);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex.ToString());
            }
        }

        private void App_UnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
        {
            // 处理未处理的异常
            HandleException(e.Exception);
            // 将事件标记为已处理，以防止应用程序崩溃
            e.Handled = true;
        }

        private void TaskScheduler_UnobservedTaskException(object sender, UnobservedTaskExceptionEventArgs e)
        {
            try
            {
                if (e.Exception is Exception exception)
                {
                    HandleException(exception);
                }
            }
            catch (Exception ex)
            {
                HandleException(ex);
            }
            finally
            {
                e.SetObserved();
            }
        }

        //非UI线程未捕获异常处理事件(例如自己创建的一个子线程)
        private void CurrentDomain_UnhandledException(object sender, System.UnhandledExceptionEventArgs e)
        {
            try
            {
                if (e.ExceptionObject is Exception exception)
                {
                    HandleException(exception);
                }
            }
            catch (Exception ex)
            {
                HandleException(ex);
            }
        }

        //日志记录
        private void HandleException(Exception ex)
        {
            var builder = new AppNotificationBuilder()
                .AddText(ex.Message + ex.StackTrace);
            var notificationManager = AppNotificationManager.Default;
            notificationManager.Show(builder.BuildNotification());
            //记录日志
            logger.Error(ex.ToString());
        }
    }
}
