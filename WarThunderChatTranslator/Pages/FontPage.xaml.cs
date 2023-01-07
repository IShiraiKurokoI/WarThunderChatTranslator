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
    public sealed partial class FontPage : Page
    {
        public FontPage()
        {
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
    }
}
