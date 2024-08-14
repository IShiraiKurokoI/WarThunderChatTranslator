using System;
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

        private void UpdateHttpClientWithUriValidation()
        {
            if (Uri.TryCreate(ApplicationConfig.GetSettings("ProxyAddress"), UriKind.Absolute, out _))
            {
                TranslationHelper.UpdateHttpClient();
            }
        }
    }
}
