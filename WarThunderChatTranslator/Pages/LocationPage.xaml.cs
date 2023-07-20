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
    public sealed partial class LocationPage : Page
    {
        public bool SettingsInitialized = false;
        public bool modifying = false;
        public LocationPage()
        {
            this.InitializeComponent();
            ConfigurationUpdateHelper.CallLocationSettingUpdate += ConfigurationUpdateHelper_CallLocationSettingUpdate;
        }

        private void ConfigurationUpdateHelper_CallLocationSettingUpdate(object sender, RoutedEventArgs e)
        {
            if(ChatWidth == null)
            {
                return;
            }
            if(modifying)
            {
                return;
            }
            setvalue();
        }

        public void setvalue()
        {
            SettingsInitialized = false;
            ChatWidth.Text = ApplicationConfig.GetSettings("ChatWidth");
            ChatHeight.Text = ApplicationConfig.GetSettings("ChatHeight");
            ChatStartUpLoactionX.Text = ApplicationConfig.GetSettings("ChatStartUpLoactionX");
            ChatStartUpLoactionY.Text = ApplicationConfig.GetSettings("ChatStartUpLoactionY");
            SettingsInitialized = true;
        }

        private void Page_Loaded(object sender, RoutedEventArgs e)
        {
            setvalue();
        }

        private void ChatWidth_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (SettingsInitialized)
            {
                modifying = true;
                if (((TextBox)sender).Text == "")
                {
                    ApplicationConfig.SaveSettings("ChatWidth", "500");
                    return;
                }
                ApplicationConfig.SaveSettings("ChatWidth", ((TextBox)sender).Text);
                ConfigurationUpdateHelper.CallUpdate(null,null);
                modifying = false;
            }
        }

        private void ChatHeight_TextChanged(object sender, TextChangedEventArgs e)
        {

            if (SettingsInitialized)
            {
                modifying = true;
                if (((TextBox)sender).Text == "")
                {
                    ApplicationConfig.SaveSettings("ChatHeight", "300");
                    return;
                }
                ApplicationConfig.SaveSettings("ChatHeight", ((TextBox)sender).Text);
                ConfigurationUpdateHelper.CallUpdate(null, null);
                modifying = false;
            }
        }

        private void ChatStartUpLoactionX_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (SettingsInitialized)
            {
                modifying = true;
                if (((TextBox)sender).Text == "")
                {
                    ApplicationConfig.SaveSettings("ChatStartUpLoactionX", "30");
                    return;
                }
                ApplicationConfig.SaveSettings("ChatStartUpLoactionX", ((TextBox)sender).Text);
                ConfigurationUpdateHelper.CallUpdate(null, null);
                modifying = false;
            }
        }

        private void ChatStartUpLoactionY_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (SettingsInitialized)
            {
                modifying = true;
                if (((TextBox)sender).Text == "")
                {
                    ApplicationConfig.SaveSettings("ChatStartUpLoactionY", "350");
                    return;
                }
                ApplicationConfig.SaveSettings("ChatStartUpLoactionY", ((TextBox)sender).Text);
                ConfigurationUpdateHelper.CallUpdate(null, null);
                modifying = false;
            }
        }
    }
}
