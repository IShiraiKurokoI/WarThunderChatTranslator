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
using WarThunderChatTranslator.Configurations;
using WinUICommunity;
using GTranslate.Translators;
using static System.Net.Mime.MediaTypeNames;
using System.Diagnostics;
using WarThunderChatTranslator.Dialogs;
using Microsoft.Windows.AppNotifications.Builder;
using Microsoft.Windows.AppNotifications;
using GTranslate.Results;
using System.Collections.ObjectModel;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace WarThunderChatTranslator.Pages
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class APIPage : Page
    {
        AggregateTranslator translator = new AggregateTranslator((IReadOnlyCollection<ITranslator>)(object)new ITranslator[1] { new YandexTranslator() });
        public APIPage()
        {
            this.InitializeComponent();
        }

        private void Page_Loaded(object sender, RoutedEventArgs e)
        {
            switch(ApplicationConfig.GetSettings("TranslateAPI"))
            {
                case "Yandex":
                    {
                        APIPanel.SelectedIndex = 0;
                        break;
                    }
                case "Bing":
                    {
                        APIPanel.SelectedIndex = 1;
                        break;
                    }
                case "Google":
                    {
                        APIPanel.SelectedIndex = 2;
                        break;
                    }
                case "Microsoft":
                    {
                        APIPanel.SelectedIndex = 3;
                        break;
                    }
            }
        }

        private void APIPanel_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ApplicationConfig.SaveSettings("TranslateAPI", ((ComboBoxItem)APIPanel.SelectedItem).Tag.ToString());
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
                    var builder = new AppNotificationBuilder()
                        .AddText("∑≠“Î≥…π¶£°")
                        .AddText("∑≠“ÎΩ·π˚£∫"+translationResult.Translation)
                        .AddText("µ˜”√∑≠“Î∆˜£∫"+translationResult.Service);
                    var notificationManager = AppNotificationManager.Default;
                    notificationManager.Show(builder.BuildNotification());
                    ChatWindow.getList().Items.Add(translationResult.Translation);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine(ex);
                    var builder = new AppNotificationBuilder()
                        .AddText("∑≠“Î ß∞‹£°")
                        .AddText(ex.Message);
                    var notificationManager = AppNotificationManager.Default;
                    notificationManager.Show(builder.BuildNotification());
                }
                Checking.Visibility = Visibility.Collapsed;
            }
        }
    }
}
