using System;
using System.Globalization;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WarThunderChatTranslator.Configurations;
using WarThunderChatTranslator.Helpers;

namespace WarThunderChatTranslator.Pages
{
    public sealed partial class NetworkPage : Page
    {
        private bool _loaded;

        public NetworkPage()
        {
            InitializeComponent();
        }

        private void Page_Loaded(object sender, RoutedEventArgs e)
        {
            LoadSettings();
            _loaded = true;
        }

        private void LoadSettings()
        {
            var selectedRadioButton = ProxyPanel.Children
                .OfType<RadioButton>()
                .FirstOrDefault(rb => rb?.Tag?.ToString() == ApplicationConfig.GetSettings("NetworkProxyMode"));

            if (selectedRadioButton != null)
            {
                selectedRadioButton.IsChecked = true;
            }

            ProxyAddress.Text = ApplicationConfig.GetSettings("ProxyAddress");
            ProxyAccount.Text = ApplicationConfig.GetSettings("ProxyAccount");
            ProxyPassword.Text = ApplicationConfig.GetSettings("ProxyPassword");

            GamePollingInterval.Value = ApplicationConfig.GetPollingIntervalSeconds(ApplicationConfig.GamePollingIntervalSecondsKey);
            WebPollingInterval.Value = ApplicationConfig.GetPollingIntervalSeconds(ApplicationConfig.WebPollingIntervalSecondsKey);
        }

        private void RadioButton_Checked(object sender, RoutedEventArgs e)
        {
            if (!_loaded) return;

            var selectedMode = ((RadioButton)sender).Tag?.ToString();
            ApplicationConfig.SaveSettings("NetworkProxyMode", selectedMode);

            if (selectedMode == "Custom")
            {
                UpdateHttpClientWithUriValidation();
            }
            else
            {
                TranslationHelper.UpdateHttpClient();
            }
        }

        private void ProxyAddress_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!_loaded) return;

            ApplicationConfig.SaveSettings("ProxyAddress", ((TextBox)sender).Text);
            UpdateHttpClientWithUriValidation();
        }

        private void ProxyAccount_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!_loaded) return;

            ApplicationConfig.SaveSettings("ProxyAccount", ((TextBox)sender).Text);
            UpdateHttpClientWithUriValidation();
        }

        private void ProxyPassword_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!_loaded) return;

            ApplicationConfig.SaveSettings("ProxyPassword", ((TextBox)sender).Text);
            UpdateHttpClientWithUriValidation();
        }


        private void GamePollingInterval_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
        {
            SavePollingInterval(sender, args.NewValue, ApplicationConfig.GamePollingIntervalSecondsKey);
        }

        private void WebPollingInterval_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
        {
            SavePollingInterval(sender, args.NewValue, ApplicationConfig.WebPollingIntervalSecondsKey);
        }

        private void SavePollingInterval(NumberBox numberBox, double newValue, string settingKey)
        {
            if (!_loaded) return;

            if (double.IsNaN(newValue))
            {
                numberBox.Value = ApplicationConfig.GetPollingIntervalSeconds(settingKey);
                return;
            }

            var seconds = Math.Clamp(
                (int)Math.Round(newValue, MidpointRounding.AwayFromZero),
                ApplicationConfig.MinPollingIntervalSeconds,
                ApplicationConfig.MaxPollingIntervalSeconds);

            if (Math.Abs(numberBox.Value - seconds) > double.Epsilon)
            {
                numberBox.Value = seconds;
            }

            ApplicationConfig.SaveSettings(settingKey, seconds.ToString(CultureInfo.InvariantCulture));
        }

        private void UpdateHttpClientWithUriValidation()
        {
            if (Uri.TryCreate(ApplicationConfig.GetSettings("ProxyAddress"), UriKind.Absolute, out _))
            {
                TranslationHelper.UpdateHttpClient();
            }
        }
    }
}
