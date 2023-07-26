// Copyright (c) Microsoft Corporation and Contributors.
// Licensed under the MIT License.

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.UI.Xaml.Shapes;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using WarThunderChatTranslator.Configurations;
using Windows.ApplicationModel;
using Windows.ApplicationModel.Activation;
using Windows.Foundation;
using Windows.Foundation.Collections;
using WinUICommunity;
using static System.Net.Mime.MediaTypeNames;
using Newtonsoft.Json;
using Windows.UI;
using WarThunderChatTranslator.Pages;
using System.Diagnostics;
using NLog;
using Path = System.IO.Path;
using Microsoft.Windows.AppNotifications.Builder;
using Microsoft.Windows.AppNotifications;
using System.Threading.Tasks;
using Application = Microsoft.UI.Xaml.Application;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace WarThunderChatTranslator
{
    /// <summary>
    /// Provides application-specific behavior to supplement the default Application class.
    /// </summary>
    public partial class App : Microsoft.UI.Xaml.Application
    {
        /// <summary>
        /// Initializes the singleton application object.  This is the first line of authored code
        /// executed, and as such is the logical equivalent of main() or WinMain().
        /// </summary>
        public App()
        {
            this.InitializeComponent();
        }

        public NLog.Logger logger;
        public static ThemeManager themeManager { get; set; }

        /// <summary>
        /// Invoked when the application is launched.
        /// </summary>
        /// <param name="args">Details about the launch request and process.</param>
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

            m_window = new ChatWindow();

            themeManager = ThemeManager.Initialize(m_window, new ThemeOptions
            {
                BackdropType = BackdropType.DesktopAcrylic,
                ElementTheme = SettingsTheme,
                TitleBarCustomization = new TitleBarCustomization
                {
                    TitleBarType = TitleBarType.AppWindow
                }
            });
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
            if (ApplicationConfig.GetSettings("ChatWidth") == null)
            {
                ApplicationConfig.SaveSettings("ChatWidth", "575");
            }
            if (ApplicationConfig.GetSettings("ChatHeight") == null)
            {
                ApplicationConfig.SaveSettings("ChatHeight", "200");
            }
            if (ApplicationConfig.GetSettings("ChatStartUpLoactionX") == null)
            {
                ApplicationConfig.SaveSettings("ChatStartUpLoactionX", "5");
            }
            if (ApplicationConfig.GetSettings("ChatStartUpLoactionY") == null)
            {
                ApplicationConfig.SaveSettings("ChatStartUpLoactionY", "340");
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
            m_window.Activate();
        }

        private Window m_window;
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
