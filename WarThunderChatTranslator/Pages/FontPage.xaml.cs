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
using WarThunderChatTranslator.Dialogs;
using System.Diagnostics;
using Windows.UI;
using System.Text.RegularExpressions;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace WarThunderChatTranslator.Pages
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class FontPage : Page
    {
        public Color FontColor;
        public Brush PreviewBrush;

        public FontPage()
        {
            string ColorString = ApplicationConfig.GetSettings("FontColor");
            FontColor = ToColor(ColorString);
            PreviewBrush = new SolidColorBrush(FontColor);
            this.InitializeComponent();
        }

        private void FontStylePanel_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ApplicationConfig.SaveSettings("FontStyle", ((ComboBoxItem)FontStylePanel.SelectedItem).Tag.ToString());
        }

        private void FontSizePanel_TextChanged(object sender, TextChangedEventArgs e)
        {
            if(FontSizePanel.Text =="")
            {
                ApplicationConfig.SaveSettings("FontSize", "14");
            }
            else
            {
                ApplicationConfig.SaveSettings("FontSize", FontSizePanel.Text);
            }
        }

        private void Page_Loaded(object sender, RoutedEventArgs e)
        {
            FontSizePanel.Text = ApplicationConfig.GetSettings("FontSize");
            switch (ApplicationConfig.GetSettings("FontStyle"))
            {
                case "Normal":
                    {
                        FontStylePanel.SelectedIndex = 0;
                        break;
                    }
                case "Bold":
                    {
                        FontStylePanel.SelectedIndex = 1;
                        break;
                    }
            }
        }

        public static Color ToColor(string color)
        {
            int Alpha, Red, Green,Blue= 0;
            char[] rgb;
            color = color.TrimStart('#');
            color = Regex.Replace(color.ToLower(), "[g-zG-Z]", "");
            rgb = color.ToCharArray();
            Alpha = Convert.ToInt32(rgb[0].ToString() + rgb[1].ToString(), 16);
            Red = Convert.ToInt32(rgb[2].ToString() + rgb[3].ToString(), 16);
            Green = Convert.ToInt32(rgb[4].ToString() + rgb[5].ToString(), 16);
            Blue = Convert.ToInt32(rgb[6].ToString() + rgb[7].ToString(), 16);
            return Color.FromArgb((byte)Alpha, (byte)Red, (byte)Green, (byte)Blue);
        }

        private async void Button_Click(object sender, RoutedEventArgs e)
        {
            ColorPickerDialog colorPickerDialog = new ColorPickerDialog(FontColor);
            colorPickerDialog.XamlRoot = this.XamlRoot;
            colorPickerDialog.Style = Application.Current.Resources["DefaultContentDialogStyle"] as Style;
            var result = await colorPickerDialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                FontColor = colorPickerDialog.pickerColor;
                ApplicationConfig.SaveSettings("FontColor",FontColor.ToString());
                PreviewBrush = new SolidColorBrush(FontColor);
                ColorPreview.Fill = PreviewBrush;
            }
        }
    }
}
