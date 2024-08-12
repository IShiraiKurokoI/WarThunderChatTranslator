// Copyright (c) Microsoft Corporation and Contributors.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WarThunderChatTranslator.Configurations;
using GTranslate.Translators;
using System.Diagnostics;
using WarThunderChatTranslator.Dialogs;
using Windows.UI.Notifications;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace WarThunderChatTranslator.Pages
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class APIPage : Page
    {
        AggregateTranslator translator;

        public NLog.Logger logger;
        public APIPage()
        {
            this.InitializeComponent();
            logger = NLog.LogManager.GetCurrentClassLogger();
        }

        private void Page_Loaded(object sender, RoutedEventArgs e)
        {
            switch(ApplicationConfig.GetSettings("TranslateAPI"))
            {
                case "Microsoft":
                    {
                        APIPanel.SelectedIndex = 0;
                        translator = new AggregateTranslator((IReadOnlyCollection<ITranslator>)(object)new ITranslator[1] { new MicrosoftTranslator() });
                        App.translator = new AggregateTranslator((IReadOnlyCollection<ITranslator>)(object)new ITranslator[1] { new MicrosoftTranslator() });
                        break;
                    }
                case "Yandex":
                    {
                        APIPanel.SelectedIndex = 1;
                        translator = new AggregateTranslator((IReadOnlyCollection<ITranslator>)(object)new ITranslator[1] { new YandexTranslator()});
                        App.translator = new AggregateTranslator((IReadOnlyCollection<ITranslator>)(object)new ITranslator[1] { new YandexTranslator() });
                        break;
                    }
                case "Bing":
                    {
                        APIPanel.SelectedIndex = 2;
                        translator = new AggregateTranslator((IReadOnlyCollection<ITranslator>)(object)new ITranslator[1] { new BingTranslator() });
                        App.translator = new AggregateTranslator((IReadOnlyCollection<ITranslator>)(object)new ITranslator[1] { new BingTranslator() });
                        break;
                    }
                case "Google":
                    {
                        APIPanel.SelectedIndex = 3;
                        translator = new AggregateTranslator((IReadOnlyCollection<ITranslator>)(object)new ITranslator[1] { new GoogleTranslator2() });
                        App.translator = new AggregateTranslator((IReadOnlyCollection<ITranslator>)(object)new ITranslator[1] { new GoogleTranslator2() });
                        break;
                    }
            }
        }

        private void APIPanel_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ApplicationConfig.SaveSettings("TranslateAPI", ((ComboBoxItem)APIPanel.SelectedItem).Tag.ToString());
            Page_Loaded(null, null);
        }

        private async void Button_Click(object sender, RoutedEventArgs e)
        {
            InputDialog inputDialog = new InputDialog();
            inputDialog.XamlRoot = this.XamlRoot;
            inputDialog.Style = Microsoft.UI.Xaml.Application.Current.Resources["DefaultContentDialogStyle"] as Style;
            var result = await inputDialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                Checking.Visibility = Visibility.Visible;
                try
                {
                    var translationResult = await translator.TranslateAsync(inputDialog.text, "zh-CN");

                    // 构建Toast通知内容
                    var toastXml = ToastNotificationManager.GetTemplateContent(ToastTemplateType.ToastText04);
                    var toastVisualElements = toastXml.GetElementsByTagName("visual");
                    var appLogoOverlay = toastXml.CreateElement("appLogoOverlay");
                    appLogoOverlay.SetAttribute("src", "favicon.ico");
                    appLogoOverlay.SetAttribute("hint-crop", "circle");
                    var stringElements = toastXml.GetElementsByTagName("text");
                    stringElements[0].AppendChild(toastXml.CreateTextNode("翻译成功！"));
                    stringElements[1].AppendChild(toastXml.CreateTextNode("翻译结果：" + translationResult.Translation));
                    stringElements[2].AppendChild(toastXml.CreateTextNode("调用翻译器：" + translationResult.Service));

                    // 创建并显示通知
                    var toast = new ToastNotification(toastXml);
                    ToastNotificationManager.CreateToastNotifier("WarThunderChatTranslator").Show(toast);

                    Debug.WriteLine(translationResult.Service + translationResult.Translation);
                    logger.Debug($"翻译测试成功！翻译器：{translationResult.Service}, 翻译内容：{translationResult.Source}, 翻译结果：{translationResult.Translation}");
                }
                catch (Exception ex)
                {
                    // 构建Toast通知内容
                    var toastXml = ToastNotificationManager.GetTemplateContent(ToastTemplateType.ToastText02);
                    var stringElements = toastXml.GetElementsByTagName("text");
                    stringElements[0].AppendChild(toastXml.CreateTextNode("翻译失败！"));
                    stringElements[1].AppendChild(toastXml.CreateTextNode(ex.Message));

                    // 创建并显示通知
                    var toast = new ToastNotification(toastXml);
                    ToastNotificationManager.CreateToastNotifier("WarThunderChatTranslator").Show(toast);

                    logger.Debug($"翻译测试失败！翻译器：{translator.Name}, 翻译内容：{inputDialog.text}, 错误：{ex.Message}");
                }
                Checking.Visibility = Visibility.Collapsed;
            }
        }
    }
}
