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
using WinUICommunity.Common.Helpers;
using WarThunderChatTranslator.Configurations;
using Microsoft.UI;
using System.Drawing;
using Microsoft.UI.Xaml.Markup;
using System.Globalization;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace WarThunderChatTranslator.Pages
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class ThemePage : Page
    {
        public ThemePage()
        {
            this.InitializeComponent();
        }

        private void OnThemeRadioButtonChecked(object sender, RoutedEventArgs e)
        {
            ApplicationConfig.SaveSettings("Theme",((RadioButton)sender).Tag.ToString());
            ThemeHelper.OnThemeRadioButtonChecked(sender);
        }

        private void OnBackgroundRadioButtonChecked(object sender, RoutedEventArgs e)
        {
            ApplicationConfig.SaveSettings("BackgroundBrush",((RadioButton)sender).Tag.ToString());
        }

        private void SettingsPageControl_Loaded(object sender, RoutedEventArgs e)
        {
            ThemeHelper.SetThemeRadioButtonChecked(ThemePanel);
            BackGroundPanel.Children.Cast<RadioButton>().FirstOrDefault((RadioButton c) => c?.Tag?.ToString() == ApplicationConfig.GetSettings("BackgroundBrush"))!.IsChecked = true;
            OpacitySlider.Value = double.Parse(ApplicationConfig.GetSettings("BackgroundOpacity"));
            TintLuminosityOpacitySlider.Value = double.Parse(ApplicationConfig.GetSettings("BackgroundLuminosityOpacity"));
        }

        private void OpacitySlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            ApplicationConfig.SaveSettings("BackgroundOpacity", OpacitySlider.Value.ToString());
        }

        private void TintLuminosityOpacitySlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            ApplicationConfig.SaveSettings("BackgroundLuminosityOpacity", TintLuminosityOpacitySlider.Value.ToString());
        }

        private void HyperlinkButton_Click(object sender, RoutedEventArgs e)
        {
            Windows.System.Launcher.LaunchUriAsync(new Uri("ms-settings:colors"));
        }
    }
}
