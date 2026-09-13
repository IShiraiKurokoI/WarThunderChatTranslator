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
        bool LanguageInitialized = false;
        public ThemePage()
        {
            this.InitializeComponent();
        }
        private void Grid_Loaded(object sender, RoutedEventArgs e)
        {
            ThemePanel.SelectedIndex = (ApplicationConfig.GetSettings("Theme") ?? "Default") switch
            {
                "Light" => 0,
                "Dark" => 1,
                _ => 2,
            };
            BackgroundCSS.Text = ApplicationConfig.GetSettings("BackgroundCSS");
            ThemeInitilized = true;
            LanguagePanel.SelectedIndex = Localization.CurrentLanguage == "zh-CN" ? 0 : 1;
            LanguageInitialized = true;
        }

        private void ThemePanel_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ThemeInitilized)
            {
                var theme = ((ComboBoxItem)ThemePanel.SelectedItem).Tag.ToString();
                ApplicationConfig.SaveSettings("Theme", theme);
                App.ApplyTheme(theme switch
                {
                    "Light" => ElementTheme.Light,
                    "Dark" => ElementTheme.Dark,
                    _ => ElementTheme.Default,
                });
            }
        }

        private async void HyperlinkButton_Click(object sender, RoutedEventArgs e)
        {
            await Windows.System.Launcher.LaunchUriAsync(new Uri("ms-settings:colors"));
        }

        private void BackgroundCSS_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (ThemeInitilized)
            {
                ApplicationConfig.SaveSettings("BackgroundCSS", BackgroundCSS.Text);
            }
        }

        private void LanguagePanel_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!LanguageInitialized || LanguagePanel.SelectedItem is not ComboBoxItem selectedItem)
            {
                return;
            }

            Localization.Apply(selectedItem.Tag.ToString());
            App.ReloadMainWindow();
        }
    }
}
