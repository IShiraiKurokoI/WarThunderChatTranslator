// Copyright (c) Microsoft Corporation and Contributors.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using WinUICommunity;
using WarThunderChatTranslator.Configurations;
using WarThunderChatTranslator.Pages;
using WarThunderChatTranslator.Helpers;
using System.Diagnostics;
using System.Reflection;
using System.Reflection.Emit;
using NLog;
using System.Runtime.CompilerServices;
using System.Net.Http;
using System.Threading.Tasks;
using WarThunderChatTranslator.Entities;
using Microsoft.WindowsAppSDK.Runtime;
using static WinUICommunity.LanguageDictionary;
using System.Collections.ObjectModel;
using GTranslate.Translators;
using WarThunderChatTranslator.Dialogs;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace WarThunderChatTranslator
{
    /// <summary>
    /// An empty window that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class ChatWindow : Window
    {
        public NLog.Logger logger;
        private OverlappedPresenter _presenter;
        public static ThemeManager themeManager { get; private set; }
        public static ListView listBox;

        public ChatWindow()
        {
            logger = NLog.LogManager.GetCurrentClassLogger();
            this.InitializeComponent();
            this.Title = "实时聊天界面";
            SetStyle(null, null);
            ConfigurationUpdateHelper.CallChatUpdate += SetStyle;
            this.AppWindow.Changed += AppWindow_Changed;
            listBox = MessageList;
        }

        public static ListView getList()
        {
            return listBox;
        }

        private TEnum GetEnum<TEnum>(string text) where TEnum : struct
        {
            if (!typeof(TEnum).GetTypeInfo().IsEnum)
            {
                throw new InvalidOperationException("Generic parameter 'TEnum' must be an enum.");
            }
            return (TEnum)Enum.Parse(typeof(TEnum), text);
        }


        public void SetStyle(object sender, RoutedEventArgs args)
        {
            var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            WindowId windowId = Win32Interop.GetWindowIdFromWindow(hWnd);
            AppWindow appWindow = AppWindow.GetFromWindowId(windowId);
            if (appWindow == null)
            {
                return;
            }
            appWindow.SetIcon("Assets/favicon.ico");
            try
            {
                appWindow.Resize(new Windows.Graphics.SizeInt32(int.Parse(ApplicationConfig.GetSettings("ChatWidth")), int.Parse(ApplicationConfig.GetSettings("ChatHeight"))));
            }
            catch (Exception ex)
            {
                logger.Error(ex);
            }
            _presenter = appWindow.Presenter as OverlappedPresenter;
            _presenter.IsMinimizable = false;
            _presenter.IsMaximizable = false;
            _presenter.IsResizable = true;
            _presenter.IsAlwaysOnTop = true;
            Microsoft.UI.Windowing.DisplayArea displayArea = Microsoft.UI.Windowing.DisplayArea.GetFromWindowId(windowId, Microsoft.UI.Windowing.DisplayAreaFallback.Nearest);
            if (displayArea is not null)
            {
                try
                {
                    var CenteredPosition = appWindow.Position;
                    CenteredPosition.X = (int.Parse(ApplicationConfig.GetSettings("ChatStartUpLoactionX")));
                    CenteredPosition.Y = (int.Parse(ApplicationConfig.GetSettings("ChatStartUpLoactionY")));
                    appWindow.Move(CenteredPosition);
                }
                catch (Exception ex)
                {
                    logger.Error(ex);
                }
            }

            TitleBarHelper.Initialize(this, TitleTextBlock, AppTitleBar, LeftPaddingColumn, IconColumn, TitleColumn, LeftDragColumn, SearchColumn, RightDragColumn, RightPaddingColumn);
        }

        private MainWindow m_window;

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            if (ConfigurationUpdateHelper.MainWindow == null)
            {
                CreateSettingWindow();
            }
            else
            {
                if (ConfigurationUpdateHelper.MainWindow.AppWindow != null)
                {
                    WindowHelper.ActivateWindowAgain(ConfigurationUpdateHelper.MainWindow);
                }
                else
                {
                    CreateSettingWindow();
                }
            }

        }

        public void CreateSettingWindow()
        {
            m_window = new MainWindow();
            ElementTheme SettingsTheme = ElementTheme.Default;
            if (ApplicationConfig.GetSettings("Theme") == "Light")
            {
                SettingsTheme = ElementTheme.Light;
            }
            if (ApplicationConfig.GetSettings("Theme") == "Dark")
            {
                SettingsTheme = ElementTheme.Dark;
            }
            App.themeManager = ThemeManager.Initialize(m_window, new ThemeOptions
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
                    CenteredPosition.X = ((displayArea.WorkArea.Width - appWindow.Size.Width) / 2);
                    CenteredPosition.Y = ((displayArea.WorkArea.Height - appWindow.Size.Height) / 2);
                    appWindow.Move(CenteredPosition);
                }
            }

            ConfigurationUpdateHelper.MainWindow = m_window;
            m_window.Activate();
        }

        private void Window_Closed(object sender, WindowEventArgs args)
        {
            ConfigurationUpdateHelper.CallClose(null, null);
        }

        public class ChatMessageEqualityComparer : IEqualityComparer<ChatMessage>
        {
            public bool Equals(ChatMessage x, ChatMessage y)
            {
                if (x == null && y == null)
                    return true;
                if (x == null || y == null)
                    return false;

                return x.Id == y.Id;
            }

            public int GetHashCode(ChatMessage obj)
            {
                return obj.Id.GetHashCode();
            }
        }

        public HashSet<ChatMessage> chatMessages { get; set; } = new HashSet<ChatMessage>(new ChatMessageEqualityComparer());
        public ObservableCollection<ChatMessage> chatMessageslist { get; set; } = new ObservableCollection<ChatMessage>(); 

        private void MessageList_Loaded(object sender, RoutedEventArgs e)
        {
            
            var dispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
            Task.Run(async () =>
            {
                GetMsg(dispatcherQueue);
            });
        }

        AggregateTranslator translator = new AggregateTranslator((IReadOnlyCollection<ITranslator>)(object)new ITranslator[1] { new YandexTranslator() });

        async Task GetMsg(Microsoft.UI.Dispatching.DispatcherQueue dispatcherQueue)
        {
            while (true)
            {
                try
                {
                    int lastid = 0;
                    if (chatMessages.Count > 0)
                    {
                        lastid = chatMessages.Last().Id;
                    }
                    string apiUrl = "http://127.0.0.1:8111/gamechat?lastId=" + lastid;
                    using (var httpClient = new HttpClient())
                    {
                        HttpResponseMessage response = await httpClient.GetAsync(apiUrl);
                        if (response.IsSuccessStatusCode)
                        {
                            string json = await response.Content.ReadAsStringAsync();
                            dynamic data = Newtonsoft.Json.JsonConvert.DeserializeObject(json);

                            foreach (var item in data)
                            {
                                ChatMessage message = new ChatMessage
                                {
                                    Id = item.id,
                                    Sender = item.sender,
                                    Mode = item.mode,
                                    Msg = item.msg,
                                    Enemy = item.enemy,
                                    Time = item.time
                                };
                                if (!string.IsNullOrEmpty(message.Sender))
                                {
                                    dispatcherQueue.TryEnqueue(() =>
                                    {
                                        chatMessages.Add(message);
                                    });
                                }
                            }
                            foreach (var message in chatMessages)
                            {
                                if (string.IsNullOrEmpty(message.PrettyMessage))
                                {
                                    string PrettyMessage = FormatTimeToMinutesSeconds(message.Time);
                                    if (message.Enemy)
                                    {
                                        PrettyMessage += " [所有人] ";
                                    }
                                    else
                                    {
                                        PrettyMessage += " [友军] ";
                                    }
                                    PrettyMessage += message.Sender;
                                    PrettyMessage += ": ";
                                    var translationResult = await translator.TranslateAsync(message.Msg.Replace("\t", ""), "zh-CN");
                                    message.TranslatedMessage = translationResult.Translation;
                                    PrettyMessage += message.TranslatedMessage;
                                    dispatcherQueue.TryEnqueue(() =>
                                    {
                                        message.PrettyMessage = PrettyMessage;
                                    });
                                }
                            }
                            List<ChatMessage> list = chatMessages.ToList();
                            if ((list.Count > chatMessageslist.Count)&& (list.Count>0))
                            {
                                ObservableCollection<ChatMessage> collection = chatMessageslist.Clone();
                                for(int i = chatMessageslist.Count;i< list.Count; i++)
                                {
                                    collection.Add(list[i]);
                                }
                                dispatcherQueue.TryEnqueue(() =>
                                {
                                    chatMessageslist = collection;
                                    MessageList.ItemsSource = chatMessageslist;
                                });
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error: {ex.Message}");
                }

                await Task.Delay(500);
            }
        }

        static string FormatTimeToMinutesSeconds(int timeInSeconds)
        {
            TimeSpan timeSpan = TimeSpan.FromSeconds(timeInSeconds);
            return $"{timeSpan.Minutes:D2}:{timeSpan.Seconds:D2}";
        }

        private void AppWindow_Changed(AppWindow sender, AppWindowChangedEventArgs args)
        {
            ApplicationConfig.SaveSettings("ChatWidth", this.AppWindow.Size.Width.ToString());
            ApplicationConfig.SaveSettings("ChatHeight", this.AppWindow.Size.Height.ToString());
            ApplicationConfig.SaveSettings("ChatStartUpLoactionX", this.AppWindow.Position.X.ToString());
            ApplicationConfig.SaveSettings("ChatStartUpLoactionY", this.AppWindow.Position.Y.ToString());
            ConfigurationUpdateHelper.CallLocationUpdate(this, null);
            try
            {
                TitleBarHelper.Initialize(this, TitleTextBlock, AppTitleBar, LeftPaddingColumn, IconColumn, TitleColumn, LeftDragColumn, SearchColumn, RightDragColumn, RightPaddingColumn);
            }
            catch
            {

            }
        }
    }
}
