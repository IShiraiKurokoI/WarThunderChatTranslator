using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using WarThunderChatTranslator.Configurations;
using WarThunderChatTranslator.Dialogs;
using WarThunderChatTranslator.Helpers;
using Windows.UI;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.UI.Xaml.Shapes;

namespace WarThunderChatTranslator.Pages
{
    public sealed partial class FontPage : Page
    {
        public Color FontColor { get; set; }
        public Brush AllyPreviewBrush { get; set; }
        public Brush EnemyPreviewBrush { get; set; }
        public Brush SystemPreviewBrush { get; set; }

        public List<Tuple<string, FontFamily>> Fonts { get; set; }

        public FontPage()
        {
            InitializeComponent();
            InitializeFontColors();
            LoadFontFamilies();
        }

        private void InitializeFontColors()
        {
            AllyPreviewBrush = new SolidColorBrush(GetFontColor("AllyFontColor"));
            EnemyPreviewBrush = new SolidColorBrush(GetFontColor("EnemyFontColor"));
            SystemPreviewBrush = new SolidColorBrush(GetFontColor("SystemFontColor"));
        }

        private Color GetFontColor(string settingKey)
        {
            string colorString = ApplicationConfig.GetSettings(settingKey);
            return ToColor(colorString);
        }

        private void Page_Loaded(object sender, RoutedEventArgs e)
        {
            FontSizePanel.Value = double.Parse(ApplicationConfig.GetSettings("FontSize"));
            FontStylePanel.SelectedIndex = GetFontStyleIndex(ApplicationConfig.GetSettings("FontStyle"));
        }

        private int GetFontStyleIndex(string fontStyle)
        {
            return fontStyle switch
            {
                "lighter" => 0,
                "normal" => 1,
                "bold" => 2,
                "bolder" => 2,
                _ => 1,
            };
        }

        public async Task LoadFontFamilies()
        {
            Fonts = FontHelper.GetFontFamilies()
                .Select(fontFamily => new Tuple<string, FontFamily>(fontFamily.Source, fontFamily))
                .ToList();

            string fontFamilySetting = ApplicationConfig.GetSettings("FontFamily");
            if (fontFamilySetting != null)
            {
                FontFamilyPanel.SelectedIndex = Fonts.FindIndex(f => f.Item1 == fontFamilySetting);
            }
        }

        private void FontFamilyPanel_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (FontFamilyPanel.SelectedValue is FontFamily selectedFontFamily)
            {
                ApplicationConfig.SaveSettings("FontFamily", selectedFontFamily.Source);
            }
        }

        private void FontSizePanel_TextChanged(object sender, NumberBoxValueChangedEventArgs e)
        {
            ApplicationConfig.SaveSettings("FontSize", FontSizePanel.Value.ToString());
        }

        private void FontStylePanel_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (FontStylePanel.SelectedItem is ComboBoxItem selectedItem)
            {
                ApplicationConfig.SaveSettings("FontStyle", selectedItem.Tag.ToString());
            }
        }

        public static Color ToColor(string color)
        {
            color = Regex.Replace(color.TrimStart('#').ToLower(), "[g-z]", "");
            int alpha = Convert.ToInt32(color.Substring(0, 2), 16);
            int red = Convert.ToInt32(color.Substring(2, 2), 16);
            int green = Convert.ToInt32(color.Substring(4, 2), 16);
            int blue = Convert.ToInt32(color.Substring(6, 2), 16);
            return Color.FromArgb((byte)alpha, (byte)red, (byte)green, (byte)blue);
        }

        private async void OnColorButtonClick(string settingKey, Brush previewBrush, Shape colorPreview)
        {
            var colorPickerDialog = new ColorPickerDialog(FontColor)
            {
                XamlRoot = this.XamlRoot,
                Style = Application.Current.Resources["DefaultContentDialogStyle"] as Style
            };
            var result = await colorPickerDialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                FontColor = colorPickerDialog.pickerColor;
                ApplicationConfig.SaveSettings(settingKey, FontColor.ToString());
                previewBrush = new SolidColorBrush(FontColor);
                colorPreview.Fill = previewBrush;
            }
        }

        private void Ally_Button_Click(object sender, RoutedEventArgs e)
        {
            OnColorButtonClick("AllyFontColor", AllyPreviewBrush, AllyColorPreview);
            AllyPreviewBrush = new SolidColorBrush(FontColor); // Update the reference after the async operation
        }

        private void Enemy_Button_Click(object sender, RoutedEventArgs e)
        {
            OnColorButtonClick("EnemyFontColor", EnemyPreviewBrush, EnemyColorPreview);
            EnemyPreviewBrush = new SolidColorBrush(FontColor); // Update the reference after the async operation
        }

        private void System_Button_Click(object sender, RoutedEventArgs e)
        {
            OnColorButtonClick("SystemFontColor", SystemPreviewBrush, SystemColorPreview);
            SystemPreviewBrush = new SolidColorBrush(FontColor); // Update the reference after the async operation
        }

    }
}