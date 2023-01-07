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
using WinUICommunity.Common.Extensions;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace WarThunderChatTranslator.Pages
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class APIPage : Page
    {
        public APIPage()
        {
            this.InitializeComponent();
        }

        private void Page_Loaded(object sender, RoutedEventArgs e)
        {
            switch(ApplicationConfig.GetSettings("TranslateAPI"))
            {
                case "Bing":
                    {
                        APIPanel.SelectedIndex = 0;
                        break;
                    }
                case "Baidu":
                    {
                        APIPanel.SelectedIndex = 1;
                        break;
                    }
                case "Youdao":
                    {
                        APIPanel.SelectedIndex = 2;
                        break;
                    }
            }
        }

        private void APIPanel_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ApplicationConfig.SaveSettings("TranslateAPI", ((ComboBoxItem)APIPanel.SelectedItem).Tag.ToString());
        }
    }
}
