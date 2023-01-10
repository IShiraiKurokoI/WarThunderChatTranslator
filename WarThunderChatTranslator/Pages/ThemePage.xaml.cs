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
using Microsoft.UI.Xaml.Markup;
using System.Globalization;
using Windows.UI;
using WarThunderChatTranslator.Dialogs;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace WarThunderChatTranslator.Pages
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class ThemePage : Page
    {
        public Color BackGroundColor;
        public Brush PreviewBrush;
        public ThemePage()
        {
            string ColorString = ApplicationConfig.GetSettings("BackGroundColor");
            BackGroundColor = FontPage.ToColor(ColorString);
            PreviewBrush = new SolidColorBrush(BackGroundColor);
            this.InitializeComponent();
        }

        private void ThemePanel_SelectionChanged(object sender, RoutedEventArgs e)
        {
            ApplicationConfig.SaveSettings("Theme",((ComboBoxItem)ThemePanel.SelectedItem).Tag.ToString());
            ThemeHelper.ComboBoxSelectionChanged(sender);
        }

        private void SettingsPageControl_Loaded(object sender, RoutedEventArgs e)
        {
            ThemeHelper.SetComboBoxDefaultItem(ThemePanel);
            switch(ApplicationConfig.GetSettings("BackgroundBrush"))
            {
                case "MicaAlt":
                    {
                        BackGroundPanel.SelectedIndex = 0;
                        break;
                    }
                case "Acrylic":
                    {
                        BackGroundPanel.SelectedIndex= 1;
                        break;
                    }
            }
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

        private async void Button_Click(object sender, RoutedEventArgs e)
        {
            ColorPickerDialog colorPickerDialog = new ColorPickerDialog(BackGroundColor);
            colorPickerDialog.XamlRoot = this.XamlRoot;
            colorPickerDialog.Style = Application.Current.Resources["DefaultContentDialogStyle"] as Style;
            var result = await colorPickerDialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                BackGroundColor = colorPickerDialog.pickerColor;
                ApplicationConfig.SaveSettings("BackGroundColor", BackGroundColor.ToString());
                PreviewBrush = new SolidColorBrush(BackGroundColor);
                ColorPreview.Fill = PreviewBrush;
                AcrylicPreview.TintColor = BackGroundColor;
            }
        }

        private void BackGroundPanel_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ApplicationConfig.SaveSettings("BackgroundBrush", ((ComboBoxItem)BackGroundPanel.SelectedItem).Tag.ToString());
        }
    }
}
