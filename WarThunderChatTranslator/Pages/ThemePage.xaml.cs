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
using Microsoft.UI.Xaml.Media.Animation;
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
        bool CssEditorExpanded = true;
        public ThemePage()
        {
            this.InitializeComponent();
        }
        private void Grid_Loaded(object sender, RoutedEventArgs e)
        {
            ThemePanel.SelectedIndex = (ApplicationConfig.GetSettings(ApplicationConfig.ThemeKey) ?? "Default") switch
            {
                "Light" => 0,
                "Dark" => 1,
                _ => 2,
            };
            BackgroundCSS.Text = ApplicationConfig.GetSettings(ApplicationConfig.BackgroundCssKey);
            ThemeInitilized = true;
            LanguagePanel.SelectedIndex = Localization.CurrentLanguage switch
            {
                StartupLanguage.SimplifiedChinese => 0,
                StartupLanguage.TraditionalChinese => 1,
                _ => 2
            };
            LanguageInitialized = true;
        }

        private void ThemePanel_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ThemeInitilized)
            {
                var theme = ((ComboBoxItem)ThemePanel.SelectedItem).Tag.ToString();
                ApplicationConfig.SaveSettings(ApplicationConfig.ThemeKey, theme);
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
                ApplicationConfig.SaveSettings(ApplicationConfig.BackgroundCssKey, BackgroundCSS.Text);
            }
        }

        private async void BackgroundCssInfo_Click(object sender, RoutedEventArgs e)
        {
            await Windows.System.Launcher.LaunchUriAsync(new Uri("https://www.w3schools.com/cssref/css3_pr_background.php"));
        }

        private void SettingsCard_Click(object sender, RoutedEventArgs e)
        {
            CssEditorExpanded = !CssEditorExpanded;
            CssEditorPanel.Visibility = CssEditorExpanded ? Visibility.Visible : Visibility.Collapsed;
            CssEditorChevron.Glyph = CssEditorExpanded ? "\uE70E" : "\uE70D";
        }

        private void LanguagePanel_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!LanguageInitialized || LanguagePanel.SelectedItem is not ComboBoxItem selectedItem)
            {
                return;
            }

            Localization.Apply(selectedItem.Tag.ToString());
        }
    }
}
