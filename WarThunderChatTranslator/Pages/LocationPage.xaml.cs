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

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace WarThunderChatTranslator.Pages
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class LocationPage : Page
    {
        public LocationPage()
        {
            this.InitializeComponent();
        }

        private void Page_Loaded(object sender, RoutedEventArgs e)
        {
            ChatWidth.Text = ApplicationConfig.GetSettings("ChatWidth");
            ChatHeight.Text = ApplicationConfig.GetSettings("ChatHeight");
            ChatStartUpLoactionX.Text = ApplicationConfig.GetSettings("ChatStartUpLoactionX");
            ChatStartUpLoactionY.Text = ApplicationConfig.GetSettings("ChatStartUpLoactionY");
        }

        private void ChatWidth_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (((TextBox)sender).Text == "")
            {
                ApplicationConfig.SaveSettings("ChatWidth", "500");
                return;
            }
            ApplicationConfig.SaveSettings("ChatWidth", ((TextBox)sender).Text);
        }

        private void ChatHeight_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (((TextBox)sender).Text == "")
            {
                ApplicationConfig.SaveSettings("ChatHeight", "300");
                return;
            }
            ApplicationConfig.SaveSettings("ChatHeight", ((TextBox)sender).Text);
        }

        private void ChatStartUpLoactionX_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (((TextBox)sender).Text == "")
            {
                ApplicationConfig.SaveSettings("ChatStartUpLoactionX", "30");
                return;
            }
            ApplicationConfig.SaveSettings("ChatStartUpLoactionX", ((TextBox)sender).Text);
        }

        private void ChatStartUpLoactionY_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (((TextBox)sender).Text == "")
            {
                ApplicationConfig.SaveSettings("ChatStartUpLoactionY", "350");
                return;
            }
            ApplicationConfig.SaveSettings("ChatStartUpLoactionY", ((TextBox)sender).Text);
        }
    }
}
