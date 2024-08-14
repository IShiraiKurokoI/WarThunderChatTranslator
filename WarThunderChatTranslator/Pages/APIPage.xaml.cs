using System;
using System.Collections.Generic;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WarThunderChatTranslator.Configurations;
using WarThunderChatTranslator.Dialogs;
using WarThunderChatTranslator.Helpers;
using GTranslate;
using GTranslate.Translators;
using NLog;
using Windows.UI.Notifications;
using System.Threading.Tasks;

namespace WarThunderChatTranslator.Pages
{
    public sealed partial class APIPage : Page
    {
        private readonly Logger _logger;
        private bool _loaded;

        public APIPage()
        {
            InitializeComponent();
            _logger = LogManager.GetCurrentClassLogger();
        }

        private void Page_Loaded(object sender, RoutedEventArgs e)
        {
            var selectedAPI = ApplicationConfig.GetSettings("TranslateAPI") ?? "Microsoft";
            InitializeAPIPanel(selectedAPI);
            LoadLanguages();
            UpdateLanguageSupport(selectedAPI);
            _loaded = true;
        }

        private void InitializeAPIPanel(String selectedAPI)
        {
            APIPanel.SelectedIndex = selectedAPI switch
            {
                "Yandex" => 1,
                "Bing" => 2,
                "Google" => 3,
                _ => 0
            };
        }

        private void LoadLanguages()
        {
            var languageDictionary = GTranslate.Language.LanguageDictionary;
            var savedLanguage = ApplicationConfig.GetSettings("TargetLanguage");

            foreach (var language in languageDictionary.Values)
            {
                var comboBoxItem = new ComboBoxItem
                {
                    Content = language.NativeName,
                    Tag = language,
                    IsSelected = language.ISO6391 == savedLanguage
                };
                TargetLanguage.Items.Add(comboBoxItem);
            }
        }

        private void APIPanel_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_loaded) return;

            var selectedTag = ((ComboBoxItem)APIPanel.SelectedItem)?.Tag?.ToString();
            if (string.IsNullOrEmpty(selectedTag)) return;

            ApplicationConfig.SaveSettings("TranslateAPI", selectedTag);
            TranslationHelper.UpdateTranslator();
            UpdateLanguageSupport(selectedTag);
        }

        private void UpdateLanguageSupport(string selectedTag)
        {
            foreach (ComboBoxItem item in TargetLanguage.Items)
            {
                if (item.Tag is Language language)
                {
                    item.IsEnabled = selectedTag switch
                    {
                        "Microsoft" => language.IsServiceSupported(TranslationServices.Microsoft),
                        "Yandex" => language.IsServiceSupported(TranslationServices.Yandex),
                        "Bing" => language.IsServiceSupported(TranslationServices.Bing),
                        "Google" => language.IsServiceSupported(TranslationServices.Google),
                        _ => true,
                    };
                }
            }
        }

        private async void Button_Click(object sender, RoutedEventArgs e)
        {
            var inputDialog = new InputDialog
            {
                XamlRoot = this.XamlRoot,
                Style = Application.Current.Resources["DefaultContentDialogStyle"] as Style
            };
            var result = await inputDialog.ShowAsync();

            if (result == ContentDialogResult.Primary)
            {
                Checking.Visibility = Visibility.Visible;
                await HandleTranslationTest(inputDialog.text);
                Checking.Visibility = Visibility.Collapsed;
            }
        }

        private async Task HandleTranslationTest(string text)
        {
            try
            {
                var translationResult = await TranslationHelper.TranslateAsync(text);
                ShowToastNotification("∑≠“Î≥…π¶£°", $"∑≠“ÎΩ·π˚£∫{translationResult.Translation}", $"µ˜”√∑≠“Î∆˜£∫{translationResult.Service}");
                _logger.Debug($"∑≠“Î≤‚ ‘≥…π¶£°∑≠“Î∆˜£∫{translationResult.Service}, ∑≠“Îƒ⁄»›£∫{translationResult.Source}, ∑≠“ÎΩ·π˚£∫{translationResult.Translation}");
            }
            catch (Exception ex)
            {
                ShowToastNotification("∑≠“Î ß∞‹£°", ex.Message);
                _logger.Debug($"∑≠“Î≤‚ ‘ ß∞‹£°∑≠“Î∆˜£∫{TranslationHelper.getCurrentTranslator().Name}, ∑≠“Îƒ⁄»›£∫{text}, ¥ÌŒÛ£∫{ex.Message}");
            }
        }

        private void ShowToastNotification(string title, string message, string subtitle = "")
        {
            var toastXml = ToastNotificationManager.GetTemplateContent(ToastTemplateType.ToastText04);
            var stringElements = toastXml.GetElementsByTagName("text");

            stringElements[0].AppendChild(toastXml.CreateTextNode(title));
            stringElements[1].AppendChild(toastXml.CreateTextNode(message));
            if (!string.IsNullOrEmpty(subtitle))
            {
                stringElements[2].AppendChild(toastXml.CreateTextNode(subtitle));
            }

            var toast = new ToastNotification(toastXml);
            ToastNotificationManager.CreateToastNotifier("WarThunderChatTranslator").Show(toast);
        }

        private void Bing_Token_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_loaded)
            {
                ApplicationConfig.SaveSettings("Bing_Token", ((TextBox)sender).Text);
            }
        }

        private void TargetLanguage_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loaded && TargetLanguage.SelectedItem is ComboBoxItem selectedItem && selectedItem.Tag is Language selectedLanguage)
            {
                ApplicationConfig.SaveSettings("TargetLanguage", selectedLanguage.ISO6391);
                _logger.Debug($"…Ë÷√ƒø±Í”Ô—‘Œ™{selectedLanguage.ISO6391}");
            }
        }
    }
}