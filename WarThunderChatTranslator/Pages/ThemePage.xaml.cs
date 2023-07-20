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
using WinUICommunity;
using WarThunderChatTranslator.Configurations;
using Microsoft.UI;
using Microsoft.UI.Xaml.Markup;
using System.Globalization;
using Windows.UI;
using WarThunderChatTranslator.Dialogs;
using WarThunderChatTranslator.Helpers;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace WarThunderChatTranslator.Pages
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class ThemePage : Page
    {
        bool ThemeInitilized = false;
        public ThemePage()
        {
            this.InitializeComponent();
        }

        private void ThemePanel_SelectionChanged(object sender, RoutedEventArgs e)
        {
            if(ThemeInitilized)
            {
                ApplicationConfig.SaveSettings("Theme", ((ComboBoxItem)ThemePanel.SelectedItem).Tag.ToString());
                App.themeManager.OnThemeComboBoxSelectionChanged(sender);
                ConfigurationUpdateHelper.CallUpdate(this, null);
            }
        }

        private void SettingsPageControl_Loaded(object sender, RoutedEventArgs e)
        {
            App.themeManager.SetThemeComboBoxDefaultItem(ThemePanel);
            ThemeInitilized=true;
        }

        private void HyperlinkButton_Click(object sender, RoutedEventArgs e)
        {
            Windows.System.Launcher.LaunchUriAsync(new Uri("ms-settings:colors"));
        }
    }
}
