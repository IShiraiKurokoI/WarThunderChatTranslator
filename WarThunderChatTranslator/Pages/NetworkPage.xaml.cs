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
    public sealed partial class NetworkPage : Page
    {
        public NetworkPage()
        {
            this.InitializeComponent();
        }

        private void RadioButton_Checked(object sender, RoutedEventArgs e)
        {
            ApplicationConfig.SaveSettings("NetworkProxyMode", ((RadioButton)sender).Tag.ToString());
        }

        private void Page_Loaded(object sender, RoutedEventArgs e)
        {
            ProxyPanel.Children.Cast<RadioButton>().FirstOrDefault((RadioButton c) => c?.Tag?.ToString() == ApplicationConfig.GetSettings("NetworkProxyMode"))!.IsChecked = true;
            ProxyAddress.Text = ApplicationConfig.GetSettings("ProxyAddress");
            ProxyPort.Text = ApplicationConfig.GetSettings("ProxyPort");
        }

        private void ProxyAddress_TextChanged(object sender, TextChangedEventArgs e)
        {
            ApplicationConfig.SaveSettings("ProxyAddress", ((TextBox)sender).Text);
        }

        private void ProxyPort_TextChanged(object sender, TextChangedEventArgs e)
        {
            ApplicationConfig.SaveSettings("ProxyPort", ((TextBox)sender).Text);
        }
    }
}
