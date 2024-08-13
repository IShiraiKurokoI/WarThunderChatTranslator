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
using WarThunderChatTranslator.Helpers;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace WarThunderChatTranslator.Pages
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class NetworkPage : Page
    {
        bool loaded = false;
        public NetworkPage()
        {
            this.InitializeComponent();
        }

        private void Page_Loaded(object sender, RoutedEventArgs e)
        {
            ProxyPanel.Children.Cast<RadioButton>().FirstOrDefault((RadioButton c) => c?.Tag?.ToString() == ApplicationConfig.GetSettings("NetworkProxyMode"))!.IsChecked = true;
            ProxyAddress.Text = ApplicationConfig.GetSettings("ProxyAddress");
            ProxyAccount.Text = ApplicationConfig.GetSettings("ProxyAccount");
            ProxyPassword.Text = ApplicationConfig.GetSettings("ProxyPassword");
            loaded = true;
        }

        private void RadioButton_Checked(object sender, RoutedEventArgs e)
        {
            if (!loaded)
            {
                return;
            }
            ApplicationConfig.SaveSettings("NetworkProxyMode", ((RadioButton)sender).Tag.ToString());
            if (((RadioButton)sender).Tag.ToString() != "Custom")
            {
                TranslationHelper.UpdateHttpClient();
            }
            else
            {
                try
                {
                    Uri uri = new Uri(ApplicationConfig.GetSettings("ProxyAddress"));
                    TranslationHelper.UpdateHttpClient();
                }
                catch (Exception)
                {

                }
            }
        }

        private void ProxyAddress_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!loaded)
            {
                return;
            }
            ApplicationConfig.SaveSettings("ProxyAddress", ((TextBox)sender).Text);
            try
            {
                Uri uri = new Uri(((TextBox)sender).Text);
                TranslationHelper.UpdateHttpClient();
            }
            catch (Exception)
            {

            }
        }

        private void ProxyAccount_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!loaded)
            {
                return;
            }
            ApplicationConfig.SaveSettings("ProxyAccount", ((TextBox)sender).Text);
            try
            {
                Uri uri = new Uri(ApplicationConfig.GetSettings("ProxyAddress"));
                TranslationHelper.UpdateHttpClient();
            }
            catch (Exception)
            {

            }
        }

        private void ProxyPassword_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!loaded)
            {
                return;
            }
            ApplicationConfig.SaveSettings("ProxyPassword", ((TextBox)sender).Text);
            try
            {
                Uri uri = new Uri(ApplicationConfig.GetSettings("ProxyAddress"));
                TranslationHelper.UpdateHttpClient();
            }
            catch (Exception)
            {

            }
        }
    }
}
