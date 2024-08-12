// Copyright (c) Microsoft Corporation and Contributors.
// Licensed under the MIT License.

using Microsoft.UI.Xaml;
using System;
using System.IO;
using WarThunderChatTranslator.Configurations;
using System.Threading.Tasks;
using Path = System.IO.Path;
using Application = Microsoft.UI.Xaml.Application;
using Microsoft.UI.Xaml.Input;
using H.NotifyIcon;
using WinUICommunity;
using Microsoft.UI;
using System.Net;
using System.Text;
using System.Net.Http;
using System.Diagnostics;
using GTranslate.Translators;
using System.Collections.Generic;
using Newtonsoft.Json;
using System.Linq;
using System.Security.Principal;
using Windows.UI.Notifications;
using Windows.ApplicationModel.Core;
using NLog;
using System.Text.RegularExpressions;
using WarThunderChatTranslator.Pages;

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
        public static IThemeService themeService { get; set; }

        public App()
        {
            this.InitializeComponent();
        }

        private Window m_window;
        public TaskbarIcon TrayIcon { get; private set; }

        protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
        {
            if (!IsAdmin())
            {
                Environment.Exit(0);
            }
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
                ApplicationConfig.SaveSettings("TranslateAPI", "Microsoft");
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

            //创建托盘图标
            var showHideWindowCommand = (XamlUICommand)Resources["ShowHideWindowCommand"];
            showHideWindowCommand.ExecuteRequested += ShowHideWindowCommand_ExecuteRequested;

            var exitApplicationCommand = (XamlUICommand)Resources["ExitApplicationCommand"];
            exitApplicationCommand.ExecuteRequested += ExitApplicationCommand_ExecuteRequested;

            TrayIcon = (TaskbarIcon)Resources["TrayIcon"];
            TrayIcon.ForceCreate();

            CoreApplication.Exiting += CoreApplication_Exiting;

            StartHttpServer();
        }

        private void CoreApplication_Exiting(object sender, object e)
        {
            HandleClosedEvents = false;
            OnClosed();
            TrayIcon?.Dispose();
            m_window?.Close();

            if (m_window == null)
            {
                Environment.Exit(0);
            }

            Application.Current.Exit();
            System.Environment.Exit(0);
        }

        public static bool IsAdmin()
        {
            var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
        public bool HandleClosedEvents { get; set; } = true;

        public static AggregateTranslator translator = new AggregateTranslator((IReadOnlyCollection<ITranslator>)(object)new ITranslator[1] { new MicrosoftTranslator() });

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
                themeService = new ThemeService();
                themeService.Initialize(m_window);
                themeService.ConfigBackdrop(BackdropType.AcrylicThin);
                themeService.ConfigElementTheme(SettingsTheme);
                themeService.ConfigTitleBar(new TitleBarCustomization
                {
                    TitleBarWindowType = TitleBarWindowType.AppWindow,
                    LightTitleBarButtons = new TitleBarButtons
                    {
                        ButtonBackgroundColor = Colors.Transparent
                    },
                    DarkTitleBarButtons = new TitleBarButtons
                    {
                        ButtonBackgroundColor = Colors.Transparent
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
                        CenteredPosition.X = ((displayArea.WorkArea.Width - appWindow.Size.Width) / 2);
                        CenteredPosition.Y = ((displayArea.WorkArea.Height - appWindow.Size.Height) / 2);
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


            switch (ApplicationConfig.GetSettings("TranslateAPI"))
            {
                case "Microsoft":
                    {
                        translator = new AggregateTranslator((IReadOnlyCollection<ITranslator>)(object)new ITranslator[1] { new MicrosoftTranslator() });
                        break;
                    }
                case "Yandex":
                    {
                        translator = new AggregateTranslator((IReadOnlyCollection<ITranslator>)(object)new ITranslator[1] { new YandexTranslator() });
                        break;
                    }
                case "Bing":
                    {
                        translator = new AggregateTranslator((IReadOnlyCollection<ITranslator>)(object)new ITranslator[1] { new BingTranslator() });
                        break;
                    }
                case "Google":
                    {
                        translator = new AggregateTranslator((IReadOnlyCollection<ITranslator>)(object)new ITranslator[1] { new GoogleTranslator2() });
                        break;
                    }
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
            OnClosed();
            TrayIcon?.Dispose();
            m_window?.Close();

            // https://github.com/HavenDV/H.NotifyIcon/issues/66
            if (m_window == null)
            {
                Environment.Exit(0);
            }

            Application.Current.Exit();
            System.Environment.Exit(0);
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
            logger.Error(ex.ToString());

            var toastXml = ToastNotificationManager.GetTemplateContent(ToastTemplateType.ToastText01);
            var stringElements = toastXml.GetElementsByTagName("text");
            stringElements[0].AppendChild(toastXml.CreateTextNode(ex.Message + ex.StackTrace));
            var toast = new ToastNotification(toastXml);
            ToastNotificationManager.CreateToastNotifier("WarThunderChatTranslator").Show(toast);
        }

        private HttpListener _httpListener;
        private Dictionary<int, string> translationCache = new Dictionary<int, string>();

        private async void StartHttpServer()
        {
            int port = 8100;
            string ruleName = $"WarThunderChatTranslator：允许端口 {port}";

            if (!IsPortAllowedInFirewall(port))
            {
                logger.Info($"端口 {port} 在防火墙中未被允许。正在添加规则...");
                AddFirewallRule(port, ruleName);
            }
            else
            {
                logger.Info($"端口 {port} 已经在防火墙中被允许。");
            }

            _httpListener = new HttpListener();

            // 监听特定端口和路由
            _httpListener.Prefixes.Add("http://+:8100/");

            _httpListener.Start();
            logger.Info("HTTP服务器已启动，正在监听 http://+:8100/");

            Windows.System.Launcher.LaunchUriAsync(new System.Uri("http://localhost:8100"));

            // 异步处理HTTP请求
            await Task.Run(() => HandleRequests());
        }

        private bool IsPortAllowedInFirewall(int port)
        {
            // 通过调用 netsh 查询是否已有该端口的规则
            Process process = new Process();
            process.StartInfo.FileName = "netsh";
            process.StartInfo.Arguments = $"advfirewall firewall show rule name=all";
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.CreateNoWindow = true;
            process.StartInfo.WindowStyle = ProcessWindowStyle.Hidden;

            process.StartInfo.StandardOutputEncoding = Encoding.UTF8;

            process.Start();
            string output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();

            // 检查输出中是否有对应端口的规则
            return output.Contains($"WarThunderChatTranslator：允许端口");
        }

        private void AddFirewallRule(int port, string ruleName)
        {
            ProcessStartInfo processStartInfo = new ProcessStartInfo();
            processStartInfo.FileName = "netsh";
            processStartInfo.Arguments = $"advfirewall firewall add rule name=\"{ruleName}\" protocol=TCP dir=in localport={port} action=allow description=\"此规则允许端口 {port} 的入站访问\"";
            processStartInfo.UseShellExecute = true; // 必须为 true 才能使用 Verb
            processStartInfo.Verb = "runas"; // 提升为管理员权限
            processStartInfo.CreateNoWindow = true;
            processStartInfo.WindowStyle = ProcessWindowStyle.Hidden;

            try
            {
                using (Process process = Process.Start(processStartInfo))
                {
                    process.WaitForExit();
                    logger.Info($"防火墙规则 '{ruleName}' 已添加。");
                }
            }
            catch (Exception ex)
            {
                logger.Info($"无法添加防火墙规则: {ex.Message}");
            }
        }

        static string COLOR_PATTERN = @"<color(.*?)>(.*?)<\/color>";

        private async Task HandleRequests()
        {
            while (_httpListener.IsListening)
            {
                var context = await _httpListener.GetContextAsync();
                var request = context.Request;
                var response = context.Response;

                try
                {
                    if (request.Url.AbsolutePath == "/gamechat")
                    {
                        // 获取请求参数lastId
                        string lastId = request.QueryString["lastId"] ?? "0";

                        // 转发请求到 http://127.0.0.1:8111/gamechat?lastId=
                        string targetUrl = $"http://127.0.0.1:8111/gamechat?lastId={lastId}";
                        string responseData = await ForwardRequestAsync(targetUrl);

                        // 处理返回的数据
                        var chatMessages = JsonConvert.DeserializeObject<List<WarThunderChatTranslator.Entities.ChatMessage>>(responseData);

                        // 使用 Task.WhenAll 并行处理翻译任务
                        var translationTasks = chatMessages.Select(async message =>
                        {
                            // 去掉 Msg 和 Mode 中的 \t
                            message.Msg = message.Msg.Replace("\t", "");
                            message.Mode = message.Mode.Replace("\t", "");

                            // 检查缓存中是否已有翻译
                            if (translationCache.ContainsKey(message.Id) && translationCache[message.Id] == message.Msg)
                            {
                                // 从缓存中获取翻译结果
                                message.TranslatedMessage = translationCache[message.Id];
                            }
                            else
                            {
                                try
                                {
                                    message.Msg = Regex.Replace(message.Msg, COLOR_PATTERN, match => match.Groups[2].Value);
                                    
                                    var translationResult = await translator.TranslateAsync(message.Msg, "zh-CN");
                                    var translatedMsg = translationResult.Translation;

                                    // 将翻译结果存储到缓存中
                                    translationCache[message.Id] = translatedMsg;

                                    // 更新消息的 TranslatedMessage 属性为翻译后的文本
                                    message.TranslatedMessage = translatedMsg;
                                }
                                catch
                                {
                                    // 翻译失败，保留原文并添加标记
                                    message.TranslatedMessage = "(翻译失败) " + message.Msg;
                                }
                            }
                            message.PrettyMessage = $"{message.Sender}: {message.TranslatedMessage}";
                        }).ToList();

                        // 等待所有翻译任务完成
                        await Task.WhenAll(translationTasks);

                        string processedData = JsonConvert.SerializeObject(chatMessages);

                        // 设置响应的编码和内容类型为UTF-8
                        response.ContentEncoding = Encoding.UTF8;
                        response.ContentType = "application/json; charset=utf-8";

                        // 将返回数据发送给客户端
                        byte[] buffer = Encoding.UTF8.GetBytes(processedData);
                        response.ContentLength64 = buffer.Length;
                        await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
                    }
                    else if (request.Url.AbsolutePath == "/dashboard")
                    {
                        // 返回 dashboard HTML 文件
                        string html = Properties.Resources.dashboard;
                        response.ContentEncoding = Encoding.UTF8;
                        response.ContentType = "text/html; charset=utf-8";
                        byte[] buffer = Encoding.UTF8.GetBytes(html);
                        response.ContentLength64 = buffer.Length;
                        await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
                    }
                    else if (request.Url.AbsolutePath == "/")
                    {
                        response.StatusCode = 302;
                        response.RedirectLocation = "/dashboard";
                        response.ContentEncoding = Encoding.UTF8;
                        response.ContentType = "text/html; charset=utf-8";
                        byte[] buffer = Encoding.UTF8.GetBytes("302");
                        response.ContentLength64 = buffer.Length;
                        await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
                    }
                    else
                    {
                        // 处理未找到的请求
                        response.StatusCode = (int)HttpStatusCode.NotFound;
                        byte[] buffer = Encoding.UTF8.GetBytes("404 Not Found");
                        await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
                    }
                }
                catch (Exception ex)
                {
                    logger.Error($"处理请求时发生错误: {ex.Message}");

                    // 返回错误信息给客户端
                    response.StatusCode = (int)HttpStatusCode.InternalServerError;
                    response.ContentEncoding = Encoding.UTF8;
                    response.ContentType = "text/html; charset=utf-8";
                    byte[] buffer = Encoding.UTF8.GetBytes("聊天数据请求失败");
                    await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
                }
                finally
                {
                    try
                    {
                        response.OutputStream.Close();
                    }
                    catch (Exception ex)
                    {

                    }
                }
            }
        }


        private async Task<string> ForwardRequestAsync(string url)
        {
            using (HttpClient client = new HttpClient())
            {
                var response = await client.GetAsync(url);
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadAsStringAsync();
            }
        }

        protected void OnClosed()
        {
            _httpListener.Stop();
            _httpListener.Close();
        }
    }
}
