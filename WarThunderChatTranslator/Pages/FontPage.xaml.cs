using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using WarThunderChatTranslator.Configurations;
using WarThunderChatTranslator.Dialogs;
using WarThunderChatTranslator.Helpers;
using Windows.UI;

namespace WarThunderChatTranslator.Pages
{
    public sealed partial class FontPage : Page
    {
        private bool _settingsInitialized;

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
            AllyPreviewBrush = new SolidColorBrush(GetFontColor("AllyFontColor", "#FF5BC0DE"));
            EnemyPreviewBrush = new SolidColorBrush(GetFontColor("EnemyFontColor", "#FFD9534F"));
            SystemPreviewBrush = new SolidColorBrush(GetFontColor("SystemFontColor", "#FF856404"));
        }

        private static Color GetFontColor(string settingKey, string fallback)
        {
            var colorString = ApplicationConfig.GetSettings(settingKey);
            return TryToColor(colorString, out var color)
                ? color
                : ToColor(fallback);
        }

        private void Page_Loaded(object sender, RoutedEventArgs e)
        {
            _settingsInitialized = false;

            var rawFontSize = ApplicationConfig.GetSettings("FontSize");
            if (!double.TryParse(rawFontSize, NumberStyles.Float, CultureInfo.InvariantCulture, out var fontSize) &&
                !double.TryParse(rawFontSize, NumberStyles.Float, CultureInfo.CurrentCulture, out fontSize))
            {
                fontSize = 14;
            }

            FontSizePanel.Value = Math.Clamp(fontSize, 1, 200);
            FontStylePanel.SelectedIndex = GetFontStyleIndex(ApplicationConfig.GetSettings("FontStyle"));
            _settingsInitialized = true;
        }

        private static int GetFontStyleIndex(string fontStyle)
        {
            return fontStyle switch
            {
                "lighter" => 0,
                "normal" => 1,
                "bold" => 2,
                "bolder" => 3,
                _ => 1,
            };
        }

        public void LoadFontFamilies()
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
            if (_settingsInitialized && FontFamilyPanel.SelectedValue is FontFamily selectedFontFamily)
            {
                ApplicationConfig.SaveSettings("FontFamily", selectedFontFamily.Source);
            }
        }

        private void FontSizePanel_TextChanged(object sender, NumberBoxValueChangedEventArgs e)
        {
            if (!_settingsInitialized || double.IsNaN(e.NewValue) || double.IsInfinity(e.NewValue))
            {
                return;
            }

            var fontSize = Math.Clamp(e.NewValue, 1, 200);
            ApplicationConfig.SaveSettings("FontSize", fontSize.ToString("0.##", CultureInfo.InvariantCulture));
        }

        private void FontStylePanel_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_settingsInitialized && FontStylePanel.SelectedItem is ComboBoxItem selectedItem)
            {
                ApplicationConfig.SaveSettings("FontStyle", selectedItem.Tag?.ToString() ?? "normal");
            }
        }

        public static Color ToColor(string color)
        {
            if (TryToColor(color, out var parsed))
            {
                return parsed;
            }

            throw new FormatException($"Invalid ARGB color value: {color}");
        }

        private static bool TryToColor(string color, out Color parsed)
        {
            parsed = default;
            if (string.IsNullOrWhiteSpace(color))
            {
                return false;
            }

            var normalized = color.Trim();
            if (normalized.StartsWith('#'))
            {
                normalized = normalized[1..];
            }

            if (normalized.Length != 8 || !uint.TryParse(normalized, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var argb))
            {
                return false;
            }

            parsed = Color.FromArgb(
                (byte)(argb >> 24),
                (byte)(argb >> 16),
                (byte)(argb >> 8),
                (byte)argb);
            return true;
        }

        private async Task SelectColorAsync(string settingKey, Shape colorPreview, string fallback)
        {
            var currentColor = GetFontColor(settingKey, fallback);
            var colorPickerDialog = new ColorPickerDialog(currentColor)
            {
                XamlRoot = XamlRoot,
                Style = Application.Current.Resources["DefaultContentDialogStyle"] as Style
            };

            var result = await colorPickerDialog.ShowAsync();
            if (result != ContentDialogResult.Primary)
            {
                return;
            }

            var selectedColor = colorPickerDialog.pickerColor;
            ApplicationConfig.SaveSettings(settingKey, selectedColor.ToString());
            colorPreview.Fill = new SolidColorBrush(selectedColor);
        }

        private async void Ally_Button_Click(object sender, RoutedEventArgs e)
        {
            await SelectColorAsync("AllyFontColor", AllyColorPreview, "#FF5BC0DE");
        }

        private async void Enemy_Button_Click(object sender, RoutedEventArgs e)
        {
            await SelectColorAsync("EnemyFontColor", EnemyColorPreview, "#FFD9534F");
        }

        private async void System_Button_Click(object sender, RoutedEventArgs e)
        {
            await SelectColorAsync("SystemFontColor", SystemColorPreview, "#FF856404");
        }
    }
}
